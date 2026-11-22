@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set DN="C:\Program Files\dotnet\dotnet.exe"
echo === %date% %time% === > logs\build.txt
%DN% --version >> logs\build.txt 2>&1
echo Restaurando pacotes...
%DN% restore RAYLER7.slnx >> logs\build.txt 2>&1
echo restore exit=%errorlevel% >> logs\build.txt
echo Compilando...
%DN% build RAYLER7.slnx -c Release --no-restore -clp:ErrorsOnly >> logs\build.txt 2>&1
echo build exit=%errorlevel% >> logs\build.txt
echo Testando...
%DN% test RAYLER7.slnx -c Release --no-build --logger "console;verbosity=normal" > logs\test.txt 2>&1
echo test exit=%errorlevel% >> logs\test.txt
echo Concluido.
timeout /t 3 >nul
