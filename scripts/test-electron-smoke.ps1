#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$OutputJsonPath,
    [string]$ExePath,
    [int]$BasePort = 9332,
    [int]$StartupTimeoutSec = 30,
    [switch]$SkipSingleInstanceCheck,
    [switch]$ReuseExistingPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-CommandPath {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) {
            continue
        }

        if ($command.Path) {
            return $command.Path
        }
        if ($command.Source) {
            return $command.Source
        }

        return $command.Definition
    }

    throw ("Required command was not found. Tried: {0}" -f ($Candidates -join ", "))
}

function Format-CommandLine {
    param(
        [string]$Command,
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

function Resolve-OutputPath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $BasePath $Path
}

function Resolve-PackagedExePath {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $Candidates[0]
}

function Invoke-StepCommand {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments,
        [string]$LogPath,
        [string]$WorkingDirectory
    )

    Write-Step $Name
    Write-Host ("    {0}" -f (Format-CommandLine -Command $Command -Arguments $Arguments))

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
    }

    Push-Location $WorkingDirectory
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $text = (& $Command @Arguments 2>&1 | Out-String).TrimEnd()
            $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    } finally {
        Pop-Location
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Set-Content -LiteralPath $LogPath -Value $text -Encoding UTF8
    }

    if ($exitCode -ne 0) {
        throw ("Step failed with exit code {0}: {1}" -f $exitCode, $Name)
    }

    return $text
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot ("out\\validation\\electron-smoke-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
} else {
    $OutputDir = Resolve-OutputPath -BasePath $repoRoot -Path $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$logsDir = Join-Path $OutputDir "logs"
New-Item -ItemType Directory -Path $logsDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $OutputDir "electron-smoke.json"
} else {
    $OutputJsonPath = Resolve-OutputPath -BasePath $repoRoot -Path $OutputJsonPath
}

$defaultExeCandidates = @(
    (Join-Path $repoRoot "release\\layout-preview\\Cli Proxy API Desktop.exe"),
    (Join-Path $repoRoot "release\\win-unpacked\\Cli Proxy API Desktop.exe")
)
$explicitExePath = -not [string]::IsNullOrWhiteSpace($ExePath)

if (-not $explicitExePath) {
    $ExePath = Resolve-PackagedExePath -Candidates $defaultExeCandidates
} elseif (-not [System.IO.Path]::IsPathRooted($ExePath)) {
    $ExePath = Join-Path $repoRoot $ExePath
}

$powershellCommand = Resolve-CommandPath -Candidates @("powershell.exe", "powershell")
$packageScript = Join-Path $scriptRoot "package-windows.ps1"
$debugLoopScript = Join-Path $scriptRoot "debug-loop.ps1"
$debugLoopSummaryPath = Join-Path $OutputDir "debug-loop-summary.json"
$debugLoopTempRoot = Join-Path $OutputDir "debug-loop"
$packagePrepared = $false
$failureMessage = $null

try {
    $needsPackage = (-not $ReuseExistingPackage) -or (-not (Test-Path -LiteralPath $ExePath -PathType Leaf))
    if ($needsPackage) {
        Invoke-StepCommand `
            -Name "Prepare packaged Electron app" `
            -Command $powershellCommand `
            -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $packageScript, "-DirOnly") `
            -LogPath (Join-Path $logsDir "package-dir.log") `
            -WorkingDirectory $repoRoot | Out-Null
        $packagePrepared = $true
    }

    if (-not $explicitExePath) {
        $ExePath = Resolve-PackagedExePath -Candidates $defaultExeCandidates
    }

    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        throw "Packaged Electron app was not found after preparation."
    }

    $debugArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $debugLoopScript,
        "-Count",
        "1",
        "-BasePort",
        "$BasePort",
        "-StartupTimeoutSec",
        "$StartupTimeoutSec",
        "-ExePath",
        $ExePath,
        "-TempRoot",
        $debugLoopTempRoot,
        "-OutputJsonPath",
        $debugLoopSummaryPath
    )
    if ($SkipSingleInstanceCheck) {
        $debugArguments += "-SkipSingleInstanceCheck"
    }

    Invoke-StepCommand `
        -Name "Run packaged Electron smoke" `
        -Command $powershellCommand `
        -Arguments $debugArguments `
        -LogPath (Join-Path $logsDir "debug-loop.log") `
        -WorkingDirectory $repoRoot | Out-Null

    $debugSummary = Get-Content -Raw -LiteralPath $debugLoopSummaryPath | ConvertFrom-Json
    $summary = [pscustomobject]@{
        status = "passed"
        repoRoot = $repoRoot
        outputDir = $OutputDir
        exePath = $ExePath
        packagePrepared = $packagePrepared
        debugLoopSummaryPath = $debugLoopSummaryPath
        debugLoop = $debugSummary
        completedAt = (Get-Date).ToUniversalTime().ToString("o")
    }
    $summary | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
} catch {
    $failureMessage = $_.Exception.Message
    $summary = [pscustomobject]@{
        status = "failed"
        repoRoot = $repoRoot
        outputDir = $OutputDir
        exePath = $ExePath
        packagePrepared = $packagePrepared
        debugLoopSummaryPath = $debugLoopSummaryPath
        error = $failureMessage
        completedAt = (Get-Date).ToUniversalTime().ToString("o")
    }
    $summary | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
    throw
}

Write-Host ""
Write-Host "Electron smoke completed." -ForegroundColor Green
Write-Host ("  OutputDir: {0}" -f $OutputDir)
Write-Host ("  Summary:   {0}" -f $OutputJsonPath)
