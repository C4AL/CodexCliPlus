package product

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestCodexRuntimeCandidatesUseOfficialRuntimeForCPAMode(t *testing.T) {
	layout := Layout{
		Directories: map[string]string{
			"codexRuntime": filepath.Join("C:\\CPAD", "runtime", "codex"),
			"cpaRuntime":   filepath.Join("C:\\CPAD", "runtime", "cpa"),
		},
	}

	candidates := CodexRuntimeCandidates(layout, CodexModeCPA)
	if len(candidates) == 0 {
		t.Fatal("expected candidates for cpa mode")
	}

	want := filepath.Join(layout.Directories["codexRuntime"], "codex.exe")
	if candidates[0] != want {
		t.Fatalf("first candidate = %q, want %q", candidates[0], want)
	}
}

func TestResolveCodexShimBuildsRemoteLaunchPlanForCPAMode(t *testing.T) {
	rootDir := t.TempDir()
	overlaySourceRoot := filepath.Join(rootDir, "sources", "cpa-uv-overlay")
	t.Setenv("CPAD_CPA_SOURCE_ROOT", overlaySourceRoot)
	layout := Layout{
		InstallRoot: rootDir,
		Directories: map[string]string{
			"codexRuntime": filepath.Join(rootDir, "runtime", "codex"),
			"cpaRuntime":   filepath.Join(rootDir, "runtime", "cpa"),
		},
		Files: map[string]string{
			"codexMode": filepath.Join(rootDir, "data", "codex-mode.json"),
		},
	}

	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", directory, err)
		}
	}
	if err := os.MkdirAll(overlaySourceRoot, 0o755); err != nil {
		t.Fatalf("mkdir overlay source root: %v", err)
	}
	if err := os.MkdirAll(filepath.Dir(layout.Files["codexMode"]), 0o755); err != nil {
		t.Fatalf("mkdir codex state dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(layout.Directories["codexRuntime"], "codex.exe"), []byte("dummy"), 0o644); err != nil {
		t.Fatalf("write official codex runtime: %v", err)
	}
	configPath := filepath.Join(layout.Directories["cpaRuntime"], "config.yaml")
	configContent := `
host: ""
port: 2723
codex-app-server-proxy:
  enable: true
`
	if err := os.WriteFile(configPath, []byte(configContent), 0o644); err != nil {
		t.Fatalf("write cpa config: %v", err)
	}
	if err := layout.WriteCodexMode(CodexModeCPA, "switch"); err != nil {
		t.Fatalf("write codex mode: %v", err)
	}

	resolution, err := ResolveCodexShim(layout)
	if err != nil {
		t.Fatalf("ResolveCodexShim: %v", err)
	}

	if !resolution.TargetExists {
		t.Fatal("expected official codex runtime candidate to exist")
	}
	if !resolution.LaunchReady {
		t.Fatalf("expected launch to be ready, got message: %s", resolution.LaunchMessage)
	}
	if len(resolution.LaunchArgs) != 2 || resolution.LaunchArgs[0] != "--remote" || resolution.LaunchArgs[1] != "ws://127.0.0.1:2723" {
		t.Fatalf("unexpected launch args: %#v", resolution.LaunchArgs)
	}
}

func TestResolveCodexShimBlocksRemoteLaunchForOfficialSource(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		InstallRoot: rootDir,
		Directories: map[string]string{
			"codexRuntime":         filepath.Join(rootDir, "runtime", "codex"),
			"cpaRuntime":           filepath.Join(rootDir, "runtime", "cpa"),
			"officialCoreBaseline": filepath.Join(rootDir, "sources", "official-backend"),
		},
		Files: map[string]string{
			"codexMode": filepath.Join(rootDir, "data", "codex-mode.json"),
		},
	}

	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", directory, err)
		}
	}
	if err := os.MkdirAll(filepath.Dir(layout.Files["codexMode"]), 0o755); err != nil {
		t.Fatalf("mkdir codex state dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(layout.Directories["codexRuntime"], "codex.exe"), []byte("dummy"), 0o644); err != nil {
		t.Fatalf("write official codex runtime: %v", err)
	}
	configPath := filepath.Join(layout.Directories["cpaRuntime"], "config.yaml")
	configContent := `
host: ""
port: 2723
codex-app-server-proxy:
  enable: true
`
	if err := os.WriteFile(configPath, []byte(configContent), 0o644); err != nil {
		t.Fatalf("write cpa config: %v", err)
	}
	if err := layout.WriteCodexMode(CodexModeCPA, "switch"); err != nil {
		t.Fatalf("write codex mode: %v", err)
	}

	resolution, err := ResolveCodexShim(layout)
	if err != nil {
		t.Fatalf("ResolveCodexShim: %v", err)
	}

	if resolution.LaunchReady {
		t.Fatalf("expected launch to be blocked, got message: %s", resolution.LaunchMessage)
	}
	if len(resolution.LaunchArgs) != 0 {
		t.Fatalf("expected empty launch args, got %#v", resolution.LaunchArgs)
	}
	if !strings.Contains(resolution.LaunchMessage, "不再直接把本地 Codex 接入开发版 CPAD 后端") {
		t.Fatalf("unexpected launch message: %s", resolution.LaunchMessage)
	}
}

func TestResolveCodexShimReturnsEmptyLaunchArgsSliceWhenTargetMissing(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		InstallRoot: rootDir,
		Directories: map[string]string{
			"codexRuntime": filepath.Join(rootDir, "runtime", "codex"),
			"cpaRuntime":   filepath.Join(rootDir, "runtime", "cpa"),
		},
		Files: map[string]string{
			"codexMode": filepath.Join(rootDir, "data", "codex-mode.json"),
		},
	}

	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", directory, err)
		}
	}
	if err := os.MkdirAll(filepath.Dir(layout.Files["codexMode"]), 0o755); err != nil {
		t.Fatalf("mkdir codex state dir: %v", err)
	}
	if err := layout.WriteCodexMode(CodexModeOfficial, "switch"); err != nil {
		t.Fatalf("write codex mode: %v", err)
	}

	resolution, err := ResolveCodexShim(layout)
	if err != nil {
		t.Fatalf("ResolveCodexShim: %v", err)
	}

	if resolution.LaunchArgs == nil {
		t.Fatal("expected launch args to be an empty slice, got nil")
	}
	if len(resolution.LaunchArgs) != 0 {
		t.Fatalf("expected empty launch args, got %#v", resolution.LaunchArgs)
	}
}
