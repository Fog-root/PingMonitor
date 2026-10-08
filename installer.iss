; =====================================================================
; Inno Setup Script for Ping Monitoring (DotaPingMonitor)
; Production-grade Windows Installer with Auto-Download of .NET 8 Runtime
; =====================================================================

#define MyAppName "Ping Monitoring"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Ping Monitoring"
#define MyAppExeName "PingMonitoring.exe"
#define MyAppAssocName MyAppName + " File"
#define MyAppAssocExt ".pmon"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

[Setup]
; Уникальный GUID приложения для идентификации обновлений и деинсталляции
AppId={{5C170E32-0A5D-4F1E-8B7C-4C919D429F2B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Требуются права администратора для установки в Program Files и установки .NET 8 Runtime
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir=installer_output
OutputBaseFilename=PingMonitoring_Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100
CloseApplications=yes
CloseApplicationsFilter=PingMonitoring.exe,DotaPingMonitor.exe
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.DotNetDownloadTitle=Загрузка необходимых компонентов
ru.DotNetDownloadPrompt=Загрузка Microsoft .NET 8 Desktop Runtime (x64)...
ru.DotNetInstallingPrompt=Установка Microsoft .NET 8 Desktop Runtime...
ru.DotNetDownloadError=Не удалось загрузить Microsoft .NET 8 Desktop Runtime. Проверьте подключение к интернету и повторите попытку.
ru.DotNetInstallError=Ошибка при установке Microsoft .NET 8 Desktop Runtime. Код ошибки: 
en.DotNetDownloadTitle=Downloading Prerequisites
en.DotNetDownloadPrompt=Downloading Microsoft .NET 8 Desktop Runtime (x64)...
en.DotNetInstallingPrompt=Installing Microsoft .NET 8 Desktop Runtime...
en.DotNetDownloadError=Failed to download Microsoft .NET 8 Desktop Runtime. Please check your internet connection and try again.
en.DotNetInstallError=Error installing Microsoft .NET 8 Desktop Runtime. Exit code: 

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Запускать Ping Monitoring при старте Windows"; GroupDescription: "Автозагрузка:"; Flags: unchecked; Languages: ru
Name: "startupicon"; Description: "Start Ping Monitoring automatically with Windows"; GroupDescription: "Startup:"; Flags: unchecked; Languages: en

[Files]
; Основные исполняемые файлы и библиотеки приложения из чистой папки publish
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Ярлык в меню «Пуск»
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0; WorkingDir: "{app}"
; Ярлык на рабочем столе
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0; WorkingDir: "{app}"; Tasks: desktopicon
; Ярлык в автозагрузке Windows (если выбран таск)
Name: "{autostartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0; WorkingDir: "{app}"; Tasks: startupicon

[Registry]
; Регистрация в автозагрузке Windows (Диспетчер задач -> вкладка Автозагрузка)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ping Monitoring"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Tasks: startupicon; Flags: uninsdeletevalue

[Run]
; Предложение запустить приложение сразу после завершения инсталляции
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Удаление устаревших и конфликтующих ярлыков с предыдущих версий
Type: files; Name: "{userdesktop}\DotaPingMonitor.lnk"
Type: files; Name: "{commondesktop}\DotaPingMonitor.lnk"
Type: files; Name: "{userprograms}\DotaPingMonitor.lnk"
Type: files; Name: "{commonprograms}\DotaPingMonitor.lnk"

[UninstallDelete]
; Очистка временных файлов и кэша при деинсталляции
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\ping.db-shm"
Type: files; Name: "{app}\ping.db-wal"
Type: files; Name: "{userdesktop}\{#MyAppName}.lnk"

[Code]
var
  DownloadPage: TDownloadWizardPage;

// Проверка наличия установленного Microsoft .NET 8 Desktop Runtime (x64)
function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
  DotNetDir: String;
  Ver: String;
begin
  Result := False;

  // 1. Проверяем стандартный каталог 64-битного .NET Desktop Runtime
  DotNetDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(DotNetDir) then
    DotNetDir := 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App';

  if DirExists(DotNetDir) then
  begin
    // Ищем любую поддиректорию версии 8.*
    if FindFirst(DotNetDir + '\8.*', FindRec) then
    begin
      try
        Result := True;
        Exit;
      finally
        FindClose(FindRec);
      end;
    end;
  end;

  // 2. Проверяем через системный реестр 64-бит
  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', Ver) or
     RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', Ver) then
  begin
    if (Length(Ver) >= 2) and (Copy(Ver, 1, 2) = '8.') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure InitializeWizard();
begin
  // Страница отображения загрузки внешних пакетов
  DownloadPage := CreateDownloadPage(
    CustomMessage('DotNetDownloadTitle'),
    CustomMessage('DotNetDownloadPrompt'),
    nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  InstallerPath: String;
  ErrorText: String;
begin
  Result := True;

  // Проверка и докачка рантайма на шаге "Готово к установке"
  if CurPageID = wpReady then
  begin
    if not IsDotNet8DesktopInstalled() then
    begin
      DownloadPage.Clear;
      DownloadPage.Add(
        'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
        'windowsdesktop-runtime-8.0-win-x64.exe',
        '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          if DownloadPage.AbortedByUser then
            Log('Download aborted by user.')
          else
          begin
            ErrorText := CustomMessage('DotNetDownloadError') + #13#10#13#10 + GetExceptionMessage;
            SuppressibleMsgBox(ErrorText, mbCriticalError, MB_OK, IDOK);
          end;
          Result := False;
          Exit;
        end;
      finally
        DownloadPage.Hide;
      end;

      // Установка загруженного Microsoft .NET 8 Desktop Runtime
      InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-8.0-win-x64.exe');
      if FileExists(InstallerPath) then
      begin
        WizardForm.StatusLabel.Caption := CustomMessage('DotNetInstallingPrompt');

        // /install /passive /norestart запускает установку с индикатором прогресса Microsoft без перезагрузки
        if not Exec(InstallerPath, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          ErrorText := CustomMessage('DotNetInstallError') + 'Exec failed';
          SuppressibleMsgBox(ErrorText, mbError, MB_OK, IDOK);
          Result := False;
          Exit;
        end;

        // Коды успешного завершения: 0 (Success), 3010 (Success with reboot)
        if (ResultCode <> 0) and (ResultCode <> 3010) then
        begin
          ErrorText := CustomMessage('DotNetInstallError') + IntToStr(ResultCode);
          SuppressibleMsgBox(ErrorText, mbError, MB_OK, IDOK);
          Result := False;
          Exit;
        end;
      end;
    end;
  end;
end;
