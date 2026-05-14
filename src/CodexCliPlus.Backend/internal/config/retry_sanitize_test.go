package config

import (
	"os"
	"path/filepath"
	"testing"
)

func TestLoadConfigOptionalClampsNegativeRetrySettings(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	configPath := filepath.Join(dir, "config.yaml")
	configYAML := []byte(`
request-retry: -1
max-retry-interval: -5
max-retry-credentials: -2
`)
	if err := os.WriteFile(configPath, configYAML, 0o600); err != nil {
		t.Fatalf("failed to write config: %v", err)
	}

	cfg, err := LoadConfigOptional(configPath, false)
	if err != nil {
		t.Fatalf("LoadConfigOptional() error = %v", err)
	}

	if cfg.RequestRetry != 0 {
		t.Fatalf("RequestRetry = %d, want 0", cfg.RequestRetry)
	}
	if cfg.MaxRetryInterval != 0 {
		t.Fatalf("MaxRetryInterval = %d, want 0", cfg.MaxRetryInterval)
	}
	if cfg.MaxRetryCredentials != 0 {
		t.Fatalf("MaxRetryCredentials = %d, want 0", cfg.MaxRetryCredentials)
	}
}
