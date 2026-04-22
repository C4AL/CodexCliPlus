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
	Mode          CodexMode `json:"mode"`
	ModeFile      string    `json:"modeFile"`
	ShimPath      string    `json:"shimPath"`
	TargetPath    string    `json:"targetPath"`
	TargetExists  bool      `json:"targetExists"`
	LaunchArgs    []string  `json:"launchArgs"`
	LaunchReady   bool      `json:"launchReady"`
	LaunchMessage string    `json:"launchMessage"`
	Message       string    `json:"message"`
	UpdatedAt     time.Time `json:"updatedAt"`
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
	officialCandidates := []string{
		filepath.Join(layout.Directories["codexRuntime"], "codex.exe"),
		filepath.Join(layout.Directories["codexRuntime"], "codex.cmd"),
		filepath.Join(layout.Directories["codexRuntime"], "bin", "codex.exe"),
		filepath.Join(layout.Directories["codexRuntime"], "bin", "codex.cmd"),
	}

	switch mode {
	case CodexModeCPA:
		if override := os.Getenv("CPAD_CODEX_CPA_EXECUTABLE"); override != "" {
			candidates = append(candidates, filepath.Clean(override))
		}
		candidates = append(candidates, officialCandidates...)
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
		candidates = append(candidates, officialCandidates...)
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
	launchArgs, launchReady, launchMessage := resolveCodexLaunchPlan(layout, state.Mode, targetExists)

	return CodexShimResolution{
		Mode:          state.Mode,
		ModeFile:      layout.Files["codexMode"],
		ShimPath:      CodexShimPath(layout),
		TargetPath:    targetPath,
		TargetExists:  targetExists,
		LaunchArgs:    launchArgs,
		LaunchReady:   launchReady,
		LaunchMessage: launchMessage,
		Message:       state.Message,
		UpdatedAt:     state.UpdatedAt,
	}, nil
}

func resolveCodexLaunchPlan(layout Layout, mode CodexMode, targetExists bool) ([]string, bool, string) {
	if !targetExists {
		return nil, false, "当前模式对应的 Codex 目标运行时不存在。"
	}

	if mode != CodexModeCPA {
		return nil, true, "官方模式将直接调用受控 Codex Runtime。"
	}

	insight, err := inspectCPARuntimeConfig(CPAManagedConfigPath(layout))
	if err != nil {
		return nil, false, fmt.Sprintf("CPA 模式缺少可解析的运行时配置：%v", err)
	}
	if !insight.CodexAppServerProxyEnabled {
		return nil, false, "CPA 模式依赖 codex-app-server-proxy；当前配置尚未启用。"
	}

	return []string{"--remote", insight.CodexRemoteURL}, true, fmt.Sprintf(
		"CPA 模式将通过 %s 连接受控 CPA Runtime。",
		insight.CodexRemoteURL,
	)
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
