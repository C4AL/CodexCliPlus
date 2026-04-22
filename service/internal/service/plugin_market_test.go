package service

import (
	"archive/zip"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

func TestBuildPluginCatalogMergesMarketplaceRemoteAndLocalFallback(t *testing.T) {
	sourceRoot := filepath.Join(t.TempDir(), "plugins")
	marketplacePath := filepath.Join(t.TempDir(), ".agents", "plugins", "marketplace.json")
	if err := os.MkdirAll(sourceRoot, 0o755); err != nil {
		t.Fatalf("mkdir source root: %v", err)
	}
	if err := os.MkdirAll(filepath.Dir(marketplacePath), 0o755); err != nil {
		t.Fatalf("mkdir marketplace dir: %v", err)
	}

	pluginSource := filepath.Join(sourceRoot, "edge-control")
	if err := os.MkdirAll(filepath.Join(pluginSource, ".codex-plugin"), 0o755); err != nil {
		t.Fatalf("mkdir plugin source: %v", err)
	}
	if err := os.WriteFile(filepath.Join(pluginSource, ".codex-plugin", "plugin.json"), []byte(`{
  "name": "edge-control",
  "version": "0.1.7",
  "description": "Edge bridge",
  "interface": {
    "displayName": "Edge Control",
    "shortDescription": "Drive Edge"
  }
}`), 0o644); err != nil {
		t.Fatalf("write plugin manifest: %v", err)
	}
	if err := os.WriteFile(filepath.Join(pluginSource, ".cpad-source.json"), []byte(`{
  "id": "edge-control",
  "repository": "https://github.com/Blackblock-inc/codex-edge-control-bridge.git",
  "branch": "main"
}`), 0o644); err != nil {
		t.Fatalf("write source metadata: %v", err)
	}

	marketplace := map[string]any{
		"name": "cpad-test",
		"plugins": []map[string]any{
			{
				"name": "edge-control",
				"source": map[string]any{
					"source":     "github",
					"repository": "Blackblock-inc/codex-edge-control-bridge",
					"ref":        "main",
				},
			},
		},
	}
	writeJSONFile(t, marketplacePath, marketplace)

	t.Setenv("CPAD_PLUGIN_SOURCE_ROOT", sourceRoot)
	t.Setenv("CPAD_PLUGIN_MARKETPLACE_FILE", marketplacePath)

	catalog, err := buildPluginCatalog(product.Layout{})
	if err != nil {
		t.Fatalf("buildPluginCatalog: %v", err)
	}
	if len(catalog.Plugins) != 1 {
		t.Fatalf("len(catalog.Plugins) = %d, want 1", len(catalog.Plugins))
	}

	plugin := catalog.Plugins[0]
	if plugin.SourceType != product.PluginSourceTypeGitHub {
		t.Fatalf("plugin.SourceType = %q, want github", plugin.SourceType)
	}
	if plugin.SourcePath != pluginSource {
		t.Fatalf("plugin.SourcePath = %q, want %q", plugin.SourcePath, pluginSource)
	}
	if plugin.Version != "0.1.7" {
		t.Fatalf("plugin.Version = %q, want 0.1.7", plugin.Version)
	}
	if plugin.Name != "Edge Control" {
		t.Fatalf("plugin.Name = %q, want Edge Control", plugin.Name)
	}
	if plugin.RepositoryURL != "https://github.com/Blackblock-inc/codex-edge-control-bridge" {
		t.Fatalf("plugin.RepositoryURL = %q", plugin.RepositoryURL)
	}
}

func TestInstallManagedPluginFromMarketplaceArchiveAndUninstallStateOnlyPlugin(t *testing.T) {
	installRoot := t.TempDir()
	sourceRoot := filepath.Join(t.TempDir(), "missing-local-source")
	marketplacePath := filepath.Join(t.TempDir(), ".agents", "plugins", "marketplace.json")
	if err := os.MkdirAll(filepath.Dir(marketplacePath), 0o755); err != nil {
		t.Fatalf("mkdir marketplace dir: %v", err)
	}

	archiveBytes := buildPluginArchive(t, map[string]string{
		"edge-control-main/.codex-plugin/plugin.json": `{
  "name": "edge-control",
  "version": "0.1.7",
  "description": "Edge bridge",
  "interface": {
    "displayName": "Edge Control",
    "shortDescription": "Drive Edge"
  }
}`,
		"edge-control-main/README.md": "# edge-control",
	})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/zip")
		_, _ = w.Write(archiveBytes)
	}))
	defer server.Close()

	marketplace := map[string]any{
		"name": "cpad-test",
		"plugins": []map[string]any{
			{
				"name":        "edge-control",
				"version":     "0.1.7",
				"description": "Edge bridge",
				"source": map[string]any{
					"source":     "github",
					"archiveUrl": server.URL + "/edge-control.zip",
					"ref":        "main",
				},
			},
		},
	}
	writeJSONFile(t, marketplacePath, marketplace)

	t.Setenv("CPAD_INSTALL_ROOT", installRoot)
	t.Setenv("CPAD_PLUGIN_SOURCE_ROOT", sourceRoot)
	t.Setenv("CPAD_PLUGIN_MARKETPLACE_FILE", marketplacePath)

	if _, err := RefreshPluginMarket(); err != nil {
		t.Fatalf("RefreshPluginMarket: %v", err)
	}

	status, err := InstallManagedPlugin("edge-control")
	if err != nil {
		t.Fatalf("InstallManagedPlugin: %v", err)
	}

	plugin, ok := findPluginStatus(status, "edge-control")
	if !ok {
		t.Fatal("expected edge-control in plugin status")
	}
	if !plugin.Installed {
		t.Fatal("expected edge-control to be installed")
	}

	installedManifest := filepath.Join(installRoot, "plugins", "edge-control", ".codex-plugin", "plugin.json")
	if _, err := os.Stat(installedManifest); err != nil {
		t.Fatalf("installed manifest missing: %v", err)
	}

	layout := product.NewLayout()
	if err := layout.WritePluginState(product.PluginStateFile{
		Plugins: map[string]product.PluginRuntimeState{
			"openai-bot-plugin": {
				ID:               "openai-bot-plugin",
				Installed:        true,
				Enabled:          true,
				InstalledVersion: "9.9.9",
				InstallPath:      filepath.Join(installRoot, "plugins", "openai-bot-plugin"),
				Message:          "legacy plugin",
				UpdatedAt:        time.Now().UTC(),
			},
		},
		UpdatedAt: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("WritePluginState: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(installRoot, "plugins", "openai-bot-plugin"), 0o755); err != nil {
		t.Fatalf("mkdir orphan plugin: %v", err)
	}

	status, err = UninstallManagedPlugin("openai-bot-plugin")
	if err != nil {
		t.Fatalf("UninstallManagedPlugin: %v", err)
	}
	if _, err := os.Stat(filepath.Join(installRoot, "plugins", "openai-bot-plugin")); !os.IsNotExist(err) {
		t.Fatalf("expected orphan install directory to be removed, got err=%v", err)
	}
	if _, ok := findPluginStatus(status, "openai-bot-plugin"); ok {
		t.Fatal("expected orphan plugin to disappear from status after uninstall")
	}

	state, err := layout.ReadPluginState()
	if err != nil {
		t.Fatalf("ReadPluginState: %v", err)
	}
	if _, ok := state.Plugins["openai-bot-plugin"]; ok {
		t.Fatal("expected orphan plugin state to be removed after uninstall")
	}
}

func writeJSONFile(t *testing.T, path string, value any) {
	t.Helper()

	content, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		t.Fatalf("marshal json: %v", err)
	}
	if err := os.WriteFile(path, content, 0o644); err != nil {
		t.Fatalf("write json %s: %v", path, err)
	}
}

func buildPluginArchive(t *testing.T, files map[string]string) []byte {
	t.Helper()

	archivePath := filepath.Join(t.TempDir(), "plugin.zip")
	file, err := os.Create(archivePath)
	if err != nil {
		t.Fatalf("create archive: %v", err)
	}

	zipWriter := zip.NewWriter(file)
	for name, content := range files {
		entryWriter, err := zipWriter.Create(name)
		if err != nil {
			t.Fatalf("create archive entry %s: %v", name, err)
		}
		if _, err := entryWriter.Write([]byte(content)); err != nil {
			t.Fatalf("write archive entry %s: %v", name, err)
		}
	}
	if err := zipWriter.Close(); err != nil {
		t.Fatalf("close archive writer: %v", err)
	}
	if err := file.Close(); err != nil {
		t.Fatalf("close archive file: %v", err)
	}

	content, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("read archive: %v", err)
	}
	return content
}
