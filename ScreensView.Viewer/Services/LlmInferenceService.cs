using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

public interface ILlmInferenceService
{
    Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct);
    Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct);
    void Reset(); // disposes cached runtime so next AnalyzeAsync loads from current model path
}

internal interface ILlmVisionRuntime : IDisposable
{
    Task<string> InferAsync(byte[] imageBytes, string prompt, CancellationToken ct);
}

internal interface ILlmVisionRuntimeFactory
{
    Task<ILlmVisionRuntime> CreateAsync(string modelPath, string projectorPath, CancellationToken ct);
}

public class LlmInferenceService : ILlmInferenceService, IDisposable
{
    private readonly IModelDownloadService _download;
    private readonly ILlmVisionRuntimeFactory _runtimeFactory;
    private readonly IViewerLogService _log;
    private ILlmVisionRuntime? _runtime;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LlmInferenceService(
        IModelDownloadService download,
        ILlamaServerBinaryService binaryService,
        Func<string> getBackend)
        : this(download,
               new LlamaServerVisionRuntimeFactory(
                   binaryService,
                   new LlamaServerProcessService(),
                   getBackend),
               null)
    {
    }

    internal LlmInferenceService(
        IModelDownloadService download,
        ILlmVisionRuntimeFactory runtimeFactory,
        IViewerLogService? log = null)
    {
        _download = download;
        _runtimeFactory = runtimeFactory;
        _log = log ?? new NullViewerLogService();
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_runtime is not null)
            return;

        _log.LogInfo(
            "Llm.EnsureLoaded.Start",
            $"Loading runtime. ModelPath='{_download.ModelPath}', ProjectorPath='{_download.ProjectorPath}'.");

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null)
                return;

            try
            {
                _runtime = await _runtimeFactory.CreateAsync(
                    _download.ModelPath,
                    _download.ProjectorPath,
                    ct).ConfigureAwait(false);
                _log.LogInfo("Llm.EnsureLoaded.Success", "Runtime loaded successfully.");
            }
            catch (LlmRuntimeLoadException ex)
            {
                _log.LogError("Llm.EnsureLoaded.Failed", ex.DiagnosticMessage, ex);
                throw;
            }
            catch (Exception ex)
            {
                var wrapped = new LlmRuntimeLoadException(
                    LlmRuntimeLoadStage.RuntimeInit,
                    "Ошибка инициализации LLM runtime",
                    $"Unexpected runtime initialization failure. {ex.Message}",
                    _download.ModelPath,
                    _download.ProjectorPath,
                    ex);
                _log.LogError("Llm.EnsureLoaded.Failed", wrapped.DiagnosticMessage, wrapped);
                throw wrapped;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct)
    {
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmRuntimeLoadException ex)
        {
            return ex;
        }
    }

    public async Task<LlmCheckResult> AnalyzeAsync(
        BitmapImage screenshot, string description, CancellationToken ct)
    {
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            var prepared = await InferenceImagePreprocessor.PrepareAsync(screenshot).ConfigureAwait(false);
            var prompt = BuildPrompt(description);
            var rawResponse = await _runtime!.InferAsync(prepared.JpegBytes, prompt, ct).ConfigureAwait(false);
            var normalizedResponse = NormalizeModelResponse(rawResponse);

            ParsedModelResponse parsed;
            try
            {
                parsed = ParseModelResponse(normalizedResponse);
            }
            catch (FormatException ex)
            {
                _log.LogWarning(
                    "LlmInferenceService.RawResponseParseError",
                    $"description='{description}', outcome=parse_error, rawResponse={FormatResponseForLog(rawResponse)}, normalizedResponse={FormatResponseForLog(normalizedResponse)}, error='{ex.Message}'.");
                return new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
            }

            if (parsed.UsedFallbackExplanation)
            {
                _log.LogWarning(
                    "LlmInferenceService.RawResponseEmptyExplanation",
                    $"description='{description}', outcome={(parsed.IsMatch ? "match" : "mismatch")}, rawResponse={FormatResponseForLog(rawResponse)}, normalizedResponse={FormatResponseForLog(normalizedResponse)}.");
            }

            if (!parsed.IsMatch)
            {
                _log.LogInfo(
                    "LlmInferenceService.RawResponseMismatch",
                    $"description='{description}', outcome=mismatch, rawResponse={FormatResponseForLog(rawResponse)}, normalizedResponse={FormatResponseForLog(normalizedResponse)}.");
            }

            return new LlmCheckResult(parsed.IsMatch, parsed.Explanation, IsError: false, DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmRuntimeLoadException ex)
        {
            return new LlmCheckResult(false, ex.UserMessage, IsError: true, DateTime.Now);
        }
        catch (Exception ex)
        {
            return new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
        }
    }

    public void Reset()
    {
        _runtime?.Dispose();
        _runtime = null;
    }

    public void Dispose()
    {
        _runtime?.Dispose();
        _loadLock.Dispose();
    }

    private static string BuildPrompt(string description) =>
        $"Answer YES or NO. Match by the general type of screen and its structural layout. Ignore colors, exact text, times, numbers, names, plates, and whether rows are filled or empty. Does this screenshot match the description? '{description}' Give one short reason.";

    private static string NormalizeModelResponse(string rawResponse)
    {
        var normalized = rawResponse.Trim();

        // Strip <think>...</think> blocks (Qwen3 reasoning chain)
        normalized = Regex.Replace(
            normalized,
            @"<think>[\s\S]*?</think>",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        normalized = Regex.Replace(
            normalized,
            @"\[\s*end of text\s*\]\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        return normalized;
    }

    private static ParsedModelResponse ParseModelResponse(string normalized)
    {

        var match = Regex.Match(
            normalized,
            @"^(YES|NO)\b[\s:,-]*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        if (!match.Success)
            throw new FormatException("Model response did not start with YES or NO.");

        var isMatch = string.Equals(match.Groups[1].Value, "YES", StringComparison.OrdinalIgnoreCase);
        var explanation = match.Groups[2].Value.Trim();
        var usedFallbackExplanation = false;
        if (string.IsNullOrWhiteSpace(explanation))
        {
            explanation = "No explanation provided.";
            usedFallbackExplanation = true;
        }

        return new ParsedModelResponse(isMatch, explanation, usedFallbackExplanation);
    }

    private static string FormatResponseForLog(string response)
        => JsonSerializer.Serialize(response);
}

internal sealed record ParsedModelResponse(bool IsMatch, string Explanation, bool UsedFallbackExplanation);

internal sealed class LlamaServerVisionRuntimeFactory : ILlmVisionRuntimeFactory
{
    private readonly ILlamaServerBinaryService _binary;
    private readonly ILlamaServerProcessService _process;
    private readonly Func<string> _getBackend;

    public LlamaServerVisionRuntimeFactory(
        ILlamaServerBinaryService binary,
        ILlamaServerProcessService process,
        Func<string> getBackend)
    {
        _binary = binary;
        _process = process;
        _getBackend = getBackend;
    }

    public async Task<ILlmVisionRuntime> CreateAsync(
        string modelPath, string projectorPath, CancellationToken ct)
    {
        var backend = _getBackend();
        var backendCheck = _binary.CheckInstallation(backend);

        if (!backendCheck.IsReady)
            throw new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.Backend,
                backendCheck.UserMessage,
                $"llama-server backend '{backend}' is {backendCheck.State}. Missing artifacts: {string.Join(", ", backendCheck.MissingArtifacts)}",
                modelPath,
                projectorPath);

        var exePath = _binary.GetExePath(backend);

        string baseUrl;
        try
        {
            baseUrl = await _process.StartAsync(exePath, modelPath, projectorPath, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            throw new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ModelLoad,
                ex.Message,
                ex.Message,
                modelPath,
                projectorPath,
                ex);
        }
        catch (Exception ex)
        {
            throw new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ModelLoad,
                "Не удалось запустить llama-server.",
                $"Failed to start llama-server: {ex.Message}",
                modelPath,
                projectorPath,
                ex);
        }

        return new LlamaServerVisionRuntime(baseUrl, _process);
    }
}

internal sealed class LlamaServerVisionRuntime : ILlmVisionRuntime
{
    private readonly string _baseUrl;
    private readonly ILlamaServerProcessService _process;
    private readonly HttpClient _http;

    public LlamaServerVisionRuntime(string baseUrl, ILlamaServerProcessService process)
        : this(baseUrl, process, null)
    {
    }

    internal LlamaServerVisionRuntime(string baseUrl, ILlamaServerProcessService process, HttpClient? httpClient)
    {
        _baseUrl = baseUrl;
        _process = process;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<string> InferAsync(byte[] imageBytes, string prompt, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(imageBytes);
        var dataUrl = $"data:image/jpeg;base64,{base64}";

        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage("user",
                [
                    new ContentPart("image_url", ImageUrl: new ImageUrlValue(dataUrl)),
                    new ContentPart("text", Text: prompt)
                ])
            ],
            MaxTokens: 64,
            Temperature: 0.0f);

        using var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/v1/chat/completions", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty response from llama-server.");

        return result.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("No choices in llama-server response.");
    }

    public void Dispose()
    {
        _process.StopAsync().GetAwaiter().GetResult();
        _http.Dispose();
    }

    // Request/response DTOs
    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] float Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] List<ContentPart> Content);

    private sealed record ContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("image_url")] ImageUrlValue? ImageUrl = null);

    private sealed record ImageUrlValue(
        [property: JsonPropertyName("url")] string Url);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice> Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatChoiceMessage Message);

    private sealed record ChatChoiceMessage(
        [property: JsonPropertyName("content")] string Content);
}
