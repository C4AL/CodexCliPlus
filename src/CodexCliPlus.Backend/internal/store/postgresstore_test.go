package store

import (
	"bytes"
	"context"
	"database/sql"
	"database/sql/driver"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"

	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
)

var postgresStoreTestDriverID atomic.Uint64

func TestPostgresStoreSaveMetadataIgnoresStaleFixedTempPath(t *testing.T) {
	t.Parallel()

	db, execCount := openPostgresStoreTestDB(t)
	t.Cleanup(func() { _ = db.Close() })

	root := t.TempDir()
	authDir := filepath.Join(root, "auths")
	store := &PostgresStore{
		db:      db,
		cfg:     PostgresStoreConfig{AuthTable: defaultAuthTable},
		authDir: authDir,
	}

	authPath := filepath.Join(authDir, "auth.json")
	staleTempPath := authPath + ".tmp"
	if err := os.MkdirAll(staleTempPath, 0o700); err != nil {
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
	if execCount.Load() != 1 {
		t.Fatalf("database exec count = %d, want 1", execCount.Load())
	}

	raw, err := os.ReadFile(authPath)
	if err != nil {
		t.Fatalf("read saved auth: %v", err)
	}
	if !strings.Contains(string(raw), `"label":"new"`) {
		t.Fatalf("saved auth = %s, want label metadata", raw)
	}
	if info, err := os.Stat(staleTempPath); err != nil || !info.IsDir() {
		t.Fatalf("stale fixed temp path was changed, info=%v err=%v", info, err)
	}
}

func openPostgresStoreTestDB(t *testing.T) (*sql.DB, *atomic.Int32) {
	t.Helper()

	var execCount atomic.Int32
	driverName := fmt.Sprintf("postgresstore_test_%d", postgresStoreTestDriverID.Add(1))
	sql.Register(driverName, &postgresStoreTestDriver{execCount: &execCount})
	db, err := sql.Open(driverName, "")
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	return db, &execCount
}

type postgresStoreTestDriver struct {
	execCount *atomic.Int32
}

func (d *postgresStoreTestDriver) Open(string) (driver.Conn, error) {
	return &postgresStoreTestConn{execCount: d.execCount}, nil
}

type postgresStoreTestConn struct {
	execCount *atomic.Int32
}

func (c *postgresStoreTestConn) Prepare(string) (driver.Stmt, error) {
	return nil, errors.New("prepare is not implemented")
}

func (c *postgresStoreTestConn) Close() error {
	return nil
}

func (c *postgresStoreTestConn) Begin() (driver.Tx, error) {
	return nil, errors.New("transactions are not implemented")
}

func (c *postgresStoreTestConn) ExecContext(_ context.Context, query string, args []driver.NamedValue) (driver.Result, error) {
	if !strings.Contains(query, "INSERT INTO") || !strings.Contains(query, "auth_store") {
		return nil, fmt.Errorf("unexpected query: %s", query)
	}
	if len(args) != 2 {
		return nil, fmt.Errorf("argument count = %d, want 2", len(args))
	}
	if got, ok := args[0].Value.(string); !ok || got != "auth.json" {
		return nil, fmt.Errorf("auth id argument = %#v, want auth.json", args[0].Value)
	}
	raw, ok := args[1].Value.([]byte)
	if !ok {
		return nil, fmt.Errorf("content argument type = %T, want []byte", args[1].Value)
	}
	if !bytes.Contains(raw, []byte(`"label":"new"`)) {
		return nil, fmt.Errorf("content argument = %s, want label metadata", raw)
	}
	c.execCount.Add(1)
	return driver.RowsAffected(1), nil
}
