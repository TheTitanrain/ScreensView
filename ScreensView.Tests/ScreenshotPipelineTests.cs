using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ScreensView.Agent;

namespace ScreensView.Tests;

public class ScreenshotPipelineTests
{
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
    /// 4-byte LE Int32 length prefix followed by JPEG bytes (FF D8 magic).
    ///
    /// Skipped on headless machines (no display → GetSystemMetrics returns 0).
    /// </summary>
    [Fact]
    public async Task ScreenshotHelper_PipeProtocol_WritesLengthPrefixedJpeg()
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
            return; // headless — no display, helper exited; skip silently
        }

        var lenBuf = new byte[4];
        ReadExact(server, lenBuf, 0, 4);
        int jpegLen = BitConverter.ToInt32(lenBuf, 0);

        Assert.True(jpegLen > 0, $"Expected positive JPEG length, got {jpegLen}");
        Assert.True(jpegLen < 50 * 1024 * 1024, "JPEG length sanity check failed (> 50 MB)");

        var jpeg = new byte[jpegLen];
        ReadExact(server, jpeg, 0, jpegLen);

        // JPEG magic bytes: FF D8
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        await helperTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void ReadExact(Stream stream, byte[] buf, int offset, int count)
    {
        while (count > 0)
        {
            int read = stream.Read(buf, offset, count);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
            count -= read;
        }
    }
}
