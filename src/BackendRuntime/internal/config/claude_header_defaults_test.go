package config

import (
	"os"
	"path/filepath"
	"testing"
)

func TestLoadConfigOptional_CodexCliPlusPrunesClaudeHeaderDefaults(t *testing.T) {
	dir := t.TempDir()
	configPath := filepath.Join(dir, "config.yaml")
	configYAML := []byte(`
claude-header-defaults:
  user-agent: "  claude-cli/2.1.70 (external, cli)  "
  package-version: "  0.80.0  "
  runtime-version: "  v24.5.0  "
  os: "  MacOS  "
  arch: "  arm64  "
  timeout: "  900  "
  stabilize-device-profile: false
`)
	if err := os.WriteFile(configPath, configYAML, 0o600); err != nil {
		t.Fatalf("failed to write config: %v", err)
	}

	cfg, err := LoadConfigOptional(configPath, false)
	if err != nil {
		t.Fatalf("LoadConfigOptional() error = %v", err)
	}

	if got := cfg.ClaudeHeaderDefaults.UserAgent; got != "" {
		t.Fatalf("UserAgent = %q, want empty after GPT-only pruning", got)
	}
	if got := cfg.ClaudeHeaderDefaults.PackageVersion; got != "" {
		t.Fatalf("PackageVersion = %q, want empty after GPT-only pruning", got)
	}
	if got := cfg.ClaudeHeaderDefaults.RuntimeVersion; got != "" {
		t.Fatalf("RuntimeVersion = %q, want empty after GPT-only pruning", got)
	}
	if got := cfg.ClaudeHeaderDefaults.OS; got != "" {
		t.Fatalf("OS = %q, want empty after GPT-only pruning", got)
	}
	if got := cfg.ClaudeHeaderDefaults.Arch; got != "" {
		t.Fatalf("Arch = %q, want empty after GPT-only pruning", got)
	}
	if got := cfg.ClaudeHeaderDefaults.Timeout; got != "" {
		t.Fatalf("Timeout = %q, want empty after GPT-only pruning", got)
	}
	if cfg.ClaudeHeaderDefaults.StabilizeDeviceProfile != nil {
		t.Fatal("StabilizeDeviceProfile = non-nil, want nil after GPT-only pruning")
	}
}
