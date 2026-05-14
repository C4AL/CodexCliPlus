@echo off
setlocal EnableExtensions

chcp 65001 >nul

set "SEARCH_DIR=%~dp0"

:find_repo_root
if exist "%SEARCH_DIR%CodexCliPlus.sln" (
    set "REPO_ROOT=%SEARCH_DIR%"
    goto repo_root_found
)

for %%I in ("%SEARCH_DIR%..") do set "PARENT_DIR=%%~fI\"
if /I "%PARENT_DIR%"=="%SEARCH_DIR%" (
    echo 未找到 CodexCliPlus.sln，无法定位仓库根目录。
    exit /b 1
)
set "SEARCH_DIR=%PARENT_DIR%"
goto find_repo_root

:repo_root_found
pushd "%REPO_ROOT%" >nul 2>nul
if errorlevel 1 (
    echo 无法进入仓库根目录。
    exit /b 1
)

if not exist "src\CodexCliPlus.OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" (
    echo 未找到离线构建器项目。
    popd >nul
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo 未找到 dotnet，请先安装 global.json 指定的 .NET SDK 后重试。
    popd >nul
    exit /b 1
)

dotnet run --project "src\CodexCliPlus.OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" --configuration Release -- %*
set "EXIT_CODE=%ERRORLEVEL%"

popd >nul
exit /b %EXIT_CODE%
