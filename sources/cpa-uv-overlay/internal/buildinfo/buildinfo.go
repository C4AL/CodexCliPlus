// Package buildinfo exposes compile-time metadata shared across the server.
package buildinfo

// The following variables are overridden via ldflags during release builds.
// Defaults cover local development builds.
var (
	// RawVersion is the semantic version or git describe output baked into the binary.
	RawVersion = "dev"

	// Version is the normalized display version shown in headers, logs, and the WebUI.
	Version = "dev"

	// Commit is the git commit SHA baked into the binary.
	Commit = "none"

	// BuildDate records when the binary was built in UTC.
	BuildDate = "unknown"
)
