package store

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"

	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
)

func TestObjectTokenStoreSaveMetadataIgnoresStaleFixedTempPath(t *testing.T) {
	t.Parallel()

	var putCount atomic.Int32
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if _, ok := r.URL.Query()["location"]; r.Method == http.MethodGet && r.URL.Path == "/bucket/" && ok {
			w.Header().Set("Content-Type", "application/xml")
			_, _ = io.WriteString(w, `<LocationConstraint xmlns="http://s3.amazonaws.com/doc/2006-03-01/">us-east-1</LocationConstraint>`)
			return
		}
		if r.Method != http.MethodPut {
			t.Fatalf("method = %s, want PUT", r.Method)
		}
		if r.URL.Path != "/bucket/auths/auth.json" {
			t.Fatalf("path = %s, want /bucket/auths/auth.json", r.URL.Path)
		}
		body, err := io.ReadAll(r.Body)
		if err != nil {
			t.Fatalf("read request body: %v", err)
		}
		if !strings.Contains(string(body), `"label":"new"`) {
			t.Fatalf("uploaded body = %s, want label metadata", body)
		}
		putCount.Add(1)
		w.Header().Set("ETag", `"test-etag"`)
		w.WriteHeader(http.StatusOK)
	}))
	t.Cleanup(server.Close)

	root := t.TempDir()
	store, err := NewObjectTokenStore(ObjectStoreConfig{
		Endpoint:  strings.TrimPrefix(server.URL, "http://"),
		Bucket:    "bucket",
		AccessKey: "access-key",
		SecretKey: "secret-key",
		LocalRoot: root,
		PathStyle: true,
	})
	if err != nil {
		t.Fatalf("NewObjectTokenStore: %v", err)
	}

	authPath := filepath.Join(root, "auths", "auth.json")
	staleTempPath := authPath + ".tmp"
	if err := os.Mkdir(staleTempPath, 0o700); err != nil {
		t.Fatalf("create stale fixed temp path: %v", err)
	}

	savedPath, err := store.Save(context.Background(), &cliproxyauth.Auth{
		ID:       "auth.json",
		Metadata: map[string]any{"type": "demo", "label": "new"},
		Attributes: map[string]string{
			"path": authPath,
		},
	})
	if err != nil {
		t.Fatalf("Save error = %v, want nil", err)
	}
	if savedPath != authPath {
		t.Fatalf("Save path = %q, want %q", savedPath, authPath)
	}
	if putCount.Load() != 1 {
		t.Fatalf("PUT count = %d, want 1", putCount.Load())
	}
	if info, err := os.Stat(staleTempPath); err != nil || !info.IsDir() {
		t.Fatalf("stale fixed temp path was changed, info=%v err=%v", info, err)
	}
}
