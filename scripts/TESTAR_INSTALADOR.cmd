@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist logs mkdir logs
set L=logs\teste_instalacao.txt
set SETUP=artifacts\instalador\RAYLER7_Instalador_1.0.0.exe
set APPDIR=%LOCALAPPDATA%\Programs\RAYLER7
set PSX=powershell -NoProfile -Command
echo === %date% %time% === > %L%
taskkill /im RAYLER7.exe /f >nul 2>&1
if exist "%LOCALAPPDATA%\RAYLER7" (echo dados_do_usuario_antes=existe >> %L%) else (echo dados_do_usuario_antes=nao_existe >> %L%)

echo --- 1. instalacao silenciosa >> %L%
"%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="desktopicon" /LOG="logs\inno_instalar.log"
echo instalar exit=%errorlevel% >> %L%
if exist "%APPDIR%\RAYLER7.exe" (echo exe=ok >> %L%) else (echo exe=AUSENTE >> %L%)
if exist "%APPDIR%\tessdata\por.traineddata" (echo tessdata=ok >> %L%) else (echo tessdata=AUSENTE >> %L%)
%PSX% "'menu_iniciar=' + (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'RAYLER7.lnk'))" >> %L%
%PSX% "'area_de_trabalho=' + (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'RAYLER7.lnk'))" >> %L%
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "RAYLER7" /d >nul 2>&1 && (echo painel_de_controle=ok >> %L%) || (echo painel_de_controle=AUSENTE >> %L%)

echo --- 2. o programa abre sozinho (sem .NET instalado a parte) >> %L%
start "" "%APPDIR%\RAYLER7.exe"
ping -n 9 127.0.0.1 >nul
tasklist /fi "imagename eq RAYLER7.exe" | find /i "RAYLER7.exe" >nul && (echo abriu=ok >> %L%) || (echo abriu=FALHOU >> %L%)
taskkill /im RAYLER7.exe /f >nul 2>&1
ping -n 3 127.0.0.1 >nul

echo --- 3. desinstalacao >> %L%
dir /b "%APPDIR%\unins*" >> %L% 2>&1
"%APPDIR%\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="%CD%\logs\inno_desinstalar.log"
echo desinstalar exit=%errorlevel% >> %L%
set /a N=0
:espera
ping -n 3 127.0.0.1 >nul
set /a N+=1
if exist "%APPDIR%\RAYLER7.exe" if %N% lss 30 goto espera
echo esperou=%N%x2s >> %L%
if exist "%APPDIR%\RAYLER7.exe" (echo removido=NAO >> %L%) else (echo removido=ok >> %L%)
%PSX% "'atalho_removido=' + -not (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'RAYLER7.lnk'))" >> %L%
if exist "%LOCALAPPDATA%\RAYLER7" (echo dados_do_usuario_depois=mantidos >> %L%) else (echo dados_do_usuario_depois=nao_existem >> %L%)

echo --- 4. reinstalacao (deixa o programa instalado) >> %L%
"%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="desktopicon" /LOG="logs\inno_reinstalar.log"
echo reinstalar exit=%errorlevel% >> %L%
if exist "%APPDIR%\RAYLER7.exe" (echo exe=ok >> %L%) else (echo exe=AUSENTE >> %L%)
echo FIM >> %L%
