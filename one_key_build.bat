@echo off
setlocal EnableExtensions

chcp 65001 >nul
title CodexCliPlus 离线构建

set "SEARCH_DIR=%~dp0"
set "REPO_PUSHED="

:find_repo_root
if exist "%SEARCH_DIR%CodexCliPlus.sln" (
    set "REPO_ROOT=%SEARCH_DIR%"
    goto repo_root_found
)

for %%I in ("%SEARCH_DIR%..") do set "PARENT_DIR=%%~fI\"
if /I "%PARENT_DIR%"=="%SEARCH_DIR%" (
    call :fail "未找到 CodexCliPlus.sln，无法定位仓库根目录。" 1
    exit /b 1
)
set "SEARCH_DIR=%PARENT_DIR%"
goto find_repo_root

:repo_root_found
pushd "%REPO_ROOT%" >nul 2>nul
if errorlevel 1 (
    call :fail "无法进入仓库根目录。" 1
    exit /b 1
)
set "REPO_PUSHED=1"

if not exist "src\BuildPipeline\OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" (
    call :fail "未找到离线构建器项目。" 1
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    call :fail "未找到 dotnet，请先安装 global.json 指定的 .NET SDK 后重试。" 1
    exit /b 1
)

if /I "%~1"=="--help" goto run_passthrough
if /I "%~1"=="-h" goto run_passthrough
if /I "%~1"=="help" goto run_passthrough
if "%~1"=="/?" goto run_passthrough

echo CodexCliPlus 离线构建
echo.
echo 正在构建离线安装包...
echo.
dotnet run --project "src\BuildPipeline\OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" --configuration Release -- %*
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
    echo.
    echo 构建完成，窗口即将关闭。
    if defined REPO_PUSHED popd >nul
    ping -n 3 127.0.0.1 >nul
    exit /b 0
)

echo.
echo CodexCliPlus 离线构建失败
echo.
echo 退出码：%EXIT_CODE%
echo.
if defined REPO_PUSHED popd >nul
pause
exit /b %EXIT_CODE%

:run_passthrough
dotnet run --project "src\BuildPipeline\OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" --configuration Release -- %*
set "EXIT_CODE=%ERRORLEVEL%"
if defined REPO_PUSHED popd >nul
exit /b %EXIT_CODE%

:fail
set "FAIL_MESSAGE=%~1"
set "FAIL_CODE=%~2"
if not defined FAIL_CODE set "FAIL_CODE=1"
cls
echo CodexCliPlus 离线构建失败
echo.
echo %FAIL_MESSAGE%
echo.
if defined REPO_PUSHED popd >nul
pause
exit /b %FAIL_CODE%
