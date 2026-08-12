#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{A13A3C4E-CB81-43B0-86A3-61D7D23F5280}
AppName=QuickSearch
AppVersion={#AppVersion}
AppPublisher=QuickSearch
DefaultDirName={autopf}\QuickSearch
DefaultGroupName=QuickSearch
OutputDir={#OutputDir}
OutputBaseFilename=QuickSearch-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no
UninstallDisplayName=QuickSearch
UninstallDisplayIcon={app}\QuickSearch.exe
SetupIconFile=..\src\QuickSearch.Windows\Assets\QuickSearch.ico
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "dependencies\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\dependencies\Everything-Setup.exe"; DestDir: "{app}\dependencies"; Flags: ignoreversion
Source: "{#PublishDir}\dependencies\Everything-Setup.sha256"; DestDir: "{app}\dependencies"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\QuickSearch"; Filename: "{app}\QuickSearch.exe"
Name: "{autodesktop}\QuickSearch"; Filename: "{app}\QuickSearch.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\QuickSearch.exe"; Description: "启动 QuickSearch"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'QuickSearch');
end;
