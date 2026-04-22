package product

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

const (
	ProductName    = "Cli Proxy API Desktop"
	ServiceName    = "CliProxyAPIDesktopService"
	InstallDirName = "Cli Proxy API Desktop"
	SourceDirName  = "sources"
)

type Layout struct {
	InstallRoot string            `json:"installRoot"`
	Directories map[string]string `json:"directories"`
	Files       map[string]string `json:"files"`
}

type ServiceState struct {
	ProductName string    `json:"productName"`
	ServiceName string    `json:"serviceName"`
	InstallRoot string    `json:"installRoot"`
	Mode        string    `json:"mode"`
	Phase       string    `json:"phase"`
	Message     string    `json:"message"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

func ResolveInstallRoot() string {
	if custom := os.Getenv("CPAD_INSTALL_ROOT"); custom != "" {
		return filepath.Clean(custom)
	}

	executablePath, err := os.Executable()
	if err == nil {
		if packagedRoot := resolveInstallRootFromExecutable(executablePath); packagedRoot != "" {
			return packagedRoot
		}
	}

	return ResolveLegacyHomeInstallRoot()
}

func ResolveLegacyHomeInstallRoot() string {
	homeDir, err := os.UserHomeDir()
	if err != nil {
		return InstallDirName
	}

	return filepath.Join(homeDir, InstallDirName)
}

func ResolveRepositoryRoot() string {
	if custom := os.Getenv("CPAD_REPO_ROOT"); custom != "" {
		if isRepositoryRoot(custom) {
			return filepath.Clean(custom)
		}
	}

	candidates := make([]string, 0, 8)
	if workingDirectory, err := os.Getwd(); err == nil {
		candidates = append(candidates, workingDirectory, filepath.Dir(workingDirectory))
	}

	if executablePath, err := os.Executable(); err == nil {
		executableDir := filepath.Dir(executablePath)
		candidates = append(
			candidates,
			executableDir,
			filepath.Dir(executableDir),
			filepath.Join(executableDir, ".."),
			filepath.Join(executableDir, "..", ".."),
		)
	}

	for _, candidate := range candidates {
		if isRepositoryRoot(candidate) {
			return filepath.Clean(candidate)
		}
	}

	return ""
}

func ResolveRepositorySourcesRoot() string {
	repositoryRoot := ResolveRepositoryRoot()
	if repositoryRoot == "" {
		return ""
	}

	return filepath.Join(repositoryRoot, SourceDirName)
}

func ResolveManagedSourcesRoot(installRoot string) string {
	if repositorySourcesRoot := ResolveRepositorySourcesRoot(); repositorySourcesRoot != "" {
		return repositorySourcesRoot
	}

	candidates := []string{}
	for _, candidate := range []string{
		filepath.Join(installRoot, SourceDirName),
		filepath.Join(installRoot, "resources", SourceDirName),
		filepath.Join(installRoot, "upstream"),
		filepath.Join(ResolveLegacyHomeInstallRoot(), "upstream"),
	} {
		candidates = appendUniquePathCandidate(candidates, candidate)
	}

	if resolved := firstExistingDirectory(candidates); resolved != "" {
		return resolved
	}
	if len(candidates) > 0 {
		return filepath.Clean(candidates[0])
	}

	return filepath.Join(installRoot, SourceDirName)
}

func isRepositoryRoot(candidate string) bool {
	if strings.TrimSpace(candidate) == "" {
		return false
	}

	cleanedCandidate := filepath.Clean(candidate)
	requiredFiles := []string{
		filepath.Join(cleanedCandidate, "package.json"),
		filepath.Join(cleanedCandidate, "service", "go.mod"),
	}
	for _, requiredFile := range requiredFiles {
		info, err := os.Stat(requiredFile)
		if err != nil || info.IsDir() {
			return false
		}
	}

	requiredDirectories := []string{
		filepath.Join(cleanedCandidate, "src"),
		filepath.Join(cleanedCandidate, "service"),
	}
	for _, requiredDirectory := range requiredDirectories {
		info, err := os.Stat(requiredDirectory)
		if err != nil || !info.IsDir() {
			return false
		}
	}

	return true
}

func resolveInstallRootFromExecutable(executablePath string) string {
	cleanedExecutable := filepath.Clean(executablePath)
	lowerExecutable := strings.ToLower(cleanedExecutable)
	if strings.Contains(lowerExecutable, "go-build") {
		return ""
	}

	baseName := strings.ToLower(filepath.Base(cleanedExecutable))
	switch baseName {
	case strings.ToLower(ProductName) + ".exe":
		return filepath.Dir(cleanedExecutable)
	case "cpad-service.exe", "codex.exe":
		binDir := filepath.Dir(cleanedExecutable)
		if !strings.EqualFold(filepath.Base(binDir), "bin") {
			return filepath.Dir(cleanedExecutable)
		}

		parentDir := filepath.Dir(binDir)
		if strings.EqualFold(filepath.Base(parentDir), "resources") {
			return filepath.Dir(parentDir)
		}

		return parentDir
	default:
		return ""
	}
}

func NewLayout() Layout {
	installRoot := ResolveInstallRoot()
	dataDir := filepath.Join(installRoot, "data")
	logsDir := filepath.Join(installRoot, "logs")
	sourcesRoot := ResolveManagedSourcesRoot(installRoot)
	officialCoreBaseline := filepath.Join(sourcesRoot, "official-backend")
	officialPanelBaseline := filepath.Join(sourcesRoot, "official-management-center")
	cpaOverlaySource := filepath.Join(sourcesRoot, "cpa-uv-overlay")

	return Layout{
		InstallRoot: installRoot,
		Directories: map[string]string{
			"data":                  dataDir,
			"codexData":             filepath.Join(dataDir, "codex"),
			"cpaData":               filepath.Join(dataDir, "cpa"),
			"codexRuntime":          filepath.Join(installRoot, "runtime", "codex"),
			"cpaRuntime":            filepath.Join(installRoot, "runtime", "cpa"),
			"plugins":               filepath.Join(installRoot, "plugins"),
			"logs":                  logsDir,
			"tmp":                   filepath.Join(installRoot, "tmp"),
			"sources":               sourcesRoot,
			"upstream":              sourcesRoot,
			"officialCoreBaseline":  officialCoreBaseline,
			"officialPanelBaseline": officialPanelBaseline,
			"cpaOverlaySource":      cpaOverlaySource,
		},
		Files: map[string]string{
			"database":          filepath.Join(dataDir, "app.db"),
			"serviceState":      filepath.Join(dataDir, "service-state.json"),
			"serviceLog":        filepath.Join(logsDir, "service-host.log"),
			"codexMode":         filepath.Join(dataDir, "codex-mode.json"),
			"cpaRuntimeState":   filepath.Join(dataDir, "cpa-runtime.json"),
			"cpaRuntimeLog":     filepath.Join(logsDir, "cpa-runtime.log"),
			"pluginCatalog":     filepath.Join(dataDir, "plugin-catalog.json"),
			"pluginState":       filepath.Join(dataDir, "plugin-state.json"),
			"updateCenterState": filepath.Join(dataDir, "update-center.json"),
		},
	}
}

func (layout Layout) Ensure() error {
	for _, directory := range layout.Directories {
		if err := os.MkdirAll(directory, 0o755); err != nil {
			return fmt.Errorf("create %s: %w", directory, err)
		}
	}

	return nil
}

func (layout Layout) WriteState(mode string, phase string, message string) error {
	state := ServiceState{
		ProductName: ProductName,
		ServiceName: ServiceName,
		InstallRoot: layout.InstallRoot,
		Mode:        mode,
		Phase:       phase,
		Message:     message,
		UpdatedAt:   time.Now().UTC(),
	}

	content, err := json.MarshalIndent(state, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["serviceState"], content, 0o644)
}

func (layout Layout) ReadState() (*ServiceState, error) {
	content, err := os.ReadFile(layout.Files["serviceState"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var state ServiceState
	if err := json.Unmarshal(content, &state); err != nil {
		return nil, err
	}

	return &state, nil
}

func (layout Layout) AppendLog(line string) error {
	return appendFileLog(layout.Files["serviceLog"], line)
}

func appendFileLog(path string, line string) error {
	file, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	defer file.Close()

	_, err = fmt.Fprintf(file, "%s %s\n", time.Now().Format(time.RFC3339), line)
	return err
}
