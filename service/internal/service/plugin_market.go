package service

import (
	"archive/zip"
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

var (
	pluginFieldPattern = regexp.MustCompile(`^\s*([A-Za-z0-9_-]+)\s*=\s*"([^"]*)"`)
	commitRefPattern   = regexp.MustCompile(`^[0-9a-fA-F]{7,40}$`)
)

const pluginDefaultDescription = "未填写插件描述。"

type codexPluginManifest struct {
	Name        string `json:"name"`
	Version     string `json:"version"`
	Description string `json:"description"`
	Repository  string `json:"repository"`
	Interface   struct {
		DisplayName      string `json:"displayName"`
		ShortDescription string `json:"shortDescription"`
		Category         string `json:"category"`
	} `json:"interface"`
}

type cpadPluginSourceMetadata struct {
	ID         string    `json:"id"`
	Name       string    `json:"name"`
	SourcePath string    `json:"sourcePath"`
	Repository string    `json:"repository"`
	Branch     string    `json:"branch"`
	Commit     string    `json:"commit"`
	SyncedAt   time.Time `json:"syncedAt"`
}

type pluginMarketplaceFile struct {
	Name      string `json:"name"`
	Interface struct {
		DisplayName string `json:"displayName"`
	} `json:"interface"`
	Plugins []pluginMarketplaceEntry `json:"plugins"`
}

type pluginMarketplaceEntry struct {
	Name        string `json:"name"`
	DisplayName string `json:"displayName"`
	Description string `json:"description"`
	Version     string `json:"version"`
	Category    string `json:"category"`
	Interface   struct {
		DisplayName      string `json:"displayName"`
		ShortDescription string `json:"shortDescription"`
	} `json:"interface"`
	Source pluginMarketplaceSource `json:"source"`
}

type pluginMarketplaceSource struct {
	Source     string `json:"source"`
	Path       string `json:"path"`
	Repository string `json:"repository"`
	URL        string `json:"url"`
	Ref        string `json:"ref"`
	Branch     string `json:"branch"`
	Tag        string `json:"tag"`
	Commit     string `json:"commit"`
	ArchiveURL string `json:"archiveUrl"`
	Subdir     string `json:"subdir"`
	Directory  string `json:"directory"`
}

type pluginInstallResult struct {
	InstalledVersion string
	SourceLabel      string
}

type pluginInstallAttempt struct {
	label string
	run   func() (pluginInstallResult, error)
}

func fallbackPluginMarketStatus(layout product.Layout) product.PluginMarketStatus {
	status := product.PluginMarketStatus{
		SourceRoot:      product.ResolvePluginSourceRoot(),
		MarketplacePath: product.ResolvePluginMarketplacePath(),
		CatalogPath:     layout.Files["pluginCatalog"],
		StatePath:       layout.Files["pluginState"],
		PluginsDir:      layout.Directories["plugins"],
	}

	if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
		status.SourceExists = true
	}
	if _, err := os.Stat(status.MarketplacePath); err == nil {
		status.MarketplaceExists = true
	}

	return status
}

func InspectPluginMarket() (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	catalog, err := layout.ReadPluginCatalog()
	if err != nil {
		return fallbackPluginMarketStatus(layout), nil
	}
	if catalog == nil {
		if _, refreshErr := RefreshPluginMarket(); refreshErr != nil {
			return fallbackPluginMarketStatus(layout), nil
		}
	}

	status, err := product.ResolvePluginMarket(layout)
	if err != nil {
		return fallbackPluginMarketStatus(layout), nil
	}

	return status, nil
}

func RefreshPluginMarket() (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	catalog, err := buildPluginCatalog(layout)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	if err := layout.WritePluginCatalog(catalog); err != nil {
		return product.PluginMarketStatus{}, err
	}

	message := fmt.Sprintf("插件市场目录已刷新，共发现 %d 个插件。", len(catalog.Plugins))
	_ = layout.AppendLog(message)

	return product.ResolvePluginMarket(layout)
}

func InstallManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return syncManagedPlugin(id, true)
}

func UpdateManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return syncManagedPlugin(id, false)
}

func UninstallManagedPlugin(id string) (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	status, err := InspectPluginMarket()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	plugin, foundInStatus := findPluginStatus(status, id)
	catalog, err := layout.ReadPluginCatalog()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	inCatalog := catalogContainsPlugin(catalog, id)
	state, err := layout.ReadPluginState()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	var runtimeState product.PluginRuntimeState
	stateHasEntry := false
	if state != nil && state.Plugins != nil {
		runtimeState, stateHasEntry = state.Plugins[id]
	}

	installPath := product.PluginInstallPath(layout, id)
	if foundInStatus && strings.TrimSpace(plugin.InstallPath) != "" {
		installPath = plugin.InstallPath
	} else if stateHasEntry && strings.TrimSpace(runtimeState.InstallPath) != "" {
		installPath = runtimeState.InstallPath
	}

	installExists := pathExists(installPath)
	if !foundInStatus && !stateHasEntry && !installExists {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found: %s", id)
	}

	if installExists {
		if err := removeManagedPluginInstall(layout.Directories["plugins"], installPath); err != nil {
			return product.PluginMarketStatus{}, err
		}
	}

	if state == nil {
		state = &product.PluginStateFile{Plugins: map[string]product.PluginRuntimeState{}}
	}
	if state.Plugins == nil {
		state.Plugins = map[string]product.PluginRuntimeState{}
	}

	if inCatalog {
		runtimeState = state.Plugins[id]
		runtimeState.ID = id
		runtimeState.Installed = false
		runtimeState.Enabled = false
		runtimeState.InstalledVersion = ""
		runtimeState.InstallPath = installPath
		runtimeState.Message = fmt.Sprintf("插件 %s 已卸载。", id)
		runtimeState.UpdatedAt = time.Now().UTC()
		state.Plugins[id] = runtimeState
	} else {
		delete(state.Plugins, id)
	}
	state.UpdatedAt = time.Now().UTC()

	if err := layout.WritePluginState(*state); err != nil {
		return product.PluginMarketStatus{}, err
	}

	_ = layout.AppendLog(fmt.Sprintf("插件 %s 已卸载。", id))
	return product.ResolvePluginMarket(layout)
}

func EnableManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return setManagedPluginEnabled(id, true)
}

func DisableManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return setManagedPluginEnabled(id, false)
}

func DiagnoseManagedPlugin(id string) (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	status, err := ensurePluginMarketWithPlugin(layout, id)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	plugin, ok := findPluginStatus(status, id)
	if !ok {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found: %s", id)
	}

	message := buildPluginDiagnosticMessage(plugin)
	state, err := layout.ReadPluginState()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	if state == nil {
		state = &product.PluginStateFile{Plugins: map[string]product.PluginRuntimeState{}}
	}
	if state.Plugins == nil {
		state.Plugins = map[string]product.PluginRuntimeState{}
	}

	runtimeState := state.Plugins[id]
	runtimeState.ID = id
	runtimeState.Installed = plugin.Installed
	runtimeState.Enabled = plugin.Enabled
	runtimeState.InstalledVersion = plugin.InstalledVersion
	runtimeState.InstallPath = plugin.InstallPath
	runtimeState.Message = message
	runtimeState.UpdatedAt = time.Now().UTC()
	state.Plugins[id] = runtimeState
	state.UpdatedAt = runtimeState.UpdatedAt

	if err := layout.WritePluginState(*state); err != nil {
		return product.PluginMarketStatus{}, err
	}

	_ = layout.AppendLog(message)
	return product.ResolvePluginMarket(layout)
}

func syncManagedPlugin(id string, installIfMissing bool) (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	status, err := ensurePluginMarketWithPlugin(layout, id)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	plugin, ok := findPluginStatus(status, id)
	if !ok {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found: %s", id)
	}
	if !installIfMissing && !plugin.Installed {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin is not installed yet: %s", id)
	}

	result, err := syncPluginInstall(layout, plugin)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	state, err := layout.ReadPluginState()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	if state == nil {
		state = &product.PluginStateFile{Plugins: map[string]product.PluginRuntimeState{}}
	}
	if state.Plugins == nil {
		state.Plugins = map[string]product.PluginRuntimeState{}
	}

	runtimeState := state.Plugins[id]
	runtimeState.ID = id
	if !runtimeState.Installed {
		runtimeState.Enabled = true
	}
	runtimeState.Installed = true
	runtimeState.InstalledVersion = fallbackString(result.InstalledVersion, plugin.Version)
	runtimeState.InstallPath = plugin.InstallPath
	if plugin.Installed {
		runtimeState.Message = fmt.Sprintf("插件 %s 已更新到 %s（来源：%s）。", id, plugin.InstallPath, result.SourceLabel)
	} else {
		runtimeState.Message = fmt.Sprintf("插件 %s 已安装到 %s（来源：%s）。", id, plugin.InstallPath, result.SourceLabel)
	}
	runtimeState.UpdatedAt = time.Now().UTC()
	state.Plugins[id] = runtimeState
	state.UpdatedAt = runtimeState.UpdatedAt

	if err := layout.WritePluginState(*state); err != nil {
		return product.PluginMarketStatus{}, err
	}

	_ = layout.AppendLog(runtimeState.Message)
	return product.ResolvePluginMarket(layout)
}

func setManagedPluginEnabled(id string, enabled bool) (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	status, err := ensurePluginMarketWithPlugin(layout, id)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	plugin, ok := findPluginStatus(status, id)
	if !ok {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found: %s", id)
	}
	if !plugin.Installed {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin must be installed before changing enabled state: %s", id)
	}

	state, err := layout.ReadPluginState()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	if state == nil {
		state = &product.PluginStateFile{Plugins: map[string]product.PluginRuntimeState{}}
	}
	if state.Plugins == nil {
		state.Plugins = map[string]product.PluginRuntimeState{}
	}

	runtimeState := state.Plugins[id]
	runtimeState.ID = id
	runtimeState.Installed = true
	runtimeState.Enabled = enabled
	runtimeState.InstalledVersion = fallbackString(plugin.InstalledVersion, plugin.Version)
	runtimeState.InstallPath = plugin.InstallPath
	if enabled {
		runtimeState.Message = fmt.Sprintf("插件 %s 已启用。", id)
	} else {
		runtimeState.Message = fmt.Sprintf("插件 %s 已禁用。", id)
	}
	runtimeState.UpdatedAt = time.Now().UTC()
	state.Plugins[id] = runtimeState
	state.UpdatedAt = runtimeState.UpdatedAt

	if err := layout.WritePluginState(*state); err != nil {
		return product.PluginMarketStatus{}, err
	}

	_ = layout.AppendLog(runtimeState.Message)
	return product.ResolvePluginMarket(layout)
}

func buildPluginCatalog(layout product.Layout) (product.PluginCatalog, error) {
	catalog := product.PluginCatalog{
		SourceRoot:      product.ResolvePluginSourceRoot(),
		MarketplacePath: product.ResolvePluginMarketplacePath(),
		UpdatedAt:       time.Now().UTC(),
	}

	entriesByID := map[string]product.PluginCatalogEntry{}
	usedSources := []string{}
	var errorsList []string

	marketplaceEntries, err := readMarketplaceCatalogEntries(catalog.MarketplacePath)
	switch {
	case err == nil:
		if len(marketplaceEntries) > 0 {
			usedSources = append(usedSources, "marketplace")
		}
		for _, entry := range marketplaceEntries {
			mergePluginCatalogEntry(entriesByID, entry)
		}
	case errors.Is(err, os.ErrNotExist):
	default:
		errorsList = append(errorsList, fmt.Sprintf("read marketplace metadata: %v", err))
	}

	fallbackEntries, err := scanLocalPluginSourceRoot(catalog.SourceRoot)
	switch {
	case err == nil:
		if len(fallbackEntries) > 0 {
			usedSources = append(usedSources, "local-fallback")
		}
		for _, entry := range fallbackEntries {
			mergePluginCatalogEntry(entriesByID, entry)
		}
	case errors.Is(err, os.ErrNotExist):
	default:
		errorsList = append(errorsList, fmt.Sprintf("scan local plugin source root: %v", err))
	}

	if len(usedSources) == 0 {
		catalog.CatalogSource = "empty"
	} else {
		catalog.CatalogSource = strings.Join(usedSources, "+")
	}

	for _, entry := range entriesByID {
		catalog.Plugins = append(catalog.Plugins, normalizePluginCatalogEntry(entry))
	}

	if len(catalog.Plugins) == 0 && len(errorsList) > 0 {
		return catalog, fmt.Errorf("%s", strings.Join(errorsList, "; "))
	}

	return catalog, nil
}

func readMarketplaceCatalogEntries(marketplacePath string) ([]product.PluginCatalogEntry, error) {
	content, err := os.ReadFile(marketplacePath)
	if err != nil {
		return nil, err
	}

	var market pluginMarketplaceFile
	if err := json.Unmarshal(content, &market); err != nil {
		return nil, err
	}

	baseDir := resolveMarketplaceBaseDir(marketplacePath)
	entries := make([]product.PluginCatalogEntry, 0, len(market.Plugins))
	for _, item := range market.Plugins {
		entry, err := parseMarketplaceCatalogEntry(baseDir, item)
		if err != nil {
			return nil, fmt.Errorf("plugin %s: %w", fallbackString(item.Name, "<unknown>"), err)
		}
		entries = append(entries, entry)
	}

	return entries, nil
}

func parseMarketplaceCatalogEntry(baseDir string, item pluginMarketplaceEntry) (product.PluginCatalogEntry, error) {
	sourceType := strings.ToLower(strings.TrimSpace(item.Source.Source))
	if sourceType == "" {
		switch {
		case strings.TrimSpace(item.Source.Repository) != "", strings.TrimSpace(item.Source.URL) != "", strings.TrimSpace(item.Source.ArchiveURL) != "":
			sourceType = product.PluginSourceTypeGitHub
		default:
			sourceType = product.PluginSourceTypeLocal
		}
	}

	switch sourceType {
	case product.PluginSourceTypeLocal:
		return buildLocalMarketplaceCatalogEntry(baseDir, item)
	case product.PluginSourceTypeGitHub:
		return buildGitHubMarketplaceCatalogEntry(item)
	default:
		return product.PluginCatalogEntry{}, fmt.Errorf("unsupported marketplace source type: %s", sourceType)
	}
}

func buildLocalMarketplaceCatalogEntry(baseDir string, item pluginMarketplaceEntry) (product.PluginCatalogEntry, error) {
	sourcePath := resolveMarketplaceLocalSourcePath(baseDir, item.Source.Path)
	entry := product.PluginCatalogEntry{
		ID:          deriveMarketplacePluginID(item, sourcePath),
		Name:        firstNonEmpty(strings.TrimSpace(item.DisplayName), strings.TrimSpace(item.Interface.DisplayName), strings.TrimSpace(item.Name)),
		Version:     strings.TrimSpace(item.Version),
		Description: firstNonEmpty(strings.TrimSpace(item.Description), strings.TrimSpace(item.Interface.ShortDescription)),
		SourceType:  product.PluginSourceTypeLocal,
		SourcePath:  sourcePath,
		Category:    strings.TrimSpace(item.Category),
	}

	if sourcePath == "" {
		return product.PluginCatalogEntry{}, fmt.Errorf("missing local source path")
	}

	entry.RepositoryURL = normalizeMaybeGitHubRepository(firstNonEmpty(item.Source.Repository, item.Source.URL))
	entry.RepositoryRef = strings.TrimSpace(firstNonEmpty(item.Source.Ref, item.Source.Branch, item.Source.Tag, item.Source.Commit))
	entry.RepositorySubdir = normalizeRepositorySubdir(firstNonEmpty(item.Source.Subdir, item.Source.Directory))
	entry.DownloadURL = strings.TrimSpace(item.Source.ArchiveURL)

	if pathExists(sourcePath) {
		sourceEntry, err := loadPluginCatalogEntryFromSourcePath(sourcePath)
		if err != nil {
			return product.PluginCatalogEntry{}, err
		}
		entry = mergePluginCatalogValues(normalizePluginCatalogEntry(entry), sourceEntry)
	}

	return normalizePluginCatalogEntry(entry), nil
}

func buildGitHubMarketplaceCatalogEntry(item pluginMarketplaceEntry) (product.PluginCatalogEntry, error) {
	repositoryURL := normalizeMaybeGitHubRepository(firstNonEmpty(item.Source.Repository, item.Source.URL))
	downloadURL := strings.TrimSpace(item.Source.ArchiveURL)
	if repositoryURL == "" && downloadURL == "" {
		return product.PluginCatalogEntry{}, fmt.Errorf("missing github repository or archive url")
	}

	entry := product.PluginCatalogEntry{
		ID:               deriveMarketplacePluginID(item, ""),
		Name:             firstNonEmpty(strings.TrimSpace(item.DisplayName), strings.TrimSpace(item.Interface.DisplayName), strings.TrimSpace(item.Name)),
		Version:          strings.TrimSpace(item.Version),
		Description:      firstNonEmpty(strings.TrimSpace(item.Description), strings.TrimSpace(item.Interface.ShortDescription)),
		SourceType:       product.PluginSourceTypeGitHub,
		Category:         strings.TrimSpace(item.Category),
		RepositoryURL:    repositoryURL,
		RepositoryRef:    strings.TrimSpace(firstNonEmpty(item.Source.Ref, item.Source.Branch, item.Source.Tag, item.Source.Commit, "main")),
		RepositorySubdir: normalizeRepositorySubdir(firstNonEmpty(item.Source.Subdir, item.Source.Directory, item.Source.Path)),
		DownloadURL:      downloadURL,
	}

	return normalizePluginCatalogEntry(entry), nil
}

func deriveMarketplacePluginID(item pluginMarketplaceEntry, sourcePath string) string {
	if id := strings.TrimSpace(item.Name); id != "" {
		return id
	}
	if sourcePath != "" {
		return filepath.Base(filepath.Clean(sourcePath))
	}

	repositoryURL := firstNonEmpty(item.Source.Repository, item.Source.URL)
	if _, ownerRepo, ok := normalizeGitHubRepositoryURL(repositoryURL); ok {
		parts := strings.Split(ownerRepo, "/")
		if len(parts) == 2 {
			return parts[1]
		}
	}

	return ""
}

func resolveMarketplaceBaseDir(marketplacePath string) string {
	dir := filepath.Dir(filepath.Clean(marketplacePath))
	if strings.EqualFold(filepath.Base(dir), "plugins") && strings.EqualFold(filepath.Base(filepath.Dir(dir)), ".agents") {
		return filepath.Dir(filepath.Dir(dir))
	}

	if repositoryRoot := product.ResolveRepositoryRoot(); repositoryRoot != "" {
		return repositoryRoot
	}

	return filepath.Dir(filepath.Clean(marketplacePath))
}

func resolveMarketplaceLocalSourcePath(baseDir string, rawPath string) string {
	if strings.TrimSpace(rawPath) == "" {
		return ""
	}
	if filepath.IsAbs(rawPath) {
		return filepath.Clean(rawPath)
	}

	return filepath.Clean(filepath.Join(baseDir, filepath.FromSlash(rawPath)))
}

func scanLocalPluginSourceRoot(sourceRoot string) ([]product.PluginCatalogEntry, error) {
	sourceInfo, err := os.Stat(sourceRoot)
	if err != nil {
		return nil, err
	}
	if !sourceInfo.IsDir() {
		return nil, fmt.Errorf("plugin source root is not a directory: %s", sourceRoot)
	}

	entries, err := os.ReadDir(sourceRoot)
	if err != nil {
		return nil, err
	}

	catalogEntries := make([]product.PluginCatalogEntry, 0, len(entries))
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}

		sourcePath := filepath.Join(sourceRoot, entry.Name())
		sourceEntry, err := loadPluginCatalogEntryFromSourcePath(sourcePath)
		if err != nil {
			continue
		}

		catalogEntries = append(catalogEntries, sourceEntry)
	}

	return catalogEntries, nil
}

func loadPluginCatalogEntryFromSourcePath(sourcePath string) (product.PluginCatalogEntry, error) {
	pyprojectPath := filepath.Join(sourcePath, "pyproject.toml")
	codexPluginPath := filepath.Join(sourcePath, ".codex-plugin", "plugin.json")

	var (
		entry product.PluginCatalogEntry
		err   error
	)

	switch {
	case pathExists(pyprojectPath):
		entry, err = parsePluginCatalogEntry(sourcePath, pyprojectPath)
	case pathExists(codexPluginPath):
		entry, err = parseCodexPluginCatalogEntry(sourcePath, codexPluginPath)
	default:
		return product.PluginCatalogEntry{}, fmt.Errorf("plugin manifest not found in %s", sourcePath)
	}
	if err != nil {
		return product.PluginCatalogEntry{}, err
	}

	applyCPADSourceMetadata(sourcePath, &entry)
	return normalizePluginCatalogEntry(entry), nil
}

func mergePluginCatalogEntry(entries map[string]product.PluginCatalogEntry, incoming product.PluginCatalogEntry) {
	incoming = normalizePluginCatalogEntry(incoming)
	if strings.TrimSpace(incoming.ID) == "" {
		return
	}

	existing, ok := entries[incoming.ID]
	if !ok {
		entries[incoming.ID] = incoming
		return
	}

	entries[incoming.ID] = mergePluginCatalogValues(existing, incoming)
}

func mergePluginCatalogValues(existing product.PluginCatalogEntry, incoming product.PluginCatalogEntry) product.PluginCatalogEntry {
	merged := existing

	if isMissingPluginName(merged.Name, merged.ID) && !isMissingPluginName(incoming.Name, incoming.ID) {
		merged.Name = incoming.Name
	}
	if shouldReplacePluginVersion(merged.Version, incoming.Version) {
		merged.Version = incoming.Version
	}
	if isMissingPluginDescription(merged.Description) && !isMissingPluginDescription(incoming.Description) {
		merged.Description = incoming.Description
	}
	if merged.SourceType == "" {
		merged.SourceType = incoming.SourceType
	}
	if strings.TrimSpace(merged.SourcePath) == "" && strings.TrimSpace(incoming.SourcePath) != "" {
		merged.SourcePath = incoming.SourcePath
	}
	if strings.TrimSpace(merged.ReadmePath) == "" && strings.TrimSpace(incoming.ReadmePath) != "" {
		merged.ReadmePath = incoming.ReadmePath
	}
	if strings.TrimSpace(merged.Category) == "" && strings.TrimSpace(incoming.Category) != "" {
		merged.Category = incoming.Category
	}
	if strings.TrimSpace(merged.RepositoryURL) == "" && strings.TrimSpace(incoming.RepositoryURL) != "" {
		merged.RepositoryURL = incoming.RepositoryURL
	}
	if strings.TrimSpace(merged.RepositoryRef) == "" && strings.TrimSpace(incoming.RepositoryRef) != "" {
		merged.RepositoryRef = incoming.RepositoryRef
	}
	if strings.TrimSpace(merged.RepositorySubdir) == "" && strings.TrimSpace(incoming.RepositorySubdir) != "" {
		merged.RepositorySubdir = incoming.RepositorySubdir
	}
	if strings.TrimSpace(merged.DownloadURL) == "" && strings.TrimSpace(incoming.DownloadURL) != "" {
		merged.DownloadURL = incoming.DownloadURL
	}

	return normalizePluginCatalogEntry(merged)
}

func normalizePluginCatalogEntry(entry product.PluginCatalogEntry) product.PluginCatalogEntry {
	entry.ID = strings.TrimSpace(entry.ID)
	entry.Name = strings.TrimSpace(entry.Name)
	entry.Version = strings.TrimSpace(entry.Version)
	entry.Description = strings.TrimSpace(entry.Description)
	entry.SourceType = strings.TrimSpace(entry.SourceType)
	entry.SourcePath = normalizeOptionalPath(entry.SourcePath)
	entry.ReadmePath = normalizeOptionalPath(entry.ReadmePath)
	entry.Category = strings.TrimSpace(entry.Category)
	entry.RepositoryURL = normalizeMaybeGitHubRepository(entry.RepositoryURL)
	entry.RepositoryRef = strings.TrimSpace(entry.RepositoryRef)
	entry.RepositorySubdir = normalizeRepositorySubdir(entry.RepositorySubdir)
	entry.DownloadURL = strings.TrimSpace(entry.DownloadURL)

	if entry.ID == "" && entry.SourcePath != "" {
		entry.ID = filepath.Base(entry.SourcePath)
	}
	if entry.Name == "" {
		entry.Name = entry.ID
	}
	if entry.Version == "" {
		entry.Version = "latest"
	}
	if entry.Description == "" {
		entry.Description = pluginDefaultDescription
	}
	if entry.SourceType == "" {
		switch {
		case entry.RepositoryURL != "" || entry.DownloadURL != "":
			entry.SourceType = product.PluginSourceTypeGitHub
		default:
			entry.SourceType = product.PluginSourceTypeLocal
		}
	}

	return entry
}

func isMissingPluginName(value string, id string) bool {
	trimmed := strings.TrimSpace(value)
	return trimmed == "" || trimmed == strings.TrimSpace(id)
}

func isMissingPluginDescription(value string) bool {
	trimmed := strings.TrimSpace(value)
	return trimmed == "" || trimmed == pluginDefaultDescription
}

func shouldReplacePluginVersion(current string, candidate string) bool {
	current = strings.TrimSpace(current)
	candidate = strings.TrimSpace(candidate)
	if candidate == "" {
		return false
	}
	if current == "" || strings.EqualFold(current, "latest") || current == "0.0.0" {
		return true
	}

	return false
}

func parsePluginCatalogEntry(sourcePath string, pyprojectPath string) (product.PluginCatalogEntry, error) {
	section, err := readTomlSection(pyprojectPath, "project")
	if err != nil {
		return product.PluginCatalogEntry{}, err
	}

	fields := map[string]string{}
	scanner := bufio.NewScanner(strings.NewReader(section))
	for scanner.Scan() {
		matches := pluginFieldPattern.FindStringSubmatch(scanner.Text())
		if len(matches) == 3 {
			fields[matches[1]] = matches[2]
		}
	}
	if err := scanner.Err(); err != nil {
		return product.PluginCatalogEntry{}, err
	}

	id := fields["name"]
	if id == "" {
		id = filepath.Base(sourcePath)
	}

	readmePath := filepath.Join(sourcePath, "README.md")
	if _, err := os.Stat(readmePath); err != nil {
		readmePath = ""
	}

	return product.PluginCatalogEntry{
		ID:          id,
		Name:        id,
		Version:     fallbackString(fields["version"], "0.0.0"),
		Description: fallbackString(fields["description"], pluginDefaultDescription),
		SourceType:  product.PluginSourceTypeLocal,
		SourcePath:  sourcePath,
		ReadmePath:  readmePath,
	}, nil
}

func parseCodexPluginCatalogEntry(sourcePath string, manifestPath string) (product.PluginCatalogEntry, error) {
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		return product.PluginCatalogEntry{}, err
	}

	var manifest codexPluginManifest
	if err := json.Unmarshal(content, &manifest); err != nil {
		return product.PluginCatalogEntry{}, err
	}

	id := fallbackString(manifest.Name, filepath.Base(sourcePath))
	name := fallbackString(manifest.Interface.DisplayName, id)
	description := fallbackString(
		manifest.Interface.ShortDescription,
		fallbackString(manifest.Description, pluginDefaultDescription),
	)

	readmePath := filepath.Join(sourcePath, "README.md")
	if _, err := os.Stat(readmePath); err != nil {
		readmePath = ""
	}

	return product.PluginCatalogEntry{
		ID:            id,
		Name:          name,
		Version:       fallbackString(manifest.Version, "0.0.0"),
		Description:   description,
		SourceType:    product.PluginSourceTypeLocal,
		SourcePath:    sourcePath,
		ReadmePath:    readmePath,
		Category:      strings.TrimSpace(manifest.Interface.Category),
		RepositoryURL: normalizeMaybeGitHubRepository(manifest.Repository),
	}, nil
}

func applyCPADSourceMetadata(sourcePath string, entry *product.PluginCatalogEntry) {
	metadataPath := filepath.Join(sourcePath, ".cpad-source.json")
	content, err := os.ReadFile(metadataPath)
	if err != nil {
		return
	}

	var metadata cpadPluginSourceMetadata
	if err := json.Unmarshal(content, &metadata); err != nil {
		return
	}

	if entry.RepositoryURL == "" {
		entry.RepositoryURL = normalizeMaybeGitHubRepository(metadata.Repository)
	}
	if entry.RepositoryRef == "" {
		entry.RepositoryRef = strings.TrimSpace(firstNonEmpty(metadata.Branch, metadata.Commit))
	}
}

func readTomlSection(path string, sectionName string) (string, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return "", err
	}

	var builder strings.Builder
	inSection := false
	scanner := bufio.NewScanner(strings.NewReader(string(content)))
	for scanner.Scan() {
		line := scanner.Text()
		trimmed := strings.TrimSpace(line)
		if strings.HasPrefix(trimmed, "[") && strings.HasSuffix(trimmed, "]") {
			name := strings.Trim(trimmed, "[]")
			if inSection && name != sectionName {
				break
			}
			inSection = name == sectionName
			continue
		}

		if inSection {
			builder.WriteString(line)
			builder.WriteByte('\n')
		}
	}
	if err := scanner.Err(); err != nil {
		return "", err
	}

	return builder.String(), nil
}

func ensurePluginMarketWithPlugin(layout product.Layout, id string) (product.PluginMarketStatus, error) {
	status, err := InspectPluginMarket()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	if _, ok := findPluginStatus(status, id); ok {
		return status, nil
	}

	refreshed, refreshErr := RefreshPluginMarket()
	if refreshErr != nil {
		return product.PluginMarketStatus{}, refreshErr
	}
	if _, ok := findPluginStatus(refreshed, id); !ok {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found in market catalog or runtime state: %s", id)
	}

	return product.ResolvePluginMarket(layout)
}

func findPluginStatus(status product.PluginMarketStatus, id string) (product.PluginStatus, bool) {
	for _, plugin := range status.Plugins {
		if plugin.ID == id {
			return plugin, true
		}
	}

	return product.PluginStatus{}, false
}

func catalogContainsPlugin(catalog *product.PluginCatalog, id string) bool {
	if catalog == nil {
		return false
	}

	for _, plugin := range catalog.Plugins {
		if plugin.ID == id {
			return true
		}
	}

	return false
}

func buildPluginDiagnosticMessage(plugin product.PluginStatus) string {
	parts := []string{fmt.Sprintf("插件 %s 诊断完成", plugin.ID)}
	if !plugin.Installed {
		parts = append(parts, "尚未安装到 CPAD 安装目录")
	}
	if !plugin.SourceExists && !pluginHasRemoteSource(plugin) {
		parts = append(parts, "缺少可用安装源")
	}
	if plugin.Installed && !plugin.Enabled {
		parts = append(parts, "当前处于禁用状态")
	}
	if plugin.NeedsUpdate {
		parts = append(parts, "已安装版本落后于目录版本")
	}
	if len(parts) == 1 {
		parts = append(parts, "状态正常")
	}

	return strings.Join(parts, "；")
}

func syncPluginInstall(layout product.Layout, plugin product.PluginStatus) (pluginInstallResult, error) {
	attempts := buildPluginInstallAttempts(layout, plugin)
	if len(attempts) == 0 {
		return pluginInstallResult{}, fmt.Errorf("plugin has no available installation source: %s", plugin.ID)
	}

	var failures []string
	for _, attempt := range attempts {
		result, err := attempt.run()
		if err == nil {
			return result, nil
		}
		failures = append(failures, fmt.Sprintf("%s: %v", attempt.label, err))
	}

	return pluginInstallResult{}, fmt.Errorf("%s", strings.Join(failures, "; "))
}

func buildPluginInstallAttempts(layout product.Layout, plugin product.PluginStatus) []pluginInstallAttempt {
	attempts := []pluginInstallAttempt{}
	remoteAttempt := func() (pluginInstallResult, error) {
		return installPluginFromRemoteSource(layout, plugin)
	}
	localAttempt := func() (pluginInstallResult, error) {
		return installPluginFromLocalSource(layout.Directories["plugins"], plugin.SourcePath, plugin.InstallPath)
	}

	if shouldPreferRemotePluginSource(plugin) {
		if pluginHasRemoteSource(plugin) {
			attempts = append(attempts, pluginInstallAttempt{label: "远程仓库", run: remoteAttempt})
		}
		if plugin.SourceExists {
			attempts = append(attempts, pluginInstallAttempt{label: "本地源目录", run: localAttempt})
		}
		return attempts
	}

	if plugin.SourceExists {
		attempts = append(attempts, pluginInstallAttempt{label: "本地源目录", run: localAttempt})
	}
	if pluginHasRemoteSource(plugin) {
		attempts = append(attempts, pluginInstallAttempt{label: "远程仓库", run: remoteAttempt})
	}

	return attempts
}

func shouldPreferRemotePluginSource(plugin product.PluginStatus) bool {
	return strings.EqualFold(plugin.SourceType, product.PluginSourceTypeGitHub) || (!plugin.SourceExists && pluginHasRemoteSource(plugin))
}

func pluginHasRemoteSource(plugin product.PluginStatus) bool {
	return strings.TrimSpace(plugin.RepositoryURL) != "" || strings.TrimSpace(plugin.DownloadURL) != ""
}

func installPluginFromLocalSource(pluginsRoot string, sourcePath string, targetPath string) (pluginInstallResult, error) {
	sourceEntry, err := loadPluginCatalogEntryFromSourcePath(sourcePath)
	if err != nil {
		return pluginInstallResult{}, err
	}
	if err := mirrorPluginSource(pluginsRoot, sourcePath, targetPath); err != nil {
		return pluginInstallResult{}, err
	}

	return pluginInstallResult{
		InstalledVersion: sourceEntry.Version,
		SourceLabel:      fmt.Sprintf("本地源目录 %s", sourcePath),
	}, nil
}

func installPluginFromRemoteSource(layout product.Layout, plugin product.PluginStatus) (pluginInstallResult, error) {
	tempDir, err := os.MkdirTemp(layout.Directories["tmp"], "plugin-download-*")
	if err != nil {
		return pluginInstallResult{}, err
	}
	defer os.RemoveAll(tempDir)

	archivePath := filepath.Join(tempDir, "plugin.zip")
	archiveURL, err := downloadPluginArchive(plugin, archivePath)
	if err != nil {
		return pluginInstallResult{}, err
	}

	extractRoot, err := unzipArchiveToDirectory(archivePath, filepath.Join(tempDir, "source"))
	if err != nil {
		return pluginInstallResult{}, err
	}

	sourcePath, err := resolveDownloadedPluginSourcePath(extractRoot, plugin.RepositorySubdir)
	if err != nil {
		return pluginInstallResult{}, err
	}

	sourceEntry, err := loadPluginCatalogEntryFromSourcePath(sourcePath)
	if err != nil {
		return pluginInstallResult{}, err
	}
	if err := mirrorPluginSource(layout.Directories["plugins"], sourcePath, plugin.InstallPath); err != nil {
		return pluginInstallResult{}, err
	}

	ref := fallbackString(plugin.RepositoryRef, "main")
	sourceLabel := archiveURL
	if strings.TrimSpace(plugin.RepositoryURL) != "" {
		sourceLabel = fmt.Sprintf("%s@%s", plugin.RepositoryURL, ref)
	}

	return pluginInstallResult{
		InstalledVersion: fallbackString(sourceEntry.Version, plugin.Version),
		SourceLabel:      sourceLabel,
	}, nil
}

func downloadPluginArchive(plugin product.PluginStatus, targetPath string) (string, error) {
	candidates := candidatePluginArchiveURLs(plugin)
	if len(candidates) == 0 {
		return "", fmt.Errorf("missing download url for plugin %s", plugin.ID)
	}

	client := &http.Client{Timeout: 2 * time.Minute}
	var failures []string
	for _, candidate := range candidates {
		if err := downloadFile(client, candidate, targetPath); err == nil {
			return candidate, nil
		} else {
			failures = append(failures, fmt.Sprintf("%s: %v", candidate, err))
		}
	}

	return "", fmt.Errorf("%s", strings.Join(failures, "; "))
}

func candidatePluginArchiveURLs(plugin product.PluginStatus) []string {
	candidates := []string{}
	if strings.TrimSpace(plugin.DownloadURL) != "" {
		candidates = append(candidates, strings.TrimSpace(plugin.DownloadURL))
	}

	normalizedRepoURL, ownerRepo, ok := normalizeGitHubRepositoryURL(plugin.RepositoryURL)
	if !ok {
		return uniqueNonEmptyStrings(candidates)
	}

	ref := strings.TrimSpace(plugin.RepositoryRef)
	if ref == "" {
		ref = "main"
	}
	escapedRef := url.PathEscape(ref)
	if commitRefPattern.MatchString(ref) {
		candidates = append(candidates,
			fmt.Sprintf("%s/archive/%s.zip", normalizedRepoURL, escapedRef),
			fmt.Sprintf("https://codeload.github.com/%s/zip/%s", ownerRepo, escapedRef),
		)
		return uniqueNonEmptyStrings(candidates)
	}

	candidates = append(candidates,
		fmt.Sprintf("%s/archive/refs/heads/%s.zip", normalizedRepoURL, escapedRef),
		fmt.Sprintf("https://codeload.github.com/%s/zip/refs/heads/%s", ownerRepo, escapedRef),
		fmt.Sprintf("%s/archive/refs/tags/%s.zip", normalizedRepoURL, escapedRef),
		fmt.Sprintf("https://codeload.github.com/%s/zip/refs/tags/%s", ownerRepo, escapedRef),
		fmt.Sprintf("%s/archive/%s.zip", normalizedRepoURL, escapedRef),
		fmt.Sprintf("https://codeload.github.com/%s/zip/%s", ownerRepo, escapedRef),
	)

	return uniqueNonEmptyStrings(candidates)
}

func downloadFile(client *http.Client, downloadURL string, targetPath string) error {
	req, err := http.NewRequest(http.MethodGet, downloadURL, nil)
	if err != nil {
		return err
	}
	req.Header.Set("User-Agent", "Cli-Proxy-API-Desktop/plugin-market")

	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode < http.StatusOK || resp.StatusCode >= http.StatusMultipleChoices {
		return fmt.Errorf("unexpected status code %d", resp.StatusCode)
	}

	file, err := os.Create(targetPath)
	if err != nil {
		return err
	}
	defer file.Close()

	_, err = io.Copy(file, resp.Body)
	return err
}

func unzipArchiveToDirectory(archivePath string, destination string) (string, error) {
	reader, err := zip.OpenReader(archivePath)
	if err != nil {
		return "", err
	}
	defer reader.Close()

	if err := os.MkdirAll(destination, 0o755); err != nil {
		return "", err
	}

	topLevelNames := map[string]struct{}{}
	for _, file := range reader.File {
		if file.Mode()&os.ModeSymlink != 0 {
			return "", fmt.Errorf("archive contains unsupported symlink: %s", file.Name)
		}

		cleanedName := filepath.Clean(filepath.FromSlash(file.Name))
		if cleanedName == "." {
			continue
		}

		targetPath := filepath.Join(destination, cleanedName)
		if err := ensurePathWithinRoot(destination, targetPath); err != nil {
			return "", err
		}

		topLevelName := cleanedName
		if index := strings.IndexRune(cleanedName, filepath.Separator); index >= 0 {
			topLevelName = cleanedName[:index]
		}
		topLevelNames[topLevelName] = struct{}{}

		if file.FileInfo().IsDir() {
			if err := os.MkdirAll(targetPath, 0o755); err != nil {
				return "", err
			}
			continue
		}

		if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
			return "", err
		}

		source, err := file.Open()
		if err != nil {
			return "", err
		}

		mode := file.Mode().Perm()
		if mode == 0 {
			mode = 0o644
		}
		target, err := os.OpenFile(targetPath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, mode)
		if err != nil {
			source.Close()
			return "", err
		}

		_, copyErr := io.Copy(target, source)
		closeErr := target.Close()
		sourceErr := source.Close()
		if copyErr != nil {
			return "", copyErr
		}
		if closeErr != nil {
			return "", closeErr
		}
		if sourceErr != nil {
			return "", sourceErr
		}
	}

	if len(topLevelNames) == 1 {
		for name := range topLevelNames {
			candidate := filepath.Join(destination, name)
			if info, err := os.Stat(candidate); err == nil && info.IsDir() {
				return candidate, nil
			}
		}
	}

	return destination, nil
}

func resolveDownloadedPluginSourcePath(extractedRoot string, subdir string) (string, error) {
	targetPath := extractedRoot
	if normalizedSubdir := normalizeRepositorySubdir(subdir); normalizedSubdir != "" {
		targetPath = filepath.Join(extractedRoot, normalizedSubdir)
		if err := ensurePathWithinRoot(extractedRoot, targetPath); err != nil {
			return "", err
		}
	}

	if _, err := os.Stat(targetPath); err != nil {
		return "", err
	}

	return targetPath, nil
}

func mirrorPluginSource(pluginsRoot string, sourcePath string, targetPath string) error {
	if err := ensureManagedPluginTargetPath(pluginsRoot, targetPath); err != nil {
		return fmt.Errorf("refusing to sync plugin outside managed plugins directory: %w", err)
	}

	if err := os.RemoveAll(targetPath); err != nil {
		return err
	}

	return filepath.WalkDir(sourcePath, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}

		name := entry.Name()
		if shouldSkipPluginPath(name) {
			if entry.IsDir() {
				return filepath.SkipDir
			}
			return nil
		}

		relative, err := filepath.Rel(sourcePath, path)
		if err != nil {
			return err
		}

		destination := filepath.Join(targetPath, relative)
		if err := ensurePathWithinRoot(targetPath, destination); err != nil {
			return err
		}
		if entry.IsDir() {
			return os.MkdirAll(destination, 0o755)
		}

		return copyFile(path, destination)
	})
}

func removeManagedPluginInstall(pluginsRoot string, targetPath string) error {
	if err := ensureManagedPluginTargetPath(pluginsRoot, targetPath); err != nil {
		return fmt.Errorf("refusing to remove plugin outside managed plugins directory: %w", err)
	}

	return os.RemoveAll(targetPath)
}

func ensurePathWithinRoot(root string, targetPath string) error {
	relativePath, err := filepath.Rel(root, targetPath)
	if err != nil {
		return err
	}
	if isRelativePathOutsideRoot(relativePath) {
		return fmt.Errorf("path %s is outside %s", targetPath, root)
	}

	return nil
}

func ensureManagedPluginTargetPath(pluginsRoot string, targetPath string) error {
	if err := ensurePathWithinRoot(pluginsRoot, targetPath); err != nil {
		return err
	}

	relativePath, err := filepath.Rel(pluginsRoot, targetPath)
	if err != nil {
		return err
	}
	if relativePath == "." {
		return fmt.Errorf("path %s resolves to the managed plugins root", targetPath)
	}

	return nil
}

func isRelativePathOutsideRoot(relativePath string) bool {
	return relativePath == ".." || strings.HasPrefix(relativePath, ".."+string(filepath.Separator))
}

func shouldSkipPluginPath(name string) bool {
	switch name {
	case ".git", "__pycache__", ".venv", ".pytest_cache", ".mypy_cache", ".ruff_cache", "node_modules", "dist":
		return true
	default:
		return strings.HasSuffix(name, ".pyc")
	}
}

func copyFile(sourcePath string, targetPath string) error {
	sourceFile, err := os.Open(sourcePath)
	if err != nil {
		return err
	}
	defer sourceFile.Close()

	sourceInfo, err := sourceFile.Stat()
	if err != nil {
		return err
	}

	if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
		return err
	}

	targetFile, err := os.OpenFile(targetPath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, sourceInfo.Mode().Perm())
	if err != nil {
		return err
	}
	defer targetFile.Close()

	_, err = io.Copy(targetFile, sourceFile)
	return err
}

func normalizeMaybeGitHubRepository(raw string) string {
	normalizedURL, _, ok := normalizeGitHubRepositoryURL(raw)
	if ok {
		return normalizedURL
	}

	return strings.TrimSpace(strings.TrimSuffix(strings.TrimSuffix(raw, ".git"), "/"))
}

func normalizeGitHubRepositoryURL(raw string) (string, string, bool) {
	trimmed := strings.TrimSpace(raw)
	if trimmed == "" {
		return "", "", false
	}

	trimmed = strings.TrimSuffix(strings.TrimSuffix(trimmed, ".git"), "/")
	if strings.HasPrefix(trimmed, "git@github.com:") {
		trimmed = "https://github.com/" + strings.TrimPrefix(trimmed, "git@github.com:")
	}
	if strings.HasPrefix(trimmed, "http://github.com/") {
		trimmed = "https://github.com/" + strings.TrimPrefix(trimmed, "http://github.com/")
	}
	if strings.HasPrefix(trimmed, "https://github.com/") {
		pathPart := strings.TrimPrefix(trimmed, "https://github.com/")
		pathPart = strings.Trim(pathPart, "/")
		parts := strings.Split(pathPart, "/")
		if len(parts) < 2 {
			return "", "", false
		}

		ownerRepo := parts[0] + "/" + parts[1]
		return "https://github.com/" + ownerRepo, ownerRepo, true
	}

	parts := strings.Split(strings.Trim(trimmed, "/"), "/")
	if len(parts) == 2 && !strings.Contains(parts[0], ":") {
		ownerRepo := parts[0] + "/" + parts[1]
		return "https://github.com/" + ownerRepo, ownerRepo, true
	}

	return "", "", false
}

func normalizeRepositorySubdir(raw string) string {
	trimmed := strings.TrimSpace(raw)
	if trimmed == "" {
		return ""
	}

	cleaned := filepath.Clean(filepath.FromSlash(trimmed))
	if cleaned == "." {
		return ""
	}

	return cleaned
}

func normalizeOptionalPath(raw string) string {
	trimmed := strings.TrimSpace(raw)
	if trimmed == "" {
		return ""
	}

	return filepath.Clean(trimmed)
}

func uniqueNonEmptyStrings(values []string) []string {
	seen := map[string]struct{}{}
	result := make([]string, 0, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if value == "" {
			continue
		}
		if _, ok := seen[value]; ok {
			continue
		}
		seen[value] = struct{}{}
		result = append(result, value)
	}

	return result
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return value
		}
	}

	return ""
}

func fallbackString(value string, fallback string) string {
	if strings.TrimSpace(value) == "" {
		return fallback
	}

	return value
}

func pathExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}
