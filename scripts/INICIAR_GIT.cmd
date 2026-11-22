@echo off
cd /d "%~dp0.."
if exist .git (echo repositorio ja existe > logs\git.txt & exit /b 0)
if not exist logs mkdir logs
git init > logs\git.txt 2>&1
git add -A >> logs\git.txt 2>&1
git commit -m "Fundacao do RAYLER7: nucleo de preservacao, selecao, duvidas, planilhas e login" >> logs\git.txt 2>&1
echo exit=%errorlevel% >> logs\git.txt
