@echo off
rem Keep the previous laptop shortcut on the same current local web build.
if not exist "%~dp0..\Release-SqlStorage\Start-LocalSqlStorageWeb.cmd" (
    echo The current local build was not found in Release-SqlStorage.
    pause
    exit /b 1
)
call "%~dp0..\Release-SqlStorage\Start-LocalSqlStorageWeb.cmd"
exit /b %ERRORLEVEL%
