# Prerequisites

This folder must contain both .NET 8 x64 offline installers before using
the **"Установить .NET 8 runtimes"** action in the Viewer.

## How to obtain

1. Go to https://dotnet.microsoft.com/download/dotnet/8.0
2. Under **Run apps - Runtime**, download both Windows x64 installers:
   - **.NET Runtime 8.0.x** → `dotnet-runtime-8.0.x-win-x64.exe`
   - **ASP.NET Core Runtime 8.0.x** → `aspnetcore-runtime-8.0.x-win-x64.exe`
3. Save both files here as-is.

The file is gitignored — only this README is committed.

## How it is used

- `ScreensView.Viewer.csproj` copies this folder into the Viewer output as `Prerequisites\`.
- The toolbar button **"Установить .NET 8 runtimes"** installs both offline runtimes on
  the selected computers in order: `.NET Runtime`, then `ASP.NET Core Runtime`.
- The main agent install/update flow does **not** install `.NET` automatically.

## Updating the runtime version

When a newer patch is available, download the new installers and place them here using
the original Microsoft filename patterns:

- `dotnet-runtime-8.*-win-x64.exe`
- `aspnetcore-runtime-8.*-win-x64.exe`

No code change is required: `RemoteAgentInstaller` locates the newest matching file
for each runtime automatically.
