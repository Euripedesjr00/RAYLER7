@echo off
rem Abre o RAYLER7 com dados numa pasta de TESTE (nao mexe nos seus projetos reais).
set RAYLER7_DATA=%~dp0..\artifacts\dados-teste
start "" "%~dp0..\src\Rayler7.App\bin\Release\net10.0-windows\RAYLER7.exe"
