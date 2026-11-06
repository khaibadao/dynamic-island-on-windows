; Installer for Dynamic Island Notifier, built by GitHub Actions for each release:
;   iscc /DAppVersion=2.4.0 /DArch=x64 installer.iss
; Installs for the current user only (no admin), in the same folder as the
; PowerShell install command, so the two can be mixed. Uninstalling first runs
; the app with --uninstall, which puts Windows' notification banners back and
; stops it starting with Windows.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6E1B8F3A-5C2D-4B9E-9A71-D1A5F0C3B2E4}
AppName=Dynamic Island Notifier
AppVersion={#AppVersion}
AppPublisher=khaibadao (fork of sadeeshasathsara/dynamic-island-on-windows)
AppPublisherURL=https://github.com/khaibadao/dynamic-island-on-windows
DefaultDirName={autopf}\DynamicIsland
DisableDirPage=yes
DefaultGroupName=Dynamic Island Notifier
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DynamicIslandNotifier.exe
UninstallDisplayName=Dynamic Island Notifier
Compression=lzma2
SolidCompression=yes
OutputDir=release
OutputBaseFilename=DynamicIslandSetup-{#Arch}
PrivilegesRequired=lowest
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
AppMutex=Local\DynamicIslandNotifier.SingleInstance
SetupIconFile=Assets\island.ico
WizardStyle=modern

[Files]
Source: "out\{#Arch}\DynamicIslandNotifier.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Dynamic Island Notifier"; Filename: "{app}\DynamicIslandNotifier.exe"
Name: "{autodesktop}\Dynamic Island Notifier"; Filename: "{app}\DynamicIslandNotifier.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\DynamicIslandNotifier.exe"; Description: "{cm:LaunchProgram,Dynamic Island Notifier}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\DynamicIslandNotifier.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreWindows"
