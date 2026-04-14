# 4K Screenshot DPI Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix screenshot cropping on 4K monitors by setting thread DPI awareness in `ScreenshotHelper` before any GDI/metrics calls.

**Architecture:** Two one-line additions — one `SetThreadDpiAwarenessContext` call in the modern agent, one `SetProcessDPIAware` call in the legacy agent — both placed immediately after `pipe.Connect()` and before `GetSystemMetrics`. No other files change.

**Tech Stack:** C# P/Invoke, `user32.dll`, .NET 8 (modern agent), .NET Framework 4.8 (legacy agent), xUnit.

**Spec:** `docs/superpowers/specs/2026-04-14-4k-screenshot-dpi-fix-design.md`

---

## File Map

| File | Change |
|------|--------|
| `ScreensView.Agent/ScreenshotHelper.cs` | Add `SetThreadDpiAwarenessContext` P/Invoke + call with NULL check |
| `ScreensView.Agent.Legacy/ScreenshotHelper.cs` | Add `SetProcessDPIAware` P/Invoke + call |

No other files touched.

**Testing note:** The fix is a Win32 API call that changes GDI coordinate behaviour at runtime. It cannot be meaningfully unit-tested without a physical 4K display — a mocked `GetSystemMetrics` would not exercise the actual `BitBlt` path. Regression is covered by the existing `ScreenshotHelper_PipeProtocol_WritesSuccessPacketWithJpeg` test (verifies the full pipeline still produces a valid JPEG). Correctness on a 4K machine requires a manual check described at the end of the plan.

**CI risk:** After this change, if the test suite runs on a Windows Server host older than version 1607 (e.g. Server 2012 R2), `SetThreadDpiAwarenessContext` will return `IntPtr.Zero`, the helper will call `Environment.Exit(2)` from inside the test, and the test runner process will be killed. If CI uses Windows 10 / Server 2016+ (both are version 1607+) this is not an issue.

---

## Task 1: Modern Agent — Add DPI-Aware Context

**Files:**
- Modify: `ScreensView.Agent/ScreenshotHelper.cs`

### Step 1: Read the current file

Open `ScreensView.Agent/ScreenshotHelper.cs` and locate:
- The existing P/Invoke block at the top of the class (lines ~11–28)
- The `Run()` method body, specifically `pipe.Connect(5_000)` and the `GetSystemMetrics` calls that follow

### Step 2: Add the P/Invoke declaration

In `ScreenshotHelper.cs`, add two lines to the P/Invoke block, alongside the existing `GetSystemMetrics` import:

```csharp
[DllImport("user32.dll")]
private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
```

Place them after the last existing `[DllImport]` declaration and before the `private const int SM_XVIRTUALSCREEN` line.

- [ ] Add the P/Invoke declaration and constant to `ScreenshotHelper.cs`

### Step 3: Add the call in `Run()`

In the `Run()` method, immediately after `pipe.Connect(5_000);` and before `if (IsSecureDesktopActive())`, insert:

```csharp
var prevCtx = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
if (prevCtx == IntPtr.Zero)
    throw new InvalidOperationException(
        "SetThreadDpiAwarenessContext failed — requires Windows 10 version 1607 or later.");
```

`SetThreadDpiAwarenessContext` returns the previous context on success, `IntPtr.Zero` on failure. It does NOT throw. The explicit check causes `InvalidOperationException` which is caught by the outer `catch` in `Run()`, which then calls `Environment.Exit(2)` — preventing a silently cropped frame from being sent.

The result should look like:

```csharp
internal static void Run(string pipeName, int quality)
{
    NamedPipeClientStream? pipe = null;
    try
    {
        pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        pipe.Connect(5_000);

        var prevCtx = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        if (prevCtx == IntPtr.Zero)
            throw new InvalidOperationException(
                "SetThreadDpiAwarenessContext failed — requires Windows 10 version 1607 or later.");

        if (IsSecureDesktopActive())
        {
            // ... existing code unchanged below
```

- [ ] Add the `SetThreadDpiAwarenessContext` call with NULL check to `Run()`

### Step 4: Build the modern agent

```bash
dotnet build ScreensView.Agent/ScreensView.Agent.csproj
```

Expected: **Build succeeded** with 0 errors.

- [ ] Run the build and confirm it succeeds

### Step 5: Run existing tests

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ScreenshotPipelineTests" -v normal
```

Expected: all three `ScreenshotPipelineTests` tests pass (the pipeline test may be skipped if running headless — that is acceptable).

- [ ] Run the pipeline tests and confirm no regressions

### Step 6: Commit

```bash
git add ScreensView.Agent/ScreenshotHelper.cs
git commit -m "fix: set PerMonitorV2 DPI context in modern agent screenshot helper"
```

- [ ] Commit the change

---

## Task 2: Legacy Agent — Add System DPI Awareness

**Files:**
- Modify: `ScreensView.Agent.Legacy/ScreenshotHelper.cs`

### Step 1: Read the current file

Open `ScreensView.Agent.Legacy/ScreenshotHelper.cs` and locate:
- The existing P/Invoke block at the top of the class
- The `Run()` method body, specifically `pipe.Connect(5_000)` and the `GetSystemMetrics` calls

### Step 2: Add the P/Invoke declaration

In `ScreensView.Agent.Legacy/ScreenshotHelper.cs`, add to the P/Invoke block:

```csharp
[DllImport("user32.dll")]
private static extern bool SetProcessDPIAware();
```

Place it after the last existing `[DllImport]` declaration and before the `private const int SM_XVIRTUALSCREEN` line.

- [ ] Add the `SetProcessDPIAware` P/Invoke to `ScreensView.Agent.Legacy/ScreenshotHelper.cs`

### Step 3: Add the call in `Run()`

In the `Run()` method, immediately after `pipe.Connect(5_000);` and before `if (IsSecureDesktopActive())`, insert:

```csharp
SetProcessDPIAware();
```

No return-value check needed: `SetProcessDPIAware` returns `false` only if the process was already DPI-aware (idempotent — not a failure condition). The call ensures the process is at least system-DPI-aware before `GetSystemMetrics` and `CopyFromScreen` run.

The result:

```csharp
pipe.Connect(5_000);

SetProcessDPIAware();

if (IsSecureDesktopActive())
{
    // ... existing code unchanged below
```

- [ ] Add the `SetProcessDPIAware()` call to `Run()` in the legacy helper

### Step 4: Build the legacy agent

```bash
dotnet build ScreensView.Agent.Legacy/ScreensView.Agent.Legacy.csproj
```

Expected: **Build succeeded** with 0 errors.

- [ ] Run the build and confirm it succeeds

### Step 5: Run full test suite

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj -v normal
```

Expected: all tests pass (or the same tests as before are skipped on headless).

- [ ] Run the full test suite and confirm no regressions

### Step 6: Commit

```bash
git add ScreensView.Agent.Legacy/ScreenshotHelper.cs
git commit -m "fix: set system DPI awareness in legacy agent screenshot helper"
```

- [ ] Commit the change

---

## Manual Verification (on a 4K machine)

Deploy the updated modern agent to the remote machine with a 4K monitor at 150% DPI and take a screenshot from the Viewer.

**Before fix:** Only the upper-left ~2560×1440 area of the 3840×2160 screen is visible; right and bottom thirds are black/missing.

**After fix:** The full 3840×2160 screen is captured. All four corners of the remote desktop are visible in the Viewer screenshot.

- [ ] Verify on a 4K machine that the full screen is captured
