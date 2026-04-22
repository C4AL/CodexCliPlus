package auth

import (
	"testing"
	"time"
)

func TestBuildComparableQuotaPoolSummary(t *testing.T) {
	now := time.Now().UTC()
	manager := &Manager{
		auths: map[string]*Auth{
			"ready": {
				ID:       "ready",
				Provider: "codex",
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now,
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: true},
							ComparableQuotaWindowWeekly:   {HasValue: true, Available: true},
						},
					},
				},
			},
			"exhausted": {
				ID:       "exhausted",
				Provider: "codex",
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now,
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: false},
						},
					},
				},
			},
			"stale": {
				ID:       "stale",
				Provider: "codex",
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now.Add(-comparableQuotaSnapshotStaleAfter - time.Minute),
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: true},
						},
					},
				},
			},
			"disabled": {
				ID:       "disabled",
				Provider: "codex",
				Disabled: true,
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now,
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: true},
						},
					},
				},
			},
			"other": {
				ID:       "other",
				Provider: "gemini",
			},
		},
	}

	summary, ok := manager.BuildComparableQuotaPoolSummary("codex")
	if !ok {
		t.Fatalf("expected summary")
	}
	if summary.TotalCount != 4 {
		t.Fatalf("expected total count 4, got %d", summary.TotalCount)
	}
	if summary.IncludedCount != 2 {
		t.Fatalf("expected included count 2, got %d", summary.IncludedCount)
	}
	if summary.ReadyCount != 1 {
		t.Fatalf("expected ready count 1, got %d", summary.ReadyCount)
	}
	if summary.ExhaustedCount != 1 {
		t.Fatalf("expected exhausted count 1, got %d", summary.ExhaustedCount)
	}
	if summary.PendingCount != 1 {
		t.Fatalf("expected pending count 1, got %d", summary.PendingCount)
	}
	if summary.DisabledCount != 1 {
		t.Fatalf("expected disabled count 1, got %d", summary.DisabledCount)
	}
}

func TestBuildComparableQuotaAggregateSkipsDisabledAuths(t *testing.T) {
	now := time.Now().UTC()
	manager := &Manager{
		auths: map[string]*Auth{
			"ready": {
				ID:       "ready",
				Provider: "codex",
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now,
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: true, UsedPercent: 20},
						},
					},
				},
			},
			"disabled": {
				ID:       "disabled",
				Provider: "codex",
				Disabled: true,
				Quota: QuotaState{
					Comparable: &ComparableQuotaSnapshot{
						UpdatedAt: now,
						Windows: map[string]ComparableQuotaWindow{
							ComparableQuotaWindowFiveHour: {HasValue: true, Available: true, UsedPercent: 80},
						},
					},
				},
			},
		},
	}

	aggregate, ok := manager.BuildComparableQuotaAggregate("codex", "")
	if !ok {
		t.Fatalf("expected aggregate")
	}
	if aggregate.SampleCount != 1 {
		t.Fatalf("expected sample count 1, got %d", aggregate.SampleCount)
	}
	window := aggregate.Windows[ComparableQuotaWindowFiveHour]
	if window.UsedPercent != 20 {
		t.Fatalf("expected used percent 20, got %v", window.UsedPercent)
	}
}
