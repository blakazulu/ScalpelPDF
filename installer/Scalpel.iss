; ============================================================================
;  Scalpel — Inno Setup installer
; ----------------------------------------------------------------------------
;  Produces a friendly per-user "Scalpel-<version>-Setup.exe" wizard, as an
;  alternative to the app's built-in self-installer (the bare portable EXE).
;
;  Design notes (why it coexists cleanly with the app's own installer):
;   - Installs PER-USER, no admin (PrivilegesRequired=lowest), to
;     {localappdata}\Programs\Scalpel — the EXACT path the app's self-installer
;     uses (Services/Installer.cs InstallExe). Running from that path makes
;     App.IsPortable() return false, so the app shows no portable badge / no
;     "Install" prompt and simply behaves as an installed app.
;   - The directory page is disabled so the path can't be changed to one that
;     would re-trip the portable detection.
;   - This installer owns the lifecycle: it creates the shortcuts, the optional
;     ".pdf -> Open with Scalpel" registration (mirroring the app's self-
;     installer), and a normal Add/Remove Programs uninstaller. It does NOT set
;     the app's HKCU\Software\Scalpel\Installed flag, so the app never offers
;     its own in-app uninstall that would compete with this one.
;   - Uninstall removes the program files, shortcuts, and the registration it
;     created. User data in {localappdata}\Scalpel (saved signatures, settings,
;     logs) is left in place, the same as a normal app uninstall.
;
;  The produced setup is UNSIGNED, so Windows SmartScreen will warn on first
;  run (More info -> Run anyway) until/unless the EXE and setup are signed.
;
;  Build:  pwsh -File installer\build-installer.ps1
;          (or: ISCC.exe installer\Scalpel.iss)
; ============================================================================

#define AppName        "Scalpel"
#ifndef AppVersion
  #define AppVersion   "2.2.0"
#endif
#define AppPublisher   "Liraz Amir"
#define AppExe         "Scalpel.exe"
#define AppUrl         "https://scalpel-pdf.netlify.app"
; Source EXE (relative to this .iss). Override with /DSourceExe=... if needed.
#ifndef SourceExe
  #define SourceExe    "..\bin\Release\net48\publish\Scalpel.exe"
#endif

[Setup]
; A stable, Scalpel-specific AppId (do not reuse elsewhere). Drives the
; Add/Remove Programs entry and upgrade detection.
AppId={{8F3A1C2E-5B4D-4E6F-9A1B-2C3D4E5F6A7B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
; Stable, version-independent filename so a fixed download URL (website /installer
; redirect) always resolves to the latest. The version still shows in the wizard
; (AppVerName) and in Add/Remove Programs.
OutputBaseFilename=Scalpel-Setup
SetupIconFile=..\Resources\scalpel.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; If Scalpel is running, ask to close it before install/uninstall.
CloseApplications=yes
CloseApplicationsFilter=Scalpel.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "pdfassoc";    Description: "Add Scalpel to the ""Open with"" menu for PDF files"; GroupDescription: "File associations:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Per-user "Open with Scalpel" for .pdf — mirrors Services/Installer.cs.
; (Setting Scalpel as the DEFAULT handler is intentionally left to the user:
;  Windows 10/11 only lets the user change the default in Settings.)
Root: HKCU; Subkey: "Software\Classes\Scalpel.pdf"; ValueType: string; ValueData: "PDF Document"; Flags: uninsdeletekey; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\Scalpel.pdf\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExe},0"; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\Scalpel.pdf\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "Scalpel.pdf"; ValueData: ""; Flags: uninsdeletevalue; Tasks: pdfassoc

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
