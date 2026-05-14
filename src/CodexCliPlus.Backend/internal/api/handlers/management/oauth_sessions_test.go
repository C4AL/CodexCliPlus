package management

import (
	"os"
	"path/filepath"
	"testing"
)

func TestWriteOAuthCallbackFileReplacesExistingPayload(t *testing.T) {
	t.Parallel()

	authDir := t.TempDir()
	path := filepath.Join(authDir, ".oauth-codex-state-1.oauth")
	if err := os.WriteFile(path, []byte(`{"code":`), 0o600); err != nil {
		t.Fatalf("seed callback file: %v", err)
	}

	gotPath, err := WriteOAuthCallbackFile(authDir, "openai", "state-1", " auth-code ", "")
	if err != nil {
		t.Fatalf("WriteOAuthCallbackFile returned error: %v", err)
	}
	if gotPath != path {
		t.Fatalf("path = %q, want %q", gotPath, path)
	}

	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read callback file: %v", err)
	}
	payload, err := parseOAuthCallbackPayload(data)
	if err != nil {
		t.Fatalf("parse callback payload: %v", err)
	}
	if payload["code"] != "auth-code" {
		t.Fatalf("code = %q, want auth-code", payload["code"])
	}
	if payload["state"] != "state-1" {
		t.Fatalf("state = %q, want state-1", payload["state"])
	}
	if payload["error"] != "" {
		t.Fatalf("error = %q, want empty", payload["error"])
	}
}
