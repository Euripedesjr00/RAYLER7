@echo off
cd /d "%~dp0.."
if not exist logs mkdir logs
if not exist .git git init > logs\git.txt 2>&1
git add -A >> logs\git.txt 2>&1
git commit -F scripts\commit_msg.txt >> logs\git.txt 2>&1
echo exit=%errorlevel% >> logs\git.txt
git log --oneline -3 >> logs\git.txt 2>&1
