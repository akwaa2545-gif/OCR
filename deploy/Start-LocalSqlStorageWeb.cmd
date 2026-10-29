@echo off
setlocal
rem Copy this launcher beside the published DLL in Web\bin\Release-SqlStorage.
if not exist "%~dp0OperatorCertificationRecord.Web.dll" (
    echo This launcher must be beside OperatorCertificationRecord.Web.dll.
    exit /b 1
)
rem Preserve the original project's App_Data admin store and other local state.
pushd "%~dp0..\.." || exit /b 1
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://localhost:5000"
set "FileStorage__ReadSqlEnabled=true"
set "FileStorage__WriteSqlEnabled=true"
set "LocalUploadSubfolder=\\svr120a\Cert$"
set "PhotoStorage__LegacyMirrorRoot=\\svr120a\PhotoEmp$"
rem Development loads the existing UserSecretsId; no credentials are stored here.
dotnet "%~dp0OperatorCertificationRecord.Web.dll" --contentRoot "%~dp0."
set "webExitCode=%ERRORLEVEL%"
popd
exit /b %webExitCode%
