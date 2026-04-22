package management

import (
	"archive/tar"
	"archive/zip"
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"html"
	"io"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"path"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/branding"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/managementasset"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/util"
	sdkconfig "github.com/router-for-me/CLIProxyAPI/v6/sdk/config"
	log "github.com/sirupsen/logrus"
)

const (
	updateReleaseUserAgent   = "CPA-UV-updater"
	maxReleaseDownloadSize   = 128 << 20
	maxReleaseMetadataSize   = 2 << 20
	selfExitDelay            = 1500 * time.Millisecond
	updateInstallScriptName  = "install-update"
	updateRunnerScriptName   = "install-update-runner"
	updateArchiveWindowsExt  = ".zip"
	updateArchiveUnixExt     = ".tar.gz"
	updateExecutableBaseName = "cli-proxy-api"
)

var (
	managementVersionPattern  = regexp.MustCompile(`(?i)\b\d+(?:\.\d+){2}-uv\s*\(\d+(?:\.\d+)*\)`)
	releaseTagPathPattern     = regexp.MustCompile(`/releases/tag/([^/?#"'>]+)`)
	releaseTitlePattern       = regexp.MustCompile(`(?is)<title>\s*Release\s+([^<]+?)\s*[·-]`)
	releaseAssetRowPattern    = regexp.MustCompile(`(?is)<li\b[^>]*>(.*?)</li>`)
	releaseAssetHrefPattern   = regexp.MustCompile(`href="([^"]*/releases/download/[^"]+)"`)
	releaseAssetNamePattern   = regexp.MustCompile(`(?is)<span[^>]*class="[^"]*Truncate-text text-bold[^"]*"[^>]*>([^<]+)</span>`)
	releaseAssetDigestPattern = regexp.MustCompile(`value="sha256:([0-9a-fA-F]{64})"`)
)

type releaseAsset struct {
	Name               string `json:"name"`
	BrowserDownloadURL string `json:"browser_download_url"`
	Digest             string `json:"digest"`
}

type githubReleaseInfo struct {
	TagName string         `json:"tag_name"`
	Name    string         `json:"name"`
	HTMLURL string         `json:"html_url"`
	Assets  []releaseAsset `json:"assets"`
}

type versionSnapshot struct {
	RawVersion      string `json:"raw-version"`
	DisplayVersion  string `json:"display-version"`
	BaselineVersion string `json:"baseline-version,omitempty"`
	UVVersion       string `json:"uv-version,omitempty"`
}

type latestVersionResponse struct {
	Repository                string          `json:"repository"`
	ReleasePage               string          `json:"release-page"`
	ManagementSource          string          `json:"management-source"`
	InstallSupported          bool            `json:"install-supported"`
	UpdateAvailable           bool            `json:"update-available"`
	ServerUpdateAvailable     bool            `json:"server-update-available"`
	ManagementUpdateAvailable bool            `json:"management-update-available"`
	InstallNote               string          `json:"install-note,omitempty"`
	AssetName                 string          `json:"asset-name,omitempty"`
	Current                   versionSnapshot `json:"current"`
	Latest                    versionSnapshot `json:"latest"`
	CurrentVersion            string          `json:"current-version"`
	LatestVersion             string          `json:"latest-version"`
	ManagementCurrent         versionSnapshot `json:"management-current"`
	ManagementLatest          versionSnapshot `json:"management-latest"`
	ManagementCurrentVersion  string          `json:"management-current-version,omitempty"`
	ManagementLatestVersion   string          `json:"management-latest-version,omitempty"`
}

type preparedUpdate struct {
	workDir              string
	executablePath       string
	replacementExecPath  string
	replacementPanelPath string
	panelTargetPath      string
	argsFilePath         string
	workingDirectory     string
}

func (h *Handler) GetLatestVersion(c *gin.Context) {
	response, err := h.buildLatestVersionResponse(c.Request.Context())
	if err != nil {
		c.JSON(http.StatusBadGateway, gin.H{"error": "version_check_failed", "message": err.Error()})
		return
	}
	c.JSON(http.StatusOK, response)
}

func (h *Handler) PostInstallUpdate(c *gin.Context) {
	if !h.beginUpdateInstall() {
		c.JSON(http.StatusConflict, gin.H{"error": "update_in_progress", "message": "an update installation is already running"})
		return
	}

	response, release, archiveAsset, err := h.buildLatestVersionResponseForInstall(c.Request.Context())
	if err != nil {
		h.finishUpdateInstall()
		c.JSON(http.StatusBadGateway, gin.H{"error": "version_check_failed", "message": err.Error()})
		return
	}
	if !response.UpdateAvailable {
		h.finishUpdateInstall()
		c.JSON(http.StatusOK, gin.H{
			"status":          "already-latest",
			"current-version": response.CurrentVersion,
			"latest-version":  response.LatestVersion,
		})
		return
	}
	if archiveAsset == nil {
		h.finishUpdateInstall()
		c.JSON(http.StatusNotImplemented, gin.H{
			"error":          "install_unsupported",
			"message":        response.InstallNote,
			"latest-version": response.LatestVersion,
		})
		return
	}

	prepared, err := h.prepareUpdate(c.Request.Context(), release, archiveAsset)
	if err != nil {
		h.finishUpdateInstall()
		c.JSON(http.StatusBadGateway, gin.H{"error": "prepare_update_failed", "message": err.Error()})
		return
	}

	if err = launchInstaller(prepared); err != nil {
		h.finishUpdateInstall()
		c.JSON(http.StatusInternalServerError, gin.H{"error": "launch_install_failed", "message": err.Error()})
		return
	}

	c.JSON(http.StatusAccepted, gin.H{
		"status":           "installing",
		"latest-version":   response.LatestVersion,
		"current-version":  response.CurrentVersion,
		"release-page":     response.ReleasePage,
		"repository":       response.Repository,
		"restart-required": true,
	})

	go func() {
		time.Sleep(selfExitDelay)
		os.Exit(0)
	}()
}

func (h *Handler) buildLatestVersionResponse(ctx context.Context) (*latestVersionResponse, error) {
	response, _, _, err := h.buildLatestVersionResponseForInstall(ctx)
	return response, err
}

func (h *Handler) buildLatestVersionResponseForInstall(ctx context.Context) (*latestVersionResponse, *githubReleaseInfo, *releaseAsset, error) {
	release, repoURL, releasePage, err := h.fetchLatestReleaseInfo(ctx)
	if err != nil {
		return nil, nil, nil, err
	}

	current := buildVersionSnapshot(buildinfo.RawVersion)
	latestRaw := strings.TrimSpace(release.TagName)
	if latestRaw == "" {
		latestRaw = strings.TrimSpace(release.Name)
	}
	if latestRaw == "" {
		return nil, nil, nil, fmt.Errorf("missing release version")
	}

	latest := buildVersionSnapshot(latestRaw)
	archiveAsset, installNote := selectReleaseArchive(release.Assets)
	if archiveAsset == nil && installNote == "" {
		installNote = fmt.Sprintf("no update package found for %s/%s", runtime.GOOS, runtime.GOARCH)
	}
	serverUpdateAvailable := branding.CompareVersions(latest.RawVersion, current.RawVersion) > 0
	managementCurrent := h.currentManagementVersion()
	managementLatest := versionSnapshot{}
	managementLatestVersion := ""
	managementUpdateAvailable := false

	if managementHTMLAsset := selectManagementAsset(release.Assets); managementHTMLAsset != nil {
		client := newUpdateHTTPClient(h.proxyURL())
		panelData, _, errDownload := downloadReleaseAsset(ctx, client, *managementHTMLAsset)
		if errDownload != nil {
			log.WithError(errDownload).Warn("failed to download latest management asset for version check")
		} else {
			managementLatest = buildManagementVersionSnapshot(extractManagementVersionFromHTML(panelData))
			managementLatestVersion = strings.TrimSpace(managementLatest.DisplayVersion)
			if managementCurrent.RawVersion != "" && managementLatest.RawVersion != "" {
				managementUpdateAvailable = branding.CompareManagementVersions(
					managementLatest.RawVersion,
					managementCurrent.RawVersion,
				) > 0
			}
		}
	}

	response := &latestVersionResponse{
		Repository:                repoURL,
		ReleasePage:               firstNonEmpty(strings.TrimSpace(release.HTMLURL), releasePage),
		ManagementSource:          managementasset.ResolveManagementSourceURL(repoURL),
		InstallSupported:          archiveAsset != nil,
		UpdateAvailable:           serverUpdateAvailable || managementUpdateAvailable,
		ServerUpdateAvailable:     serverUpdateAvailable,
		ManagementUpdateAvailable: managementUpdateAvailable,
		InstallNote:               installNote,
		AssetName:                 assetName(archiveAsset),
		Current:                   current,
		Latest:                    latest,
		CurrentVersion:            current.DisplayVersion,
		LatestVersion:             latest.DisplayVersion,
		ManagementCurrent:         managementCurrent,
		ManagementLatest:          managementLatest,
		ManagementCurrentVersion:  strings.TrimSpace(managementCurrent.DisplayVersion),
		ManagementLatestVersion:   managementLatestVersion,
	}
	return response, release, archiveAsset, nil
}

func buildVersionSnapshot(raw string) versionSnapshot {
	info := branding.NormalizeVersion(raw)
	return versionSnapshot{
		RawVersion:      strings.TrimSpace(raw),
		DisplayVersion:  info.Display,
		BaselineVersion: info.BaselineVersion,
		UVVersion:       info.UVVersion,
	}
}

func buildManagementVersionSnapshot(raw string) versionSnapshot {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return versionSnapshot{}
	}

	info := branding.NormalizeManagementVersion(raw)
	return versionSnapshot{
		RawVersion:      raw,
		DisplayVersion:  info.Display,
		BaselineVersion: info.BaselineVersion,
		UVVersion:       info.UVVersion,
	}
}

func extractManagementVersionFromHTML(data []byte) string {
	match := managementVersionPattern.Find(data)
	if len(match) == 0 {
		return ""
	}
	return strings.TrimSpace(string(match))
}

func (h *Handler) currentManagementVersion() versionSnapshot {
	path := managementasset.FilePath(h.configFilePath)
	if strings.TrimSpace(path) == "" {
		return versionSnapshot{}
	}

	data, err := os.ReadFile(path)
	if err != nil {
		return versionSnapshot{}
	}

	return buildManagementVersionSnapshot(extractManagementVersionFromHTML(data))
}

func (h *Handler) fetchLatestReleaseInfo(ctx context.Context) (*githubReleaseInfo, string, string, error) {
	repoURL := h.panelRepositoryURL()
	releaseURL := managementasset.ResolveReleaseURL(repoURL)
	releasePage := managementasset.ResolveLatestReleasePageURL(repoURL)
	client := newUpdateHTTPClient(h.proxyURL())

	release, err := fetchLatestReleaseInfoFromAPI(ctx, client, releaseURL)
	if err == nil {
		return release, repoURL, releasePage, nil
	}

	log.WithError(err).Warn("failed to query latest release API, falling back to release page")
	release, fallbackPage, fallbackErr := fetchLatestReleaseInfoFromReleasePage(
		ctx,
		client,
		repoURL,
		releasePage,
	)
	if fallbackErr != nil {
		return nil, "", "", fmt.Errorf("query latest release API: %w; release page fallback: %v", err, fallbackErr)
	}

	return release, repoURL, firstNonEmpty(strings.TrimSpace(release.HTMLURL), fallbackPage, releasePage), nil
}

func fetchLatestReleaseInfoFromAPI(
	ctx context.Context,
	client *http.Client,
	releaseURL string,
) (*githubReleaseInfo, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, releaseURL, nil)
	if err != nil {
		return nil, fmt.Errorf("create release request: %w", err)
	}
	req.Header.Set("Accept", "application/vnd.github+json")
	req.Header.Set("User-Agent", updateReleaseUserAgent)

	resp, err := client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("request latest release: %w", err)
	}
	defer func() {
		_ = resp.Body.Close()
	}()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
		return nil, fmt.Errorf("unexpected release status %d: %s", resp.StatusCode, strings.TrimSpace(string(body)))
	}

	var release githubReleaseInfo
	if err = json.NewDecoder(resp.Body).Decode(&release); err != nil {
		return nil, fmt.Errorf("decode release response: %w", err)
	}

	return &release, nil
}

func fetchLatestReleaseInfoFromReleasePage(
	ctx context.Context,
	client *http.Client,
	repoURL string,
	releasePage string,
) (*githubReleaseInfo, string, error) {
	pageData, finalPageURL, err := fetchReleasePageDocument(ctx, client, releasePage)
	if err != nil {
		return nil, "", err
	}

	tag := extractReleaseTag(finalPageURL, pageData)
	if tag == "" {
		return nil, finalPageURL, fmt.Errorf("extract release tag from %s", finalPageURL)
	}

	assetsPageURL := buildExpandedAssetsURL(repoURL, tag)
	if assetsPageURL == "" {
		return nil, finalPageURL, fmt.Errorf("build expanded assets url from %s", repoURL)
	}

	assetsData, _, err := fetchReleasePageDocument(ctx, client, assetsPageURL)
	if err != nil {
		return nil, finalPageURL, fmt.Errorf("request expanded assets page: %w", err)
	}

	assets := parseReleaseAssetsFromHTML(assetsData, finalPageURL)
	if len(assets) == 0 {
		return nil, finalPageURL, fmt.Errorf("no release assets found on %s", assetsPageURL)
	}

	return &githubReleaseInfo{
		TagName: tag,
		Name:    tag,
		HTMLURL: finalPageURL,
		Assets:  assets,
	}, finalPageURL, nil
}

func fetchReleasePageDocument(
	ctx context.Context,
	client *http.Client,
	pageURL string,
) ([]byte, string, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, pageURL, nil)
	if err != nil {
		return nil, "", fmt.Errorf("create release page request: %w", err)
	}
	req.Header.Set("User-Agent", updateReleaseUserAgent)

	resp, err := client.Do(req)
	if err != nil {
		return nil, "", fmt.Errorf("request release page: %w", err)
	}
	defer func() {
		_ = resp.Body.Close()
	}()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
		return nil, "", fmt.Errorf("unexpected release page status %d: %s", resp.StatusCode, strings.TrimSpace(string(body)))
	}

	data, err := io.ReadAll(io.LimitReader(resp.Body, maxReleaseMetadataSize+1))
	if err != nil {
		return nil, "", fmt.Errorf("read release page body: %w", err)
	}
	if int64(len(data)) > maxReleaseMetadataSize {
		return nil, "", fmt.Errorf("release page exceeds maximum size of %d bytes", maxReleaseMetadataSize)
	}

	finalURL := pageURL
	if resp.Request != nil && resp.Request.URL != nil {
		finalURL = resp.Request.URL.String()
	}

	return data, finalURL, nil
}

func extractReleaseTag(pageURL string, pageData []byte) string {
	if match := releaseTagPathPattern.FindStringSubmatch(pageURL); len(match) > 1 {
		return strings.TrimSpace(html.UnescapeString(match[1]))
	}

	if match := releaseTagPathPattern.FindSubmatch(pageData); len(match) > 1 {
		return strings.TrimSpace(html.UnescapeString(string(match[1])))
	}

	if match := releaseTitlePattern.FindSubmatch(pageData); len(match) > 1 {
		return strings.TrimSpace(html.UnescapeString(string(match[1])))
	}

	return ""
}

func buildExpandedAssetsURL(repoURL string, tag string) string {
	repositoryURL := strings.TrimSuffix(managementasset.ResolveRepositoryURL(repoURL), "/")
	if repositoryURL == "" || strings.TrimSpace(tag) == "" {
		return ""
	}

	return repositoryURL + "/releases/expanded_assets/" + url.PathEscape(strings.TrimSpace(tag))
}

func parseReleaseAssetsFromHTML(data []byte, pageURL string) []releaseAsset {
	baseURL, err := url.Parse(pageURL)
	if err != nil {
		baseURL = nil
	}

	rows := releaseAssetRowPattern.FindAllSubmatch(data, -1)
	if len(rows) == 0 {
		return nil
	}

	assets := make([]releaseAsset, 0, len(rows))
	seen := make(map[string]struct{}, len(rows))

	for _, rowMatch := range rows {
		if len(rowMatch) < 2 {
			continue
		}
		row := rowMatch[1]

		hrefMatch := releaseAssetHrefPattern.FindSubmatch(row)
		if len(hrefMatch) < 2 {
			continue
		}

		rawHref := strings.TrimSpace(html.UnescapeString(string(hrefMatch[1])))
		downloadURL := rawHref
		if baseURL != nil {
			if parsedHref, errParse := url.Parse(rawHref); errParse == nil {
				downloadURL = baseURL.ResolveReference(parsedHref).String()
			}
		}

		name := ""
		if nameMatch := releaseAssetNamePattern.FindSubmatch(row); len(nameMatch) > 1 {
			name = strings.TrimSpace(html.UnescapeString(string(nameMatch[1])))
		}
		if name == "" {
			if parsedHref, errParse := url.Parse(rawHref); errParse == nil {
				if decodedName, errDecode := url.PathUnescape(path.Base(parsedHref.Path)); errDecode == nil {
					name = strings.TrimSpace(decodedName)
				}
			}
		}
		if name == "" {
			continue
		}

		normalizedName := strings.ToLower(name)
		if _, exists := seen[normalizedName]; exists {
			continue
		}
		seen[normalizedName] = struct{}{}

		digest := ""
		if digestMatch := releaseAssetDigestPattern.FindSubmatch(row); len(digestMatch) > 1 {
			digest = "sha256:" + strings.ToLower(strings.TrimSpace(string(digestMatch[1])))
		}

		assets = append(assets, releaseAsset{
			Name:               name,
			BrowserDownloadURL: downloadURL,
			Digest:             digest,
		})
	}

	return assets
}

func (h *Handler) prepareUpdate(ctx context.Context, release *githubReleaseInfo, archiveAsset *releaseAsset) (*preparedUpdate, error) {
	if archiveAsset == nil {
		return nil, fmt.Errorf("missing release archive")
	}

	executablePath, err := os.Executable()
	if err != nil {
		return nil, fmt.Errorf("resolve current executable: %w", err)
	}
	executablePath, err = filepath.Abs(executablePath)
	if err != nil {
		return nil, fmt.Errorf("resolve executable absolute path: %w", err)
	}

	workingDirectory := filepath.Dir(executablePath)

	workDir, err := os.MkdirTemp("", "cpa-uv-update-*")
	if err != nil {
		return nil, fmt.Errorf("create update workspace: %w", err)
	}

	client := newUpdateHTTPClient(h.proxyURL())
	archiveData, archiveHash, err := downloadReleaseAsset(ctx, client, *archiveAsset)
	if err != nil {
		return nil, err
	}
	replacementExecPath := filepath.Join(workDir, expectedExecutableFileName())
	if err = extractExecutableFromArchive(archiveData, replacementExecPath); err != nil {
		return nil, err
	}
	if err = os.Chmod(replacementExecPath, 0o755); err != nil && runtime.GOOS != "windows" {
		return nil, fmt.Errorf("mark replacement executable executable: %w", err)
	}

	var replacementPanelPath string
	panelAsset := selectManagementAsset(release.Assets)
	if panelAsset != nil {
		panelData, _, errDownload := downloadReleaseAsset(ctx, client, *panelAsset)
		if errDownload != nil {
			log.WithError(errDownload).Warn("failed to download management panel asset for update install")
		} else {
			replacementPanelPath = filepath.Join(workDir, managementasset.ManagementFileName)
			if errWrite := os.WriteFile(replacementPanelPath, panelData, 0o644); errWrite != nil {
				log.WithError(errWrite).Warn("failed to stage management panel asset for update install")
				replacementPanelPath = ""
			}
		}
	}

	argsFilePath := filepath.Join(workDir, "args.txt")
	if err = writeArgsFile(argsFilePath, os.Args[1:]); err != nil {
		return nil, err
	}

	log.Infof(
		"prepared CPA-UV update installation for %s (release=%s asset=%s hash=%s)",
		executablePath,
		release.TagName,
		archiveAsset.Name,
		archiveHash,
	)

	return &preparedUpdate{
		workDir:              workDir,
		executablePath:       executablePath,
		replacementExecPath:  replacementExecPath,
		replacementPanelPath: replacementPanelPath,
		panelTargetPath:      managementasset.FilePath(h.configFilePath),
		argsFilePath:         argsFilePath,
		workingDirectory:     workingDirectory,
	}, nil
}

func launchInstaller(prepared *preparedUpdate) error {
	if prepared == nil {
		return fmt.Errorf("missing prepared update")
	}
	if runtime.GOOS == "windows" {
		return launchWindowsInstaller(prepared)
	}
	return launchUnixInstaller(prepared)
}

func launchWindowsInstaller(prepared *preparedUpdate) error {
	scriptPath := filepath.Join(prepared.workDir, updateInstallScriptName+".ps1")
	script := buildWindowsInstallerScript()
	if err := os.WriteFile(scriptPath, []byte(script), 0o600); err != nil {
		return fmt.Errorf("write windows install script: %w", err)
	}

	runnerScriptPath := filepath.Join(prepared.workDir, updateRunnerScriptName+".ps1")
	runnerScript := buildWindowsInstallerRunnerScript()
	if err := os.WriteFile(runnerScriptPath, []byte(runnerScript), 0o600); err != nil {
		return fmt.Errorf("write windows install runner script: %w", err)
	}

	cmd := exec.Command(
		"powershell",
		"-NoProfile",
		"-ExecutionPolicy",
		"Bypass",
		"-WindowStyle",
		"Hidden",
		"-File",
		runnerScriptPath,
		"-InstallerScriptPath", scriptPath,
		"-ParentPid", fmt.Sprintf("%d", os.Getpid()),
		"-ExecutablePath", prepared.executablePath,
		"-ReplacementPath", prepared.replacementExecPath,
		"-PanelPath", prepared.replacementPanelPath,
		"-PanelTarget", prepared.panelTargetPath,
		"-ArgsPath", prepared.argsFilePath,
		"-WorkingDirectory", prepared.workingDirectory,
	)
	cmd.Stdout = io.Discard
	cmd.Stderr = io.Discard
	return cmd.Start()
}

func buildWindowsInstallerRunnerScript() string {
	return `param(
  [string]$InstallerScriptPath,
  [int]$ParentPid,
  [string]$ExecutablePath,
  [string]$ReplacementPath,
  [string]$PanelPath,
  [string]$PanelTarget,
  [string]$ArgsPath,
  [string]$WorkingDirectory
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'install-update-runner.log'

function Write-RunnerLog {
  param([string]$Message)
  $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
  Add-Content -LiteralPath $logPath -Value "[$timestamp] $Message"
}

Write-RunnerLog("runner starting for $ExecutablePath")

$argumentList = @(
  '-NoProfile',
  '-ExecutionPolicy', 'Bypass',
  '-WindowStyle', 'Hidden',
  '-File', $InstallerScriptPath,
  '-ParentPid', "$ParentPid",
  '-ExecutablePath', $ExecutablePath,
  '-ReplacementPath', $ReplacementPath,
  '-PanelPath', $PanelPath,
  '-PanelTarget', $PanelTarget,
  '-ArgsPath', $ArgsPath,
  '-WorkingDirectory', $WorkingDirectory
)

Start-Process -FilePath 'powershell' -ArgumentList $argumentList -WindowStyle Hidden -WorkingDirectory $PSScriptRoot | Out-Null
Write-RunnerLog('runner handed off installer successfully')
`
}

func buildWindowsInstallerScript() string {
	return `param(
  [int]$ParentPid,
  [string]$ExecutablePath,
  [string]$ReplacementPath,
  [string]$PanelPath,
  [string]$PanelTarget,
  [string]$ArgsPath,
  [string]$WorkingDirectory
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'install-update.log'

function Write-InstallerLog {
  param([string]$Message)
  $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
  Add-Content -LiteralPath $logPath -Value "[$timestamp] $Message"
}

function Invoke-WithRetry {
  param(
    [string]$Operation,
    [scriptblock]$Action,
    [int]$MaxAttempts = 120,
    [int]$DelayMilliseconds = 500
  )

  $lastErrorMessage = ''
  for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    try {
      & $Action
      if ($attempt -gt 1) {
        Write-InstallerLog("$Operation succeeded on attempt $attempt")
      }
      return
    } catch {
      $lastErrorMessage = $_.Exception.Message
      Write-InstallerLog("$Operation attempt $attempt failed: $lastErrorMessage")
      Start-Sleep -Milliseconds $DelayMilliseconds
    }
  }

  throw "$Operation failed after $MaxAttempts attempts: $lastErrorMessage"
}

Write-InstallerLog("installer started for $ExecutablePath with parent pid $ParentPid")

for ($i = 0; $i -lt 240; $i++) {
  if (-not (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) {
    Write-InstallerLog("detected parent process exit after $i wait iterations")
    break
  }
  Start-Sleep -Milliseconds 500
}

$backupPath = "$ExecutablePath.old"
$stagedPath = "$ExecutablePath.new"

try {
  if (Test-Path -LiteralPath $backupPath) {
    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
  }

  Invoke-WithRetry 'stage replacement executable' {
    Copy-Item -LiteralPath $ReplacementPath -Destination $stagedPath -Force
  }

  Invoke-WithRetry 'replace executable' {
    if (-not (Test-Path -LiteralPath $backupPath) -and (Test-Path -LiteralPath $ExecutablePath)) {
      Move-Item -LiteralPath $ExecutablePath -Destination $backupPath -Force
    }
    if (Test-Path -LiteralPath $ExecutablePath) {
      throw 'target executable still exists while waiting for replacement'
    }
    if (-not (Test-Path -LiteralPath $stagedPath)) {
      Copy-Item -LiteralPath $ReplacementPath -Destination $stagedPath -Force
    }
    Move-Item -LiteralPath $stagedPath -Destination $ExecutablePath -Force
  }

  if ($PanelPath -and $PanelTarget) {
    Invoke-WithRetry 'replace management panel' {
      $panelDir = Split-Path -Parent $PanelTarget
      if ($panelDir) {
        New-Item -ItemType Directory -Force -Path $panelDir | Out-Null
      }
      Copy-Item -LiteralPath $PanelPath -Destination $PanelTarget -Force
    }
  }

  $argList = @()
  if ($ArgsPath -and (Test-Path -LiteralPath $ArgsPath)) {
    $argList = @(Get-Content -LiteralPath $ArgsPath)
  }

  Invoke-WithRetry 'restart executable' {
    Start-Process -FilePath $ExecutablePath -WorkingDirectory $WorkingDirectory -ArgumentList $argList -WindowStyle Hidden | Out-Null
  }

  if (Test-Path -LiteralPath $backupPath) {
    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
  }
  if (Test-Path -LiteralPath $stagedPath) {
    Remove-Item -LiteralPath $stagedPath -Force -ErrorAction SilentlyContinue
  }

  Write-InstallerLog('installer finished successfully')
} catch {
  Write-InstallerLog("installer failed: $($_.Exception.Message)")
  if (-not (Test-Path -LiteralPath $ExecutablePath) -and (Test-Path -LiteralPath $backupPath)) {
    try {
      Move-Item -LiteralPath $backupPath -Destination $ExecutablePath -Force
      Write-InstallerLog('restored backup executable after failure')
    } catch {
      Write-InstallerLog("failed to restore backup executable: $($_.Exception.Message)")
    }
  }
  throw
}
`
}

func launchUnixInstaller(prepared *preparedUpdate) error {
	scriptPath := filepath.Join(prepared.workDir, updateInstallScriptName+".sh")
	script := `#!/bin/sh
set -eu

parent_pid="$1"
executable_path="$2"
replacement_path="$3"
panel_path="$4"
panel_target="$5"
args_path="$6"
working_directory="$7"

while kill -0 "$parent_pid" 2>/dev/null; do
  sleep 1
done

cp "$replacement_path" "$executable_path"
chmod +x "$executable_path"

if [ -n "$panel_path" ] && [ -n "$panel_target" ]; then
  mkdir -p "$(dirname "$panel_target")"
  cp "$panel_path" "$panel_target"
fi

set --
if [ -f "$args_path" ]; then
  while IFS= read -r line; do
    set -- "$@" "$line"
  done < "$args_path"
fi

cd "$working_directory"
nohup "$executable_path" "$@" >/dev/null 2>&1 &
`
	if err := os.WriteFile(scriptPath, []byte(script), 0o700); err != nil {
		return fmt.Errorf("write unix install script: %w", err)
	}

	cmd := exec.Command(
		"/bin/sh",
		scriptPath,
		fmt.Sprintf("%d", os.Getpid()),
		prepared.executablePath,
		prepared.replacementExecPath,
		prepared.replacementPanelPath,
		prepared.panelTargetPath,
		prepared.argsFilePath,
		prepared.workingDirectory,
	)
	cmd.Stdout = io.Discard
	cmd.Stderr = io.Discard
	return cmd.Start()
}

func newUpdateHTTPClient(proxyURL string) *http.Client {
	client := &http.Client{Timeout: 30 * time.Second}
	sdkCfg := &sdkconfig.SDKConfig{ProxyURL: strings.TrimSpace(proxyURL)}
	util.SetProxy(sdkCfg, client)
	return client
}

func downloadReleaseAsset(ctx context.Context, client *http.Client, asset releaseAsset) ([]byte, string, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, asset.BrowserDownloadURL, nil)
	if err != nil {
		return nil, "", fmt.Errorf("create download request for %s: %w", asset.Name, err)
	}
	req.Header.Set("User-Agent", updateReleaseUserAgent)

	resp, err := client.Do(req)
	if err != nil {
		return nil, "", fmt.Errorf("download %s: %w", asset.Name, err)
	}
	defer func() {
		_ = resp.Body.Close()
	}()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
		return nil, "", fmt.Errorf("unexpected download status %d for %s: %s", resp.StatusCode, asset.Name, strings.TrimSpace(string(body)))
	}

	data, err := io.ReadAll(io.LimitReader(resp.Body, maxReleaseDownloadSize+1))
	if err != nil {
		return nil, "", fmt.Errorf("read %s: %w", asset.Name, err)
	}
	if int64(len(data)) > maxReleaseDownloadSize {
		return nil, "", fmt.Errorf("asset %s exceeds maximum size of %d bytes", asset.Name, maxReleaseDownloadSize)
	}

	sum := sha256.Sum256(data)
	hash := hex.EncodeToString(sum[:])
	expectedHash := normalizeDigest(asset.Digest)
	if expectedHash != "" && !strings.EqualFold(expectedHash, hash) {
		return nil, "", fmt.Errorf("digest mismatch for %s: expected %s got %s", asset.Name, expectedHash, hash)
	}

	return data, hash, nil
}

func extractExecutableFromArchive(data []byte, destinationPath string) error {
	if runtime.GOOS == "windows" {
		return extractExecutableFromZip(data, destinationPath)
	}
	return extractExecutableFromTarGz(data, destinationPath)
}

func extractExecutableFromZip(data []byte, destinationPath string) error {
	reader, err := zip.NewReader(bytes.NewReader(data), int64(len(data)))
	if err != nil {
		return fmt.Errorf("open zip archive: %w", err)
	}

	expectedName := strings.ToLower(expectedExecutableFileName())
	for _, file := range reader.File {
		if strings.ToLower(filepath.Base(file.Name)) != expectedName {
			continue
		}
		rc, errOpen := file.Open()
		if errOpen != nil {
			return fmt.Errorf("open %s in zip: %w", file.Name, errOpen)
		}
		defer func() {
			_ = rc.Close()
		}()
		content, errRead := io.ReadAll(io.LimitReader(rc, maxReleaseDownloadSize+1))
		if errRead != nil {
			return fmt.Errorf("read %s in zip: %w", file.Name, errRead)
		}
		if int64(len(content)) > maxReleaseDownloadSize {
			return fmt.Errorf("archive entry %s exceeds maximum size", file.Name)
		}
		if errWrite := os.WriteFile(destinationPath, content, 0o755); errWrite != nil {
			return fmt.Errorf("write extracted executable: %w", errWrite)
		}
		return nil
	}

	return fmt.Errorf("executable %s not found in release archive", expectedExecutableFileName())
}

func extractExecutableFromTarGz(data []byte, destinationPath string) error {
	gzipReader, err := gzip.NewReader(bytes.NewReader(data))
	if err != nil {
		return fmt.Errorf("open tar.gz archive: %w", err)
	}
	defer func() {
		_ = gzipReader.Close()
	}()

	tarReader := tar.NewReader(gzipReader)
	expectedName := expectedExecutableFileName()

	for {
		header, errNext := tarReader.Next()
		if errNext == io.EOF {
			break
		}
		if errNext != nil {
			return fmt.Errorf("read tar.gz archive: %w", errNext)
		}
		if header == nil || header.Typeflag != tar.TypeReg {
			continue
		}
		if filepath.Base(header.Name) != expectedName {
			continue
		}
		content, errRead := io.ReadAll(io.LimitReader(tarReader, maxReleaseDownloadSize+1))
		if errRead != nil {
			return fmt.Errorf("read %s in tar.gz: %w", header.Name, errRead)
		}
		if int64(len(content)) > maxReleaseDownloadSize {
			return fmt.Errorf("archive entry %s exceeds maximum size", header.Name)
		}
		if errWrite := os.WriteFile(destinationPath, content, 0o755); errWrite != nil {
			return fmt.Errorf("write extracted executable: %w", errWrite)
		}
		return nil
	}

	return fmt.Errorf("executable %s not found in release archive", expectedName)
}

func selectReleaseArchive(assets []releaseAsset) (*releaseAsset, string) {
	needle := "_" + strings.ToLower(runtime.GOOS) + "_" + strings.ToLower(runtime.GOARCH)
	expectedExt := updateArchiveUnixExt
	if runtime.GOOS == "windows" {
		expectedExt = updateArchiveWindowsExt
	}

	for i := range assets {
		name := strings.ToLower(strings.TrimSpace(assets[i].Name))
		if strings.Contains(name, needle) && strings.HasSuffix(name, expectedExt) {
			return &assets[i], ""
		}
	}

	return nil, fmt.Sprintf("no release package found for %s/%s", runtime.GOOS, runtime.GOARCH)
}

func selectManagementAsset(assets []releaseAsset) *releaseAsset {
	for i := range assets {
		if strings.EqualFold(strings.TrimSpace(assets[i].Name), managementasset.ManagementFileName) {
			return &assets[i]
		}
	}
	return nil
}

func writeArgsFile(path string, args []string) error {
	content := strings.Join(args, "\n")
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		return fmt.Errorf("write update args file: %w", err)
	}
	return nil
}

func normalizeDigest(value string) string {
	value = strings.TrimSpace(value)
	if value == "" {
		return ""
	}
	if idx := strings.Index(value, ":"); idx >= 0 {
		value = value[idx+1:]
	}
	return strings.ToLower(strings.TrimSpace(value))
}

func expectedExecutableFileName() string {
	if runtime.GOOS == "windows" {
		return updateExecutableBaseName + ".exe"
	}
	return updateExecutableBaseName
}

func (h *Handler) proxyURL() string {
	if h == nil || h.cfg == nil {
		return ""
	}
	return strings.TrimSpace(h.cfg.ProxyURL)
}

func (h *Handler) panelRepositoryURL() string {
	if h == nil || h.cfg == nil {
		return branding.RepoURL
	}
	repo := strings.TrimSpace(h.cfg.RemoteManagement.PanelGitHubRepository)
	if repo == "" {
		return branding.RepoURL
	}
	return managementasset.ResolveRepositoryURL(repo)
}

func (h *Handler) beginUpdateInstall() bool {
	h.updateMu.Lock()
	defer h.updateMu.Unlock()
	if h.updateInProgress {
		return false
	}
	h.updateInProgress = true
	return true
}

func (h *Handler) finishUpdateInstall() {
	h.updateMu.Lock()
	h.updateInProgress = false
	h.updateMu.Unlock()
}

func assetName(asset *releaseAsset) string {
	if asset == nil {
		return ""
	}
	return asset.Name
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return value
		}
	}
	return ""
}
