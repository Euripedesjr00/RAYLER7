@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
taskkill /im RAYLER7.exe /f >nul 2>&1
set DN="C:\Program Files\dotnet\dotnet.exe"
set ISCC="%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
echo === %date% %time% === > logs\instalador.txt
if exist artifacts\publish rmdir /s /q artifacts\publish
rem Publicacao autocontida: o usuario final nao precisa instalar o .NET
%DN% publish src\Rayler7.App\Rayler7.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish -p:DebugType=none >> logs\instalador.txt 2>&1
echo publish exit=%errorlevel% >> logs\instalador.txt
if not exist artifacts\publish\tessdata\por.traineddata echo AVISO: tessdata ausente na publicacao >> logs\instalador.txt
%ISCC% installer\RAYLER7.iss >> logs\instalador.txt 2>&1
echo iscc exit=%errorlevel% >> logs\instalador.txt
dir /b artifacts\instalador >> logs\instalador.txt 2>&1
