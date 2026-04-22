[CmdletBinding()]
param(
  [switch]$InstallDependencies,
  [switch]$SkipMcpRegistration,
  [switch]$SkipBridgeStart
)

$ErrorActionPreference = "Stop"

$InstallScriptPath = Join-Path $PSScriptRoot "install.ps1"
$InstallGlobalSkillScriptPath = Join-Path $PSScriptRoot "install-global-skill.ps1"
$RegisterScriptPath = Join-Path $PSScriptRoot "register-codex-mcp.ps1"
$PluginRoot = Split-Path -Parent $PSScriptRoot
$InstallArgs = @{}

if ($InstallDependencies) {
  $InstallArgs.InstallDependencies = $true
}

if (-not $SkipBridgeStart) {
  $InstallArgs.StartBridge = $true
}

& $InstallScriptPath @InstallArgs
& $InstallGlobalSkillScriptPath

if (-not $SkipMcpRegistration) {
  if (Get-Command codex -ErrorAction SilentlyContinue) {
    & $RegisterScriptPath
  } else {
    Write-Warning "codex command not found; skipped MCP registration."
  }
}

Write-Host "Quick deploy completed."
Write-Host "If the extension has not been loaded before, open edge://extensions and load:"
Write-Host "  $PluginRoot\\extension"
