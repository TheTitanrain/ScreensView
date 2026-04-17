using System.Runtime.ExceptionServices;
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

    [Fact]
    public void Start_SetsIsLlmServiceActiveOnAllComputers()
    {
        var inference = new FakeLlmInferenceService();
        var svc = new LlmCheckService(inference);
        var computers = new[] { MakeVm("desc 1"), MakeVm("desc 2") };

        svc.Start(computers, intervalMinutes: 60);

        Assert.All(computers, vm => Assert.True(vm.IsLlmServiceActive));

        svc.Stop();
    }

    [Fact]
    public void Stop_ClearsIsLlmServiceActiveOnAllComputers()
    {
        var inference = new FakeLlmInferenceService();
        var svc = new LlmCheckService(inference);
        var computers = new[] { MakeVm("desc 1"), MakeVm("desc 2") };

        svc.Start(computers, intervalMinutes: 60);
        svc.Stop();

        Assert.All(computers, vm => Assert.False(vm.IsLlmServiceActive));
    }

    [Fact]
    public async Task Start_RunsFirstCycleImmediately()
    {
        var expected = new LlmCheckResult(true, "ok", false, DateTime.UtcNow);
        var inference = new FakeLlmInferenceService(expected);
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        svc.Start([vm], intervalMinutes: 60);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await inference.WaitForCallAsync(timeout.Token);

        Assert.NotNull(vm.LastLlmCheck);
        Assert.Equal(expected, vm.LastLlmCheck);

        svc.Stop();
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
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        await svc.RunCycleAsync([vm]);

        Assert.Equal(expected, vm.LastLlmCheck);
        Assert.False(vm.IsLlmChecking);
    }

    [Fact]
    public async Task RunCycle_LogsResultWithDurationAndDetails()
    {
        var expected = new LlmCheckResult(true, "Looks good", false, DateTime.Now);
        var inference = new FakeLlmInferenceService(expected);
        var log = new FakeViewerLogService();
        var svc = new LlmCheckService(inference, log);
        var vm = MakeVm("Excel dashboard");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        await svc.RunCycleAsync([vm]);

        var entry = Assert.Single(log.Infos, x => x.EventName == "LlmCheckService.Result");
        Assert.Contains("PC", entry.Message);
        Assert.Contains("elapsedMs=", entry.Message);
        Assert.Contains("outcome=match", entry.Message);
        Assert.Contains("Excel dashboard", entry.Message);
        Assert.Contains("Looks good", entry.Message);
    }

    [Fact]
    public async Task RunCycle_WhenScreenshotMatchesOneOfRecentImages_ReusesCachedResult()
    {
        var inference = new FakeLlmInferenceService();
        inference.Enqueue(new LlmCheckResult(true, "first", false, DateTime.UtcNow));
        inference.Enqueue(new LlmCheckResult(false, "second", false, DateTime.UtcNow.AddSeconds(1)));
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");

        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;
        await svc.RunCycleAsync([vm]);

        var firstScreenshot = vm.Screenshot;
        var firstTimestamp = vm.LastUpdated;

        vm.Screenshot = CreateBitmapFromColor(System.Drawing.Color.DarkRed);
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = firstTimestamp!.Value.AddSeconds(1);
        await svc.RunCycleAsync([vm]);

        vm.Screenshot = firstScreenshot;
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = firstTimestamp.Value.AddSeconds(2);
        await svc.RunCycleAsync([vm]);

        Assert.Equal(2, inference.Calls.Count);
        Assert.Equal("first", vm.LastLlmCheck!.Explanation);
    }

    [Fact]
    public async Task RunCycle_WhenHistoryExceeds16_EvictsOldestImage()
    {
        var inference = new FakeLlmInferenceService();
        for (var i = 0; i < 18; i++)
            inference.Enqueue(new LlmCheckResult(i % 2 == 0, $"result-{i}", false, DateTime.UtcNow.AddSeconds(i)));

        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");
        var screenshots = Enumerable.Range(0, 17)
            .Select(i => CreateBitmapFromColor(System.Drawing.Color.FromArgb(255, i * 10 % 255, i * 20 % 255, i * 30 % 255)))
            .ToList();
        var baseTime = DateTime.UtcNow;

        for (var i = 0; i < screenshots.Count; i++)
        {
            vm.Screenshot = screenshots[i];
            vm.Status = ComputerStatus.Online;
            vm.LastUpdated = baseTime.AddSeconds(i);
            await svc.RunCycleAsync([vm]);
        }

        vm.Screenshot = screenshots[0];
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = baseTime.AddSeconds(100);
        await svc.RunCycleAsync([vm]);

        Assert.Equal(18, inference.Calls.Count);
        Assert.Equal("result-17", vm.LastLlmCheck!.Explanation);
    }

    [Fact]
    public async Task RunCycle_WhenComputerIsOffline_ClearsLastResultAndSkipsLlm()
    {
        var inference = new FakeLlmInferenceService();
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.LastUpdated = DateTime.UtcNow;
        vm.Status = ComputerStatus.Online;
        vm.LastLlmCheck = new LlmCheckResult(true, "cached", false, DateTime.UtcNow);

        vm.SetError("offline");
        await svc.RunCycleAsync([vm]);

        Assert.Empty(inference.Calls);
        Assert.Null(vm.LastLlmCheck);
    }

    [Fact]
    public async Task RunCycle_WhenCancelledByService_DoesNotWriteErrorResult()
    {
        var inference = new FakeLlmInferenceService(delayUntilCancelled: true);
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        using var cts = new CancellationTokenSource();
        var runTask = svc.RunCycleAsync([vm], cts.Token);
        await inference.WaitForCallAsync(CancellationToken.None);
        cts.Cancel();
        await runTask;

        Assert.Null(vm.LastLlmCheck);
        Assert.False(vm.IsLlmChecking);
    }

    [Fact]
    public async Task Stop_ClearsHistoryCache()
    {
        var inference = new FakeLlmInferenceService();
        inference.Enqueue(new LlmCheckResult(true, "first", false, DateTime.UtcNow));
        inference.Enqueue(new LlmCheckResult(false, "second", false, DateTime.UtcNow.AddSeconds(1)));
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");

        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;
        await svc.RunCycleAsync([vm]);

        svc.Stop();

        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow.AddSeconds(1);
        await svc.RunCycleAsync([vm]);

        Assert.Equal(2, inference.Calls.Count);
        Assert.Equal("second", vm.LastLlmCheck!.Explanation);
    }

    [Fact]
    public async Task RunCycle_WhenInferenceTimesOut_WritesFriendlyErrorAndContinues()
    {
        var inference = new ScriptedLlmInferenceService(
            async ct =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new LlmCheckResult(false, "unreachable", false, DateTime.UtcNow);
            },
            ct => Task.FromResult(new LlmCheckResult(true, "next-ok", false, DateTime.UtcNow)));
        var svc = new LlmCheckService(inference, null, TimeSpan.FromMilliseconds(50));

        var first = MakeVm("desc 1");
        first.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        first.Status = ComputerStatus.Online;
        first.LastUpdated = DateTime.UtcNow;

        var second = MakeVm("desc 2");
        second.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        second.Status = ComputerStatus.Online;
        second.LastUpdated = DateTime.UtcNow.AddSeconds(1);

        await svc.RunCycleAsync([first, second]);

        Assert.NotNull(first.LastLlmCheck);
        Assert.True(first.LastLlmCheck!.IsError);
        Assert.Equal(LlmCheckService.BuildTimeoutMessage(), first.LastLlmCheck.Explanation);
        Assert.NotNull(second.LastLlmCheck);
        Assert.Equal("next-ok", second.LastLlmCheck!.Explanation);
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
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

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
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        await svc.RunCycleAsync([vm]);

        Assert.NotNull(vm.LastLlmCheck);
        Assert.True(vm.LastLlmCheck!.IsError);
        Assert.Contains("model error", vm.LastLlmCheck.Explanation);
        Assert.False(vm.IsLlmChecking);
    }

    [Fact]
    public async Task RunCycle_WhenInferenceThrows_LogsErrorWithDurationAndDetails()
    {
        var inference = new FakeLlmInferenceService(throwException: new InvalidOperationException("model error"));
        var log = new FakeViewerLogService();
        var svc = new LlmCheckService(inference, log);
        var vm = MakeVm("Excel dashboard");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        await svc.RunCycleAsync([vm]);

        var entry = Assert.Single(log.Errors, x => x.EventName == "LlmCheckService.ResultError");
        Assert.Contains("PC", entry.Message);
        Assert.Contains("elapsedMs=", entry.Message);
        Assert.Contains("Excel dashboard", entry.Message);
        Assert.NotNull(entry.Exception);
        Assert.Contains("model error", entry.Exception!.Message);
    }

    // ---- IsLlmChecking always reset even on exception ----

    [Fact]
    public async Task RunCycle_IsLlmChecking_AlwaysResetToFalse()
    {
        var inference = new FakeLlmInferenceService(throwException: new Exception("boom"));
        var svc = new LlmCheckService(inference);
        var vm = MakeVm("desc");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();
        vm.Status = ComputerStatus.Online;
        vm.LastUpdated = DateTime.UtcNow;

        await svc.RunCycleAsync([vm]);

        Assert.False(vm.IsLlmChecking);
    }

    // ---- Fake ----

    private class FakeLlmInferenceService : ILlmInferenceService
    {
        private readonly LlmCheckResult? _result;
        private readonly Exception? _throw;
        private readonly bool _delayUntilCancelled;
        private readonly TaskCompletionSource _callTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Queue<LlmCheckResult> _queuedResults = new();

        public List<(string Description, CancellationToken Ct)> Calls { get; } = [];

        // Set OnBeforeReturn to mutate state mid-inference (e.g. change vm.Description)
        public Action? OnBeforeReturn { get; set; }

        public FakeLlmInferenceService(
            LlmCheckResult? result = null,
            Exception? throwException = null,
            bool delayUntilCancelled = false)
        {
            _result = result;
            _throw = throwException;
            _delayUntilCancelled = delayUntilCancelled;
        }

        public void Enqueue(LlmCheckResult result) => _queuedResults.Enqueue(result);

        public Task<LlmCheckResult> AnalyzeAsync(
            System.Windows.Media.Imaging.BitmapImage screenshot,
            string description,
            CancellationToken ct)
        {
            Calls.Add((description, ct));
            _callTcs.TrySetResult();
            if (_throw is not null)
                throw _throw;
            if (_delayUntilCancelled)
                return WaitForCancellationAsync(ct);
            OnBeforeReturn?.Invoke();

            if (_queuedResults.Count > 0)
                return Task.FromResult(_queuedResults.Dequeue());

            return Task.FromResult(_result ?? new LlmCheckResult(true, "ok", false, DateTime.Now));
        }

        public Task WaitForCallAsync(CancellationToken cancellationToken)
            => _callTcs.Task.WaitAsync(cancellationToken);

        public Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct)
            => Task.FromResult<LlmRuntimeLoadException?>(null);

        public int ResetCalls { get; private set; }
        public void Reset() => ResetCalls++;

        private static async Task<LlmCheckResult> WaitForCancellationAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class FakeViewerLogService : IViewerLogService
    {
        public List<(string EventName, string Message)> Infos { get; } = [];
        public List<(string EventName, string Message)> Warnings { get; } = [];
        public List<(string EventName, string Message, Exception? Exception)> Errors { get; } = [];

        public void LogInfo(string eventName, string message)
            => Infos.Add((eventName, message));

        public void LogWarning(string eventName, string message)
            => Warnings.Add((eventName, message));

        public void LogError(string eventName, string message, Exception? exception = null)
            => Errors.Add((eventName, message, exception));
    }

    private sealed class ScriptedLlmInferenceService(params Func<CancellationToken, Task<LlmCheckResult>>[] steps)
        : ILlmInferenceService
    {
        private readonly Queue<Func<CancellationToken, Task<LlmCheckResult>>> _steps = new(steps);

        public Task<LlmCheckResult> AnalyzeAsync(
            System.Windows.Media.Imaging.BitmapImage screenshot,
            string description,
            CancellationToken ct)
            => _steps.Dequeue()(ct);

        public Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct)
            => Task.FromResult<LlmRuntimeLoadException?>(null);

        public void Reset()
        {
        }
    }

    private static System.Windows.Media.Imaging.BitmapImage CreateBitmapFromColor(System.Drawing.Color color)
    {
        using var bmp = new System.Drawing.Bitmap(16, 16);
        using var graphics = System.Drawing.Graphics.FromImage(bmp);
        graphics.Clear(color);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        var bytes = ms.ToArray();

        return RunOnSta(() =>
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            using var imageStream = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.StreamSource = imageStream;
            img.EndInit();
            img.Freeze();
            return img;
        });
    }

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null) ExceptionDispatchInfo.Capture(caught).Throw();
        return result!;
    }
}
