# Build And Validation

## Current workflow

Use the solution build, test project, and native app startup as the active validation path:

```powershell
dotnet build CliProxyApiDesktop.sln
dotnet test tests/CPAD.Tests/CPAD.Tests.csproj
dotnet run --project src/CPAD.App/CPAD.App.csproj
```

## Minimum smoke checks

- The app starts into the native WPF shell.
- Tray icon and tray actions are available.
- The desktop app can start the managed backend.
- `/healthz` responds successfully.
- The automated test suite passes.

## Publishing status

- Legacy PowerShell release scripts are removed.
- Legacy Inno Setup packaging is removed.
- The future release path will move into `CPAD.BuildTool`.
- Installer, portable, and dev packaging remain later-stage work.

## Current expectation

For now, repository acceptance is based on successful build, successful tests, and a working desktop startup smoke run.
