using System.Reflection;

namespace ScreensView.Tests;

public class ViewerStartupOptionsTests
{
    [Fact]
    public void Parse_WhenConnectionsFileArgumentIsMissing_ReturnsNoOverride()
    {
        var result = ParseOptions(["ScreensView.Viewer.exe"]);

        Assert.True(GetRequiredBoolean(result, "IsValid"));
        Assert.False(GetRequiredBoolean(result, "HasConnectionsFileOverride"));
        Assert.Null(GetNullableString(result, "ConnectionsFilePath"));
    }

    [Fact]
    public void Parse_WhenConnectionsFileUsesAbsolutePath_ReturnsOverride()
    {
        var result = ParseOptions(["ScreensView.Viewer.exe", "--connections-file", @"C:\Shared\connections.svc"]);

        Assert.True(GetRequiredBoolean(result, "IsValid"));
        Assert.True(GetRequiredBoolean(result, "HasConnectionsFileOverride"));
        Assert.Equal(@"C:\Shared\connections.svc", GetNullableString(result, "ConnectionsFilePath"));
    }

    [Fact]
    public void Parse_WhenConnectionsFileUsesUncPath_ReturnsOverride()
    {
        var result = ParseOptions(["ScreensView.Viewer.exe", "--connections-file", @"\\server\share\connections.svc"]);

        Assert.True(GetRequiredBoolean(result, "IsValid"));
        Assert.True(GetRequiredBoolean(result, "HasConnectionsFileOverride"));
        Assert.Equal(@"\\server\share\connections.svc", GetNullableString(result, "ConnectionsFilePath"));
    }

    [Fact]
    public void Parse_WhenConnectionsFileUsesRelativePath_ReturnsError()
    {
        var result = ParseOptions(["ScreensView.Viewer.exe", "--connections-file", @".\connections.svc"]);

        Assert.False(GetRequiredBoolean(result, "IsValid"));
        Assert.Contains("absolute", GetNullableString(result, "ErrorMessage"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenConnectionsFileIsRepeated_ReturnsError()
    {
        var result = ParseOptions([
            "ScreensView.Viewer.exe",
            "--connections-file", @"C:\one.svc",
            "--connections-file", @"C:\two.svc"
        ]);

        Assert.False(GetRequiredBoolean(result, "IsValid"));
        Assert.Contains("multiple", GetNullableString(result, "ErrorMessage"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenConnectionsFileValueIsMissing_ReturnsError()
    {
        var result = ParseOptions(["ScreensView.Viewer.exe", "--connections-file"]);

        Assert.False(GetRequiredBoolean(result, "IsValid"));
        Assert.Contains("value", GetNullableString(result, "ErrorMessage"), StringComparison.OrdinalIgnoreCase);
    }

    private static object ParseOptions(IReadOnlyList<string> args)
    {
        var type = Type.GetType("ScreensView.Viewer.Services.ViewerStartupOptionsParser, ScreensView.Viewer", throwOnError: false);
        Assert.NotNull(type);

        var method = type!.GetMethod("Parse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return method!.Invoke(null, [args.ToArray()])
               ?? throw new InvalidOperationException("Parse returned null.");
    }

    private static bool GetRequiredBoolean(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(instance));
    }

    private static string? GetNullableString(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(property);
        return (string?)property!.GetValue(instance);
    }
}
