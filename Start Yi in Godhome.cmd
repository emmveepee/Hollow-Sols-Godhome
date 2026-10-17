@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Start-HollowSols.ps1" %*
if errorlevel 1 (
    echo Hollow Sols could not launch. Review the error above.
    pause
)
