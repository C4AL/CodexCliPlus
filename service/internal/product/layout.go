package product

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"
)

const (
	ProductName    = "Cli Proxy API Desktop"
	ServiceName    = "CliProxyAPIDesktopService"
	InstallDirName = "Cli Proxy API Desktop"
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

	homeDir, err := os.UserHomeDir()
	if err != nil {
		return InstallDirName
	}

	return filepath.Join(homeDir, InstallDirName)
}

func NewLayout() Layout {
	installRoot := ResolveInstallRoot()
	dataDir := filepath.Join(installRoot, "data")
	logsDir := filepath.Join(installRoot, "logs")

	return Layout{
		InstallRoot: installRoot,
		Directories: map[string]string{
			"data":         dataDir,
			"codexData":    filepath.Join(dataDir, "codex"),
			"cpaData":      filepath.Join(dataDir, "cpa"),
			"codexRuntime": filepath.Join(installRoot, "runtime", "codex"),
			"cpaRuntime":   filepath.Join(installRoot, "runtime", "cpa"),
			"plugins":      filepath.Join(installRoot, "plugins"),
			"logs":         logsDir,
			"tmp":          filepath.Join(installRoot, "tmp"),
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
