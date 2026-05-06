set shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command"]

web := "resources/webui/upstream/source"

default:
    just --list

restore:
    dotnet tool restore
    dotnet restore CodexCliPlus.sln --locked-mode
    Push-Location {{web}}; try { npm ci; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

build:
    dotnet build CodexCliPlus.sln --configuration Release --no-restore

test:
    & .\tools\Invoke-Tests.ps1 -Scope Quick -NoBuild

test-full:
    & .\tools\Invoke-Tests.ps1 -Scope Full -NoBuild

test-packaging:
    & .\tools\Invoke-Tests.ps1 -Scope Release -NoBuild

test-smoke:
    & .\tools\Invoke-Tests.ps1 -Scope Smoke -NoBuild

test-live-backend:
    & .\tools\Invoke-Tests.ps1 -Scope LiveBackend -NoBuild

web-lint:
    Push-Location {{web}}; try { npm run lint; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-type-check:
    Push-Location {{web}}; try { npm run type-check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-test:
    Push-Location {{web}}; try { npm run test:fast; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-build:
    Push-Location {{web}}; try { npm run build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-ci:
    just web-lint
    just web-type-check
    just web-test
    just web-build

format-check:
    dotnet csharpier check src tests

coverage:
    & .\tools\Invoke-Tests.ps1 -Scope Coverage

sbom:
    if (-not (Test-Path artifacts/sbom)) { New-Item -ItemType Directory -Path artifacts/sbom | Out-Null }
    dotnet dotnet-CycloneDX CodexCliPlus.sln -o artifacts/sbom -fn codexcliplus.cyclonedx.xml
    syft dir:. -o cyclonedx-json=artifacts/sbom/codexcliplus.syft.cyclonedx.json --exclude ./artifacts --exclude ./resources/webui/upstream/source/node_modules

security:
    actionlint
    gitleaks detect --source . --redact --no-banner --config .gitleaks.toml

vuln-scan:
    trivy fs --skip-dirs artifacts --skip-dirs resources/webui/upstream/source/node_modules --severity HIGH,CRITICAL .
    grype dir:. --exclude ./artifacts --exclude ./resources/webui/upstream/source/node_modules

mutation:
    dotnet stryker --project src/CodexCliPlus.Infrastructure/CodexCliPlus.Infrastructure.csproj --test-project tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj

ci:
    just restore
    just build
    just test
    just web-ci
    just security
