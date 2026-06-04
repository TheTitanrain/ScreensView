using System.Text.Json;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ModelCatalogServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }

    private ModelCatalogService Service() => new(_file);

    private static string JsonOf(params string[] entries) => "[" + string.Join(",", entries) + "]";

    private static string Entry(
        string id = "custom-x",
        string displayName = "Custom X",
        string fileName = "custom-x.gguf",
        string downloadUrl = "https://example.com/custom-x.gguf",
        string? projectorFileName = null,
        string? projectorDownloadUrl = null)
    {
        var proj = projectorFileName is null
            ? ""
            : $",\"ProjectorFileName\":\"{projectorFileName}\",\"ProjectorDownloadUrl\":\"{projectorDownloadUrl}\"";
        return $"{{\"Id\":\"{id}\",\"DisplayName\":\"{displayName}\",\"FileName\":\"{fileName}\",\"DownloadUrl\":\"{downloadUrl}\"{proj}}}";
    }

    [Fact]
    public void LoadOrSeed_WhenFileMissing_ReturnsBuiltInsAndWritesSeed()
    {
        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn, result);
        Assert.True(File.Exists(_file));

        var seeded = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(_file));
        Assert.NotNull(seeded);
        Assert.Equal(ModelDefinition.BuiltIn.Count, seeded!.Count);
    }

    [Fact]
    public void LoadOrSeed_RoundTrip_SeedThenReadBack_EqualsBuiltIns()
    {
        Service().LoadOrSeed();           // seeds the file
        var result = Service().LoadOrSeed(); // reads it back, merges

        Assert.Equal(ModelDefinition.BuiltIn.Count, result.Count);
        foreach (var builtIn in ModelDefinition.BuiltIn)
            Assert.Contains(result, m => m.Id == builtIn.Id && m.FileName == builtIn.FileName);
    }

    [Fact]
    public void LoadOrSeed_NewId_IsAppended()
    {
        File.WriteAllText(_file, JsonOf(Entry()));

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn.Count + 1, result.Count);
        Assert.Contains(result, m => m.Id == "custom-x" && m.FileName == "custom-x.gguf");
        foreach (var builtIn in ModelDefinition.BuiltIn)
            Assert.Contains(result, m => m.Id == builtIn.Id);
    }

    [Fact]
    public void LoadOrSeed_SameIdAsBuiltIn_OverridesInPlace()
    {
        File.WriteAllText(_file, JsonOf(Entry(
            id: "llava-v1.5-7b-q4",
            displayName: "My Custom Llava",
            fileName: "llava-v1.5-7b-Q4_K_M.gguf",
            downloadUrl: "https://mirror.example.com/llava.gguf")));

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn.Count, result.Count); // override, not append
        var llava = Assert.Single(result, m => m.Id == "llava-v1.5-7b-q4");
        Assert.Equal("My Custom Llava", llava.DisplayName);
        Assert.Equal("https://mirror.example.com/llava.gguf", llava.DownloadUrl);
    }

    [Fact]
    public void LoadOrSeed_EmptyArray_ReturnsBuiltIns_FileUntouched()
    {
        File.WriteAllText(_file, "[]");
        var before = File.ReadAllBytes(_file);

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn, result);
        Assert.Equal(before, File.ReadAllBytes(_file));
    }

    [Theory]
    [InlineData("")]                 // empty id
    [InlineData("   ")]              // whitespace id
    public void LoadOrSeed_BlankRequiredField_EntryDropped(string id)
    {
        File.WriteAllText(_file, JsonOf(Entry(id: id)));

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn.Count, result.Count); // nothing added
    }

    [Theory]
    [InlineData("..\\\\evil.gguf")]
    [InlineData("sub/dir/x.gguf")]
    [InlineData("sub\\\\dir\\\\x.gguf")]
    [InlineData("C:\\\\windows\\\\x.gguf")]
    public void LoadOrSeed_PathTraversalFileName_EntryDropped(string fileName)
    {
        File.WriteAllText(_file, JsonOf(Entry(id: "evil", fileName: fileName)));

        var result = Service().LoadOrSeed();

        Assert.DoesNotContain(result, m => m.Id == "evil");
        Assert.Equal(ModelDefinition.BuiltIn.Count, result.Count);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x.gguf")]
    [InlineData("relative/path.gguf")]
    [InlineData("not a url")]
    public void LoadOrSeed_InvalidDownloadUrlScheme_EntryDropped(string url)
    {
        File.WriteAllText(_file, JsonOf(Entry(id: "bad-url", downloadUrl: url)));

        var result = Service().LoadOrSeed();

        Assert.DoesNotContain(result, m => m.Id == "bad-url");
    }

    [Fact]
    public void LoadOrSeed_DuplicateFileNameAcrossEntries_SecondDropped()
    {
        File.WriteAllText(_file, JsonOf(
            Entry(id: "a", fileName: "shared.gguf", downloadUrl: "https://example.com/a.gguf"),
            Entry(id: "b", fileName: "shared.gguf", downloadUrl: "https://example.com/b.gguf")));

        var result = Service().LoadOrSeed();

        Assert.Contains(result, m => m.Id == "a");
        Assert.DoesNotContain(result, m => m.Id == "b");
    }

    [Fact]
    public void LoadOrSeed_FileNameCollidesWithBuiltIn_EntryDropped()
    {
        // built-in llava already owns this artifact name
        File.WriteAllText(_file, JsonOf(Entry(id: "intruder", fileName: "llava-v1.5-7b-Q4_K_M.gguf")));

        var result = Service().LoadOrSeed();

        Assert.DoesNotContain(result, m => m.Id == "intruder");
    }

    [Fact]
    public void LoadOrSeed_CaseInsensitiveDuplicateId_FirstWins()
    {
        File.WriteAllText(_file, JsonOf(
            Entry(id: "dup", displayName: "First", fileName: "first.gguf", downloadUrl: "https://example.com/1.gguf"),
            Entry(id: "DUP", displayName: "Second", fileName: "second.gguf", downloadUrl: "https://example.com/2.gguf")));

        var result = Service().LoadOrSeed();

        var entry = Assert.Single(result, m => string.Equals(m.Id, "dup", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("First", entry.DisplayName);
    }

    [Fact]
    public void LoadOrSeed_ProjectorOnlyFileName_EntryDropped()
    {
        var json = $"[{{\"Id\":\"half\",\"DisplayName\":\"Half\",\"FileName\":\"half.gguf\"," +
                   $"\"DownloadUrl\":\"https://example.com/half.gguf\",\"ProjectorFileName\":\"proj.gguf\"}}]";
        File.WriteAllText(_file, json);

        var result = Service().LoadOrSeed();

        Assert.DoesNotContain(result, m => m.Id == "half");
    }

    [Fact]
    public void LoadOrSeed_ValidProjectorPair_Accepted()
    {
        File.WriteAllText(_file, JsonOf(Entry(
            id: "with-proj",
            fileName: "wp.gguf",
            downloadUrl: "https://example.com/wp.gguf",
            projectorFileName: "wp-mmproj.gguf",
            projectorDownloadUrl: "https://example.com/wp-mmproj.gguf")));

        var result = Service().LoadOrSeed();

        var entry = Assert.Single(result, m => m.Id == "with-proj");
        Assert.Equal("wp-mmproj.gguf", entry.ProjectorFileName);
    }

    [Fact]
    public void LoadOrSeed_OneMalformedElementAmongValid_SkipsBadKeepsGood()
    {
        // second element is not an object — must be skipped, not abort the load
        var json = "[" + Entry(id: "good", fileName: "good.gguf", downloadUrl: "https://example.com/good.gguf")
                       + ",12345]";
        File.WriteAllText(_file, json);

        var result = Service().LoadOrSeed();

        Assert.Contains(result, m => m.Id == "good");
    }

    [Fact]
    public void LoadOrSeed_UnknownExtraFields_Ignored()
    {
        var json = "[{\"Id\":\"x\",\"DisplayName\":\"X\",\"FileName\":\"x.gguf\"," +
                   "\"DownloadUrl\":\"https://example.com/x.gguf\",\"Notes\":\"hello\",\"Size\":42}]";
        File.WriteAllText(_file, json);

        var result = Service().LoadOrSeed();

        Assert.Contains(result, m => m.Id == "x");
    }

    [Fact]
    public void LoadOrSeed_MalformedDocument_ReturnsBuiltIns_FileUntouched()
    {
        File.WriteAllText(_file, "{ this is not valid json ]");
        var before = File.ReadAllBytes(_file);

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn, result);
        Assert.Equal(before, File.ReadAllBytes(_file)); // byte-for-byte unchanged
    }

    [Fact]
    public void LoadOrSeed_RootIsNotArray_ReturnsBuiltIns_FileUntouched()
    {
        File.WriteAllText(_file, "{\"Id\":\"x\"}");
        var before = File.ReadAllBytes(_file);

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn, result);
        Assert.Equal(before, File.ReadAllBytes(_file));
    }

    [Fact]
    public void LoadOrSeed_AllInvalid_ReturnsBuiltIns_FileUntouched()
    {
        File.WriteAllText(_file, JsonOf(Entry(id: ""), Entry(id: "  ", fileName: "")));
        var before = File.ReadAllBytes(_file);

        var result = Service().LoadOrSeed();

        Assert.Equal(ModelDefinition.BuiltIn, result);
        Assert.Equal(before, File.ReadAllBytes(_file));
    }
}
