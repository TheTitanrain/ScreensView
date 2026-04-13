# Fix: 4K Screenshot Cropping on High-DPI Monitors

**Date:** 2026-04-14
**Status:** Approved

## Problem

On a remote machine with a 4K monitor (3840×2160) at 150% DPI scaling, the screenshot helper captures only the upper-left portion of the screen. The right third and bottom third of the physical screen are missing from the captured image.

### Root Cause

`ScreenshotHelper` is spawned by the Windows Service via `CreateProcessAsUser`. Neither agent project has an app.manifest that declares DPI awareness, so Windows may treat the helper as DPI-unaware or with inconsistent DPI context.

In this state there is a mismatch between the coordinate spaces used by two consecutive calls:

1. `GetSystemMetrics(SM_CXVIRTUALSCREEN)` / `SM_CYVIRTUALSCREEN` — returns **logical** pixels (e.g. 2560×1440 at 150% DPI on a 3840×2160 physical screen).
2. `Graphics.CopyFromScreen` / internal `BitBlt` — accesses the screen DC in **physical** pixel coordinates.

Result: a 2560×1440 bitmap is created, and `BitBlt` copies physical pixels (0,0)→(2560,1440) from a 3840×2160 screen — covering only the top-left two-thirds of the actual display.

The captured image fills the viewer window edge-to-edge (both image and viewer are 16:9) so there are no letterbox bars, but the right and bottom content of the remote screen is never captured.

## Solution

Set the thread DPI awareness context to `PER_MONITOR_AWARE_V2` at the very start of `ScreenshotHelper.Run()`, before any display metric or GDI calls. After this call both `GetSystemMetrics` and `CopyFromScreen` operate in **physical pixel** coordinates, eliminating the mismatch.

This is a targeted, thread-scoped change. It does not affect the service host or Kestrel portions of the agent process.

## Changes

### `ScreensView.Agent/ScreenshotHelper.cs`

Add one P/Invoke and call it at the top of `Run()`:

```csharp
[DllImport("user32.dll")]
private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4 (Windows 10 1607+)
private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
```

In `Run()`, first line after opening the pipe:

```csharp
SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
```

After this call `GetSystemMetrics` returns physical pixels (3840×2160) and `CopyFromScreen` captures the full physical screen at native resolution.

### `ScreensView.Agent.Legacy/ScreenshotHelper.cs`

The legacy agent targets .NET Framework 4.8 and Windows 7 SP1+. `SetThreadDpiAwarenessContext` is only available on Windows 10 1607+, so use `SetProcessDPIAware` (available since Windows Vista) which sets system-DPI-aware mode — sufficient for Windows 7 where per-monitor DPI V2 does not exist:

```csharp
[DllImport("user32.dll")]
private static extern bool SetProcessDPIAware();
```

In `Run()`, first line:

```csharp
SetProcessDPIAware();
```

## Scope

| File | Change |
|------|--------|
| `ScreensView.Agent/ScreenshotHelper.cs` | Add `SetThreadDpiAwarenessContext` P/Invoke + call at start of `Run()` |
| `ScreensView.Agent.Legacy/ScreenshotHelper.cs` | Add `SetProcessDPIAware` P/Invoke + call at start of `Run()` |

No changes to: Viewer, Shared, service host, Kestrel config, app settings, or tests.

## Expected Result

- 4K monitors at any DPI scaling (125%, 150%, 200%) capture the full physical screen.
- Multi-monitor setups with mixed DPI are also handled correctly by `PER_MONITOR_AWARE_V2`.
- Single-monitor and standard-DPI setups are unaffected.
