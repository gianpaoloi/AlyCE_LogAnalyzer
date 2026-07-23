; AlyCE Log Analyzer - Inno Setup Script
; Compile with: iscc setup.iss
; Or override version: iscc /DMyAppVersion=1.2.3 setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName      "AlyCE Log Analyzer"
#define MyAppPublisher "TeamSystem"
#define MyAppURL       "https://github.com/g-iannetta_TSGC24/AlyCE_LogAnalyzer"
#define MyAppExeName   "LogAnalyzer.Maui.exe"
#define MyAppId        "com.teamsystem.alyce.loganalyzer"

; Source directory: output of `dotnet publish -c Release -f net10.0-windows10.0.19041.0 --runtime win-x64 --self-contained`
; This path is relative to the location of this .iss file (LogAnalyzer.Maui/installer/)
#define PublishDir "..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\TeamSystem\AlyCE Log Analyzer
DefaultGroupName=TeamSystem\AlyCE Log Analyzer
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=AlyCE-LogAnalyzer-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Per-user install — no UAC / admin rights required
PrivilegesRequired=lowest
; Windows-only, x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Minimum: Windows 10 1809 (matches MAUI project SupportedOSPlatformVersion)
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All self-contained publish output (runtime, native libs, assets)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";                            Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                      Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent
