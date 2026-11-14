@echo off
rem Despues de cambiar tarjetas.json: crea las direcciones de cada tarjeta y avisa si hay algo mal escrito.
chcp 65001 >nul
cd /d "%~dp0"
node scripts\generar-tarjetas.js
echo.
pause
