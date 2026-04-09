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

        await svc.RunCycleAsync([vm]);

        var entry = Assert.Single(log.Infos, x => x.EventName == "LlmCheckService.Result");
        Assert.Contains("PC", entry.Message);
        Assert.Contains("elapsedMs=", entry.Message);
        Assert.Contains("outcome=match", entry.Message);
        Assert.Contains("Excel dashboard", entry.Message);
        Assert.Contains("Looks good", entry.Message);
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

    [Fact]
    public async Task RunCycle_WhenInferenceThrows_LogsErrorWithDurationAndDetails()
    {
        var inference = new FakeLlmInferenceService(throwException: new InvalidOperationException("model error"));
        var log = new FakeViewerLogService();
        var svc = new LlmCheckService(inference, log);
        var vm = MakeVm("Excel dashboard");
        vm.Screenshot = ComputerViewModelTests.CreateMinimalBitmap();

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

        await svc.RunCycleAsync([vm]);

        Assert.False(vm.IsLlmChecking);
    }

    // ---- Fake ----

    private class FakeLlmInferenceService : ILlmInferenceService
    {
        private readonly LlmCheckResult? _result;
        private readonly Exception? _throw;
        private readonly TaskCompletionSource _callTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            _callTcs.TrySetResult();
            if (_throw is not null)
                throw _throw;
            OnBeforeReturn?.Invoke();
            return Task.FromResult(_result ?? new LlmCheckResult(true, "ok", false, DateTime.Now));
        }

        public Task WaitForCallAsync(CancellationToken cancellationToken)
            => _callTcs.Task.WaitAsync(cancellationToken);

        public Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct)
            => Task.FromResult<LlmRuntimeLoadException?>(null);

        public int ResetCalls { get; private set; }
        public void Reset() => ResetCalls++;
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
}
