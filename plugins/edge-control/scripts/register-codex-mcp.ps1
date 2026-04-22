[CmdletBinding()]
param(
  [string]$ServerName = "edge-control"
)

$ErrorActionPreference = "Stop"

function Convert-ToCodexPath([string]$Value) {
  return ((Resolve-Path -LiteralPath $Value).Path -replace "\\", "/")
}

if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
  throw "codex command not found in PATH."
}

$PluginRoot = Convert-ToCodexPath (Split-Path -Parent $PSScriptRoot)
$McpServerPath = Convert-ToCodexPath (Join-Path $PSScriptRoot "mcp-server.mjs")
$ListOutput = & codex mcp list 2>$null

if ($LASTEXITCODE -eq 0 -and $ListOutput -match ("(?m)^" + [regex]::Escape($ServerName) + "\s")) {
  try {
    & codex mcp remove $ServerName | Out-Null
  } catch {
    Write-Warning "Existing MCP entry removal failed, continuing with re-add."
  }
}

& codex mcp add $ServerName --env "EDGE_CONTROL_PLUGIN_ROOT=$PluginRoot" -- node $McpServerPath

if ($LASTEXITCODE -ne 0) {
  throw "codex mcp add failed."
}

Write-Host "Codex MCP registration completed for $ServerName."
