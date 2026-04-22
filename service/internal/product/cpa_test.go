package product

import (
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
