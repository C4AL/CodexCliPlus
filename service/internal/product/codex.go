package product

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type CodexMode string

const (
	CodexModeOfficial CodexMode = "official"
	CodexModeCPA      CodexMode = "cpa"
)

type CodexModeState struct {
	ProductName string    `json:"productName"`
	Mode        CodexMode `json:"mode"`
	Message     string    `json:"message"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

type CodexShimResolution struct {
	Mode         CodexMode `json:"mode"`
	ModeFile     string    `json:"modeFile"`
	ShimPath     string    `json:"shimPath"`
	TargetPath   string    `json:"targetPath"`
	TargetExists bool      `json:"targetExists"`
	Message      string    `json:"message"`
	UpdatedAt    time.Time `json:"updatedAt"`
}

func ParseCodexMode(value string) (CodexMode, error) {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case string(CodexModeOfficial):
		return CodexModeOfficial, nil
	case string(CodexModeCPA):
		return CodexModeCPA, nil
	default:
		return "", fmt.Errorf("不支持的 Codex 模式：%s", value)
	}
}

func normalizeCodexMode(value string) CodexMode {
	mode, err := ParseCodexMode(value)
	if err != nil {
		return CodexModeOfficial
	}

	return mode
}

func CodexShimPath(layout Layout) string {
	return filepath.Join(layout.InstallRoot, "codex.exe")
}

func CodexRuntimeCandidates(layout Layout, mode CodexMode) []string {
	var candidates []string

	switch mode {
	case CodexModeCPA:
		if override := os.Getenv("CPAD_CODEX_CPA_EXECUTABLE"); override != "" {
			candidates = append(candidates, filepath.Clean(override))
		}
		candidates = append(candidates,
			filepath.Join(layout.Directories["cpaRuntime"], "codex.exe"),
			filepath.Join(layout.Directories["cpaRuntime"], "codex.cmd"),
			filepath.Join(layout.Directories["cpaRuntime"], "bin", "codex.exe"),
			filepath.Join(layout.Directories["cpaRuntime"], "bin", "codex.cmd"),
		)
	default:
		if override := os.Getenv("CPAD_CODEX_OFFICIAL_EXECUTABLE"); override != "" {
			candidates = append(candidates, filepath.Clean(override))
		}
		candidates = append(candidates,
			filepath.Join(layout.Directories["codexRuntime"], "codex.exe"),
			filepath.Join(layout.Directories["codexRuntime"], "codex.cmd"),
			filepath.Join(layout.Directories["codexRuntime"], "bin", "codex.exe"),
			filepath.Join(layout.Directories["codexRuntime"], "bin", "codex.cmd"),
		)
	}

	return candidates
}

func (layout Layout) EnsureCodexModeState() (*CodexModeState, error) {
	state, err := layout.ReadCodexMode()
	if err != nil {
		return nil, err
	}

	if state != nil {
		return state, nil
	}

	if err := layout.WriteCodexMode(CodexModeOfficial, "Codex 模式已初始化为官方模式；切换仅影响之后新启动的 Codex 会话。"); err != nil {
		return nil, err
	}

	return layout.ReadCodexMode()
}

func (layout Layout) WriteCodexMode(mode CodexMode, message string) error {
	state := CodexModeState{
		ProductName: ProductName,
		Mode:        mode,
		Message:     message,
		UpdatedAt:   time.Now().UTC(),
	}

	content, err := json.MarshalIndent(state, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["codexMode"], content, 0o644)
}

func (layout Layout) ReadCodexMode() (*CodexModeState, error) {
	content, err := os.ReadFile(layout.Files["codexMode"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var state CodexModeState
	if err := json.Unmarshal(content, &state); err != nil {
		return nil, err
	}

	state.Mode = normalizeCodexMode(string(state.Mode))
	return &state, nil
}

func ResolveCodexShim(layout Layout) (CodexShimResolution, error) {
	state, err := layout.EnsureCodexModeState()
	if err != nil {
		return CodexShimResolution{}, err
	}

	targetPath, targetExists := resolveFirstExisting(CodexRuntimeCandidates(layout, state.Mode))

	return CodexShimResolution{
		Mode:         state.Mode,
		ModeFile:     layout.Files["codexMode"],
		ShimPath:     CodexShimPath(layout),
		TargetPath:   targetPath,
		TargetExists: targetExists,
		Message:      state.Message,
		UpdatedAt:    state.UpdatedAt,
	}, nil
}

func resolveFirstExisting(candidates []string) (string, bool) {
	if len(candidates) == 0 {
		return "", false
	}

	for _, candidate := range candidates {
		if candidate == "" {
			continue
		}

		if _, err := os.Stat(candidate); err == nil {
			return candidate, true
		}
	}

	return candidates[0], false
}
