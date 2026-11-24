@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
taskkill /im RAYLER7.exe /f >nul 2>&1
set DN="C:\Program Files\dotnet\dotnet.exe"
if not exist %DN% set DN=dotnet
set ISCC="%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles%\Inno Setup 7\ISCC.exe"
echo === %date% %time% === > logs\instalador.txt
if exist artifacts\publish rmdir /s /q artifacts\publish
if not exist src\Rayler7.Infrastructure\tessdata\por.traineddata powershell -NoProfile -Command "New-Item -ItemType Directory -Force src\Rayler7.Infrastructure\tessdata | Out-Null; Invoke-WebRequest -UseBasicParsing -Uri https://github.com/tesseract-ocr/tessdata_fast/raw/main/por.traineddata -OutFile src\Rayler7.Infrastructure\tessdata\por.traineddata" >> logs\instalador.txt 2>&1
rem Publicacao autocontida: o usuario final nao precisa instalar o .NET
%DN% publish src\Rayler7.App\Rayler7.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish -p:DebugType=none >> logs\instalador.txt 2>&1
echo publish exit=%errorlevel% >> logs\instalador.txt
if not exist artifacts\publish\tessdata\por.traineddata (echo ERRO: tessdata ausente na publicacao >> logs\instalador.txt & exit /b 1)
%ISCC% installer\RAYLER7.iss >> logs\instalador.txt 2>&1
set R=%errorlevel%
echo iscc exit=%R% >> logs\instalador.txt
dir /b artifacts\instalador >> logs\instalador.txt 2>&1
exit /b %R%
