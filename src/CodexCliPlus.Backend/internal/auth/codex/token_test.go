package codex

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSaveTokenToFileWritesJSONWithMetadata(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "auth.json")
	storage := &CodexTokenStorage{
		AccessToken:  "access-token",
		RefreshToken: "refresh-token",
		Email:        "user@example.com",
		Metadata: map[string]any{
			"project_id": "project-a",
		},
	}

	if err := storage.SaveTokenToFile(path); err != nil {
		t.Fatalf("SaveTokenToFile returned error: %v", err)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read token file: %v", err)
	}

	var got map[string]any
	if err = json.Unmarshal(raw, &got); err != nil {
		t.Fatalf("token file is invalid JSON: %v", err)
	}
	if got["type"] != "codex" {
		t.Fatalf("type = %q, want codex", got["type"])
	}
	if got["access_token"] != "access-token" {
		t.Fatalf("access_token = %q, want access-token", got["access_token"])
	}
	if got["project_id"] != "project-a" {
		t.Fatalf("project_id = %q, want project-a", got["project_id"])
	}
}

func TestSaveTokenToFilePreservesExistingFileOnMarshalFailure(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	original := []byte(`{"access_token":"old"}`)
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create auth file: %v", err)
	}

	storage := &CodexTokenStorage{
		AccessToken: "new-token",
		Metadata: map[string]any{
			"unmarshalable": make(chan int),
		},
	}

	err := storage.SaveTokenToFile(path)
	if err == nil || !strings.Contains(err.Error(), "failed to encode token JSON") {
		t.Fatalf("SaveTokenToFile error = %v, want JSON encode failure", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read original auth file: %v", err)
	}
	if string(got) != string(original) {
		t.Fatalf("auth file = %s, want original %s", got, original)
	}
}
