package codexappserver

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"math"
	"net"
	"net/http"
	"os/exec"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
	log "github.com/sirupsen/logrus"
	"github.com/tidwall/gjson"
	"github.com/tidwall/sjson"
)

const childShutdownGracePeriod = 5 * time.Second

// RootProxyHandler mounts a websocket-only Codex app-server wrapper on CPA's root path.
// It exists to let `codex --remote ws://host:port` talk to a child `codex app-server`
// without modifying the upstream CLI source.
type RootProxyHandler struct {
	cfgProvider func() *config.Config
	authManager *coreauth.Manager
	upgrader    websocket.Upgrader
}

// NewRootProxyHandler builds a root websocket proxy handler that reads the latest CPA config
// on each request.
func NewRootProxyHandler(cfgProvider func() *config.Config, authManager *coreauth.Manager) *RootProxyHandler {
	return &RootProxyHandler{
		cfgProvider: cfgProvider,
		authManager: authManager,
		upgrader: websocket.Upgrader{
			ReadBufferSize:  4096,
			WriteBufferSize: 4096,
			CheckOrigin: func(_ *http.Request) bool {
				return true
			},
		},
	}
}

// Handle upgrades root websocket requests when the wrapper is enabled.
// It returns true when the request was consumed, even if the result was an error response.
func (h *RootProxyHandler) Handle(c *gin.Context) bool {
	if h == nil || c == nil || c.Request == nil {
		return false
	}
	cfg := h.currentConfig()
	if cfg == nil || !cfg.CodexAppServerProxy.Enable || !websocket.IsWebSocketUpgrade(c.Request) {
		return false
	}
	if cfg.CodexAppServerProxy.RestrictToLocalhost && !requestIsLoopback(c.Request) {
		c.AbortWithStatus(http.StatusForbidden)
		return true
	}

	upgraded, err := h.serve(c.Writer, c.Request, cfg.CodexAppServerProxy)
	if err != nil {
		entry := log.WithError(err)
		if upgraded {
			entry.Debug("codex app-server proxy session ended")
		} else {
			entry.Warn("codex app-server proxy start failed")
		}
	}
	c.Abort()
	return true
}

func (h *RootProxyHandler) currentConfig() *config.Config {
	if h == nil || h.cfgProvider == nil {
		return nil
	}
	return h.cfgProvider()
}

func (h *RootProxyHandler) serve(
	w http.ResponseWriter,
	r *http.Request,
	cfg config.CodexAppServerProxy,
) (bool, error) {
	binName := cfg.CodexBin
	if binName == "" {
		binName = "codex"
	}
	resolvedBin, err := exec.LookPath(binName)
	if err != nil {
		writeProxyError(w, http.StatusBadGateway, "codex_app_server_proxy_spawn_failed", "failed to resolve codex binary")
		return false, err
	}

	cmd := exec.Command(resolvedBin, "app-server", "--listen", "stdio://")
	stdin, err := cmd.StdinPipe()
	if err != nil {
		writeProxyError(w, http.StatusBadGateway, "codex_app_server_proxy_spawn_failed", "failed to open codex stdin")
		return false, err
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		_ = stdin.Close()
		writeProxyError(w, http.StatusBadGateway, "codex_app_server_proxy_spawn_failed", "failed to open codex stdout")
		return false, err
	}
	stderr, err := cmd.StderrPipe()
	if err != nil {
		_ = stdin.Close()
		writeProxyError(w, http.StatusBadGateway, "codex_app_server_proxy_spawn_failed", "failed to open codex stderr")
		return false, err
	}
	if err := cmd.Start(); err != nil {
		_ = stdin.Close()
		writeProxyError(w, http.StatusBadGateway, "codex_app_server_proxy_spawn_failed", "failed to start codex app-server")
		return false, err
	}

	conn, err := h.upgrader.Upgrade(w, r, nil)
	if err != nil {
		_ = stdin.Close()
		waitForChildExit(cmd)
		return false, err
	}

	session := &proxySession{
		conn:             conn,
		cmd:              cmd,
		stdin:            stdin,
		stdout:           bufio.NewReader(stdout),
		stderr:           stderr,
		accountLabel:     strings.TrimSpace(cfg.AccountLabel),
		hideAccountEmail: cfg.HideAccountEmail,
		usePoolPlanType:  cfg.UsePoolPlanType,
		authManager:      h.authManager,
		pendingMethods:   make(map[string]string),
	}
	return true, session.run()
}

type proxySession struct {
	conn *websocket.Conn
	cmd  *exec.Cmd

	stdin  io.WriteCloser
	stdout *bufio.Reader
	stderr io.ReadCloser

	accountLabel     string
	hideAccountEmail bool
	usePoolPlanType  bool
	authManager      *coreauth.Manager

	mu             sync.Mutex
	pendingMethods map[string]string

	closeOnce sync.Once
}

func (s *proxySession) run() error {
	go s.drainStderr()

	errCh := make(chan error, 2)
	go func() { errCh <- s.proxyClientToChild() }()
	go func() { errCh <- s.proxyChildToClient() }()

	err := <-errCh
	s.shutdown(err)
	return normalizeSessionError(err)
}

func (s *proxySession) proxyClientToChild() error {
	for {
		messageType, payload, err := s.conn.ReadMessage()
		if err != nil {
			return err
		}
		switch messageType {
		case websocket.TextMessage:
			payload = bytes.TrimRight(payload, "\r\n")
			s.trackClientRequest(payload)
			if _, err := s.stdin.Write(append(payload, '\n')); err != nil {
				return err
			}
		case websocket.BinaryMessage, websocket.PingMessage, websocket.PongMessage:
			continue
		case websocket.CloseMessage:
			return nil
		default:
			continue
		}
	}
}

func (s *proxySession) proxyChildToClient() error {
	for {
		line, err := s.stdout.ReadBytes('\n')
		if err != nil {
			if errors.Is(err, io.EOF) && len(bytes.TrimSpace(line)) == 0 {
				return nil
			}
			if len(bytes.TrimSpace(line)) == 0 {
				return err
			}
		}

		payload := bytes.TrimRight(line, "\r\n")
		if len(bytes.TrimSpace(payload)) == 0 {
			if err != nil {
				return err
			}
			continue
		}

		rewritten := s.rewriteServerPayload(payload)
		if writeErr := s.conn.WriteMessage(websocket.TextMessage, rewritten); writeErr != nil {
			return writeErr
		}
		if err != nil {
			return err
		}
	}
}

func (s *proxySession) drainStderr() {
	if s == nil || s.stderr == nil {
		return
	}
	scanner := bufio.NewScanner(s.stderr)
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}
		log.Debugf("codex app-server proxy stderr: %s", line)
	}
	if err := scanner.Err(); err != nil && !errors.Is(err, io.EOF) {
		log.WithError(err).Debug("codex app-server proxy stderr read failed")
	}
}

func (s *proxySession) shutdown(reason error) {
	s.closeOnce.Do(func() {
		closeCode := websocket.CloseNormalClosure
		closeText := ""
		if reason != nil && !isNormalSessionError(reason) {
			closeCode = websocket.CloseInternalServerErr
			closeText = "codex app-server proxy stopped"
		}
		_ = s.conn.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(closeCode, closeText), time.Now().Add(time.Second))
		_ = s.conn.Close()
		if s.stdin != nil {
			_ = s.stdin.Close()
		}
		waitForChildExit(s.cmd)
	})
}

func waitForChildExit(cmd *exec.Cmd) {
	if cmd == nil {
		return
	}
	done := make(chan struct{})
	go func() {
		_ = cmd.Wait()
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(childShutdownGracePeriod):
		if cmd.Process != nil {
			_ = cmd.Process.Kill()
		}
		<-done
	}
}

func (s *proxySession) trackClientRequest(payload []byte) {
	method := strings.TrimSpace(gjson.GetBytes(payload, "method").String())
	if method == "" {
		return
	}
	idKey := requestIDKey(payload)
	if idKey == "" {
		return
	}
	s.mu.Lock()
	s.pendingMethods[idKey] = method
	s.mu.Unlock()
}

func (s *proxySession) rewriteServerPayload(payload []byte) []byte {
	if method := strings.TrimSpace(gjson.GetBytes(payload, "method").String()); method != "" {
		switch method {
		case "account/updated":
			return s.rewriteAccountUpdatedNotification(payload)
		case "account/rateLimits/updated":
			return s.rewriteAccountRateLimitsUpdatedNotification(payload)
		default:
			return payload
		}
	}

	idKey := requestIDKey(payload)
	if idKey == "" {
		return payload
	}
	if !gjson.GetBytes(payload, "result").Exists() && !gjson.GetBytes(payload, "error").Exists() {
		return payload
	}

	s.mu.Lock()
	method := s.pendingMethods[idKey]
	delete(s.pendingMethods, idKey)
	s.mu.Unlock()

	switch method {
	case "account/read":
		return s.rewriteAccountReadResponse(payload)
	case "account/rateLimits/read":
		return s.rewriteAccountRateLimitsResponse(payload)
	default:
		return payload
	}
}

func (s *proxySession) rewriteAccountReadResponse(payload []byte) []byte {
	if !gjson.GetBytes(payload, "result").Exists() {
		return payload
	}
	account := s.buildSyntheticAccount(payload)
	if account == nil {
		return payload
	}

	if rewritten, err := sjson.SetBytes(payload, "result.account", account); err == nil {
		return rewritten
	}
	return payload
}

func (s *proxySession) rewriteAccountRateLimitsResponse(payload []byte) []byte {
	result := s.buildAccountRateLimitsResult()
	if result == nil {
		return payload
	}

	rewritten := payload
	if next, err := sjson.DeleteBytes(rewritten, "error"); err == nil {
		rewritten = next
	}
	if next, err := sjson.SetRawBytes(rewritten, "result", result); err == nil {
		rewritten = next
	}
	return rewritten
}

func (s *proxySession) rewriteAccountUpdatedNotification(payload []byte) []byte {
	account := s.buildSyntheticAccount(payload)
	if account == nil {
		return payload
	}
	if rewritten, err := sjson.SetBytes(payload, "params.account", account); err == nil {
		return rewritten
	}
	return payload
}

func (s *proxySession) rewriteAccountRateLimitsUpdatedNotification(payload []byte) []byte {
	result := s.buildAccountRateLimitsResult()
	if result == nil {
		return payload
	}
	if rewritten, err := sjson.SetRawBytes(payload, "params", result); err == nil {
		return rewritten
	}
	return payload
}

func (s *proxySession) buildSyntheticAccount(payload []byte) map[string]any {
	if s == nil {
		return nil
	}
	hasAccount := gjson.GetBytes(payload, "result.account").Exists() || gjson.GetBytes(payload, "params.account").Exists()
	requiresOpenAIAuth := gjson.GetBytes(payload, "result.requiresOpenaiAuth").Bool() || gjson.GetBytes(payload, "params.requiresOpenaiAuth").Bool()
	if !hasAccount && !requiresOpenAIAuth {
		return nil
	}

	email := s.accountLabel
	if s.hideAccountEmail {
		email = ""
	}

	return map[string]any{
		"type":     "chatgpt",
		"email":    email,
		"planType": s.syntheticPlanType(payload),
	}
}

func (s *proxySession) syntheticPlanType(payload []byte) string {
	if s.usePoolPlanType {
		if poolPlanType := s.poolPlanType(); poolPlanType != "" {
			return poolPlanType
		}
	}

	for _, path := range []string{"result.account.planType", "params.account.planType"} {
		if normalized := normalizeKnownPlanType(gjson.GetBytes(payload, path).String()); normalized != "" {
			return normalized
		}
	}
	return "unknown"
}

func (s *proxySession) buildAccountRateLimitsResult() []byte {
	aggregate, summary := s.poolAggregate()
	if aggregate == nil {
		return nil
	}

	snapshot := buildRateLimitSnapshot(aggregate, summary)
	if snapshot == nil {
		return nil
	}

	result := map[string]any{
		"rateLimits": snapshot,
		"rateLimitsByLimitId": map[string]any{
			"codex": snapshot,
		},
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		log.WithError(err).Debug("codex app-server proxy rate limit marshal failed")
		return nil
	}
	return encoded
}

func (s *proxySession) poolPlanType() string {
	aggregate, _ := s.poolAggregate()
	if aggregate == nil {
		return ""
	}
	return normalizeKnownPlanType(aggregate.PlanType)
}

func (s *proxySession) poolAggregate() (*coreauth.ComparableQuotaAggregate, *coreauth.ComparableQuotaPoolSummary) {
	if s == nil || s.authManager == nil {
		return nil, nil
	}
	if err := s.authManager.RefreshComparableQuotaProvider(context.Background(), "codex", false); err != nil {
		log.WithError(err).Debug("codex app-server proxy comparable quota refresh failed")
	}
	aggregate, ok := s.authManager.BuildComparableQuotaAggregate("codex", "")
	if !ok || aggregate == nil {
		return nil, nil
	}
	summary, _ := s.authManager.BuildComparableQuotaPoolSummary("codex")
	return aggregate, summary
}

func buildRateLimitSnapshot(
	aggregate *coreauth.ComparableQuotaAggregate,
	summary *coreauth.ComparableQuotaPoolSummary,
) map[string]any {
	if aggregate == nil {
		return nil
	}

	primary := buildRateLimitWindow(aggregate.Windows[coreauth.ComparableQuotaWindowFiveHour])
	secondary := buildRateLimitWindow(aggregate.Windows[coreauth.ComparableQuotaWindowWeekly])
	if primary == nil && secondary == nil {
		return nil
	}

	snapshot := map[string]any{
		"limitId":   "codex",
		"limitName": buildPoolLimitName(summary),
		"planType":  normalizeKnownPlanTypeOrUnknown(aggregate.PlanType),
		"primary":   primary,
		"secondary": secondary,
	}

	if window, ok := aggregate.Windows[coreauth.ComparableQuotaWindowFiveHour]; ok && window.HasValue && !window.Available {
		snapshot["rateLimitReachedType"] = "rate_limit_reached"
		return snapshot
	}
	if window, ok := aggregate.Windows[coreauth.ComparableQuotaWindowWeekly]; ok && window.HasValue && !window.Available {
		snapshot["rateLimitReachedType"] = "rate_limit_reached"
	}
	return snapshot
}

func buildRateLimitWindow(window coreauth.ComparableQuotaWindow) map[string]any {
	if !window.HasValue {
		return nil
	}

	var resetsAt any
	if !window.ResetAt.IsZero() {
		resetsAt = window.ResetAt.UTC().Unix()
	}

	var duration any
	if window.WindowSeconds > 0 {
		duration = int64(window.WindowSeconds / 60)
	}

	return map[string]any{
		"usedPercent":        clampRoundedPercent(window.UsedPercent),
		"resetsAt":           resetsAt,
		"windowDurationMins": duration,
	}
}

func clampRoundedPercent(value float64) int {
	switch {
	case math.IsNaN(value), math.IsInf(value, 0):
		return 0
	case value <= 0:
		return 0
	case value >= 100:
		return 100
	default:
		return int(math.Round(value))
	}
}

func buildPoolLimitName(summary *coreauth.ComparableQuotaPoolSummary) string {
	if summary == nil {
		return "CPA号池总额度"
	}

	parts := []string{
		"总共" + strconv.Itoa(summary.TotalCount),
		"可用" + strconv.Itoa(summary.ReadyCount),
		"耗尽" + strconv.Itoa(summary.ExhaustedCount),
	}
	return "CPA号池总额度(" + strings.Join(parts, ",") + ")"
}

func requestIDKey(payload []byte) string {
	id := gjson.GetBytes(payload, "id")
	if !id.Exists() {
		return ""
	}
	return strings.TrimSpace(id.Raw)
}

func requestIsLoopback(r *http.Request) bool {
	if r == nil {
		return false
	}
	host := strings.TrimSpace(r.RemoteAddr)
	if parsedHost, _, err := net.SplitHostPort(host); err == nil {
		host = parsedHost
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

func writeProxyError(w http.ResponseWriter, status int, code string, message string) {
	if w == nil {
		return
	}
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_, _ = io.WriteString(w, `{"error":"`+code+`","message":"`+message+`"}`)
}

func normalizeKnownPlanType(planType string) string {
	switch strings.ToLower(strings.TrimSpace(planType)) {
	case "free", "go", "plus", "pro", "team", "business", "enterprise", "edu", "unknown",
		"guest", "free_workspace", "quorum", "k12", "education":
		return strings.ToLower(strings.TrimSpace(planType))
	case "pro-lite", "pro_lite", "prolite":
		return "prolite"
	case "self-serve-business-usage-based", "self_serve_business_usage_based":
		return "self_serve_business_usage_based"
	case "enterprise-cbp-usage-based", "enterprise_cbp_usage_based":
		return "enterprise_cbp_usage_based"
	default:
		return ""
	}
}

func normalizeKnownPlanTypeOrUnknown(planType string) string {
	if normalized := normalizeKnownPlanType(planType); normalized != "" {
		return normalized
	}
	return "unknown"
}

func normalizeSessionError(err error) error {
	if isNormalSessionError(err) {
		return nil
	}
	return err
}

func isNormalSessionError(err error) bool {
	if err == nil {
		return true
	}
	if errors.Is(err, io.EOF) {
		return true
	}
	if websocket.IsCloseError(err, websocket.CloseNormalClosure, websocket.CloseGoingAway, websocket.CloseNoStatusReceived) {
		return true
	}
	return strings.Contains(strings.ToLower(err.Error()), "use of closed network connection")
}
