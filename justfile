set shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command"]

web := "resources/webui/upstream/source"

default:
    just --list

restore:
    dotnet tool restore
    dotnet restore CodexCliPlus.sln
    Push-Location {{web}}; try { npm ci; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

build:
    dotnet build CodexCliPlus.sln --configuration Release --no-restore

test:
    dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --no-build --verbosity normal

test-ui:
    dotnet test tests/CodexCliPlus.UiTests/CodexCliPlus.UiTests.csproj --configuration Release --verbosity normal

web-lint:
    Push-Location {{web}}; try { npm run lint; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-type-check:
    Push-Location {{web}}; try { npm run type-check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

web-test:
    Push-Location {{web}}; try { npm run test; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } } finally { Pop-Location }

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
    dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/test-results
    reportgenerator -reports:"artifacts/test-results/**/coverage.cobertura.xml" -targetdir:"artifacts/coverage" -reporttypes:"Html;TextSummary"

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
