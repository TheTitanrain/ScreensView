using ScreensView.Viewer.Models;

namespace ScreensView.Tests;

public class ModelDefinitionTests
{
    [Fact]
    public void Default_UsesLlavaV15Model()
    {
        Assert.Equal("llava-v1.5-7b-q4", ModelDefinition.Default.Id);
        Assert.Equal("llava-v1.5-7b-Q4_K_M.gguf", ModelDefinition.Default.FileName);
        Assert.Equal("llava-v1.5-7b-mmproj-model-f16.gguf", ModelDefinition.Default.ProjectorFileName);
    }

    [Fact]
    public void Available_ContainsGemma4E2BModelWithDedicatedProjectorFileName()
    {
        var gemma = Assert.Single(ModelDefinition.Available, model => model.Id == "gemma-4-e2b-q4");

        Assert.Equal("Gemma 4 E2B Q4_K_M (~3.0 + 0.6 GB) [experimental]", gemma.DisplayName);
        Assert.Equal("gemma-4-E2B-it-Q4_K_M.gguf", gemma.FileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-Q4_K_M.gguf",
            gemma.DownloadUrl);
        Assert.Equal("gemma-4-E2B-it-mmproj-F16.gguf", gemma.ProjectorFileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/mmproj-F16.gguf",
            gemma.ProjectorDownloadUrl);
    }

    [Fact]
    public void Available_ContainsQwen35_0_8BModel()
    {
        var qwen = Assert.Single(ModelDefinition.Available, model => model.Id == "qwen3.5-0.8b-q4");

        Assert.Equal("Qwen3.5-0.8B Q4_K_M (~0.5 + 0.2 GB) [experimental]", qwen.DisplayName);
        Assert.Equal("Qwen3.5-0.8B-Q4_K_M.gguf", qwen.FileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf",
            qwen.DownloadUrl);
        Assert.Equal("mmproj-F16.gguf", qwen.ProjectorFileName);
        Assert.Equal(
            "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/mmproj-F16.gguf",
            qwen.ProjectorDownloadUrl);
    }
}
