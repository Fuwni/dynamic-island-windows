; Dynamic Island for Windows — installer (Inno Setup 6).
;
;   dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -o publish/DynamicIsland
;   iscc /DAppVer=1.2.1 installer\DynamicIsland.iss          -> dist\DynamicIsland-1.2.1-setup.exe
;
; Per-user install (no admin rights), Start-menu entry, optional desktop icon and autostart,
; a proper entry in Windows "Installed apps", and an uninstaller that also removes the Claude Code
; hooks and autostart, and offers to delete settings, notes and chat logins.

#ifndef AppVer
  #define AppVer "1.2.1"
#endif

[Setup]
AppId={{6D2A9B57-3C41-4E8E-9F0B-5A7C1E2D8B90}
AppName=Dynamic Island
AppVersion={#AppVer}
AppVerName=Dynamic Island {#AppVer}
AppPublisher=Fuwni
AppPublisherURL=https://github.com/Fuwni/dynamic-island-windows
AppSupportURL=https://github.com/Fuwni/dynamic-island-windows/issues
AppUpdatesURL=https://github.com/Fuwni/dynamic-island-windows/releases
VersionInfoVersion={#AppVer}
DefaultDirName={localappdata}\Programs\DynamicIsland
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=DynamicIsland-{#AppVer}-setup
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\DynamicIsland.exe
UninstallDisplayName=Dynamic Island
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no
ShowLanguageDialog=auto
LicenseFile=..\LICENSE

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
en.AutoStart=Start Dynamic Island with Windows
ru.AutoStart=Запускать Dynamic Island вместе с Windows
uk.AutoStart=Запускати Dynamic Island разом із Windows
es.AutoStart=Iniciar Dynamic Island con Windows
de.AutoStart=Dynamic Island mit Windows starten
pt.AutoStart=Iniciar o Dynamic Island com o Windows
en.DeleteData=Also delete your settings, notes and chat logins?
ru.DeleteData=Удалить также настройки, заметки и вход в чаты?
uk.DeleteData=Видалити також налаштування, нотатки та вхід у чати?
es.DeleteData=¿Eliminar también los ajustes, las notas y las sesiones de chat?
de.DeleteData=Auch Einstellungen, Notizen und Chat-Anmeldungen löschen?
pt.DeleteData=Excluir também configurações, notas e logins dos chats?
en.Extras=Extras:
ru.Extras=Дополнительно:
uk.Extras=Додатково:
es.Extras=Extras:
de.Extras=Extras:
pt.Extras=Extras:

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Extras}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:Extras}"; Flags: unchecked

[Files]
Source: "..\publish\DynamicIsland\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Dynamic Island"; Filename: "{app}\DynamicIsland.exe"
Name: "{autodesktop}\Dynamic Island"; Filename: "{app}\DynamicIsland.exe"; Tasks: desktopicon

[Registry]
; same value the app's own "Start with Windows" switch uses, so both stay in sync
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DynamicIsland"; \
  ValueData: """{app}\DynamicIsland.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DynamicIsland.exe"; Description: "{cm:LaunchProgram,Dynamic Island}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; the app removes its Claude Code hooks and autostart entry itself
Filename: "{app}\DynamicIsland.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
{ Close a running island gently (it saves cookies and settings), then make sure it is gone. }
procedure CloseIsland();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM DynamicIsland.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DynamicIsland.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseIsland();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseIsland();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\DynamicIsland'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\DynamicIsland'), True, True, True);
    end;
end;
