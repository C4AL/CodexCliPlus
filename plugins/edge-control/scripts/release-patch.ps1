[CmdletBinding()]
param(
  [string]$GitHubOutputPath
)

$ErrorActionPreference = "Stop"

$PluginRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = Join-Path $PluginRoot "extension\\manifest.json"
$Manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json
$Parts = $Manifest.version.Split(".")

if ($Parts.Count -ne 3) {
  throw "Current version must be semver (x.y.z)."
}

$NewVersion = "{0}.{1}.{2}" -f $Parts[0], $Parts[1], ([int]$Parts[2] + 1)

Push-Location $PSScriptRoot
try {
  node ".\\sync-version.mjs" $NewVersion | Out-Null
  & ".\\build-release.ps1" -Version $NewVersion
} finally {
  Pop-Location
}

$Tag = "v$NewVersion"
$SourceZipPath = Join-Path $PluginRoot "dist\\edge-control-v$NewVersion-source.zip"
$QuickZipPath = Join-Path $PluginRoot "dist\\edge-control-v$NewVersion-quick-deploy.zip"
$ChecksumsPath = Join-Path $PluginRoot "dist\\checksums-v$NewVersion.txt"
$ReleaseNotesPath = Join-Path $PluginRoot "dist\\release-notes-v$NewVersion.md"

if ($GitHubOutputPath) {
  @(
    "version=$NewVersion"
    "tag=$Tag"
    "source_zip=$SourceZipPath"
    "quick_deploy_zip=$QuickZipPath"
    "checksums_file=$ChecksumsPath"
    "release_notes=$ReleaseNotesPath"
  ) | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
}

Write-Host "Patch release prepared: $Tag"
