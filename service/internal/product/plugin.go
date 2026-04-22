package product

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

const (
	PluginSourceTypeLocal  = "local"
	PluginSourceTypeGitHub = "github"
)

type PluginCatalogEntry struct {
	ID               string `json:"id"`
	Name             string `json:"name"`
	Version          string `json:"version"`
	Description      string `json:"description"`
	SourceType       string `json:"sourceType,omitempty"`
	SourcePath       string `json:"sourcePath"`
	ReadmePath       string `json:"readmePath"`
	Category         string `json:"category,omitempty"`
	RepositoryURL    string `json:"repositoryUrl,omitempty"`
	RepositoryRef    string `json:"repositoryRef,omitempty"`
	RepositorySubdir string `json:"repositorySubdir,omitempty"`
	DownloadURL      string `json:"downloadUrl,omitempty"`
}

type PluginCatalog struct {
	ProductName     string               `json:"productName"`
	SourceRoot      string               `json:"sourceRoot"`
	MarketplacePath string               `json:"marketplacePath,omitempty"`
	CatalogSource   string               `json:"catalogSource,omitempty"`
	Plugins         []PluginCatalogEntry `json:"plugins"`
	UpdatedAt       time.Time            `json:"updatedAt"`
}

type PluginRuntimeState struct {
	ID               string    `json:"id"`
	Installed        bool      `json:"installed"`
	Enabled          bool      `json:"enabled"`
	InstalledVersion string    `json:"installedVersion"`
	InstallPath      string    `json:"installPath"`
	Message          string    `json:"message"`
	UpdatedAt        time.Time `json:"updatedAt"`
}

type PluginStateFile struct {
	ProductName string                        `json:"productName"`
	Plugins     map[string]PluginRuntimeState `json:"plugins"`
	UpdatedAt   time.Time                     `json:"updatedAt"`
}

type PluginStatus struct {
	ID               string    `json:"id"`
	Name             string    `json:"name"`
	Version          string    `json:"version"`
	Description      string    `json:"description"`
	SourceType       string    `json:"sourceType"`
	SourcePath       string    `json:"sourcePath"`
	SourceExists     bool      `json:"sourceExists"`
	ReadmePath       string    `json:"readmePath"`
	ReadmeExists     bool      `json:"readmeExists"`
	Category         string    `json:"category"`
	RepositoryURL    string    `json:"repositoryUrl"`
	RepositoryRef    string    `json:"repositoryRef"`
	RepositorySubdir string    `json:"repositorySubdir"`
	DownloadURL      string    `json:"downloadUrl"`
	InstallPath      string    `json:"installPath"`
	InstallExists    bool      `json:"installExists"`
	Installed        bool      `json:"installed"`
	Enabled          bool      `json:"enabled"`
	InstalledVersion string    `json:"installedVersion"`
	NeedsUpdate      bool      `json:"needsUpdate"`
	Message          string    `json:"message"`
	UpdatedAt        time.Time `json:"updatedAt"`
}

type PluginMarketStatus struct {
	SourceRoot        string         `json:"sourceRoot"`
	SourceExists      bool           `json:"sourceExists"`
	MarketplacePath   string         `json:"marketplacePath"`
	MarketplaceExists bool           `json:"marketplaceExists"`
	CatalogSource     string         `json:"catalogSource"`
	CatalogPath       string         `json:"catalogPath"`
	StatePath         string         `json:"statePath"`
	PluginsDir        string         `json:"pluginsDir"`
	Plugins           []PluginStatus `json:"plugins"`
	UpdatedAt         time.Time      `json:"updatedAt"`
}

func ResolvePluginSourceRoot() string {
	if custom := os.Getenv("CPAD_PLUGIN_SOURCE_ROOT"); custom != "" {
		return filepath.Clean(custom)
	}

	if repositoryRoot := ResolveRepositoryRoot(); repositoryRoot != "" {
		return filepath.Join(repositoryRoot, "plugins")
	}

	candidates := []string{}
	installRoot := ResolveInstallRoot()
	for _, candidate := range []string{
		filepath.Join(installRoot, "plugin-source"),
		filepath.Join(installRoot, "resources", "plugin-source"),
		filepath.Join(installRoot, "resources", "plugins"),
		filepath.Join(ResolveLegacyHomeInstallRoot(), "plugin-source"),
	} {
		candidates = appendUniquePathCandidate(candidates, candidate)
	}
	if resolved := firstExistingDirectory(candidates); resolved != "" {
		return resolved
	}
	if len(candidates) > 0 {
		return filepath.Clean(candidates[0])
	}

	homeDir, err := os.UserHomeDir()
	if err != nil {
		return filepath.Join(installRoot, "plugin-source")
	}

	return filepath.Join(homeDir, "workspace", "omni-bot-plugins-oss")
}

func ResolvePluginMarketplacePath() string {
	if custom := os.Getenv("CPAD_PLUGIN_MARKETPLACE_FILE"); custom != "" {
		return filepath.Clean(custom)
	}

	candidates := []string{}
	if repositoryRoot := ResolveRepositoryRoot(); repositoryRoot != "" {
		candidates = append(candidates, filepath.Join(repositoryRoot, ".agents", "plugins", "marketplace.json"))
	}

	installRoot := ResolveInstallRoot()
	candidates = append(
		candidates,
		filepath.Join(installRoot, ".agents", "plugins", "marketplace.json"),
		filepath.Join(installRoot, "resources", ".agents", "plugins", "marketplace.json"),
	)

	for _, candidate := range candidates {
		if _, err := os.Stat(candidate); err == nil {
			return filepath.Clean(candidate)
		}
	}

	if len(candidates) > 0 {
		return filepath.Clean(candidates[0])
	}

	return filepath.Join(".agents", "plugins", "marketplace.json")
}

func PluginInstallPath(layout Layout, id string) string {
	return filepath.Join(layout.Directories["plugins"], id)
}

func (layout Layout) ReadPluginCatalog() (*PluginCatalog, error) {
	content, err := os.ReadFile(layout.Files["pluginCatalog"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var catalog PluginCatalog
	if err := json.Unmarshal(content, &catalog); err != nil {
		return nil, err
	}

	if catalog.SourceRoot == "" {
		catalog.SourceRoot = ResolvePluginSourceRoot()
	}
	if catalog.MarketplacePath == "" {
		catalog.MarketplacePath = ResolvePluginMarketplacePath()
	}

	sort.Slice(catalog.Plugins, func(i int, j int) bool {
		return catalog.Plugins[i].ID < catalog.Plugins[j].ID
	})

	return &catalog, nil
}

func (layout Layout) WritePluginCatalog(catalog PluginCatalog) error {
	if catalog.ProductName == "" {
		catalog.ProductName = ProductName
	}

	if catalog.SourceRoot == "" {
		catalog.SourceRoot = ResolvePluginSourceRoot()
	}
	if catalog.MarketplacePath == "" {
		catalog.MarketplacePath = ResolvePluginMarketplacePath()
	}

	if catalog.UpdatedAt.IsZero() {
		catalog.UpdatedAt = time.Now().UTC()
	}

	sort.Slice(catalog.Plugins, func(i int, j int) bool {
		return catalog.Plugins[i].ID < catalog.Plugins[j].ID
	})

	content, err := json.MarshalIndent(catalog, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["pluginCatalog"], content, 0o644)
}

func (layout Layout) ReadPluginState() (*PluginStateFile, error) {
	content, err := os.ReadFile(layout.Files["pluginState"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var state PluginStateFile
	if err := json.Unmarshal(content, &state); err != nil {
		return nil, err
	}

	if state.Plugins == nil {
		state.Plugins = map[string]PluginRuntimeState{}
	}

	return &state, nil
}

func (layout Layout) WritePluginState(state PluginStateFile) error {
	if state.ProductName == "" {
		state.ProductName = ProductName
	}

	if state.Plugins == nil {
		state.Plugins = map[string]PluginRuntimeState{}
	}

	if state.UpdatedAt.IsZero() {
		state.UpdatedAt = time.Now().UTC()
	}

	content, err := json.MarshalIndent(state, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["pluginState"], content, 0o644)
}

func ResolvePluginMarket(layout Layout) (PluginMarketStatus, error) {
	status := PluginMarketStatus{
		SourceRoot:      ResolvePluginSourceRoot(),
		MarketplacePath: ResolvePluginMarketplacePath(),
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

	catalog, err := layout.ReadPluginCatalog()
	if err != nil {
		return PluginMarketStatus{}, err
	}

	if catalog != nil {
		status.SourceRoot = catalog.SourceRoot
		status.MarketplacePath = catalog.MarketplacePath
		status.CatalogSource = catalog.CatalogSource
		status.UpdatedAt = catalog.UpdatedAt
		if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
			status.SourceExists = true
		}
		if _, err := os.Stat(status.MarketplacePath); err == nil {
			status.MarketplaceExists = true
		}
	}

	state, err := layout.ReadPluginState()
	if err != nil {
		return PluginMarketStatus{}, err
	}

	stateMap := map[string]PluginRuntimeState{}
	if state != nil && state.Plugins != nil {
		stateMap = state.Plugins
		if status.UpdatedAt.IsZero() || state.UpdatedAt.After(status.UpdatedAt) {
			status.UpdatedAt = state.UpdatedAt
		}
	}

	seenIDs := map[string]struct{}{}
	if catalog != nil {
		for _, entry := range catalog.Plugins {
			seenIDs[entry.ID] = struct{}{}
			runtimeState := stateMap[entry.ID]
			pluginStatus := PluginStatus{
				ID:               entry.ID,
				Name:             entry.Name,
				Version:          entry.Version,
				Description:      entry.Description,
				SourceType:       entry.SourceType,
				SourcePath:       entry.SourcePath,
				ReadmePath:       entry.ReadmePath,
				Category:         entry.Category,
				RepositoryURL:    entry.RepositoryURL,
				RepositoryRef:    entry.RepositoryRef,
				RepositorySubdir: entry.RepositorySubdir,
				DownloadURL:      entry.DownloadURL,
				InstallPath:      PluginInstallPath(layout, entry.ID),
				InstalledVersion: runtimeState.InstalledVersion,
				Message:          runtimeState.Message,
				UpdatedAt:        runtimeState.UpdatedAt,
			}

			if runtimeState.InstallPath != "" {
				pluginStatus.InstallPath = runtimeState.InstallPath
			}

			if _, err := os.Stat(pluginStatus.SourcePath); err == nil {
				pluginStatus.SourceExists = true
			}

			if pluginStatus.ReadmePath != "" {
				if _, err := os.Stat(pluginStatus.ReadmePath); err == nil {
					pluginStatus.ReadmeExists = true
				}
			}

			if _, err := os.Stat(pluginStatus.InstallPath); err == nil {
				pluginStatus.InstallExists = true
			}

			pluginStatus.Installed = runtimeState.Installed && pluginStatus.InstallExists
			pluginStatus.Enabled = pluginStatus.Installed && runtimeState.Enabled
			pluginStatus.NeedsUpdate = pluginStatus.Installed &&
				shouldComparePluginVersion(pluginStatus.Version) &&
				pluginStatus.InstalledVersion != "" &&
				pluginStatus.InstalledVersion != pluginStatus.Version
			status.Plugins = append(status.Plugins, pluginStatus)
		}
	}

	for id, runtimeState := range stateMap {
		if _, seen := seenIDs[id]; seen || !shouldExposeStateOnlyPlugin(runtimeState) {
			continue
		}

		pluginStatus := PluginStatus{
			ID:               id,
			Name:             id,
			Version:          runtimeState.InstalledVersion,
			Description:      "插件已不在当前目录中，可执行卸载清理。",
			InstallPath:      PluginInstallPath(layout, id),
			InstalledVersion: runtimeState.InstalledVersion,
			Message:          runtimeState.Message,
			UpdatedAt:        runtimeState.UpdatedAt,
		}

		if runtimeState.InstallPath != "" {
			pluginStatus.InstallPath = runtimeState.InstallPath
		}
		if _, err := os.Stat(pluginStatus.InstallPath); err == nil {
			pluginStatus.InstallExists = true
		}

		pluginStatus.Installed = runtimeState.Installed && pluginStatus.InstallExists
		pluginStatus.Enabled = pluginStatus.Installed && runtimeState.Enabled
		status.Plugins = append(status.Plugins, pluginStatus)
	}

	sort.Slice(status.Plugins, func(i int, j int) bool {
		return status.Plugins[i].ID < status.Plugins[j].ID
	})

	return status, nil
}

func shouldComparePluginVersion(version string) bool {
	normalized := strings.ToLower(strings.TrimSpace(version))
	return normalized != "" && normalized != "latest" && normalized != "0.0.0"
}

func shouldExposeStateOnlyPlugin(state PluginRuntimeState) bool {
	return state.Installed ||
		state.Enabled ||
		strings.TrimSpace(state.InstallPath) != "" ||
		strings.TrimSpace(state.Message) != "" ||
		!state.UpdatedAt.IsZero()
}
