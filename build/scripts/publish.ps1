param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repoRoot "artifacts\publish\$Runtime"
}
else {
    $OutputDir
}

& (Join-Path $PSScriptRoot 'fetch-assets.ps1')

if (Test-Path $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

Write-Host "Publishing DesktopHost for $Runtime ($Configuration)..."
dotnet publish `
    (Join-Path $repoRoot 'src\DesktopHost\DesktopHost.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outputDirectory

$assetsRoot = Join-Path $outputDirectory 'assets'
$publishBackendDir = Join-Path $assetsRoot 'backend\windows-x64'
$publishWebViewDir = Join-Path $assetsRoot 'webview2'
New-Item -ItemType Directory -Force -Path $publishBackendDir | Out-Null
New-Item -ItemType Directory -Force -Path $publishWebViewDir | Out-Null

$backendSourceDir = Join-Path $repoRoot 'resources\backend\windows-x64'
$webViewSourceDir = Join-Path $repoRoot 'resources\webview2'

Copy-Item -LiteralPath (Join-Path $backendSourceDir 'cli-proxy-api.exe') -Destination $publishBackendDir -Force
foreach ($name in 'LICENSE', 'README.md', 'README_CN.md') {
    $candidate = Join-Path $backendSourceDir $name
    if (Test-Path $candidate) {
        Copy-Item -LiteralPath $candidate -Destination $publishBackendDir -Force
    }
}

Copy-Item -LiteralPath (Join-Path $webViewSourceDir 'management.html') -Destination $publishWebViewDir -Force
Copy-Item -LiteralPath (Join-Path $webViewSourceDir 'MicrosoftEdgeWebView2Setup.exe') -Destination $publishWebViewDir -Force

$desktopHostExe = Join-Path $outputDirectory 'DesktopHost.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopHostExe).ProductVersion
$commit = (git -C $repoRoot rev-parse --short HEAD).Trim()

@"
version=$version
commit=$commit
runtime=$Runtime
configuration=$Configuration
publishedAt=$(Get-Date -Format o)
"@ | Set-Content -Path (Join-Path $outputDirectory 'publish-info.txt') -Encoding utf8

Write-Host "Publish output: $outputDirectory"
Write-Host "Version: $version"
