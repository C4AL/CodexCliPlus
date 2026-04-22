package service

import (
	"bufio"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

var pluginFieldPattern = regexp.MustCompile(`^\s*([A-Za-z0-9_-]+)\s*=\s*"([^"]*)"`)

func fallbackPluginMarketStatus(layout product.Layout) product.PluginMarketStatus {
	status := product.PluginMarketStatus{
		SourceRoot:  product.ResolvePluginSourceRoot(),
		CatalogPath: layout.Files["pluginCatalog"],
		StatePath:   layout.Files["pluginState"],
		PluginsDir:  layout.Directories["plugins"],
	}

	if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
		status.SourceExists = true
	}

	return status
}

func InspectPluginMarket() (product.PluginMarketStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.PluginMarketStatus{}, err
	}

	if _, err := layout.ReadPluginCatalog(); err != nil {
		return fallbackPluginMarketStatus(layout), nil
	} else if _, err := os.Stat(layout.Files["pluginCatalog"]); os.IsNotExist(err) {
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

	sourceRoot := product.ResolvePluginSourceRoot()
	sourceInfo, err := os.Stat(sourceRoot)
	if err != nil {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin source root unavailable: %w", err)
	}
	if !sourceInfo.IsDir() {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin source root is not a directory: %s", sourceRoot)
	}

	entries, err := os.ReadDir(sourceRoot)
	if err != nil {
		return product.PluginMarketStatus{}, err
	}

	catalog := product.PluginCatalog{
		SourceRoot: sourceRoot,
		UpdatedAt:  time.Now().UTC(),
	}

	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}

		sourcePath := filepath.Join(sourceRoot, entry.Name())
		pyprojectPath := filepath.Join(sourcePath, "pyproject.toml")
		if _, err := os.Stat(pyprojectPath); err != nil {
			continue
		}

		pluginEntry, err := parsePluginCatalogEntry(sourcePath, pyprojectPath)
		if err != nil {
			return product.PluginMarketStatus{}, err
		}

		catalog.Plugins = append(catalog.Plugins, pluginEntry)
	}

	if err := layout.WritePluginCatalog(catalog); err != nil {
		return product.PluginMarketStatus{}, err
	}

	message := fmt.Sprintf("插件市场清单已刷新，共发现 %d 个插件源。", len(catalog.Plugins))
	_ = layout.AppendLog(message)

	return product.ResolvePluginMarket(layout)
}

func InstallManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return syncManagedPlugin(id, true)
}

func UpdateManagedPlugin(id string) (product.PluginMarketStatus, error) {
	return syncManagedPlugin(id, false)
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
	if !plugin.SourceExists {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin source is missing: %s", plugin.SourcePath)
	}
	if !installIfMissing && !plugin.Installed {
		return product.PluginMarketStatus{}, fmt.Errorf("plugin is not installed yet: %s", id)
	}

	if err := mirrorPluginSource(layout.Directories["plugins"], plugin.SourcePath, plugin.InstallPath); err != nil {
		return product.PluginMarketStatus{}, err
	}

	state, err := layout.ReadPluginState()
	if err != nil {
		return product.PluginMarketStatus{}, err
	}
	if state == nil {
		state = &product.PluginStateFile{Plugins: map[string]product.PluginRuntimeState{}}
	}

	runtimeState := state.Plugins[id]
	runtimeState.ID = id
	runtimeState.Installed = true
	if runtimeState.UpdatedAt.IsZero() {
		runtimeState.Enabled = true
	}
	runtimeState.InstalledVersion = plugin.Version
	runtimeState.InstallPath = plugin.InstallPath
	if plugin.Installed {
		runtimeState.Message = fmt.Sprintf("插件 %s 已从源目录同步更新到 %s。", id, plugin.InstallPath)
	} else {
		runtimeState.Message = fmt.Sprintf("插件 %s 已安装到 %s。", id, plugin.InstallPath)
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

	runtimeState := state.Plugins[id]
	runtimeState.ID = id
	runtimeState.Installed = true
	runtimeState.Enabled = enabled
	runtimeState.InstalledVersion = plugin.Version
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
		Description: fallbackString(fields["description"], "未填写插件描述。"),
		SourcePath:  sourcePath,
		ReadmePath:  readmePath,
	}, nil
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
		return product.PluginMarketStatus{}, fmt.Errorf("plugin not found in market catalog: %s", id)
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

func buildPluginDiagnosticMessage(plugin product.PluginStatus) string {
	parts := []string{fmt.Sprintf("插件 %s 诊断完成", plugin.ID)}
	if !plugin.SourceExists {
		parts = append(parts, "源目录缺失")
	}
	if !plugin.Installed {
		parts = append(parts, "尚未安装到 CPAD 安装目录")
	}
	if plugin.Installed && !plugin.Enabled {
		parts = append(parts, "当前处于禁用状态")
	}
	if plugin.NeedsUpdate {
		parts = append(parts, "已安装版本落后于源版本")
	}
	if len(parts) == 1 {
		parts = append(parts, "状态正常")
	}

	return strings.Join(parts, "；")
}

func mirrorPluginSource(pluginsRoot string, sourcePath string, targetPath string) error {
	relativePath, err := filepath.Rel(pluginsRoot, targetPath)
	if err != nil {
		return err
	}
	if strings.HasPrefix(relativePath, "..") || relativePath == "." {
		return fmt.Errorf("refusing to sync plugin outside managed plugins directory: %s", targetPath)
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
		if entry.IsDir() {
			return os.MkdirAll(destination, 0o755)
		}

		return copyFile(path, destination)
	})
}

func shouldSkipPluginPath(name string) bool {
	switch name {
	case ".git", "__pycache__", ".venv", ".pytest_cache", ".mypy_cache", ".ruff_cache":
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

func fallbackString(value string, fallback string) string {
	if strings.TrimSpace(value) == "" {
		return fallback
	}

	return value
}
