package branding

import "testing"

func TestNormalizeVersion(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name      string
		raw       string
		display   string
		baseline  string
		uvVersion string
	}{
		{
			name:      "release tag with uv suffix",
			raw:       "6.9.31-uv.1",
			display:   "6.9.31-UV (1.0.0)",
			baseline:  "6.9.31",
			uvVersion: "1.0.0",
		},
		{
			name:      "webui-only tag",
			raw:       "v1.7.41",
			display:   "6.9.31-UV (1.7.41)",
			baseline:  "6.9.31",
			uvVersion: "1.7.41",
		},
		{
			name:      "already formatted",
			raw:       "6.9.31-UV (1.7.41)",
			display:   "6.9.31-UV (1.7.41)",
			baseline:  "6.9.31",
			uvVersion: "1.7.41",
		},
	}

	for _, tc := range cases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			info := NormalizeVersion(tc.raw)
			if info.Display != tc.display {
				t.Fatalf("Display = %q, want %q", info.Display, tc.display)
			}
			if info.BaselineVersion != tc.baseline {
				t.Fatalf("BaselineVersion = %q, want %q", info.BaselineVersion, tc.baseline)
			}
			if info.UVVersion != tc.uvVersion {
				t.Fatalf("UVVersion = %q, want %q", info.UVVersion, tc.uvVersion)
			}
		})
	}
}

func TestCompareVersions(t *testing.T) {
	t.Parallel()

	if got := CompareVersions("6.9.31-uv.1", "6.9.31-uv.1.7.41"); got >= 0 {
		t.Fatalf("CompareVersions should report latest release as newer, got %d", got)
	}
	if got := CompareVersions("6.9.31-uv.2", "6.9.31-uv.1"); got <= 0 {
		t.Fatalf("CompareVersions should report uv.2 as newer, got %d", got)
	}
}

func TestNormalizeManagementVersion(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name      string
		raw       string
		display   string
		baseline  string
		uvVersion string
	}{
		{
			name:      "display version",
			raw:       "1.7.41-UV (2.0.0)",
			display:   "1.7.41-UV (2.0.0)",
			baseline:  "1.7.41",
			uvVersion: "2.0.0",
		},
		{
			name:      "uv tag",
			raw:       "v1.7.41-uv.2",
			display:   "1.7.41-UV (2.0.0)",
			baseline:  "1.7.41",
			uvVersion: "2.0.0",
		},
		{
			name:      "uv only version",
			raw:       "2.0.0",
			display:   "1.7.41-UV (2.0.0)",
			baseline:  "1.7.41",
			uvVersion: "2.0.0",
		},
	}

	for _, tc := range cases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			info := NormalizeManagementVersion(tc.raw)
			if info.Display != tc.display {
				t.Fatalf("Display = %q, want %q", info.Display, tc.display)
			}
			if info.BaselineVersion != tc.baseline {
				t.Fatalf("BaselineVersion = %q, want %q", info.BaselineVersion, tc.baseline)
			}
			if info.UVVersion != tc.uvVersion {
				t.Fatalf("UVVersion = %q, want %q", info.UVVersion, tc.uvVersion)
			}
		})
	}
}

func TestCompareManagementVersions(t *testing.T) {
	t.Parallel()

	if got := CompareManagementVersions("1.7.41-UV (2.0.0)", "1.7.41-UV (1.0.0)"); got <= 0 {
		t.Fatalf("CompareManagementVersions should report 2.0.0 as newer, got %d", got)
	}
	if got := CompareManagementVersions("1.7.41-UV (2.0.0)", "v1.7.41-uv.2"); got != 0 {
		t.Fatalf("CompareManagementVersions should treat equivalent versions as equal, got %d", got)
	}
}
