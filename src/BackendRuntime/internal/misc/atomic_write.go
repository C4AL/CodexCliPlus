package misc

import (
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
)

// WriteFileAtomically writes data to a temporary file, syncs it, then renames it into place.
func WriteFileAtomically(path string, data []byte, perm fs.FileMode) error {
	return writeFileAtomically(path, data, perm, os.Rename)
}

func writeFileAtomically(path string, data []byte, perm fs.FileMode, rename func(string, string) error) (err error) {
	tmpFile, err := os.CreateTemp(filepath.Dir(path), ".codexcliplus-*.tmp")
	if err != nil {
		return fmt.Errorf("create temp file: %w", err)
	}

	tmpName := tmpFile.Name()
	closed := false
	defer func() {
		if !closed {
			_ = tmpFile.Close()
		}
		if err != nil {
			_ = os.Remove(tmpName)
		}
	}()

	if _, err = tmpFile.Write(data); err != nil {
		return fmt.Errorf("write temp file: %w", err)
	}
	if err = tmpFile.Chmod(perm); err != nil {
		return fmt.Errorf("chmod temp file: %w", err)
	}
	if err = tmpFile.Sync(); err != nil {
		return fmt.Errorf("sync temp file: %w", err)
	}
	if err = tmpFile.Close(); err != nil {
		closed = true
		return fmt.Errorf("close temp file: %w", err)
	}
	closed = true

	if err = rename(tmpName, path); err != nil {
		return fmt.Errorf("rename temp file: %w", err)
	}
	return nil
}
