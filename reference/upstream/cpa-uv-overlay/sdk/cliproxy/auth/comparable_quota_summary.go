package auth

import (
	"strings"
	"time"
)

// ComparableQuotaPoolSummary captures quota-oriented pool counts for one provider.
type ComparableQuotaPoolSummary struct {
	Provider       string    `json:"-"`
	TotalCount     int       `json:"-"`
	IncludedCount  int       `json:"-"`
	ReadyCount     int       `json:"-"`
	ExhaustedCount int       `json:"-"`
	PendingCount   int       `json:"-"`
	DisabledCount  int       `json:"-"`
	UpdatedAt      time.Time `json:"-"`
}

// BuildComparableQuotaPoolSummary summarizes the current comparable-quota pool state.
func (m *Manager) BuildComparableQuotaPoolSummary(provider string) (*ComparableQuotaPoolSummary, bool) {
	if m == nil {
		return nil, false
	}
	provider = strings.ToLower(strings.TrimSpace(provider))
	now := time.Now()

	m.mu.RLock()
	defer m.mu.RUnlock()

	summary := &ComparableQuotaPoolSummary{Provider: provider}
	found := false
	for _, auth := range m.auths {
		if auth == nil {
			continue
		}
		if provider != "" && strings.ToLower(strings.TrimSpace(auth.Provider)) != provider {
			continue
		}

		found = true
		summary.TotalCount++

		if auth.Disabled || auth.Status == StatusDisabled {
			summary.DisabledCount++
			continue
		}

		snapshot := auth.Quota.Comparable
		if !comparableQuotaSnapshotFresh(snapshot, now) || !comparableQuotaSnapshotHasValue(snapshot) {
			summary.PendingCount++
			continue
		}

		summary.IncludedCount++
		if snapshot.UpdatedAt.After(summary.UpdatedAt) {
			summary.UpdatedAt = snapshot.UpdatedAt
		}

		if comparableQuotaSnapshotAvailable(snapshot) {
			summary.ReadyCount++
			continue
		}
		summary.ExhaustedCount++
	}

	if !found {
		return nil, false
	}
	return summary, true
}

func comparableQuotaSnapshotHasValue(snapshot *ComparableQuotaSnapshot) bool {
	if snapshot == nil {
		return false
	}
	for _, window := range snapshot.Windows {
		if window.HasValue {
			return true
		}
	}
	return false
}

func comparableQuotaSnapshotAvailable(snapshot *ComparableQuotaSnapshot) bool {
	if snapshot == nil {
		return false
	}
	hasValue := false
	for _, window := range snapshot.Windows {
		if !window.HasValue {
			continue
		}
		hasValue = true
		if !window.Available {
			return false
		}
	}
	return hasValue
}
