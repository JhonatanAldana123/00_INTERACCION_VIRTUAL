@echo off
rem Arrastra un .glb exportado desde Rhino encima de este archivo para optimizarlo.
if "%~1"=="" (
  echo Arrastra un archivo .glb encima de optimizar.bat
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\optimizar-modelo.ps1" "%~1"
pause
