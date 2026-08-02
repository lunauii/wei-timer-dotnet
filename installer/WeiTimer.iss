; Wei Timer -- Inno Setup script.
;
; Builds a per-user Setup Wizard installer from the app's `dotnet publish`
; output. Must be compiled with Inno Setup 6 (iscc.exe), which -- like the
; app itself -- only runs on Windows.
;
; Prerequisite (run before compiling this script):
;   dotnet publish WeiTimer\WeiTimer.csproj -c Release -r win-x64 ^
;     --self-contained true -p:PublishSingleFile=true ^
;     -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -p:EnableCompressionInSingleFile=true
;
; Compile:
;   iscc installer\WeiTimer.iss
;
; Output:
;   installer\Output\WeiTimerSetup-{#MyAppVersion}.exe

#define MyAppName "Wei Timer"
; Default used for local/manual builds; CI overrides this from the pushed git
; tag via `iscc /DMyAppVersion=x.y.z installer\WeiTimer.iss` (see
; .github/workflows/release.yml), so keep this ifndef guard rather than a
; plain #define or the command-line override would be silently redefined.
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "lunaui"
#define MyAppExeName "WeiTimer.exe"
#define MyPublishDir "..\WeiTimer\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Generated once and must never change -- Inno Setup uses this to recognize
; upgrades/uninstalls of the same product across versions.
AppId={{8B029A2F-0A77-4BD4-ADE0-D90DB710C19A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\WeiTimer
DefaultGroupName={#MyAppName}
; Per-user install -- no admin resources are needed, and this avoids a UAC
; prompt entirely, which matters for first-run trust as much as signing does.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DisableProgramGroupPage=yes
SetupIconFile=..\WeiTimer\Assets\wei-timer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=WeiTimerSetup-{#MyAppVersion}
WizardStyle=modern
; No code-signing step here yet -- see CLAUDE.md's Installer section for
; where a signtool/Trusted Signing step would slot into the build once real
; signing credentials exist.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Launch {#MyAppName} automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WeiTimer"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
