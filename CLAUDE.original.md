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

# Run tests
dotnet test

# Run modern agent (requires admin — writes cert and HTTPS config)
dotnet run --project ScreensView.Agent

# Run Viewer
dotnet run --project ScreensView.Viewer
```

Before running any agent, set `Agent:ApiKey` in the deployed `appsettings.json` (cannot be empty).

## Architecture

Five projects in one solution:

- **ScreensView.Shared** (`netstandard2.0`) — shared models (`ComputerConfig`, `ScreenshotResponse`), `Constants`, and `AgentJsonSerializer`. Referenced by modern agent, legacy agent, and Viewer.
- **ScreensView.Agent** (`net8.0-windows`, SDK: `Microsoft.NET.Sdk.Web`) — ASP.NET Core minimal API running as a Windows Service via `UseWindowsService()`. Kestrel listens on HTTPS using a self-signed cert stored in `LocalMachine\My`.
- **ScreensView.Agent.Legacy** (`net48`) — Windows Service for Windows 7 SP1. Hosts HTTPS via `HttpListener`, manages self-signed cert + `netsh http` binding, and preserves the same `/health` + `/screenshot` contract and `X-Api-Key` auth.
- **ScreensView.Viewer** (`net8.0-windows`, WPF) — polls agents via `ScreenshotPollerService`, stores computer list encrypted in `%AppData%\ScreensView\computers.json`, and performs remote install/update plus manual `.NET 8 runtimes` installation through `RemoteAgentInstaller`.
- **ScreensView.Tests** (`net8.0-windows`, xUnit) — tests for `ScreenshotHelper` pipe protocol and `NoActiveSessionException`.

### Key data flow

1. User adds a computer in `AddEditComputerWindow` → saved to `ComputerStorageService` → shown in `MainWindow` grid as `ComputerViewModel`
2. `ScreenshotPollerService.Start()` polls all enabled computers on a configurable interval → updates `ComputerViewModel.Screenshot` on UI thread via Dispatcher
3. Remote install: `RemoteAgentInstaller` connects via WNet (SMB to `Admin$`), queries remote OS via WMI, selects either modern or legacy payload, copies files + writes `appsettings.json`, then creates/starts the Windows Service via `Win32_Service`. If a machine needs modern-agent prerequisites, install them separately from the toolbar action `Установить .NET 8 runtimes`, which runs `.NET Runtime` and `ASP.NET Core Runtime` in order.
4. Tile right-click context menu in `MainWindow` resolves the target `ComputerViewModel` via `GetMenuVm()` (`MenuItem.Parent` → `ContextMenu.PlacementTarget` → `DataContext` cast) and then delegates to `OpenZoomWindow`, `_vm.UpdateComputer`, `_vm.RemoveComputer`, or `AgentHttpClient.CheckHealthAsync`

### Screenshot capture — Session 0 isolation

Both agents run as **LocalSystem in Session 0**. Windows Vista+ isolates Session 0 from the interactive desktop, so `CopyFromScreen()` called directly from the service reads an empty surface → black image.

**Fix:** each `/screenshot` request spawns the same EXE as a helper subprocess via `CreateProcessAsUser` with the logged-in user's token (`WTSQueryUserToken` + `DuplicateTokenEx`). The helper runs in the user session with `lpDesktop="winsta0\default"`, captures the screen with plain `CopyFromScreen()`, and sends the JPEG back via a named pipe (4-byte length prefix + raw bytes). The service reads the pipe and returns the image.

Key files:

- `ScreenshotService.cs` — `WTSQueryUserToken` → `DuplicateTokenEx` → `CreateProcessAsUser` → named pipe read
- `ScreenshotHelper.cs` — helper entry point; `CopyFromScreen` → JPEG encode → pipe write
- `NoActiveSessionException.cs` — thrown when `WTSGetActiveConsoleSessionId()` returns `0xFFFFFFFF` (no user logged in) → HTTP 503

Both agents (`ScreensView.Agent` and `ScreensView.Agent.Legacy`) implement this pattern identically. P/Invokes use `advapi32.dll` for `CreateProcessAsUser` (Win7 compatibility; Win8+ forwards from kernel32 transparently).

### Target frameworks

- Shared: `netstandard2.0`
- Modern agent: `net8.0-windows`
- Legacy agent: `net48` (Windows 7 SP1 + `.NET Framework 4.8` required)
- Viewer: `net8.0-windows`
- Tests: `net8.0-windows`

### Remote Agent Install Requirements

Target machine needs: `Admin$` share accessible, WMI open (port 135 + dynamic RPC), local admin credentials.

Payload selection rules:

- Windows 10/11 and supported server OS → `ScreensView.Agent`
- Windows 7 SP1 with `.NET Framework 4.8` → `ScreensView.Agent.Legacy`
- Windows 7 without `.NET Framework 4.8` or unsupported OS → installer fails with a descriptive error

Both payloads are copied to `C:\Windows\ScreensViewAgent\`; service name remains `ScreensViewAgent`. Service is always created with `StartName=LocalSystem` — required for `WTSQueryUserToken` (`SeTcbPrivilege`).

`RemoteAgentInstaller` stops the existing service before copying files (`InstallAsync`, `UpdateAsync`, `UninstallAsync`). `StopService` failures are non-fatal — logged as warnings and the operation continues.

Modern agent prerequisites:

- The Viewer packages offline `dotnet-runtime-8.*-win-x64.exe` and `aspnetcore-runtime-8.*-win-x64.exe` installers from `ScreensView.Viewer/Prerequisites/`.
- Runtime installation is a separate per-selection action in `ComputersManagerWindow`.
- `InstallAsync` and `UpdateAsync` do not install `.NET` automatically.

### InstallProgressWindow

`InstallProgressWindow` shows one row per computer. Each step from `RemoteAgentInstaller` updates the row in real time via `INotifyPropertyChanged`. Rows are color-coded by `AgentLogLevel`: green = success, red = error, yellow = warning (transient, e.g. stop-service failure or runtime install requiring reboot).

## Auto-update

`ViewerUpdateService` checks GitHub Releases on startup. The repository URL is hardcoded as `YOUR_GITHUB_USER` in `ScreensView.Viewer/Services/ViewerUpdateService.cs` — update this before publishing.
