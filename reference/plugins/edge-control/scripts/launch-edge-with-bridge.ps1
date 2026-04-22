[CmdletBinding()]
param(
  [int]$RemoteDebuggingPort = 0,
  [string[]]$AdditionalArgs = @()
)

$ErrorActionPreference = "Stop"

$EdgePath = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"
$PluginRoot = Split-Path -Parent $PSScriptRoot
$ExtensionPath = Join-Path $PluginRoot "extension"
$Running = Get-Process msedge -ErrorAction SilentlyContinue

if (-not (Test-Path $EdgePath)) {
  throw "Edge executable not found at $EdgePath"
}

if ($Running) {
  throw "Edge is already running. Chromium ignores --load-extension for an existing browser session. Close Edge first or use edge://extensions -> Load unpacked."
}

$Args = @(
  "--load-extension=$ExtensionPath",
  "--disable-extensions-except=$ExtensionPath"
)

if ($RemoteDebuggingPort -gt 0) {
  $Args += "--remote-debugging-port=$RemoteDebuggingPort"
}

$Args += $AdditionalArgs

Start-Process -FilePath $EdgePath -ArgumentList $Args | Out-Null
Write-Host "Launched Edge with the Edge Control extension path."
