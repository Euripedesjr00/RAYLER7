@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
taskkill /im git-credential-manager.exe /f >nul 2>&1
taskkill /im git-remote-https.exe /f >nul 2>&1
git add -A > logs\push.txt 2>&1
git diff --cached --quiet || git commit -F scripts\commit_msg.txt >> logs\push.txt 2>&1
git remote get-url origin >nul 2>&1 || git remote add origin https://github.com/Euripedesjr00/RAYLER7.git
git branch -M main >> logs\push.txt 2>&1
rem Login por codigo de dispositivo: o codigo aparece em logs\push.txt
git -c credential.guiPrompt=false -c credential.gitHubAuthModes=device push -u origin main >> logs\push.txt 2>&1
echo push exit=%errorlevel% >> logs\push.txt
