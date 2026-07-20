; Inno Setup script — MD Editor
; Empaquette la build framework-dependante (publish\framework-dependent\) et vérifie / installe
; les deux prérequis de la machine cible : le runtime .NET 8 Desktop (win-x64) et le runtime
; WebView2 Evergreen (utilisé par le panneau d'aperçu Markdown).
;
; Compilation : & "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\MdEditor.iss
; Prérequis   : avoir d'abord produit publish\framework-dependent\MdEditor.exe
;               (dotnet publish ... --self-contained false -p:PublishSingleFile=true
;                                  -p:EnableCompressionInSingleFile=false)

#define AppName "MD Editor"
#define AppPublisher "Zakaria Hadj"
#define AppExeName "MdEditor.exe"
#define AppExeSource "..\publish\framework-dependent\" + AppExeName
; La version n'est PAS ecrite ici : elle est lue dans le ProductVersion de l'exe publie, lui-meme
; issu de <Version> dans MdEditor.csproj (source de verite unique). Compiler ce script sans avoir
; republie l'exe produirait donc un Setup portant l'ancienne version.
#define AppVersion GetStringFileInfo(AppExeSource, "ProductVersion")
; URL evergreen officiel Microsoft vers le dernier runtime .NET 8 Desktop x64
#define DotNetRuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
; Bootstrapper evergreen officiel Microsoft du runtime WebView2
#define WebView2BootstrapperUrl "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
; Clé EdgeUpdate identifiant le runtime WebView2 Evergreen
#define WebView2ClientKey "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{38A2DDF7-FB09-4D26-A064-9136186D95C9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=Output
OutputBaseFilename=MdEditor-Setup-{#AppVersion}
SetupIconFile=..\MdEditor\Resources\MdEditor.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
LicenseFile=..\LICENSE
; Installation par-utilisateur par défaut (aucun UAC) ; l'utilisateur peut choisir "tous les
; utilisateurs" (Program Files) via la boîte de dialogue d'élévation.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppExeSource}"; DestDir: "{app}"; Flags: ignoreversion
; Fichiers voisins restants (README.md livre comme documentation lisible). Depuis que le publish
; framework-dependant passe -p:IncludeNativeLibrariesForSelfExtract=true, WebView2Loader.dll est
; embarque dans l'exe et n'apparait plus ici ; la ligne reste pour tout fichier voisin futur.
Source: "..\publish\framework-dependent\*"; DestDir: "{app}"; Excludes: "{#AppExeName},*.pdb,*.xml"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

{ Détecte un runtime .NET 8 Desktop (x64) via la présence d'un dossier 8.* sous
  <ProgramFiles>\dotnet\shared\Microsoft.WindowsDesktop.App — méthode la plus fiable,
  indépendante de la présence de dotnet.exe sur le PATH. }
function DotNetDesktop8Installed: Boolean;
var
  FindRec: TFindRec;
  BaseDir: String;
begin
  Result := False;
  BaseDir := GetEnv('ProgramW6432');
  if BaseDir = '' then
    BaseDir := ExpandConstant('{commonpf}');
  BaseDir := BaseDir + '\dotnet\shared\Microsoft.WindowsDesktop.App';
  if FindFirst(BaseDir + '\8.*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ Détecte le runtime WebView2 Evergreen : EdgeUpdate publie un numéro de version dans "pv".
  L'installation machine-wide écrit dans la vue 32 bits de HKLM ; l'installation par-utilisateur
  écrit dans HKCU. Une valeur vide ou "0.0.0.0" signifie "désinstallé". }
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result := False;
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE_32, '{#WebView2ClientKey}', 'pv', Version) then
    if not RegQueryStringValue(HKEY_LOCAL_MACHINE_64, '{#WebView2ClientKey}', 'pv', Version) then
      if not RegQueryStringValue(HKEY_CURRENT_USER, '{#WebView2ClientKey}', 'pv', Version) then
        Exit;
  Result := (Version <> '') and (Version <> '0.0.0.0');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    'Prérequis manquants',
    'Téléchargement des composants Microsoft requis pour exécuter l''application.', nil);
end;

{ Lance un installeur de prérequis déjà téléchargé et signale les codes de retour anormaux.
  0 = succès ; 3010 = succès nécessitant un redémarrage. }
procedure RunPrerequisite(FileName, Params, DisplayName: String);
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{tmp}\' + FileName), Params, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    if (ResultCode <> 0) and (ResultCode <> 3010) then
      SuppressibleMsgBox(
        Format('L''installation de %s a renvoyé le code %d.', [DisplayName, ResultCode]) + #13#10 +
        'L''application pourrait ne pas fonctionner correctement.',
        mbError, MB_OK, IDOK);
  end
  else
    SuppressibleMsgBox(Format('Impossible de lancer l''installeur de %s.', [DisplayName]), mbError, MB_OK, IDOK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  NeedDotNet, NeedWebView2: Boolean;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  NeedDotNet := not DotNetDesktop8Installed;
  NeedWebView2 := not WebView2Installed;
  if not (NeedDotNet or NeedWebView2) then
    Exit;

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add('{#DotNetRuntimeUrl}', 'windowsdesktop-runtime-8-win-x64.exe', '');
  if NeedWebView2 then
    DownloadPage.Add('{#WebView2BootstrapperUrl}', 'MicrosoftEdgeWebview2Setup.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      SuppressibleMsgBox(
        'Le téléchargement des prérequis a échoué (connexion Internet ?).' + #13#10 +
        'Installez-les manuellement depuis :' + #13#10 +
        '  .NET 8 Desktop : https://dotnet.microsoft.com/download/dotnet/8.0/runtime' + #13#10 +
        '  WebView2 : https://developer.microsoft.com/microsoft-edge/webview2/' + #13#10 +
        'puis relancez cette installation.',
        mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  { Installe les prérequis en silencieux (déclenche une invite UAC : les installeurs MS s'élèvent). }
  if NeedDotNet then
    RunPrerequisite('windowsdesktop-runtime-8-win-x64.exe', '/install /quiet /norestart', 'le runtime .NET 8 Desktop');
  if NeedWebView2 then
    RunPrerequisite('MicrosoftEdgeWebview2Setup.exe', '/silent /install', 'le runtime WebView2');
end;
