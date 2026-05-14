package config

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSaveConfigPreserveCommentsUpdateNestedScalarUpdatesExistingFile(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "config.yaml")
	original := []byte("# management\nremote-management:\n  secret-key: old\n")
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create config file: %v", err)
	}

	if err := SaveConfigPreserveCommentsUpdateNestedScalar(path, []string{"remote-management", "secret-key"}, "new"); err != nil {
		t.Fatalf("SaveConfigPreserveCommentsUpdateNestedScalar error = %v, want nil", err)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read config file: %v", err)
	}
	text := string(raw)
	if !strings.Contains(text, "# management") {
		t.Fatalf("config file = %q, want original comment preserved", text)
	}
	if !strings.Contains(text, "secret-key: new") {
		t.Fatalf("config file = %q, want updated secret-key", text)
	}
}

func TestSaveConfigPreserveCommentsUpdatesExistingFile(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "config.yaml")
	original := []byte("# debug flag\ndebug: false\n")
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create config file: %v", err)
	}

	if err := SaveConfigPreserveComments(path, &Config{Debug: true}); err != nil {
		t.Fatalf("SaveConfigPreserveComments error = %v, want nil", err)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read config file: %v", err)
	}
	text := string(raw)
	if !strings.Contains(text, "# debug flag") {
		t.Fatalf("config file = %q, want original comment preserved", text)
	}
	if !strings.Contains(text, "debug: true") {
		t.Fatalf("config file = %q, want updated debug flag", text)
	}
}

func TestWriteConfigFileAtomicallyPreservesExistingFileOnRenameFailure(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "config.yaml")
	original := []byte("debug: false\n")
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create config file: %v", err)
	}

	renameErr := errors.New("rename failed")
	err := writeConfigFileAtomically(path, []byte("debug: true\n"), func(tmpName, target string) error {
		if tmpName == "" {
			t.Fatalf("temp path is empty")
		}
		if target != path {
			t.Fatalf("rename target = %q, want %q", target, path)
		}
		return renameErr
	})
	if !errors.Is(err, renameErr) || !strings.Contains(err.Error(), "rename temp config file") {
		t.Fatalf("writeConfigFileAtomically error = %v, want rename failure", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read original config file: %v", err)
	}
	if string(got) != string(original) {
		t.Fatalf("config file = %q, want original %q", got, original)
	}

	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatalf("failed to list config dir: %v", err)
	}
	if len(entries) != 1 || entries[0].Name() != "config.yaml" {
		t.Fatalf("config dir entries = %v, want only config.yaml", entries)
	}
}
