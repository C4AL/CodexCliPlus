package store

import (
	"fmt"
	"os"
	"path/filepath"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/misc"
)

func writeLocalMirrorFile(path string, data []byte) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return fmt.Errorf("prepare local mirror directory: %w", err)
	}
	if err := misc.WriteFileAtomically(path, data, 0o600); err != nil {
		return fmt.Errorf("write local mirror file: %w", err)
	}
	return nil
}
