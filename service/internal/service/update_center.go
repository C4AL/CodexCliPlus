package service

import (
	"bytes"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
)

const (
	officialCoreBaselineRepository  = "https://github.com/router-for-me/CLIProxyAPI"
	officialPanelBaselineRepository = "https://github.com/router-for-me/Cli-Proxy-API-Management-Center"
)

type officialBaselineSpec struct {
	ID         string
	Name       string
	Repository string
	LocalPath  string
}

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
		refreshCachedUpdateCenterSources(layout, state)
		return *state, nil
	}

	return CheckUpdateCenter()
}

func refreshCachedUpdateCenterSources(layout product.Layout, state *product.UpdateCenterStatus) {
	if state == nil {
		return
	}

	for index := range state.Sources {
		switch state.Sources[index].ID {
		case "cpa-source":
			state.Sources[index] = buildLocalGitSourceStatus(
				"cpa-source",
				"CPA-UV 源仓",
				"git-worktree",
				product.ResolveCPASourceRoot(),
			)
		case "plugin-source":
			state.Sources[index] = buildLocalGitSourceStatus(
				"plugin-source",
				"插件源仓",
				"git-worktree",
				product.ResolvePluginSourceRoot(),
			)
		case "managed-cpa-runtime":
			state.Sources[index] = buildManagedRuntimeStatus(layout)
		case "managed-codex-runtime":
			state.Sources[index] = buildManagedCodexStatus(layout)
		}
	}
}

func CheckUpdateCenter() (product.UpdateCenterStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.UpdateCenterStatus{}, err
	}

	baselineSpecs := officialBaselineSpecs(layout)
	sources := make([]product.UpdateSourceStatus, 0, len(baselineSpecs)+4)
	for _, spec := range baselineSpecs {
		sources = append(sources, buildOfficialBaselineStatus(spec))
	}
	sources = append(
		sources,
		buildLocalGitSourceStatus("cpa-source", "CPA-UV 源仓", "git-worktree", product.ResolveCPASourceRoot()),
		buildLocalGitSourceStatus("plugin-source", "插件源仓", "git-worktree", product.ResolvePluginSourceRoot()),
		buildManagedRuntimeStatus(layout),
		buildManagedCodexStatus(layout),
	)

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

func SyncOfficialBaselines() (product.UpdateCenterStatus, error) {
	layout := product.NewLayout()
	if err := layout.Ensure(); err != nil {
		return product.UpdateCenterStatus{}, err
	}

	for _, spec := range officialBaselineSpecs(layout) {
		if err := syncOfficialBaseline(spec, layout.Directories["upstream"]); err != nil {
			return product.UpdateCenterStatus{}, err
		}
	}

	_ = layout.AppendLog("官方双基线工作树已同步。")
	return CheckUpdateCenter()
}

func officialBaselineSpecs(layout product.Layout) []officialBaselineSpec {
	return []officialBaselineSpec{
		{
			ID:         "official-core-baseline",
			Name:       "官方主程序基线",
			Repository: officialCoreBaselineRepository,
			LocalPath:  layout.Directories["officialCoreBaseline"],
		},
		{
			ID:         "official-panel-baseline",
			Name:       "官方管理中心基线",
			Repository: officialPanelBaselineRepository,
			LocalPath:  layout.Directories["officialPanelBaseline"],
		},
	}
}

func buildOfficialBaselineStatus(spec officialBaselineSpec) product.UpdateSourceStatus {
	status := product.UpdateSourceStatus{
		ID:        spec.ID,
		Name:      spec.Name,
		Kind:      "managed-git-baseline",
		Source:    spec.LocalPath,
		Message:   "尚未建立本地官方基线工作树。",
		UpdatedAt: time.Now().UTC(),
	}

	latestRef, err := readRemoteHead(spec.Repository)
	if err != nil {
		status.Message = fmt.Sprintf("无法读取远端官方基线 %s：%v", spec.Repository, err)
		return status
	}
	status.LatestRef = latestRef

	sourceInfo, err := os.Stat(spec.LocalPath)
	if err != nil || !sourceInfo.IsDir() {
		status.Message = fmt.Sprintf(
			"远端 main 当前为 %s；本地工作树尚未建立，可执行同步建立基线。",
			abbreviateRef(status.LatestRef),
		)
		return status
	}

	status.Available = true

	status.CurrentRef, err = readGitOutput(spec.LocalPath, "rev-parse", "HEAD")
	if err != nil {
		status.Message = fmt.Sprintf("本地工作树已存在，但无法读取 Git 提交：%v", err)
		return status
	}

	dirtyOutput, err := readGitOutput(spec.LocalPath, "status", "--short")
	if err == nil {
		status.Dirty = strings.TrimSpace(dirtyOutput) != ""
	}

	if status.CurrentRef == status.LatestRef {
		status.Message = fmt.Sprintf(
			"本地基线已同步到 %s。",
			abbreviateRef(status.CurrentRef),
		)
	} else {
		status.Message = fmt.Sprintf(
			"本地基线当前为 %s，远端最新为 %s。",
			abbreviateRef(status.CurrentRef),
			abbreviateRef(status.LatestRef),
		)
	}

	if status.Dirty {
		status.Message += " 工作树有未提交改动。"
	}

	return status
}

func syncOfficialBaseline(spec officialBaselineSpec, managedRoot string) error {
	if err := ensureManagedPath(managedRoot, spec.LocalPath); err != nil {
		return err
	}

	if err := os.MkdirAll(filepath.Dir(spec.LocalPath), 0o755); err != nil {
		return fmt.Errorf("创建官方基线目录失败：%w", err)
	}

	if _, err := os.Stat(spec.LocalPath); os.IsNotExist(err) {
	} else if err != nil {
		return err
	}

	if _, err := os.Stat(filepath.Join(spec.LocalPath, ".git")); err != nil {
		entries, readDirErr := os.ReadDir(spec.LocalPath)
		if readDirErr != nil {
			return readDirErr
		}
		if len(entries) != 0 {
			return fmt.Errorf("官方基线路径不是 Git 工作树：%s", spec.LocalPath)
		}
		if err := os.Remove(spec.LocalPath); err != nil {
			return fmt.Errorf("清理空官方基线目录失败：%w", err)
		}
		_, cloneErr := readCommandOutput(
			"git",
			"clone",
			"--branch",
			"main",
			"--single-branch",
			"--depth",
			"1",
			spec.Repository,
			spec.LocalPath,
		)
		if cloneErr != nil {
			return fmt.Errorf("克隆官方基线失败 %s：%w", spec.Repository, cloneErr)
		}
		return nil
	}

	remoteURL, err := readGitOutput(spec.LocalPath, "remote", "get-url", "origin")
	if err != nil {
		return fmt.Errorf("读取官方基线 origin 失败：%w", err)
	}
	if strings.TrimSpace(remoteURL) != spec.Repository {
		return fmt.Errorf("官方基线 origin 不匹配：期望 %s，实际 %s", spec.Repository, strings.TrimSpace(remoteURL))
	}

	dirtyOutput, err := readGitOutput(spec.LocalPath, "status", "--short")
	if err == nil && strings.TrimSpace(dirtyOutput) != "" {
		return fmt.Errorf("官方基线工作树存在未提交改动，拒绝覆盖：%s", spec.LocalPath)
	}

	if _, err := readGitOutput(spec.LocalPath, "fetch", "--depth", "1", "origin", "main"); err != nil {
		return fmt.Errorf("抓取官方基线失败：%w", err)
	}

	if _, err := readGitOutput(spec.LocalPath, "checkout", "-B", "main", "FETCH_HEAD"); err != nil {
		return fmt.Errorf("更新官方基线工作树失败：%w", err)
	}

	return nil
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

func readRemoteHead(repository string) (string, error) {
	latestRef, err := readCommandOutput("git", "ls-remote", repository, "refs/heads/main")
	if err != nil {
		return "", err
	}

	fields := strings.Fields(latestRef)
	if len(fields) == 0 {
		return "", fmt.Errorf("remote main ref is empty")
	}

	return fields[0], nil
}

func ensureManagedPath(parent string, child string) error {
	absoluteParent, err := filepath.Abs(parent)
	if err != nil {
		return err
	}

	absoluteChild, err := filepath.Abs(child)
	if err != nil {
		return err
	}

	relativePath, err := filepath.Rel(absoluteParent, absoluteChild)
	if err != nil {
		return err
	}

	if relativePath == ".." || strings.HasPrefix(relativePath, ".."+string(os.PathSeparator)) {
		return fmt.Errorf("refusing to operate outside managed baseline root: %s", absoluteChild)
	}

	return nil
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
		return "", errors.New(errText)
	}

	return strings.TrimSpace(stdout.String()), nil
}

func abbreviateRef(ref string) string {
	if len(ref) <= 12 {
		return ref
	}

	return ref[:12]
}
