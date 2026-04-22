package management

import (
	"strings"
	"testing"
)

func TestExtractManagementVersionFromHTML(t *testing.T) {
	t.Parallel()

	html := []byte(`<html><body><script>const VERSION="1.7.41-UV (2.0.0)"</script></body></html>`)
	if got := extractManagementVersionFromHTML(html); got != "1.7.41-UV (2.0.0)" {
		t.Fatalf("extractManagementVersionFromHTML() = %q, want %q", got, "1.7.41-UV (2.0.0)")
	}
}

func TestBuildManagementVersionSnapshot(t *testing.T) {
	t.Parallel()

	got := buildManagementVersionSnapshot("1.7.41-UV (2.0.0)")
	if got.DisplayVersion != "1.7.41-UV (2.0.0)" {
		t.Fatalf("DisplayVersion = %q, want %q", got.DisplayVersion, "1.7.41-UV (2.0.0)")
	}
	if got.BaselineVersion != "1.7.41" {
		t.Fatalf("BaselineVersion = %q, want %q", got.BaselineVersion, "1.7.41")
	}
	if got.UVVersion != "2.0.0" {
		t.Fatalf("UVVersion = %q, want %q", got.UVVersion, "2.0.0")
	}
}

func TestExtractReleaseTag(t *testing.T) {
	t.Parallel()

	pageURL := "https://github.com/Blackblock-inc/CPA-UV/releases/tag/v6.9.31-uv.4"
	page := []byte(`<html><head><title>Release v6.9.31-uv.4 · Blackblock-inc/CPA-UV · GitHub</title></head></html>`)

	if got := extractReleaseTag(pageURL, page); got != "v6.9.31-uv.4" {
		t.Fatalf("extractReleaseTag() = %q, want %q", got, "v6.9.31-uv.4")
	}
}

func TestParseReleaseAssetsFromHTML(t *testing.T) {
	t.Parallel()

	html := []byte(`
<div class="Box Box--condensed tmp-mt-3">
  <ul>
    <li class="Box-row">
      <a href="/Blackblock-inc/CPA-UV/releases/download/v6.9.31-uv.4/management.html" rel="nofollow" class="Truncate">
        <span class="Truncate-text text-bold">management.html</span>
      </a>
      <clipboard-copy value="sha256:1111111111111111111111111111111111111111111111111111111111111111"></clipboard-copy>
    </li>
    <li class="Box-row">
      <a href="/Blackblock-inc/CPA-UV/releases/download/v6.9.31-uv.4/CPA-UV_6.9.31-uv.4_windows_amd64.zip" rel="nofollow" class="Truncate">
        <span class="Truncate-text text-bold">CPA-UV_6.9.31-uv.4_windows_amd64.zip</span>
      </a>
      <clipboard-copy value="sha256:2222222222222222222222222222222222222222222222222222222222222222"></clipboard-copy>
    </li>
  </ul>
</div>`)

	assets := parseReleaseAssetsFromHTML(
		html,
		"https://github.com/Blackblock-inc/CPA-UV/releases/tag/v6.9.31-uv.4",
	)
	if len(assets) != 2 {
		t.Fatalf("len(parseReleaseAssetsFromHTML()) = %d, want %d", len(assets), 2)
	}

	if assets[0].Name != "management.html" {
		t.Fatalf("assets[0].Name = %q, want %q", assets[0].Name, "management.html")
	}
	if assets[0].BrowserDownloadURL != "https://github.com/Blackblock-inc/CPA-UV/releases/download/v6.9.31-uv.4/management.html" {
		t.Fatalf("assets[0].BrowserDownloadURL = %q", assets[0].BrowserDownloadURL)
	}
	if assets[0].Digest != "sha256:1111111111111111111111111111111111111111111111111111111111111111" {
		t.Fatalf("assets[0].Digest = %q", assets[0].Digest)
	}

	if assets[1].Name != "CPA-UV_6.9.31-uv.4_windows_amd64.zip" {
		t.Fatalf("assets[1].Name = %q", assets[1].Name)
	}
	if assets[1].Digest != "sha256:2222222222222222222222222222222222222222222222222222222222222222" {
		t.Fatalf("assets[1].Digest = %q", assets[1].Digest)
	}
}

func TestBuildWindowsInstallerScript(t *testing.T) {
	t.Parallel()

	script := buildWindowsInstallerScript()
	requiredSnippets := []string{
		"$logPath = Join-Path $PSScriptRoot 'install-update.log'",
		"function Invoke-WithRetry",
		"stage replacement executable",
		"replace executable",
		"restart executable",
		"installer finished successfully",
		"restored backup executable after failure",
	}

	for _, snippet := range requiredSnippets {
		if !strings.Contains(script, snippet) {
			t.Fatalf("buildWindowsInstallerScript() missing %q", snippet)
		}
	}
}

func TestBuildWindowsInstallerRunnerScript(t *testing.T) {
	t.Parallel()

	script := buildWindowsInstallerRunnerScript()
	requiredSnippets := []string{
		"$logPath = Join-Path $PSScriptRoot 'install-update-runner.log'",
		"'-File', $InstallerScriptPath",
		"Start-Process -FilePath 'powershell'",
		"runner handed off installer successfully",
	}

	for _, snippet := range requiredSnippets {
		if !strings.Contains(script, snippet) {
			t.Fatalf("buildWindowsInstallerRunnerScript() missing %q", snippet)
		}
	}
}
