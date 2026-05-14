@echo off
setlocal EnableExtensions

chcp 65001 >nul
title CodexCliPlus 离线构建

set "SEARCH_DIR=%~dp0"
set "OUTPUT_FILE="
set "EXIT_FILE="
set "COMMAND_FILE="
set "REPO_PUSHED="
set "RUNNING_PROGRESS_LIMIT=95"
set "RUNNING_PROGRESS_SCALE=2"
set "RUNNING_PROGRESS_STEP_UNITS=12"
set /a "RUNNING_PROGRESS_LIMIT_UNITS=RUNNING_PROGRESS_LIMIT*RUNNING_PROGRESS_SCALE"

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

if not exist "src\CodexCliPlus.OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" (
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

set "RUN_ID=%RANDOM%%RANDOM%%RANDOM%"
set "OUTPUT_FILE=%TEMP%\CodexCliPlus.one_key_build.%RUN_ID%.out"
set "EXIT_FILE=%TEMP%\CodexCliPlus.one_key_build.%RUN_ID%.exit"
set "COMMAND_FILE=%TEMP%\CodexCliPlus.one_key_build.%RUN_ID%.cmd"

> "%COMMAND_FILE%" (
    echo @echo off
    echo chcp 65001 ^>nul
    echo cd /d "%REPO_ROOT%"
    echo dotnet run --project "src\CodexCliPlus.OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" --configuration Release -- %* ^> "%OUTPUT_FILE%" 2^>^&1
    echo ^>"%EXIT_FILE%" echo %%ERRORLEVEL%%
)

call :draw_progress 3 "正在启动离线构建..."
start "" /b cmd.exe /d /c ""%COMMAND_FILE%""
if errorlevel 1 (
    call :fail "启动离线构建失败。" 1
    exit /b 1
)

set "PROGRESS=3"
set /a "PROGRESS_UNITS=PROGRESS*RUNNING_PROGRESS_SCALE"

:progress_loop
if exist "%EXIT_FILE%" goto build_finished

set /a "PROGRESS_UNITS+=RUNNING_PROGRESS_STEP_UNITS"
if %PROGRESS_UNITS% GTR %RUNNING_PROGRESS_LIMIT_UNITS% set "PROGRESS_UNITS=%RUNNING_PROGRESS_LIMIT_UNITS%"
set /a "PROGRESS=PROGRESS_UNITS/RUNNING_PROGRESS_SCALE"
call :draw_progress %PROGRESS% "正在构建离线安装包..."
if exist "%EXIT_FILE%" goto build_finished
ping -n 2 127.0.0.1 >nul
goto progress_loop

:build_finished
set "EXIT_CODE="
set /p EXIT_CODE=<"%EXIT_FILE%"
if not defined EXIT_CODE set "EXIT_CODE=1"

if "%EXIT_CODE%"=="0" (
    call :draw_progress 100 "构建完成，窗口即将关闭。"
    call :cleanup
    if defined REPO_PUSHED popd >nul
    ping -n 3 127.0.0.1 >nul
    exit /b 0
)

cls
echo CodexCliPlus 离线构建失败
echo.
echo 退出码：%EXIT_CODE%
echo.
if exist "%OUTPUT_FILE%" (
    type "%OUTPUT_FILE%"
) else (
    echo 未捕获到构建输出。
)
echo.
call :cleanup
if defined REPO_PUSHED popd >nul
pause
exit /b %EXIT_CODE%

:run_passthrough
dotnet run --project "src\CodexCliPlus.OfflineBuilder\CodexCliPlus.OfflineBuilder.csproj" --configuration Release -- %*
set "EXIT_CODE=%ERRORLEVEL%"
if defined REPO_PUSHED popd >nul
exit /b %EXIT_CODE%

:draw_progress
setlocal EnableDelayedExpansion
set /a "PERCENT=%~1"
set "MESSAGE=%~2"
set /a "FILLED=PERCENT*40/100"
set "BAR="
for /L %%I in (1,1,40) do (
    if %%I LEQ !FILLED! (
        set "BAR=!BAR!#"
    ) else (
        set "BAR=!BAR!-"
    )
)
cls
echo CodexCliPlus 离线构建
echo.
echo !MESSAGE!
echo.
echo [!BAR!] !PERCENT!%%
endlocal
exit /b 0

:cleanup
if defined COMMAND_FILE if exist "%COMMAND_FILE%" del /q "%COMMAND_FILE%" >nul 2>nul
if defined EXIT_FILE if exist "%EXIT_FILE%" del /q "%EXIT_FILE%" >nul 2>nul
if defined OUTPUT_FILE if exist "%OUTPUT_FILE%" del /q "%OUTPUT_FILE%" >nul 2>nul
exit /b 0

:fail
set "FAIL_MESSAGE=%~1"
set "FAIL_CODE=%~2"
if not defined FAIL_CODE set "FAIL_CODE=1"
cls
echo CodexCliPlus 离线构建失败
echo.
echo %FAIL_MESSAGE%
echo.
call :cleanup
if defined REPO_PUSHED popd >nul
pause
exit /b %FAIL_CODE%
