# LLM Screen Analysis — Design Spec

**Date:** 2026-04-02
**Status:** Approved

---

## Context

ScreensView displays live screenshots from remote computers in a tile grid. Currently there is no way to automatically detect whether what's visible on a computer screen matches what's expected. This feature adds a local vision LLM that periodically analyzes each screenshot against a user-defined description, flagging mismatches visually on the tile.

---

## Goal

Each computer can have an optional text description of what should be on its screen. A local multimodal LLM (Qwen3.5-2B-GGUF via LLamaSharp) runs on a configurable interval, compares the current screenshot to the description, and marks the tile with a colored border + tooltip explanation if the screen does not match.

---

## Data Model Changes

### `ComputerConfig` — `ScreensView.Shared/Models/ComputerConfig.cs`

Add optional field (backward-compatible, null for existing records):

```csharp
public string? Description { get; set; }
```

### `ComputerViewModel` — `ScreensView.Viewer/ViewModels/ComputerViewModel.cs`

Add backing field and observable property `Description`, mapped from `ComputerConfig.Description`.

Update **constructor** `ComputerViewModel(ComputerConfig config)`:

```csharp
Description = config.Description;
```

Update **`ToConfig()`** method — add to the returned `ComputerConfig`:

```csharp
Description = Description,
```

Add additional observable properties:

```csharp
public string? Description { get; set; }
public LlmCheckResult? LastLlmCheck { get; set; }  // null = never checked
public bool IsLlmChecking { get; set; }
```

Clearing `Description` (set to null/empty) must also reset `LastLlmCheck` to null — add this side-effect to the `Description` setter.

### New: `LlmCheckResult` — `ScreensView.Viewer/Models/LlmCheckResult.cs`

```csharp
public record LlmCheckResult(
    bool IsMatch,
    string Explanation,
    bool IsError,       // true = inference failed, not a real mismatch
    DateTime CheckedAt
);
```

---

## New Services

### `ModelDownloadService` — `ScreensView.Viewer/Services/ModelDownloadService.cs`

Downloads the GGUF model file on first use.

- Target path: `%AppData%\ScreensView\models\qwen3.5-2b-q4_k_m.gguf`
- Download URL: direct HuggingFace CDN link to the Q4_K_M quantization file
- Download writes to `<target>.part` temporary file, renamed atomically to final path on success
- Supports resume via `Range` header if `.part` file exists
- Progress reported via `IProgress<double>` (0.0–100.0)
- **`IsModelReady`**: returns true only if the final file exists **and** no `.part` file is present
- Accepts `CancellationToken`; on cancellation, leaves the `.part` file for next-startup resume
- Exposes `event EventHandler ModelReady` — fired once when model becomes ready (triggers `LlmCheckService.Start()`)

Interface:

```csharp
bool IsModelReady { get; }
string ModelPath { get; }
event EventHandler ModelReady;
Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
```

No `Cancel()` method — cancellation is entirely through the `CancellationToken` passed to `DownloadAsync`.

### `LlmInferenceService` — `ScreensView.Viewer/Services/LlmInferenceService.cs`

Wraps LLamaSharp to run vision inference.

- Lazy-loads model into memory on first call using `ModelDownloadService.ModelPath`
- Accepts `CancellationToken` for timeout enforcement
- Method: `Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct)`
- Converts `BitmapImage` → JPEG bytes → base64 for multimodal input
- Fixed English prompt:
  > `"Does the screen match this description: '{description}'? Reply with YES or NO and one sentence explanation."`
- Parses: first word YES/NO → `IsMatch`, remainder → `Explanation`
- On parse failure or exception: returns `new LlmCheckResult(IsMatch: false, Explanation: ex.Message, IsError: true, CheckedAt: DateTime.Now)`

**LLamaSharp vision risk:** LLavaWeights multimodal support is experimental. If it proves incompatible with Qwen3.5 GGUF at implementation time, the fallback is to use a different model file that IS confirmed to work with LLamaSharp (e.g. a LLaVA-based GGUF). The download URL in `ModelDownloadService` and the inference internals change, but the service interface and the rest of the architecture stay the same. **Ollama is not a fallback** — switching to Ollama would make `ModelDownloadService` obsolete and require separate installation/readiness-checking outside the scope of this spec.

**NuGet dependencies:**

- `LLamaSharp`
- `LLamaSharp.Backend.Cpu` (or `LLamaSharp.Backend.Cuda12` if GPU available)

### `LlmCheckService` — `ScreensView.Viewer/Services/LlmCheckService.cs`

Runs LLM analysis, independent of screenshot polling. Follows the same interface shape as `IScreenshotPollerService` — collection is passed to `Start()`, not to the constructor.

**Accessing the collection:** `LlmCheckService` receives `IReadOnlyList<ComputerViewModel>` at `Start()` time (a snapshot reference to `MainViewModel.Computers`). All reads of `vm.Screenshot` and `vm.Description` happen on the background thread — these are simple property reads, no WPF Dispatcher needed. Writes back to `vm.IsLlmChecking` and `vm.LastLlmCheck` must go through `Dispatcher.Invoke` (same pattern as `ScreenshotPollerService`).

**Interval behavior:** the interval is **between cycles**, not wall-clock. After each cycle completes (however long it took), wait N minutes, then start the next cycle. Implemented with `Task.Delay` after cycle completion, not a periodic `Timer`. This prevents overlapping cycles regardless of how many computers there are.

**Cycle logic:**

```text
loop:
    await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stopCts.Token)  // read volatile field each cycle
    take snapshot: computers = Start()-provided collection
                              .Where(vm => vm.IsEnabled && !string.IsNullOrEmpty(vm.Description))
                              .ToList()               // snapshot on Dispatcher thread
    for each vm in snapshot sequentially:
        screenshotCopy = read vm.Screenshot           // simple property read, no Dispatcher
        if screenshotCopy == null → skip
        descriptionAtStart = vm.Description           // capture before inference starts
        if string.IsNullOrEmpty(descriptionAtStart) → skip  // may have been cleared since snapshot
        set vm.IsLlmChecking = true  [Dispatcher.Invoke]
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60))
        try:
            result = await _inference.AnalyzeAsync(screenshotCopy, descriptionAtStart, cts.Token)
            Dispatcher.Invoke:
                if (vm.Description == descriptionAtStart)  // guard: description unchanged
                    vm.LastLlmCheck = result
                // else: description changed during inference — discard stale result
        catch:
            Dispatcher.Invoke:
                if (vm.Description == descriptionAtStart)
                    vm.LastLlmCheck = new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now)
        finally:
            Dispatcher.Invoke: vm.IsLlmChecking = false  // always reset
```

**Description-change guard:** `descriptionAtStart` is captured synchronously before `AnalyzeAsync`. After inference completes (up to 60s later), the result is written only if `vm.Description` still equals `descriptionAtStart`. If the user cleared or changed the description during inference, the result is silently discarded — `IsLlmChecking` is still reset to false. This prevents stale results from appearing after a description edit.

The `computers.ToList()` snapshot is taken via `Dispatcher.Invoke` to safely read from the WPF `ObservableCollection` (it lives on the UI thread). The loop itself runs on a background thread.

**Lifecycle:**

- `Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)` — stores ref, starts loop; if already running, does nothing (idempotent).
- `UpdateInterval(int intervalMinutes)` — atomically updates the interval field (`volatile int` or `Interlocked`). The running loop reads the field at the start of each `Task.Delay`, so the new interval takes effect after the current cycle finishes. No restart, no overlap.
- `Stop()` — synchronous, cancels `stopCts` (fire-and-forget). The in-flight 60s inference may run briefly past `Stop()` — acceptable given the cap.
- Does NOT auto-start. Called explicitly after `ModelDownloadService.ModelReady` fires.

---

## Integration / Wiring — `App.xaml.cs` `OnStartup`

`MainViewModel` owns `CancellationTokenSource _appCts = new()` as a private field. Expose its token via `CancellationToken AppToken => _appCts.Token`.

Construction order (mainViewModel must be constructed before download starts):

```csharp
var downloadService = new ModelDownloadService();
var inferenceService = new LlmInferenceService(downloadService);
var llmCheckService = new LlmCheckService(inferenceService);

var mainViewModel = new MainViewModel(..., llmCheckService, downloadService);

// Wire auto-start before starting download
downloadService.ModelReady += (_, _) =>
    llmCheckService.Start(mainViewModel.Computers, mainViewModel.LlmCheckIntervalMinutes);

if (downloadService.IsModelReady)
    llmCheckService.Start(mainViewModel.Computers, mainViewModel.LlmCheckIntervalMinutes);
else
{
    var progress = new Progress<double>(p => mainViewModel.ModelDownloadProgress = p);
    _ = downloadService.DownloadAsync(progress, mainViewModel.AppToken);
}
```

`ModelDownloadProgress` is a `double` property on `MainViewModel` (for UI binding), initialized to `-1` (sentinel = not downloading, progress bar hidden).

The download is started from an `async void` method in `App.xaml.cs` (not a bare fire-and-forget `_ = ...`) so that success/error/cancel can reset the progress bar and surface errors:

```csharp
async void StartModelDownloadAsync()
{
    var progress = new Progress<double>(p => mainViewModel.ModelDownloadProgress = p);
    try
    {
        await downloadService.DownloadAsync(progress, mainViewModel.AppToken);
        // ModelReady fires inside DownloadAsync on success → LlmCheckService.Start() called
        mainViewModel.ModelDownloadProgress = -1;   // hide progress bar
    }
    catch (OperationCanceledException)
    {
        mainViewModel.ModelDownloadProgress = -1;   // hide on cancel (app closing)
    }
    catch (Exception ex)
    {
        mainViewModel.ModelDownloadProgress = -1;   // hide on error
        // Use the existing _reportError delegate that App.xaml.cs already passes to MainViewModel
        // (Action<string,string> reportError in the internal constructor).
        // Expose it as an internal method on MainViewModel:
        //   internal void ReportDownloadError(string message) => ReportError("Model download", message);
        mainViewModel.ReportDownloadError(ex.Message);
    }
}
```

Replace the bare `_ = downloadService.DownloadAsync(...)` line in the wiring section above with `StartModelDownloadAsync()`.

`MainViewModel` constructor adds:

```csharp
ILlmCheckService llmCheckService,
IModelDownloadService downloadService
```

**`MainViewModel.Dispose()`** — add to the **existing** `_poller.Dispose()` call, not replace it:

```csharp
public void Dispose()
{
    _poller.Dispose();           // existing — keep
    _llmCheckService.Stop();     // new
    _appCts.Cancel();            // new — stops in-progress download
    _appCts.Dispose();           // new
}
```

---

## `AddEditComputerWindow` — Data Round-Trip

### `AddEditComputerWindow.xaml.cs`

Add `TextBox` bound to `Description`. In `Ok_Click`, the result `ComputerConfig` must include:

```csharp
var desc = DescriptionTextBox.Text.Trim();
Description = desc.Length > 0 ? desc : null,
```

### `MainViewModel.UpdateComputer`

Add to the existing property assignment block:

```csharp
vm.Description = config.Description;
vm.LastLlmCheck = null; // reset stale result when description changes
```

---

## Settings — `ViewerSettingsService`

Add to `ViewerSettings`:

```csharp
public int LlmCheckIntervalMinutes { get; set; } = 5;
```

In `MainViewModel` constructor, load the value the same way `RefreshInterval` is loaded (load → normalize → write back if changed):

```csharp
_llmCheckIntervalMinutes = NormalizeLlmCheckInterval(_viewerSettings.LlmCheckIntervalMinutes);
_viewerSettings.LlmCheckIntervalMinutes = _llmCheckIntervalMinutes;
```

Add `[ObservableProperty] private int _llmCheckIntervalMinutes = 5;` alongside the existing `_refreshInterval`.

In `MainViewModel`, add handler `OnLlmCheckIntervalChanged` (mirrors existing `OnRefreshIntervalChanged`):

```csharp
partial void OnLlmCheckIntervalChanged(int value)
{
    var normalized = NormalizeLlmCheckInterval(value);
    if (value != normalized) { LlmCheckIntervalMinutes = normalized; return; }

    _viewerSettings.LlmCheckIntervalMinutes = value;   // update before saving
    _viewerSettingsService.Save(_viewerSettings);

    // Update interval without restarting the loop — no overlap possible.
    // If model is not ready yet, the saved value will be read by Start()
    // when ModelReady fires.
    _llmCheckService.UpdateInterval(value);
}
```

`NormalizeLlmCheckInterval`: clamp to 1–60 minutes, default 5.

---

## UI Changes

### `MainWindow.xaml` — tile card

Add `Border` overlay driven by converters on `LastLlmCheck`:

- `IsError = true` → transparent (error ≠ mismatch, no false alarm)
- `IsMatch = true` → 2px green border
- `IsMatch = false` and not error → 3px orange border
- `LastLlmCheck = null` or `IsLlmChecking = true` → transparent

Add `ToolTip` to tile card:

- Checking: `"LLM: analysing..."`
- Match: `"LLM: Match — {Explanation}"`
- Mismatch: `"LLM: Mismatch — {Explanation}"`
- Error: `"LLM: Error — {Explanation}"`
- Null: no tooltip

Note: model explanation will be in English (model prompted in English for accuracy). Tooltip labels kept in English for consistency.

### `MainWindow.xaml` — toolbar

Download progress bar (visible only while downloading):

```text
[ Downloading model: 47% ████████░░ ]
```

Bound to `MainViewModel.ModelDownloadProgress` (`double` 0–100). Hidden when `ModelDownloadProgress < 0`.

LLM interval control (next to screenshot interval slider):

```text
LLM interval: [5] min
```

Bound to `MainViewModel.LlmCheckIntervalMinutes`, triggers `OnLlmCheckIntervalChanged`.

### `AddEditComputerWindow.xaml`

New multiline `TextBox` at bottom of form:

- Label: `"Screen description (optional)"`
- Placeholder: `"What should be visible on this computer's screen?"`
- Height: 3 lines (`AcceptsReturn="True"`, `TextWrapping="Wrap"`)
- Bound to `Description`

---

## Unit Tests

New test class `LlmCheckResultTests` and additions to existing test classes. Target project: `ScreensView.Tests`.

### `ComputerViewModelTests` — additions

- **Description round-trip:** construct `ComputerViewModel(new ComputerConfig { Description = "X" })`, assert `vm.Description == "X"`, call `vm.ToConfig()`, assert `config.Description == "X"`
- **Description null round-trip:** same with `Description = null`
- **LastLlmCheck reset on Description cleared:** set `vm.Description = "X"`, set `vm.LastLlmCheck = someResult`, set `vm.Description = ""`, assert `vm.LastLlmCheck == null`
- **LastLlmCheck not reset when Description set to non-empty:** set `vm.Description = "X"`, set result, set `vm.Description = "Y"`, assert result still set (only clearing resets it — up to implementation decision, document either way)

### `MainViewModelTests` — additions

- **UpdateComputer propagates Description:** call `UpdateComputer(vm, configWithDescription)`, assert `vm.Description == config.Description`
- **UpdateComputer resets LastLlmCheck:** set stale result, call `UpdateComputer`, assert `vm.LastLlmCheck == null`
- **LlmCheckIntervalMinutes saved on change:** set interval, assert `_viewerSettingsService.Save` was called with updated value
- **Dispose calls Stop on LlmCheckService:** mock `ILlmCheckService`, call `mainViewModel.Dispose()`, assert `Stop()` was called

### `ModelDownloadServiceTests` — new class

These tests require injectable seams. `ModelDownloadService` must expose an `internal` constructor:

```csharp
internal ModelDownloadService(HttpClient httpClient, string basePath)
```

The production constructor `ModelDownloadService()` calls this with `new HttpClient()` and `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreensView", "models")`.

`basePath` controls where `.gguf` and `.part` files are written. Tests use `Path.GetTempPath()` + a per-test GUID subfolder (created in test setup, deleted in teardown).

`HttpClient` is injected to allow a fake `HttpMessageHandler` that returns controlled response streams and `Content-Length` headers.

Test cases:

- **IsModelReady false when only .part exists:** create `.part` file in basePath, assert `IsModelReady == false`
- **IsModelReady false when neither file exists:** assert `IsModelReady == false`
- **IsModelReady true when final file exists and no .part:** create final file, assert `IsModelReady == true`
- **Download resumes via Range header:** create a `.part` file of N bytes in basePath; fake handler returns HTTP 206; assert the outgoing request contains `Range: bytes=N-`
- **Cancellation leaves .part file:** fake handler streams data slowly; cancel after first chunk; assert `.part` file still present, `IsModelReady == false`
- **Successful download fires ModelReady and renames .part:** fake handler returns full content; await `DownloadAsync`; assert final file exists, `.part` file absent, `ModelReady` event was raised

---

## Verification (Manual)

1. Add a computer with a non-empty Description
2. Wait for a screenshot to be captured
3. After LLM interval elapses:
   - `IsLlmChecking` briefly true → tooltip shows "analysing..."
   - `LastLlmCheck` populated → tile shows green or orange border
4. Change Description to something obviously wrong → after next cycle, tile shows orange border
5. Clear Description → `LastLlmCheck` resets to null immediately, border disappears
6. **Model download:** delete `%AppData%\ScreensView\models\`, restart app → progress bar appears, model downloads, LLM checks start automatically. App usable during download.
7. **Interrupted download:** kill app mid-download → `.part` file remains; on restart, resumes from offset
8. **Inference timeout:** slow machine where inference exceeds 60s → `LastLlmCheck.IsError = true`, no orange border
9. **App shutdown mid-check:** app closes cleanly within seconds — `Stop()` cancels inference
10. Computers without Description are never processed
11. **Many computers:** with 10 computers at 60s each, cycle takes ~10 min; next cycle starts only after previous finishes — no skipped cycles, no overlaps
12. **Description changed during inference:** change Description on a computer while its `IsLlmChecking = true`; after inference completes, `LastLlmCheck` stays as it was before the check started (result discarded)
13. **Download error:** disconnect network mid-download → progress bar hides, error message shown via standard error dialog; no crash, no stuck progress bar
