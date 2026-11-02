@echo off
setlocal
rem Copy this launcher beside the published DLL in Web\bin\Release-SqlStorage.
if not exist "%~dp0OperatorCertificationRecord.Web.dll" (
    echo This launcher must be beside OperatorCertificationRecord.Web.dll.
    pause
    exit /b 1
)
rem Reopening the shortcut should not try to bind a second copy to port 5000.
powershell.exe -NoProfile -Command "try { $page = Invoke-WebRequest -Uri 'http://localhost:5000/Login' -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop; if ($page.Content -match 'OTD System') { exit 0 } } catch {}; exit 1" >nul 2>&1
if not errorlevel 1 (
    echo OTD is already running. Open http://localhost:5000 in your browser.
    exit /b 0
)
rem Preserve the original project's local state; access roles are shared through SQL.
pushd "%~dp0..\.." || exit /b 1
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://localhost:5000"
set "FileStorage__ReadSqlEnabled=true"
set "FileStorage__WriteSqlEnabled=true"
set "AccessRoles__Storage=Sql"
set "LocalUploadSubfolder=\\svr120a\Cert$"
set "PhotoStorage__LegacyMirrorRoot=\\svr120a\PhotoEmp$"
rem Development loads the existing UserSecretsId; no credentials are stored here.
dotnet "%~dp0OperatorCertificationRecord.Web.dll" --contentRoot "%~dp0."
set "webExitCode=%ERRORLEVEL%"
popd
if not "%webExitCode%"=="0" (
    echo.
    echo OTD could not start. Keep this window open and read the error above.
    pause
)
exit /b %webExitCode%
