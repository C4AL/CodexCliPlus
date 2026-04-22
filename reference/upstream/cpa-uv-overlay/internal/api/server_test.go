package api

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	gin "github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"
	proxyconfig "github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	internallogging "github.com/router-for-me/CLIProxyAPI/v6/internal/logging"
	sdkaccess "github.com/router-for-me/CLIProxyAPI/v6/sdk/access"
	"github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
	sdkconfig "github.com/router-for-me/CLIProxyAPI/v6/sdk/config"
)

func TestMain(m *testing.M) {
	if os.Getenv("CPA_TEST_HELPER_CODEX_APP_SERVER") == "1" {
		os.Exit(runHelperCodexAppServer())
	}
	os.Exit(m.Run())
}

func runHelperCodexAppServer() int {
	reader := bufio.NewReader(os.Stdin)
	writer := bufio.NewWriter(os.Stdout)
	writeResponse := func(idRaw []byte, result string) error {
		_, err := fmt.Fprintf(writer, `{"id":%s,"result":%s}`+"\n", strings.TrimSpace(string(idRaw)), result)
		if err != nil {
			return err
		}
		return writer.Flush()
	}

	for {
		line, err := reader.ReadBytes('\n')
		if err != nil {
			if errors.Is(err, io.EOF) {
				return 0
			}
			return 1
		}
		trimmed := bytesTrimSpaceString(line)
		if trimmed == "" {
			continue
		}

		var envelope map[string]json.RawMessage
		if err := json.Unmarshal([]byte(trimmed), &envelope); err != nil {
			return 2
		}

		var method string
		if rawMethod, ok := envelope["method"]; ok {
			if err := json.Unmarshal(rawMethod, &method); err != nil {
				return 3
			}
		}
		idRaw, hasID := envelope["id"]
		if !hasID {
			continue
		}

		switch method {
		case "initialize":
			if err := writeResponse(idRaw, `{}`); err != nil {
				return 4
			}
		case "account/read":
			if err := writeResponse(idRaw, `{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}`); err != nil {
				return 5
			}
		case "account/rateLimits/read":
			if err := writeResponse(idRaw, `{}`); err != nil {
				return 6
			}
		default:
			if err := writeResponse(idRaw, `{}`); err != nil {
				return 7
			}
		}
	}
}

func bytesTrimSpaceString(data []byte) string {
	return strings.TrimSpace(string(data))
}

func newTestServer(t *testing.T) *Server {
	t.Helper()

	gin.SetMode(gin.TestMode)

	tmpDir := t.TempDir()
	authDir := filepath.Join(tmpDir, "auth")
	if err := os.MkdirAll(authDir, 0o700); err != nil {
		t.Fatalf("failed to create auth dir: %v", err)
	}

	cfg := &proxyconfig.Config{
		SDKConfig: sdkconfig.SDKConfig{
			APIKeys: []string{"test-key"},
		},
		Port:                   0,
		AuthDir:                authDir,
		Debug:                  true,
		LoggingToFile:          false,
		UsageStatisticsEnabled: false,
	}

	authManager := auth.NewManager(nil, nil, nil)
	accessManager := sdkaccess.NewManager()

	configPath := filepath.Join(tmpDir, "config.yaml")
	return NewServer(cfg, authManager, accessManager, configPath)
}

func TestHealthz(t *testing.T) {
	server := newTestServer(t)

	t.Run("GET", func(t *testing.T) {
		req := httptest.NewRequest(http.MethodGet, "/healthz", nil)
		rr := httptest.NewRecorder()
		server.engine.ServeHTTP(rr, req)

		if rr.Code != http.StatusOK {
			t.Fatalf("unexpected status code: got %d want %d; body=%s", rr.Code, http.StatusOK, rr.Body.String())
		}

		var resp struct {
			Status string `json:"status"`
		}
		if err := json.Unmarshal(rr.Body.Bytes(), &resp); err != nil {
			t.Fatalf("failed to parse response JSON: %v; body=%s", err, rr.Body.String())
		}
		if resp.Status != "ok" {
			t.Fatalf("unexpected response status: got %q want %q", resp.Status, "ok")
		}
	})

	t.Run("HEAD", func(t *testing.T) {
		req := httptest.NewRequest(http.MethodHead, "/healthz", nil)
		rr := httptest.NewRecorder()
		server.engine.ServeHTTP(rr, req)

		if rr.Code != http.StatusOK {
			t.Fatalf("unexpected status code: got %d want %d; body=%s", rr.Code, http.StatusOK, rr.Body.String())
		}
		if rr.Body.Len() != 0 {
			t.Fatalf("expected empty body for HEAD request, got %q", rr.Body.String())
		}
	})
}

func TestBackendAPIWhamUsage_LocalLoopbackReturnsComparableQuotaPayload(t *testing.T) {
	server := newTestServer(t)

	now := time.Now().UTC()
	registerComparable := func(id string, primaryUsed float64, secondaryUsed float64) {
		t.Helper()
		_, err := server.handlers.AuthManager.Register(context.Background(), &auth.Auth{
			ID:       id,
			Provider: "codex",
			Quota: auth.QuotaState{
				Comparable: &auth.ComparableQuotaSnapshot{
					Provider:  "codex",
					AccountID: id,
					PlanType:  "pro",
					UpdatedAt: now,
					Windows: map[string]auth.ComparableQuotaWindow{
						auth.ComparableQuotaWindowFiveHour: {
							ID:            auth.ComparableQuotaWindowFiveHour,
							UsedPercent:   primaryUsed,
							HasValue:      true,
							Available:     true,
							ResetAt:       now.Add(10 * time.Minute),
							WindowSeconds: 18_000,
						},
						auth.ComparableQuotaWindowWeekly: {
							ID:            auth.ComparableQuotaWindowWeekly,
							UsedPercent:   secondaryUsed,
							HasValue:      true,
							Available:     true,
							ResetAt:       now.Add(24 * time.Hour),
							WindowSeconds: 604_800,
						},
					},
				},
			},
		})
		if err != nil {
			t.Fatalf("Register(%s) error = %v", id, err)
		}
	}

	registerComparable("codex-a", 20, 50)
	registerComparable("codex-b", 60, 70)

	req := httptest.NewRequest(http.MethodGet, "/backend-api/wham/usage", nil)
	req.RemoteAddr = "127.0.0.1:4321"
	rr := httptest.NewRecorder()

	server.engine.ServeHTTP(rr, req)

	if rr.Code != http.StatusOK {
		t.Fatalf("unexpected status code: got %d want %d; body=%s", rr.Code, http.StatusOK, rr.Body.String())
	}

	var resp struct {
		PlanType  string `json:"plan_type"`
		RateLimit struct {
			Allowed       bool `json:"allowed"`
			LimitReached  bool `json:"limit_reached"`
			PrimaryWindow struct {
				UsedPercent        int `json:"used_percent"`
				LimitWindowSeconds int `json:"limit_window_seconds"`
			} `json:"primary_window"`
			SecondaryWindow struct {
				UsedPercent        int `json:"used_percent"`
				LimitWindowSeconds int `json:"limit_window_seconds"`
			} `json:"secondary_window"`
		} `json:"rate_limit"`
	}
	if err := json.Unmarshal(rr.Body.Bytes(), &resp); err != nil {
		t.Fatalf("failed to parse response JSON: %v; body=%s", err, rr.Body.String())
	}

	if resp.PlanType != "pro" {
		t.Fatalf("plan_type = %q, want %q", resp.PlanType, "pro")
	}
	if !resp.RateLimit.Allowed {
		t.Fatalf("rate_limit.allowed = false, want true")
	}
	if resp.RateLimit.LimitReached {
		t.Fatalf("rate_limit.limit_reached = true, want false")
	}
	if resp.RateLimit.PrimaryWindow.UsedPercent != 40 {
		t.Fatalf("primary used_percent = %d, want %d", resp.RateLimit.PrimaryWindow.UsedPercent, 40)
	}
	if resp.RateLimit.PrimaryWindow.LimitWindowSeconds != 18_000 {
		t.Fatalf("primary limit_window_seconds = %d, want %d", resp.RateLimit.PrimaryWindow.LimitWindowSeconds, 18_000)
	}
	if resp.RateLimit.SecondaryWindow.UsedPercent != 60 {
		t.Fatalf("secondary used_percent = %d, want %d", resp.RateLimit.SecondaryWindow.UsedPercent, 60)
	}
	if resp.RateLimit.SecondaryWindow.LimitWindowSeconds != 604_800 {
		t.Fatalf("secondary limit_window_seconds = %d, want %d", resp.RateLimit.SecondaryWindow.LimitWindowSeconds, 604_800)
	}
}

func TestBackendAPIWhamUsage_RejectsNonLoopback(t *testing.T) {
	server := newTestServer(t)

	req := httptest.NewRequest(http.MethodGet, "/backend-api/wham/usage", nil)
	req.RemoteAddr = "192.0.2.10:4321"
	rr := httptest.NewRecorder()

	server.engine.ServeHTTP(rr, req)

	if rr.Code != http.StatusForbidden {
		t.Fatalf("unexpected status code: got %d want %d; body=%s", rr.Code, http.StatusForbidden, rr.Body.String())
	}
}

func TestRootRemainsHTTPWhenCodexAppServerProxyEnabled(t *testing.T) {
	server := newTestServer(t)
	server.cfg.CodexAppServerProxy = proxyconfig.CodexAppServerProxy{
		Enable:              true,
		RestrictToLocalhost: true,
		AccountLabel:        "CPA-UV@limit",
		UsePoolPlanType:     true,
	}

	req := httptest.NewRequest(http.MethodGet, "/", nil)
	rr := httptest.NewRecorder()

	server.engine.ServeHTTP(rr, req)

	if rr.Code != http.StatusOK {
		t.Fatalf("unexpected status code: got %d want %d; body=%s", rr.Code, http.StatusOK, rr.Body.String())
	}
	if !strings.Contains(rr.Body.String(), "CLI Proxy API Server") {
		t.Fatalf("unexpected root body: %s", rr.Body.String())
	}
}

func TestRootCodexAppServerProxy_RewritesAccountRead(t *testing.T) {
	server := newTestServer(t)
	exePath, err := os.Executable()
	if err != nil {
		t.Fatalf("os.Executable error = %v", err)
	}
	server.cfg.CodexAppServerProxy = proxyconfig.CodexAppServerProxy{
		Enable:              true,
		RestrictToLocalhost: true,
		CodexBin:            exePath,
		HideAccountEmail:    true,
		UsePoolPlanType:     true,
	}
	t.Setenv("CPA_TEST_HELPER_CODEX_APP_SERVER", "1")

	now := time.Now().UTC()
	_, err = server.handlers.AuthManager.Register(context.Background(), &auth.Auth{
		ID:       "codex-pool-a",
		Provider: "codex",
		Quota: auth.QuotaState{
			Comparable: &auth.ComparableQuotaSnapshot{
				Provider:  "codex",
				AccountID: "codex-pool-a",
				PlanType:  "pro",
				UpdatedAt: now,
				Windows: map[string]auth.ComparableQuotaWindow{
					auth.ComparableQuotaWindowFiveHour: {
						ID:            auth.ComparableQuotaWindowFiveHour,
						UsedPercent:   18,
						HasValue:      true,
						Available:     true,
						ResetAt:       now.Add(30 * time.Minute),
						WindowSeconds: 18_000,
					},
					auth.ComparableQuotaWindowWeekly: {
						ID:            auth.ComparableQuotaWindowWeekly,
						UsedPercent:   22,
						HasValue:      true,
						Available:     true,
						ResetAt:       now.Add(24 * time.Hour),
						WindowSeconds: 604_800,
					},
				},
			},
		},
	})
	if err != nil {
		t.Fatalf("Register comparable auth error = %v", err)
	}

	ts := httptest.NewServer(server.engine)
	defer ts.Close()

	wsURL := "ws" + strings.TrimPrefix(ts.URL, "http") + "/"
	conn, _, err := websocket.DefaultDialer.Dial(wsURL, nil)
	if err != nil {
		t.Fatalf("websocket dial error = %v", err)
	}
	defer conn.Close()

	if err := conn.WriteJSON(map[string]any{
		"jsonrpc": "2.0",
		"method":  "initialize",
		"id":      "init-1",
		"params": map[string]any{
			"clientInfo": map[string]any{
				"name":    "test-client",
				"version": "0.0.0-test",
			},
		},
	}); err != nil {
		t.Fatalf("initialize write error = %v", err)
	}

	_, initResponse, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("initialize read error = %v", err)
	}
	if !strings.Contains(string(initResponse), `"id":"init-1"`) {
		t.Fatalf("unexpected initialize response: %s", string(initResponse))
	}

	if err := conn.WriteJSON(map[string]any{
		"jsonrpc": "2.0",
		"method":  "initialized",
		"params":  map[string]any{},
	}); err != nil {
		t.Fatalf("initialized write error = %v", err)
	}

	if err := conn.WriteJSON(map[string]any{
		"jsonrpc": "2.0",
		"method":  "account/read",
		"id":      7,
		"params": map[string]any{
			"refreshToken": false,
		},
	}); err != nil {
		t.Fatalf("account/read write error = %v", err)
	}

	_, accountResponse, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("account/read read error = %v", err)
	}

	var payload struct {
		ID     int `json:"id"`
		Result struct {
			Account struct {
				Type     string `json:"type"`
				Email    string `json:"email"`
				PlanType string `json:"planType"`
			} `json:"account"`
			RequiresOpenAIAuth bool `json:"requiresOpenaiAuth"`
		} `json:"result"`
	}
	if err := json.Unmarshal(accountResponse, &payload); err != nil {
		t.Fatalf("account/read json.Unmarshal error = %v; body=%s", err, string(accountResponse))
	}

	if payload.ID != 7 {
		t.Fatalf("response id = %d, want %d", payload.ID, 7)
	}
	if payload.Result.Account.Type != "chatgpt" {
		t.Fatalf("account.type = %q, want %q", payload.Result.Account.Type, "chatgpt")
	}
	if payload.Result.Account.Email != "" {
		t.Fatalf("account.email = %q, want empty string", payload.Result.Account.Email)
	}
	if payload.Result.Account.PlanType != "pro" {
		t.Fatalf("account.planType = %q, want %q", payload.Result.Account.PlanType, "pro")
	}
	if !payload.Result.RequiresOpenAIAuth {
		t.Fatalf("requiresOpenaiAuth = false, want true")
	}

	if err := conn.WriteJSON(map[string]any{
		"jsonrpc": "2.0",
		"method":  "account/rateLimits/read",
		"id":      8,
		"params":  nil,
	}); err != nil {
		t.Fatalf("account/rateLimits/read write error = %v", err)
	}

	_, rateLimitsResponse, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("account/rateLimits/read read error = %v", err)
	}

	var ratePayload struct {
		ID     int `json:"id"`
		Result struct {
			RateLimits struct {
				LimitID   string `json:"limitId"`
				LimitName string `json:"limitName"`
				PlanType  string `json:"planType"`
				Primary   struct {
					UsedPercent        int `json:"usedPercent"`
					WindowDurationMins int `json:"windowDurationMins"`
				} `json:"primary"`
				Secondary struct {
					UsedPercent        int `json:"usedPercent"`
					WindowDurationMins int `json:"windowDurationMins"`
				} `json:"secondary"`
			} `json:"rateLimits"`
			RateLimitsByLimitID map[string]struct {
				PlanType string `json:"planType"`
			} `json:"rateLimitsByLimitId"`
		} `json:"result"`
	}
	if err := json.Unmarshal(rateLimitsResponse, &ratePayload); err != nil {
		t.Fatalf("account/rateLimits/read json.Unmarshal error = %v; body=%s", err, string(rateLimitsResponse))
	}

	if ratePayload.ID != 8 {
		t.Fatalf("rate limits response id = %d, want %d", ratePayload.ID, 8)
	}
	if ratePayload.Result.RateLimits.LimitID != "codex" {
		t.Fatalf("rateLimits.limitId = %q, want %q", ratePayload.Result.RateLimits.LimitID, "codex")
	}
	if ratePayload.Result.RateLimits.PlanType != "pro" {
		t.Fatalf("rateLimits.planType = %q, want %q", ratePayload.Result.RateLimits.PlanType, "pro")
	}
	if ratePayload.Result.RateLimits.Primary.UsedPercent != 18 {
		t.Fatalf("primary usedPercent = %d, want %d", ratePayload.Result.RateLimits.Primary.UsedPercent, 18)
	}
	if ratePayload.Result.RateLimits.Primary.WindowDurationMins != 300 {
		t.Fatalf("primary windowDurationMins = %d, want %d", ratePayload.Result.RateLimits.Primary.WindowDurationMins, 300)
	}
	if ratePayload.Result.RateLimits.Secondary.UsedPercent != 22 {
		t.Fatalf("secondary usedPercent = %d, want %d", ratePayload.Result.RateLimits.Secondary.UsedPercent, 22)
	}
	if ratePayload.Result.RateLimits.Secondary.WindowDurationMins != 10080 {
		t.Fatalf("secondary windowDurationMins = %d, want %d", ratePayload.Result.RateLimits.Secondary.WindowDurationMins, 10080)
	}
	if len(ratePayload.Result.RateLimitsByLimitID) != 1 {
		t.Fatalf("expected one entry in rateLimitsByLimitId, got %d", len(ratePayload.Result.RateLimitsByLimitID))
	}
	if ratePayload.Result.RateLimitsByLimitID["codex"].PlanType != "pro" {
		t.Fatalf("rateLimitsByLimitId[codex].planType = %q, want %q", ratePayload.Result.RateLimitsByLimitID["codex"].PlanType, "pro")
	}
}

func TestAmpProviderModelRoutes(t *testing.T) {
	testCases := []struct {
		name         string
		path         string
		wantStatus   int
		wantContains string
	}{
		{
			name:         "openai root models",
			path:         "/api/provider/openai/models",
			wantStatus:   http.StatusOK,
			wantContains: `"object":"list"`,
		},
		{
			name:         "groq root models",
			path:         "/api/provider/groq/models",
			wantStatus:   http.StatusOK,
			wantContains: `"object":"list"`,
		},
		{
			name:         "openai models",
			path:         "/api/provider/openai/v1/models",
			wantStatus:   http.StatusOK,
			wantContains: `"object":"list"`,
		},
		{
			name:         "anthropic models",
			path:         "/api/provider/anthropic/v1/models",
			wantStatus:   http.StatusOK,
			wantContains: `"data"`,
		},
		{
			name:         "google models v1",
			path:         "/api/provider/google/v1/models",
			wantStatus:   http.StatusOK,
			wantContains: `"models"`,
		},
		{
			name:         "google models v1beta",
			path:         "/api/provider/google/v1beta/models",
			wantStatus:   http.StatusOK,
			wantContains: `"models"`,
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			server := newTestServer(t)

			req := httptest.NewRequest(http.MethodGet, tc.path, nil)
			req.Header.Set("Authorization", "Bearer test-key")

			rr := httptest.NewRecorder()
			server.engine.ServeHTTP(rr, req)

			if rr.Code != tc.wantStatus {
				t.Fatalf("unexpected status code for %s: got %d want %d; body=%s", tc.path, rr.Code, tc.wantStatus, rr.Body.String())
			}
			if body := rr.Body.String(); !strings.Contains(body, tc.wantContains) {
				t.Fatalf("response body for %s missing %q: %s", tc.path, tc.wantContains, body)
			}
		})
	}
}

func TestDefaultRequestLoggerFactory_UsesResolvedLogDirectory(t *testing.T) {
	t.Setenv("WRITABLE_PATH", "")
	t.Setenv("writable_path", "")

	originalWD, errGetwd := os.Getwd()
	if errGetwd != nil {
		t.Fatalf("failed to get current working directory: %v", errGetwd)
	}

	tmpDir := t.TempDir()
	if errChdir := os.Chdir(tmpDir); errChdir != nil {
		t.Fatalf("failed to switch working directory: %v", errChdir)
	}
	defer func() {
		if errChdirBack := os.Chdir(originalWD); errChdirBack != nil {
			t.Fatalf("failed to restore working directory: %v", errChdirBack)
		}
	}()

	// Force ResolveLogDirectory to fallback to auth-dir/logs by making ./logs not a writable directory.
	if errWriteFile := os.WriteFile(filepath.Join(tmpDir, "logs"), []byte("not-a-directory"), 0o644); errWriteFile != nil {
		t.Fatalf("failed to create blocking logs file: %v", errWriteFile)
	}

	configDir := filepath.Join(tmpDir, "config")
	if errMkdirConfig := os.MkdirAll(configDir, 0o755); errMkdirConfig != nil {
		t.Fatalf("failed to create config dir: %v", errMkdirConfig)
	}
	configPath := filepath.Join(configDir, "config.yaml")

	authDir := filepath.Join(tmpDir, "auth")
	if errMkdirAuth := os.MkdirAll(authDir, 0o700); errMkdirAuth != nil {
		t.Fatalf("failed to create auth dir: %v", errMkdirAuth)
	}

	cfg := &proxyconfig.Config{
		SDKConfig: proxyconfig.SDKConfig{
			RequestLog: false,
		},
		AuthDir:           authDir,
		ErrorLogsMaxFiles: 10,
	}

	logger := defaultRequestLoggerFactory(cfg, configPath)
	fileLogger, ok := logger.(*internallogging.FileRequestLogger)
	if !ok {
		t.Fatalf("expected *FileRequestLogger, got %T", logger)
	}

	errLog := fileLogger.LogRequestWithOptions(
		"/v1/chat/completions",
		http.MethodPost,
		map[string][]string{"Content-Type": []string{"application/json"}},
		[]byte(`{"input":"hello"}`),
		http.StatusBadGateway,
		map[string][]string{"Content-Type": []string{"application/json"}},
		[]byte(`{"error":"upstream failure"}`),
		nil,
		nil,
		nil,
		nil,
		nil,
		true,
		"issue-1711",
		time.Now(),
		time.Now(),
	)
	if errLog != nil {
		t.Fatalf("failed to write forced error request log: %v", errLog)
	}

	authLogsDir := filepath.Join(authDir, "logs")
	authEntries, errReadAuthDir := os.ReadDir(authLogsDir)
	if errReadAuthDir != nil {
		t.Fatalf("failed to read auth logs dir %s: %v", authLogsDir, errReadAuthDir)
	}
	foundErrorLogInAuthDir := false
	for _, entry := range authEntries {
		if strings.HasPrefix(entry.Name(), "error-") && strings.HasSuffix(entry.Name(), ".log") {
			foundErrorLogInAuthDir = true
			break
		}
	}
	if !foundErrorLogInAuthDir {
		t.Fatalf("expected forced error log in auth fallback dir %s, got entries: %+v", authLogsDir, authEntries)
	}

	configLogsDir := filepath.Join(configDir, "logs")
	configEntries, errReadConfigDir := os.ReadDir(configLogsDir)
	if errReadConfigDir != nil && !os.IsNotExist(errReadConfigDir) {
		t.Fatalf("failed to inspect config logs dir %s: %v", configLogsDir, errReadConfigDir)
	}
	for _, entry := range configEntries {
		if strings.HasPrefix(entry.Name(), "error-") && strings.HasSuffix(entry.Name(), ".log") {
			t.Fatalf("unexpected forced error log in config dir %s", configLogsDir)
		}
	}
}
