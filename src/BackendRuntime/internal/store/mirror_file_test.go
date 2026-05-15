package store

import (
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

func TestWriteLocalMirrorFileCreatesParentAndReplacesExistingFile(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "nested", "auth.json")
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		t.Fatalf("failed to create mirror directory: %v", err)
	}
	if err := os.WriteFile(path, []byte(`{"access_token":"old"}`), 0o644); err != nil {
		t.Fatalf("failed to seed mirror file: %v", err)
	}

	if err := writeLocalMirrorFile(path, []byte(`{"access_token":"new"}`)); err != nil {
		t.Fatalf("writeLocalMirrorFile returned error: %v", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read mirror file: %v", err)
	}
	if string(got) != `{"access_token":"new"}` {
		t.Fatalf("mirror file = %s, want new token", got)
	}

	if runtime.GOOS != "windows" {
		info, errStat := os.Stat(path)
		if errStat != nil {
			t.Fatalf("failed to stat mirror file: %v", errStat)
		}
		if gotPerm := info.Mode().Perm(); gotPerm != 0o600 {
			t.Fatalf("mirror file permissions = %o, want 600", gotPerm)
		}
	}
}

func TestWriteLocalMirrorFileCreatesMissingParent(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "missing", "config.yaml")
	if err := writeLocalMirrorFile(path, []byte("debug: true\n")); err != nil {
		t.Fatalf("writeLocalMirrorFile returned error: %v", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read mirror file: %v", err)
	}
	if string(got) != "debug: true\n" {
		t.Fatalf("mirror file = %q, want config payload", got)
	}
}
