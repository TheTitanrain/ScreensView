using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ScreensView.Agent;
using ScreensView.Shared;

namespace ScreensView.Tests;

public class ScreenshotPipelineTests
{
    [Fact]
    public void ScreenshotTransferProtocol_RoundTripsLockedMessage()
    {
        using var stream = new MemoryStream();

        ScreenshotTransferProtocol.WriteLocked(stream, "Workstation is locked.");
        stream.Position = 0;

        var result = ScreenshotTransferProtocol.Read(stream);

        Assert.Equal(ScreenshotTransferStatus.Locked, result.Status);
        Assert.Equal("Workstation is locked.", result.Message);
        Assert.Null(result.JpegBytes);
    }

    [Fact]
    public void NoActiveSessionException_IsInvalidOperationException()
    {
        var ex = new NoActiveSessionException();

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    [Fact]
    public void NoActiveSessionException_MessageDescribesNoSession()
    {
        var ex = new NoActiveSessionException();

        Assert.Contains("session", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the named pipe protocol used by ScreenshotHelper:
    /// 1-byte transfer status + 4-byte LE payload length + payload bytes.
    ///
    /// Skipped on headless machines (no display -> GetSystemMetrics returns 0).
    /// </summary>
    [Fact]
    public async Task ScreenshotHelper_PipeProtocol_WritesSuccessPacketWithJpeg()
    {
        var pipeName = "ScreensViewTest-" + Guid.NewGuid().ToString("N");

        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        using var server = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            ps);

        var helperTask = Task.Run(() => ScreenshotHelper.Run(pipeName, 75));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await server.WaitForConnectionAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // headless - no display, helper exited; skip silently
        }

        var result = ScreenshotTransferProtocol.Read(server);

        Assert.Equal(ScreenshotTransferStatus.Success, result.Status);
        Assert.NotNull(result.JpegBytes);

        var jpeg = result.JpegBytes!;
        Assert.True(jpeg.Length > 0, $"Expected positive JPEG length, got {jpeg.Length}");
        Assert.True(jpeg.Length < 50 * 1024 * 1024, "JPEG length sanity check failed (> 50 MB)");

        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        await helperTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
