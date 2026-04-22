[CmdletBinding()]
param(
  [switch]$InstallDependencies,
  [switch]$SkipDependencies,
  [switch]$StartBridge
)

$ErrorActionPreference = "Stop"

$PluginRoot = Split-Path -Parent $PSScriptRoot
$StateDir = Join-Path $env:APPDATA "CodexEdgeControl"
$ConfigPath = Join-Path $StateDir "config.json"
$ExtensionConfigPath = Join-Path $PluginRoot "extension\\config.local.js"
$NodeModulesPath = Join-Path $PSScriptRoot "node_modules"
$Port = 47173
$HostName = "127.0.0.1"

New-Item -ItemType Directory -Force $StateDir | Out-Null

if (Test-Path $ConfigPath) {
  $Config = Get-Content -Raw $ConfigPath | ConvertFrom-Json
} else {
  $Token = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
  $Config = [pscustomobject]@{
    host = $HostName
    port = $Port
    authToken = $Token
    bridgePath = "/bridge"
  }
}

$Config | ConvertTo-Json -Depth 4 | Set-Content -Path $ConfigPath -Encoding utf8

$ExtensionConfig = @"
globalThis.EDGE_CONTROL_CONFIG = {
  bridgeUrl: "ws://$($Config.host):$($Config.port)$($Config.bridgePath)",
  authToken: "$($Config.authToken)"
};
"@
$ExtensionConfig | Set-Content -Path $ExtensionConfigPath -Encoding utf8

if ($SkipDependencies) {
  $ShouldInstallDependencies = $false
} elseif ($InstallDependencies) {
  $ShouldInstallDependencies = $true
} else {
  $ShouldInstallDependencies = -not (Test-Path $NodeModulesPath)
}

if ($ShouldInstallDependencies) {
  Push-Location $PSScriptRoot
  try {
    npm ci
  } finally {
    Pop-Location
  }
}

Write-Host "Edge Control config written to $ConfigPath"
Write-Host "Edge extension config written to $ExtensionConfigPath"
Write-Host "Load unpacked extension from $PluginRoot\\extension"

if ($StartBridge) {
  & (Join-Path $PSScriptRoot "start-host.ps1") -Background
}
