@echo off
setlocal

set ROOT=%~dp0..
set ISCC=iscc

echo [1/3] Building Viewer and agent payloads...
dotnet build "%ROOT%\ScreensView.Viewer\ScreensView.Viewer.csproj" -c Release
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )

echo [2/3] Building Viewer installer...
%ISCC% "%~dp0ViewerSetup.iss"
if errorlevel 1 ( echo VIEWER INSTALLER FAILED & exit /b 1 )

echo [3/3] Building Agent installer...
%ISCC% "%~dp0AgentSetup.iss"
if errorlevel 1 ( echo AGENT INSTALLER FAILED & exit /b 1 )

echo.
echo Done. Installers in installer\Output\
