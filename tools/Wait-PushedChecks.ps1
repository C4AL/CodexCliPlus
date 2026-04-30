[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Sha,

    [ValidateNotNullOrEmpty()]
    [string]$OwnerRepo = "C4AL/CodexCliPlus",

    [ValidateNotNullOrEmpty()]
    [string]$Branch = "main",

    [ValidateRange(1, 300)]
    [int]$PollSeconds = 5,

    [ValidateRange(1, 3600)]
    [int]$CreationTimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
$env:GH_NO_UPDATE_NOTIFIER = "1"
$env:NO_COLOR = "1"

$successConclusions = @("success", "skipped", "neutral")
$failureConclusions = @("failure", "cancelled", "timed_out", "action_required", "startup_failure")

function Resolve-CommitSha {
    param([string]$InputSha)

    $resolved = & git rev-parse --verify "$InputSha^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($resolved)) {
        return $resolved.Trim()
    }

    return $InputSha
}

function Invoke-GhJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$TreatMissingAsEmpty
    )

    $errorFile = [System.IO.Path]::GetTempFileName()
    try {
        $output = & gh @Arguments 2> $errorFile
        $exitCode = $LASTEXITCODE
        $errorOutput = if (Test-Path -LiteralPath $errorFile) { Get-Content -Raw -LiteralPath $errorFile } else { "" }
    }
    finally {
        Remove-Item -LiteralPath $errorFile -ErrorAction SilentlyContinue
    }

    if ($exitCode -ne 0) {
        $message = ($errorOutput | Out-String).Trim()
        if ($TreatMissingAsEmpty -and ($message -match "HTTP 404|HTTP 422|No commit found")) {
            return $null
        }

        throw "gh $($Arguments -join ' ') failed: $message"
    }

    $json = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    return $json | ConvertFrom-Json -Depth 64
}

function Get-WorkflowRuns {
    param([string]$CommitSha)

    $runs = Invoke-GhJson -Arguments @(
        "run",
        "list",
        "--repo",
        $OwnerRepo,
        "--commit",
        $CommitSha,
        "--limit",
        "100",
        "--json",
        "databaseId,name,workflowName,displayTitle,event,headBranch,headSha,status,conclusion,url,createdAt,updatedAt"
    )

    if ($null -eq $runs) {
        return @()
    }

    return @($runs)
}

function Get-CheckRuns {
    param([string]$CommitSha)

    $response = Invoke-GhJson -TreatMissingAsEmpty -Arguments @(
        "api",
        "-H",
        "Accept: application/vnd.github+json",
        "repos/$OwnerRepo/commits/$CommitSha/check-runs?per_page=100"
    )

    if ($null -eq $response -or $null -eq $response.check_runs) {
        return @()
    }

    return @($response.check_runs)
}

function Get-RemoteBranchSha {
    $remoteRef = & git ls-remote --heads origin $Branch 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteRef)) {
        return $null
    }

    return (($remoteRef -split "\s+")[0]).Trim()
}

function ConvertTo-CheckItem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        $Source
    )

    if ($Kind -eq "Workflow") {
        $name = if ([string]::IsNullOrWhiteSpace($Source.workflowName)) { $Source.name } else { $Source.workflowName }
        if ([string]::IsNullOrWhiteSpace($name)) {
            $name = $Source.displayTitle
        }

        return [pscustomobject]@{
            Kind = "workflow"
            Id = [string]$Source.databaseId
            Name = [string]$name
            Status = [string]$Source.status
            Conclusion = [string]$Source.conclusion
            Url = [string]$Source.url
        }
    }

    return [pscustomobject]@{
        Kind = "check-run"
        Id = [string]$Source.id
        Name = [string]$Source.name
        Status = [string]$Source.status
        Conclusion = [string]$Source.conclusion
        Url = [string]$Source.html_url
    }
}

function Test-ItemSucceeded {
    param($Item)

    return $Item.Status -eq "completed" -and $successConclusions -contains $Item.Conclusion
}

function Test-ItemFailed {
    param($Item)

    if ($failureConclusions -contains $Item.Status) {
        return $true
    }

    return $Item.Status -eq "completed" -and -not ($successConclusions -contains $Item.Conclusion)
}

function Write-Items {
    param(
        [Parameter(Mandatory = $true)]
        [array]$Items
    )

    foreach ($item in ($Items | Sort-Object Kind, Name, Id)) {
        $conclusion = if ([string]::IsNullOrWhiteSpace($item.Conclusion)) { "-" } else { $item.Conclusion }
        Write-Host ("{0}: {1} [{2}/{3}] {4}" -f $item.Kind, $item.Name, $item.Status, $conclusion, $item.Url)
    }
}

$fullSha = Resolve-CommitSha -InputSha $Sha
$started = Get-Date
$observedBranchAtSha = $false
$observedChecks = $false
$stableSuccessPolls = 0
$lastSuccessSignature = $null

Write-Host "Waiting for GitHub checks attached to $fullSha in $OwnerRepo."

while ($true) {
    $remoteBranchSha = Get-RemoteBranchSha
    if ($remoteBranchSha -eq $fullSha) {
        $observedBranchAtSha = $true
    }
    elseif ($observedBranchAtSha -and -not [string]::IsNullOrWhiteSpace($remoteBranchSha)) {
        Write-Warning "提交 $fullSha 已被后续推送替代；origin/$Branch 当前是 $remoteBranchSha。"
        exit 2
    }

    $workflowRuns = Get-WorkflowRuns -CommitSha $fullSha
    $checkRuns = Get-CheckRuns -CommitSha $fullSha
    $items = @(
        foreach ($run in $workflowRuns) {
            ConvertTo-CheckItem -Kind "Workflow" -Source $run
        }
        foreach ($checkRun in $checkRuns) {
            ConvertTo-CheckItem -Kind "CheckRun" -Source $checkRun
        }
    )

    if ($items.Count -gt 0) {
        $observedChecks = $true
    }

    $failures = @($items | Where-Object { Test-ItemFailed -Item $_ })
    if ($failures.Count -gt 0) {
        Write-Host "GitHub checks failed for $fullSha."
        Write-Items -Items $items
        exit 1
    }

    $allSucceeded = $items.Count -gt 0 -and @($items | Where-Object { -not (Test-ItemSucceeded -Item $_) }).Count -eq 0
    if ($allSucceeded) {
        $signature = (($items | Sort-Object Kind, Id, Name | ForEach-Object {
            "{0}:{1}:{2}:{3}" -f $_.Kind, $_.Id, $_.Status, $_.Conclusion
        }) -join "|")

        if ($signature -eq $lastSuccessSignature) {
            $stableSuccessPolls += 1
        }
        else {
            $lastSuccessSignature = $signature
            $stableSuccessPolls = 1
        }

        if ($stableSuccessPolls -ge 2) {
            Write-Host "All checks attached to $fullSha have passed."
            Write-Items -Items $items
            exit 0
        }
    }
    else {
        $stableSuccessPolls = 0
        $lastSuccessSignature = $null
    }

    $elapsedSeconds = [int](((Get-Date) - $started).TotalSeconds)
    if (-not $observedChecks -and $elapsedSeconds -ge $CreationTimeoutSeconds) {
        Write-Host "No GitHub workflow runs or check-runs were created for $fullSha within $CreationTimeoutSeconds seconds."
        exit 1
    }

    if ($items.Count -eq 0) {
        Write-Host "No checks attached yet after ${elapsedSeconds}s; polling again in ${PollSeconds}s."
    }
    else {
        $pendingCount = @($items | Where-Object { -not (Test-ItemSucceeded -Item $_) }).Count
        Write-Host ("{0} check item(s) visible, {1} pending; polling again in {2}s." -f $items.Count, $pendingCount, $PollSeconds)
    }

    Start-Sleep -Seconds $PollSeconds
}
