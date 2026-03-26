# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build entire solution
dotnet build

# Build specific projects
dotnet build ScreensView.Agent/ScreensView.Agent.csproj
dotnet build ScreensView.Agent.Legacy/ScreensView.Agent.Legacy.csproj
dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj

# Run modern agent (requires admin — writes cert and HTTPS config)
dotnet run --project ScreensView.Agent

# Run Viewer
dotnet run --project ScreensView.Viewer
```

Before running any agent, set `Agent:ApiKey` in the deployed `appsettings.json` (cannot be empty).

## Architecture

Four projects in one solution:

- **ScreensView.Shared** (`netstandard2.0`) — shared models (`ComputerConfig`, `ScreenshotResponse`), `Constants`, and `AgentJsonSerializer`. Referenced by modern agent, legacy agent, and Viewer.
- **ScreensView.Agent** (`net8.0-windows`, SDK: `Microsoft.NET.Sdk.Web`) — ASP.NET Core minimal API running as a Windows Service via `UseWindowsService()`. Kestrel listens on HTTPS using a self-signed cert stored in `LocalMachine\My`.
- **ScreensView.Agent.Legacy** (`net48`) — Windows Service for Windows 7 SP1. Hosts HTTPS via `HttpListener`, manages self-signed cert + `netsh http` binding, and preserves the same `/health` + `/screenshot` contract and `X-Api-Key` auth.
- **ScreensView.Viewer** (`net8.0-windows`, WPF) — polls agents via `ScreenshotPollerService`, stores computer list encrypted in `%AppData%\ScreensView\computers.json`, and performs remote install/update through `RemoteAgentInstaller`.

### Key data flow

1. User adds a computer in `AddEditComputerWindow` → saved to `ComputerStorageService` → shown in `MainWindow` grid as `ComputerViewModel`
2. `ScreenshotPollerService.Start()` polls all enabled computers on a configurable interval → updates `ComputerViewModel.Screenshot` on UI thread via Dispatcher
3. Remote install: `RemoteAgentInstaller` connects via WNet (SMB to `Admin$`), queries remote OS via WMI, selects either modern or legacy payload, copies files + writes `appsettings.json`, then creates/starts the Windows Service via `Win32_Service`

### Target frameworks

- Shared: `netstandard2.0`
- Modern agent: `net8.0-windows`
- Legacy agent: `net48` (Windows 7 SP1 + `.NET Framework 4.8` required)
- Viewer: `net8.0-windows`

### Remote Agent Install Requirements

Target machine needs: `Admin$` share accessible, WMI open (port 135 + dynamic RPC), local admin credentials.

Payload selection rules:

- Windows 10/11 and supported server OS → `ScreensView.Agent`
- Windows 7 SP1 with `.NET Framework 4.8` → `ScreensView.Agent.Legacy`
- Windows 7 without `.NET Framework 4.8` or unsupported OS → installer fails with a descriptive error

Both payloads are copied to `C:\Windows\ScreensViewAgent\`; service name remains `ScreensViewAgent`.

## Auto-update

`ViewerUpdateService` checks GitHub Releases on startup. The repository URL is hardcoded as `YOUR_GITHUB_USER` in `ScreensView.Viewer/Services/ViewerUpdateService.cs` — update this before publishing.
