#Requires -Version 5.1
[CmdletBinding()]
param(
    [int]$Count = 50,
    [int]$BasePort = 9222,
    [int]$StartupTimeoutSec = 20,
    [switch]$SkipSingleInstanceCheck,
    [string]$ExePath,
    [string]$TempRoot,
    [string]$OutputJsonPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Wait-DebugEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        try {
            return Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 "http://127.0.0.1:$Port/json/version"
        } catch {
            Start-Sleep -Milliseconds 400
        }
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for debug endpoint on port $Port."
}

function Get-DebugTargets {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 "http://127.0.0.1:$Port/json/list"
    return $response.Content | ConvertFrom-Json
}

function Assert-RendererReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WebSocketDebuggerUrl,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSec
    )

    $scriptPath = Join-Path $RepoRoot "scripts\\assert-renderer-ready.mjs"
    $output = & node $scriptPath $WebSocketDebuggerUrl ($TimeoutSec * 1000)
    if ($LASTEXITCODE -ne 0) {
        throw "Renderer readiness check failed."
    }

    return $output | ConvertFrom-Json
}

function Get-AppProcessesByUserDataDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UserDataDir
    )

    $escapedDir = [Regex]::Escape($UserDataDir)
    Get-CimInstance Win32_Process -Filter "Name = 'Cli Proxy API Desktop.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $escapedDir }
}

function Stop-AppProcessesByUserDataDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UserDataDir
    )

    $processes = @(Get-AppProcessesByUserDataDir -UserDataDir $UserDataDir)
    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 500
}

function Start-DebugApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$UserDataDir,
        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    $arguments = @(
        "--remote-debugging-port=$Port",
        "--remote-allow-origins=*",
        "--enable-logging=file",
        "--log-file=$LogPath",
        "--user-data-dir=$UserDataDir"
    )

    return Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru
}

function Get-LogIssues {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return @()
    }

    $patterns = @(
        "ERROR:",
        "Unhandled",
        "Exception",
        "Unable to create cache",
        "Failed to initialize the DIPS SQLite database"
    )

    $lines = Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue
    return @($lines | Where-Object {
        $line = $_
        $patterns | Where-Object { $line -like "*$_*" }
    })
}

function Test-SingleInstanceLock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,
        [Parameter(Mandatory = $true)]
        [string]$TempRoot,
        [Parameter(Mandatory = $true)]
        [int]$BasePort,
        [Parameter(Mandatory = $true)]
        [int]$StartupTimeoutSec
    )

    Write-Step "Single-instance lock regression"

    $sharedUserDataDir = Join-Path $TempRoot "single-instance"
    $primaryLog = Join-Path $TempRoot "single-instance-primary.log"
    $secondaryLog = Join-Path $TempRoot "single-instance-secondary.log"

    if (Test-Path $sharedUserDataDir) {
        Remove-Item -LiteralPath $sharedUserDataDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $sharedUserDataDir -Force | Out-Null

    try {
        $primary = Start-DebugApp -ExePath $ExePath -Port $BasePort -UserDataDir $sharedUserDataDir -LogPath $primaryLog
        [void](Wait-DebugEndpoint -Port $BasePort -TimeoutSec $StartupTimeoutSec)

        $secondary = Start-DebugApp -ExePath $ExePath -Port ($BasePort + 1) -UserDataDir $sharedUserDataDir -LogPath $secondaryLog
        Start-Sleep -Seconds 3

        $browserProcesses = @(
            Get-AppProcessesByUserDataDir -UserDataDir $sharedUserDataDir |
                Where-Object { $_.CommandLine -notmatch "\s--type=" }
        )

        if ($browserProcesses.Count -ne 1) {
            throw "Expected exactly one browser process for shared userDataDir, got $($browserProcesses.Count)."
        }

        if (-not (Get-Process -Id $browserProcesses[0].ProcessId -ErrorAction SilentlyContinue)) {
            throw "Primary browser process is not alive after second launch."
        }

        Write-Host "    single-instance lock is active" -ForegroundColor DarkGreen
    } finally {
        Stop-AppProcessesByUserDataDir -UserDataDir $sharedUserDataDir
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot "release\\win-unpacked\\Cli Proxy API Desktop.exe"
} else {
    $ExePath = (Resolve-Path -LiteralPath $ExePath).Path
}
if ([string]::IsNullOrWhiteSpace($TempRoot)) {
    $TempRoot = Join-Path $env:TEMP "cpad-debug-loops"
}

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Packaged app not found: $ExePath. Run npm run dist:win first."
}

New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]
$singleInstanceStatus = [pscustomobject]@{
    skipped = [bool]$SkipSingleInstanceCheck
    status = if ($SkipSingleInstanceCheck) { "skipped" } else { "pending" }
}
$failureMessage = $null

try {
    if (-not $SkipSingleInstanceCheck) {
        Test-SingleInstanceLock -ExePath $ExePath -TempRoot $TempRoot -BasePort $BasePort -StartupTimeoutSec $StartupTimeoutSec
        $singleInstanceStatus = [pscustomobject]@{
            skipped = $false
            status = "passed"
        }
    }

    for ($index = 1; $index -le $Count; $index++) {
        $cycleName = "{0:D3}" -f $index
        $port = $BasePort + 10 + $index
        $userDataDir = Join-Path $TempRoot "cycle-$cycleName"
        $logPath = Join-Path $TempRoot "cycle-$cycleName.log"

        Write-Step "Debug cycle $cycleName / $Count (port $port)"

        if (Test-Path $userDataDir) {
            Remove-Item -LiteralPath $userDataDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null
        if (Test-Path $logPath) {
            Remove-Item -LiteralPath $logPath -Force
        }

        try {
            $process = Start-DebugApp -ExePath $ExePath -Port $port -UserDataDir $userDataDir -LogPath $logPath
            $versionResponse = Wait-DebugEndpoint -Port $port -TimeoutSec $StartupTimeoutSec
            $version = $versionResponse.Content | ConvertFrom-Json
            $targets = @(Get-DebugTargets -Port $port)

            if ($targets.Count -lt 1) {
                throw "No page target returned from debug endpoint."
            }

            $pageTarget = $targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1
            if ($null -eq $pageTarget) {
                throw "No page-type target returned from debug endpoint."
            }

            if ($pageTarget.title -notlike "*Cli Proxy API Desktop*") {
                throw "Unexpected page title: $($pageTarget.title)"
            }

            $rendererState = Assert-RendererReady -WebSocketDebuggerUrl $pageTarget.webSocketDebuggerUrl -RepoRoot $repoRoot -TimeoutSec $StartupTimeoutSec

            $issues = @(Get-LogIssues -LogPath $logPath)
            if ($issues.Count -gt 0) {
                throw ("Log issues detected:`n" + ($issues -join "`n"))
            }

            $results.Add([pscustomobject]@{
                Cycle = $index
                Port = $port
                Browser = $version.Browser
                Title = $rendererState.title
                Status = "pass"
            }) | Out-Null

            Write-Host ("    pass: {0} / {1}" -f $version.Browser, $rendererState.title) -ForegroundColor DarkGreen
        } catch {
            $results.Add([pscustomobject]@{
                Cycle = $index
                Port = $port
                Browser = ""
                Title = ""
                Status = "fail"
                Error = $_.Exception.Message
            }) | Out-Null
            throw
        } finally {
            Stop-AppProcessesByUserDataDir -UserDataDir $userDataDir
        }
    }
} catch {
    $failureMessage = $_.Exception.Message
    if ($singleInstanceStatus.status -eq "pending") {
        $singleInstanceStatus = [pscustomobject]@{
            skipped = $false
            status = "failed"
            error = $failureMessage
        }
    }
    throw
} finally {
    if ($OutputJsonPath) {
        $outputDirectory = Split-Path -Parent $OutputJsonPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        $summary = [pscustomobject]@{
            status = if ($failureMessage) { "failed" } else { "passed" }
            exePath = $ExePath
            tempRoot = $TempRoot
            count = $Count
            startupTimeoutSec = $StartupTimeoutSec
            singleInstanceCheck = $singleInstanceStatus
            cycles = $results
            completedAt = (Get-Date).ToUniversalTime().ToString("o")
        }
        $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
    }
}

Write-Host ""
Write-Host ("Completed {0} debug cycles successfully." -f $Count) -ForegroundColor Green
$results | Format-Table -AutoSize
