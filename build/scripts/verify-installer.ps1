param(
    [string]$InstallerPath = '',
    [switch]$Rebuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Invoke-ProcessAndRequireSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$ArgumentList = @(),

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$FailureMessage Exit code: $($process.ExitCode)."
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Stop-ProcessesUnderPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    Get-Process |
        Where-Object { $_.Path -and $_.Path.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force
}

if ($Rebuild -or [string]::IsNullOrWhiteSpace($InstallerPath)) {
    & (Join-Path $PSScriptRoot 'build-installer.ps1')
    $InstallerPath = Get-ChildItem -Path (Join-Path $repoRoot 'artifacts\installer') -Filter 'CPAD-Setup-*.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not (Test-Path $InstallerPath)) {
    throw "Installer not found: $InstallerPath"
}

$installDir = Join-Path $env:TEMP ('cpad-install-' + [guid]::NewGuid().ToString('N'))
$verifyAppRoot = Join-Path $env:TEMP ('cpad-installed-app-' + [guid]::NewGuid().ToString('N'))
$verifyHostRoot = Join-Path $env:TEMP ('cpad-installed-host-' + [guid]::NewGuid().ToString('N'))
$verifyOnboardingCodex = Join-Path $env:TEMP ('cpad-installed-codex-' + [guid]::NewGuid().ToString('N'))
$verifyHostCodex = Join-Path $env:TEMP ('cpad-installed-host-codex-' + [guid]::NewGuid().ToString('N'))

Write-Host "Installing $InstallerPath to $installDir ..."
Invoke-ProcessAndRequireSuccess `
    -FilePath $InstallerPath `
    -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installDir") `
    -FailureMessage 'Installer execution failed.'

$installedExe = Join-Path $installDir 'DesktopHost.exe'
if (-not (Test-Path $installedExe)) {
    throw "Installed DesktopHost.exe not found: $installedExe"
}

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CPAD.lnk'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CPAD\CPAD.lnk'
if (-not (Test-Path $desktopShortcut)) {
    throw "Desktop shortcut not found: $desktopShortcut"
}
if (-not (Test-Path $startMenuShortcut)) {
    throw "Start menu shortcut not found: $startMenuShortcut"
}

Write-Host 'Running installed onboarding verification...'
$env:CPAD_APP_ROOT = $verifyAppRoot
$env:CODEX_HOME = $verifyOnboardingCodex
Invoke-ProcessAndRequireSuccess `
    -FilePath $installedExe `
    -ArgumentList @('--verify-onboarding') `
    -FailureMessage 'Installed onboarding verification failed.'

Write-Host 'Running installed hosting verification...'
$verifyPort = Get-FreeTcpPort
$configDir = Join-Path $verifyHostRoot 'config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
@'
{
  "onboardingCompleted": true,
  "backendPort": __BACKEND_PORT__,
  "managementKey": "",
  "preferredCodexSource": "Official",
  "startWithWindows": false,
  "enableDebugTools": false,
  "lastRepositoryPath": "C:\\Users\\Reol\\workspace\\Cli-Proxy-API-Desktop"
}
'@.Replace('__BACKEND_PORT__', $verifyPort.ToString()) | Set-Content -Path (Join-Path $configDir 'desktop.json') -Encoding utf8
$env:CPAD_APP_ROOT = $verifyHostRoot
$env:CODEX_HOME = $verifyHostCodex
Invoke-ProcessAndRequireSuccess `
    -FilePath $installedExe `
    -ArgumentList @('--verify-hosting') `
    -FailureMessage 'Installed hosting verification failed.'

$uninstaller = Join-Path $installDir 'unins000.exe'
if (-not (Test-Path $uninstaller)) {
    throw "Uninstaller not found: $uninstaller"
}

Write-Host 'Running silent uninstall...'
Stop-ProcessesUnderPath -RootPath $installDir
Invoke-ProcessAndRequireSuccess `
    -FilePath $uninstaller `
    -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLEANAPPDATA') `
    -FailureMessage 'Silent uninstall failed.'

Start-Sleep -Seconds 2

if (Test-Path $installDir) {
    throw "Install directory still exists after uninstall: $installDir"
}

Write-Host 'Installer verification passed.'
