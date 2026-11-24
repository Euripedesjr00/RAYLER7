@echo off
rem Abre o RAYLER7 INSTALADO usando a pasta de dados de teste (nao mexe nos dados reais do usuario).
cd /d "%~dp0.."
set RAYLER7_DATA=%CD%\artifacts\dados-teste
start "" "%LOCALAPPDATA%\Programs\RAYLER7\RAYLER7.exe"
