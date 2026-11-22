@echo off
cd /d "%~dp0.."
if not exist artifacts mkdir artifacts
git rm --cached samples\credenciais_teste.txt > logs\git_limpeza.txt 2>&1
if exist samples\credenciais_teste.txt move /y samples\credenciais_teste.txt artifacts\credenciais_teste.txt >> logs\git_limpeza.txt 2>&1
git commit --amend --no-edit >> logs\git_limpeza.txt 2>&1
echo exit=%errorlevel% >> logs\git_limpeza.txt
git log --oneline --stat -1 >> logs\git_limpeza.txt 2>&1
git ls-files samples >> logs\git_limpeza.txt 2>&1
