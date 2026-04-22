package product

import (
	"os"
	"path/filepath"
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
  panel-github-repository: "https://github.com/Blackblock-inc/CPA-UV"
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
