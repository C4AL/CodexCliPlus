package auth

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"net/http"
	"net/url"
	"strings"
	"sync"
	"time"

	internalcodex "github.com/router-for-me/CLIProxyAPI/v6/internal/auth/codex"
	log "github.com/sirupsen/logrus"
)

const (
	ComparableQuotaWindowFiveHour = "five-hour"
	ComparableQuotaWindowWeekly   = "weekly"

	comparableQuotaDefaultInterval      = 30 * time.Second
	comparableQuotaMinRefreshInterval   = 5 * time.Second
	comparableQuotaSnapshotStaleAfter   = 10 * time.Minute
	comparableQuotaRefreshTimeout       = 20 * time.Second
	comparableQuotaDefaultWorkerCount   = 4
	comparableQuotaCodexDefaultBaseURL  = "https://chatgpt.com/backend-api/codex"
	comparableQuotaCodexUsageUserAgent  = "codex-cli"
	comparableQuotaCodexFiveHourSeconds = 18_000
	comparableQuotaCodexWeeklySeconds   = 604_800
)

type ComparableQuotaAggregate struct {
	Provider    string                           `json:"-"`
	PlanType    string                           `json:"-"`
	SampleCount int                              `json:"-"`
	UpdatedAt   time.Time                        `json:"-"`
	Windows     map[string]ComparableQuotaWindow `json:"-"`
}

type comparableQuotaProvider interface {
	Supports(auth *Auth) bool
	Fetch(ctx context.Context, manager *Manager, auth *Auth) (*ComparableQuotaSnapshot, error)
}

type comparableQuotaRefreshRequest struct {
	authID string
	force  bool
}

type comparableQuotaTracker struct {
	manager  *Manager
	interval time.Duration
	workers  int
	queue    chan comparableQuotaRefreshRequest
}

type codexComparableQuotaProvider struct{}

type codexComparableUsagePayload struct {
	PlanType   string                        `json:"plan_type"`
	PlanType2  string                        `json:"planType"`
	RateLimit  *codexComparableRateLimitInfo `json:"rate_limit"`
	RateLimit2 *codexComparableRateLimitInfo `json:"rateLimit"`
}

type codexComparableRateLimitInfo struct {
	Allowed          *bool                  `json:"allowed"`
	LimitReached     *bool                  `json:"limit_reached"`
	LimitReached2    *bool                  `json:"limitReached"`
	PrimaryWindow    *codexComparableWindow `json:"primary_window"`
	PrimaryWindow2   *codexComparableWindow `json:"primaryWindow"`
	SecondaryWindow  *codexComparableWindow `json:"secondary_window"`
	SecondaryWindow2 *codexComparableWindow `json:"secondaryWindow"`
}

type codexComparableWindow struct {
	UsedPercent         *float64 `json:"used_percent"`
	UsedPercent2        *float64 `json:"usedPercent"`
	LimitWindowSeconds  *int     `json:"limit_window_seconds"`
	LimitWindowSeconds2 *int     `json:"limitWindowSeconds"`
	ResetAt             *int64   `json:"reset_at"`
	ResetAt2            *int64   `json:"resetAt"`
}

func (a *ComparableQuotaAggregate) Clone() *ComparableQuotaAggregate {
	if a == nil {
		return nil
	}
	copyAggregate := *a
	if len(a.Windows) > 0 {
		copyAggregate.Windows = make(map[string]ComparableQuotaWindow, len(a.Windows))
		for key, value := range a.Windows {
			copyAggregate.Windows[key] = value
		}
	}
	return &copyAggregate
}

func comparableQuotaProviderForAuth(auth *Auth) comparableQuotaProvider {
	if auth == nil {
		return nil
	}
	switch strings.ToLower(strings.TrimSpace(auth.Provider)) {
	case "codex":
		provider := codexComparableQuotaProvider{}
		if provider.Supports(auth) {
			return provider
		}
	}
	return nil
}

func comparableQuotaSnapshotFresh(snapshot *ComparableQuotaSnapshot, now time.Time) bool {
	if snapshot == nil || snapshot.UpdatedAt.IsZero() {
		return false
	}
	if now.IsZero() {
		now = time.Now()
	}
	return now.Sub(snapshot.UpdatedAt) <= comparableQuotaSnapshotStaleAfter
}

func comparablePrimaryQuotaUsedPercent(auth *Auth, now time.Time) (float64, bool) {
	if auth == nil {
		return 0, false
	}
	snapshot := auth.Quota.Comparable
	if !comparableQuotaSnapshotFresh(snapshot, now) {
		return 0, false
	}
	window, ok := snapshot.Windows[ComparableQuotaWindowFiveHour]
	if !ok || !window.HasValue || !window.Available {
		return 0, false
	}
	return window.UsedPercent, true
}

func (m *Manager) StartComparableQuotaTracking(parent context.Context, interval time.Duration) {
	if m == nil {
		return
	}
	if parent == nil {
		parent = context.Background()
	}
	if interval <= 0 {
		interval = comparableQuotaDefaultInterval
	}

	m.mu.Lock()
	cancelPrev := m.comparableQuotaCancel
	m.comparableQuotaCancel = nil
	m.comparableQuotaTracker = nil
	m.mu.Unlock()
	if cancelPrev != nil {
		cancelPrev()
	}

	ctx, cancelCtx := context.WithCancel(parent)
	tracker := &comparableQuotaTracker{
		manager:  m,
		interval: interval,
		workers:  comparableQuotaDefaultWorkerCount,
		queue:    make(chan comparableQuotaRefreshRequest, 256),
	}

	m.mu.Lock()
	m.comparableQuotaCancel = cancelCtx
	m.comparableQuotaTracker = tracker
	m.mu.Unlock()

	go tracker.run(ctx)
}

func (m *Manager) StopComparableQuotaTracking() {
	if m == nil {
		return
	}
	m.mu.Lock()
	cancel := m.comparableQuotaCancel
	m.comparableQuotaCancel = nil
	m.comparableQuotaTracker = nil
	m.mu.Unlock()
	if cancel != nil {
		cancel()
	}
}

func (m *Manager) queueComparableQuotaRefresh(authID string, force bool) {
	if m == nil {
		return
	}
	authID = strings.TrimSpace(authID)
	if authID == "" {
		return
	}
	m.mu.RLock()
	tracker := m.comparableQuotaTracker
	m.mu.RUnlock()
	if tracker == nil {
		return
	}
	select {
	case tracker.queue <- comparableQuotaRefreshRequest{authID: authID, force: force}:
	default:
	}
}

func (t *comparableQuotaTracker) run(ctx context.Context) {
	if t == nil || t.manager == nil {
		return
	}
	if err := t.manager.RefreshComparableQuotaProvider(ctx, "", false); err != nil && !errors.Is(err, context.Canceled) {
		log.WithError(err).Debug("initial comparable quota refresh failed")
	}

	ticker := time.NewTicker(t.interval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case request := <-t.queue:
			refreshCtx, cancel := context.WithTimeout(ctx, comparableQuotaRefreshTimeout)
			_ = t.manager.refreshComparableQuotaAuthByID(refreshCtx, request.authID, request.force)
			cancel()
		case <-ticker.C:
			refreshCtx, cancel := context.WithTimeout(ctx, comparableQuotaRefreshTimeout)
			_ = t.manager.RefreshComparableQuotaProvider(refreshCtx, "", false)
			cancel()
		}
	}
}

func (m *Manager) RefreshComparableQuotaProvider(ctx context.Context, provider string, force bool) error {
	if m == nil {
		return nil
	}
	candidates := m.snapshotComparableQuotaCandidates(provider)
	if len(candidates) == 0 {
		return nil
	}

	workers := comparableQuotaDefaultWorkerCount
	if len(candidates) < workers {
		workers = len(candidates)
	}
	if workers <= 0 {
		workers = 1
	}

	sem := make(chan struct{}, workers)
	var wg sync.WaitGroup
	var firstErr error
	var errMu sync.Mutex

	for _, candidate := range candidates {
		if candidate == nil {
			continue
		}
		if !m.shouldRefreshComparableQuota(candidate, time.Now(), force) {
			continue
		}
		wg.Add(1)
		go func(auth *Auth) {
			defer wg.Done()
			sem <- struct{}{}
			defer func() { <-sem }()
			refreshCtx := ctx
			cancel := func() {}
			if refreshCtx == nil {
				refreshCtx = context.Background()
			}
			if _, hasDeadline := refreshCtx.Deadline(); !hasDeadline {
				refreshCtx, cancel = context.WithTimeout(refreshCtx, comparableQuotaRefreshTimeout)
			}
			defer cancel()
			if err := m.refreshComparableQuotaAuth(refreshCtx, auth); err != nil && refreshCtx.Err() == nil {
				errMu.Lock()
				if firstErr == nil {
					firstErr = err
				}
				errMu.Unlock()
			}
		}(candidate)
	}

	wg.Wait()
	return firstErr
}

func (m *Manager) GetComparableQuotaSnapshot(authID string) (*ComparableQuotaSnapshot, bool) {
	if m == nil {
		return nil, false
	}
	authID = strings.TrimSpace(authID)
	if authID == "" {
		return nil, false
	}
	m.mu.RLock()
	defer m.mu.RUnlock()
	auth, ok := m.auths[authID]
	if !ok || auth == nil || auth.Quota.Comparable == nil {
		return nil, false
	}
	return auth.Quota.Comparable.Clone(), true
}

func (m *Manager) BuildComparableQuotaAggregate(provider string, requestAccountID string) (*ComparableQuotaAggregate, bool) {
	if m == nil {
		return nil, false
	}
	provider = strings.ToLower(strings.TrimSpace(provider))
	requestAccountID = strings.TrimSpace(requestAccountID)
	now := time.Now()

	m.mu.RLock()
	defer m.mu.RUnlock()

	sumByWindow := make(map[string]float64)
	countByWindow := make(map[string]int)
	availableByWindow := make(map[string]bool)
	resetAtByWindow := make(map[string]time.Time)
	planType := ""
	updatedAt := time.Time{}
	sampleCount := 0

	for _, auth := range m.auths {
		if auth == nil {
			continue
		}
		if provider != "" && strings.ToLower(strings.TrimSpace(auth.Provider)) != provider {
			continue
		}
		if auth.Disabled || auth.Status == StatusDisabled {
			continue
		}
		snapshot := auth.Quota.Comparable
		if !comparableQuotaSnapshotFresh(snapshot, now) {
			continue
		}
		sampleCount++
		if snapshot.UpdatedAt.After(updatedAt) {
			updatedAt = snapshot.UpdatedAt
		}
		if requestAccountID != "" && requestAccountID == snapshot.AccountID {
			if resolved := strings.TrimSpace(snapshot.PlanType); resolved != "" {
				planType = resolved
			}
		} else if planType == "" {
			if resolved := strings.TrimSpace(snapshot.PlanType); resolved != "" {
				planType = resolved
			}
		}
		for windowID, window := range snapshot.Windows {
			if !window.HasValue {
				continue
			}
			sumByWindow[windowID] += window.UsedPercent
			countByWindow[windowID]++
			if window.Available {
				availableByWindow[windowID] = true
			}
			if !window.ResetAt.IsZero() {
				current := resetAtByWindow[windowID]
				if current.IsZero() || window.ResetAt.Before(current) {
					resetAtByWindow[windowID] = window.ResetAt
				}
			}
		}
	}

	if sampleCount == 0 {
		return nil, false
	}
	if planType == "" {
		planType = "free"
	}

	aggregate := &ComparableQuotaAggregate{
		Provider:    provider,
		PlanType:    planType,
		SampleCount: sampleCount,
		UpdatedAt:   updatedAt,
		Windows:     make(map[string]ComparableQuotaWindow),
	}
	for _, windowID := range []string{ComparableQuotaWindowFiveHour, ComparableQuotaWindowWeekly} {
		count := countByWindow[windowID]
		if count == 0 {
			continue
		}
		window := ComparableQuotaWindow{
			ID:        windowID,
			HasValue:  true,
			Available: availableByWindow[windowID],
			ResetAt:   resetAtByWindow[windowID],
		}
		window.UsedPercent = clampComparableUsedPercent(sumByWindow[windowID] / float64(count))
		switch windowID {
		case ComparableQuotaWindowFiveHour:
			window.WindowSeconds = comparableQuotaCodexFiveHourSeconds
		case ComparableQuotaWindowWeekly:
			window.WindowSeconds = comparableQuotaCodexWeeklySeconds
		}
		aggregate.Windows[windowID] = window
	}
	if len(aggregate.Windows) == 0 {
		return nil, false
	}
	return aggregate, true
}

func (m *Manager) snapshotComparableQuotaCandidates(provider string) []*Auth {
	if m == nil {
		return nil
	}
	provider = strings.ToLower(strings.TrimSpace(provider))
	m.mu.RLock()
	defer m.mu.RUnlock()

	candidates := make([]*Auth, 0, len(m.auths))
	for _, auth := range m.auths {
		if auth == nil || auth.Disabled {
			continue
		}
		if provider != "" && strings.ToLower(strings.TrimSpace(auth.Provider)) != provider {
			continue
		}
		if comparableQuotaProviderForAuth(auth) == nil {
			continue
		}
		candidates = append(candidates, auth.Clone())
	}
	return candidates
}

func (m *Manager) shouldRefreshComparableQuota(auth *Auth, now time.Time, force bool) bool {
	if auth == nil || auth.Disabled {
		return false
	}
	if comparableQuotaProviderForAuth(auth) == nil {
		return false
	}
	if now.IsZero() {
		now = time.Now()
	}
	snapshot := auth.Quota.Comparable
	if snapshot == nil || snapshot.UpdatedAt.IsZero() {
		return true
	}
	age := now.Sub(snapshot.UpdatedAt)
	if age < 0 {
		age = 0
	}
	if force {
		return age >= comparableQuotaMinRefreshInterval
	}
	return age >= comparableQuotaDefaultInterval
}

func (m *Manager) refreshComparableQuotaAuthByID(ctx context.Context, authID string, force bool) error {
	if m == nil {
		return nil
	}
	authID = strings.TrimSpace(authID)
	if authID == "" {
		return nil
	}

	m.mu.RLock()
	auth, ok := m.auths[authID]
	if !ok || auth == nil {
		m.mu.RUnlock()
		return nil
	}
	snapshot := auth.Clone()
	m.mu.RUnlock()

	if !m.shouldRefreshComparableQuota(snapshot, time.Now(), force) {
		return nil
	}
	return m.refreshComparableQuotaAuth(ctx, snapshot)
}

func (m *Manager) refreshComparableQuotaAuth(ctx context.Context, auth *Auth) error {
	if m == nil || auth == nil {
		return nil
	}
	provider := comparableQuotaProviderForAuth(auth)
	if provider == nil {
		return nil
	}
	snapshot, err := provider.Fetch(ctx, m, auth)
	if err != nil {
		return err
	}
	if snapshot == nil {
		return nil
	}

	var authSnapshot *Auth
	m.mu.Lock()
	current, ok := m.auths[auth.ID]
	if ok && current != nil {
		current.Quota.Comparable = snapshot.Clone()
		authSnapshot = current.Clone()
	}
	m.mu.Unlock()

	if m.scheduler != nil && authSnapshot != nil {
		m.scheduler.upsertAuth(authSnapshot)
	}
	return nil
}

func (codexComparableQuotaProvider) Supports(auth *Auth) bool {
	if auth == nil {
		return false
	}
	if strings.ToLower(strings.TrimSpace(auth.Provider)) != "codex" {
		return false
	}
	if resolveCodexComparableAccountID(auth) == "" {
		return false
	}
	return resolveCodexComparableTokenPresent(auth)
}

func (codexComparableQuotaProvider) Fetch(ctx context.Context, manager *Manager, auth *Auth) (*ComparableQuotaSnapshot, error) {
	if manager == nil || auth == nil {
		return nil, nil
	}
	accountID := resolveCodexComparableAccountID(auth)
	if accountID == "" {
		return nil, nil
	}

	targetURL, err := resolveCodexComparableUsageURL(auth)
	if err != nil {
		return nil, err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, targetURL, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", comparableQuotaCodexUsageUserAgent)
	req.Header.Set("Chatgpt-Account-Id", accountID)

	resp, err := manager.HttpRequest(ctx, auth, req)
	if err != nil {
		return nil, err
	}
	defer func() {
		_ = resp.Body.Close()
	}()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("codex comparable quota request failed with status %d", resp.StatusCode)
	}

	var payload codexComparableUsagePayload
	if err := json.Unmarshal(body, &payload); err != nil {
		return nil, err
	}

	rateLimit := payload.RateLimit
	if rateLimit == nil {
		rateLimit = payload.RateLimit2
	}
	if rateLimit == nil {
		return nil, fmt.Errorf("codex comparable quota payload missing rate_limit")
	}

	limitReached := derefBool(rateLimit.LimitReached, rateLimit.LimitReached2)
	primaryWindow, secondaryWindow := pickCodexComparableWindows(rateLimit)
	primaryComparable := buildCodexComparableWindow(ComparableQuotaWindowFiveHour, primaryWindow, limitReached, rateLimit.Allowed)
	secondaryComparable := buildCodexComparableWindow(ComparableQuotaWindowWeekly, secondaryWindow, limitReached, rateLimit.Allowed)
	if secondaryComparable.HasValue {
		secondaryComparable.Available = secondaryComparable.Available && secondaryComparable.UsedPercent < 100
	}
	if primaryComparable.HasValue {
		primaryComparable.Available = primaryComparable.Available && primaryComparable.UsedPercent < 100
		if secondaryComparable.HasValue && !secondaryComparable.Available {
			primaryComparable.Available = false
		}
	}

	planType := strings.TrimSpace(payload.PlanType)
	if planType == "" {
		planType = strings.TrimSpace(payload.PlanType2)
	}
	if planType == "" {
		planType = resolveCodexComparablePlanType(auth)
	}

	snapshot := &ComparableQuotaSnapshot{
		Provider:  "codex",
		AccountID: accountID,
		PlanType:  strings.ToLower(strings.TrimSpace(planType)),
		UpdatedAt: time.Now().UTC(),
		Windows: map[string]ComparableQuotaWindow{
			ComparableQuotaWindowFiveHour: primaryComparable,
			ComparableQuotaWindowWeekly:   secondaryComparable,
		},
	}
	return snapshot, nil
}

func resolveCodexComparableUsageURL(auth *Auth) (string, error) {
	baseURL := comparableQuotaCodexDefaultBaseURL
	if auth != nil && auth.Attributes != nil {
		if candidate := strings.TrimSpace(auth.Attributes["base_url"]); candidate != "" {
			baseURL = candidate
		}
	}
	parsed, err := url.Parse(baseURL)
	if err != nil {
		return "", err
	}
	parsed.Path = "/backend-api/wham/usage"
	parsed.RawPath = ""
	parsed.RawQuery = ""
	parsed.Fragment = ""
	return parsed.String(), nil
}

func resolveCodexComparableAccountID(auth *Auth) string {
	if auth == nil {
		return ""
	}
	if auth.Metadata != nil {
		if raw, ok := auth.Metadata["account_id"].(string); ok {
			if trimmed := strings.TrimSpace(raw); trimmed != "" {
				return trimmed
			}
		}
	}
	idToken := ""
	if auth.Metadata != nil {
		if raw, ok := auth.Metadata["id_token"].(string); ok {
			idToken = strings.TrimSpace(raw)
		}
	}
	if idToken == "" && auth.Attributes != nil {
		idToken = strings.TrimSpace(auth.Attributes["id_token"])
	}
	if idToken == "" {
		return ""
	}
	claims, err := internalcodex.ParseJWTToken(idToken)
	if err != nil || claims == nil {
		return ""
	}
	return strings.TrimSpace(claims.CodexAuthInfo.ChatgptAccountID)
}

func resolveCodexComparablePlanType(auth *Auth) string {
	if auth == nil {
		return ""
	}
	if auth.Attributes != nil {
		if raw := strings.TrimSpace(auth.Attributes["plan_type"]); raw != "" {
			return strings.ToLower(raw)
		}
	}
	idToken := ""
	if auth.Metadata != nil {
		if raw, ok := auth.Metadata["id_token"].(string); ok {
			idToken = strings.TrimSpace(raw)
		}
	}
	if idToken == "" && auth.Attributes != nil {
		idToken = strings.TrimSpace(auth.Attributes["id_token"])
	}
	if idToken == "" {
		return ""
	}
	claims, err := internalcodex.ParseJWTToken(idToken)
	if err != nil || claims == nil {
		return ""
	}
	return strings.ToLower(strings.TrimSpace(claims.CodexAuthInfo.ChatgptPlanType))
}

func resolveCodexComparableTokenPresent(auth *Auth) bool {
	if auth == nil {
		return false
	}
	if auth.Attributes != nil {
		if token := strings.TrimSpace(auth.Attributes["api_key"]); token != "" {
			return true
		}
	}
	if auth.Metadata != nil {
		if token, ok := auth.Metadata["access_token"].(string); ok && strings.TrimSpace(token) != "" {
			return true
		}
		if token, ok := auth.Metadata["token"].(string); ok && strings.TrimSpace(token) != "" {
			return true
		}
	}
	return false
}

func pickCodexComparableWindows(rateLimit *codexComparableRateLimitInfo) (*codexComparableWindow, *codexComparableWindow) {
	if rateLimit == nil {
		return nil, nil
	}
	primaryWindow := rateLimit.PrimaryWindow
	if primaryWindow == nil {
		primaryWindow = rateLimit.PrimaryWindow2
	}
	secondaryWindow := rateLimit.SecondaryWindow
	if secondaryWindow == nil {
		secondaryWindow = rateLimit.SecondaryWindow2
	}

	var fiveHourWindow *codexComparableWindow
	var weeklyWindow *codexComparableWindow
	for _, window := range []*codexComparableWindow{primaryWindow, secondaryWindow} {
		if window == nil {
			continue
		}
		windowSeconds := codexComparableWindowSeconds(window)
		switch windowSeconds {
		case comparableQuotaCodexFiveHourSeconds:
			if fiveHourWindow == nil {
				fiveHourWindow = window
			}
		case comparableQuotaCodexWeeklySeconds:
			if weeklyWindow == nil {
				weeklyWindow = window
			}
		}
	}
	if fiveHourWindow == nil {
		fiveHourWindow = primaryWindow
	}
	if weeklyWindow == nil && secondaryWindow != fiveHourWindow {
		weeklyWindow = secondaryWindow
	}
	return fiveHourWindow, weeklyWindow
}

func buildCodexComparableWindow(id string, window *codexComparableWindow, limitReached bool, allowed *bool) ComparableQuotaWindow {
	comparable := ComparableQuotaWindow{
		ID:        id,
		Available: allowed == nil || *allowed,
	}
	if window == nil {
		return comparable
	}

	usedPercent := derefFloat(window.UsedPercent, window.UsedPercent2)
	if math.IsNaN(usedPercent) || math.IsInf(usedPercent, 0) {
		usedPercent = 0
	}
	if usedPercent <= 0 && limitReached {
		usedPercent = 100
	}
	hasValue := window.UsedPercent != nil || window.UsedPercent2 != nil || limitReached || (allowed != nil && !*allowed)
	if hasValue {
		comparable.HasValue = true
		comparable.UsedPercent = clampComparableUsedPercent(usedPercent)
	}
	resetAtUnix := derefInt64(window.ResetAt, window.ResetAt2)
	if resetAtUnix > 0 {
		comparable.ResetAt = time.Unix(resetAtUnix, 0).UTC()
	}
	comparable.WindowSeconds = codexComparableWindowSeconds(window)
	return comparable
}

func codexComparableWindowSeconds(window *codexComparableWindow) int {
	if window == nil {
		return 0
	}
	if window.LimitWindowSeconds != nil {
		return *window.LimitWindowSeconds
	}
	if window.LimitWindowSeconds2 != nil {
		return *window.LimitWindowSeconds2
	}
	return 0
}

func clampComparableUsedPercent(value float64) float64 {
	switch {
	case math.IsNaN(value), math.IsInf(value, 0):
		return 0
	case value < 0:
		return 0
	case value > 100:
		return 100
	default:
		return value
	}
}

func derefBool(values ...*bool) bool {
	for _, value := range values {
		if value != nil {
			return *value
		}
	}
	return false
}

func derefFloat(values ...*float64) float64 {
	for _, value := range values {
		if value != nil {
			return *value
		}
	}
	return 0
}

func derefInt64(values ...*int64) int64 {
	for _, value := range values {
		if value != nil {
			return *value
		}
	}
	return 0
}
