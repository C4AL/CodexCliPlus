package shim

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

type Runner struct {
	layout product.Layout
}

func NewRunner() *Runner {
	return &Runner{layout: product.NewLayout()}
}

func (runner *Runner) Inspect() (product.CodexShimResolution, error) {
	if err := runner.layout.Ensure(); err != nil {
		return product.CodexShimResolution{}, err
	}

	return product.ResolveCodexShim(runner.layout)
}

func (runner *Runner) Run(args []string) error {
	resolution, err := runner.Inspect()
	if err != nil {
		return err
	}

	if !resolution.TargetExists {
		return fmt.Errorf(
			"Codex 当前模式 %s 对应的目标 %s 不存在；请先补齐 %s 下的受控运行时，或切换模式",
			resolution.Mode,
			resolution.TargetPath,
			runner.layout.InstallRoot,
		)
	}
	if !resolution.LaunchReady {
		if strings.TrimSpace(resolution.LaunchMessage) != "" {
			return errors.New(resolution.LaunchMessage)
		}
		return fmt.Errorf("Codex 当前模式 %s 尚未达到可启动条件", resolution.Mode)
	}
	if resolution.Mode == product.CodexModeCPA {
		runtimeStatus, runtimeErr := product.ResolveCPARuntime(runner.layout)
		if runtimeErr != nil {
			return runtimeErr
		}
		if !runtimeStatus.Running {
			return fmt.Errorf("CPA 模式要求受控 CPA Runtime 先运行；当前状态为 %s", runtimeStatus.Phase)
		}
	}

	command, err := buildCommand(resolution.TargetPath, append(resolution.LaunchArgs, args...))
	if err != nil {
		return err
	}

	command.Stdin = os.Stdin
	command.Stdout = os.Stdout
	command.Stderr = os.Stderr

	return command.Run()
}

func buildCommand(targetPath string, args []string) (*exec.Cmd, error) {
	extension := strings.ToLower(filepath.Ext(targetPath))
	if extension == ".cmd" || extension == ".bat" {
		escaped := make([]string, 0, len(args)+1)
		escaped = append(escaped, syscall.EscapeArg(targetPath))
		for _, arg := range args {
			escaped = append(escaped, syscall.EscapeArg(arg))
		}

		return exec.Command("cmd.exe", "/d", "/s", "/c", strings.Join(escaped, " ")), nil
	}

	return exec.Command(targetPath, args...), nil
}
