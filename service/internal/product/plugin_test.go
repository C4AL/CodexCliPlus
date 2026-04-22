package product

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestResolvePluginMarketDoesNotAutoInstallWithoutState(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		Directories: map[string]string{
			"plugins": filepath.Join(rootDir, "plugins"),
		},
		Files: map[string]string{
			"pluginCatalog": filepath.Join(rootDir, "data", "plugin-catalog.json"),
			"pluginState":   filepath.Join(rootDir, "data", "plugin-state.json"),
		},
	}

	if err := os.MkdirAll(layout.Directories["plugins"], 0o755); err != nil {
		t.Fatalf("mkdir plugins: %v", err)
	}
	sourcePath := filepath.Join(rootDir, "source", "edge-control")
	if err := os.MkdirAll(sourcePath, 0o755); err != nil {
		t.Fatalf("mkdir source: %v", err)
	}
	installPath := PluginInstallPath(layout, "edge-control")
	if err := os.MkdirAll(installPath, 0o755); err != nil {
		t.Fatalf("mkdir install: %v", err)
	}

	catalog := PluginCatalog{
		SourceRoot: filepath.Join(rootDir, "source"),
		Plugins: []PluginCatalogEntry{
			{
				ID:          "edge-control",
				Name:        "Edge Control",
				Version:     "0.1.7",
				Description: "Test plugin",
				SourceType:  PluginSourceTypeLocal,
				SourcePath:  sourcePath,
			},
		},
	}
	if err := os.MkdirAll(filepath.Dir(layout.Files["pluginCatalog"]), 0o755); err != nil {
		t.Fatalf("mkdir data: %v", err)
	}
	if err := layout.WritePluginCatalog(catalog); err != nil {
		t.Fatalf("WritePluginCatalog: %v", err)
	}

	status, err := ResolvePluginMarket(layout)
	if err != nil {
		t.Fatalf("ResolvePluginMarket: %v", err)
	}
	if len(status.Plugins) != 1 {
		t.Fatalf("len(status.Plugins) = %d, want 1", len(status.Plugins))
	}

	plugin := status.Plugins[0]
	if !plugin.InstallExists {
		t.Fatal("expected install path to exist")
	}
	if plugin.Installed {
		t.Fatal("expected plugin to remain uninstalled without runtime state")
	}
	if plugin.Enabled {
		t.Fatal("expected plugin to remain disabled without runtime state")
	}
}

func TestResolvePluginMarketIncludesStateOnlyPlugin(t *testing.T) {
	rootDir := t.TempDir()
	layout := Layout{
		Directories: map[string]string{
			"plugins": filepath.Join(rootDir, "plugins"),
		},
		Files: map[string]string{
			"pluginCatalog": filepath.Join(rootDir, "data", "plugin-catalog.json"),
			"pluginState":   filepath.Join(rootDir, "data", "plugin-state.json"),
		},
	}

	if err := os.MkdirAll(layout.Directories["plugins"], 0o755); err != nil {
		t.Fatalf("mkdir plugins: %v", err)
	}
	installPath := PluginInstallPath(layout, "openai-bot-plugin")
	if err := os.MkdirAll(installPath, 0o755); err != nil {
		t.Fatalf("mkdir install: %v", err)
	}

	if err := os.MkdirAll(filepath.Dir(layout.Files["pluginState"]), 0o755); err != nil {
		t.Fatalf("mkdir data: %v", err)
	}
	if err := layout.WritePluginState(PluginStateFile{
		Plugins: map[string]PluginRuntimeState{
			"openai-bot-plugin": {
				ID:               "openai-bot-plugin",
				Installed:        true,
				Enabled:          true,
				InstalledVersion: "1.2.3",
				InstallPath:      installPath,
				Message:          "legacy plugin",
				UpdatedAt:        time.Now().UTC(),
			},
		},
		UpdatedAt: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("WritePluginState: %v", err)
	}

	status, err := ResolvePluginMarket(layout)
	if err != nil {
		t.Fatalf("ResolvePluginMarket: %v", err)
	}
	if len(status.Plugins) != 1 {
		t.Fatalf("len(status.Plugins) = %d, want 1", len(status.Plugins))
	}

	plugin := status.Plugins[0]
	if plugin.ID != "openai-bot-plugin" {
		t.Fatalf("plugin.ID = %q, want openai-bot-plugin", plugin.ID)
	}
	if !plugin.Installed {
		t.Fatal("expected state-only plugin to be reported as installed")
	}
	if !plugin.Enabled {
		t.Fatal("expected state-only plugin to be reported as enabled")
	}
}
