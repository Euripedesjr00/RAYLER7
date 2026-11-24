@echo off
rem Cria a tag da versao e envia ao GitHub; o GitHub Actions gera o instalador e publica a Release.
cd /d "%~dp0.."
if not exist logs mkdir logs
set V=v1.0.0
if not "%~1"=="" set V=%~1
echo === %date% %time% === > logs\versao.txt
git tag -a %V% -m "RAYLER7 %V%" >> logs\versao.txt 2>&1
git push origin %V% >> logs\versao.txt 2>&1
echo push exit=%errorlevel% >> logs\versao.txt
git ls-remote --tags origin >> logs\versao.txt 2>&1
