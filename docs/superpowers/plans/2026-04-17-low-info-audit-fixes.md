# Low/Info Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close low/info audit items L1-L9 with low-risk hardening and cleanup, while preserving current viewer and agent behavior except for explicit busy-response handling on overlapping screenshot requests.

**Architecture:** Split the work into three slices: screenshot-path hardening shared by both agents, WMI/process cleanup in viewer services, and viewer presentation cleanup around converters, timeout constants, and language-refresh behavior. Prefer small, testable helpers only where they remove duplication between the modern and legacy agent or make behavior verifiable in xUnit.

**Tech Stack:** C#, .NET 8, .NET Framework 4.8, WPF, ASP.NET Core minimal API, `HttpListener`, Win32 token APIs, WMI, xUnit.

**Audit Source:** `docs/audit-2026-04-17.md`

---

## File Map

| File | Change |
|------|--------|
| `ScreensView.Shared/ScreenshotQuality.cs` | New shared helper for parsing and clamping JPEG quality arguments to `0..100` |
| `ScreensView.Shared/SingleFlightGate.cs` | New shared helper for non-blocking single-flight capture gating |
| `ScreensView.Shared/ScreenshotBusyException.cs` | New shared exception used by both agents to map "capture already in progress" to HTTP 429 |
| `ScreensView.Agent/Program.cs` | Use shared quality parser; map busy capture to `429 Too Many Requests` |
| `ScreensView.Agent/ScreenshotService.cs` | Add single-flight guard and reduce token access mask to the minimum needed by `CreateProcessAsUser` |
| `ScreensView.Agent.Legacy/Program.cs` | Use shared quality parser |
| `ScreensView.Agent.Legacy/LegacyAgentHost.cs` | Map busy capture to `429 Too Many Requests` |
| `ScreensView.Agent.Legacy/ScreenshotService.cs` | Mirror modern single-flight guard and reduced token access mask |
| `ScreensView.Viewer/Services/RemoteAgentInstaller.cs` | Replace WQL interpolation at the service lookup point with a dedicated constant/helper query builder |
| `ScreensView.Viewer/Services/LlamaServerProcessService.cs` | Explicitly dispose `TcpListener` in `FindFreePort()` |
| `ScreensView.Viewer/Services/RemotePowerService.cs` | Dispose `ManagementObject` instances inside the WMI enumeration |
| `ScreensView.Viewer/Converters.cs` | New home for converter classes currently defined in `App.xaml.cs` |
| `ScreensView.Viewer/App.xaml.cs` | Remove converter type declarations after extraction |
| `ScreensView.Viewer/Services/LlmCheckService.cs` | Extract the duplicated 120-second timeout into named constants |
| `ScreensView.Viewer/ViewModels/MainViewModel.cs` | Replace static localized helper properties with state that recomputes correctly after language changes |
| `ScreensView.Viewer/ViewModels/ComputerViewModel.cs` | Keep disabled-state localized text refresh aligned with the `MainViewModel` localization cleanup |
| `ScreensView.Tests/ScreenshotHardeningTests.cs` | New tests for screenshot quality clamp and single-flight gate behavior |
| `ScreensView.Tests/RemoteAgentInstallerTests.cs` | Add a focused test for the fixed service lookup query helper |
| `ScreensView.Tests/LlamaServerProcessServiceTests.cs` | Add a focused test for `FindFreePort()` port reuse after disposal |
| `ScreensView.Tests/AppConvertersTests.cs` | Keep converter coverage green after the move to `Converters.cs` |
| `ScreensView.Tests/MainViewModelTests.cs` | Add coverage for localized status/error text refresh after switching language |
| `ScreensView.Tests/ComputerViewModelTests.cs` | Add coverage for disabled-state localization refresh if needed |
| `ScreensView.Tests/LlmCheckServiceTests.cs` | Assert timeout text still reflects the extracted constant |
| `docs/audit-2026-04-17.md` | Mark L1-L9 resolved or add implementation notes after the code lands |

**Testing note:** The current suite is already red in this workspace because `LocalizationService.Get()` dereferences `Application.Current` in non-WPF test paths. Task 3 is expected to stabilize part of that area. Capture the red baseline first, then require task-level tests to pass, and finish with a full `dotnet test` before considering the work complete.

**Important audit caveat:** L8 should not blindly implement the suggested `TOKEN_IMPERSONATE | TOKEN_QUERY` mask. `CreateProcessAsUser` requires a primary token handle with `TOKEN_QUERY`, `TOKEN_DUPLICATE`, and `TOKEN_ASSIGN_PRIMARY` rights according to Microsoft Learn. The plan below reduces privilege from `TOKEN_ALL_ACCESS`, but only to the minimum rights needed for `CreateProcessAsUser`.

---

## Task 1: Harden Screenshot Capture Requests in Both Agents

**Files:**
- Create: `ScreensView.Shared/ScreenshotQuality.cs`
- Create: `ScreensView.Shared/SingleFlightGate.cs`
- Create: `ScreensView.Shared/ScreenshotBusyException.cs`
- Modify: `ScreensView.Agent/Program.cs`
- Modify: `ScreensView.Agent/ScreenshotService.cs`
- Modify: `ScreensView.Agent.Legacy/Program.cs`
- Modify: `ScreensView.Agent.Legacy/LegacyAgentHost.cs`
- Modify: `ScreensView.Agent.Legacy/ScreenshotService.cs`
- Test: `ScreensView.Tests/ScreenshotHardeningTests.cs`
- Test: `ScreensView.Tests/ScreenshotPipelineTests.cs`

- [ ] **Step 1: Write failing tests for shared hardening helpers**

Create `ScreensView.Tests/ScreenshotHardeningTests.cs` with focused tests for:

```csharp
[Theory]
[InlineData("75", 75)]
[InlineData("200", 100)]
[InlineData("-5", 0)]
[InlineData("bad", 75)]
public void ScreenshotQuality_ParseOrDefault_ClampsIntoJpegRange(string raw, int expected)
{
    Assert.Equal(expected, ScreenshotQuality.ParseOrDefault(raw));
}

[Fact]
public void SingleFlightGate_RejectsSecondConcurrentEntryUntilLeaseDisposed()
{
    var gate = new SingleFlightGate();
    using var first = gate.TryEnter();

    Assert.NotNull(first);
    Assert.Null(gate.TryEnter());
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ScreenshotHardeningTests" -v normal
```

Expected: FAIL because the helper classes do not exist yet.

- [ ] **Step 3: Add the shared helpers**

Create `ScreensView.Shared/ScreenshotQuality.cs`:

```csharp
namespace ScreensView.Shared;

public static class ScreenshotQuality
{
    public const int Default = 75;

    public static int ParseOrDefault(string? raw) =>
        int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : Default;
}
```

Create `ScreensView.Shared/SingleFlightGate.cs`:

```csharp
namespace ScreensView.Shared;

public sealed class SingleFlightGate
{
    private int _busy;

    public Lease? TryEnter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return null;
        return new Lease(this);
    }

    public sealed class Lease : IDisposable
    {
        private readonly SingleFlightGate _owner;
        private int _disposed;

        internal Lease(SingleFlightGate owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Volatile.Write(ref _owner._busy, 0);
        }
    }
}
```

Create `ScreensView.Shared/ScreenshotBusyException.cs`:

```csharp
namespace ScreensView.Shared;

public sealed class ScreenshotBusyException : InvalidOperationException
{
    public ScreenshotBusyException()
        : base("Screenshot capture is already in progress.") { }
}
```

- [ ] **Step 4: Wire the modern and legacy entry points to the shared quality parser**

Replace the inline parsing in:
- `ScreensView.Agent/Program.cs`
- `ScreensView.Agent.Legacy/Program.cs`

Use:

```csharp
ScreenshotHelper.Run(pipe, ScreenshotQuality.ParseOrDefault(qualStr));
```

and the legacy equivalent for `args[idx + 2]`.

- [ ] **Step 5: Add single-flight busy protection to both screenshot services**

In both screenshot services:
- Add a field:

```csharp
private readonly SingleFlightGate _captureGate = new();
```

- Wrap the capture body:

```csharp
using var captureLease = _captureGate.TryEnter()
    ?? throw new ScreenshotBusyException();
```

Place the lease at the start of `CaptureJpegAsync()` in the modern agent and `CaptureJpeg()` in the legacy agent so overlapping requests fail fast before any token duplication or helper process launch.

- [ ] **Step 6: Reduce token rights without breaking `CreateProcessAsUser`**

In both screenshot services, replace:

```csharp
private const uint TOKEN_ALL_ACCESS = 0x000F01FF;
```

with named minimum rights:

```csharp
private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
private const uint TOKEN_DUPLICATE = 0x0002;
private const uint TOKEN_QUERY = 0x0008;
private const uint REQUIRED_PRIMARY_TOKEN_ACCESS =
    TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY;
```

and use `REQUIRED_PRIMARY_TOKEN_ACCESS` in `DuplicateTokenEx(...)`.

Do **not** use the audit's suggested `TOKEN_IMPERSONATE | TOKEN_QUERY`; that is too narrow for the primary token passed to `CreateProcessAsUser`.

- [ ] **Step 7: Return 429 when a capture is already running**

Modern agent:
- In `ScreensView.Agent/Program.cs`, add a `catch (ScreenshotBusyException)` branch in `/screenshot` and return `Results.Problem(..., statusCode: 429)`.

Legacy agent:
- In `ScreensView.Agent.Legacy/LegacyAgentHost.cs`, add a `catch (ScreenshotBusyException)` branch in the `/screenshot` case and write `429`.

Keep the existing `NoActiveSessionException` mapping intact.

- [ ] **Step 8: Run targeted tests**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ScreenshotHardeningTests" -v normal
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~ScreenshotPipelineTests" -v normal
```

Expected: the new helper tests pass; screenshot pipeline tests still pass or the headless helper test skips silently as before.

- [ ] **Step 9: Commit**

```bash
git add ScreensView.Shared/ScreenshotQuality.cs ScreensView.Shared/SingleFlightGate.cs ScreensView.Shared/ScreenshotBusyException.cs ScreensView.Agent/Program.cs ScreensView.Agent/ScreenshotService.cs ScreensView.Agent.Legacy/Program.cs ScreensView.Agent.Legacy/LegacyAgentHost.cs ScreensView.Agent.Legacy/ScreenshotService.cs ScreensView.Tests/ScreenshotHardeningTests.cs
git commit -m "fix: harden screenshot capture paths in both agents"
```

---

## Task 2: Clean Up WMI Queries and Disposable Resources

**Files:**
- Modify: `ScreensView.Viewer/Services/RemoteAgentInstaller.cs`
- Modify: `ScreensView.Viewer/Services/LlamaServerProcessService.cs`
- Modify: `ScreensView.Viewer/Services/RemotePowerService.cs`
- Test: `ScreensView.Tests/RemoteAgentInstallerTests.cs`
- Test: `ScreensView.Tests/LlamaServerProcessServiceTests.cs`

- [ ] **Step 1: Add a dedicated query builder for the service lookup**

In `RemoteAgentInstaller.cs`, replace the inline interpolated WQL string in `GetServiceObject()` with a dedicated helper:

```csharp
internal static ObjectQuery BuildServiceLookupQuery() =>
    new("SELECT * FROM Win32_Service WHERE Name='" + Constants.ServiceName + "'");
```

Then use:

```csharp
var query = BuildServiceLookupQuery();
```

The point is not current security exposure, but making the query construction obviously constant and non-user-driven.

- [ ] **Step 2: Add a targeted test for the service lookup query**

Extend `ScreensView.Tests/RemoteAgentInstallerTests.cs`:

```csharp
[Fact]
public void BuildServiceLookupQuery_UsesConstantServiceName()
{
    var query = RemoteAgentInstaller.BuildServiceLookupQuery();

    Assert.Equal(
        $"SELECT * FROM Win32_Service WHERE Name='{Constants.ServiceName}'",
        query.QueryString);
}
```

- [ ] **Step 3: Explicitly dispose the free-port probe listener**

In `LlamaServerProcessService.cs`, change:

```csharp
var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
```

to:

```csharp
using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
```

Keep the method behavior the same.

- [ ] **Step 4: Add a focused free-port reuse test**

Extend `ScreensView.Tests/LlamaServerProcessServiceTests.cs` with a reflection-based smoke test:

```csharp
[Fact]
public void FindFreePort_ReturnedPortCanBeReboundImmediately()
{
    var port = InvokeFindFreePort();
    using var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();

    Assert.Equal(port, ((IPEndPoint)listener.LocalEndpoint).Port);
}
```

This does not directly prove `Dispose()`, but it does prove the helper leaves the port immediately reusable.

- [ ] **Step 5: Dispose `ManagementObject` instances in `RemotePowerService`**

Change:

```csharp
foreach (ManagementObject os in searcher.Get())
    os.InvokeMethod("Win32Shutdown", new object[] { flags, 0 });
```

to:

```csharp
foreach (ManagementObject os in searcher.Get())
using (os)
    os.InvokeMethod("Win32Shutdown", new object[] { flags, 0 });
```

Keep the WMI enumeration otherwise unchanged.

- [ ] **Step 6: Run targeted tests**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~RemoteAgentInstallerTests|FullyQualifiedName~LlamaServerProcessServiceTests" -v normal
```

Expected: all tests in those two suites pass.

- [ ] **Step 7: Commit**

```bash
git add ScreensView.Viewer/Services/RemoteAgentInstaller.cs ScreensView.Viewer/Services/LlamaServerProcessService.cs ScreensView.Viewer/Services/RemotePowerService.cs ScreensView.Tests/RemoteAgentInstallerTests.cs ScreensView.Tests/LlamaServerProcessServiceTests.cs
git commit -m "refactor: clean up WMI queries and disposable resource handling"
```

---

## Task 3: Clean Up Viewer Presentation Code and Language Refresh Behavior

**Files:**
- Create: `ScreensView.Viewer/Converters.cs`
- Modify: `ScreensView.Viewer/App.xaml.cs`
- Modify: `ScreensView.Viewer/Services/LlmCheckService.cs`
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Viewer/ViewModels/ComputerViewModel.cs`
- Test: `ScreensView.Tests/AppConvertersTests.cs`
- Test: `ScreensView.Tests/MainViewModelTests.cs`
- Test: `ScreensView.Tests/ComputerViewModelTests.cs`
- Test: `ScreensView.Tests/LlmCheckServiceTests.cs`

- [ ] **Step 1: Move converter classes out of `App.xaml.cs`**

Create `ScreensView.Viewer/Converters.cs` and move these classes there without changing their namespace:
- `NullToBoolConverter`
- `LlmBorderBrushConverter`
- `LlmBorderThicknessConverter`
- `LlmStatusToTextConverter`
- `LlmStatusToBackgroundConverter`
- `LlmStatusToVisibilityConverter`
- `LlmTooltipConverter`
- `ModelDownloadActiveConverter`

`App.xaml` should continue to resolve them through the existing `local:` namespace, so no XAML resource key changes should be necessary.

- [ ] **Step 2: Remove dead converter code from `App.xaml.cs`**

After the move:
- Delete the converter class declarations from `App.xaml.cs`
- Remove no-longer-needed `using` directives such as `System.Globalization` and `System.Windows.Data` if they become unused

- [ ] **Step 3: Extract the duplicated 120-second timeout into named constants**

In `LlmCheckService.cs`, replace the duplicated timeout literals with:

```csharp
private const int DefaultPerComputerTimeoutSeconds = 120;
private static readonly TimeSpan DefaultPerComputerTimeout =
    TimeSpan.FromSeconds(DefaultPerComputerTimeoutSeconds);
private const string TimeoutMessageTemplate =
    "Распознавание превысило лимит {0} секунд. Повторим в следующем цикле.";
```

Use `DefaultPerComputerTimeout` in the constructor and:

```csharp
var timeoutMessage = string.Format(
    TimeoutMessageTemplate,
    DefaultPerComputerTimeoutSeconds);
```

in the timeout branch.

- [ ] **Step 4: Replace localized static helper properties in `MainViewModel`**

Remove:

```csharp
private static string ModelLoadErrorStatusText => LocalizationService.Get(...);
private static string ModelMissingMessage => LocalizationService.Get(...);
private static string LlmDisabledMessage => LocalizationService.Get(...);
private static string BackendErrorTitle => LocalizationService.Get(...);
```

Prefer stable keys or explicit instance methods. The lowest-risk option is to store the resource key instead of a pre-localized string for persistent error state:

```csharp
private string? _modelLoadErrorResourceKey;

public string ModelStatusText =>
    _modelLoadErrorResourceKey is not null
        ? LocalizationService.Get(_modelLoadErrorResourceKey)
        : _downloadService.IsModelReady
            ? LocalizationService.Get("Str.Vm.ModelReady")
            : LocalizationService.Get("Str.Vm.ModelNotDownloaded");
```

Update `SetModelLoadError(...)`, `ClearModelLoadError()`, and all callers accordingly.

- [ ] **Step 5: Make language-switch refresh explicit for stateful localized text**

In `MainViewModel.OnLanguageChanged(...)`, keep the current `OnPropertyChanged(...)` calls and ensure any cached localized state also rehydrates correctly. The important outcome is:
- `ModelStatusText` changes language even if an error was already present before the switch
- backend error titles and manual-run error messages use the current language when invoked after the switch

In `ComputerViewModel`, remove the static `DisabledMessage` helper if it is no longer needed, and keep `NotifyLanguageChanged()` as the single place that re-applies the disabled-state string.

- [ ] **Step 6: Add or update tests for the new refresh behavior**

Extend `MainViewModelTests.cs` with a focused test:

```csharp
[Fact]
public void ModelStatusText_WhenLanguageChanges_RecomputesLocalizedErrorText()
{
    // Arrange a model-load error state in Russian
    // Switch vm.Language to "en"
    // Assert vm.ModelStatusText now uses the English resource
}
```

Extend `LlmCheckServiceTests.cs` so the timeout test asserts through the extracted constant/message path instead of a duplicated literal.

Keep `AppConvertersTests.cs` green after the type move. If `ComputerViewModel` disabled text behavior changes, add/adjust a focused `ComputerViewModelTests.cs` assertion for `NotifyLanguageChanged()`.

- [ ] **Step 7: Run targeted viewer tests**

Run:

```bash
dotnet test ScreensView.Tests/ScreensView.Tests.csproj --filter "FullyQualifiedName~AppConvertersTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~ComputerViewModelTests|FullyQualifiedName~LlmCheckServiceTests" -v normal
```

Expected: these suites pass, and the existing localization-related failures disappear if they were caused by cached localized strings in this area.

- [ ] **Step 8: Commit**

```bash
git add ScreensView.Viewer/Converters.cs ScreensView.Viewer/App.xaml.cs ScreensView.Viewer/Services/LlmCheckService.cs ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Viewer/ViewModels/ComputerViewModel.cs ScreensView.Tests/AppConvertersTests.cs ScreensView.Tests/MainViewModelTests.cs ScreensView.Tests/ComputerViewModelTests.cs ScreensView.Tests/LlmCheckServiceTests.cs
git commit -m "refactor: clean up viewer converters and localization state"
```

---

## Task 4: Final Verification and Audit Update

**Files:**
- Modify: `docs/audit-2026-04-17.md`

- [ ] **Step 1: Update the audit document**

In `docs/audit-2026-04-17.md`, mark L1-L9 as fixed or append short implementation notes. For L8, note the implemented minimum rights rather than copying the original audit suggestion if the final code uses `TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY`.

- [ ] **Step 2: Run the full test suite**

Run:

```bash
dotnet test
```

Expected: all tests pass. If unrelated pre-existing failures remain, stop and fix them before closing the work. Do not declare the audit items complete while the suite is red.

- [ ] **Step 3: Commit the documentation update and final verification state**

```bash
git add docs/audit-2026-04-17.md
git commit -m "docs: record low and info audit remediations"
```

