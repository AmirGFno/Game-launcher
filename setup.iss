; ============================================================================
;  GFno Launcher - installer script (Inno Setup 6 / 7)
;  Compiled automatically by .github/workflows/release.yml after publishing.
;
;  Run ISCC.exe on this file from the repository root, e.g.:
;      "C:\Program Files\Inno Setup 7\ISCC.exe" setup.iss
;  Relative source paths below are resolved against this script's directory.
; ============================================================================

#define MyAppName     "GFno Launcher"
#define MyAppVersion  "1.1.0"
#define MyAppExeName  "GFnoLauncher.exe"

[Setup]
; Fixed Id so upgrades keep their uninstall registration.
AppId={{8F3C2A1E-5B7D-4C9A-9E2F-1A2B3C4D5E6F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=GFno

; Install for all users - the launcher clears %TEMP% and C:\Windows\Temp,
; so it must run elevated.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no

; Relative paths are resolved against this script's directory, so the compiled
; installer lands next to setup.iss regardless of the caller's working directory.
OutputDir=.
OutputBaseFilename=GameLauncherSetup

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; 64-bit installer / 64-bit install location.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

SetupIconFile=Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Files]
; The published self-contained single-file executable.
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
