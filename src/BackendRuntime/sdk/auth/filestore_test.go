package auth

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
)

func TestExtractAccessToken(t *testing.T) {
	t.Parallel()

	tests := []struct {
		name     string
		metadata map[string]any
		expected string
	}{
		{
			"antigravity top-level access_token",
			map[string]any{"access_token": "tok-abc"},
			"tok-abc",
		},
		{
			"gemini nested token.access_token",
			map[string]any{
				"token": map[string]any{"access_token": "tok-nested"},
			},
			"tok-nested",
		},
		{
			"top-level takes precedence over nested",
			map[string]any{
				"access_token": "tok-top",
				"token":        map[string]any{"access_token": "tok-nested"},
			},
			"tok-top",
		},
		{
			"empty metadata",
			map[string]any{},
			"",
		},
		{
			"whitespace-only access_token",
			map[string]any{"access_token": "   "},
			"",
		},
		{
			"wrong type access_token",
			map[string]any{"access_token": 12345},
			"",
		},
		{
			"token is not a map",
			map[string]any{"token": "not-a-map"},
			"",
		},
		{
			"nested whitespace-only",
			map[string]any{
				"token": map[string]any{"access_token": "  "},
			},
			"",
		},
		{
			"fallback to nested when top-level empty",
			map[string]any{
				"access_token": "",
				"token":        map[string]any{"access_token": "tok-fallback"},
			},
			"tok-fallback",
		},
	}

	for _, tt := range tests {
		tt := tt
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()
			got := extractAccessToken(tt.metadata)
			if got != tt.expected {
				t.Errorf("extractAccessToken() = %q, want %q", got, tt.expected)
			}
		})
	}
}

func TestRefreshGeminiAccessTokenReturnsReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("refresh body failed")
	tokenMap := map[string]any{
		"refresh_token": "refresh-token",
		"client_id":     "client-id",
		"client_secret": "client-secret",
		"access_token":  "old-token",
	}
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	token, err := refreshGeminiAccessToken(tokenMap, client)
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read refresh response body") {
		t.Fatalf("refreshGeminiAccessToken error = %v, want body read failure", err)
	}
	if token != "" {
		t.Fatalf("token = %q, want empty", token)
	}
	if got := tokenMap["access_token"]; got != "old-token" {
		t.Fatalf("access_token = %q, want old-token", got)
	}
}

func TestRefreshGeminiAccessTokenReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("refresh body close failed")
	tokenMap := map[string]any{
		"refresh_token": "refresh-token",
		"client_id":     "client-id",
		"client_secret": "client-secret",
		"access_token":  "old-token",
	}
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{"access_token":"new-token"}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	token, err := refreshGeminiAccessToken(tokenMap, client)
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close refresh response body") {
		t.Fatalf("refreshGeminiAccessToken error = %v, want body close failure", err)
	}
	if token != "" {
		t.Fatalf("token = %q, want empty", token)
	}
	if got := tokenMap["access_token"]; got != "old-token" {
		t.Fatalf("access_token = %q, want old-token", got)
	}
}

func TestFileTokenStoreSaveMetadataUpdatesExistingFile(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "auth.json")
	if err := os.WriteFile(path, []byte(`{"type":"demo","label":"old","disabled":false}`), 0o600); err != nil {
		t.Fatalf("failed to create existing auth file: %v", err)
	}

	store := NewFileTokenStore()
	savedPath, err := store.Save(context.Background(), &cliproxyauth.Auth{
		ID:       "auth.json",
		Metadata: map[string]any{"type": "demo", "label": "new"},
		Attributes: map[string]string{
			"path": path,
		},
	})
	if err != nil {
		t.Fatalf("Save error = %v, want nil", err)
	}
	if savedPath != path {
		t.Fatalf("Save path = %q, want %q", savedPath, path)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read saved auth file: %v", err)
	}
	var got map[string]any
	if err = json.Unmarshal(raw, &got); err != nil {
		t.Fatalf("saved auth file is invalid JSON: %v", err)
	}
	if got["label"] != "new" {
		t.Fatalf("label = %q, want new", got["label"])
	}
	if got["disabled"] != false {
		t.Fatalf("disabled = %v, want false", got["disabled"])
	}
}

func TestFileTokenStoreSaveMetadataBackfillsPathWhenExistingFileUnchanged(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	if err := os.WriteFile(path, []byte(`{"type":"demo","label":"new","disabled":false}`), 0o600); err != nil {
		t.Fatalf("failed to create existing auth file: %v", err)
	}

	store := NewFileTokenStore()
	store.SetBaseDir(dir)
	auth := &cliproxyauth.Auth{
		ID:       "auth.json",
		Metadata: map[string]any{"type": "demo", "label": "new"},
	}

	savedPath, err := store.Save(context.Background(), auth)
	if err != nil {
		t.Fatalf("Save error = %v, want nil", err)
	}
	if savedPath != path {
		t.Fatalf("Save path = %q, want %q", savedPath, path)
	}
	if auth.Attributes["path"] != path {
		t.Fatalf("auth path attribute = %q, want %q", auth.Attributes["path"], path)
	}
	if auth.FileName != "auth.json" {
		t.Fatalf("auth filename = %q, want auth.json", auth.FileName)
	}
}

func TestFileTokenStoreSaveStorageProtectsWithAtomicRewrite(t *testing.T) {
	secretServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			t.Fatalf("secret broker method = %s, want POST", r.Method)
		}
		if got := r.Header.Get("Authorization"); got != "Bearer test-secret-token" {
			t.Fatalf("secret broker Authorization = %q, want bearer token", got)
		}
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(`{"uri":"ccp-secret://saved-access-token"}`))
	}))
	t.Cleanup(secretServer.Close)
	t.Setenv("CCP_SECRET_BROKER_URL", secretServer.URL)
	t.Setenv("CCP_SECRET_BROKER_TOKEN", "test-secret-token")

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	store := NewFileTokenStore()
	savedPath, err := store.Save(context.Background(), &cliproxyauth.Auth{
		ID:      "auth.json",
		Storage: staticTokenStorage{payload: `{"type":"codex","access_token":"plain-access-token"}`},
		Attributes: map[string]string{
			"path": path,
		},
	})
	if err != nil {
		t.Fatalf("Save error = %v, want nil", err)
	}
	if savedPath != path {
		t.Fatalf("Save path = %q, want %q", savedPath, path)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read saved auth file: %v", err)
	}
	var got map[string]any
	if err = json.Unmarshal(raw, &got); err != nil {
		t.Fatalf("saved auth file is invalid JSON: %v", err)
	}
	if got["access_token"] != "ccp-secret://saved-access-token" {
		t.Fatalf("access_token = %q, want saved secret ref", got["access_token"])
	}

	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatalf("failed to list auth dir: %v", err)
	}
	if len(entries) != 1 || entries[0].Name() != "auth.json" {
		t.Fatalf("auth dir entries = %v, want only auth.json", entries)
	}
}

func TestPersistAuthMetadataWritesProjectID(t *testing.T) {
	t.Parallel()

	path := filepath.Join(t.TempDir(), "auth.json")
	metadata := map[string]any{
		"type":         "antigravity",
		"access_token": "token",
		"project_id":   "project-a",
	}

	if err := persistAuthMetadata(path, metadata); err != nil {
		t.Fatalf("persistAuthMetadata error = %v, want nil", err)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read persisted metadata: %v", err)
	}
	var got map[string]any
	if err = json.Unmarshal(raw, &got); err != nil {
		t.Fatalf("persisted metadata is invalid JSON: %v", err)
	}
	if got["project_id"] != "project-a" {
		t.Fatalf("project_id = %q, want project-a", got["project_id"])
	}
}

func TestWriteFileAtomicallyPreservesExistingFileOnRenameFailure(t *testing.T) {
	t.Parallel()

	dir := t.TempDir()
	path := filepath.Join(dir, "auth.json")
	original := []byte(`{"type":"antigravity","access_token":"old"}`)
	if err := os.WriteFile(path, original, 0o600); err != nil {
		t.Fatalf("failed to create auth file: %v", err)
	}

	renameErr := errors.New("rename failed")
	err := writeFileAtomically(path, []byte(`{"type":"antigravity","project_id":"project-a"}`), 0o600, func(tmpName, target string) error {
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

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

type errorReadCloser struct {
	err error
}

func (r errorReadCloser) Read([]byte) (int, error) {
	return 0, r.err
}

func (r errorReadCloser) Close() error {
	return nil
}

var _ io.ReadCloser = errorReadCloser{}

type staticTokenStorage struct {
	payload string
}

func (s staticTokenStorage) SaveTokenToFile(path string) error {
	return os.WriteFile(path, []byte(s.payload), 0o600)
}
