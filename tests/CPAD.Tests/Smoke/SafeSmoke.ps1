[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$AppPath,
    [string]$SmokeRoot,
    [switch]$Launch,
    [switch]$RemoveSmokeRoot,
    [int]$LaunchWaitSeconds = 12,
    [int]$HealthWaitSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Section {
    param([string]$Title)

    Write-Output ""
    Write-Output ("=== {0} ===" -f $Title)
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Resolve-RepositoryRoot {
    param([string]$Override)

    $candidate = if ([string]::IsNullOrWhiteSpace($Override)) {
        [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    }
    else {
        [System.IO.Path]::GetFullPath($Override)
    }

    if (-not (Test-Path -LiteralPath (Join-Path $candidate "CliProxyApiDesktop.sln"))) {
        throw "Repository root does not contain CliProxyApiDesktop.sln: $candidate"
    }

    return $candidate
}

function Get-DisplayPath {
    param(
        [string]$FullPath,
        [string]$Root
    )

    if ($FullPath.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $FullPath.Substring($Root.Length).TrimStart("\")
    }

    return $FullPath
}

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]]$List,
        [string]$Kind,
        [string]$Detail
    )

    $List.Add([pscustomobject]@{
            Kind   = $Kind
            Detail = $Detail
        }) | Out-Null
}

function Get-ActiveTreeFiles {
    param(
        [string]$Root,
        [string[]]$RelativeRoots,
        [string[]]$AllowedExtensions
    )

    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($relativeRoot in $RelativeRoots) {
        $absoluteRoot = Join-Path $Root $relativeRoot
        if (-not (Test-Path -LiteralPath $absoluteRoot)) {
            continue
        }

        $items = Get-ChildItem -LiteralPath $absoluteRoot -Recurse -File | Where-Object {
            $_.FullName -notmatch "\\(bin|obj|artifacts|reference-repos)\\"
        }

        if ($AllowedExtensions.Count -gt 0) {
            $items = $items | Where-Object { $AllowedExtensions -contains $_.Extension.ToLowerInvariant() }
        }

        foreach ($item in $items) {
            $files.Add($item) | Out-Null
        }
    }

    return $files
}

function Get-MatchDetails {
    param(
        [System.Collections.Generic.List[System.IO.FileInfo]]$Files,
        [string]$Pattern,
        [string]$RepoRoot
    )

    if ($Files.Count -eq 0) {
        return @()
    }

    return @(
        $Files |
        Select-String -Pattern $Pattern |
        ForEach-Object {
            "{0}:{1}: {2}" -f (Get-DisplayPath -FullPath $_.Path -Root $RepoRoot), $_.LineNumber, $_.Line.Trim()
        }
    )
}

function Get-GeneratedResidueSample {
    param(
        [string]$Path,
        [string]$RepoRoot,
        [string[]]$NamePatterns,
        [int]$MaxCount = 5
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $results = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
        foreach ($pattern in $NamePatterns) {
            if ($_.Name -like $pattern) {
                return $true
            }
        }

        return $false
    }

    if (-not $results) {
        return $null
    }

    $sample = $results | Select-Object -First $MaxCount | ForEach-Object {
        Get-DisplayPath -FullPath $_.FullName -Root $RepoRoot
    }

    return [pscustomobject]@{
        Count  = $results.Count
        Sample = @($sample)
    }
}

function New-SmokeRoot {
    param([string]$Override)

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        return [System.IO.Path]::GetFullPath($Override)
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    return Join-Path ([System.IO.Path]::GetTempPath()) ("cpad-safe-smoke-{0}-{1}" -f $stamp, $PID)
}

function Escape-YamlString {
    param([string]$Value)

    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function Write-IsolatedConfiguration {
    param(
        [string]$SmokeRootPath,
        [string]$RepositoryRoot
    )

    $configDirectory = Join-Path $SmokeRootPath "config"
    $userProfileDirectory = Join-Path $SmokeRootPath "userprofile"
    $legacyConfigDirectory = Join-Path $userProfileDirectory ".cli-proxy-api"
    $legacyAuthDirectory = Join-Path $SmokeRootPath "legacy-auth"
    $codexHomeDirectory = Join-Path $SmokeRootPath "codex-home"
    $tempDirectory = Join-Path $SmokeRootPath "tmp"

    foreach ($directory in @(
            $SmokeRootPath,
            $configDirectory,
            $userProfileDirectory,
            $legacyConfigDirectory,
            $legacyAuthDirectory,
            $codexHomeDirectory,
            $tempDirectory
        )) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $desktopSettings = @'
{
  "backendPort": 5317,
  "managementKeyReference": "cpad-management-key",
  "preferredCodexSource": "Official",
  "startWithWindows": false,
  "minimizeToTrayOnClose": true,
  "enableTrayIcon": true,
  "checkForUpdatesOnStartup": false,
  "useBetaChannel": false,
  "themeMode": "System",
  "minimumLogLevel": "Information",
  "enableDebugTools": false,
  "lastRepositoryPath": "__REPO_ROOT__"
}
'@.Replace("__REPO_ROOT__", $RepositoryRoot.Replace("\", "\\"))
    Write-Utf8NoBom -Path (Join-Path $configDirectory "desktop.json") -Content $desktopSettings

    $legacyConfig = @"
host: "127.0.0.1"
port: 5317
remote-management:
  allow-remote: false
  secret-key: "smoke-only"
  disable-control-panel: true
  disable-auto-update-panel: true
auth-dir: "$(Escape-YamlString -Value $legacyAuthDirectory)"
api-keys:
  - "sk-smoke"
logging-to-file: true
oauth-model-alias:
  codex:
    - name: "gpt-5.4"
      alias: "gpt-5-codex"
      fork: true
"@
    Write-Utf8NoBom -Path (Join-Path $legacyConfigDirectory "config.yaml") -Content $legacyConfig

    return [pscustomobject]@{
        SmokeRoot       = $SmokeRootPath
        UserProfileRoot = $userProfileDirectory
        CodexHomeRoot   = $codexHomeDirectory
        TempRoot        = $tempDirectory
    }
}

function Get-ProcessNode {
    param([int]$ProcessId)

    $process = Get-CimInstance Win32_Process -Filter ("ProcessId = {0}" -f $ProcessId) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }

    return [pscustomobject]@{
        ProcessId      = [int]$process.ProcessId
        ParentProcessId = [int]$process.ParentProcessId
        Name           = [string]$process.Name
        ExecutablePath = [string]$process.ExecutablePath
    }
}

function Get-ChildProcessTree {
    param([int]$RootProcessId)

    $results = New-Object System.Collections.Generic.List[object]
    $pending = New-Object System.Collections.Generic.Queue[int]
    $pending.Enqueue($RootProcessId)

    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        $children = Get-CimInstance Win32_Process -Filter ("ParentProcessId = {0}" -f $parentId) -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            $node = [pscustomobject]@{
                ProcessId      = [int]$child.ProcessId
                ParentProcessId = [int]$child.ParentProcessId
                Name           = [string]$child.Name
                ExecutablePath = [string]$child.ExecutablePath
            }
            $results.Add($node) | Out-Null
            $pending.Enqueue([int]$child.ProcessId)
        }
    }

    return $results
}

function Stop-ExactProcess {
    param(
        [int]$ProcessId,
        [switch]$TryCloseWindow
    )

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    if ($TryCloseWindow -and $process.MainWindowHandle -ne 0) {
        $null = $process.CloseMainWindow()
        if ($process.WaitForExit(5000)) {
            return
        }
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    $finished = $process.WaitForExit(5000)
    if (-not $finished) {
        throw "Process $ProcessId did not exit within 5 seconds."
    }
}

function Invoke-StaticVerification {
    param([string]$RepositoryRoot)

    Write-Section "Static Verification"

    $failures = New-Object System.Collections.Generic.List[object]
    $warnings = New-Object System.Collections.Generic.List[object]

    $activeCodeFiles = Get-ActiveTreeFiles -Root $RepositoryRoot -RelativeRoots @("src", "tests", "resources", "build") -AllowedExtensions @(
        ".cs",
        ".xaml",
        ".csproj",
        ".props",
        ".targets",
        ".json",
        ".xml",
        ".yaml",
        ".yml"
    )

    $legacyCodePatterns = @(
        @{ Label = "Legacy WebView2 namespace/package"; Pattern = "Microsoft\.Web\.WebView2|CoreWebView2" },
        @{ Label = "Legacy WebView2 runtime loader"; Pattern = "WebView2Loader|MicrosoftEdgeWebView2Setup" },
        @{ Label = "Legacy management static wiring"; Pattern = "MANAGEMENT_STATIC_PATH|SetVirtualHostNameToFolderMapping" }
    )

    foreach ($entry in $legacyCodePatterns) {
        $matches = Get-MatchDetails -Files $activeCodeFiles -Pattern $entry.Pattern -RepoRoot $RepositoryRoot
        foreach ($match in $matches) {
            Add-Finding -List $failures -Kind $entry.Label -Detail $match
        }
    }

    $activeFiles = Get-ActiveTreeFiles -Root $RepositoryRoot -RelativeRoots @("src", "tests", "resources", "build") -AllowedExtensions @()
    $legacyNamedFiles = $activeFiles | Where-Object {
        $_.FullName -notlike "*\tests\CPAD.Tests\Smoke\*" -and (
            $_.Name -ieq "management.html" -or
            $_.Name -like "*DesktopHost*" -or
            $_.Name -like "*Onboarding*"
        )
    }
    foreach ($file in $legacyNamedFiles) {
        Add-Finding -List $failures -Kind "Legacy file still present in active tree" -Detail (Get-DisplayPath -FullPath $file.FullName -Root $RepositoryRoot)
    }

    $legacyScriptFiles = $activeFiles | Where-Object {
        $_.FullName -notlike "*\tests\CPAD.Tests\Smoke\*" -and
        @(".ps1", ".cmd", ".bat", ".iss") -contains $_.Extension.ToLowerInvariant()
    }
    foreach ($file in $legacyScriptFiles) {
        Add-Finding -List $failures -Kind "Legacy build/script artifact in active tree" -Detail (Get-DisplayPath -FullPath $file.FullName -Root $RepositoryRoot)
    }

    $webView2Directory = Join-Path $RepositoryRoot "resources\webview2"
    if (Test-Path -LiteralPath $webView2Directory) {
        Add-Finding -List $warnings -Kind "Legacy directory still present" -Detail (Get-DisplayPath -FullPath $webView2Directory -Root $RepositoryRoot)
    }

    $bundledReadmes = @(
        (Join-Path $RepositoryRoot "resources\backend\windows-x64\README.md"),
        (Join-Path $RepositoryRoot "resources\backend\windows-x64\README_CN.md")
    ) | Where-Object { Test-Path -LiteralPath $_ }
    foreach ($path in $bundledReadmes) {
        Add-Finding -List $warnings -Kind "Bundled backend doc is upstream content" -Detail (Get-DisplayPath -FullPath $path -Root $RepositoryRoot)
    }

    $publishResidue = Get-GeneratedResidueSample -Path (Join-Path $RepositoryRoot "artifacts\publish") -RepoRoot $RepositoryRoot -NamePatterns @("DesktopHost*", "*WebView2*", "management.html")
    if ($null -ne $publishResidue) {
        Add-Finding -List $warnings -Kind "Historical publish residue" -Detail ("{0} matching files under artifacts\publish; sample: {1}" -f $publishResidue.Count, ($publishResidue.Sample -join ", "))
    }

    $installerPath = Join-Path $RepositoryRoot "artifacts\installer"
    if (Test-Path -LiteralPath $installerPath) {
        $installerFiles = Get-ChildItem -LiteralPath $installerPath -Recurse -File -ErrorAction SilentlyContinue
        if ($installerFiles) {
            $sample = $installerFiles | Select-Object -First 3 | ForEach-Object { Get-DisplayPath -FullPath $_.FullName -Root $RepositoryRoot }
            Add-Finding -List $warnings -Kind "Installer output exists" -Detail ("{0} file(s) under artifacts\installer; sample: {1}" -f $installerFiles.Count, ($sample -join ", "))
        }
    }

    $generatedSourceResidue = Get-GeneratedResidueSample -Path (Join-Path $RepositoryRoot "src") -RepoRoot $RepositoryRoot -NamePatterns @("DesktopHost*", "*WebView2*", "management.html")
    if ($null -ne $generatedSourceResidue) {
        Add-Finding -List $warnings -Kind "Generated source residue in bin/obj" -Detail ("{0} matching files under src; sample: {1}" -f $generatedSourceResidue.Count, ($generatedSourceResidue.Sample -join ", "))
    }

    $generatedTestResidue = Get-GeneratedResidueSample -Path (Join-Path $RepositoryRoot "tests") -RepoRoot $RepositoryRoot -NamePatterns @("DesktopHost*", "*WebView2*", "management.html")
    if ($null -ne $generatedTestResidue) {
        Add-Finding -List $warnings -Kind "Generated test residue in bin/obj" -Detail ("{0} matching files under tests; sample: {1}" -f $generatedTestResidue.Count, ($generatedTestResidue.Sample -join ", "))
    }

    if ($failures.Count -eq 0) {
        Write-Output "[pass] No active legacy source/build references were detected."
    }
    else {
        Write-Output ("[fail] Active legacy findings: {0}" -f $failures.Count)
        foreach ($failure in $failures) {
            Write-Output ("  - {0}: {1}" -f $failure.Kind, $failure.Detail)
        }
    }

    if ($warnings.Count -eq 0) {
        Write-Output "[pass] No generated or documentation residue warnings were detected."
    }
    else {
        Write-Output ("[warn] Residue and acceptance warnings: {0}" -f $warnings.Count)
        foreach ($warning in $warnings) {
            Write-Output ("  - {0}: {1}" -f $warning.Kind, $warning.Detail)
        }
    }

    return [pscustomobject]@{
        Failures = $failures
        Warnings = $warnings
    }
}

function Invoke-LaunchSmoke {
    param(
        [string]$RepositoryRoot,
        [string]$ApplicationPath,
        [string]$SmokeRootOverride,
        [bool]$DeleteSmokeRoot,
        [int]$WaitSeconds,
        [int]$HealthSeconds
    )

    if ([string]::IsNullOrWhiteSpace($ApplicationPath)) {
        throw "Launch mode requires -AppPath."
    }

    $resolvedAppPath = [System.IO.Path]::GetFullPath($ApplicationPath)
    if (-not (Test-Path -LiteralPath $resolvedAppPath)) {
        throw "CPAD.exe was not found: $resolvedAppPath"
    }

    if ([System.IO.Path]::GetFileName($resolvedAppPath) -ne "CPAD.exe") {
        throw "Launch mode only accepts CPAD.exe. Refusing to run: $resolvedAppPath"
    }

    Write-Section "Launch Smoke"

    $resolvedSmokeRoot = New-SmokeRoot -Override $SmokeRootOverride
    $isolatedRoots = Write-IsolatedConfiguration -SmokeRootPath $resolvedSmokeRoot -RepositoryRoot $RepositoryRoot

    Write-Output ("Smoke root : {0}" -f $isolatedRoots.SmokeRoot)
    Write-Output ("USERPROFILE: {0}" -f $isolatedRoots.UserProfileRoot)
    Write-Output ("CODEX_HOME : {0}" -f $isolatedRoots.CodexHomeRoot)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $resolvedAppPath
    $startInfo.WorkingDirectory = [System.IO.Path]::GetDirectoryName($resolvedAppPath)
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables["CPAD_APP_ROOT"] = $isolatedRoots.SmokeRoot
    $startInfo.EnvironmentVariables["CPAD_APP_MODE"] = "development"
    $startInfo.EnvironmentVariables["USERPROFILE"] = $isolatedRoots.UserProfileRoot
    $startInfo.EnvironmentVariables["HOME"] = $isolatedRoots.UserProfileRoot
    $startInfo.EnvironmentVariables["CODEX_HOME"] = $isolatedRoots.CodexHomeRoot
    $startInfo.EnvironmentVariables["TEMP"] = $isolatedRoots.TempRoot
    $startInfo.EnvironmentVariables["TMP"] = $isolatedRoots.TempRoot

    $rootProcess = $null
    try {
        $rootProcess = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $rootProcess) {
            throw "Failed to start CPAD.exe."
        }

        Write-Output ("Started CPAD.exe (PID {0})." -f $rootProcess.Id)
        Start-Sleep -Seconds $WaitSeconds

        if ($rootProcess.HasExited) {
            throw ("CPAD.exe exited early with code {0}." -f $rootProcess.ExitCode)
        }

        $childProcesses = Get-ChildProcessTree -RootProcessId $rootProcess.Id
        $backendProcess = $childProcesses | Where-Object {
            $_.Name -ieq "cli-proxy-api.exe" -and
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            $_.ExecutablePath.StartsWith($isolatedRoots.SmokeRoot, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -ne $backendProcess) {
            $backendConfigPath = Join-Path $isolatedRoots.SmokeRoot "config\cliproxyapi.yaml"
            $healthUrl = $null
            if (Test-Path -LiteralPath $backendConfigPath) {
                $portMatch = Select-String -LiteralPath $backendConfigPath -Pattern "^\s*port:\s*(\d+)\s*$" -AllMatches
                if ($portMatch) {
                    $port = $portMatch.Matches[0].Groups[1].Value
                    $healthUrl = "http://127.0.0.1:{0}/healthz" -f $port
                }
            }

            if ($null -ne $healthUrl) {
                $deadline = (Get-Date).AddSeconds($HealthSeconds)
                $healthy = $false
                while ((Get-Date) -lt $deadline) {
                    try {
                        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
                        if ($response.StatusCode -eq 200) {
                            $healthy = $true
                            break
                        }
                    }
                    catch {
                        Start-Sleep -Milliseconds 300
                    }
                }

                if ($healthy) {
                    Write-Output ("[pass] Isolated backend reached health endpoint: {0}" -f $healthUrl)
                }
                else {
                    Write-Output ("[warn] Backend child process exists, but health endpoint did not become ready within {0}s: {1}" -f $HealthSeconds, $healthUrl)
                }
            }
            else {
                Write-Output "[warn] Backend child process exists, but cliproxyapi.yaml was not available for health probing."
            }
        }
        else {
            Write-Output "[pass] CPAD.exe stayed running without spawning a backend child during the smoke window."
        }

        Write-Output "[pass] Launch smoke completed with isolated environment variables."
    }
    finally {
        if ($null -ne $rootProcess) {
            $children = Get-ChildProcessTree -RootProcessId $rootProcess.Id
            $ownedBackendProcesses = $children | Where-Object {
                $_.Name -ieq "cli-proxy-api.exe" -and
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                $_.ExecutablePath.StartsWith($isolatedRoots.SmokeRoot, [System.StringComparison]::OrdinalIgnoreCase)
            }

            foreach ($child in $ownedBackendProcesses) {
                Stop-ExactProcess -ProcessId $child.ProcessId
            }

            Stop-ExactProcess -ProcessId $rootProcess.Id -TryCloseWindow
        }

        if ($DeleteSmokeRoot -and (Test-Path -LiteralPath $isolatedRoots.SmokeRoot)) {
            Remove-Item -LiteralPath $isolatedRoots.SmokeRoot -Recurse -Force
            Write-Output ("Removed smoke root: {0}" -f $isolatedRoots.SmokeRoot)
        }
    }
}

$resolvedRepoRoot = Resolve-RepositoryRoot -Override $RepoRoot
$staticOutput = @(Invoke-StaticVerification -RepositoryRoot $resolvedRepoRoot)
foreach ($item in $staticOutput) {
    if ($item -is [string]) {
        Write-Output $item
    }
}

$staticResult = $staticOutput |
Where-Object { $null -ne $_.PSObject.Properties["Failures"] -and $null -ne $_.PSObject.Properties["Warnings"] } |
Select-Object -Last 1
if ($null -eq $staticResult) {
    throw "Static verification did not return a structured result."
}

if ($Launch) {
    Invoke-LaunchSmoke `
        -RepositoryRoot $resolvedRepoRoot `
        -ApplicationPath $AppPath `
        -SmokeRootOverride $SmokeRoot `
        -DeleteSmokeRoot $RemoveSmokeRoot.IsPresent `
        -WaitSeconds $LaunchWaitSeconds `
        -HealthSeconds $HealthWaitSeconds
}

Write-Section "Summary"
Write-Output ("Failures: {0}" -f $staticResult.Failures.Count)
Write-Output ("Warnings: {0}" -f $staticResult.Warnings.Count)
if (-not $Launch) {
    Write-Output "Launch smoke was skipped. Re-run with -Launch -AppPath <path-to-CPAD.exe> to verify isolated startup."
}

if ($staticResult.Failures.Count -gt 0) {
    exit 1
}
