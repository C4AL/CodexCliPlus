package product

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sort"
	"time"
)

type PluginCatalogEntry struct {
	ID          string `json:"id"`
	Name        string `json:"name"`
	Version     string `json:"version"`
	Description string `json:"description"`
	SourcePath  string `json:"sourcePath"`
	ReadmePath  string `json:"readmePath"`
}

type PluginCatalog struct {
	ProductName string               `json:"productName"`
	SourceRoot  string               `json:"sourceRoot"`
	Plugins     []PluginCatalogEntry `json:"plugins"`
	UpdatedAt   time.Time            `json:"updatedAt"`
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
	SourcePath       string    `json:"sourcePath"`
	SourceExists     bool      `json:"sourceExists"`
	ReadmePath       string    `json:"readmePath"`
	ReadmeExists     bool      `json:"readmeExists"`
	InstallPath      string    `json:"installPath"`
	Installed        bool      `json:"installed"`
	Enabled          bool      `json:"enabled"`
	InstalledVersion string    `json:"installedVersion"`
	NeedsUpdate      bool      `json:"needsUpdate"`
	Message          string    `json:"message"`
	UpdatedAt        time.Time `json:"updatedAt"`
}

type PluginMarketStatus struct {
	SourceRoot   string         `json:"sourceRoot"`
	SourceExists bool           `json:"sourceExists"`
	CatalogPath  string         `json:"catalogPath"`
	StatePath    string         `json:"statePath"`
	PluginsDir   string         `json:"pluginsDir"`
	Plugins      []PluginStatus `json:"plugins"`
	UpdatedAt    time.Time      `json:"updatedAt"`
}

func ResolvePluginSourceRoot() string {
	if custom := os.Getenv("CPAD_PLUGIN_SOURCE_ROOT"); custom != "" {
		return filepath.Clean(custom)
	}

	homeDir, err := os.UserHomeDir()
	if err != nil {
		return "omni-bot-plugins-oss"
	}

	return filepath.Join(homeDir, "workspace", "omni-bot-plugins-oss")
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
		SourceRoot:  ResolvePluginSourceRoot(),
		CatalogPath: layout.Files["pluginCatalog"],
		StatePath:   layout.Files["pluginState"],
		PluginsDir:  layout.Directories["plugins"],
	}

	if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
		status.SourceExists = true
	}

	catalog, err := layout.ReadPluginCatalog()
	if err != nil {
		return PluginMarketStatus{}, err
	}

	if catalog != nil {
		status.SourceRoot = catalog.SourceRoot
		status.UpdatedAt = catalog.UpdatedAt
		if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
			status.SourceExists = true
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

	if catalog != nil {
		for _, entry := range catalog.Plugins {
			runtimeState := stateMap[entry.ID]
			pluginStatus := PluginStatus{
				ID:               entry.ID,
				Name:             entry.Name,
				Version:          entry.Version,
				Description:      entry.Description,
				SourcePath:       entry.SourcePath,
				ReadmePath:       entry.ReadmePath,
				InstallPath:      PluginInstallPath(layout, entry.ID),
				Installed:        runtimeState.Installed,
				Enabled:          runtimeState.Enabled,
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
				pluginStatus.Installed = true
			}

			pluginStatus.NeedsUpdate = pluginStatus.Installed && pluginStatus.InstalledVersion != "" && pluginStatus.InstalledVersion != pluginStatus.Version
			status.Plugins = append(status.Plugins, pluginStatus)
		}
	}

	sort.Slice(status.Plugins, func(i int, j int) bool {
		return status.Plugins[i].ID < status.Plugins[j].ID
	})

	return status, nil
}
