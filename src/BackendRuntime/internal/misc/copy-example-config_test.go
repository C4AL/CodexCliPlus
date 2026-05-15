package misc

import (
	"os"
	"path/filepath"
	"testing"
)

func TestCopyConfigTemplateCopiesTemplateToNestedPath(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	src := filepath.Join(root, "config.example.yaml")
	dst := filepath.Join(root, "nested", "config.yaml")
	want := []byte("model: gpt-5\n")
	if err := os.WriteFile(src, want, 0o600); err != nil {
		t.Fatalf("write template: %v", err)
	}

	if err := CopyConfigTemplate(src, dst); err != nil {
		t.Fatalf("CopyConfigTemplate returned error: %v", err)
	}

	got, err := os.ReadFile(dst)
	if err != nil {
		t.Fatalf("read copied config: %v", err)
	}
	if string(got) != string(want) {
		t.Fatalf("copied config = %q, want %q", got, want)
	}
}

func TestCopyConfigTemplatePreservesDestinationWhenTemplateReadFails(t *testing.T) {
	t.Parallel()

	root := t.TempDir()
	srcDir := filepath.Join(root, "config.example.yaml")
	dst := filepath.Join(root, "config.yaml")
	existing := []byte("model: existing\n")
	if err := os.Mkdir(srcDir, 0o700); err != nil {
		t.Fatalf("create template directory: %v", err)
	}
	if err := os.WriteFile(dst, existing, 0o600); err != nil {
		t.Fatalf("write existing config: %v", err)
	}

	err := CopyConfigTemplate(srcDir, dst)
	if err == nil {
		t.Fatal("CopyConfigTemplate returned nil, want read error")
	}

	got, errRead := os.ReadFile(dst)
	if errRead != nil {
		t.Fatalf("read existing config: %v", errRead)
	}
	if string(got) != string(existing) {
		t.Fatalf("existing config = %q, want %q", got, existing)
	}
}
