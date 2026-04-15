# CLAUDE.md

Guidance for Claude Code (claude.ai/code) in repo.

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

Before run: set `Agent:ApiKey` in `appsettings.json`. Cannot be empty.

## Architecture

Five projects:

- **ScreensView.Shared** (`netstandard2.0`) — shared models (`ComputerConfig`, `ScreenshotResponse`), `Constants`, `AgentJsonSerializer`. Used by modern agent, legacy agent, Viewer.
- **ScreensView.Agent** (`net8.0-windows`, SDK: `Microsoft.NET.Sdk.Web`) — ASP.NET Core minimal API as Windows Service via `UseWindowsService()`. Kestrel HTTPS, self-signed cert in `LocalMachine\My`.
- **ScreensView.Agent.Legacy** (`net48`) — Windows Service for Win7 SP1. HTTPS via `HttpListener`, self-signed cert + `netsh http` binding, same `/health` + `/screenshot` contract, `X-Api-Key` auth.
- **ScreensView.Viewer** (`net8.0-windows`, WPF) — polls agents via `ScreenshotPollerService`, stores computer list encrypted in `%AppData%\ScreensView\computers.json`, remote install/update + `.NET 8 runtimes` install via `RemoteAgentInstaller`.
- **ScreensView.Tests** (`net8.0-windows`, xUnit) — tests for `ScreenshotHelper` pipe protocol + `NoActiveSessionException`.

### Key data flow

1. User adds computer in `AddEditComputerWindow` → saved to `ComputerStorageService` → shown in `MainWindow` grid as `ComputerViewModel`
2. `ScreenshotPollerService.Start()` polls all enabled computers on configurable interval → updates `ComputerViewModel.Screenshot` on UI thread via Dispatcher
3. Remote install: `RemoteAgentInstaller` connects via WNet (SMB to `Admin$`), queries remote OS via WMI, selects modern or legacy payload, copies files + writes `appsettings.json`, creates/starts Windows Service via `Win32_Service`. Machine needing modern-agent prerequisites: install separately via toolbar action `Установить .NET 8 runtimes`, runs `.NET Runtime` then `ASP.NET Core Runtime` in order.
4. Tile right-click context menu in `MainWindow` resolves target `ComputerViewModel` via `GetMenuVm()` (`MenuItem.Parent` → `ContextMenu.PlacementTarget` → `DataContext` cast), delegates to `OpenZoomWindow`, `_vm.UpdateComputer`, `_vm.RemoveComputer`, or `AgentHttpClient.CheckHealthAsync`

### Screenshot capture — Session 0 isolation

Both agents run as **LocalSystem in Session 0**. Vista+ isolates Session 0 from interactive desktop — `CopyFromScreen()` from service reads empty surface → black image.

**Fix:** each `/screenshot` spawns same EXE as helper subprocess via `CreateProcessAsUser` with logged-in user token (`WTSQueryUserToken` + `DuplicateTokenEx`). Helper runs in user session with `lpDesktop="winsta0\default"`, captures via `CopyFromScreen()`, sends JPEG via named pipe (4-byte length prefix + raw bytes). Service reads pipe, returns image.

Key files:

- `ScreenshotService.cs` — `WTSQueryUserToken` → `DuplicateTokenEx` → `CreateProcessAsUser` → named pipe read
- `ScreenshotHelper.cs` — helper entry point; `CopyFromScreen` → JPEG encode → pipe write
- `NoActiveSessionException.cs` — thrown when `WTSGetActiveConsoleSessionId()` returns `0xFFFFFFFF` (no user logged in) → HTTP 503

Both agents implement pattern identically. P/Invokes use `advapi32.dll` for `CreateProcessAsUser` (Win7 compat; Win8+ forwards from kernel32).

### Target frameworks

- Shared: `netstandard2.0`
- Modern agent: `net8.0-windows`
- Legacy agent: `net48` (Win7 SP1 + `.NET Framework 4.8` required)
- Viewer: `net8.0-windows`
- Tests: `net8.0-windows`

### Remote Agent Install Requirements

Target machine: `Admin$` share accessible, WMI open (port 135 + dynamic RPC), local admin credentials.

Payload selection:

- Windows 10/11 and supported server OS → `ScreensView.Agent`
- Windows 7 SP1 with `.NET Framework 4.8` → `ScreensView.Agent.Legacy`
- Win7 without `.NET Framework 4.8` or unsupported OS → installer fails with descriptive error

Both payloads copied to `C:\Windows\ScreensViewAgent\`; service name stays `ScreensViewAgent`. Service always created with `StartName=LocalSystem` — required for `WTSQueryUserToken` (`SeTcbPrivilege`).

`RemoteAgentInstaller` stops existing service before copying (`InstallAsync`, `UpdateAsync`, `UninstallAsync`). `StopService` failures non-fatal — logged as warnings, operation continues.

Modern agent prerequisites:

- Viewer packages offline `dotnet-runtime-8.*-win-x64.exe` and `aspnetcore-runtime-8.*-win-x64.exe` installers from `ScreensView.Viewer/Prerequisites/`.
- Runtime install is separate per-selection action in `ComputersManagerWindow`.
- `InstallAsync` and `UpdateAsync` do not install `.NET` automatically.

### InstallProgressWindow

One row per computer. Each step from `RemoteAgentInstaller` updates row real-time via `INotifyPropertyChanged`. Color by `AgentLogLevel`: green = success, red = error, yellow = warning (transient — stop-service failure or runtime install needs reboot).

## Auto-update

`ViewerUpdateService` checks GitHub Releases on startup. Repo URL hardcoded as `YOUR_GITHUB_USER` in `ScreensView.Viewer/Services/ViewerUpdateService.cs` — update before publishing.