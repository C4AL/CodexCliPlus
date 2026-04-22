#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$PluginId,
    [int]$CPAHealthTimeoutSec = 30,
    [switch]$SkipElectronSmoke
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

function Same-Path {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    $leftPath = [System.IO.Path]::GetFullPath($Left)
    $rightPath = [System.IO.Path]::GetFullPath($Right)
    return [string]::Equals($leftPath, $rightPath, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Description,
        [switch]$Directory
    )

    $pathType = if ($Directory) { "Container" } else { "Leaf" }
    if (-not (Test-Path -LiteralPath $Path -PathType $pathType)) {
        throw ("Expected {0} at '{1}'." -f $Description, $Path)
    }
}

function Write-JsonArtifact {
    param(
        [object]$Value,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $Value | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Summarize-CommandText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $lines = $Text -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if (($lines | Measure-Object).Count -eq 0) {
        return ""
    }

    return $lines[-1].Trim()
}

function Invoke-CommandCapture {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [hashtable]$Environment,
        [string]$LogPath
    )

    $previousValues = @{}
    if ($Environment) {
        foreach ($entry in $Environment.GetEnumerator()) {
            if (Test-Path ("Env:" + $entry.Key)) {
                $previousValues[$entry.Key] = (Get-Item ("Env:" + $entry.Key)).Value
            } else {
                $previousValues[$entry.Key] = $null
            }

            Set-Item -Path ("Env:" + $entry.Key) -Value $entry.Value
        }
    }

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        Push-Location $WorkingDirectory
        try {
            $process = Start-Process `
                -FilePath $Command `
                -ArgumentList $Arguments `
                -WorkingDirectory $WorkingDirectory `
                -Wait `
                -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath
            $exitCode = [int]$process.ExitCode
        } finally {
            Pop-Location
        }

        $stdoutText = if (Test-Path -LiteralPath $stdoutPath) {
            [System.IO.File]::ReadAllText($stdoutPath, [System.Text.Encoding]::UTF8)
        } else {
            ""
        }
        $stderrText = if (Test-Path -LiteralPath $stderrPath) {
            [System.IO.File]::ReadAllText($stderrPath, [System.Text.Encoding]::UTF8)
        } else {
            ""
        }
        $parts = @()
        if (-not [string]::IsNullOrWhiteSpace($stdoutText)) {
            $parts += $stdoutText.TrimEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
            $parts += $stderrText.TrimEnd()
        }
        $text = $parts -join [Environment]::NewLine
    } finally {
        if ($Environment) {
            foreach ($entry in $Environment.GetEnumerator()) {
                if ($null -eq $previousValues[$entry.Key]) {
                    Remove-Item ("Env:" + $entry.Key) -ErrorAction SilentlyContinue
                } else {
                    Set-Item -Path ("Env:" + $entry.Key) -Value $previousValues[$entry.Key]
                }
            }
        }

        Remove-Item -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
        Set-Content -LiteralPath $LogPath -Value $text -Encoding UTF8
    }

    if ($exitCode -ne 0) {
        $summary = Summarize-CommandText -Text $text
        if ($summary) {
            throw ("Command failed with exit code {0}: {1}. Last output: {2}" -f $exitCode, (Format-CommandLine -Command $Command -Arguments $Arguments), $summary)
        }

        throw ("Command failed with exit code {0}: {1}" -f $exitCode, (Format-CommandLine -Command $Command -Arguments $Arguments))
    }

    return $text
}

function Invoke-DirectCommandCapture {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [hashtable]$Environment,
        [string]$LogPath
    )

    $previousValues = @{}
    if ($Environment) {
        foreach ($entry in $Environment.GetEnumerator()) {
            if (Test-Path ("Env:" + $entry.Key)) {
                $previousValues[$entry.Key] = (Get-Item ("Env:" + $entry.Key)).Value
            } else {
                $previousValues[$entry.Key] = $null
            }

            Set-Item -Path ("Env:" + $entry.Key) -Value $entry.Value
        }
    }

    $text = ""
    $exitCode = 0
    try {
        Push-Location $WorkingDirectory
        try {
            $text = (& $Command @Arguments 2>&1 | Out-String).TrimEnd()
            if ($null -ne $LASTEXITCODE) {
                $exitCode = [int]$LASTEXITCODE
            }
        } finally {
            Pop-Location
        }
    } finally {
        if ($Environment) {
            foreach ($entry in $Environment.GetEnumerator()) {
                if ($null -eq $previousValues[$entry.Key]) {
                    Remove-Item ("Env:" + $entry.Key) -ErrorAction SilentlyContinue
                } else {
                    Set-Item -Path ("Env:" + $entry.Key) -Value $previousValues[$entry.Key]
                }
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null
        Set-Content -LiteralPath $LogPath -Value $text -Encoding UTF8
    }

    if ($exitCode -ne 0) {
        $summary = Summarize-CommandText -Text $text
        if ($summary) {
            throw ("Command failed with exit code {0}: {1}. Last output: {2}" -f $exitCode, (Format-CommandLine -Command $Command -Arguments $Arguments), $summary)
        }

        throw ("Command failed with exit code {0}: {1}" -f $exitCode, (Format-CommandLine -Command $Command -Arguments $Arguments))
    }

    return $text
}

function Add-StepRecord {
    param(
        [string]$Name,
        [string]$Status,
        [datetime]$StartedAt,
        [datetime]$FinishedAt,
        [string]$LogPath,
        [string]$JsonPath,
        [string]$Message
    )

    $script:StepResults.Add([pscustomobject]@{
        name = $Name
        status = $Status
        startedAt = $StartedAt.ToUniversalTime().ToString("o")
        finishedAt = $FinishedAt.ToUniversalTime().ToString("o")
        logPath = $LogPath
        jsonPath = $JsonPath
        message = $Message
    }) | Out-Null
}

function Invoke-ValidationStep {
    param(
        [string]$Name,
        [string]$LogPath,
        [string]$JsonPath,
        [scriptblock]$Action
    )

    $startedAt = Get-Date
    Write-Step $Name
    try {
        $result = & $Action
        Add-StepRecord -Name $Name -Status "passed" -StartedAt $startedAt -FinishedAt (Get-Date) -LogPath $LogPath -JsonPath $JsonPath -Message ""
        return $result
    } catch {
        Add-StepRecord -Name $Name -Status "failed" -StartedAt $startedAt -FinishedAt (Get-Date) -LogPath $LogPath -JsonPath $JsonPath -Message $_.Exception.Message
        throw
    }
}

function Get-PluginStatus {
    param(
        [object]$PluginMarketStatus,
        [string]$ID
    )

    return $PluginMarketStatus.plugins | Where-Object { $_.id -eq $ID } | Select-Object -First 1
}

function Select-PluginForValidation {
    param(
        [object]$PluginMarketStatus,
        [string]$RequestedPluginID
    )

    if ($RequestedPluginID) {
        $plugin = Get-PluginStatus -PluginMarketStatus $PluginMarketStatus -ID $RequestedPluginID
        Assert-Condition ($null -ne $plugin) ("Requested plugin was not found in plugin market: {0}" -f $RequestedPluginID)
        return $RequestedPluginID
    }

    $preferred = Get-PluginStatus -PluginMarketStatus $PluginMarketStatus -ID "edge-control"
    if ($null -ne $preferred) {
        return $preferred.id
    }

    $firstPlugin = $PluginMarketStatus.plugins | Select-Object -First 1
    Assert-Condition ($null -ne $firstPlugin) "Plugin market did not contain any plugins."
    return $firstPlugin.id
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }
}

function Set-CPARuntimeConfigPort {
    param(
        [string]$ConfigPath,
        [int]$Port
    )

    Assert-PathExists -Path $ConfigPath -Description "CPA runtime config"

    $content = [System.IO.File]::ReadAllText($ConfigPath, [System.Text.Encoding]::UTF8)
    $updated = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '(?m)^port:\s*\d+\s*$',
        ("port: {0}" -f $Port),
        1
    )

    if ($updated -eq $content) {
        throw ("Unable to rewrite CPA runtime port in config: {0}" -f $ConfigPath)
    }

    [System.IO.File]::WriteAllText($ConfigPath, $updated, [System.Text.Encoding]::UTF8)
}

function Wait-ForCPAHealthy {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [hashtable]$Environment,
        [int]$TimeoutSec,
        [string]$LogPath,
        [string]$JsonPath
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $attempt = 0
    $lastStatus = $null
    $segments = New-Object System.Collections.Generic.List[string]

    while ((Get-Date) -lt $deadline) {
        $attempt++
        $text = Invoke-DirectCommandCapture -Command $Command -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Environment $Environment -LogPath $null
        $segments.Add(("Attempt {0}`r`n{1}" -f $attempt, $text)) | Out-Null

        $status = $text | ConvertFrom-Json
        $lastStatus = $status
        if ($status.running -and $status.healthCheck.checked -and $status.healthCheck.healthy) {
            if ($LogPath) {
                Set-Content -LiteralPath $LogPath -Value ($segments -join "`r`n`r`n") -Encoding UTF8
            }
            Write-JsonArtifact -Value $status -Path $JsonPath
            return $status
        }

        Start-Sleep -Seconds 1
    }

    if ($LogPath) {
        Set-Content -LiteralPath $LogPath -Value ($segments -join "`r`n`r`n") -Encoding UTF8
    }
    if ($null -ne $lastStatus) {
        Write-JsonArtifact -Value $lastStatus -Path $JsonPath
        throw ("CPA runtime did not become healthy within {0}s. Last phase: {1}. Last message: {2}" -f $TimeoutSec, $lastStatus.phase, $lastStatus.healthCheck.message)
    }

    throw ("CPA runtime did not return any status within {0}s." -f $TimeoutSec)
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot ("out\\validation\\full-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
} else {
    $OutputDir = Resolve-OutputPath -BasePath $repoRoot -Path $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$logsDir = Join-Path $OutputDir "logs"
$jsonDir = Join-Path $OutputDir "json"
$installRoot = Join-Path $OutputDir "install-root"
$summaryPath = Join-Path $OutputDir "summary.json"

New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null
if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null

$npmCommand = Resolve-CommandPath -Candidates @("npm.cmd", "npm")
$goCommand = Resolve-CommandPath -Candidates @("go.exe", "go")
$powershellCommand = Resolve-CommandPath -Candidates @("powershell.exe", "powershell")
$electronSmokeScript = Join-Path $scriptRoot "test-electron-smoke.ps1"

$envOverrides = @{
    CPAD_REPO_ROOT = $repoRoot
    CPAD_INSTALL_ROOT = $installRoot
    CPAD_PLUGIN_SOURCE_ROOT = Join-Path $repoRoot "plugins"
    CPAD_CPA_SOURCE_ROOT = Join-Path $repoRoot "sources\\official-backend"
    CPAD_CPA_OVERLAY_SOURCE_ROOT = Join-Path $repoRoot "sources\\cpa-uv-overlay"
}

$electronMainPath = Join-Path $repoRoot "out\\main\\index.js"
$electronPreloadPath = Join-Path $repoRoot "out\\preload\\index.js"
$electronRendererPath = Join-Path $repoRoot "out\\renderer\\index.html"
$serviceBinaryPath = Join-Path $repoRoot "service\\bin\\cpad-service.exe"
$shimBinaryPath = Join-Path $repoRoot "service\\bin\\codex.exe"

$script:StepResults = New-Object System.Collections.Generic.List[object]
$selectedPluginID = $null
$cpaStarted = $false
$failureMessage = $null
$goTestFailureMessage = $null
$validationCPAPort = 0

try {
    Invoke-ValidationStep -Name "Build Electron app" -LogPath (Join-Path $logsDir "01-build-electron.log") -JsonPath $null -Action {
        $arguments = @("run", "--silent", "build")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "01-build-electron.log") | Out-Null
        Assert-PathExists -Path $electronMainPath -Description "Electron main bundle"
        Assert-PathExists -Path $electronPreloadPath -Description "Electron preload bundle"
        Assert-PathExists -Path $electronRendererPath -Description "Electron renderer entrypoint"
    } | Out-Null

    Invoke-ValidationStep -Name "Build Go service" -LogPath (Join-Path $logsDir "02-build-service.log") -JsonPath $null -Action {
        $arguments = @("run", "--silent", "build:service")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "02-build-service.log") | Out-Null
        Assert-PathExists -Path $serviceBinaryPath -Description "service binary"
    } | Out-Null

    Invoke-ValidationStep -Name "Build Codex shim" -LogPath (Join-Path $logsDir "03-build-shim.log") -JsonPath $null -Action {
        $arguments = @("run", "--silent", "build:shim")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "03-build-shim.log") | Out-Null
        Assert-PathExists -Path $shimBinaryPath -Description "Codex shim binary"
    } | Out-Null

    try {
        Invoke-ValidationStep -Name "Run go test" -LogPath (Join-Path $logsDir "04-go-test.log") -JsonPath $null -Action {
            $arguments = @("-C", "service", "test", "./...")
            Write-Host ("    {0}" -f (Format-CommandLine -Command $goCommand -Arguments $arguments))
            Invoke-CommandCapture -Command $goCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "04-go-test.log") | Out-Null
        } | Out-Null
    } catch {
        $goTestFailureMessage = $_.Exception.Message
        Write-Host ("    continuing after go test failure: {0}" -f $goTestFailureMessage) -ForegroundColor Yellow
    }

    $initialServiceStatus = Invoke-ValidationStep -Name "Inspect service status" -LogPath (Join-Path $logsDir "05-service-status-initial.log") -JsonPath (Join-Path $jsonDir "service-status-initial.json") -Action {
        $arguments = @("run", "--silent", "service:status")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        $text = Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "05-service-status-initial.log")
        $status = $text | ConvertFrom-Json
        Assert-Condition (Same-Path -Left $status.installRoot -Right $installRoot) "service:status returned an unexpected installRoot."
        Assert-Condition ($status.cpaRuntime.sourceExists) "service:status did not resolve the CPA source."
        Assert-Condition ($status.pluginMarket.sourceExists) "service:status did not resolve the plugin source."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($status.managerStatus.serviceName)) "service:status did not include managerStatus.serviceName."
        Write-JsonArtifact -Value $status -Path (Join-Path $jsonDir "service-status-initial.json")
        return $status
    }

    $cpaBuildStatus = Invoke-ValidationStep -Name "Build CPA runtime" -LogPath (Join-Path $logsDir "06-cpa-runtime-build.log") -JsonPath (Join-Path $jsonDir "cpa-runtime-build.json") -Action {
        $arguments = @("cpa-runtime", "build")
        Assert-PathExists -Path $serviceBinaryPath -Description "service binary"
        Write-Host ("    {0}" -f (Format-CommandLine -Command $serviceBinaryPath -Arguments $arguments))
        $text = Invoke-DirectCommandCapture -Command $serviceBinaryPath -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "06-cpa-runtime-build.log")
        $status = $text | ConvertFrom-Json
        Assert-Condition ($status.sourceExists) "CPA runtime build did not detect the managed source."
        Assert-Condition ($status.binaryExists) "CPA runtime build did not produce the managed binary."
        Assert-Condition ($status.configExists) "CPA runtime build did not materialize config.yaml."
        Assert-Condition ($status.phase -eq "built") ("Unexpected CPA runtime phase after build: {0}" -f $status.phase)

        $script:validationCPAPort = Get-FreeTcpPort
        Set-CPARuntimeConfigPort -ConfigPath $status.configPath -Port $script:validationCPAPort
        Write-Host ("    configured isolated CPA runtime port: {0}" -f $script:validationCPAPort) -ForegroundColor DarkGreen

        $statusText = Invoke-DirectCommandCapture -Command $serviceBinaryPath -Arguments @("cpa-runtime", "status") -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $null
        $updatedStatus = $statusText | ConvertFrom-Json
        Assert-Condition ($updatedStatus.configInsight.port -eq $script:validationCPAPort) ("CPA runtime config port did not update to {0}" -f $script:validationCPAPort)
        Write-JsonArtifact -Value $updatedStatus -Path (Join-Path $jsonDir "cpa-runtime-build.json")
        return $updatedStatus
    }

    $cpaHealthyStatus = Invoke-ValidationStep -Name "Start CPA runtime and wait for health" -LogPath (Join-Path $logsDir "07-cpa-runtime-health.log") -JsonPath (Join-Path $jsonDir "cpa-runtime-health.json") -Action {
        $startArguments = @("cpa-runtime", "start")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $serviceBinaryPath -Arguments $startArguments))
        $startText = Invoke-DirectCommandCapture -Command $serviceBinaryPath -Arguments $startArguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "07-cpa-runtime-start.log")
        $startStatus = $startText | ConvertFrom-Json
        Assert-Condition ($startStatus.binaryExists) "CPA runtime start did not find the managed binary."
        Assert-Condition ($startStatus.running) "CPA runtime was not running immediately after start."
        $script:cpaStarted = $true

        $healthyStatus = Wait-ForCPAHealthy `
            -Command $serviceBinaryPath `
            -Arguments @("cpa-runtime", "status") `
            -WorkingDirectory $repoRoot `
            -Environment $envOverrides `
            -TimeoutSec $CPAHealthTimeoutSec `
            -LogPath (Join-Path $logsDir "07-cpa-runtime-health.log") `
            -JsonPath (Join-Path $jsonDir "cpa-runtime-health.json")

        Assert-Condition ($healthyStatus.running) "CPA runtime stopped before passing health checks."
        Assert-Condition ($healthyStatus.healthCheck.healthy) "CPA runtime health check did not pass."
        return $healthyStatus
    }

    $pluginRefreshStatus = Invoke-ValidationStep -Name "Refresh plugin market" -LogPath (Join-Path $logsDir "08-plugin-market-refresh.log") -JsonPath (Join-Path $jsonDir "plugin-market-refresh.json") -Action {
        $arguments = @("run", "--silent", "plugin:market:refresh")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        $text = Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "08-plugin-market-refresh.log")
        $status = $text | ConvertFrom-Json
        Assert-Condition ($status.sourceExists) "Plugin market refresh did not resolve the plugin source."
        Assert-Condition (($status.plugins | Measure-Object).Count -gt 0) "Plugin market refresh returned no plugins."
        Write-JsonArtifact -Value $status -Path (Join-Path $jsonDir "plugin-market-refresh.json")
        return $status
    }
    $selectedPluginID = Select-PluginForValidation -PluginMarketStatus $pluginRefreshStatus -RequestedPluginID $PluginId

    $pluginOperationStatus = Invoke-ValidationStep -Name "Exercise plugin market operations" -LogPath (Join-Path $logsDir "09-plugin-market-ops.log") -JsonPath (Join-Path $jsonDir "plugin-market-ops.json") -Action {
        $segments = New-Object System.Collections.Generic.List[string]

        $installText = Invoke-CommandCapture -Command $goCommand -Arguments @("-C", "service", "run", "./cmd/cpad-service", "plugin-market", "install", $selectedPluginID) -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $null
        $segments.Add(("install`r`n{0}" -f $installText)) | Out-Null
        $installStatus = $installText | ConvertFrom-Json
        $installedPlugin = Get-PluginStatus -PluginMarketStatus $installStatus -ID $selectedPluginID
        Assert-Condition ($null -ne $installedPlugin) "Installed plugin was missing from plugin market status."
        Assert-Condition ($installedPlugin.installed) ("Plugin install did not mark '{0}' as installed." -f $selectedPluginID)

        $diagnoseText = Invoke-CommandCapture -Command $goCommand -Arguments @("-C", "service", "run", "./cmd/cpad-service", "plugin-market", "diagnose", $selectedPluginID) -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $null
        $segments.Add(("diagnose`r`n{0}" -f $diagnoseText)) | Out-Null
        $diagnoseStatus = $diagnoseText | ConvertFrom-Json
        $diagnosedPlugin = Get-PluginStatus -PluginMarketStatus $diagnoseStatus -ID $selectedPluginID
        Assert-Condition ($null -ne $diagnosedPlugin) "Diagnosed plugin was missing from plugin market status."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($diagnosedPlugin.message)) ("Plugin diagnose did not populate a message for '{0}'." -f $selectedPluginID)

        $disableText = Invoke-CommandCapture -Command $goCommand -Arguments @("-C", "service", "run", "./cmd/cpad-service", "plugin-market", "disable", $selectedPluginID) -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $null
        $segments.Add(("disable`r`n{0}" -f $disableText)) | Out-Null
        $disableStatus = $disableText | ConvertFrom-Json
        $disabledPlugin = Get-PluginStatus -PluginMarketStatus $disableStatus -ID $selectedPluginID
        Assert-Condition ($null -ne $disabledPlugin) "Disabled plugin was missing from plugin market status."
        Assert-Condition (-not $disabledPlugin.enabled) ("Plugin disable did not mark '{0}' as disabled." -f $selectedPluginID)

        $enableText = Invoke-CommandCapture -Command $goCommand -Arguments @("-C", "service", "run", "./cmd/cpad-service", "plugin-market", "enable", $selectedPluginID) -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $null
        $segments.Add(("enable`r`n{0}" -f $enableText)) | Out-Null
        $enableStatus = $enableText | ConvertFrom-Json
        $enabledPlugin = Get-PluginStatus -PluginMarketStatus $enableStatus -ID $selectedPluginID
        Assert-Condition ($null -ne $enabledPlugin) "Enabled plugin was missing from plugin market status."
        Assert-Condition ($enabledPlugin.installed) ("Plugin enable lost the installed state for '{0}'." -f $selectedPluginID)
        Assert-Condition ($enabledPlugin.enabled) ("Plugin enable did not mark '{0}' as enabled." -f $selectedPluginID)

        Set-Content -LiteralPath (Join-Path $logsDir "09-plugin-market-ops.log") -Value ($segments -join "`r`n`r`n") -Encoding UTF8
        Write-JsonArtifact -Value $enableStatus -Path (Join-Path $jsonDir "plugin-market-ops.json")
        return $enableStatus
    }

    if ($SkipElectronSmoke) {
        Add-StepRecord -Name "Packaged Electron smoke" -Status "skipped" -StartedAt (Get-Date) -FinishedAt (Get-Date) -LogPath $null -JsonPath $null -Message "Skipped by -SkipElectronSmoke."
    } else {
        $electronSmokeSummaryPath = Join-Path $jsonDir "electron-smoke.json"
        Invoke-ValidationStep -Name "Packaged Electron smoke" -LogPath (Join-Path $logsDir "10-electron-smoke.log") -JsonPath $electronSmokeSummaryPath -Action {
            $arguments = @(
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                $electronSmokeScript,
                "-OutputDir",
                (Join-Path $OutputDir "electron-smoke"),
                "-OutputJsonPath",
                $electronSmokeSummaryPath,
                "-BasePort",
                "9440"
            )
            Write-Host ("    {0}" -f (Format-CommandLine -Command $powershellCommand -Arguments $arguments))
            Invoke-CommandCapture -Command $powershellCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "10-electron-smoke.log") | Out-Null
            $summary = Get-Content -Raw -LiteralPath $electronSmokeSummaryPath | ConvertFrom-Json
            Assert-Condition ($summary.status -eq "passed") "Electron smoke summary reported failure."
            Assert-Condition ($summary.debugLoop.status -eq "passed") "debug-loop did not complete successfully."
            Write-JsonArtifact -Value $summary -Path $electronSmokeSummaryPath
            return $summary
        } | Out-Null
    }

    Invoke-ValidationStep -Name "Stop CPA runtime" -LogPath (Join-Path $logsDir "11-cpa-runtime-stop.log") -JsonPath (Join-Path $jsonDir "cpa-runtime-stop.json") -Action {
        $arguments = @("cpa-runtime", "stop")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $serviceBinaryPath -Arguments $arguments))
        $text = Invoke-DirectCommandCapture -Command $serviceBinaryPath -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "11-cpa-runtime-stop.log")
        $status = $text | ConvertFrom-Json
        Assert-Condition (-not $status.running) "CPA runtime stop left the runtime running."
        Write-JsonArtifact -Value $status -Path (Join-Path $jsonDir "cpa-runtime-stop.json")
        $script:cpaStarted = $false
        return $status
    } | Out-Null

    $finalServiceStatus = Invoke-ValidationStep -Name "Inspect final service status" -LogPath (Join-Path $logsDir "12-service-status-final.log") -JsonPath (Join-Path $jsonDir "service-status-final.json") -Action {
        $arguments = @("run", "--silent", "service:status")
        Write-Host ("    {0}" -f (Format-CommandLine -Command $npmCommand -Arguments $arguments))
        $text = Invoke-CommandCapture -Command $npmCommand -Arguments $arguments -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath (Join-Path $logsDir "12-service-status-final.log")
        $status = $text | ConvertFrom-Json
        Assert-Condition (Same-Path -Left $status.installRoot -Right $installRoot) "final service:status returned an unexpected installRoot."
        Assert-Condition (-not $status.cpaRuntime.running) "final service:status still reports a running CPA runtime."
        if ($selectedPluginID) {
            $plugin = Get-PluginStatus -PluginMarketStatus $status.pluginMarket -ID $selectedPluginID
            Assert-Condition ($null -ne $plugin) ("final service:status did not include plugin '{0}'." -f $selectedPluginID)
            Assert-Condition ($plugin.installed) ("final service:status lost the installed state for '{0}'." -f $selectedPluginID)
            Assert-Condition ($plugin.enabled) ("final service:status lost the enabled state for '{0}'." -f $selectedPluginID)
        }
        Write-JsonArtifact -Value $status -Path (Join-Path $jsonDir "service-status-final.json")
        return $status
    }
} catch {
    $failureMessage = $_.Exception.Message
} finally {
    if ($cpaStarted) {
        $cleanupLogPath = Join-Path $logsDir "cleanup-cpa-stop.log"
        try {
            $cleanupOutput = Invoke-DirectCommandCapture -Command $serviceBinaryPath -Arguments @("cpa-runtime", "stop") -WorkingDirectory $repoRoot -Environment $envOverrides -LogPath $cleanupLogPath
            $cleanupStatus = $cleanupOutput | ConvertFrom-Json
            Write-JsonArtifact -Value $cleanupStatus -Path (Join-Path $jsonDir "cleanup-cpa-stop.json")
            Add-StepRecord -Name "Cleanup CPA runtime stop" -Status "passed" -StartedAt (Get-Date) -FinishedAt (Get-Date) -LogPath $cleanupLogPath -JsonPath (Join-Path $jsonDir "cleanup-cpa-stop.json") -Message ""
        } catch {
            Add-StepRecord -Name "Cleanup CPA runtime stop" -Status "failed" -StartedAt (Get-Date) -FinishedAt (Get-Date) -LogPath $cleanupLogPath -JsonPath (Join-Path $jsonDir "cleanup-cpa-stop.json") -Message $_.Exception.Message
        }
    }

    $failedSteps = @($script:StepResults | Where-Object { $_.status -eq "failed" })
    $summaryError = $failureMessage
    if ([string]::IsNullOrWhiteSpace($summaryError) -and ($failedSteps | Measure-Object).Count -gt 0) {
        $summaryError = $failedSteps[0].message
    }

    $summary = [pscustomobject]@{
        status = if (($failedSteps | Measure-Object).Count -gt 0) { "failed" } else { "passed" }
        repoRoot = $repoRoot
        outputDir = $OutputDir
        installRoot = $installRoot
        selectedPluginId = $selectedPluginID
        skippedElectronSmoke = [bool]$SkipElectronSmoke
        artifacts = [pscustomobject]@{
            logsDir = $logsDir
            jsonDir = $jsonDir
        }
        steps = $script:StepResults
        completedAt = (Get-Date).ToUniversalTime().ToString("o")
        error = $summaryError
    }
    Write-JsonArtifact -Value $summary -Path $summaryPath
}

$failedSteps = @($script:StepResults | Where-Object { $_.status -eq "failed" })
if (($failedSteps | Measure-Object).Count -gt 0) {
    if ($failureMessage) {
        throw $failureMessage
    }

    throw $failedSteps[0].message
}

Write-Host ""
Write-Host "Full validation completed." -ForegroundColor Green
Write-Host ("  OutputDir: {0}" -f $OutputDir)
Write-Host ("  Summary:   {0}" -f $summaryPath)
