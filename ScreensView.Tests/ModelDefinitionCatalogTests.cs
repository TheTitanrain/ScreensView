using ScreensView.Viewer.Models;

namespace ScreensView.Tests;

/// <summary>
/// Tests that mutate the static <see cref="ModelDefinition.Available"/> via <c>Initialize</c>.
/// Each resets the static in <see cref="Dispose"/> so reads don't leak into other test classes.
/// (Assembly-level parallelization is disabled — see TestAssembly.cs.)
/// </summary>
public class ModelDefinitionCatalogTests : IDisposable
{
    public void Dispose() => ModelDefinition.ResetForTests();

    private static ModelDefinition Model(string id, string fileName = "f.gguf") =>
        new(id, id, fileName, "https://example.com/" + fileName, null, null);

    [Fact]
    public void Available_BeforeInitialize_IsBuiltIn()
    {
        Assert.Equal(ModelDefinition.BuiltIn, ModelDefinition.Available);
    }

    [Fact]
    public void Initialize_SetsAvailable()
    {
        var custom = new List<ModelDefinition> { Model("a"), Model("b", "g.gguf") };

        ModelDefinition.Initialize(custom);

        Assert.Equal(custom, ModelDefinition.Available);
    }

    [Fact]
    public void Initialize_IsIdempotent_FirstWins()
    {
        var first = new List<ModelDefinition> { Model("first") };
        var second = new List<ModelDefinition> { Model("second") };

        ModelDefinition.Initialize(first);
        ModelDefinition.Initialize(second);

        Assert.Equal(first, ModelDefinition.Available);
    }

    [Fact]
    public void Initialize_WithEmptyCatalog_FallsBackToBuiltIn()
    {
        ModelDefinition.Initialize(new List<ModelDefinition>());

        Assert.Equal(ModelDefinition.BuiltIn, ModelDefinition.Available);
    }

    [Fact]
    public void Default_ResolvesByDefaultModelId_NotByPosition()
    {
        // Custom catalog whose FIRST entry is not the default model.
        var custom = new List<ModelDefinition>
        {
            Model("zzz-other"),
            ModelDefinition.BuiltIn.Single(m => m.Id == ModelDefinition.DefaultModelId),
        };

        ModelDefinition.Initialize(custom);

        Assert.Equal(ModelDefinition.DefaultModelId, ModelDefinition.Default.Id);
    }

    [Fact]
    public void Default_WhenDefaultIdAbsent_FallsBackToFirst()
    {
        var custom = new List<ModelDefinition> { Model("only-one") };

        ModelDefinition.Initialize(custom);

        Assert.Equal("only-one", ModelDefinition.Default.Id);
    }
}
