package product

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"golang.org/x/sys/windows"
)

const (
	cpaBuildPackage = "./cmd/server"
	DefaultCPAPort  = 2723
)

type CPARuntimeState struct {
	ProductName   string    `json:"productName"`
	SourceRoot    string    `json:"sourceRoot"`
	ManagedBinary string    `json:"managedBinary"`
	ConfigPath    string    `json:"configPath"`
	LogPath       string    `json:"logPath"`
	Phase         string    `json:"phase"`
	PID           int       `json:"pid"`
	Message       string    `json:"message"`
	UpdatedAt     time.Time `json:"updatedAt"`
}

type CPARuntimeStatus struct {
	SourceRoot    string    `json:"sourceRoot"`
	SourceExists  bool      `json:"sourceExists"`
	BuildPackage  string    `json:"buildPackage"`
	ManagedBinary string    `json:"managedBinary"`
	BinaryExists  bool      `json:"binaryExists"`
	ConfigPath    string    `json:"configPath"`
	ConfigExists  bool      `json:"configExists"`
	StateFile     string    `json:"stateFile"`
	LogPath       string    `json:"logPath"`
	Phase         string    `json:"phase"`
	PID           int       `json:"pid"`
	Running       bool      `json:"running"`
	Message       string    `json:"message"`
	UpdatedAt     time.Time `json:"updatedAt"`
}

func ResolveCPASourceRoot() string {
	if custom := os.Getenv("CPAD_CPA_SOURCE_ROOT"); custom != "" {
		return filepath.Clean(custom)
	}

	homeDir, err := os.UserHomeDir()
	if err != nil {
		return "CPA-UV-publish"
	}

	return filepath.Join(homeDir, "workspace", "CPA-UV-publish")
}

func CPAManagedBinaryPath(layout Layout) string {
	return filepath.Join(layout.Directories["cpaRuntime"], "CPA-UV.exe")
}

func CPAManagedConfigPath(layout Layout) string {
	return filepath.Join(layout.Directories["cpaRuntime"], "config.yaml")
}

func CPATemplateConfigPath(sourceRoot string) string {
	return filepath.Join(sourceRoot, "config.example.yaml")
}

func (layout Layout) AppendCPARuntimeLog(line string) error {
	return appendFileLog(layout.Files["cpaRuntimeLog"], line)
}

func (layout Layout) WriteCPARuntimeState(state CPARuntimeState) error {
	if state.ProductName == "" {
		state.ProductName = ProductName
	}

	if state.SourceRoot == "" {
		state.SourceRoot = ResolveCPASourceRoot()
	}

	if state.ManagedBinary == "" {
		state.ManagedBinary = CPAManagedBinaryPath(layout)
	}

	if state.ConfigPath == "" {
		state.ConfigPath = CPAManagedConfigPath(layout)
	}

	if state.LogPath == "" {
		state.LogPath = layout.Files["cpaRuntimeLog"]
	}

	if state.Phase == "" {
		state.Phase = "not-initialized"
	}

	if state.UpdatedAt.IsZero() {
		state.UpdatedAt = time.Now().UTC()
	}

	content, err := json.MarshalIndent(state, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(layout.Files["cpaRuntimeState"], content, 0o644)
}

func (layout Layout) ReadCPARuntimeState() (*CPARuntimeState, error) {
	content, err := os.ReadFile(layout.Files["cpaRuntimeState"])
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}

		return nil, err
	}

	var state CPARuntimeState
	if err := json.Unmarshal(content, &state); err != nil {
		return nil, err
	}

	if state.SourceRoot == "" {
		state.SourceRoot = ResolveCPASourceRoot()
	}

	if state.ManagedBinary == "" {
		state.ManagedBinary = CPAManagedBinaryPath(layout)
	}

	if state.ConfigPath == "" {
		state.ConfigPath = CPAManagedConfigPath(layout)
	}

	if state.LogPath == "" {
		state.LogPath = layout.Files["cpaRuntimeLog"]
	}

	if state.Phase == "" {
		state.Phase = "not-initialized"
	}

	return &state, nil
}

func ResolveCPARuntime(layout Layout) (CPARuntimeStatus, error) {
	status := CPARuntimeStatus{
		SourceRoot:    ResolveCPASourceRoot(),
		BuildPackage:  cpaBuildPackage,
		ManagedBinary: CPAManagedBinaryPath(layout),
		ConfigPath:    CPAManagedConfigPath(layout),
		StateFile:     layout.Files["cpaRuntimeState"],
		LogPath:       layout.Files["cpaRuntimeLog"],
		Phase:         "not-initialized",
		Message:       "CPA Runtime 尚未纳入受控运行。",
	}

	state, err := layout.ReadCPARuntimeState()
	if err != nil {
		return CPARuntimeStatus{}, err
	}

	if state != nil {
		status.SourceRoot = state.SourceRoot
		status.ManagedBinary = state.ManagedBinary
		status.ConfigPath = state.ConfigPath
		status.LogPath = state.LogPath
		status.Phase = state.Phase
		status.Message = state.Message
		status.UpdatedAt = state.UpdatedAt
		status.Running = isProcessRunning(state.PID)
		if status.Running {
			status.PID = state.PID
		}

		if state.PID > 0 && state.Phase == "running" && !status.Running {
			status.Phase = "stopped"
			status.Message = fmt.Sprintf("上次记录的 CPA Runtime 进程 %d 已不在运行，需要重新启动。", state.PID)
		}
	}

	if status.SourceRoot != "" {
		if sourceInfo, err := os.Stat(status.SourceRoot); err == nil && sourceInfo.IsDir() {
			status.SourceExists = true
		}
	}

	if _, err := os.Stat(status.ManagedBinary); err == nil {
		status.BinaryExists = true
	}

	if _, err := os.Stat(status.ConfigPath); err == nil {
		status.ConfigExists = true
	}

	return status, nil
}

func isProcessRunning(pid int) bool {
	if pid <= 0 {
		return false
	}

	handle, err := windows.OpenProcess(windows.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		return false
	}
	defer windows.CloseHandle(handle)

	waitStatus, err := windows.WaitForSingleObject(handle, 0)
	if err != nil {
		return false
	}

	return waitStatus == uint32(windows.WAIT_TIMEOUT)
}
