[CmdletBinding()]
param(
    [ValidateSet("Quick", "Full", "Coverage", "Release", "Smoke", "LiveBackend")]
    [string]$Scope = "Quick",

    [string]$Configuration = "Release",

    [switch]$NoBuild,

    [decimal]$MinimumLineCoverage = 35.0
)

$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
chcp.com 65001 | Out-Null

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj"
$testResultsRoot = Join-Path $repoRoot "artifacts/test-results"
$coverageRoot = Join-Path $repoRoot "artifacts/coverage"

$filter = switch ($Scope) {
    "Quick" { "Category=Fast|Category=LocalIntegration" }
    "Full" { "Category!=LiveBackend&Category!=Smoke" }
    "Coverage" { "Category!=LiveBackend&Category!=Smoke" }
    "Release" { "Category=Packaging" }
    "Smoke" { "Category=Smoke" }
    "LiveBackend" { "Category=LiveBackend" }
}

$testArguments = @(
    "test",
    $testProject,
    "--configuration",
    $Configuration,
    "--verbosity",
    "normal",
    "--filter",
    $filter
)

if ($NoBuild) {
    $testArguments += "--no-build"
}

if ($Scope -eq "Coverage") {
    $testArguments += @(
        "--collect:XPlat Code Coverage",
        "--results-directory",
        $testResultsRoot,
        "--logger",
        "trx;LogFileName=codexcliplus-tests.trx"
    )
}

& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Scope -ne "Coverage") {
    exit 0
}

& dotnet tool run reportgenerator -- "-reports:$testResultsRoot/**/coverage.cobertura.xml" "-targetdir:$coverageRoot" "-reporttypes:Html;TextSummary;Cobertura"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$summaryPath = Join-Path $coverageRoot "Summary.txt"
if (!(Test-Path $summaryPath)) {
    throw "Coverage summary not found: $summaryPath"
}

$summary = Get-Content -Raw $summaryPath
if ($summary -notmatch "Line coverage:\s*(?<coverage>[0-9]+(?:\.[0-9]+)?)%") {
    throw "Could not parse line coverage from $summaryPath"
}

$lineCoverage = [decimal]$Matches.coverage
if ($lineCoverage -lt $MinimumLineCoverage) {
    throw "Line coverage $lineCoverage% is below baseline $MinimumLineCoverage%."
}
