# Prerequisites

This folder must contain the ASP.NET Core Runtime 8 offline installer before using
the **"Установить .NET 8 Runtime"** action in the Viewer.

## How to obtain

1. Go to https://dotnet.microsoft.com/download/dotnet/8.0
2. Under **ASP.NET Core Runtime 8.0.x** → **Windows** → click **Installer (x64)**
3. Save the file here as-is (e.g. `aspnetcore-runtime-8.0.15-win-x64.exe`)

The file is gitignored — only this README is committed.

## How it is used

- `ScreensView.Viewer.csproj` copies this folder into the Viewer output as `Prerequisites\`.
- The toolbar button **"Установить .NET 8 Runtime"** installs the offline runtime on the
  selected computers.
- The main agent install/update flow does **not** install `.NET` automatically.

## Updating the runtime version

When a newer patch is available, download the new installer and place it here using
the original Microsoft filename pattern `aspnetcore-runtime-8.*-win-x64.exe`.
No code change is required: `RemoteAgentInstaller` locates the newest matching file
automatically.
