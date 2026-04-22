package chatgpt

import (
	"testing"
	"time"

	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

func TestBuildUsagePayloadIncludesPoolAdditionalRateLimit(t *testing.T) {
	now := time.Now().UTC().Add(2 * time.Hour)
	aggregate := &coreauth.ComparableQuotaAggregate{
		PlanType:    "plus",
		SampleCount: 2,
		UpdatedAt:   now,
		Windows: map[string]coreauth.ComparableQuotaWindow{
			coreauth.ComparableQuotaWindowFiveHour: {
				ID:            coreauth.ComparableQuotaWindowFiveHour,
				HasValue:      true,
				Available:     true,
				UsedPercent:   18,
				WindowSeconds: 18_000,
				ResetAt:       now,
			},
			coreauth.ComparableQuotaWindowWeekly: {
				ID:            coreauth.ComparableQuotaWindowWeekly,
				HasValue:      true,
				Available:     true,
				UsedPercent:   25,
				WindowSeconds: 604_800,
				ResetAt:       now,
			},
		},
	}
	summary := &coreauth.ComparableQuotaPoolSummary{
		TotalCount:     4,
		IncludedCount:  2,
		ReadyCount:     1,
		ExhaustedCount: 1,
		PendingCount:   1,
		DisabledCount:  1,
	}

	payload := buildUsagePayload("", aggregate, summary)
	if payload.PlanType != "plus" {
		t.Fatalf("expected plan type plus, got %q", payload.PlanType)
	}
	if len(payload.AdditionalRateLimits) != 1 {
		t.Fatalf("expected one additional rate limit, got %d", len(payload.AdditionalRateLimits))
	}

	additional := payload.AdditionalRateLimits[0]
	if additional.MeteredFeature != "codex" {
		t.Fatalf("expected metered feature codex, got %q", additional.MeteredFeature)
	}

	wantLabel := "\u0043\u0050\u0041\u53f7\u6c60\u603b\u989d\u5ea6(\u603b\u51714,\u53ef\u75281,\u8017\u5c3d1)"
	if additional.LimitName != wantLabel {
		t.Fatalf("expected limit name %q, got %q", wantLabel, additional.LimitName)
	}
	if additional.RateLimit == nil || additional.RateLimit.PrimaryWindow == nil || additional.RateLimit.SecondaryWindow == nil {
		t.Fatalf("expected primary and secondary windows in additional rate limit")
	}
}
