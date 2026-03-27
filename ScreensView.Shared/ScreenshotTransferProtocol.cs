using System.IO;
using System.Text;

namespace ScreensView.Shared;

public enum ScreenshotTransferStatus : byte
{
    Success = 1,
    Locked = 2
}

public sealed class ScreenshotTransferResult
{
    public ScreenshotTransferResult(ScreenshotTransferStatus status, byte[]? jpegBytes, string message)
    {
        Status = status;
        JpegBytes = jpegBytes;
        Message = message;
    }

    public ScreenshotTransferStatus Status { get; }

    public byte[]? JpegBytes { get; }

    public string Message { get; }
}

public static class ScreenshotTransferProtocol
{
    private const int MaxPayloadLength = 50 * 1024 * 1024;

    public static void WriteSuccess(Stream stream, byte[] jpegBytes)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (jpegBytes is null) throw new ArgumentNullException(nameof(jpegBytes));

        WriteRecord(stream, ScreenshotTransferStatus.Success, jpegBytes);
    }

    public static void WriteLocked(Stream stream, string message)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        var payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
        WriteRecord(stream, ScreenshotTransferStatus.Locked, payload);
    }

    public static ScreenshotTransferResult Read(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        var statusByte = stream.ReadByte();
        if (statusByte < 0)
            throw new EndOfStreamException("Helper closed pipe before sending a transfer status.");

        var status = (ScreenshotTransferStatus)(byte)statusByte;
        if (!Enum.IsDefined(typeof(ScreenshotTransferStatus), status))
            throw new InvalidDataException($"Unknown transfer status: {statusByte}");

        var lenBuf = new byte[4];
        ReadExact(stream, lenBuf, 0, lenBuf.Length);
        var payloadLength = BitConverter.ToInt32(lenBuf, 0);

        if (payloadLength < 0 || payloadLength > MaxPayloadLength)
            throw new InvalidDataException($"Invalid helper payload length: {payloadLength}");

        var payload = new byte[payloadLength];
        ReadExact(stream, payload, 0, payload.Length);

        if (status == ScreenshotTransferStatus.Success)
            return new ScreenshotTransferResult(status, payload, string.Empty);

        return new ScreenshotTransferResult(status, null, Encoding.UTF8.GetString(payload));
    }

    private static void WriteRecord(Stream stream, ScreenshotTransferStatus status, byte[] payload)
    {
        stream.WriteByte((byte)status);

        var len = BitConverter.GetBytes(payload.Length);
        stream.Write(len, 0, len.Length);
        stream.Write(payload, 0, payload.Length);
    }

    private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read == 0)
                throw new EndOfStreamException("Helper closed pipe before sending all payload bytes.");

            offset += read;
            count -= read;
        }
    }
}
