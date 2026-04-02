@echo off
REM Stardew Valley Dedicated Server - Windows Launcher
REM Launches SMAPI with the dedicated server mod

setlocal

set SCRIPT_DIR=%~dp0
set GAME_DIR=%~dp0..

REM Allow override via argument
if not "%~1"=="" set GAME_DIR=%~1

echo [Launcher] Stardew Valley Dedicated Server
echo [Launcher] Game directory: %GAME_DIR%

REM Check if SMAPI exists
if not exist "%GAME_DIR%\StardewModdingAPI.exe" (
    echo [Launcher] ERROR: StardewModdingAPI.exe not found in %GAME_DIR%
    echo [Launcher] Please install SMAPI first.
    exit /b 1
)

echo [Launcher] Launching StardewModdingAPI...

cd /d "%GAME_DIR%"
StardewModdingAPI.exe
