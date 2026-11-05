@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0WifiFallback.ps1" %*
exit /b %ERRORLEVEL%
