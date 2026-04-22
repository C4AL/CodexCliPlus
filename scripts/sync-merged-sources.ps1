#Requires -Version 5.1
[CmdletBinding()]
param(
    [Alias("OfficialBackendSource")]
    [string]$OfficialCoreBaselineSource = "C:\Users\Reol\Cli Proxy API Desktop\upstream\CLIProxyAPI",

    [Alias("OfficialManagementCenterSource")]
    [string]$OfficialPanelBaselineSource = "C:\Users\Reol\Cli Proxy API Desktop\upstream\Cli-Proxy-API-Management-Center",

    [Alias("CpaUvSource")]
    [string]$CPAOverlaySource = "C:\Users\Reol\workspace\CPA-UV-publish",

    [Alias("EdgeControlSource")]
    [string]$EdgeControlPluginSource = "C:\Users\Reol\plugins\edge-control"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-DirectoryExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw ("{0} does not exist: {1}" -f $Label, $Path)
    }
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $normalizedRoot = (Resolve-AbsolutePath -Path $Root).TrimEnd('\') + '\'
    $normalizedPath = Resolve-AbsolutePath -Path $Path

    if (-not $normalizedPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ("Refusing to sync {0} outside repo root: {1}" -f $Label, $normalizedPath)
    }
}

function Invoke-RoboSync {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $null = New-Item -ItemType Directory -Force -Path $Destination

    $arguments = @(
        $Source,
        $Destination,
        "/MIR",
        "/R:2",
        "/W:1",
        "/NFL",
        "/NDL",
        "/NJH",
        "/NJS",
        "/NP",
        "/XD",
        ".git",
        "node_modules",
        "dist",
        "out",
        "release",
        ".vite",
        "coverage"
    )

    Write-Host ("==> Syncing {0}" -f $Label) -ForegroundColor Cyan
    Write-Host ("    from: {0}" -f $Source)
    Write-Host ("    to:   {0}" -f $Destination)

    & robocopy @arguments | Out-Host
    if ($LASTEXITCODE -gt 7) {
        throw ("robocopy failed for {0} with exit code {1}" -f $Label, $LASTEXITCODE)
    }
}

function Get-GitValueOrEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryPath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    try {
        return (& git -C $RepositoryPath @Arguments 2>$null | Select-Object -First 1).Trim()
    }
    catch {
        return ""
    }
}

function Write-SourceManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Id,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $manifest = [ordered]@{
        id = $Id
        name = $Name
        sourcePath = $Source
        repository = Get-GitValueOrEmpty -RepositoryPath $Source -Arguments @("remote", "get-url", "origin")
        branch = Get-GitValueOrEmpty -RepositoryPath $Source -Arguments @("rev-parse", "--abbrev-ref", "HEAD")
        commit = Get-GitValueOrEmpty -RepositoryPath $Source -Arguments @("rev-parse", "HEAD")
        syncedAt = [DateTime]::UtcNow.ToString("o")
    }

    $manifestPath = Join-Path $Destination ".cpad-source.json"
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function Write-BundleManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        [string]$InstallDirectory,

        [Parameter(Mandatory = $true)]
        [System.Object[]]$Entries
    )

    $manifest = [ordered]@{
        kind = $Kind
        installDirectory = $InstallDirectory
        generatedAt = [DateTime]::UtcNow.ToString("o")
        entries = $Entries
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-AbsolutePath -Path (Join-Path $scriptRoot "..")
$sourcesRoot = Resolve-AbsolutePath -Path (Join-Path $repoRoot "sources")
$pluginsRoot = Resolve-AbsolutePath -Path (Join-Path $repoRoot "plugins")

$officialCoreBaselineSource = Resolve-AbsolutePath -Path $OfficialCoreBaselineSource
$officialPanelBaselineSource = Resolve-AbsolutePath -Path $OfficialPanelBaselineSource
$cpaOverlaySource = Resolve-AbsolutePath -Path $CPAOverlaySource
$edgeControlPluginSource = Resolve-AbsolutePath -Path $EdgeControlPluginSource

Assert-DirectoryExists -Path $officialCoreBaselineSource -Label "Official core baseline source"
Assert-DirectoryExists -Path $officialPanelBaselineSource -Label "Official panel baseline source"
Assert-DirectoryExists -Path $cpaOverlaySource -Label "CPA overlay source"
Assert-DirectoryExists -Path $edgeControlPluginSource -Label "Edge Control plugin source"

$officialCoreBaselineDestination = Resolve-AbsolutePath -Path (Join-Path $sourcesRoot "official-backend")
$officialPanelBaselineDestination = Resolve-AbsolutePath -Path (Join-Path $sourcesRoot "official-management-center")
$cpaOverlayDestination = Resolve-AbsolutePath -Path (Join-Path $sourcesRoot "cpa-uv-overlay")
$edgeControlPluginDestination = Resolve-AbsolutePath -Path (Join-Path $pluginsRoot "edge-control")

Assert-PathWithinRoot -Root $repoRoot -Path $officialCoreBaselineDestination -Label "official core baseline"
Assert-PathWithinRoot -Root $repoRoot -Path $officialPanelBaselineDestination -Label "official panel baseline"
Assert-PathWithinRoot -Root $repoRoot -Path $cpaOverlayDestination -Label "CPA overlay"
Assert-PathWithinRoot -Root $repoRoot -Path $edgeControlPluginDestination -Label "edge-control plugin"

$managedSourceSpecs = @(
    [ordered]@{
        Id = "official-core-baseline"
        Name = "official-backend"
        Label = "official core baseline"
        SourcePath = $officialCoreBaselineSource
        DestinationPath = $officialCoreBaselineDestination
        InstallDirectory = "sources"
    },
    [ordered]@{
        Id = "official-panel-baseline"
        Name = "official-management-center"
        Label = "official panel baseline"
        SourcePath = $officialPanelBaselineSource
        DestinationPath = $officialPanelBaselineDestination
        InstallDirectory = "sources"
    },
    [ordered]@{
        Id = "cpa-source"
        Name = "cpa-uv-overlay"
        Label = "CPA overlay source"
        SourcePath = $cpaOverlaySource
        DestinationPath = $cpaOverlayDestination
        InstallDirectory = "sources"
    }
)

$pluginSourceSpecs = @(
    [ordered]@{
        Id = "edge-control"
        Name = "edge-control"
        Label = "edge-control plugin source"
        SourcePath = $edgeControlPluginSource
        DestinationPath = $edgeControlPluginDestination
        InstallDirectory = "plugin-source"
    }
)

foreach ($spec in ($managedSourceSpecs + $pluginSourceSpecs)) {
    Invoke-RoboSync -Source $spec.SourcePath -Destination $spec.DestinationPath -Label $spec.Label
    Write-SourceManifest -Id $spec.Id -Name $spec.Name -Source $spec.SourcePath -Destination $spec.DestinationPath
}

$managedSourceBundleManifestPath = Join-Path $sourcesRoot "cpad-source-bundle.json"
$pluginSourceBundleManifestPath = Join-Path $pluginsRoot "cpad-plugin-source-bundle.json"

Write-BundleManifest -ManifestPath $managedSourceBundleManifestPath -Kind "managed-source-bundle" -InstallDirectory "sources" -Entries @(
    foreach ($spec in $managedSourceSpecs) {
        [ordered]@{
            id = $spec.Id
            name = $spec.Name
            directory = Split-Path -Leaf $spec.DestinationPath
            installDirectory = $spec.InstallDirectory
            syncedPath = $spec.DestinationPath
            sourcePath = $spec.SourcePath
            manifestFile = ".cpad-source.json"
        }
    }
)

Write-BundleManifest -ManifestPath $pluginSourceBundleManifestPath -Kind "plugin-source-bundle" -InstallDirectory "plugin-source" -Entries @(
    foreach ($spec in $pluginSourceSpecs) {
        [ordered]@{
            id = $spec.Id
            name = $spec.Name
            directory = Split-Path -Leaf $spec.DestinationPath
            installDirectory = $spec.InstallDirectory
            syncedPath = $spec.DestinationPath
            sourcePath = $spec.SourcePath
            manifestFile = ".cpad-source.json"
        }
    }
)

Write-Host ""
Write-Host "Managed source snapshots are now present under the current repo:" -ForegroundColor Green
Write-Host ("  {0}" -f $officialCoreBaselineDestination)
Write-Host ("  {0}" -f $officialPanelBaselineDestination)
Write-Host ("  {0}" -f $cpaOverlayDestination)
Write-Host ("  {0}" -f $edgeControlPluginDestination)
Write-Host ("  {0}" -f $managedSourceBundleManifestPath)
Write-Host ("  {0}" -f $pluginSourceBundleManifestPath)
