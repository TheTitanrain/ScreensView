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
}
