using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class BulkComputerParserTests
{
    private static readonly ISet<string> NoExisting =
        new ReadOnlySetWrapper<string>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void ParseHosts_SingleLine_ReturnsOneConfig()
    {
        var result = BulkComputerParser.ParseHosts("192.168.1.1", 5443, NoExisting);

        Assert.Single(result);
        Assert.Equal("192.168.1.1", result[0].Host);
        Assert.Equal("192.168.1.1", result[0].Name);
        Assert.Equal(5443, result[0].Port);
        Assert.True(result[0].IsEnabled);
        Assert.NotEmpty(result[0].ApiKey);
    }

    [Fact]
    public void ParseHosts_MultipleLines_ReturnsOnePerNonEmptyLine()
    {
        var input = "host1\nhost2\nhost3";
        var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseHosts_BlankLinesIgnored()
    {
        var input = "host1\n\n  \nhost2";
        var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseHosts_CrLfLineEndings_Handled()
    {
        var input = "host1\r\nhost2\r\nhost3";
        var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseHosts_IntraListDuplicates_FirstOccurrenceWins()
    {
        var input = "192.168.1.1\n192.168.1.1\n192.168.1.2";
        var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.NotEqual(result[0].Host, result[1].Host));
    }

    [Fact]
    public void ParseHosts_DuplicateCaseInsensitive_Deduplicated()
    {
        var input = "MyHost\nmyhost";
        var result = BulkComputerParser.ParseHosts(input, 5443, NoExisting);
        Assert.Single(result);
    }

    [Fact]
    public void ParseHosts_ExistingHostSkipped()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.1" };
        var result = BulkComputerParser.ParseHosts("192.168.1.1\n192.168.1.2", 5443, existing);
        Assert.Single(result);
        Assert.Equal("192.168.1.2", result[0].Host);
    }

    [Fact]
    public void ParseHosts_UniqueApiKeyPerEntry()
    {
        var result = BulkComputerParser.ParseHosts("host1\nhost2", 5443, NoExisting);
        Assert.NotEqual(result[0].ApiKey, result[1].ApiKey);
    }

    [Fact]
    public void ParseHosts_EmptyInput_ReturnsEmpty()
    {
        var result = BulkComputerParser.ParseHosts("", 5443, NoExisting);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseIpRange_ValidRange_ReturnsCorrectCount()
    {
        var result = BulkComputerParser.ParseIpRange(
            "192.168.1.10", "192.168.1.12", 5443, NoExisting, out var error);

        Assert.Null(error);
        Assert.Equal(3, result.Count);
        Assert.Equal("192.168.1.10", result[0].Host);
        Assert.Equal("192.168.1.11", result[1].Host);
        Assert.Equal("192.168.1.12", result[2].Host);
    }

    [Fact]
    public void ParseIpRange_SingleAddress_ReturnsOne()
    {
        var result = BulkComputerParser.ParseIpRange(
            "10.0.0.5", "10.0.0.5", 5443, NoExisting, out var error);

        Assert.Null(error);
        Assert.Single(result);
    }

    [Fact]
    public void ParseIpRange_InvalidStartIp_ReturnsError()
    {
        BulkComputerParser.ParseIpRange("not-an-ip", "10.0.0.5", 5443, NoExisting, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseIpRange_InvalidEndIp_ReturnsError()
    {
        BulkComputerParser.ParseIpRange("10.0.0.1", "999.999.999.999", 5443, NoExisting, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseIpRange_IPv6_ReturnsError()
    {
        BulkComputerParser.ParseIpRange(
            "::1", "::2", 5443, NoExisting, out var error);
        Assert.NotNull(error);
        Assert.Contains("IPv4", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseIpRange_EndLessThanStart_ReturnsError()
    {
        BulkComputerParser.ParseIpRange(
            "192.168.1.20", "192.168.1.10", 5443, NoExisting, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseIpRange_Exactly255Addresses_Allowed()
    {
        // 192.168.1.0 to 192.168.1.254 = 255 addresses
        var result = BulkComputerParser.ParseIpRange(
            "192.168.1.0", "192.168.1.254", 5443, NoExisting, out var error);
        Assert.Null(error);
        Assert.Equal(255, result.Count);
    }

    [Fact]
    public void ParseIpRange_256Addresses_ReturnsError()
    {
        // 192.168.1.0 to 192.168.1.255 = 256 addresses
        BulkComputerParser.ParseIpRange(
            "192.168.1.0", "192.168.1.255", 5443, NoExisting, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseIpRange_ExistingHostsSkipped()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.11" };
        var result = BulkComputerParser.ParseIpRange(
            "192.168.1.10", "192.168.1.12", 5443, existing, out var error);
        Assert.Null(error);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, c => c.Host == "192.168.1.11");
    }

    [Fact]
    public void GenerateApiKey_IsHexString_Length64()
    {
        var key = BulkComputerParser.GenerateApiKey();
        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]+$", key);
    }

    [Fact]
    public void GenerateApiKey_EachCallUnique()
    {
        var key1 = BulkComputerParser.GenerateApiKey();
        var key2 = BulkComputerParser.GenerateApiKey();
        Assert.NotEqual(key1, key2);
    }
}

/// <summary>Read-only wrapper around an ISet — mutation methods throw NotSupportedException.</summary>
file sealed class ReadOnlySetWrapper<T>(ISet<T> inner) : ISet<T>
{
    public int Count => inner.Count;
    public bool IsReadOnly => true;
    public bool Contains(T item) => inner.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((System.Collections.IEnumerable)inner).GetEnumerator();
    public bool IsSubsetOf(IEnumerable<T> other) => inner.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<T> other) => inner.IsSupersetOf(other);
    public bool IsProperSubsetOf(IEnumerable<T> other) => inner.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<T> other) => inner.IsProperSupersetOf(other);
    public bool Overlaps(IEnumerable<T> other) => inner.Overlaps(other);
    public bool SetEquals(IEnumerable<T> other) => inner.SetEquals(other);
    public void Add(T item) => throw new NotSupportedException();
    bool ISet<T>.Add(T item) => throw new NotSupportedException();
    public void UnionWith(IEnumerable<T> other) => throw new NotSupportedException();
    public void IntersectWith(IEnumerable<T> other) => throw new NotSupportedException();
    public void ExceptWith(IEnumerable<T> other) => throw new NotSupportedException();
    public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
}
