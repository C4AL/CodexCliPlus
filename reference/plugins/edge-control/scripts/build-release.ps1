[CmdletBinding()]
param(
  [string]$Version,
  [switch]$SkipDependencyInstall
)

$ErrorActionPreference = "Stop"

$PluginRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = Join-Path $PluginRoot "extension\\manifest.json"
$DistDir = Join-Path $PluginRoot "dist"
$StageRoot = Join-Path $DistDir "_stage"
$ScriptsDir = Join-Path $PluginRoot "scripts"
$NodeModulesDir = Join-Path $ScriptsDir "node_modules"

if (-not $Version) {
  $Manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json
  $Version = $Manifest.version
}

if (-not $SkipDependencyInstall -and -not (Test-Path $NodeModulesDir)) {
  Push-Location $ScriptsDir
  try {
    npm ci
  } finally {
    Pop-Location
  }
}

function Reset-Directory([string]$Path) {
  if (Test-Path $Path) {
    Remove-Item -LiteralPath $Path -Recurse -Force
  }
  New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-ReleaseLayout([string]$TargetPath, [bool]$IncludeNodeModules, [bool]$IncludeGitHubWorkflow) {
  $Items = @(
    ".codex-plugin",
    "assets",
    "extension",
    "scripts",
    "skills",
    ".gitignore",
    ".mcp.json",
    "README.md"
  )

  if ($IncludeGitHubWorkflow -and (Test-Path (Join-Path $PluginRoot ".github"))) {
    $Items += ".github"
  }

  foreach ($Item in $Items) {
    Copy-Item -LiteralPath (Join-Path $PluginRoot $Item) -Destination $TargetPath -Recurse -Force
  }

  $GeneratedConfigPath = Join-Path $TargetPath "extension\\config.local.js"
  if (Test-Path $GeneratedConfigPath) {
    Remove-Item -LiteralPath $GeneratedConfigPath -Force
  }

  if (-not $IncludeNodeModules) {
    $CopiedNodeModulesPath = Join-Path $TargetPath "scripts\\node_modules"
    if (Test-Path $CopiedNodeModulesPath) {
      Remove-Item -LiteralPath $CopiedNodeModulesPath -Recurse -Force
    }
  }
}

New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
Reset-Directory $StageRoot

$SourceStage = Join-Path $StageRoot "edge-control-v$Version-source"
$QuickStage = Join-Path $StageRoot "edge-control-v$Version-quick-deploy"

Reset-Directory $SourceStage
Reset-Directory $QuickStage

Copy-ReleaseLayout -TargetPath $SourceStage -IncludeNodeModules:$false -IncludeGitHubWorkflow:$true
Copy-ReleaseLayout -TargetPath $QuickStage -IncludeNodeModules:$true -IncludeGitHubWorkflow:$false

$SourceZipPath = Join-Path $DistDir "edge-control-v$Version-source.zip"
$QuickZipPath = Join-Path $DistDir "edge-control-v$Version-quick-deploy.zip"
$ChecksumsPath = Join-Path $DistDir "checksums-v$Version.txt"
$ReleaseNotesPath = Join-Path $DistDir "release-notes-v$Version.md"

foreach ($ArtifactPath in @($SourceZipPath, $QuickZipPath, $ChecksumsPath, $ReleaseNotesPath)) {
  if (Test-Path $ArtifactPath) {
    Remove-Item -LiteralPath $ArtifactPath -Force
  }
}

Compress-Archive -Path (Join-Path $SourceStage "*") -DestinationPath $SourceZipPath -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $QuickStage "*") -DestinationPath $QuickZipPath -CompressionLevel Optimal

$Hashes = @(
  Get-FileHash -LiteralPath $SourceZipPath -Algorithm SHA256
  Get-FileHash -LiteralPath $QuickZipPath -Algorithm SHA256
)

($Hashes | ForEach-Object { "{0}  {1}" -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) }) -join [Environment]::NewLine |
  Set-Content -LiteralPath $ChecksumsPath -Encoding ascii

$ReleaseNotesLines = @(
  "# Edge Control v$Version",
  "",
  "## Release Contents",
  "",
  "- `edge-control-v$Version-source.zip`: source package for GitHub upload and further development.",
  "- `edge-control-v$Version-quick-deploy.zip`: includes `scripts/node_modules` for fast deployment.",
  "- `checksums-v$Version.txt`: SHA-256 checksums for release assets.",
  "- `scripts/install-from-github-release.ps1`: bootstrap installer for GitHub release based deployment.",
  "",
  "## Quick Deploy",
  "",
  "1. Extract `edge-control-v$Version-quick-deploy.zip`.",
  "2. Run `powershell -ExecutionPolicy Bypass -File .\scripts\quick-deploy.ps1`.",
  "3. The deploy flow forcibly installs the global `edge-browser-ops` Codex skill.",
  "4. If needed, open `edge://extensions` and load the `extension` directory once.",
  "",
  "## GitHub Release Bootstrap Install",
  "",
  "1. Download `scripts/install-from-github-release.ps1` from the repository raw URL.",
  "2. Run it with `-Repository owner/repo`.",
  "3. The script downloads the latest quick-deploy asset and runs the standard installer.",
  "",
  "## Repository Upload",
  "",
  "1. Push the repository to GitHub.",
  "2. Run the `Patch Release` workflow in GitHub Actions.",
  "3. The workflow bumps the patch version, builds assets, creates a tag, and publishes a release."
)

$ReleaseNotesLines | Set-Content -LiteralPath $ReleaseNotesPath -Encoding utf8

Write-Host "Release artifacts created:"
Write-Host "  $SourceZipPath"
Write-Host "  $QuickZipPath"
Write-Host "  $ChecksumsPath"
Write-Host "  $ReleaseNotesPath"
