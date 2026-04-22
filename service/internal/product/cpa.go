package product

import (
	"crypto/tls"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"golang.org/x/sys/windows"
	"gopkg.in/yaml.v3"
)

const (
	cpaBuildPackage        = "./cmd/server"
	DefaultCPAPort         = 2723
	ManagedCPABinaryName   = "CPAD-CPA.exe"
	legacyManagedCPABinary = "CPA-UV.exe"
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
	SourceRoot    string                  `json:"sourceRoot"`
	SourceExists  bool                    `json:"sourceExists"`
	BuildPackage  string                  `json:"buildPackage"`
	ManagedBinary string                  `json:"managedBinary"`
	BinaryExists  bool                    `json:"binaryExists"`
	ConfigPath    string                  `json:"configPath"`
	ConfigExists  bool                    `json:"configExists"`
	StateFile     string                  `json:"stateFile"`
	LogPath       string                  `json:"logPath"`
	Phase         string                  `json:"phase"`
	PID           int                     `json:"pid"`
	Running       bool                    `json:"running"`
	Message       string                  `json:"message"`
	UpdatedAt     time.Time               `json:"updatedAt"`
	ConfigInsight CPARuntimeConfigInsight `json:"configInsight"`
	HealthCheck   CPARuntimeHealthCheck   `json:"healthCheck"`
}

type CPARuntimeConfigInsight struct {
	Host                              string `json:"host"`
	Port                              int    `json:"port"`
	TLSEnabled                        bool   `json:"tlsEnabled"`
	BaseURL                           string `json:"baseUrl"`
	HealthURL                         string `json:"healthUrl"`
	ManagementURL                     string `json:"managementUrl"`
	UsageURL                          string `json:"usageUrl"`
	CodexRemoteURL                    string `json:"codexRemoteUrl"`
	ManagementAllowRemote             bool   `json:"managementAllowRemote"`
	ManagementEnabled                 bool   `json:"managementEnabled"`
	ControlPanelEnabled               bool   `json:"controlPanelEnabled"`
	PanelRepository                   string `json:"panelRepository"`
	CodexAppServerProxyEnabled        bool   `json:"codexAppServerProxyEnabled"`
	CodexAppServerRestrictToLocalhost bool   `json:"codexAppServerRestrictToLocalhost"`
	CodexAppServerCodexBin            string `json:"codexAppServerCodexBin"`
}

type CPARuntimeHealthCheck struct {
	Checked    bool      `json:"checked"`
	Healthy    bool      `json:"healthy"`
	StatusCode int       `json:"statusCode"`
	Message    string    `json:"message"`
	CheckedAt  time.Time `json:"checkedAt"`
}

type cpaConfigFile struct {
	Host string `yaml:"host"`
	Port int    `yaml:"port"`
	TLS  struct {
		Enable bool `yaml:"enable"`
	} `yaml:"tls"`
	RemoteManagement struct {
		AllowRemote           bool   `yaml:"allow-remote"`
		SecretKey             string `yaml:"secret-key"`
		DisableControlPanel   bool   `yaml:"disable-control-panel"`
		PanelGitHubRepository string `yaml:"panel-github-repository"`
	} `yaml:"remote-management"`
	CodexAppServerProxy struct {
		Enable              bool   `yaml:"enable"`
		RestrictToLocalhost bool   `yaml:"restrict-to-localhost"`
		CodexBin            string `yaml:"codex-bin"`
	} `yaml:"codex-app-server-proxy"`
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
	return filepath.Join(layout.Directories["cpaRuntime"], ManagedCPABinaryName)
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
	state.ManagedBinary = normalizeCPAManagedBinaryPath(layout, state.ManagedBinary)

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
	state.ManagedBinary = normalizeCPAManagedBinaryPath(layout, state.ManagedBinary)

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
		status.ManagedBinary = normalizeCPAManagedBinaryPath(layout, state.ManagedBinary)
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

	if status.ConfigExists {
		insight, err := inspectCPARuntimeConfig(status.ConfigPath)
		if err != nil {
			status.HealthCheck.Message = fmt.Sprintf("运行时配置解析失败：%v", err)
		} else {
			status.ConfigInsight = insight
			status.HealthCheck = probeCPARuntimeHealth(insight, status.Running)
		}
	} else {
		status.HealthCheck.Message = "运行时配置文件不存在，未执行健康检查。"
	}

	return status, nil
}

func inspectCPARuntimeConfig(configPath string) (CPARuntimeConfigInsight, error) {
	content, err := os.ReadFile(configPath)
	if err != nil {
		return CPARuntimeConfigInsight{}, err
	}

	var cfg cpaConfigFile
	if err := yaml.Unmarshal(content, &cfg); err != nil {
		return CPARuntimeConfigInsight{}, err
	}

	port := cfg.Port
	if port <= 0 {
		port = DefaultCPAPort
	}

	probeHost := normalizeCPARuntimeProbeHost(cfg.Host)
	scheme := "http"
	wsScheme := "ws"
	if cfg.TLS.Enable {
		scheme = "https"
		wsScheme = "wss"
	}

	baseURL := fmt.Sprintf("%s://%s:%d", scheme, probeHost, port)
	codexBin := strings.TrimSpace(cfg.CodexAppServerProxy.CodexBin)
	if codexBin == "" {
		codexBin = "codex"
	}

	return CPARuntimeConfigInsight{
		Host:                              strings.TrimSpace(cfg.Host),
		Port:                              port,
		TLSEnabled:                        cfg.TLS.Enable,
		BaseURL:                           baseURL,
		HealthURL:                         baseURL + "/healthz",
		ManagementURL:                     baseURL + "/management.html",
		UsageURL:                          baseURL + "/backend-api/wham/usage",
		CodexRemoteURL:                    fmt.Sprintf("%s://%s:%d", wsScheme, probeHost, port),
		ManagementAllowRemote:             cfg.RemoteManagement.AllowRemote,
		ManagementEnabled:                 strings.TrimSpace(cfg.RemoteManagement.SecretKey) != "",
		ControlPanelEnabled:               !cfg.RemoteManagement.DisableControlPanel,
		PanelRepository:                   strings.TrimSpace(cfg.RemoteManagement.PanelGitHubRepository),
		CodexAppServerProxyEnabled:        cfg.CodexAppServerProxy.Enable,
		CodexAppServerRestrictToLocalhost: cfg.CodexAppServerProxy.RestrictToLocalhost,
		CodexAppServerCodexBin:            codexBin,
	}, nil
}

func normalizeCPARuntimeProbeHost(rawHost string) string {
	host := strings.Trim(strings.TrimSpace(rawHost), "[]")
	switch strings.ToLower(host) {
	case "", "0.0.0.0", "::", "::1", "localhost":
		return "127.0.0.1"
	default:
		return host
	}
}

func probeCPARuntimeHealth(insight CPARuntimeConfigInsight, running bool) CPARuntimeHealthCheck {
	if strings.TrimSpace(insight.HealthURL) == "" {
		return CPARuntimeHealthCheck{Message: "未解析出 /healthz 地址。"}
	}
	if !running {
		return CPARuntimeHealthCheck{Message: "CPA Runtime 当前未运行，未执行 /healthz 探测。"}
	}

	client := &http.Client{
		Timeout: 1500 * time.Millisecond,
	}
	if insight.TLSEnabled {
		client.Transport = &http.Transport{
			TLSClientConfig: &tls.Config{InsecureSkipVerify: true},
		}
	}

	checkedAt := time.Now().UTC()
	response, err := client.Get(insight.HealthURL)
	if err != nil {
		return CPARuntimeHealthCheck{
			Checked:   true,
			Healthy:   false,
			Message:   fmt.Sprintf("/healthz 请求失败：%v", err),
			CheckedAt: checkedAt,
		}
	}
	defer response.Body.Close()
	_, _ = io.Copy(io.Discard, response.Body)

	healthy := response.StatusCode >= 200 && response.StatusCode < 300
	message := fmt.Sprintf("/healthz 返回 HTTP %d。", response.StatusCode)
	if healthy {
		message = "CPA Runtime 健康检查通过。"
	}

	return CPARuntimeHealthCheck{
		Checked:    true,
		Healthy:    healthy,
		StatusCode: response.StatusCode,
		Message:    message,
		CheckedAt:  checkedAt,
	}
}

func normalizeCPAManagedBinaryPath(layout Layout, managedBinary string) string {
	defaultPath := CPAManagedBinaryPath(layout)
	if strings.TrimSpace(managedBinary) == "" {
		return defaultPath
	}

	cleaned := filepath.Clean(managedBinary)
	if strings.EqualFold(filepath.Base(cleaned), legacyManagedCPABinary) {
		return defaultPath
	}

	return cleaned
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
