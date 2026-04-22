package product

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestNormalizeCPAManagedBinaryPathMigratesLegacyBinaryName(t *testing.T) {
	layout := Layout{
		Directories: map[string]string{
			"cpaRuntime": filepath.Join("C:\\CPAD", "runtime", "cpa"),
		},
	}

	got := normalizeCPAManagedBinaryPath(layout, filepath.Join("C:\\legacy", "CPA-UV.exe"))
	want := filepath.Join(layout.Directories["cpaRuntime"], ManagedCPABinaryName)
	if got != want {
		t.Fatalf("expected legacy managed binary to migrate to %q, got %q", want, got)
	}
}

func TestNormalizeCPAManagedBinaryPathPreservesNonLegacyPath(t *testing.T) {
	layout := Layout{
		Directories: map[string]string{
			"cpaRuntime": filepath.Join("C:\\CPAD", "runtime", "cpa"),
		},
	}

	customPath := filepath.Join("D:\\custom", "cpad-custom-runtime.exe")
	got := normalizeCPAManagedBinaryPath(layout, customPath)
	if got != filepath.Clean(customPath) {
		t.Fatalf("expected custom managed binary path to be preserved, got %q", got)
	}
}

func TestInspectCPARuntimeConfigBuildsInsight(t *testing.T) {
	configPath := filepath.Join(t.TempDir(), "config.yaml")
	content := `
host: ""
port: 2723
tls:
  enable: false
remote-management:
  allow-remote: false
  secret-key: "test-secret"
  disable-control-panel: false
  panel-github-repository: "https://github.com/router-for-me/Cli-Proxy-API-Management-Center"
codex-app-server-proxy:
  enable: true
  restrict-to-localhost: true
  codex-bin: "codex"
`
	if err := os.WriteFile(configPath, []byte(content), 0o644); err != nil {
		t.Fatalf("write config: %v", err)
	}

	insight, err := inspectCPARuntimeConfig(configPath)
	if err != nil {
		t.Fatalf("inspect config: %v", err)
	}

	if insight.BaseURL != "http://127.0.0.1:2723" {
		t.Fatalf("BaseURL = %q, want %q", insight.BaseURL, "http://127.0.0.1:2723")
	}
	if insight.ManagementURL != "http://127.0.0.1:2723/management.html" {
		t.Fatalf("ManagementURL = %q", insight.ManagementURL)
	}
	if !insight.ManagementEnabled {
		t.Fatalf("ManagementEnabled = false, want true")
	}
	if insight.PanelRepository != ResolveManagedCPAPanelRepository() {
		t.Fatalf("PanelRepository = %q, want %q", insight.PanelRepository, ResolveManagedCPAPanelRepository())
	}
	if !insight.CodexAppServerProxyEnabled {
		t.Fatalf("CodexAppServerProxyEnabled = false, want true")
	}
	if insight.CodexRemoteURL != "ws://127.0.0.1:2723" {
		t.Fatalf("CodexRemoteURL = %q", insight.CodexRemoteURL)
	}
}

func TestNormalizeCPARuntimeProbeHostFallsBackToLoopback(t *testing.T) {
	for _, raw := range []string{"", "0.0.0.0", "::", "::1", "localhost"} {
		if got := normalizeCPARuntimeProbeHost(raw); got != "127.0.0.1" {
			t.Fatalf("normalizeCPARuntimeProbeHost(%q) = %q, want %q", raw, got, "127.0.0.1")
		}
	}
}

func TestReadCPARuntimeStateMigratesLegacyOverlaySourceRoot(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		Directories: map[string]string{
			"cpaRuntime":           filepath.Join(rootDir, "runtime", "cpa"),
			"officialCoreBaseline": filepath.Join(rootDir, "sources", "official-backend"),
		},
		Files: map[string]string{
			"cpaRuntimeState": filepath.Join(rootDir, "data", "cpa-runtime.json"),
			"cpaRuntimeLog":   filepath.Join(rootDir, "logs", "cpa-runtime.log"),
		},
	}
	t.Setenv("CPAD_CPA_SOURCE_ROOT", layout.Directories["officialCoreBaseline"])

	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", directory, err)
		}
	}
	for _, filePath := range layout.Files {
		if err := os.MkdirAll(filepath.Dir(filePath), 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", filepath.Dir(filePath), err)
		}
	}

	runtimeStatePath := layout.Files["cpaRuntimeState"]
	runtimeStateContent := `{"sourceRoot":"` + filepath.ToSlash(filepath.Join(rootDir, "sources", "cpa-uv-overlay")) + `"}`
	if err := os.WriteFile(runtimeStatePath, []byte(runtimeStateContent), 0o644); err != nil {
		t.Fatalf("write runtime state: %v", err)
	}

	state, err := layout.ReadCPARuntimeState()
	if err != nil {
		t.Fatalf("ReadCPARuntimeState: %v", err)
	}

	if state.SourceRoot != layout.Directories["officialCoreBaseline"] {
		t.Fatalf("SourceRoot = %q, want %q", state.SourceRoot, layout.Directories["officialCoreBaseline"])
	}
}

func TestResolveCPARuntimeNormalizesLegacyPanelRepository(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		Directories: map[string]string{
			"cpaRuntime":           filepath.Join(rootDir, "runtime", "cpa"),
			"officialCoreBaseline": filepath.Join(rootDir, "sources", "official-backend"),
		},
		Files: map[string]string{
			"cpaRuntimeState": filepath.Join(rootDir, "data", "cpa-runtime.json"),
			"cpaRuntimeLog":   filepath.Join(rootDir, "logs", "cpa-runtime.log"),
		},
	}

	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", directory, err)
		}
	}
	for _, filePath := range layout.Files {
		if err := os.MkdirAll(filepath.Dir(filePath), 0o755); err != nil {
			t.Fatalf("mkdir %s: %v", filepath.Dir(filePath), err)
		}
	}

	if err := os.WriteFile(layout.Files["cpaRuntimeState"], []byte(`{"sourceRoot":"`+filepath.ToSlash(layout.Directories["officialCoreBaseline"])+`"}`), 0o644); err != nil {
		t.Fatalf("write runtime state: %v", err)
	}

	configPath := CPAManagedConfigPath(layout)
	configContent := `
host: ""
remote-management:
  secret-key: "test-secret"
  panel-github-repository: "https://github.com/Blackblock-inc/CPA-UV"
custom-setting: "keep-me"
`
	if err := os.WriteFile(configPath, []byte(configContent), 0o644); err != nil {
		t.Fatalf("write config: %v", err)
	}

	status, err := ResolveCPARuntime(layout)
	if err != nil {
		t.Fatalf("ResolveCPARuntime: %v", err)
	}

	if status.ConfigInsight.PanelRepository != ResolveManagedCPAPanelRepository() {
		t.Fatalf("PanelRepository = %q, want %q", status.ConfigInsight.PanelRepository, ResolveManagedCPAPanelRepository())
	}

	rewrittenConfig, err := os.ReadFile(configPath)
	if err != nil {
		t.Fatalf("read config: %v", err)
	}

	rewrittenText := string(rewrittenConfig)
	if strings.Contains(rewrittenText, "Blackblock-inc/CPA-UV") {
		t.Fatalf("expected legacy panel repository to be removed, got %s", rewrittenText)
	}
	if !strings.Contains(rewrittenText, ResolveManagedCPAPanelRepository()) {
		t.Fatalf("expected rewritten config to contain %q, got %s", ResolveManagedCPAPanelRepository(), rewrittenText)
	}
	if !strings.Contains(rewrittenText, `custom-setting: "keep-me"`) {
		t.Fatalf("expected unrelated config to be preserved, got %s", rewrittenText)
	}
}
