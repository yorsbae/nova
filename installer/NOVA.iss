; Inno Setup 6 script. Build with: iscc installer\NOVA.iss  (run build\publish.ps1 first)
#define AppVersion "2.0.0"

[Setup]
AppId={{8F3C2B7A-5D1E-4B6A-9C20-4E7A1F0D3B55}
AppName=NOVA
AppVersion={#AppVersion}
AppPublisher=NOVA
DefaultDirName={autopf}\NOVA
DefaultGroupName=NOVA
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=NOVA-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Nova.SingleInstance.v1
UninstallDisplayName=NOVA
UninstallDisplayIcon={app}\NOVA.exe

[Tasks]
Name: "startup"; Description: "Start NOVA when I sign in to Windows"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NOVA"; Filename: "{app}\NOVA.exe"
Name: "{group}\NOVA (safe mode)"; Filename: "{app}\NOVA.exe"; Parameters: "--safe-mode"
Name: "{group}\Uninstall NOVA"; Filename: "{uninstallexe}"

[Registry]
; Created only if the task is ticked; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NOVA"; ValueData: """{app}\NOVA.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue
; Also remove a Run value the app itself created through its Settings toggle.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NOVA"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\NOVA.exe"; Description: "Launch NOVA"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Also delete NOVA settings and logs from this PC?', mbConfirmation, MB_YESNO or MB_DEFDEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\NOVA'), True, True, True);
  end;
end;
