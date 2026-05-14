package management

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
)

func TestPutRequestRetryClampsNegativeValue(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg:            &config.Config{RequestRetry: 2},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPut,
		"/v0/management/request-retry",
		strings.NewReader(`{"value":-1}`),
	)

	h.PutRequestRetry(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	if h.cfg.RequestRetry != 0 {
		t.Fatalf("request-retry = %d, want 0", h.cfg.RequestRetry)
	}
}

func TestPutMaxRetryIntervalClampsNegativeValue(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg:            &config.Config{MaxRetryInterval: 30},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPut,
		"/v0/management/max-retry-interval",
		strings.NewReader(`{"value":-1}`),
	)

	h.PutMaxRetryInterval(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	if h.cfg.MaxRetryInterval != 0 {
		t.Fatalf("max-retry-interval = %d, want 0", h.cfg.MaxRetryInterval)
	}
}

func TestLatestReleaseUnexpectedStatusMessageReturnsReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("release body failed")
	message, err := latestReleaseUnexpectedStatusMessage(http.StatusBadGateway, failingLatestReleaseReader{err: readErr})
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read latest release error response body") {
		t.Fatalf("latestReleaseUnexpectedStatusMessage error = %v, want body read failure", err)
	}
	if !strings.Contains(message, "status 502: read latest release error response body") {
		t.Fatalf("message = %q, want status with read failure", message)
	}
}

func TestWriteConfigUpdatesExistingFile(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "config.yaml")
	if err := os.WriteFile(path, []byte("debug: false\n"), 0o644); err != nil {
		t.Fatalf("failed to create config file: %v", err)
	}

	if err := WriteConfig(path, []byte("debug: true\n")); err != nil {
		t.Fatalf("WriteConfig error = %v, want nil", err)
	}

	got, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read config file: %v", err)
	}
	if string(got) != "debug: true\n" {
		t.Fatalf("config file = %q, want updated YAML", got)
	}
}

func TestWriteConfigFileAtomicallyPreservesExistingFileOnRenameFailure(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "config.yaml")
	original := []byte("debug: false\n")
	if err := os.WriteFile(path, original, 0o644); err != nil {
		t.Fatalf("failed to create config file: %v", err)
	}

	renameErr := errors.New("rename failed")
	err := writeConfigFileAtomically(path, []byte("debug: true\n"), 0o644, func(tmpName, target string) error {
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

type failingLatestReleaseReader struct {
	err error
}

func (r failingLatestReleaseReader) Read([]byte) (int, error) {
	return 0, r.err
}
