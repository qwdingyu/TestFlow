@echo off
setlocal

REM 用法：双击或传入根目录，例如：check_emoji.bat D:\repo
set "ROOT=%~1"
if "%ROOT%"=="" set "ROOT=%CD%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check_emoji.ps1" "%ROOT%"
exit /b %ERRORLEVEL%
