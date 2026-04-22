package service

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
	"golang.org/x/sys/windows"
)

func InspectCPARuntime() (product.CPARuntimeStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	return product.ResolveCPARuntime(layout)
}

func BuildCPARuntime() (product.CPARuntimeStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	status, err := product.ResolveCPARuntime(layout)
	if err != nil {
		return product.CPARuntimeStatus{}, err
	}

	if status.Running {
		return status, fmt.Errorf("CPA Runtime 当前仍在运行，pid=%d；请先停止后再重建", status.PID)
	}

	if !status.SourceExists {
		return status, fmt.Errorf("CPA 源目录不存在：%s", status.SourceRoot)
	}

	if _, err := os.Stat(filepath.Join(status.SourceRoot, "go.mod")); err != nil {
		return status, fmt.Errorf("CPA 源目录缺少 go.mod：%w", err)
	}

	buildCommand := exec.Command("go", "build", "-o", status.ManagedBinary, status.BuildPackage)
	buildCommand.Dir = status.SourceRoot

	output, err := buildCommand.CombinedOutput()
	if err != nil {
		message := fmt.Sprintf("CPA Runtime 构建失败：%s", summarizeCommandOutput(output, err))
		_ = persistCPARuntimeState(layout, status, "failed", 0, message)
		return inspectAfterPersist(layout, errors.New(message))
	}

	if err := ensureCPAConfig(layout, status.SourceRoot); err != nil {
		message := fmt.Sprintf("CPA Runtime 配置初始化失败：%v", err)
		_ = persistCPARuntimeState(layout, status, "failed", 0, message)
		return inspectAfterPersist(layout, errors.New(message))
	}

	message := fmt.Sprintf("CPA Runtime 已从 %s 构建到 %s", status.SourceRoot, status.ManagedBinary)
	if err := persistCPARuntimeState(layout, status, "built", 0, message); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	return product.ResolveCPARuntime(layout)
}

func StartCPARuntime() (product.CPARuntimeStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	status, err := product.ResolveCPARuntime(layout)
	if err != nil {
		return product.CPARuntimeStatus{}, err
	}

	if status.Running {
		return status, nil
	}

	if !status.BinaryExists {
		status, err = BuildCPARuntime()
		if err != nil {
			return status, err
		}
	}

	if !status.ConfigExists {
		if err := ensureCPAConfig(layout, status.SourceRoot); err != nil {
			return status, err
		}
		status, err = product.ResolveCPARuntime(layout)
		if err != nil {
			return product.CPARuntimeStatus{}, err
		}
	}

	logFile, err := os.OpenFile(status.LogPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return status, err
	}
	defer logFile.Close()

	startMessage := fmt.Sprintf("正在从 %s 启动 CPA Runtime，配置文件为 %s", status.ManagedBinary, status.ConfigPath)
	if err := layout.AppendCPARuntimeLog(startMessage); err != nil {
		return status, err
	}

	command := exec.Command(status.ManagedBinary, "-config", status.ConfigPath)
	command.Dir = layout.Directories["cpaRuntime"]
	command.Stdout = logFile
	command.Stderr = logFile
	command.SysProcAttr = &syscall.SysProcAttr{
		CreationFlags: windows.CREATE_NEW_PROCESS_GROUP,
		HideWindow:    true,
	}

	if err := command.Start(); err != nil {
		message := fmt.Sprintf("CPA Runtime 启动失败：%v", err)
		_ = persistCPARuntimeState(layout, status, "failed", 0, message)
		return inspectAfterPersist(layout, err)
	}

	pid := command.Process.Pid
	if releaseErr := command.Process.Release(); releaseErr != nil {
		_ = layout.AppendCPARuntimeLog(fmt.Sprintf("警告：释放 CPA Runtime 进程句柄失败，pid=%d：%v", pid, releaseErr))
	}

	message := fmt.Sprintf("CPA Runtime 已启动，pid=%d", pid)
	if err := persistCPARuntimeState(layout, status, "running", pid, message); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	time.Sleep(750 * time.Millisecond)

	inspectedStatus, err := product.ResolveCPARuntime(layout)
	if err != nil {
		return product.CPARuntimeStatus{}, err
	}

	if !inspectedStatus.Running {
		startError := fmt.Errorf("CPA Runtime 启动后立即退出，请检查 %s", inspectedStatus.LogPath)
		_ = persistCPARuntimeState(layout, inspectedStatus, "failed", 0, startError.Error())
		return inspectAfterPersist(layout, startError)
	}

	return inspectedStatus, nil
}

func StopCPARuntime() (product.CPARuntimeStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	status, err := product.ResolveCPARuntime(layout)
	if err != nil {
		return product.CPARuntimeStatus{}, err
	}

	if !status.Running {
		if err := persistCPARuntimeState(layout, status, "stopped", 0, "CPA Runtime 已停止。"); err != nil {
			return product.CPARuntimeStatus{}, err
		}

		return product.ResolveCPARuntime(layout)
	}

	if err := terminateProcess(status.PID); err != nil {
		message := fmt.Sprintf("CPA Runtime 停止失败，pid=%d：%v", status.PID, err)
		_ = persistCPARuntimeState(layout, status, "failed", status.PID, message)
		return inspectAfterPersist(layout, err)
	}

	message := fmt.Sprintf("CPA Runtime 已停止，原 pid=%d", status.PID)
	if err := persistCPARuntimeState(layout, status, "stopped", 0, message); err != nil {
		return product.CPARuntimeStatus{}, err
	}

	return product.ResolveCPARuntime(layout)
}

func ensureCPAConfig(layout product.Layout, sourceRoot string) error {
	configPath := product.CPAManagedConfigPath(layout)
	templatePath := product.CPATemplateConfigPath(sourceRoot)

	templateContent, err := os.ReadFile(templatePath)
	if err != nil {
		return fmt.Errorf("读取配置模板失败：%w", err)
	}

	rewrittenContent := rewriteCPAConfigDefaults(string(templateContent), layout)

	if _, err := os.Stat(configPath); errors.Is(err, os.ErrNotExist) {
		return os.WriteFile(configPath, []byte(rewrittenContent), 0o644)
	} else if err != nil {
		return err
	}

	existingContent, err := os.ReadFile(configPath)
	if err != nil {
		return err
	}

	updatedContent := rewriteCPAConfigDefaults(string(existingContent), layout)
	if updatedContent != string(existingContent) {
		return os.WriteFile(configPath, []byte(updatedContent), 0o644)
	}

	return nil
}

func rewriteCPAConfigDefaults(content string, layout product.Layout) string {
	authDirLine := fmt.Sprintf("auth-dir: '%s'", strings.ReplaceAll(layout.Directories["cpaData"], "'", "''"))
	portLine := fmt.Sprintf("port: %d", product.DefaultCPAPort)

	replacements := []struct {
		old string
		new string
	}{
		{old: `auth-dir: "~/.cli-proxy-api"`, new: authDirLine},
		{old: "port: 8317", new: portLine},
		{old: "port: 12723", new: portLine},
		{old: "ws://127.0.0.1:8317", new: fmt.Sprintf("ws://127.0.0.1:%d", product.DefaultCPAPort)},
		{old: "ws://127.0.0.1:12723", new: fmt.Sprintf("ws://127.0.0.1:%d", product.DefaultCPAPort)},
	}

	rewritten := content
	for _, replacement := range replacements {
		rewritten = strings.ReplaceAll(rewritten, replacement.old, replacement.new)
	}

	return rewritten
}

func persistCPARuntimeState(layout product.Layout, status product.CPARuntimeStatus, phase string, pid int, message string) error {
	state := product.CPARuntimeState{
		SourceRoot:    status.SourceRoot,
		ManagedBinary: status.ManagedBinary,
		ConfigPath:    status.ConfigPath,
		LogPath:       status.LogPath,
		Phase:         phase,
		PID:           pid,
		Message:       message,
	}

	if err := layout.WriteCPARuntimeState(state); err != nil {
		return err
	}

	if message != "" {
		if err := layout.AppendCPARuntimeLog(message); err != nil {
			return err
		}
	}

	return nil
}

func inspectAfterPersist(layout product.Layout, originalErr error) (product.CPARuntimeStatus, error) {
	status, inspectErr := product.ResolveCPARuntime(layout)
	if inspectErr != nil {
		return product.CPARuntimeStatus{}, inspectErr
	}

	return status, originalErr
}

func summarizeCommandOutput(output []byte, commandErr error) string {
	trimmed := strings.TrimSpace(string(output))
	if trimmed == "" {
		return commandErr.Error()
	}

	lines := strings.Split(trimmed, "\n")
	lastLine := strings.TrimSpace(lines[len(lines)-1])
	if lastLine == "" {
		lastLine = trimmed
	}

	if len(lastLine) > 280 {
		lastLine = lastLine[:280] + "..."
	}

	return lastLine
}

func terminateProcess(pid int) error {
	handle, err := windows.OpenProcess(windows.PROCESS_TERMINATE|windows.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		return err
	}
	defer windows.CloseHandle(handle)

	if err := windows.TerminateProcess(handle, 0); err != nil {
		return err
	}

	_, err = windows.WaitForSingleObject(handle, 5_000)
	return err
}
