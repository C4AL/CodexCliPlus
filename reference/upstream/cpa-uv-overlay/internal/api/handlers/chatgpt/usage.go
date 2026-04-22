package chatgpt

import (
	"math"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
	log "github.com/sirupsen/logrus"
)

type UsageHandler struct {
	authManager *coreauth.Manager
}

type rateLimitStatusPayload struct {
	PlanType             string                  `json:"plan_type"`
	RateLimit            *rateLimitStatusDetails `json:"rate_limit,omitempty"`
	AdditionalRateLimits []additionalRateLimit   `json:"additional_rate_limits,omitempty"`
	RateLimitReachedType *rateLimitReachedType   `json:"rate_limit_reached_type,omitempty"`
}

type rateLimitStatusDetails struct {
	Allowed         bool                     `json:"allowed"`
	LimitReached    bool                     `json:"limit_reached"`
	PrimaryWindow   *rateLimitWindowSnapshot `json:"primary_window,omitempty"`
	SecondaryWindow *rateLimitWindowSnapshot `json:"secondary_window,omitempty"`
}

type rateLimitWindowSnapshot struct {
	UsedPercent        int   `json:"used_percent"`
	LimitWindowSeconds int   `json:"limit_window_seconds"`
	ResetAfterSeconds  int   `json:"reset_after_seconds"`
	ResetAt            int64 `json:"reset_at"`
}

type rateLimitReachedType struct {
	Type string `json:"type"`
}

type additionalRateLimit struct {
	LimitName      string                  `json:"limit_name"`
	MeteredFeature string                  `json:"metered_feature"`
	RateLimit      *rateLimitStatusDetails `json:"rate_limit,omitempty"`
}

func NewUsageHandler(authManager *coreauth.Manager) *UsageHandler {
	return &UsageHandler{authManager: authManager}
}

func (h *UsageHandler) GetUsage(c *gin.Context) {
	requestAccountID := strings.TrimSpace(c.GetHeader("Chatgpt-Account-Id"))
	if h == nil || h.authManager == nil {
		c.JSON(http.StatusOK, buildUsagePayload("", nil, nil))
		return
	}

	if err := h.authManager.RefreshComparableQuotaProvider(c.Request.Context(), "codex", false); err != nil {
		log.WithError(err).Debug("chatgpt usage refresh failed")
	}

	aggregate, _ := h.authManager.BuildComparableQuotaAggregate("codex", requestAccountID)
	poolSummary, _ := h.authManager.BuildComparableQuotaPoolSummary("codex")
	c.JSON(http.StatusOK, buildUsagePayload("", aggregate, poolSummary))
}

func buildUsagePayload(
	planType string,
	aggregate *coreauth.ComparableQuotaAggregate,
	poolSummary *coreauth.ComparableQuotaPoolSummary,
) rateLimitStatusPayload {
	if aggregate != nil {
		planType = aggregate.PlanType
	}
	payload := rateLimitStatusPayload{
		PlanType: normalizePlanType(planType),
	}
	if aggregate == nil {
		return payload
	}

	if rateLimit, limitReachedType := buildRateLimitDetails(aggregate); rateLimit != nil {
		payload.RateLimit = rateLimit
		payload.RateLimitReachedType = limitReachedType
	}
	if rateLimit := buildPoolRateLimitDetails(aggregate); rateLimit != nil {
		payload.AdditionalRateLimits = []additionalRateLimit{{
			LimitName:      buildPoolLimitName(poolSummary),
			MeteredFeature: "codex",
			RateLimit:      rateLimit,
		}}
	}
	return payload
}

func buildPoolRateLimitDetails(aggregate *coreauth.ComparableQuotaAggregate) *rateLimitStatusDetails {
	rateLimit, _ := buildRateLimitDetails(aggregate)
	return rateLimit
}

func buildRateLimitDetails(aggregate *coreauth.ComparableQuotaAggregate) (*rateLimitStatusDetails, *rateLimitReachedType) {
	if aggregate == nil {
		return nil, nil
	}

	primaryComparable, hasPrimary := aggregate.Windows[coreauth.ComparableQuotaWindowFiveHour]
	secondaryComparable, hasSecondary := aggregate.Windows[coreauth.ComparableQuotaWindowWeekly]

	primary := buildRateLimitWindow(primaryComparable)
	secondary := buildRateLimitWindow(secondaryComparable)
	if primary == nil && secondary == nil {
		return nil, nil
	}

	allowed := true
	switch {
	case hasPrimary && primaryComparable.HasValue:
		allowed = primaryComparable.Available
	case hasSecondary && secondaryComparable.HasValue:
		allowed = secondaryComparable.Available
	}

	limitReached := false
	if hasPrimary && primaryComparable.HasValue && !primaryComparable.Available {
		limitReached = true
	}
	if hasSecondary && secondaryComparable.HasValue && !secondaryComparable.Available {
		limitReached = true
	}

	var reachedType *rateLimitReachedType
	if limitReached {
		reachedType = &rateLimitReachedType{Type: "rate_limit_reached"}
	}

	return &rateLimitStatusDetails{
		Allowed:         allowed,
		LimitReached:    limitReached,
		PrimaryWindow:   primary,
		SecondaryWindow: secondary,
	}, reachedType
}

func buildRateLimitWindow(window coreauth.ComparableQuotaWindow) *rateLimitWindowSnapshot {
	if !window.HasValue {
		return nil
	}

	resetAfter := 0
	resetAt := int64(0)
	if !window.ResetAt.IsZero() {
		resetAt = window.ResetAt.UTC().Unix()
		if remaining := time.Until(window.ResetAt); remaining > 0 {
			resetAfter = int(math.Round(remaining.Seconds()))
		}
	}

	return &rateLimitWindowSnapshot{
		UsedPercent:        clampRoundedPercent(window.UsedPercent),
		LimitWindowSeconds: window.WindowSeconds,
		ResetAfterSeconds:  resetAfter,
		ResetAt:            resetAt,
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

func normalizePlanType(planType string) string {
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
		return "unknown"
	}
}

func buildPoolLimitName(summary *coreauth.ComparableQuotaPoolSummary) string {
	if summary == nil {
		return "\u0043\u0050\u0041\u53f7\u6c60\u603b\u989d\u5ea6"
	}

	parts := []string{
		"\u603b\u5171" + intString(summary.TotalCount),
		"\u53ef\u7528" + intString(summary.ReadyCount),
		"\u8017\u5c3d" + intString(summary.ExhaustedCount),
	}

	return "\u0043\u0050\u0041\u53f7\u6c60\u603b\u989d\u5ea6(" + strings.Join(parts, ",") + ")"
}

func intString(value int) string {
	return strconv.Itoa(value)
}
