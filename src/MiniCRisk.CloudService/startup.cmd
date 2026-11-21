@echo off
REM Cloud Services startup task (runs elevated, as SYSTEM, on every role start/recycle).
REM Installs R and the Plumber scoring service. On AWS this logic moves into a custom AMI / user data / container image.
powershell -ExecutionPolicy Unrestricted -NoProfile -File "%~dp0startup.ps1" >> "%TEMP%\startup.log" 2>&1
exit /b 0
