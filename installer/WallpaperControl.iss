; Build through Build-Installer.ps1 so the version always comes from the project.
#ifndef AppVersion
  #error AppVersion must be provided by the build script.
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained win-x64 publish output.
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
; Overrides allow installation tests to use isolated registry keys and shortcuts.
#ifndef InstallerAppId
  #define InstallerAppId "WallpaperControl"
#endif
#ifndef ProductName
  #define ProductName "Wallpaper Control"
#endif
#ifndef StartupKey
  #define StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif

[Setup]
AppId={#InstallerAppId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=Yasmin Mahr
AppPublisherURL=https://github.com/yasmin78-gif/WallpaperControl
AppSupportURL=https://github.com/yasmin78-gif/WallpaperControl/issues
AppUpdatesURL=https://github.com/yasmin78-gif/WallpaperControl/releases
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\WallpaperControl\WallpaperControl.ico
UninstallDisplayIcon={app}\WallpaperControl.exe
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=WallpaperControl-{#AppVersion}-Setup-x64
VersionInfoVersion={#AppVersion}.0
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\WallpaperControl.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\WallpaperControl.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WallpaperControl.exe"; Description: "{cm:LaunchProgram,{#ProductName}}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  RunKey = '{#StartupKey}';
  RunValue = 'WallpaperControl';

// Returns the quoted command used by the application's existing autostart setting.
function InstalledStartupCommand(): String;
begin
  Result := '"' + ExpandConstant('{app}\WallpaperControl.exe') + '" --tray';
end;

// Preserve enabled autostart while migrating an older portable executable path.
// No entry is created for users who have not enabled autostart in the application.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingCommand: String;
begin
  if CurStep = ssPostInstall then
    if RegQueryStringValue(HKCU, RunKey, RunValue, ExistingCommand) then
      if Trim(ExistingCommand) <> '' then
        if not RegWriteStringValue(HKCU, RunKey, RunValue, InstalledStartupCommand()) then
          Log('Could not update the existing WallpaperControl autostart entry.');
end;

// Remove only autostart owned by this installation. Keep user settings, statistics,
// wallpaper files, and entries that have since been redirected to another copy.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, RunValue, ExistingCommand) then
      if CompareText(Trim(ExistingCommand), InstalledStartupCommand()) = 0 then
        RegDeleteValue(HKCU, RunKey, RunValue);
end;
