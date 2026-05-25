using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Services;

public interface ITelegramNotificationService
{
    void Start(IReadOnlyList<ComputerViewModel> computers);
    void Stop();
    void UpdateSettings(string botToken, string chatId, IReadOnlyList<string> scheduleTimes);
    Task<string> SendNowAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct = default);
}

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly AgentHttpClient? _agentHttp;
    private readonly IViewerLogService _log;
    private readonly HttpClient _telegramClient;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private readonly object _schedLock = new();

    private volatile string _botToken = string.Empty;
    private volatile string _chatId = string.Empty;
    private List<string> _scheduleTimes = [];
    private string? _lastFiredDateTime;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private IReadOnlyList<ComputerViewModel>? _computers;

    public TelegramNotificationService(AgentHttpClient? agentHttp, IViewerLogService? log = null)
    {
        _agentHttp = agentHttp;
        _log = log ?? new NullViewerLogService();
        _telegramClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public void Start(IReadOnlyList<ComputerViewModel> computers)
    {
        if (_cts is not null)
            return;

        _computers = computers;
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
        _log.LogInfo("TelegramNotificationService.Start", $"Started for {computers.Count} computers.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
        _log.LogInfo("TelegramNotificationService.Stop", "Stopped.");
    }

    public void UpdateSettings(string botToken, string chatId, IReadOnlyList<string> scheduleTimes)
    {
        _botToken = botToken;
        _chatId = chatId;
        lock (_schedLock)
            _scheduleTimes = [.. scheduleTimes];
    }

    public Task<string> SendNowAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct = default)
    {
        return RunCycleExclusiveAsync(computers, ct);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
                continue;

            var now = DateTime.Now;
            var nowMinute = now.ToString("HH:mm");
            var nowDateTime = now.ToString("yyyy-MM-dd HH:mm");

            bool shouldFire;
            lock (_schedLock)
                shouldFire = _scheduleTimes.Contains(nowMinute) && _lastFiredDateTime != nowDateTime;

            if (!shouldFire)
                continue;

            _lastFiredDateTime = nowDateTime;
            _ = RunCycleExclusiveAsync(_computers ?? [], ct);
        }
    }

    private async Task<string> RunCycleExclusiveAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct)
    {
        if (!await _cycleGate.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            _log.LogWarning("TelegramNotificationService", "Previous cycle still running, skipping.");
            return string.Empty;
        }

        try
        {
            return await RunCycleAsync(computers, ct);
        }
        catch (Exception ex)
        {
            _log.LogError("TelegramNotificationService", $"Cycle error: {ex.Message}");
            return string.Format(LocalizationService.Get("Str.Telegram.Error"), ex.Message);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    private async Task<string> RunCycleAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct)
    {
        if (_agentHttp is null)
            return string.Empty;

        // Snapshot: online computers to attempt screenshot, enabled-but-not-online for the failed list
        var (onlineVms, failedNames) = System.Windows.Application.Current.Dispatcher.Invoke(
            () =>
            {
                var online = computers
                    .Where(vm => vm.IsEnabled && vm.Status == ComputerStatus.Online)
                    .ToList();
                var failed = computers
                    .Where(vm => vm.IsEnabled && vm.Status != ComputerStatus.Online)
                    .Select(vm => vm.Name)
                    .ToList();
                return (online, failed);
            });

        var photos = new List<(string Name, byte[] Bytes)>(onlineVms.Count);
        foreach (var vm in onlineVms)
        {
            try
            {
                var response = await _agentHttp.GetScreenshotAsync(vm.ToConfig(), ct);
                if (response?.ImageBase64 is not null)
                    photos.Add((vm.Name, Convert.FromBase64String(response.ImageBase64)));
                else
                    failedNames.Add(vm.Name);
            }
            catch (Exception ex)
            {
                _log.LogWarning("TelegramNotificationService", $"Failed to get screenshot from '{vm.Name}': {ex.Message}");
                failedNames.Add(vm.Name);
            }
        }

        if (photos.Count == 0 && failedNames.Count == 0)
            return LocalizationService.Get("Str.Telegram.NoOnline");

        foreach (var (name, bytes) in photos)
            await PostSendPhotoAsync(bytes, name, ct);
        int totalSent = photos.Count;

        if (failedNames.Count > 0)
        {
            var label = DateTime.Now.ToString("HH:mm");
            var failedText = $"ScreensView — {label}\n"
                + LocalizationService.Get("Str.Telegram.NoScreenshot") + ":\n"
                + string.Join("\n", failedNames.Select(n => $"• {n}"));
            await PostSendMessageAsync(failedText, ct);
        }

        return string.Format(LocalizationService.Get("Str.Telegram.SentOk"), totalSent);
    }

    private async Task PostSendPhotoAsync(byte[] bytes, string caption, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_botToken}/sendPhoto";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_chatId), "chat_id");
        content.Add(new StringContent(caption), "caption");
        var photoContent = new ByteArrayContent(bytes);
        photoContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(photoContent, "photo", "photo.jpg");

        var response = await _telegramClient.PostAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("TelegramNotificationService.PostSendPhoto", $"HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private async Task PostSendMessageAsync(string text, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
        var payload = JsonSerializer.Serialize(new { chat_id = _chatId, text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _telegramClient.PostAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("TelegramNotificationService.PostSendMessage", $"HTTP {(int)response.StatusCode}: {body}");
        }
    }

}
