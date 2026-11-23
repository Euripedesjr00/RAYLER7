@echo off
cd /d "%~dp0.."
if not exist logs mkdir logs
echo === %date% %time% === > logs\publicar.txt
git add -A >> logs\publicar.txt 2>&1
git diff --cached --quiet || git commit -F scripts\commit_msg.txt >> logs\publicar.txt 2>&1
git push origin main >> logs\publicar.txt 2>&1
echo push exit=%errorlevel% >> logs\publicar.txt
git log --oneline -3 >> logs\publicar.txt 2>&1
