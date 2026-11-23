@echo off
cd /d "%~dp0.."
if not exist logs mkdir logs
echo === %date% %time% === > logs\github.txt
git add -A >> logs\github.txt 2>&1
git commit -F scripts\commit_msg.txt >> logs\github.txt 2>&1
git remote get-url origin >nul 2>&1 || git remote add origin https://github.com/Euripedesjr00/RAYLER7.git
git branch -M main >> logs\github.txt 2>&1
echo Enviando para o GitHub (se abrir uma janela de login do GitHub, autorize)...
git push -u origin main >> logs\github.txt 2>&1
echo push exit=%errorlevel% >> logs\github.txt
git log --oneline -5 >> logs\github.txt 2>&1
timeout /t 3 >nul
