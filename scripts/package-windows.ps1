#Requires -Version 5.1
<#
.SYNOPSIS
Builds the Windows packaging inputs for Cli Proxy API Desktop.

.DESCRIPTION
Builds the Go service binary and Codex shim first, then builds the Electron app,
then packages Windows distributables with Electron Builder. Each step stops on
failure and verifies the expected output files.

.PARAMETER DryRun
Prints the commands and output checks without running the builds.

.PARAMETER PrepareOnly
Builds the packaging inputs but skips the Electron Builder packaging step.

.PARAMETER DirOnly
Packages only the unpacked Windows directory target.

.PARAMETER PortableOnly
Packages only the portable EXE target.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1

.EXAMPLE
pwsh -File .\scripts\package-windows.ps1 -DryRun
#>
[CmdletBinding(DefaultParameterSetName = "Default")]
param(
    [switch]$DryRun,
    [switch]$PrepareOnly,
    [Parameter(ParameterSetName = "DirOnly")]
    [switch]$DirOnly,
    [Parameter(ParameterSetName = "PortableOnly")]
    [switch]$PortableOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-CommandPath {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $command) {
            if ($command.Path) {
                return $command.Path
            }

            if ($command.Source) {
                return $command.Source
            }

            return $command.Definition
        }
    }

    throw ("Required command was not found. Tried: {0}" -f ($Candidates -join ", "))
}

function Format-CommandLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $parts = @($Command) + $Arguments
    $escaped = foreach ($part in $parts) {
        if ($part -match "\s") {
            '"{0}"' -f $part
        } else {
            $part
        }
    }

    return ($escaped -join " ")
}

function Invoke-StepCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Step -Message $Name
    Write-Host ("    {0}" -f (Format-CommandLine -Command $Command -Arguments $Arguments))

    if ($DryRun) {
        return
    }

    & $Command @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ("Step failed with exit code {0}: {1}" -f $exitCode, $Name)
    }
}

function Assert-Artifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ($DryRun) {
        Write-Host ("    would verify {0}: {1}" -f $Description, $Path)
        return
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ("Expected {0} at '{1}', but it was not found." -f $Description, $Path)
    }

    Write-Host ("    verified {0}: {1}" -f $Description, $Path) -ForegroundColor DarkGreen
}

function Assert-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ($DryRun) {
        Write-Host ("    would verify {0}: {1}" -f $Description, $Path)
        return
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw ("Expected {0} directory at '{1}', but it was not found." -f $Description, $Path)
    }

    Write-Host ("    verified {0}: {1}" -f $Description, $Path) -ForegroundColor DarkGreen
}

if ($env:OS -ne "Windows_NT") {
    throw "This packaging helper is Windows-only."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

$packageJsonPath = Join-Path $repoRoot "package.json"
$serviceGoModPath = Join-Path (Join-Path $repoRoot "service") "go.mod"
$builderConfigPath = Join-Path $repoRoot "electron-builder.yml"

foreach ($requiredPath in @($packageJsonPath, $serviceGoModPath, $builderConfigPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw ("Required repo file was not found: {0}" -f $requiredPath)
    }
}

$npmCommand = Resolve-CommandPath -Candidates @("npm.cmd", "npm")
[void](Resolve-CommandPath -Candidates @("go.exe", "go"))

$serviceBinDir = Join-Path (Join-Path $repoRoot "service") "bin"
$serviceBinaryPath = Join-Path $serviceBinDir "cpad-service.exe"
$shimBinaryPath = Join-Path $serviceBinDir "codex.exe"
$electronOutDir = Join-Path $repoRoot "out"
$electronMainPath = Join-Path (Join-Path $electronOutDir "main") "index.js"
$electronPreloadPath = Join-Path (Join-Path $electronOutDir "preload") "index.js"
$electronRendererPath = Join-Path (Join-Path $electronOutDir "renderer") "index.html"
$releaseDir = Join-Path $repoRoot "release"

$packageMetadata = Get-Content -Raw -LiteralPath $packageJsonPath | ConvertFrom-Json
$version = [string]$packageMetadata.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Unable to resolve package version from package.json."
}

$expectedPortablePath = Join-Path $releaseDir ("Cli Proxy API Desktop-Portable-{0}-x64.exe" -f $version)
$expectedSetupPath = Join-Path $releaseDir ("Cli Proxy API Desktop-Setup-{0}-x64.exe" -f $version)
$expectedUnpackedDir = Join-Path $releaseDir "win-unpacked"

$builderTargets = @()
if ($DirOnly) {
    $builderTargets = @("dir")
} elseif ($PortableOnly) {
    $builderTargets = @("portable")
} else {
    $builderTargets = @("nsis", "portable", "dir")
}

Push-Location $repoRoot
try {
    Invoke-StepCommand -Name "Build Go service binary for packaging" -Command $npmCommand -Arguments @("run", "build:service")
    Assert-Artifact -Path $serviceBinaryPath -Description "Go service binary"

    Invoke-StepCommand -Name "Build Go Codex shim for packaging" -Command $npmCommand -Arguments @("run", "build:shim")
    Assert-Artifact -Path $shimBinaryPath -Description "Go Codex shim"

    Invoke-StepCommand -Name "Build Electron app bundle for packaging" -Command $npmCommand -Arguments @("run", "build")
    Assert-Artifact -Path $electronMainPath -Description "Electron main bundle"
    Assert-Artifact -Path $electronPreloadPath -Description "Electron preload bundle"
    Assert-Artifact -Path $electronRendererPath -Description "Electron renderer entrypoint"

    if (-not $PrepareOnly) {
        $previousAutoDiscovery = $env:CSC_IDENTITY_AUTO_DISCOVERY
        $env:CSC_IDENTITY_AUTO_DISCOVERY = "false"

        $builderArgs = @(
            "exec",
            "--",
            "electron-builder",
            "--config",
            $builderConfigPath,
            "--win"
        ) + $builderTargets + @("--x64")

        try {
            Invoke-StepCommand -Name "Package Windows distributables" -Command $npmCommand -Arguments $builderArgs
        }
        finally {
            if ($null -eq $previousAutoDiscovery) {
                Remove-Item Env:CSC_IDENTITY_AUTO_DISCOVERY -ErrorAction SilentlyContinue
            } else {
                $env:CSC_IDENTITY_AUTO_DISCOVERY = $previousAutoDiscovery
            }
        }

        Assert-Directory -Path $expectedUnpackedDir -Description "unpacked Windows app directory"

        if (-not $DirOnly) {
            if ($PortableOnly) {
                Assert-Artifact -Path $expectedPortablePath -Description "portable Windows executable"
            } else {
                Assert-Artifact -Path $expectedSetupPath -Description "Windows setup executable"
                Assert-Artifact -Path $expectedPortablePath -Description "portable Windows executable"
            }
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""
if ($PrepareOnly) {
    Write-Host "Windows packaging inputs are ready:" -ForegroundColor Green
    Write-Host ("  Service:  {0}" -f $serviceBinaryPath)
    Write-Host ("  Shim:     {0}" -f $shimBinaryPath)
    Write-Host ("  Electron: {0}" -f $electronOutDir)
} else {
    Write-Host "Windows distributables are ready:" -ForegroundColor Green
    Write-Host ("  Release:  {0}" -f $releaseDir)
    Write-Host ("  Unpacked: {0}" -f $expectedUnpackedDir)
    if ($PortableOnly) {
        Write-Host ("  Portable: {0}" -f $expectedPortablePath)
    } elseif (-not $DirOnly) {
        Write-Host ("  Setup:    {0}" -f $expectedSetupPath)
        Write-Host ("  Portable: {0}" -f $expectedPortablePath)
    }
}
