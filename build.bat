@echo off
rem Double-click to build. Runs build.ps1 (tests + publish). Add -SkipTests to skip tests.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
