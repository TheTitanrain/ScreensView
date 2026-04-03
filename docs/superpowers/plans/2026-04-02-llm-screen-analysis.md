# LLM Screen Analysis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local multimodal LLM (Qwen3.5-2B-GGUF via LLamaSharp) that periodically compares each computer's screenshot to a user-defined description and flags mismatches on the tile with a colored border and tooltip.

**Architecture:** `LlmCheckService` runs an independent `Task.Delay`-based loop (interval between cycles, not wall-clock) that iterates enabled computers with non-empty descriptions sequentially, calling `LlmInferenceService.AnalyzeAsync` for each. `ModelDownloadService` downloads the GGUF file on first use to `%AppData%\ScreensView\models\`, fires `ModelReady` on completion, and supports resume via `.part` file. Results are written back to `ComputerViewModel.LastLlmCheck` with a stale-result guard that discards results if the description changed during inference.

**Tech Stack:** C# 12, .NET 8, WPF, CommunityToolkit.Mvvm 8.4.0, xUnit 2.9.2, LLamaSharp (NuGet), LLamaSharp.Backend.Cpu (NuGet)

---

## File Map

**New files:**
- `ScreensView.Viewer/Models/LlmCheckResult.cs` — immutable result record
- `ScreensView.Viewer/Services/LlmInferenceService.cs` — `ILlmInferenceService` interface + stub impl
- `ScreensView.Viewer/Services/ModelDownloadService.cs` — `IModelDownloadService` + download impl
- `ScreensView.Viewer/Services/LlmCheckService.cs` — `ILlmCheckService` + check loop impl

**Modified files:**
- `ScreensView.Shared/Models/ComputerConfig.cs` — add `Description`
- `ScreensView.Viewer/Services/ViewerSettingsService.cs` — add `LlmCheckIntervalMinutes` to `ViewerSettings`
- `ScreensView.Viewer/ViewModels/ComputerViewModel.cs` — add `Description`, `LastLlmCheck`, `IsLlmChecking`
- `ScreensView.Viewer/ViewModels/MainViewModel.cs` — add LLM service wiring, `ModelDownloadProgress`, interval handling, `ReportDownloadError`
- `ScreensView.Viewer/Views/AddEditComputerWindow.xaml` — add Description TextBox
- `ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs` — populate/read Description
- `ScreensView.Viewer/MainWindow.xaml` — LLM border overlay, tooltip, progress bar, interval slider
- `ScreensView.Viewer/App.xaml.cs` — construct and wire new services, `StartModelDownloadAsync`
- `ScreensView.Viewer/ScreensView.Viewer.csproj` — add LLamaSharp NuGet packages
- `ScreensView.Tests/ComputerViewModelTests.cs` — Description round-trip tests
- `ScreensView.Tests/MainViewModelTests.cs` — interval, Dispose, UpdateComputer tests
- `ScreensView.Tests/ModelDownloadServiceTests.cs` — new test class

---

## Task 1: LlmCheckResult model + ComputerConfig.Description

**Files:**
- Modify: `ScreensView.Shared/Models/ComputerConfig.cs`
- Create: `ScreensView.Viewer/Models/LlmCheckResult.cs`
- Modify: `ScreensView.Tests/ComputerViewModelTests.cs` (description config tests added in Task 2 — skip here)

- [ ] **Step 1: Add Description to ComputerConfig**

  In `ScreensView.Shared/Models/ComputerConfig.cs`, add after `CertThumbprint`:

  ```csharp
  public string? Description { get; set; }
  ```

- [ ] **Step 2: Create LlmCheckResult**

  Create `ScreensView.Viewer/Models/LlmCheckResult.cs`:

  ```csharp
  namespace ScreensView.Viewer.Models;

  public record LlmCheckResult(
      bool IsMatch,
      string Explanation,
      bool IsError,
      DateTime CheckedAt
  );
  ```

- [ ] **Step 3: Build to confirm no regressions**

  ```bash
  dotnet build
  ```

  Expected: build succeeds (Description is nullable — existing code unaffected).

- [ ] **Step 4: Commit**

  ```bash
  git add ScreensView.Shared/Models/ComputerConfig.cs ScreensView.Viewer/Models/LlmCheckResult.cs
  git commit -m "feat: add ComputerConfig.Description and LlmCheckResult model"
  ```

---

## Task 2: ComputerViewModel — Description, LastLlmCheck, IsLlmChecking

**Files:**
- Modify: `ScreensView.Viewer/ViewModels/ComputerViewModel.cs`
- Modify: `ScreensView.Tests/ComputerViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

  Add to `ScreensView.Tests/ComputerViewModelTests.cs` (after existing tests):

  ```csharp
  [Fact]
  public void Constructor_MapsDescriptionFromConfig()
  {
      var config = MakeConfig(c => c.Description = "Desktop with Office icons");
      var vm = new ComputerViewModel(config);
      Assert.Equal("Desktop with Office icons", vm.Description);
  }

  [Fact]
  public void Constructor_NullDescription_MapsNull()
  {
      var config = MakeConfig(c => c.Description = null);
      var vm = new ComputerViewModel(config);
      Assert.Null(vm.Description);
  }

  [Fact]
  public void ToConfig_IncludesDescription()
  {
      var config = MakeConfig(c => c.Description = "Server room monitor");
      var vm = new ComputerViewModel(config);
      Assert.Equal("Server room monitor", vm.ToConfig().Description);
  }

  [Fact]
  public void ToConfig_NullDescription_RoundTrips()
  {
      var vm = new ComputerViewModel(MakeConfig(c => c.Description = null));
      Assert.Null(vm.ToConfig().Description);
  }

  [Fact]
  public void Description_WhenCleared_ResetsLastLlmCheck()
  {
      var vm = new ComputerViewModel(MakeConfig(c => c.Description = "some desc"));
      vm.LastLlmCheck = new LlmCheckResult(true, "ok", false, DateTime.Now);

      vm.Description = string.Empty;

      Assert.Null(vm.LastLlmCheck);
  }

  [Fact]
  public void Description_WhenSetToNull_ResetsLastLlmCheck()
  {
      var vm = new ComputerViewModel(MakeConfig(c => c.Description = "some desc"));
      vm.LastLlmCheck = new LlmCheckResult(true, "ok", false, DateTime.Now);

      vm.Description = null;

      Assert.Null(vm.LastLlmCheck);
  }

  [Fact]
  public void Description_WhenChangedToNonEmpty_DoesNotResetLastLlmCheck()
  {
      var vm = new ComputerViewModel(MakeConfig(c => c.Description = "A"));
      var result = new LlmCheckResult(true, "ok", false, DateTime.Now);
      vm.LastLlmCheck = result;

      vm.Description = "B";

      Assert.Same(result, vm.LastLlmCheck);
  }
  ```

  Add `using ScreensView.Viewer.Models;` at the top of the test file.

- [ ] **Step 2: Run tests to confirm they fail**

  ```bash
  dotnet test --filter "ComputerViewModelTests" -- --no-build
  ```

  Expected: compile error (Description, LastLlmCheck not yet on ComputerViewModel).

- [ ] **Step 3: Implement in ComputerViewModel**

  In `ScreensView.Viewer/ViewModels/ComputerViewModel.cs`:

  Add using:
  ```csharp
  using ScreensView.Viewer.Models;
  ```

  Add observable properties after `_lastUpdated`:
  ```csharp
  [ObservableProperty] private string? _description;
  [ObservableProperty] private LlmCheckResult? _lastLlmCheck;
  [ObservableProperty] private bool _isLlmChecking;
  ```

  Add partial method to reset `LastLlmCheck` when description is cleared:
  ```csharp
  partial void OnDescriptionChanged(string? value)
  {
      if (string.IsNullOrEmpty(value))
          LastLlmCheck = null;
  }
  ```

  In the constructor, after `ApplyEnabledState(_isEnabled)`:
  ```csharp
  _description = config.Description;
  ```

  In `ToConfig()`, add to the initializer:
  ```csharp
  Description = Description,
  ```

- [ ] **Step 4: Run tests to confirm they pass**

  ```bash
  dotnet test --filter "ComputerViewModelTests"
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add ScreensView.Viewer/ViewModels/ComputerViewModel.cs ScreensView.Tests/ComputerViewModelTests.cs
  git commit -m "feat: add Description/LastLlmCheck/IsLlmChecking to ComputerViewModel"
  ```

---

## Task 3: ViewerSettings — LlmCheckIntervalMinutes

**Files:**
- Modify: `ScreensView.Viewer/Services/ViewerSettingsService.cs`
- Modify: `ScreensView.Tests/ViewerSettingsServiceTests.cs`

- [ ] **Step 1: Write failing test**

  Open `ScreensView.Tests/ViewerSettingsServiceTests.cs` and read existing tests for context, then add:

  ```csharp
  [Fact]
  public void LlmCheckIntervalMinutes_DefaultValue_IsFive()
  {
      var settings = new ViewerSettings();
      Assert.Equal(5, settings.LlmCheckIntervalMinutes);
  }

  [Fact]
  public void LlmCheckIntervalMinutes_PersistsAcrossSaveLoad()
  {
      var path = Path.GetTempFileName();
      try
      {
          var svc = new ViewerSettingsService(path);
          var settings = svc.Load();
          settings.LlmCheckIntervalMinutes = 15;
          svc.Save(settings);

          var loaded = svc.Load();
          Assert.Equal(15, loaded.LlmCheckIntervalMinutes);
      }
      finally { File.Delete(path); }
  }
  ```

- [ ] **Step 2: Run to confirm failure**

  ```bash
  dotnet test --filter "ViewerSettingsServiceTests"
  ```

  Expected: compile error (property not yet on ViewerSettings).

- [ ] **Step 3: Add property to ViewerSettings**

  In `ScreensView.Viewer/Services/ViewerSettingsService.cs`, add to the `ViewerSettings` class:

  ```csharp
  public int LlmCheckIntervalMinutes { get; set; } = 5;
  ```

- [ ] **Step 4: Run to confirm pass**

  ```bash
  dotnet test --filter "ViewerSettingsServiceTests"
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add ScreensView.Viewer/Services/ViewerSettingsService.cs ScreensView.Tests/ViewerSettingsServiceTests.cs
  git commit -m "feat: add LlmCheckIntervalMinutes to ViewerSettings"
  ```

---

## Task 4: ILlmInferenceService interface + NuGet packages

**Files:**
- Create: `ScreensView.Viewer/Services/LlmInferenceService.cs`
- Modify: `ScreensView.Viewer/ScreensView.Viewer.csproj`

The LLamaSharp vision API is experimental — a full implementation is deferred. This task wires the interface so all other code can compile and be tested with a fake. The real implementation is added in Task 11.

- [ ] **Step 1: Add LLamaSharp NuGet packages**

  In `ScreensView.Viewer/ScreensView.Viewer.csproj`, add inside the existing `<ItemGroup>` with PackageReferences:

  ```xml
  <PackageReference Include="LLamaSharp" Version="0.20.0" />
  <PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.20.0" />
  ```

  Then restore:

  ```bash
  dotnet restore ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: packages downloaded successfully.

  > **Note:** Check [nuget.org/packages/LLamaSharp](https://www.nuget.org/packages/LLamaSharp) for the latest stable version and update accordingly.

- [ ] **Step 2: Create ILlmInferenceService with stub**

  Create `ScreensView.Viewer/Services/LlmInferenceService.cs`:

  ```csharp
  using System.Windows.Media.Imaging;
  using ScreensView.Viewer.Models;

  namespace ScreensView.Viewer.Services;

  public interface ILlmInferenceService
  {
      Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct);
  }

  /// <summary>
  /// LLamaSharp-based multimodal inference. Vision support is experimental —
  /// this stub throws until a compatible model/backend is confirmed.
  /// See Task 11 in the implementation plan to complete this.
  /// </summary>
  public class LlmInferenceService : ILlmInferenceService, IDisposable
  {
      private readonly IModelDownloadService _download;

      public LlmInferenceService(IModelDownloadService download)
      {
          _download = download;
      }

      public Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct)
      {
          // TODO: implement LLamaSharp vision inference (Task 11)
          // Verify LLavaWeights compatibility with Qwen3.5 GGUF before implementing.
          throw new NotImplementedException("LlmInferenceService is not yet implemented. See Task 11.");
      }

      public void Dispose() { }
  }
  ```

- [ ] **Step 3: Build to confirm no errors**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: build succeeds.

- [ ] **Step 4: Commit**

  ```bash
  git add ScreensView.Viewer/Services/LlmInferenceService.cs ScreensView.Viewer/ScreensView.Viewer.csproj
  git commit -m "feat: add ILlmInferenceService interface and LLamaSharp packages (stub impl)"
  ```

---

## Task 5: ModelDownloadService

**Files:**
- Create: `ScreensView.Viewer/Services/ModelDownloadService.cs`
- Create: `ScreensView.Tests/ModelDownloadServiceTests.cs`

- [ ] **Step 1: Write failing tests**

  Create `ScreensView.Tests/ModelDownloadServiceTests.cs`:

  ```csharp
  using System.Net;
  using System.Text;
  using ScreensView.Viewer.Services;

  namespace ScreensView.Tests;

  public class ModelDownloadServiceTests : IDisposable
  {
      private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

      public ModelDownloadServiceTests() => Directory.CreateDirectory(_tempDir);

      public void Dispose()
      {
          if (Directory.Exists(_tempDir))
              Directory.Delete(_tempDir, recursive: true);
      }

      private ModelDownloadService Make(HttpMessageHandler? handler = null)
          => new(handler ?? new NoOpHandler(), _tempDir);

      // ---- IsModelReady ----

      [Fact]
      public void IsModelReady_WhenNoFilesExist_ReturnsFalse()
      {
          Assert.False(Make().IsModelReady);
      }

      [Fact]
      public void IsModelReady_WhenOnlyPartFileExists_ReturnsFalse()
      {
          File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), "partial");
          Assert.False(Make().IsModelReady);
      }

      [Fact]
      public void IsModelReady_WhenFinalFileExistsAndNoPartFile_ReturnsTrue()
      {
          File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf"), "model");
          Assert.True(Make().IsModelReady);
      }

      [Fact]
      public void IsModelReady_WhenBothFilesExist_ReturnsFalse()
      {
          File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf"), "model");
          File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), "partial");
          Assert.False(Make().IsModelReady);
      }

      // ---- Download writes file ----

      [Fact]
      public async Task DownloadAsync_OnSuccess_WritesFileAndFiresModelReady()
      {
          var content = "fake-model-bytes"u8.ToArray();
          var handler = new FakeHandler(HttpStatusCode.OK, content);
          var svc = Make(handler);
          bool modelReadyFired = false;
          svc.ModelReady += (_, _) => modelReadyFired = true;

          await svc.DownloadAsync(new Progress<double>(), CancellationToken.None);

          Assert.True(svc.IsModelReady);
          Assert.True(modelReadyFired);
          Assert.False(File.Exists(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part")));
      }

      // ---- Resume via Range header ----

      [Fact]
      public async Task DownloadAsync_WhenPartFileExists_SendsRangeHeader()
      {
          var existingBytes = "existing"u8.ToArray();
          File.WriteAllBytes(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), existingBytes);

          string? rangeHeader = null;
          var remaining = "more"u8.ToArray();
          var handler = new CapturingHandler(HttpStatusCode.PartialContent, remaining,
              req => rangeHeader = req.Headers.Range?.ToString());
          var svc = Make(handler);

          await svc.DownloadAsync(new Progress<double>(), CancellationToken.None);

          Assert.Equal($"bytes={existingBytes.Length}-", rangeHeader);
      }

      // ---- Cancellation leaves .part ----

      [Fact]
      public async Task DownloadAsync_WhenCancelled_LeavesPartFile()
      {
          var cts = new CancellationTokenSource();
          // Handler cancels after first byte
          var handler = new CancellingHandler(cts, firstChunk: "first"u8.ToArray());
          var svc = Make(handler);

          await Assert.ThrowsAsync<OperationCanceledException>(
              () => svc.DownloadAsync(new Progress<double>(), cts.Token));

          Assert.True(File.Exists(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part")));
          Assert.False(svc.IsModelReady);
      }

      // ---- Helpers ----

      private class NoOpHandler : HttpMessageHandler
      {
          protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
              => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new ByteArrayContent([])
              });
      }

      private class FakeHandler(HttpStatusCode status, byte[] content) : HttpMessageHandler
      {
          protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
              => Task.FromResult(new HttpResponseMessage(status)
              {
                  Content = new ByteArrayContent(content)
              });
      }

      private class CapturingHandler(HttpStatusCode status, byte[] content, Action<HttpRequestMessage> capture)
          : HttpMessageHandler
      {
          protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
          {
              capture(req);
              return Task.FromResult(new HttpResponseMessage(status)
              {
                  Content = new ByteArrayContent(content)
              });
          }
      }

      // Returns a real HTTP 200 response whose content stream writes firstChunk,
      // then cancels before writing the second chunk — ensuring .part is written
      // before the OperationCanceledException propagates.
      private class CancellingHandler(CancellationTokenSource cts, byte[] firstChunk) : HttpMessageHandler
      {
          protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
          {
              var stream = new CancelAfterFirstChunkStream(cts, firstChunk);
              var response = new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StreamContent(stream)
              };
              response.Content.Headers.ContentLength = firstChunk.Length + 4L; // pretend more to come
              return Task.FromResult(response);
          }
      }

      private class CancelAfterFirstChunkStream(CancellationTokenSource cts, byte[] chunk) : Stream
      {
          private bool _chunkSent;

          public override bool CanRead => true;
          public override bool CanSeek => false;
          public override bool CanWrite => false;
          public override long Length => throw new NotSupportedException();
          public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
          public override void Flush() { }
          public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
          public override void SetLength(long value) => throw new NotSupportedException();
          public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

          public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
          {
              if (_chunkSent)
              {
                  // Cancel after writing the first chunk so DownloadAsync has already written to .part
                  cts.Cancel();
                  ct.ThrowIfCancellationRequested();
              }
              _chunkSent = true;
              var n = Math.Min(count, chunk.Length);
              Array.Copy(chunk, 0, buffer, offset, n);
              return n;
          }

          public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      }
  }
  ```

- [ ] **Step 2: Run to confirm compile failure**

  ```bash
  dotnet test --filter "ModelDownloadServiceTests"
  ```

  Expected: compile error (ModelDownloadService does not exist yet).

- [ ] **Step 3: Implement ModelDownloadService**

  Create `ScreensView.Viewer/Services/ModelDownloadService.cs`:

  ```csharp
  using System.IO;
  using System.Net.Http;
  using System.Net.Http.Headers;

  namespace ScreensView.Viewer.Services;

  public interface IModelDownloadService
  {
      bool IsModelReady { get; }
      string ModelPath { get; }
      event EventHandler ModelReady;
      Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
  }

  public class ModelDownloadService : IModelDownloadService
  {
      private const string ModelFileName = "qwen3.5-2b-q4_k_m.gguf";
      private const string DownloadUrl =
          "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/qwen3.5-2b-q4_k_m.gguf";

      private readonly HttpClient _http;
      private readonly string _basePath;

      public event EventHandler? ModelReady;

      // Production constructor
      public ModelDownloadService()
          : this(new HttpClient(), Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
              "ScreensView", "models"))
      {
      }

      // Testable constructor — injected HttpClient and path
      internal ModelDownloadService(HttpMessageHandler handler, string basePath)
          : this(new HttpClient(handler), basePath)
      {
      }

      private ModelDownloadService(HttpClient http, string basePath)
      {
          _http = http;
          _basePath = basePath;
          Directory.CreateDirectory(_basePath);
      }

      public string ModelPath => Path.Combine(_basePath, ModelFileName);

      private string PartPath => ModelPath + ".part";

      public bool IsModelReady =>
          File.Exists(ModelPath) && !File.Exists(PartPath);

      public async Task DownloadAsync(IProgress<double> progress, CancellationToken ct)
      {
          var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl);

          long existingBytes = 0;
          if (File.Exists(PartPath))
          {
              existingBytes = new FileInfo(PartPath).Length;
              request.Headers.Range = new RangeHeaderValue(existingBytes, null);
          }

          using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
          response.EnsureSuccessStatusCode();

          var totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;

          await using var stream = await response.Content.ReadAsStreamAsync(ct);
          await using var file = new FileStream(PartPath, FileMode.Append, FileAccess.Write, FileShare.None);

          var buffer = new byte[81920];
          long downloaded = existingBytes;
          int read;

          while ((read = await stream.ReadAsync(buffer, ct)) > 0)
          {
              await file.WriteAsync(buffer.AsMemory(0, read), ct);
              downloaded += read;
              if (totalBytes > 0)
                  progress.Report(downloaded * 100.0 / totalBytes);
          }

          await file.FlushAsync(ct);
          file.Close();

          // Atomic rename: .part → final
          if (File.Exists(ModelPath))
              File.Delete(ModelPath);
          File.Move(PartPath, ModelPath);

          progress.Report(100.0);
          ModelReady?.Invoke(this, EventArgs.Empty);
      }
  }
  ```

- [ ] **Step 4: Run tests to confirm pass**

  ```bash
  dotnet test --filter "ModelDownloadServiceTests"
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add ScreensView.Viewer/Services/ModelDownloadService.cs ScreensView.Tests/ModelDownloadServiceTests.cs
  git commit -m "feat: add ModelDownloadService with resume, .part atomic rename, ModelReady event"
  ```

---

## Task 6: LlmCheckService

**Files:**
- Create: `ScreensView.Viewer/Services/LlmCheckService.cs`
- Create: `ScreensView.Tests/LlmCheckServiceTests.cs`

- [ ] **Step 1: Write failing tests**

  Create `ScreensView.Tests/LlmCheckServiceTests.cs`:

  ```csharp
  using ScreensView.Shared.Models;
  using ScreensView.Viewer.Models;
  using ScreensView.Viewer.Services;
  using ScreensView.Viewer.ViewModels;

  namespace ScreensView.Tests;

  public class LlmCheckServiceTests
  {
      private static ComputerViewModel MakeVm(string? description = "Desktop with Office")
          => new(new ComputerConfig
          {
              Id = Guid.NewGuid(), Name = "PC", Host = "1.2.3.4", Port = 5443,
              ApiKey = "k", IsEnabled = true, Description = description
          });

      // ---- Start is idempotent ----

      [Fact]
      public void Start_CalledTwice_DoesNotThrow()
      {
          var inference = new FakeLlmInferenceService();
          var svc = new LlmCheckService(inference);
          var vms = new List<ComputerViewModel>();

          svc.Start(vms, intervalMinutes: 60);
          svc.Start(vms, intervalMinutes: 60); // second call must be a no-op

          svc.Stop();
      }

      // ---- Stop is safe when not running ----

      [Fact]
      public void Stop_WhenNotStarted_DoesNotThrow()
      {
          var svc = new LlmCheckService(new FakeLlmInferenceService());
          svc.Stop(); // must not throw
      }

      // ---- UpdateInterval updates volatile field ----

      [Fact]
      public void UpdateInterval_WhenNotStarted_DoesNotThrow()
      {
          var svc = new LlmCheckService(new FakeLlmInferenceService());
          svc.UpdateInterval(10); // must not throw
      }

      // ---- Skips computers without description ----

      [Fact]
      public async Task RunCycle_SkipsComputerWithNullDescription()
      {
          var inference = new FakeLlmInferenceService();
          var svc = new LlmCheckService(inference);
          var vm = MakeVm(description: null);

          await svc.RunCycleAsync([vm]);

          Assert.Empty(inference.Calls);
          Assert.Null(vm.LastLlmCheck);
      }

      [Fact]
      public async Task RunCycle_SkipsComputerWithEmptyDescription()
      {
          var inference = new FakeLlmInferenceService();
          var svc = new LlmCheckService(inference);
          var vm = MakeVm(description: "");

          await svc.RunCycleAsync([vm]);

          Assert.Empty(inference.Calls);
      }

      // ---- Skips computers with null screenshot ----

      [Fact]
      public async Task RunCycle_SkipsComputerWithNullScreenshot()
      {
          var inference = new FakeLlmInferenceService();
          var svc = new LlmCheckService(inference);
          var vm = MakeVm("desc");
          // vm.Screenshot is null by default

          await svc.RunCycleAsync([vm]);

          Assert.Empty(inference.Calls);
      }

      // ---- Result is written for matching computer ----

      [Fact]
      public async Task RunCycle_WritesResultToVm()
      {
          var expected = new LlmCheckResult(true, "Looks good", false, DateTime.Now);
          var inference = new FakeLlmInferenceService(expected);
          var svc = new LlmCheckService(inference);
          var vm = MakeVm("desc");
          vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap(); // helper from Task 2 tests

          await svc.RunCycleAsync([vm]);

          Assert.Equal(expected, vm.LastLlmCheck);
          Assert.False(vm.IsLlmChecking);
      }

      // ---- Stale-result guard: description changed during inference ----

      [Fact]
      public async Task RunCycle_WhenDescriptionChangedDuringInference_DiscardsResult()
      {
          var originalDesc = "original";
          LlmCheckResult fakeResult = new(false, "mismatch", false, DateTime.Now);

          // vm must be declared before inference so OnBeforeReturn can capture it
          var vm = MakeVm(originalDesc);
          vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();

          var inference = new FakeLlmInferenceService(fakeResult);
          inference.OnBeforeReturn = () => vm.Description = "changed during inference";
          var svc = new LlmCheckService(inference);

          await svc.RunCycleAsync([vm]);

          // Description changed during inference, so result is discarded
          Assert.Null(vm.LastLlmCheck);
          Assert.False(vm.IsLlmChecking);
      }

      // ---- Error result on inference failure ----

      [Fact]
      public async Task RunCycle_WhenInferenceThrows_WritesErrorResult()
      {
          var inference = new FakeLlmInferenceService(throwException: new InvalidOperationException("model error"));
          var svc = new LlmCheckService(inference);
          var vm = MakeVm("desc");
          vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();

          await svc.RunCycleAsync([vm]);

          Assert.NotNull(vm.LastLlmCheck);
          Assert.True(vm.LastLlmCheck!.IsError);
          Assert.Contains("model error", vm.LastLlmCheck.Explanation);
          Assert.False(vm.IsLlmChecking);
      }

      // ---- IsLlmChecking always reset even on exception ----

      [Fact]
      public async Task RunCycle_IsLlmChecking_AlwaysResetToFalse()
      {
          var inference = new FakeLlmInferenceService(throwException: new Exception("boom"));
          var svc = new LlmCheckService(inference);
          var vm = MakeVm("desc");
          vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();

          await svc.RunCycleAsync([vm]);

          Assert.False(vm.IsLlmChecking);
      }

      // ---- Fake ----

      private class FakeLlmInferenceService : ILlmInferenceService
      {
          private readonly LlmCheckResult? _result;
          private readonly Exception? _throw;

          public List<(string Description, CancellationToken Ct)> Calls { get; } = [];

          // Set OnBeforeReturn to mutate state mid-inference (e.g. change vm.Description)
          public Action? OnBeforeReturn { get; set; }

          public FakeLlmInferenceService(
              LlmCheckResult? result = null,
              Exception? throwException = null)
          {
              _result = result;
              _throw = throwException;
          }

          public Task<LlmCheckResult> AnalyzeAsync(
              System.Windows.Media.Imaging.BitmapImage screenshot,
              string description,
              CancellationToken ct)
          {
              Calls.Add((description, ct));
              if (_throw is not null)
                  throw _throw;
              OnBeforeReturn?.Invoke();
              return Task.FromResult(_result ?? new LlmCheckResult(true, "ok", false, DateTime.Now));
          }
      }
  }
  ```

- [ ] **Step 2: Add `CreateMinimalBitmap` helper to ComputerViewModelTests**

  In `ScreensView.Tests/ComputerViewModelTests.cs`, add this `internal static` helper (alongside the existing `CreateMinimalJpegBase64`):

  ```csharp
  internal static System.Windows.Media.Imaging.BitmapImage CreateMinimalBitmap()
  {
      var base64 = CreateMinimalJpegBase64();
      var bytes = Convert.FromBase64String(base64);
      return RunOnSta(() =>
      {
          var img = new System.Windows.Media.Imaging.BitmapImage();
          using var ms = new System.IO.MemoryStream(bytes);
          img.BeginInit();
          img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
          img.StreamSource = ms;
          img.EndInit();
          img.Freeze();
          return img;
      });
  }
  ```

- [ ] **Step 3: Run tests to confirm compile failure**

  ```bash
  dotnet test --filter "LlmCheckServiceTests"
  ```

  Expected: compile errors (LlmCheckService not yet created).

- [ ] **Step 4: Implement LlmCheckService**

  Create `ScreensView.Viewer/Services/LlmCheckService.cs`:

  ```csharp
  using ScreensView.Viewer.Models;
  using ScreensView.Viewer.ViewModels;

  namespace ScreensView.Viewer.Services;

  public interface ILlmCheckService
  {
      void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes);
      void UpdateInterval(int intervalMinutes);
      void Stop();
  }

  public class LlmCheckService : ILlmCheckService
  {
      private readonly ILlmInferenceService _inference;
      private volatile int _intervalMinutes = 5;
      private CancellationTokenSource? _cts;
      private Task? _loopTask;
      private IReadOnlyList<ComputerViewModel>? _computers;

      public LlmCheckService(ILlmInferenceService inference)
      {
          _inference = inference;
      }

      public void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)
      {
          if (_cts is not null)
              return; // idempotent — already running

          _computers = computers;
          _intervalMinutes = intervalMinutes;
          _cts = new CancellationTokenSource();
          _loopTask = RunLoopAsync(_cts.Token);
      }

      public void UpdateInterval(int intervalMinutes)
      {
          _intervalMinutes = intervalMinutes; // volatile write — read by loop at next Task.Delay
      }

      public void Stop()
      {
          _cts?.Cancel();
          _cts?.Dispose();
          _cts = null;
      }

      private async Task RunLoopAsync(CancellationToken ct)
      {
          while (!ct.IsCancellationRequested)
          {
              try
              {
                  await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), ct);
              }
              catch (TaskCanceledException)
              {
                  break;
              }

              if (ct.IsCancellationRequested)
                  break;

              await RunCycleAsync(_computers ?? [], ct);
          }
      }

      // Internal for testing — allows direct cycle execution without the delay
      internal async Task RunCycleAsync(IReadOnlyList<ComputerViewModel> computers,
          CancellationToken ct = default)
      {
          var snapshot = System.Windows.Application.Current is not null
              ? System.Windows.Application.Current.Dispatcher.Invoke(
                  () => computers
                      .Where(vm => vm.IsEnabled && !string.IsNullOrEmpty(vm.Description))
                      .ToList())
              : computers
                  .Where(vm => vm.IsEnabled && !string.IsNullOrEmpty(vm.Description))
                  .ToList(); // test path: no Application.Current

          foreach (var vm in snapshot)
          {
              var screenshotCopy = vm.Screenshot;
              if (screenshotCopy is null)
                  continue;

              var descriptionAtStart = vm.Description;
              if (string.IsNullOrEmpty(descriptionAtStart))
                  continue;

              SetOnDispatcher(() => vm.IsLlmChecking = true);

              using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
              using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

              try
              {
                  var result = await _inference.AnalyzeAsync(screenshotCopy, descriptionAtStart, linked.Token);

                  SetOnDispatcher(() =>
                  {
                      if (vm.Description == descriptionAtStart)
                          vm.LastLlmCheck = result;
                  });
              }
              catch (Exception ex)
              {
                  SetOnDispatcher(() =>
                  {
                      if (vm.Description == descriptionAtStart)
                          vm.LastLlmCheck = new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
                  });
              }
              finally
              {
                  SetOnDispatcher(() => vm.IsLlmChecking = false);
              }
          }
      }

      private static void SetOnDispatcher(Action action)
      {
          var app = System.Windows.Application.Current;
          if (app is not null)
              app.Dispatcher.Invoke(action);
          else
              action(); // test path: no WPF dispatcher
      }
  }
  ```

- [ ] **Step 5: Run tests to confirm pass**

  ```bash
  dotnet test --filter "LlmCheckServiceTests"
  ```

  Expected: all tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add ScreensView.Viewer/Services/LlmCheckService.cs ScreensView.Tests/LlmCheckServiceTests.cs ScreensView.Tests/ComputerViewModelTests.cs
  git commit -m "feat: add LlmCheckService with description guard, error handling, UpdateInterval"
  ```

---

## Task 7: MainViewModel — LLM integration

**Files:**
- Modify: `ScreensView.Viewer/ViewModels/MainViewModel.cs`
- Modify: `ScreensView.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

  Add to `ScreensView.Tests/MainViewModelTests.cs` — first add `FakeLlmCheckService` and `FakeModelDownloadService` to the private classes section:

  ```csharp
  private sealed class FakeLlmCheckService : ILlmCheckService
  {
      public List<(IReadOnlyList<string> Names, int IntervalMinutes)> StartCalls { get; } = [];
      public List<int> UpdateIntervalCalls { get; } = [];
      public int StopCalls { get; private set; }

      public void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)
          => StartCalls.Add((computers.Select(vm => vm.Name).ToList(), intervalMinutes));

      public void UpdateInterval(int intervalMinutes)
          => UpdateIntervalCalls.Add(intervalMinutes);

      public void Stop() => StopCalls++;
  }

  private sealed class FakeModelDownloadService : IModelDownloadService
  {
      public bool IsModelReady { get; set; }
      public string ModelPath => string.Empty;
      public event EventHandler? ModelReady;
      public void FireModelReady() => ModelReady?.Invoke(this, EventArgs.Empty);
      public Task DownloadAsync(IProgress<double> progress, CancellationToken ct) => Task.CompletedTask;
  }
  ```

  Add test methods:

  ```csharp
  private MainViewModel CreateVmWithLlm(
      IViewerSettingsService? settingsService = null,
      IAutostartService? autostartService = null,
      ILlmCheckService? llmCheckService = null,
      IModelDownloadService? downloadService = null,
      Action<string, string>? reportError = null)
  {
      var storage = new ComputerStorageService(_tempFile);
      var poller = new ScreenshotPollerService(new AgentHttpClient());
      return new MainViewModel(
          storage, poller,
          settingsService ?? new ViewerSettingsService(_settingsFile),
          autostartService ?? new FakeAutostartService(false),
          reportError,
          llmCheckService ?? new FakeLlmCheckService(),
          downloadService ?? new FakeModelDownloadService());
  }

  [Fact]
  public void Constructor_LoadsLlmCheckIntervalFromSettings()
  {
      var settings = new FakeViewerSettingsService(false, llmCheckIntervalMinutes: 12);
      using var vm = CreateVmWithLlm(settingsService: settings);
      Assert.Equal(12, vm.LlmCheckIntervalMinutes);
  }

  [Fact]
  public void Constructor_WhenModelReady_StartsLlmCheckService()
  {
      var download = new FakeModelDownloadService { IsModelReady = true };
      var llm = new FakeLlmCheckService();
      using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);
      Assert.Single(llm.StartCalls);
  }

  [Fact]
  public void Constructor_WhenModelNotReady_DoesNotStartLlmCheckService()
  {
      var download = new FakeModelDownloadService { IsModelReady = false };
      var llm = new FakeLlmCheckService();
      using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);
      Assert.Empty(llm.StartCalls);
  }

  [Fact]
  public void ModelReady_Event_StartsLlmCheckService()
  {
      var download = new FakeModelDownloadService { IsModelReady = false };
      var llm = new FakeLlmCheckService();
      using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);

      download.FireModelReady();

      Assert.Single(llm.StartCalls);
  }

  [Fact]
  public void LlmCheckIntervalMinutes_WhenChanged_CallsUpdateInterval()
  {
      var download = new FakeModelDownloadService { IsModelReady = true };
      var llm = new FakeLlmCheckService();
      using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);

      vm.LlmCheckIntervalMinutes = 20;

      Assert.Contains(20, llm.UpdateIntervalCalls);
  }

  [Fact]
  public void LlmCheckIntervalMinutes_WhenChanged_SavesSettings()
  {
      var settings = new FakeViewerSettingsService(false);
      var download = new FakeModelDownloadService { IsModelReady = false };
      using var vm = CreateVmWithLlm(settingsService: settings, downloadService: download);
      var priorSaves = settings.SaveCalls;

      vm.LlmCheckIntervalMinutes = 10;

      Assert.True(settings.SaveCalls > priorSaves);
      Assert.Equal(10, settings.Current.LlmCheckIntervalMinutes);
  }

  [Fact]
  public void Dispose_StopsLlmCheckService()
  {
      var llm = new FakeLlmCheckService();
      var vm = CreateVmWithLlm(llmCheckService: llm);
      vm.Dispose();
      Assert.Equal(1, llm.StopCalls);
  }

  [Fact]
  public void UpdateComputer_PropagatesDescription()
  {
      using var vm = CreateVmWithLlm();
      vm.AddComputer(new ComputerConfig { Name = "X", Host = "1.1.1.1", Port = 5443, ApiKey = "k" });
      var computerVm = vm.Computers[0];
      var updated = computerVm.ToConfig();
      updated.Description = "new desc";

      vm.UpdateComputer(computerVm, updated);

      Assert.Equal("new desc", computerVm.Description);
  }

  [Fact]
  public void UpdateComputer_ResetsLastLlmCheck()
  {
      using var vm = CreateVmWithLlm();
      vm.AddComputer(new ComputerConfig { Name = "X", Host = "1.1.1.1", Port = 5443, ApiKey = "k" });
      var computerVm = vm.Computers[0];
      computerVm.LastLlmCheck = new LlmCheckResult(true, "ok", false, DateTime.Now);

      vm.UpdateComputer(computerVm, computerVm.ToConfig());

      Assert.Null(computerVm.LastLlmCheck);
  }
  ```

  Update `FakeViewerSettingsService` to track `LlmCheckIntervalMinutes`:

  ```csharp
  private class FakeViewerSettingsService(bool initialValue, int refreshIntervalSeconds = 5,
      int llmCheckIntervalMinutes = 5) : IViewerSettingsService
  {
      public ViewerSettings Current { get; private set; } = new()
      {
          LaunchAtStartup = initialValue,
          RefreshIntervalSeconds = refreshIntervalSeconds,
          LlmCheckIntervalMinutes = llmCheckIntervalMinutes
      };
      public int SaveCalls { get; private set; }

      public ViewerSettings Load() => new()
      {
          LaunchAtStartup = Current.LaunchAtStartup,
          RefreshIntervalSeconds = Current.RefreshIntervalSeconds,
          LlmCheckIntervalMinutes = Current.LlmCheckIntervalMinutes
      };

      public void Save(ViewerSettings settings)
      {
          SaveCalls++;
          Current = new ViewerSettings
          {
              LaunchAtStartup = settings.LaunchAtStartup,
              RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
              LlmCheckIntervalMinutes = settings.LlmCheckIntervalMinutes
          };
      }
  }
  ```

- [ ] **Step 2: Verify existing tests still compile before making any changes**

  The new `internal` constructor parameters are **optional** (`= null`), so all existing 5-argument call sites in `MainViewModelTests.cs` continue to compile unchanged. Confirm this by running:

  ```bash
  dotnet test --filter "MainViewModelTests"
  ```

  Expected: all existing tests pass. If they fail here, stop and investigate before continuing.

- [ ] **Step 3: Run to confirm new tests fail**

  ```bash
  dotnet test --filter "MainViewModelTests"
  ```

  Expected: compile errors (new constructor params `ILlmCheckService`, `IModelDownloadService`, and new properties `LlmCheckIntervalMinutes`, `ModelDownloadProgress` not yet on `MainViewModel`).

  > **Note:** Since the new constructor parameters are optional, existing tests still compile. The new tests added in Step 1 fail because `CreateVmWithLlm` passes 7 arguments and `FakeLlmCheckService`/`FakeModelDownloadService` don't exist yet.

- [ ] **Step 4: Implement MainViewModel changes**

  In `ScreensView.Viewer/ViewModels/MainViewModel.cs`:

  Add fields after existing fields:
  ```csharp
  private readonly ILlmCheckService _llmCheckService;
  private readonly IModelDownloadService _downloadService;
  private readonly CancellationTokenSource _appCts = new();
  ```

  Add observable properties after `_isAutostartEnabled`:
  ```csharp
  [ObservableProperty] private int _llmCheckIntervalMinutes = 5;
  [ObservableProperty] private double _modelDownloadProgress = -1;
  ```

  Expose token:
  ```csharp
  public CancellationToken AppToken => _appCts.Token;
  ```

  Update the public convenience constructor. Use a single `ModelDownloadService` instance shared between `LlmInferenceService` and the download parameter to avoid two separate instances:
  ```csharp
  public MainViewModel(IComputerStorageService storage, IScreenshotPollerService poller)
  {
      var downloadService = new ModelDownloadService();
      // delegate to full internal ctor
      // (cannot use : this(...) here because downloadService is needed twice)
      // Instead, inline the full ctor body by keeping this constructor calling
      // the internal overload with explicit shared instance:
  }
  ```

  > **Implementation note:** The convenience constructor cannot share a single `downloadService` instance via `: this(...)` without creating it twice. The cleanest fix is to make it `internal` instead of `public`, or to remove it entirely — `App.xaml.cs` is the production entry point and always passes all services. This constructor is only used in tests that don't exercise the download path (e.g. `CountingSaveViewModel`). Replace it with:

  ```csharp
  // Convenience for tests that don't need LLM services
  public MainViewModel(IComputerStorageService storage, IScreenshotPollerService poller)
      : this(storage, poller, new ViewerSettingsService(), new AutostartService())
  {
  }
  ```

  This calls the `internal` constructor with `llmCheckService = null` and `downloadService = null`, which resolves to `new LlmCheckService(new LlmInferenceService(new ModelDownloadService()))` and a separate `new ModelDownloadService()` — same as before. Since the convenience constructor is only used in tests that don't run the download path (they test polling, not LLM), the duplicate instance is harmless in that context.

  Update the `internal` constructor signature:
  ```csharp
  internal MainViewModel(
      IComputerStorageService storage,
      IScreenshotPollerService poller,
      IViewerSettingsService viewerSettingsService,
      IAutostartService autostartService,
      Action<string, string>? reportError = null,
      ILlmCheckService? llmCheckService = null,
      IModelDownloadService? downloadService = null)
  ```

  In the constructor body, after `InitializeAutostartState()`:
  ```csharp
  _llmCheckService = llmCheckService ?? new LlmCheckService(
      new LlmInferenceService(new ModelDownloadService()));
  _downloadService = downloadService ?? new ModelDownloadService();

  _llmCheckIntervalMinutes = NormalizeLlmCheckInterval(
      _viewerSettings.LlmCheckIntervalMinutes);
  _viewerSettings.LlmCheckIntervalMinutes = _llmCheckIntervalMinutes;

  _downloadService.ModelReady += (_, _) =>
      _llmCheckService.Start(Computers, _llmCheckIntervalMinutes);

  if (_downloadService.IsModelReady)
      _llmCheckService.Start(Computers, _llmCheckIntervalMinutes);
  ```

  Add `OnLlmCheckIntervalChanged`:
  ```csharp
  partial void OnLlmCheckIntervalChanged(int value)
  {
      var normalized = NormalizeLlmCheckInterval(value);
      if (value != normalized)
      {
          LlmCheckIntervalMinutes = normalized;
          return;
      }

      _viewerSettings.LlmCheckIntervalMinutes = value;
      _viewerSettingsService.Save(_viewerSettings);

      _llmCheckService.UpdateInterval(value);
  }
  ```

  Add helper:
  ```csharp
  private const int MinLlmCheckIntervalMinutes = 1;
  private const int MaxLlmCheckIntervalMinutes = 60;
  private const int DefaultLlmCheckIntervalMinutes = 5;

  private static int NormalizeLlmCheckInterval(int value) =>
      value is >= MinLlmCheckIntervalMinutes and <= MaxLlmCheckIntervalMinutes
          ? value
          : DefaultLlmCheckIntervalMinutes;
  ```

  Add `ReportDownloadError`:
  ```csharp
  internal void ReportDownloadError(string message) =>
      ReportError("Model download", message);
  ```

  Update `UpdateComputer` to add after `vm.CertThumbprint = config.CertThumbprint`:
  ```csharp
  vm.Description = config.Description;
  vm.LastLlmCheck = null;
  ```

  Update `Dispose()`:
  ```csharp
  public void Dispose()
  {
      _poller.Dispose();
      _llmCheckService.Stop();
      _appCts.Cancel();
      _appCts.Dispose();
  }
  ```

- [ ] **Step 5: Run tests to confirm pass**

  ```bash
  dotnet test --filter "MainViewModelTests"
  ```

  Expected: all tests pass.

- [ ] **Step 6: Run full test suite**

  ```bash
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 7: Commit**

  ```bash
  git add ScreensView.Viewer/ViewModels/MainViewModel.cs ScreensView.Tests/MainViewModelTests.cs
  git commit -m "feat: wire LlmCheckService and ModelDownloadService into MainViewModel"
  ```

---

## Task 8: App.xaml.cs wiring

**Files:**
- Modify: `ScreensView.Viewer/App.xaml.cs`

- [ ] **Step 1: Update App.xaml.cs**

  In `OnStartup`, after `var poller = new ScreenshotPollerService(http);` and before `viewModel = new MainViewModel(...)`:

  ```csharp
  var downloadService = new ModelDownloadService();
  var inferenceService = new LlmInferenceService(downloadService);
  var llmCheckService = new LlmCheckService(inferenceService);
  ```

  Update the `new MainViewModel(...)` call to pass the new services as the last two params:
  ```csharp
  viewModel = new MainViewModel(
      startup.Storage!,
      poller,
      settingsService,
      new AutostartService(),
      (title, message) =>
      {
          if (mainWindow is null)
              MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
          else
              MessageBox.Show(mainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
      },
      llmCheckService,
      downloadService);
  ```

  After `mainWindow = new MainWindow(...)` line, add the download kick-off:
  ```csharp
  if (!downloadService.IsModelReady)
      StartModelDownloadAsync(downloadService, viewModel);
  ```

  Add the method inside the `App` class:
  ```csharp
  private static async void StartModelDownloadAsync(
      ModelDownloadService downloadService,
      MainViewModel viewModel)
  {
      var progress = new Progress<double>(p => viewModel.ModelDownloadProgress = p);
      try
      {
          await downloadService.DownloadAsync(progress, viewModel.AppToken);
          viewModel.ModelDownloadProgress = -1;
      }
      catch (OperationCanceledException)
      {
          viewModel.ModelDownloadProgress = -1;
      }
      catch (Exception ex)
      {
          viewModel.ModelDownloadProgress = -1;
          viewModel.ReportDownloadError(ex.Message);
      }
  }
  ```

- [ ] **Step 2: Build to confirm**

  ```bash
  dotnet build ScreensView.Viewer/ScreensView.Viewer.csproj
  ```

  Expected: build succeeds.

- [ ] **Step 3: Commit**

  ```bash
  git add ScreensView.Viewer/App.xaml.cs
  git commit -m "feat: wire new LLM services in App.xaml.cs with download lifecycle handling"
  ```

---

## Task 9: AddEditComputerWindow — Description field

**Files:**
- Modify: `ScreensView.Viewer/Views/AddEditComputerWindow.xaml`
- Modify: `ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs`

- [ ] **Step 1: Add Description TextBox to XAML**

  In `AddEditComputerWindow.xaml`, replace the `<CheckBox>` element and the `<StackPanel Orientation="Horizontal"...>` OK/Cancel block with:

  ```xml
  <CheckBox x:Name="EnabledCheck" Content="Включён" IsChecked="True" Margin="0,0,0,12"/>

  <Label Content="Описание экрана (необязательно):"/>
  <TextBox x:Name="DescriptionBox"
           AcceptsReturn="True"
           TextWrapping="Wrap"
           Height="64"
           VerticalScrollBarVisibility="Auto"
           Margin="0,0,0,16"/>

  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
      <Button Content="OK" Width="80" Margin="0,0,8,0" IsDefault="True" Click="Ok_Click"/>
      <Button Content="Отмена" Width="80" IsCancel="True"/>
  </StackPanel>
  ```

  Also increase the Window `Height` from `380` to `480`.

- [ ] **Step 2: Update code-behind**

  In `AddEditComputerWindow.xaml.cs`, in the `if (existing != null)` block, add:
  ```csharp
  DescriptionBox.Text = existing.Description ?? string.Empty;
  ```

  In `Ok_Click`, after setting `CertThumbprint`, update `Result`:
  ```csharp
  var desc = DescriptionBox.Text.Trim();
  Result = new ComputerConfig
  {
      Name = NameBox.Text.Trim(),
      Host = newHost,
      Port = port,
      ApiKey = ApiKeyBox.Text.Trim(),
      IsEnabled = EnabledCheck.IsChecked == true,
      CertThumbprint = certThumbprint,
      Description = desc.Length > 0 ? desc : null
  };
  ```

- [ ] **Step 3: Build and manually verify**

  ```bash
  dotnet build
  ```

  Then run the Viewer, open Add Computer, confirm the Description field is present and populated when editing.

- [ ] **Step 4: Commit**

  ```bash
  git add ScreensView.Viewer/Views/AddEditComputerWindow.xaml ScreensView.Viewer/Views/AddEditComputerWindow.xaml.cs
  git commit -m "feat: add Description field to AddEditComputerWindow"
  ```

---

## Task 10: MainWindow.xaml — LLM border, tooltip, progress bar, interval

**Files:**
- Modify: `ScreensView.Viewer/MainWindow.xaml`
- Modify: `ScreensView.Viewer/App.xaml.cs` (add value converter)

- [ ] **Step 1: Add LlmBorderBrushConverter to App.xaml.cs**

  After the existing `NullToBoolConverter` class, add:

  ```csharp
  public class LlmBorderBrushConverter : IValueConverter
  {
      public static readonly LlmBorderBrushConverter Instance = new();

      public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is LlmCheckResult { IsError: false, IsMatch: true })
              return new System.Windows.Media.SolidColorBrush(
                  System.Windows.Media.Color.FromRgb(0x44, 0xCC, 0x44)); // green
          if (value is LlmCheckResult { IsError: false, IsMatch: false })
              return new System.Windows.Media.SolidColorBrush(
                  System.Windows.Media.Color.FromRgb(0xFF, 0x88, 0x00)); // orange
          return System.Windows.Media.Brushes.Transparent;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
          => throw new NotImplementedException();
  }

  public class LlmBorderThicknessConverter : IValueConverter
  {
      public static readonly LlmBorderThicknessConverter Instance = new();

      public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is LlmCheckResult { IsError: false, IsMatch: true })
              return new System.Windows.Thickness(2);
          if (value is LlmCheckResult { IsError: false, IsMatch: false })
              return new System.Windows.Thickness(3);
          return new System.Windows.Thickness(0);
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
          => throw new NotImplementedException();
  }

  public class LlmTooltipConverter : IValueConverter
  {
      public static readonly LlmTooltipConverter Instance = new();

      public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is bool isChecking && isChecking)
              return "LLM: analysing...";
          if (value is LlmCheckResult result)
          {
              var prefix = result.IsError ? "LLM: Error" :
                           result.IsMatch ? "LLM: Match" : "LLM: Mismatch";
              return $"{prefix} — {result.Explanation}";
          }
          return null;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
          => throw new NotImplementedException();
  }
  ```

  Add `using ScreensView.Viewer.Models;` at the top of `App.xaml.cs`.

- [ ] **Step 2: Register converters in App.xaml resources**

  In `ScreensView.Viewer/App.xaml`, add converter resources (inside `<Application.Resources>`):

  ```xml
  <local:LlmBorderBrushConverter x:Key="LlmBorderBrush"/>
  <local:LlmBorderThicknessConverter x:Key="LlmBorderThickness"/>
  <local:LlmTooltipConverter x:Key="LlmTooltip"/>
  ```

  Ensure `xmlns:local="clr-namespace:ScreensView.Viewer"` is present on the `<Application>` element.

- [ ] **Step 3: Wrap tile card with LLM border overlay**

  In `MainWindow.xaml`, find the outer tile `<Border Margin="6" ...>` (line ~207). Wrap it in an additional `<Border>` that shows the LLM color:

  Replace:
  ```xml
  <Border Margin="6" BorderBrush="#CCCCCC" BorderThickness="1"
          Background="#1E1E1E" CornerRadius="4"
          MouseLeftButtonDown="Card_MouseLeftButtonDown">
  ```

  With:
  ```xml
  <Border Margin="6"
          BorderBrush="{Binding LastLlmCheck, Converter={StaticResource LlmBorderBrush}}"
          BorderThickness="{Binding LastLlmCheck, Converter={StaticResource LlmBorderThickness}}"
          CornerRadius="4">
      <Border.ToolTip>
          <ToolTip>
              <TextBlock>
                  <TextBlock.Style>
                      <Style TargetType="TextBlock">
                          <Setter Property="Text" Value="{Binding LastLlmCheck, Converter={StaticResource LlmTooltip}}"/>
                          <Style.Triggers>
                              <DataTrigger Binding="{Binding IsLlmChecking}" Value="True">
                                  <Setter Property="Text" Value="LLM: analysing..."/>
                              </DataTrigger>
                          </Style.Triggers>
                      </Style>
                  </TextBlock.Style>
              </TextBlock>
          </ToolTip>
      </Border.ToolTip>
  <Border BorderBrush="#CCCCCC" BorderThickness="1"
          Background="#1E1E1E" CornerRadius="4"
          MouseLeftButtonDown="Card_MouseLeftButtonDown">
  ```

  And close both `</Border>` tags at the end of the tile template.

- [ ] **Step 4: Add download progress bar and LLM interval control to toolbar**

  In `MainWindow.xaml`, find the toolbar `<StackPanel>` that contains the interval slider. After the existing `<CheckBox Content="Автозапуск" ...>`, add:

  ```xml
  <Rectangle Style="{StaticResource ToolbarSeparator}"/>

  <!-- LLM interval -->
  <TextBlock Text="LLM:" VerticalAlignment="Center" Margin="4,0,2,0" FontSize="12"/>
  <Slider Minimum="1" Maximum="60" Width="80" VerticalAlignment="Center"
          Value="{Binding LlmCheckIntervalMinutes}"
          Margin="4,0" TickFrequency="1" IsSnapToTickEnabled="True"/>
  <TextBlock Text="{Binding LlmCheckIntervalMinutes, StringFormat='{}{0} min'}"
             VerticalAlignment="Center" Width="42"
             FontSize="13" FontWeight="SemiBold" Foreground="#1A1A1A"/>

  <!-- Model download progress (hidden when not downloading) -->
  <Rectangle Style="{StaticResource ToolbarSeparator}">
      <Rectangle.Style>
          <Style TargetType="Rectangle" BasedOn="{StaticResource ToolbarSeparator}">
              <Setter Property="Visibility" Value="Collapsed"/>
              <Style.Triggers>
                  <DataTrigger Binding="{Binding ModelDownloadProgress,
                      Converter={StaticResource NullToBool}}" Value="True">
                      <Setter Property="Visibility" Value="Visible"/>
                  </DataTrigger>
              </Style.Triggers>
          </Style>
      </Rectangle.Style>
  </Rectangle>
  <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
      <StackPanel.Style>
          <Style TargetType="StackPanel">
              <Setter Property="Visibility" Value="Collapsed"/>
              <Style.Triggers>
                  <DataTrigger Binding="{Binding ModelDownloadProgress,
                      Converter={StaticResource NullToBool}}" Value="True">
                      <Setter Property="Visibility" Value="Visible"/>
                  </DataTrigger>
              </Style.Triggers>
          </Style>
      </StackPanel.Style>
      <TextBlock Text="Downloading model: " VerticalAlignment="Center" FontSize="12"/>
      <ProgressBar Width="120" Height="14" VerticalAlignment="Center"
                   Minimum="0" Maximum="100"
                   Value="{Binding ModelDownloadProgress, Mode=OneWay}"/>
      <TextBlock Text="{Binding ModelDownloadProgress, StringFormat='{} {0:0}%'}"
                 VerticalAlignment="Center" Margin="4,0,0,0" FontSize="12"/>
  </StackPanel>
  ```

  > **IMPORTANT:** The XAML above uses `Converter={StaticResource NullToBool}` as a placeholder — `NullToBool` always returns `true` for `double` (which is never null). Replace with `ModelDownloadActive` after completing Step 4a.

- [ ] **Step 4a: Add ModelDownloadActiveConverter**

  `ModelDownloadProgress` is a `double` initialized to `-1` (not downloading) and `≥0` when active. The `NullToBoolConverter` cannot distinguish these values. Add to `App.xaml.cs` after the existing converters:

  ```csharp
  public class ModelDownloadActiveConverter : IValueConverter
  {
      public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
          => value is double d && d >= 0;
      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
          => throw new NotImplementedException();
  }
  ```

  Register in `App.xaml` resources:

  ```xml
  <local:ModelDownloadActiveConverter x:Key="ModelDownloadActive"/>
  ```

  In the XAML from Step 4, replace every `Converter={StaticResource NullToBool}` inside the download progress section with `Converter={StaticResource ModelDownloadActive}`.

- [ ] **Step 5: Build and verify visually**

  ```bash
  dotnet build
  dotnet run --project ScreensView.Viewer
  ```

  Confirm: toolbar shows LLM interval slider; progress bar hidden; tiles have no border until LlmCheckResult is set (use debugger or add a test computer with description to trigger).

- [ ] **Step 6: Commit**

  ```bash
  git add ScreensView.Viewer/MainWindow.xaml ScreensView.Viewer/App.xaml ScreensView.Viewer/App.xaml.cs
  git commit -m "feat: add LLM tile border/tooltip, progress bar, and interval control to MainWindow"
  ```

---

## Task 11: LlmInferenceService — LLamaSharp vision implementation

**Files:**
- Modify: `ScreensView.Viewer/Services/LlmInferenceService.cs`

> **Prerequisites:** Verify at [github.com/SciSharp/LLamaSharp](https://github.com/SciSharp/LLamaSharp) that the installed version supports multimodal/vision inference with Qwen3.5 GGUF. If `LLavaWeights` is incompatible, download a LLaVA-based GGUF instead and update `ModelDownloadService.DownloadUrl` and `ModelFileName` accordingly.

- [x] **Step 1: Confirm LLamaSharp vision API**

  Checked LLamaSharp `v0.20.0` docs/samples and verified the supported multimodal path is:
  - `LLamaWeights.LoadFromFileAsync(modelParams, ct)`
  - `LLavaWeights.LoadFromFileAsync(projectorPath, ct)`
  - `new InteractiveExecutor(context, clipModel)`
  - `executor.Images.Add(jpegBytes)`
  - prompt includes `<image>` and the `USER` / `ASSISTANT` turn markers

  The current `unsloth/Qwen3.5-2B-GGUF` release provides both `Qwen3.5-2B-Q4_K_M.gguf` and `mmproj-F16.gguf`, so the app must download both files.

- [x] **Step 2: Implement AnalyzeAsync**

  Replace the `NotImplementedException` stub in `LlmInferenceService.AnalyzeAsync` with the actual multimodal call:

  ```csharp
  private ILlmVisionRuntime? _runtime;
  private readonly SemaphoreSlim _loadLock = new(1, 1);

  private async Task EnsureLoadedAsync(CancellationToken ct)
  {
      if (_runtime is not null) return;
      await _loadLock.WaitAsync(ct);
      try
      {
          if (_runtime is not null) return;
          _runtime = await _runtimeFactory.CreateAsync(
              _download.ModelPath,
              _download.ProjectorPath,
              ct);
      }
      finally { _loadLock.Release(); }
  }

  public async Task<LlmCheckResult> AnalyzeAsync(
      BitmapImage screenshot, string description, CancellationToken ct)
  {
      try
      {
          await EnsureLoadedAsync(ct);

          var jpegBytes = await EncodeJpegAsync(screenshot);
          var prompt = "<image>\nUSER:\nDoes the screen match this description: " +
                       $"'{description}'? Reply with YES or NO and one sentence explanation.\nASSISTANT:\n";
          var rawResponse = await _runtime!.InferAsync(jpegBytes, prompt, ct);
          var (isMatch, explanation) = ParseModelResponse(rawResponse);

          return new LlmCheckResult(isMatch, explanation, IsError: false, DateTime.Now);
      }
      catch (OperationCanceledException)
      {
          throw;
      }
      catch (Exception ex)
      {
          return new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
      }
  }
  ```

  Implementation notes:
  - `ModelDownloadService` must expose both `ModelPath` and `ProjectorPath`
  - The production runtime should serialize inference calls and create `InteractiveExecutor(context, projector)` per request
  - Parse `YES` / `NO` from the first token and treat malformed output as an error result

- [x] **Step 3: Commit working implementation**

  ```bash
  git add ScreensView.Viewer/Services/LlmInferenceService.cs ScreensView.Viewer/Services/ModelDownloadService.cs ScreensView.Tests/LlmInferenceServiceTests.cs ScreensView.Tests/ModelDownloadServiceTests.cs ScreensView.Tests/MainViewModelTests.cs docs/superpowers/specs/2026-04-02-llm-screen-analysis-design.md docs/superpowers/plans/2026-04-02-llm-screen-analysis.md
  git commit -m "feat: implement LLamaSharp multimodal screen analysis"
  ```

---

## Final Verification

- [ ] Run all tests:

  ```bash
  dotnet test
  ```

  Expected: all tests pass.

- [ ] Run the full Viewer application:

  ```bash
  dotnet run --project ScreensView.Viewer
  ```

  Manually verify against the spec's Verification section (items 1–13 in the spec).

- [ ] Commit final state:

  ```bash
  git add -A
  git commit -m "feat: complete LLM screen analysis feature"
  ```
