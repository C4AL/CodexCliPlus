package service

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/store"
	"golang.org/x/sys/windows/svc"
)

type Runner struct {
	layout product.Layout
	store  *store.DB
}

type WindowsService struct {
	runner *Runner
}

func NewRunner() *Runner {
	return &Runner{layout: product.NewLayout()}
}

func NewWindowsService() *WindowsService {
	return &WindowsService{runner: NewRunner()}
}

func RunWindowsService() error {
	return svc.Run(product.ServiceName, NewWindowsService())
}

func (runner *Runner) ensureStore() error {
	if runner.store != nil {
		return nil
	}

	database, err := store.Open(runner.layout.Files["database"])
	if err != nil {
		return err
	}

	runner.store = database
	return nil
}

func (runner *Runner) initialize(mode string) error {
	if err := runner.layout.Ensure(); err != nil {
		return err
	}

	if err := runner.ensureStore(); err != nil {
		return err
	}

	codexModeState, err := runner.layout.EnsureCodexModeState()
	if err != nil {
		return err
	}

	if runner.store != nil {
		if err := runner.store.UpdateCodexMode(string(codexModeState.Mode), codexModeState.Message); err != nil {
			return err
		}
	}

	return runner.persistState(mode, "starting", fmt.Sprintf("服务宿主正在以 %s 模式初始化。", mode), true)
}

func (runner *Runner) close() error {
	if runner.store == nil {
		return nil
	}

	err := runner.store.Close()
	runner.store = nil
	return err
}

func (runner *Runner) persistState(mode string, phase string, message string, recordEvent bool) error {
	if err := runner.layout.WriteState(mode, phase, message); err != nil {
		return err
	}

	if runner.store != nil {
		if err := runner.store.UpdateRuntimeState(mode, phase); err != nil {
			return err
		}

		if recordEvent {
			if err := runner.store.AppendEvent(mode, phase, message); err != nil {
				return err
			}
		}
	}

	if message != "" {
		if err := runner.layout.AppendLog(message); err != nil {
			return err
		}
	}

	return nil
}

func (runner *Runner) RunInteractive(ctx context.Context) error {
	if err := runner.initialize("interactive"); err != nil {
		return err
	}
	defer runner.close()

	if err := runner.persistState("interactive", "running", "交互式服务宿主已进入运行状态。", true); err != nil {
		return err
	}

	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()

	signals := make(chan os.Signal, 1)
	signal.Notify(signals, os.Interrupt, syscall.SIGTERM)
	defer signal.Stop(signals)

	for {
		select {
		case <-ctx.Done():
			return runner.persistState("interactive", "stopped", "交互式服务宿主因上下文取消而停止。", true)
		case <-signals:
			return runner.persistState("interactive", "stopped", "交互式服务宿主收到停止信号。", true)
		case <-ticker.C:
			if err := runner.persistState("interactive", "running", "", false); err != nil {
				return err
			}
		}
	}
}

func (serviceHost *WindowsService) Execute(_ []string, requests <-chan svc.ChangeRequest, changes chan<- svc.Status) (bool, uint32) {
	const accepted = svc.AcceptStop | svc.AcceptShutdown

	changes <- svc.Status{State: svc.StartPending}
	defer serviceHost.runner.close()

	if err := serviceHost.runner.initialize("service"); err != nil {
		_ = serviceHost.runner.persistState("service", "failed", fmt.Sprintf("service initialization failed: %v", err), true)
		return true, 1
	}

	if err := serviceHost.runner.persistState("service", "running", "服务宿主已进入运行状态。", true); err != nil {
		_ = serviceHost.runner.layout.AppendLog(fmt.Sprintf("服务状态写入失败：%v", err))
		return true, 1
	}

	changes <- svc.Status{State: svc.Running, Accepts: accepted}

	for {
		request := <-requests

		switch request.Cmd {
		case svc.Interrogate:
			changes <- request.CurrentStatus
		case svc.Stop, svc.Shutdown:
			_ = serviceHost.runner.persistState("service", "stopping", "服务宿主收到停止请求。", true)
			changes <- svc.Status{State: svc.StopPending}
			_ = serviceHost.runner.persistState("service", "stopped", "服务宿主已停止。", true)
			return false, 0
		default:
		}
	}
}
