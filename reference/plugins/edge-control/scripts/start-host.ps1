[CmdletBinding()]
param(
  [switch]$Background,
  [switch]$Foreground,
  [switch]$Quiet,
  [switch]$InstallDependencies
)

$ErrorActionPreference = "Stop"
$NodePath = (Get-Command node).Source
$StateDir = Join-Path $env:APPDATA "CodexEdgeControl"
$LogDir = Join-Path $StateDir "logs"
$StdOutLogPath = Join-Path $LogDir "bridge.stdout.log"
$StdErrLogPath = Join-Path $LogDir "bridge.stderr.log"
$PidPath = Join-Path $StateDir "bridge.pid"

if ($InstallDependencies -or -not (Test-Path (Join-Path $PSScriptRoot "node_modules"))) {
  Push-Location $PSScriptRoot
  try {
    npm install
  } finally {
    Pop-Location
  }
}

New-Item -ItemType Directory -Force $StateDir | Out-Null

if (-not $Foreground) {
  New-Item -ItemType Directory -Force $LogDir | Out-Null
  $Process = Start-Process `
    -FilePath $NodePath `
    -ArgumentList "bridge-server.mjs" `
    -WorkingDirectory $PSScriptRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $StdOutLogPath `
    -RedirectStandardError $StdErrLogPath `
    -PassThru

  Set-Content -LiteralPath $PidPath -Value $Process.Id -Encoding ascii

  if (-not $Quiet) {
    Write-Host "Started Edge Control bridge silently in background. PID=$($Process.Id)"
    Write-Host "stdout: $StdOutLogPath"
    Write-Host "stderr: $StdErrLogPath"
  }
  exit 0
}

Push-Location $PSScriptRoot
try {
  & $NodePath "bridge-server.mjs"
} finally {
  Pop-Location
}
