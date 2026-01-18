@echo off
REM Build script for Pin to Deck Stream Deck Plugin
REM This is a legacy wrapper. For full functionality, use dev.ps1 directly.
REM Usage: build.bat [command]
REM Commands: build (default), clean, test, deploy, restart

set COMMAND=%1
if "%COMMAND%"=="" set COMMAND=build

echo Redirecting to dev.ps1 %COMMAND%...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %COMMAND%
