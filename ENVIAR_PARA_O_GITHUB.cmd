@echo off
chcp 65001 >nul
title RAYLER7 - Enviar para o GitHub
cd /d "%~dp0"
echo.
echo  ===========================================================
echo   RAYLER7 - Enviar o codigo para github.com/Euripedesjr00/RAYLER7
echo  ===========================================================
echo.
echo  [1/3] Encerrando tentativas anteriores que ficaram travadas...
taskkill /im git-credential-manager.exe /f >nul 2>&1
taskkill /im git-remote-https.exe /f >nul 2>&1
echo.
echo  [2/3] Preparando o envio...
git add -A >nul 2>&1
git diff --cached --quiet || git commit -F scripts\commit_msg.txt >nul 2>&1
git remote get-url origin >nul 2>&1 || git remote add origin https://github.com/Euripedesjr00/RAYLER7.git
git branch -M main >nul 2>&1
echo.
echo  [3/3] Enviando. Vai abrir uma janela "Connect to GitHub":
echo        clique em "Sign in with your browser" e depois em "Authorize".
echo.
git push -u origin main
if errorlevel 1 (
  echo.
  echo  XXX  O envio NAO foi concluido. Feche esta janela e tente de novo.
) else (
  echo.
  echo  OK - Codigo enviado com sucesso!
  echo  Veja em: https://github.com/Euripedesjr00/RAYLER7
)
echo.
pause
