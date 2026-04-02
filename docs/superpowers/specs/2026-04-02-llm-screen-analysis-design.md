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
- Progress reported via `IProgress<double>`
- **`IsModelReady`**: returns true only if the final file exists **and** no `.part` file is present. Does not check file size (HuggingFace CDN content-length is used for progress only).
- Accepts `CancellationToken`; on cancellation, leaves the `.part` file for next-startup resume
- Exposes `event EventHandler ModelReady` — fired once when model becomes ready (used to trigger `LlmCheckService.Start()`)

Interface:

```csharp
bool IsModelReady { get; }
string ModelPath { get; }
event EventHandler ModelReady;
Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
```

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

**Fallback strategy:** LLamaSharp multimodal (LLavaWeights) support is experimental. If it proves incompatible with Qwen3.5 GGUF, **commit to Ollama instead** (simpler than bundling llama-server.exe). The service interface stays unchanged; only the internals swap to `HttpClient` calls to `localhost:11434`. The spec does not require bundling llama-server.exe.

**NuGet dependencies:**

- `LLamaSharp`
- `LLamaSharp.Backend.Cpu` (or `LLamaSharp.Backend.Cuda12` if GPU available)

### `LlmCheckService` — `ScreensView.Viewer/Services/LlmCheckService.cs`

Runs LLM analysis on a configurable timer, independent of screenshot polling.

**Interval:** from `ViewerSettingsService.Settings.LlmCheckIntervalMinutes` (default: 5).

**Loop logic (per cycle):**

```text
if model not ready → skip
if already running a check cycle → skip
collect enabled computers where Description is not null/empty
for each computer sequentially:
    take snapshot of vm.Screenshot (may be slightly stale for later computers — acceptable in v1)
    if snapshot == null → skip
    set vm.IsLlmChecking = true  [via Dispatcher]
    try:
        result = await _inference.AnalyzeAsync(snapshot, vm.Description, cts.Token)
        vm.LastLlmCheck = result
    catch:
        vm.LastLlmCheck = new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now)
    finally:
        vm.IsLlmChecking = false  [via Dispatcher — always reset]
```

Per-call timeout: **60 seconds** — passed as `CancellationToken` from `CancellationTokenSource` with 60s timeout, created fresh per computer.

**Lifecycle:**

- `Start()` — starts the timer loop
- `Stop()` — synchronous, cancels the current cycle's `CancellationToken` (fire-and-forget cancel, does not await the in-flight task). The in-flight inference may run briefly beyond `Stop()` — this is acceptable given the 60s timeout cap.
- `LlmCheckService` does NOT auto-start. It starts only after `ModelDownloadService.ModelReady` fires.

---

## Integration / Wiring — `App.xaml.cs` `OnStartup`

Construction order (mainViewModel must be constructed before download starts):

```csharp
var downloadService = new ModelDownloadService();
var inferenceService = new LlmInferenceService(downloadService);
var llmCheckService = new LlmCheckService(inferenceService, settingsService);

// Construct mainViewModel first — download progress binding requires it
var mainViewModel = new MainViewModel(..., llmCheckService, downloadService);

// Wire auto-start before starting download
downloadService.ModelReady += (_, _) => llmCheckService.Start();

// If model already ready on startup (subsequent runs), start immediately
if (downloadService.IsModelReady)
    llmCheckService.Start();
else
    _ = downloadService.DownloadAsync(mainViewModel.ModelDownloadProgress, mainViewModel.AppToken);
```

`MainViewModel` constructor adds:

```csharp
ILlmCheckService llmCheckService,
IModelDownloadService downloadService
```

`MainViewModel` owns `CancellationTokenSource _appCts = new()` as a private field. Expose its token via a property `CancellationToken AppToken => _appCts.Token` for the wiring call. `Dispose()`:

```csharp
_appCts.Cancel();        // stops in-progress download (token was passed to DownloadAsync)
_appCts.Dispose();
_llmCheckService.Stop(); // signals check cycle to stop (fire-and-forget cancel)
```

`ModelDownloadService` interface does **not** expose a `Cancel()` method — cancellation is entirely through the `CancellationToken` passed to `DownloadAsync`.

`ModelDownloadService` download is started in `App.OnStartup` if model not ready. The main window shows immediately regardless — no blocking on download.

---

## `AddEditComputerWindow` — Data Round-Trip

### `AddEditComputerWindow.xaml.cs`

Add `TextBox` bound to `Description`. In `Ok_Click`, the result `ComputerConfig` must include:

```csharp
var desc = DescriptionTextBox.Text.Trim();
Description = desc.Length > 0 ? desc : null,
```

### `MainViewModel.UpdateComputer`

When updating an existing `ComputerViewModel` from `AddEditComputerWindow` result, add:

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

In `MainViewModel`, add handler `OnLlmCheckIntervalChanged` (mirrors existing `OnRefreshIntervalChanged`) that:

1. Updates `_llmCheckService.IntervalMinutes`
2. Calls `_settingsService.Save()`

---

## UI Changes

### `MainWindow.xaml` — tile card

Add `Border` overlay:

- `IsError = true` → no colored border (failure ≠ mismatch)
- `IsMatch = true` → 2px green border
- `IsMatch = false` (and not error) → 3px orange border
- `LastLlmCheck = null` or `IsLlmChecking = true` → transparent border

Add `ToolTip` to tile card:

- Checking: `"LLM: analysing..."`
- Match: `"LLM: Match — {Explanation}"`
- Mismatch: `"LLM: Mismatch — {Explanation}"`
- Error: `"LLM: Error — {Explanation}"`
- Null: no tooltip

Note: Model explanation will be in English (model prompted in English for accuracy). Tooltip labels kept in English for consistency.

### `MainWindow.xaml` — toolbar

Download progress bar (visible only while downloading):

```text
[ Downloading model: 47% ████████░░ ]
```

Bound to `MainViewModel.ModelDownloadProgress` (`double` 0–100). Hidden when `ModelDownloadProgress < 0` (sentinel = not downloading).

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

## Verification

1. Add a computer with a non-empty Description
2. Wait for a screenshot to be captured
3. After configured LLM interval fires:
   - `IsLlmChecking` briefly becomes true → tooltip shows "analysing..."
   - `LastLlmCheck` is populated → tile shows green or orange border + tooltip text
4. Change Description to something obviously wrong → after next interval, tile shows orange border
5. Clear Description → `LastLlmCheck` resets to null, border disappears immediately
6. **Model download flow:** delete `%AppData%\ScreensView\models\`, restart app → download progress bar appears, model downloads, then LLM checks begin automatically. App window is fully usable during download.
7. **Interrupted download:** kill app mid-download → `.part` file remains. On restart, download resumes from where it left off.
8. **Inference timeout:** if analysis exceeds 60 seconds, `LastLlmCheck.IsError = true`, no orange border shown
9. **App shutdown mid-check:** app closes cleanly without hanging — `Stop()` cancels in-flight inference
10. Computers without Description are never processed by `LlmCheckService`
