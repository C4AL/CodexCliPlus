[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Repository,
  [string]$Tag = "latest",
  [string]$InstallRoot,
  [switch]$InstallDependencies,
  [switch]$SkipBridgeStart,
  [switch]$SkipMcpRegistration
)

$ErrorActionPreference = "Stop"

function Get-SafeRepositoryName([string]$Value) {
  return (($Value -replace "[^A-Za-z0-9._-]", "-").Trim("-"))
}

function Assert-PathWithin([string]$RootPath, [string]$TargetPath) {
  $RootFullPath = [System.IO.Path]::GetFullPath($RootPath)
  $TargetFullPath = [System.IO.Path]::GetFullPath($TargetPath)

  if (-not $TargetFullPath.StartsWith($RootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to operate outside the install root: $TargetFullPath"
  }
}

function Get-ReleaseApiUrl([string]$Repo, [string]$ReleaseTag) {
  if ($ReleaseTag -eq "latest") {
    return "https://api.github.com/repos/$Repo/releases/latest"
  }

  return "https://api.github.com/repos/$Repo/releases/tags/$ReleaseTag"
}

function Find-QuickDeployAsset($ReleasePayload) {
  $QuickDeployAsset = $ReleasePayload.assets |
    Where-Object { $_.name -like "edge-control-v*-quick-deploy.zip" } |
    Select-Object -First 1

  if ($QuickDeployAsset) {
    return $QuickDeployAsset
  }

  return $ReleasePayload.assets |
    Where-Object { $_.name -like "*.zip" } |
    Select-Object -First 1
}

function Resolve-QuickDeployPath([string]$RootPath) {
  $DirectPath = Join-Path $RootPath "scripts\quick-deploy.ps1"
  if (Test-Path $DirectPath) {
    return $DirectPath
  }

  $Candidate = Get-ChildItem -LiteralPath $RootPath -Recurse -Filter "quick-deploy.ps1" -File |
    Where-Object { $_.FullName -like "*\scripts\quick-deploy.ps1" } |
    Select-Object -First 1

  if ($Candidate) {
    return $Candidate.FullName
  }

  throw "quick-deploy.ps1 was not found in the downloaded release package."
}

$Headers = @{
  "Accept" = "application/vnd.github+json"
  "User-Agent" = "edge-control-release-installer"
}

$ReleaseApiUrl = Get-ReleaseApiUrl -Repo $Repository -ReleaseTag $Tag
$Release = Invoke-RestMethod -Uri $ReleaseApiUrl -Headers $Headers
$Asset = Find-QuickDeployAsset -ReleasePayload $Release

if (-not $Asset) {
  throw "Could not find a quick-deploy zip asset in GitHub release '$($Release.tag_name)'."
}

$SafeRepositoryName = Get-SafeRepositoryName $Repository
$DefaultInstallParent = Join-Path $env:LOCALAPPDATA "CodexEdgeControl\github-release"

if (-not $InstallRoot) {
  $InstallRoot = Join-Path $DefaultInstallParent $SafeRepositoryName
}

$InstallParent = Split-Path -Parent $InstallRoot
if (-not $InstallParent) {
  throw "Install root must include a parent directory."
}

New-Item -ItemType Directory -Path $DefaultInstallParent -Force | Out-Null
New-Item -ItemType Directory -Path $InstallParent -Force | Out-Null
Assert-PathWithin -RootPath $DefaultInstallParent -TargetPath $InstallRoot

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("edge-control-install-" + [System.Guid]::NewGuid().ToString("N"))
$ZipPath = Join-Path $TempRoot $Asset.name
$ExtractPath = Join-Path $TempRoot "package"

New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
New-Item -ItemType Directory -Path $ExtractPath -Force | Out-Null

try {
  Invoke-WebRequest -Uri $Asset.browser_download_url -Headers @{ "User-Agent" = $Headers["User-Agent"] } -OutFile $ZipPath
  Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractPath -Force

  $QuickDeployPath = Resolve-QuickDeployPath -RootPath $ExtractPath

  if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
  }

  New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

  Get-ChildItem -LiteralPath $ExtractPath -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Recurse -Force
  }

  $InstalledQuickDeployPath = Resolve-QuickDeployPath -RootPath $InstallRoot
  $QuickDeployArgs = @{}

  if ($InstallDependencies) {
    $QuickDeployArgs.InstallDependencies = $true
  }

  if ($SkipBridgeStart) {
    $QuickDeployArgs.SkipBridgeStart = $true
  }

  if ($SkipMcpRegistration) {
    $QuickDeployArgs.SkipMcpRegistration = $true
  }

  & $InstalledQuickDeployPath @QuickDeployArgs

  Write-Host "Installed Edge Control from GitHub release:"
  Write-Host "  Repository: $Repository"
  Write-Host "  Release: $($Release.tag_name)"
  Write-Host "  Path: $InstallRoot"
} finally {
  if (Test-Path $TempRoot) {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force
  }
}
