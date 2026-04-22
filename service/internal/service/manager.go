package service

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/store"
	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/mgr"
)

const serviceDisplayName = "Cli Proxy API Desktop Service"

type ManagerStatus struct {
	ServiceName string `json:"serviceName"`
	Installed   bool   `json:"installed"`
	State       string `json:"state"`
	StartType   string `json:"startType"`
	BinaryPath  string `json:"binaryPath"`
}

type HostSnapshot struct {
	InstallRoot   string                      `json:"installRoot"`
	ServiceState  *product.ServiceState       `json:"serviceState,omitempty"`
	CPARuntime    product.CPARuntimeStatus    `json:"cpaRuntime"`
	Codex         product.CodexShimResolution `json:"codex"`
	PluginMarket  product.PluginMarketStatus  `json:"pluginMarket"`
	UpdateCenter  product.UpdateCenterStatus  `json:"updateCenter"`
	Database      *store.Snapshot             `json:"database,omitempty"`
	ManagerStatus ManagerStatus               `json:"managerStatus"`
}

func InstallService(explicitBinaryPath string) error {
	binaryPath, err := resolveBinaryPath(explicitBinaryPath)
	if err != nil {
		return err
	}

	manager, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer manager.Disconnect()

	existingService, err := manager.OpenService(product.ServiceName)
	if err == nil {
		existingService.Close()
		return fmt.Errorf("service %s is already installed", product.ServiceName)
	}
	if !errors.Is(err, windows.ERROR_SERVICE_DOES_NOT_EXIST) {
		return err
	}

	service, err := manager.CreateService(
		product.ServiceName,
		binaryPath,
		mgr.Config{
			DisplayName:      serviceDisplayName,
			Description:      "Cli Proxy API Desktop 的后台宿主，负责运行时接管、持久化和更新控制。",
			StartType:        mgr.StartAutomatic,
			ErrorControl:     mgr.ErrorNormal,
			DelayedAutoStart: true,
		},
		"service",
	)
	if err != nil {
		return err
	}
	defer service.Close()

	return nil
}

func RemoveService() error {
	manager, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer manager.Disconnect()

	service, err := manager.OpenService(product.ServiceName)
	if err != nil {
		return fmt.Errorf("open service: %w", err)
	}
	defer service.Close()

	status, err := service.Query()
	if err == nil && status.State != svc.Stopped {
		if _, err := service.Control(svc.Stop); err == nil {
			if err := waitForState(service, svc.Stopped, 20*time.Second); err != nil {
				return err
			}
		}
	}

	return service.Delete()
}

func StartService() error {
	manager, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer manager.Disconnect()

	service, err := manager.OpenService(product.ServiceName)
	if err != nil {
		return fmt.Errorf("open service: %w", err)
	}
	defer service.Close()

	status, err := service.Query()
	if err == nil && status.State == svc.Running {
		return nil
	}

	if err := service.Start(); err != nil {
		return err
	}

	return waitForState(service, svc.Running, 20*time.Second)
}

func StopService() error {
	manager, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer manager.Disconnect()

	service, err := manager.OpenService(product.ServiceName)
	if err != nil {
		return fmt.Errorf("open service: %w", err)
	}
	defer service.Close()

	status, err := service.Query()
	if err == nil && status.State == svc.Stopped {
		return nil
	}

	if _, err := service.Control(svc.Stop); err != nil {
		return err
	}

	return waitForState(service, svc.Stopped, 20*time.Second)
}

func QueryServiceStatus() (ManagerStatus, error) {
	status := ManagerStatus{
		ServiceName: product.ServiceName,
		State:       "not-installed",
	}

	manager, err := mgr.Connect()
	if err != nil {
		status.State = "unavailable"
		return status, nil
	}
	defer manager.Disconnect()

	service, err := manager.OpenService(product.ServiceName)
	if err != nil {
		if errors.Is(err, windows.ERROR_SERVICE_DOES_NOT_EXIST) {
			return status, nil
		}

		status.State = "unavailable"
		return status, nil
	}
	defer service.Close()

	status.Installed = true

	queryStatus, err := service.Query()
	if err != nil {
		status.State = "unavailable"
		return status, nil
	}
	status.State = serviceStateName(queryStatus.State)

	config, err := service.Config()
	if err != nil {
		return status, nil
	}

	status.StartType = startTypeName(config.StartType)
	status.BinaryPath = config.BinaryPathName

	return status, nil
}

func GetCodexShimStatus() (product.CodexShimResolution, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CodexShimResolution{}, err
	}

	return product.ResolveCodexShim(layout)
}

func SetCodexMode(rawMode string) (product.CodexShimResolution, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CodexShimResolution{}, err
	}

	mode, err := product.ParseCodexMode(rawMode)
	if err != nil {
		return product.CodexShimResolution{}, err
	}

	message := fmt.Sprintf("Codex 模式已切换为 %s；切换仅影响之后新启动的 Codex 会话。", mode)
	if err := layout.WriteCodexMode(mode, message); err != nil {
		return product.CodexShimResolution{}, err
	}

	if err := layout.AppendLog(message); err != nil {
		return product.CodexShimResolution{}, err
	}

	database, err := store.Open(layout.Files["database"])
	if err == nil {
		defer database.Close()
		_ = database.UpdateCodexMode(string(mode), message)
	}

	return product.ResolveCodexShim(layout)
}

func LoadHostSnapshot() (HostSnapshot, error) {
	layout := product.NewLayout()
	snapshot := HostSnapshot{
		InstallRoot: layout.InstallRoot,
	}

	if err := layout.Ensure(); err != nil {
		return HostSnapshot{}, err
	}

	codexResolution, err := product.ResolveCodexShim(layout)
	if err != nil {
		return HostSnapshot{}, err
	}
	snapshot.Codex = codexResolution

	cpaRuntime, err := product.ResolveCPARuntime(layout)
	if err != nil {
		return HostSnapshot{}, err
	}
	snapshot.CPARuntime = cpaRuntime

	pluginMarket, err := InspectPluginMarket()
	if err != nil {
		return HostSnapshot{}, err
	}
	snapshot.PluginMarket = pluginMarket

	updateCenter, err := InspectUpdateCenter()
	if err != nil {
		return HostSnapshot{}, err
	}
	snapshot.UpdateCenter = updateCenter

	managerStatus, err := QueryServiceStatus()
	if err != nil {
		return HostSnapshot{}, err
	}

	snapshot.ManagerStatus = managerStatus

	state, err := layout.ReadState()
	if err == nil {
		snapshot.ServiceState = state
	}

	database, err := store.Open(layout.Files["database"])
	if err == nil {
		defer database.Close()

		dbSnapshot, snapshotErr := database.Snapshot()
		if snapshotErr == nil {
			snapshot.Database = &dbSnapshot
		}
	}

	return snapshot, nil
}

func waitForState(service *mgr.Service, desired svc.State, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)

	for time.Now().Before(deadline) {
		status, err := service.Query()
		if err != nil {
			return err
		}

		if status.State == desired {
			return nil
		}

		time.Sleep(300 * time.Millisecond)
	}

	return fmt.Errorf("timeout waiting for service state %s", serviceStateName(desired))
}

func resolveBinaryPath(explicitBinaryPath string) (string, error) {
	if explicitBinaryPath != "" {
		return filepath.Abs(explicitBinaryPath)
	}

	executablePath, err := os.Executable()
	if err != nil {
		return "", err
	}

	executablePath, err = filepath.Abs(executablePath)
	if err != nil {
		return "", err
	}

	lowerPath := strings.ToLower(executablePath)
	if strings.Contains(lowerPath, "go-build") {
		return "", fmt.Errorf("refusing to install a temporary go-build executable; build service/bin/cpad-service.exe first or pass an explicit binary path")
	}

	return executablePath, nil
}

func serviceStateName(state svc.State) string {
	switch state {
	case svc.Stopped:
		return "stopped"
	case svc.StartPending:
		return "start-pending"
	case svc.StopPending:
		return "stop-pending"
	case svc.Running:
		return "running"
	case svc.ContinuePending:
		return "continue-pending"
	case svc.PausePending:
		return "pause-pending"
	case svc.Paused:
		return "paused"
	default:
		return "unknown"
	}
}

func startTypeName(startType uint32) string {
	switch startType {
	case mgr.StartAutomatic:
		return "automatic"
	case mgr.StartManual:
		return "manual"
	case mgr.StartDisabled:
		return "disabled"
	default:
		return fmt.Sprintf("unknown(%d)", startType)
	}
}
