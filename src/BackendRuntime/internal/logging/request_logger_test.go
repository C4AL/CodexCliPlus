package logging

import (
	"strings"
	"testing"
)

func TestSanitizeForFilenameReplacesWindowsPathSeparators(t *testing.T) {
	t.Parallel()

	logger := NewFileRequestLogger(true, t.TempDir(), "", 0)
	got := logger.sanitizeForFilename(`v1\chat/completions`)

	if strings.Contains(got, `\`) {
		t.Fatalf("sanitized filename = %q, want no Windows path separator", got)
	}
	if got != "v1-chat-completions" {
		t.Fatalf("sanitized filename = %q, want v1-chat-completions", got)
	}
}

func TestGenerateFilenameKeepsRepeatedRequestIDUnique(t *testing.T) {
	t.Parallel()

	logger := NewFileRequestLogger(true, t.TempDir(), "", 0)
	timestamp := "2026-05-09T010203"

	first := logger.generateFilenameWithTimestamp("/v1/responses?api_key=masked", timestamp, "req-1")
	second := logger.generateFilenameWithTimestamp("/v1/responses?api_key=masked", timestamp, "req-1")

	if first == second {
		t.Fatalf("generated duplicate filenames for repeated request ID: %q", first)
	}
	for _, got := range []string{first, second} {
		if !strings.HasSuffix(got, "-req-1.log") {
			t.Fatalf("generated filename = %q, want request ID suffix", got)
		}
		if !strings.Contains(got, timestamp) {
			t.Fatalf("generated filename = %q, want timestamp %s", got, timestamp)
		}
		if strings.Contains(got, "?") {
			t.Fatalf("generated filename = %q, want query string removed", got)
		}
	}
}
