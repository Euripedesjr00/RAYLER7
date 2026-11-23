@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
rem fecha uma copia aberta do programa para liberar o executavel
taskkill /im RAYLER7.exe /f >nul 2>&1
set DN="C:\Program Files\dotnet\dotnet.exe"
echo === %date% %time% === > logs\build.txt
%DN% --version >> logs\build.txt 2>&1
rem Dados de idioma do Tesseract (portugues), baixados uma unica vez do repositorio oficial
if not exist src\Rayler7.Infrastructure\tessdata\por.traineddata (
  echo Baixando dados de idioma do Tesseract...
  powershell -NoProfile -Command "New-Item -ItemType Directory -Force src\Rayler7.Infrastructure\tessdata | Out-Null; Invoke-WebRequest -UseBasicParsing -Uri https://github.com/tesseract-ocr/tessdata_fast/raw/main/por.traineddata -OutFile src\Rayler7.Infrastructure\tessdata\por.traineddata" >> logs\build.txt 2>&1
)
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
