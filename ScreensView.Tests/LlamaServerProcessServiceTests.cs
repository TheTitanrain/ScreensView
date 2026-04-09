using System.Reflection;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class LlamaServerProcessServiceTests
{
    [Fact]
    public void BuildArgs_UsesLowVramFastProfile()
    {
        var args = InvokeBuildArgs(
            @"C:\models\llava-v1.5-7b-Q4_K_M.gguf",
            @"C:\models\llava-v1.5-7b-mmproj-model-f16.gguf",
            9669);

        Assert.Contains("--parallel 1", args);
        Assert.Contains("--ctx-size 4096", args);
        Assert.Contains("--reasoning-budget 0", args);
    }

    [Fact]
    public void BuildArgs_ForDynamicVisionModel_AddsImageMaxTokens()
    {
        var args = InvokeBuildArgs(
            @"C:\models\Qwen3-VL-2B-Instruct-Q4_K_M.gguf",
            @"C:\models\qwen3-vl-2b-instruct-mmproj-F16.gguf",
            9669);

        Assert.Contains("--image-max-tokens 512", args);
    }

    [Fact]
    public void BuildArgs_ForLlavaModel_DoesNotAddImageMaxTokens()
    {
        var args = InvokeBuildArgs(
            @"C:\models\llava-v1.5-7b-Q4_K_M.gguf",
            @"C:\models\llava-v1.5-7b-mmproj-model-f16.gguf",
            9669);

        Assert.DoesNotContain("--image-max-tokens", args);
    }

    private static string InvokeBuildArgs(string modelPath, string projectorPath, int port)
    {
        var method = typeof(LlamaServerProcessService).GetMethod(
            "BuildArgs",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        return (string)method!.Invoke(null, [modelPath, projectorPath, port])!;
    }
}
