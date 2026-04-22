[CmdletBinding()]
param(
  [string]$SkillName = "edge-browser-ops"
)

$ErrorActionPreference = "Stop"

function Get-CodexHome() {
  if ($env:CODEX_HOME) {
    return $env:CODEX_HOME
  }

  if ($HOME) {
    return (Join-Path $HOME ".codex")
  }

  return (Join-Path ([Environment]::GetFolderPath("UserProfile")) ".codex")
}

function Assert-PathWithin([string]$RootPath, [string]$TargetPath) {
  $RootFullPath = [System.IO.Path]::GetFullPath($RootPath)
  $TargetFullPath = [System.IO.Path]::GetFullPath($TargetPath)

  if (-not $TargetFullPath.StartsWith($RootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to operate outside the Codex skills root: $TargetFullPath"
  }
}

$PluginRoot = Split-Path -Parent $PSScriptRoot
$SourceSkillPath = Join-Path $PluginRoot ("skills\" + $SkillName)

if (-not (Test-Path $SourceSkillPath)) {
  throw "Source skill path not found: $SourceSkillPath"
}

$CodexHome = Get-CodexHome
$GlobalSkillsRoot = Join-Path $CodexHome "skills"
$TargetSkillPath = Join-Path $GlobalSkillsRoot $SkillName

New-Item -ItemType Directory -Path $GlobalSkillsRoot -Force | Out-Null
Assert-PathWithin -RootPath $GlobalSkillsRoot -TargetPath $TargetSkillPath

if (Test-Path $TargetSkillPath) {
  Remove-Item -LiteralPath $TargetSkillPath -Recurse -Force
}

Copy-Item -LiteralPath $SourceSkillPath -Destination $TargetSkillPath -Recurse -Force

Write-Host "Installed global Codex skill:"
Write-Host "  $TargetSkillPath"
