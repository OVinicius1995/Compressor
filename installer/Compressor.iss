#ifndef PackageDir
  #error PackageDir obrigatorio
#endif
#ifndef BuildVersion
  #define BuildVersion "0.2.0"
#endif
#ifndef OutputDir
  #error OutputDir obrigatorio
#endif

[Setup]
AppId={{D56FF312-D70A-4A84-B629-B3C701C840E2}
AppName=Jira Video Compressor
AppVersion={#BuildVersion}
AppPublisher=Compressor
AppPublisherURL=https://github.com/OVinicius1995/Compressor
DefaultDirName={localappdata}\Programs\JiraVideoCompressor
DefaultGroupName=Jira Video Compressor
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern dark
DisableProgramGroupPage=yes
LicenseFile={#PackageDir}\LICENSE
SetupIconFile=..\Assets\roller.ico
UninstallDisplayIcon={app}\JiraVideoCompressor.exe
OutputDir={#OutputDir}
OutputBaseFilename=Compressor-{#BuildVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: checkedonce

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json,Iniciar.cmd"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed.marker"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{localappdata}\JiraVideoCompressor"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\Jira Video Compressor"; Filename: "{app}\JiraVideoCompressor.exe"; Parameters: "--config ""{localappdata}\JiraVideoCompressor\appsettings.json"""; WorkingDir: "{app}"
Name: "{autodesktop}\Jira Video Compressor"; Filename: "{app}\JiraVideoCompressor.exe"; Parameters: "--config ""{localappdata}\JiraVideoCompressor\appsettings.json"""; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\JiraVideoCompressor.exe"; Parameters: "--config ""{localappdata}\JiraVideoCompressor\appsettings.json"""; Description: "Abrir Jira Video Compressor"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsPath, Contents, Documents, Parameters: String;
  RawContents: AnsiString;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  SettingsPath := ExpandConstant('{localappdata}\JiraVideoCompressor\appsettings.json');
  if not FileExists(SettingsPath) then
  begin
    if not LoadStringFromFile(ExpandConstant('{app}\appsettings.example.json'), RawContents) then
      RaiseException('Não foi possível preparar as preferências. Execute o instalador novamente.');
    Contents := UTF8Decode(RawContents);
    Documents := ExpandConstant('{userdocs}\Jira Video Compressor');
    StringChangeEx(Documents, '\', '\\', True);
    StringChangeEx(Contents, '"InputDirectory": "Entrada"', '"InputDirectory": "' + Documents + '\\Entrada"', True);
    StringChangeEx(Contents, '"OutputDirectory": "Prontos para Jira"', '"OutputDirectory": "' + Documents + '\\Prontos para Jira"', True);
    if not SaveStringToFile(SettingsPath, UTF8Encode(Contents), False) then
      RaiseException('Não foi possível salvar as preferências. Confira a permissão da sua pasta pessoal.');
  end;
  WizardForm.StatusLabel.Caption := 'Preparando os recursos de vídeo. Aguarde; a primeira instalação precisa de internet...';
  Parameters := '--prepare-tools --config "' + SettingsPath + '"';
  if not Exec(ExpandConstant('{app}\JiraVideoCompressor.exe'), Parameters, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('Não foi possível iniciar a preparação. Execute o instalador novamente.');
  if ResultCode <> 0 then
    RaiseException('Não foi possível preparar os recursos de vídeo. Confira a internet e execute o instalador novamente. Você também pode abrir o aplicativo pelo atalho e clicar em Tentar preparar novamente. Nenhum vídeo será processado até concluir a preparação.');
end;
