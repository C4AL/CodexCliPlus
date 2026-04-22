package product

import (
	"encoding/json"
	"errors"
	"os"
	"time"
)

type UpdateSourceStatus struct {
	ID         string    `json:"id"`
	Name       string    `json:"name"`
	Kind       string    `json:"kind"`
	Source     string    `json:"source"`
	CurrentRef string    `json:"currentRef"`
	LatestRef  string    `json:"latestRef"`
	Dirty      bool      `json:"dirty"`
	Available  bool      `json:"available"`
	Message    string    `json:"message"`
	UpdatedAt  time.Time `json:"updatedAt"`
}

type UpdateCenterStatus struct {
	ProductName string               `json:"productName"`
	StateFile   string               `json:"stateFile"`
	Sources     []UpdateSourceStatus `json:"sources"`
	UpdatedAt   time.Time            `json:"updatedAt"`
}

func (layout Layout) ReadUpdateCenterState() (*UpdateCenterStatus, error) {
	content, err := os.ReadFile(layout.Files["updateCenterState"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var state UpdateCenterStatus
	if err := json.Unmarshal(content, &state); err != nil {
		return nil, err
	}

	if state.StateFile == "" {
		state.StateFile = layout.Files["updateCenterState"]
	}

	return &state, nil
}

func (layout Layout) WriteUpdateCenterState(state UpdateCenterStatus) error {
	if state.ProductName == "" {
		state.ProductName = ProductName
	}

	if state.StateFile == "" {
		state.StateFile = layout.Files["updateCenterState"]
	}

	if state.UpdatedAt.IsZero() {
		state.UpdatedAt = time.Now().UTC()
	}

	content, err := json.MarshalIndent(state, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["updateCenterState"], content, 0o644)
}
