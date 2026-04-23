Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$backendResources = Join-Path $repoRoot 'resources\backend\windows-x64'
$webViewResources = Join-Path $repoRoot 'resources\webview2'

$backendArchiveUrl = 'https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.34/CLIProxyAPI_6.9.34_windows_amd64.zip'
$backendArchiveSha256 = '34ca9b7bf53a6dd89b874ed3e204371673b7eb1abf34792498af4e65bf204815'
$managementHtmlUrl = 'https://github.com/router-for-me/Cli-Proxy-API-Management-Center/releases/download/v1.7.41/management.html'
$managementHtmlSha256 = '5df3cf888afab7678ee94f6041fb6584796c203092f3e270c00db6a43dfcaa99'
$webViewBootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'

function Ensure-Directory([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Download-File([string]$Url, [string]$DestinationPath) {
    Invoke-WebRequest -Uri $Url -OutFile $DestinationPath
}

Ensure-Directory $backendResources
Ensure-Directory $webViewResources

$backendExecutablePath = Join-Path $backendResources 'cli-proxy-api.exe'
$backendArchivePath = Join-Path $env:TEMP 'cpad-cli-proxy-api.zip'
$needsBackendRefresh = -not (Test-Path $backendExecutablePath)

if ($needsBackendRefresh) {
    Write-Host "Downloading CLIProxyAPI Windows x64 archive..."
    Download-File -Url $backendArchiveUrl -DestinationPath $backendArchivePath
    $actualHash = Get-Sha256 $backendArchivePath
    if ($actualHash -ne $backendArchiveSha256) {
        throw "CLIProxyAPI archive SHA256 mismatch. Expected $backendArchiveSha256 but got $actualHash."
    }

    Expand-Archive -LiteralPath $backendArchivePath -DestinationPath $backendResources -Force
}
else {
    Write-Host "CLIProxyAPI Windows x64 asset already exists."
}

$managementHtmlPath = Join-Path $webViewResources 'management.html'
$needsManagementRefresh = -not (Test-Path $managementHtmlPath)
if (-not $needsManagementRefresh) {
    $needsManagementRefresh = (Get-Sha256 $managementHtmlPath) -ne $managementHtmlSha256
}

if ($needsManagementRefresh) {
    Write-Host "Downloading management.html..."
    Download-File -Url $managementHtmlUrl -DestinationPath $managementHtmlPath
    $actualHash = Get-Sha256 $managementHtmlPath
    if ($actualHash -ne $managementHtmlSha256) {
        throw "management.html SHA256 mismatch. Expected $managementHtmlSha256 but got $actualHash."
    }
}
else {
    Write-Host "management.html asset already exists and matches expected SHA256."
}

$webViewBootstrapperPath = Join-Path $webViewResources 'MicrosoftEdgeWebView2Setup.exe'
if (-not (Test-Path $webViewBootstrapperPath)) {
    Write-Host "Downloading WebView2 Evergreen bootstrapper..."
    Download-File -Url $webViewBootstrapperUrl -DestinationPath $webViewBootstrapperPath
}
else {
    Write-Host "WebView2 Evergreen bootstrapper already exists."
}

Write-Host "Assets ready:"
Write-Host "  Backend: $backendExecutablePath"
Write-Host "  Management: $managementHtmlPath"
Write-Host "  WebView2 bootstrapper: $webViewBootstrapperPath"
