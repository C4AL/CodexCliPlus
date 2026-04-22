package service

import (
	"bytes"
	"encoding/json"
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
	sourceManifestFileName          = ".cpad-source.json"
)

type officialBaselineSpec struct {
	ID         string
	Name       string
	Repository string
	LocalPath  string
}

type syncedSourceManifest struct {
	ID         string `json:"id"`
	Name       string `json:"name"`
	SourcePath string `json:"sourcePath"`
	Repository string `json:"repository"`
	Branch     string `json:"branch"`
	Commit     string `json:"commit"`
	SyncedAt   string `json:"syncedAt"`
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
				"CPA-UV 源码覆盖层",
				"managed-source-snapshot",
				product.ResolveCPAOverlaySourceRoot(),
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

	state.UpdatedAt = time.Now().UTC()
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
		buildLocalGitSourceStatus(
			"cpa-source",
			"CPA-UV 源码覆盖层",
			"managed-source-snapshot",
			product.ResolveCPAOverlaySourceRoot(),
		),
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

	if repositorySourcesRoot := product.ResolveRepositorySourcesRoot(); repositorySourcesRoot != "" &&
		product.ResolveRepositoryRoot() != "" &&
		samePath(repositorySourcesRoot, layout.Directories["sources"]) {
		if err := syncRepositorySnapshots(product.ResolveRepositoryRoot()); err != nil {
			return product.UpdateCenterStatus{}, err
		}

		_ = layout.AppendLog("已通过仓库内同步脚本刷新源码快照。")
		return CheckUpdateCenter()
	}

	for _, spec := range officialBaselineSpecs(layout) {
		if err := syncOfficialBaseline(spec, layout.Directories["sources"]); err != nil {
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
			Name:       "官方完整后端基线",
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
		Kind:      "managed-source-snapshot",
		Source:    spec.LocalPath,
		Message:   "本地基线目录尚未建立。",
		UpdatedAt: time.Now().UTC(),
	}

	latestRef, err := readRemoteHead(spec.Repository)
	if err != nil {
		status.Message = fmt.Sprintf("无法读取远端基线 %s：%v", spec.Repository, err)
		return status
	}
	status.LatestRef = latestRef

	sourceInfo, err := os.Stat(spec.LocalPath)
	if err != nil || !sourceInfo.IsDir() {
		status.Message = fmt.Sprintf(
			"远端 main 当前为 %s；本地基线目录不存在。",
			abbreviateRef(status.LatestRef),
		)
		return status
	}

	status.Available = true
	status.CurrentRef, status.Dirty, err = resolveSourceVersionStatus(spec.LocalPath)
	if err != nil {
		status.Message = fmt.Sprintf("无法解析本地基线版本状态：%v", err)
		return status
	}

	if status.CurrentRef == status.LatestRef {
		status.Message = fmt.Sprintf("本地基线已同步到 %s。", abbreviateRef(status.CurrentRef))
	} else {
		status.Message = fmt.Sprintf(
			"本地基线当前为 %s，远端最新为 %s。",
			abbreviateRef(status.CurrentRef),
			abbreviateRef(status.LatestRef),
		)
	}

	if status.Dirty {
		status.Message += " 源码目录有未提交改动。"
	}

	return status
}

func syncRepositorySnapshots(repositoryRoot string) error {
	scriptPath := filepath.Join(repositoryRoot, "scripts", "sync-merged-sources.ps1")
	if _, err := os.Stat(scriptPath); err != nil {
		return fmt.Errorf("未找到仓库内同步脚本：%w", err)
	}

	if _, err := readCommandOutput("powershell", "-ExecutionPolicy", "Bypass", "-File", scriptPath); err != nil {
		return fmt.Errorf("执行仓库内源码同步脚本失败：%w", err)
	}

	return nil
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
			return fmt.Errorf("官方基线路径不是独立 Git 工作树：%s", spec.LocalPath)
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
		return fmt.Errorf(
			"官方基线 origin 不匹配：期望 %s，实际 %s",
			spec.Repository,
			strings.TrimSpace(remoteURL),
		)
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
		Message:   "源码目录尚未就绪。",
		UpdatedAt: time.Now().UTC(),
	}

	sourceInfo, err := os.Stat(sourceRoot)
	if err != nil || !sourceInfo.IsDir() {
		status.Message = "源码目录不存在。"
		return status
	}

	status.Available = true
	status.CurrentRef, status.Dirty, err = resolveSourceVersionStatus(sourceRoot)
	if err != nil {
		status.Message = fmt.Sprintf("无法读取源码版本信息：%v", err)
		return status
	}

	status.Message = fmt.Sprintf("当前源码快照 %s。", abbreviateRef(status.CurrentRef))
	if status.Dirty {
		status.Message += " 源码目录有未提交改动。"
	} else {
		status.Message += " 源码目录干净。"
	}

	return status
}

func resolveSourceVersionStatus(sourceRoot string) (string, bool, error) {
	manifest, err := readSourceManifest(sourceRoot)
	if err != nil {
		return "", false, err
	}

	currentRef := ""
	if manifest != nil {
		currentRef = strings.TrimSpace(manifest.Commit)
	}
	if currentRef == "" {
		currentRef, err = readStandaloneGitHead(sourceRoot)
		if err != nil {
			return "", false, err
		}
	}

	dirty, err := readSourceDirtyState(sourceRoot)
	if err != nil {
		return "", false, err
	}

	return currentRef, dirty, nil
}

func readSourceManifest(sourceRoot string) (*syncedSourceManifest, error) {
	content, err := os.ReadFile(filepath.Join(sourceRoot, sourceManifestFileName))
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}
		return nil, err
	}

	content = bytes.TrimPrefix(content, []byte{0xEF, 0xBB, 0xBF})

	var manifest syncedSourceManifest
	if err := json.Unmarshal(content, &manifest); err != nil {
		return nil, err
	}

	return &manifest, nil
}

func readStandaloneGitHead(sourceRoot string) (string, error) {
	if _, err := os.Stat(filepath.Join(sourceRoot, ".git")); err != nil {
		return "", fmt.Errorf("缺少 %s 且不是独立 Git 工作树", sourceManifestFileName)
	}

	return readGitOutput(sourceRoot, "rev-parse", "HEAD")
}

func readSourceDirtyState(sourceRoot string) (bool, error) {
	if _, err := os.Stat(filepath.Join(sourceRoot, ".git")); err == nil {
		dirtyOutput, err := readGitOutput(sourceRoot, "status", "--short")
		if err != nil {
			return false, err
		}
		return strings.TrimSpace(dirtyOutput) != "", nil
	}

	repositoryRoot, err := readGitOutput(sourceRoot, "rev-parse", "--show-toplevel")
	if err != nil {
		return false, err
	}

	relativePath, err := filepath.Rel(repositoryRoot, sourceRoot)
	if err != nil {
		return false, err
	}

	dirtyOutput, err := readGitOutput(
		repositoryRoot,
		"status",
		"--short",
		"--",
		filepath.Clean(relativePath),
	)
	if err != nil {
		return false, err
	}

	return strings.TrimSpace(dirtyOutput) != "", nil
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
		status.Message = fmt.Sprintf(
			"当前模式 %s 的目标运行时尚未落地：%s",
			codexStatus.Mode,
			codexStatus.TargetPath,
		)
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

func samePath(left string, right string) bool {
	if left == "" || right == "" {
		return false
	}

	leftAbs, leftErr := filepath.Abs(left)
	rightAbs, rightErr := filepath.Abs(right)
	if leftErr != nil || rightErr != nil {
		return strings.EqualFold(filepath.Clean(left), filepath.Clean(right))
	}

	return strings.EqualFold(leftAbs, rightAbs)
}
