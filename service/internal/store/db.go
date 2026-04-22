package store

import (
	"database/sql"
	"fmt"
	"time"

	_ "modernc.org/sqlite"
)

const schema = `
CREATE TABLE IF NOT EXISTS service_runtime_state (
	singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
	mode TEXT NOT NULL,
	phase TEXT NOT NULL,
	updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS service_events (
	id INTEGER PRIMARY KEY AUTOINCREMENT,
	mode TEXT NOT NULL,
	phase TEXT NOT NULL,
	message TEXT NOT NULL,
	created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS codex_mode_state (
	singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
	mode TEXT NOT NULL,
	message TEXT NOT NULL,
	updated_at TEXT NOT NULL
);
`

type Snapshot struct {
	DatabasePath   string    `json:"databasePath"`
	CurrentMode    string    `json:"currentMode"`
	CurrentPhase   string    `json:"currentPhase"`
	LastMessage    string    `json:"lastMessage"`
	UpdatedAt      time.Time `json:"updatedAt"`
	EventCount     int64     `json:"eventCount"`
	CodexMode      string    `json:"codexMode"`
	CodexMessage   string    `json:"codexMessage"`
	CodexUpdatedAt time.Time `json:"codexUpdatedAt"`
}

type DB struct {
	path string
	sql  *sql.DB
}

func Open(path string) (*DB, error) {
	connection, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}

	connection.SetMaxOpenConns(1)

	if _, err := connection.Exec(`PRAGMA busy_timeout = 5000;`); err != nil {
		_ = connection.Close()
		return nil, err
	}

	if _, err := connection.Exec(schema); err != nil {
		_ = connection.Close()
		return nil, err
	}

	return &DB{
		path: path,
		sql:  connection,
	}, nil
}

func (db *DB) Close() error {
	if db == nil || db.sql == nil {
		return nil
	}

	return db.sql.Close()
}

func (db *DB) UpdateRuntimeState(mode string, phase string) error {
	if db == nil || db.sql == nil {
		return nil
	}

	_, err := db.sql.Exec(
		`INSERT INTO service_runtime_state (singleton_id, mode, phase, updated_at)
		 VALUES (1, ?, ?, ?)
		 ON CONFLICT(singleton_id) DO UPDATE SET
		   mode = excluded.mode,
		   phase = excluded.phase,
		   updated_at = excluded.updated_at`,
		mode,
		phase,
		time.Now().UTC().Format(time.RFC3339),
	)

	return err
}

func (db *DB) AppendEvent(mode string, phase string, message string) error {
	if db == nil || db.sql == nil || message == "" {
		return nil
	}

	_, err := db.sql.Exec(
		`INSERT INTO service_events (mode, phase, message, created_at) VALUES (?, ?, ?, ?)`,
		mode,
		phase,
		message,
		time.Now().UTC().Format(time.RFC3339),
	)

	return err
}

func (db *DB) UpdateCodexMode(mode string, message string) error {
	if db == nil || db.sql == nil {
		return nil
	}

	_, err := db.sql.Exec(
		`INSERT INTO codex_mode_state (singleton_id, mode, message, updated_at)
		 VALUES (1, ?, ?, ?)
		 ON CONFLICT(singleton_id) DO UPDATE SET
		   mode = excluded.mode,
		   message = excluded.message,
		   updated_at = excluded.updated_at`,
		mode,
		message,
		time.Now().UTC().Format(time.RFC3339),
	)

	return err
}

func (db *DB) Snapshot() (Snapshot, error) {
	snapshot := Snapshot{DatabasePath: db.path}

	if db == nil || db.sql == nil {
		return snapshot, nil
	}

	var updatedAtText sql.NullString
	if err := db.sql.QueryRow(
		`SELECT mode, phase, updated_at FROM service_runtime_state WHERE singleton_id = 1`,
	).Scan(&snapshot.CurrentMode, &snapshot.CurrentPhase, &updatedAtText); err != nil && err != sql.ErrNoRows {
		return Snapshot{}, err
	}

	if updatedAtText.Valid {
		parsed, err := time.Parse(time.RFC3339, updatedAtText.String)
		if err != nil {
			return Snapshot{}, fmt.Errorf("parse updated_at: %w", err)
		}

		snapshot.UpdatedAt = parsed
	}

	var lastMessage sql.NullString
	if err := db.sql.QueryRow(
		`SELECT message FROM service_events ORDER BY id DESC LIMIT 1`,
	).Scan(&lastMessage); err != nil && err != sql.ErrNoRows {
		return Snapshot{}, err
	}

	if lastMessage.Valid {
		snapshot.LastMessage = lastMessage.String
	}

	if err := db.sql.QueryRow(`SELECT COUNT(*) FROM service_events`).Scan(&snapshot.EventCount); err != nil {
		return Snapshot{}, err
	}

	var codexUpdatedAtText sql.NullString
	if err := db.sql.QueryRow(
		`SELECT mode, message, updated_at FROM codex_mode_state WHERE singleton_id = 1`,
	).Scan(&snapshot.CodexMode, &snapshot.CodexMessage, &codexUpdatedAtText); err != nil && err != sql.ErrNoRows {
		return Snapshot{}, err
	}

	if codexUpdatedAtText.Valid {
		parsed, err := time.Parse(time.RFC3339, codexUpdatedAtText.String)
		if err != nil {
			return Snapshot{}, fmt.Errorf("parse codex updated_at: %w", err)
		}

		snapshot.CodexUpdatedAt = parsed
	}

	return snapshot, nil
}
