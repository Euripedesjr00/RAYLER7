; Instalador do RAYLER7 (Inno Setup 6). Gerado por scripts\GERAR_INSTALADOR.cmd
#define AppName "RAYLER7"
#define AppVersion "1.1.0"

[Setup]
AppId={{A840A183-D32E-41E8-9426-2799332B4526}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Jhon
AppComments=Criado por Jhon - Desisti nunca foi uma opção!
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
; Instalação por usuário: não pede senha de administrador
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 versão 1809 ou superior
MinVersion=10.0.17763
OutputDir=..\artifacts\instalador
OutputBaseFilename=RAYLER7_Instalador_{#AppVersion}
SetupIconFile=..\src\Rayler7.App\Assets\rayler7.ico
UninstallDisplayIcon={app}\RAYLER7.exe
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\RAYLER7.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\RAYLER7.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RAYLER7.exe"; Description: "Abrir o RAYLER7 agora"; Flags: nowait postinstall skipifsilent

[Messages]
brazilianportuguese.FinishedLabel=O RAYLER7 foi instalado. Seus projetos ficam em %LOCALAPPDATA%\RAYLER7 e são mantidos mesmo se o programa for desinstalado.
