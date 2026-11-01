; SleepGuard 安装包脚本（Inno Setup 6）
;
; 本地构建：
;   dotnet publish src\SleepGuard\SleepGuard.csproj -c Release -r win-x64 --self-contained true -o dist\publish
;   iscc installer\SleepGuard.iss /DAppVersion=1.0.0
;
; CI 构建见 .github/workflows/release.yml。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "SleepGuard"
#define AppPublisher "dameng324"
#define AppUrl "https://github.com/dameng324/SleepGuard"
#define AppExeName "SleepGuard.exe"
#define ProjectRoot ".."

[Setup]
AppId={{7F2C4A16-9F4B-4B7E-9C1D-5A3E8B6C0D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

; 程序安装在 Program Files，安装器本身也需要管理员权限
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#ProjectRoot}\dist\installer
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile={#ProjectRoot}\src\SleepGuard\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; dotnet publish 的输出目录（自包含，用户无需另装 .NET 运行时）
Source: "{#ProjectRoot}\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; 安装器以管理员身份运行，直接启动即可让程序也以管理员身份打开
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
