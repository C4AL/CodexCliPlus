package misc

import (
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestWriteFileAtomicallyReplacesExistingFile(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	if err := os.WriteFile(path, []byte(`{"access_token":"old"}`), 0o644); err != nil {
		t.Fatalf("failed to create auth file: %v", err)
	}

	if err := WriteFileAtomically(path, []byte(`{"access_token":"new"}`), 0o600); err != nil {
		t.Fatalf("WriteFileAtomically returned error: %v", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read auth file: %v", err)
	}
	if string(got) != `{"access_token":"new"}` {
		t.Fatalf("auth file = %s, want new token", got)
	}

	if runtime.GOOS != "windows" {
		info, errStat := os.Stat(path)
		if errStat != nil {
			t.Fatalf("failed to stat auth file: %v", errStat)
		}
		if gotPerm := info.Mode().Perm(); gotPerm != 0o600 {
			t.Fatalf("auth file permissions = %o, want 600", gotPerm)
		}
	}
}

func TestWriteFileAtomicallyPreservesExistingFileOnRenameFailure(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	original := []byte(`{"access_token":"old"}`)
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create auth file: %v", err)
	}

	renameErr := errors.New("rename failed")
	err := writeFileAtomically(path, []byte(`{"access_token":"new"}`), 0o600, func(tmpName, target string) error {
		if tmpName == "" {
			t.Fatalf("temp path is empty")
		}
		if target != path {
			t.Fatalf("rename target = %q, want %q", target, path)
		}
		return renameErr
	})
	if !errors.Is(err, renameErr) || !strings.Contains(err.Error(), "rename temp file") {
		t.Fatalf("writeFileAtomically error = %v, want rename failure", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read original auth file: %v", err)
	}
	if string(got) != string(original) {
		t.Fatalf("auth file = %s, want original %s", got, original)
	}

	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatalf("failed to list auth dir: %v", err)
	}
	if len(entries) != 1 || entries[0].Name() != "auth.json" {
		t.Fatalf("auth dir entries = %v, want only auth.json", entries)
	}
}
