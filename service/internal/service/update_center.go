package service

import (
	"bytes"
	"fmt"
	"os"
	"os/exec"
	"strings"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

const officialBaselineRepository = "https://github.com/router-for-me/CLIProxyAPI"

func InspectUpdateCenter() (product.UpdateCenterStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.UpdateCenterStatus{}, err
	}

	state, err := layout.ReadUpdateCenterState()
	if err != nil {
		return product.UpdateCenterStatus{}, err
	}
	if state != nil {
		return *state, nil
	}

	return CheckUpdateCenter()
}

func CheckUpdateCenter() (product.UpdateCenterStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.UpdateCenterStatus{}, err
	}

	sources := []product.UpdateSourceStatus{
		buildOfficialBaselineStatus(),
		buildLocalGitSourceStatus("cpa-source", "CPA-UV 源仓", "git-worktree", product.ResolveCPASourceRoot()),
		buildLocalGitSourceStatus("plugin-source", "插件源仓", "git-worktree", product.ResolvePluginSourceRoot()),
		buildManagedRuntimeStatus(layout),
		buildManagedCodexStatus(layout),
	}

	state := product.UpdateCenterStatus{
		StateFile:   layout.Files["updateCenterState"],
		Sources:     sources,
		UpdatedAt:   time.Now().UTC(),
		ProductName: product.ProductName,
	}

	if err := layout.WriteUpdateCenterState(state); err != nil {
		return product.UpdateCenterStatus{}, err
	}

	_ = layout.AppendLog("更新中心状态已刷新。")
	return state, nil
}

func buildOfficialBaselineStatus() product.UpdateSourceStatus {
	status := product.UpdateSourceStatus{
		ID:        "official-baseline",
		Name:      "官方最新基线",
		Kind:      "remote-git",
		Source:    officialBaselineRepository,
		Message:   "尚未建立本地官方基线工作树。",
		UpdatedAt: time.Now().UTC(),
	}

	latestRef, err := readCommandOutput("git", "ls-remote", officialBaselineRepository, "refs/heads/main")
	if err != nil {
		status.Message = fmt.Sprintf("无法读取官方远端基线：%v", err)
		return status
	}

	fields := strings.Fields(latestRef)
	if len(fields) > 0 {
		status.LatestRef = fields[0]
		status.Message = fmt.Sprintf("官方 main 当前远端头为 %s，等待第三阶段收尾时并入。", abbreviateRef(fields[0]))
	}

	return status
}

func buildLocalGitSourceStatus(id string, name string, kind string, sourceRoot string) product.UpdateSourceStatus {
	status := product.UpdateSourceStatus{
		ID:        id,
		Name:      name,
		Kind:      kind,
		Source:    sourceRoot,
		Message:   "源仓尚未就绪。",
		UpdatedAt: time.Now().UTC(),
	}

	sourceInfo, err := os.Stat(sourceRoot)
	if err != nil || !sourceInfo.IsDir() {
		status.Message = "源目录不存在。"
		return status
	}

	status.CurrentRef, err = readGitOutput(sourceRoot, "rev-parse", "HEAD")
	if err != nil {
		status.Message = fmt.Sprintf("无法读取本地 Git 提交：%v", err)
		return status
	}

	dirtyOutput, err := readGitOutput(sourceRoot, "status", "--short")
	if err == nil {
		status.Dirty = strings.TrimSpace(dirtyOutput) != ""
	}

	status.Message = fmt.Sprintf("当前提交 %s。", abbreviateRef(status.CurrentRef))
	if status.Dirty {
		status.Message += " 工作树有未提交改动。"
	} else {
		status.Message += " 工作树干净。"
	}

	return status
}

func buildManagedRuntimeStatus(layout product.Layout) product.UpdateSourceStatus {
	status := product.UpdateSourceStatus{
		ID:        "managed-cpa-runtime",
		Name:      "受控 CPA Runtime",
		Kind:      "managed-runtime",
		Source:    product.CPAManagedBinaryPath(layout),
		UpdatedAt: time.Now().UTC(),
	}

	runtimeStatus, err := product.ResolveCPARuntime(layout)
	if err != nil {
		status.Message = fmt.Sprintf("无法读取受控 CPA Runtime 状态：%v", err)
		return status
	}

	status.CurrentRef = runtimeStatus.Phase
	status.Available = runtimeStatus.BinaryExists
	status.Message = runtimeStatus.Message
	return status
}

func buildManagedCodexStatus(layout product.Layout) product.UpdateSourceStatus {
	status := product.UpdateSourceStatus{
		ID:        "managed-codex-runtime",
		Name:      "受控 Codex Runtime",
		Kind:      "managed-runtime",
		Source:    layout.Directories["codexRuntime"],
		UpdatedAt: time.Now().UTC(),
	}

	codexStatus, err := product.ResolveCodexShim(layout)
	if err != nil {
		status.Message = fmt.Sprintf("无法读取受控 Codex Runtime 状态：%v", err)
		return status
	}

	status.CurrentRef = string(codexStatus.Mode)
	status.Available = codexStatus.TargetExists
	status.Message = codexStatus.Message
	if !codexStatus.TargetExists {
		status.Message = fmt.Sprintf("当前模式 %s 的目标运行时尚未落地：%s", codexStatus.Mode, codexStatus.TargetPath)
	}

	return status
}

func readGitOutput(workDir string, args ...string) (string, error) {
	command := exec.Command("git", args...)
	command.Dir = workDir
	return runCommand(command)
}

func readCommandOutput(name string, args ...string) (string, error) {
	command := exec.Command(name, args...)
	return runCommand(command)
}

func runCommand(command *exec.Cmd) (string, error) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	command.Stdout = &stdout
	command.Stderr = &stderr

	if err := command.Run(); err != nil {
		errText := strings.TrimSpace(stderr.String())
		if errText == "" {
			errText = err.Error()
		}
		return "", fmt.Errorf(errText)
	}

	return strings.TrimSpace(stdout.String()), nil
}

func abbreviateRef(ref string) string {
	if len(ref) <= 12 {
		return ref
	}

	return ref[:12]
}
