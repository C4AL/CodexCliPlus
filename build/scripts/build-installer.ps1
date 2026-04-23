param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installerOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repoRoot 'artifacts\installer'
}
else {
    $OutputDir
}

function Resolve-IsccPath {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Ensure-InnoSetup {
    $isccPath = Resolve-IsccPath
    if ($isccPath) {
        return $isccPath
    }

    Write-Host 'Installing Inno Setup via winget...'
    winget install --id JRSoftware.InnoSetup --silent --accept-package-agreements --accept-source-agreements --disable-interactivity

    $isccPath = Resolve-IsccPath
    if (-not $isccPath) {
        throw 'ISCC.exe not found after installing Inno Setup.'
    }

    return $isccPath
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Configuration $Configuration -Runtime $Runtime

$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$desktopHostExe = Join-Path $publishDir 'DesktopHost.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopHostExe).ProductVersion
$commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
$iscc = Ensure-InnoSetup

New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null

$issPath = Join-Path $repoRoot 'build\installer\CPAD.iss'
Write-Host "Building installer with ISCC: $iscc"
& $iscc "/DPublishDir=$publishDir" "/DOutputDir=$installerOutputDir" "/DAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$installerPath = Get-ChildItem -Path $installerOutputDir -Filter 'CPAD-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $installerPath) {
    throw 'Installer output not found.'
}

$hash = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
@"
version=$version
commit=$commit
installer=$(Split-Path -Leaf $installerPath)
sha256=$hash
builtAt=$(Get-Date -Format o)
"@ | Set-Content -Path (Join-Path $installerOutputDir 'release-info.txt') -Encoding utf8

Write-Host "Installer: $installerPath"
Write-Host "SHA256: $hash"
