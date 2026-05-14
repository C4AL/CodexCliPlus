package logging

import (
	"os"
	"path/filepath"
	"testing"
)

func TestIsDirWritableDoesNotOverwriteExistingPermissionProbeFile(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	probePath := filepath.Join(dir, ".perm_test")
	want := []byte("existing probe file\n")
	if err := os.WriteFile(probePath, want, 0o600); err != nil {
		t.Fatalf("write existing probe file: %v", err)
	}

	if !isDirWritable(dir) {
		t.Fatal("isDirWritable returned false, want true")
	}

	got, err := os.ReadFile(probePath)
	if err != nil {
		t.Fatalf("read existing probe file: %v", err)
	}
	if string(got) != string(want) {
		t.Fatalf("existing probe file = %q, want %q", got, want)
	}
}
