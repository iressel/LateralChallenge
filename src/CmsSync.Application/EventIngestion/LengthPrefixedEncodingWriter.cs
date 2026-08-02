using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace CmsSync.Application.EventIngestion;

internal sealed class LengthPrefixedEncodingWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly ArrayBufferWriter<byte> _buffer = new();

    public void WriteByte(byte value)
    {
        var destination = _buffer.GetSpan(1);
        destination[0] = value;
        _buffer.Advance(1);
    }

    public void WriteInt32(int value)
    {
        var destination = _buffer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(destination, value);
        _buffer.Advance(sizeof(int));
    }

    public void WriteInt64(long value)
    {
        var destination = _buffer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(destination, value);
        _buffer.Advance(sizeof(long));
    }

    public void WriteLengthPrefixed(string value)
    {
        var byteCount = StrictUtf8.GetByteCount(value);
        WriteInt32(byteCount);

        var destination = _buffer.GetSpan(byteCount);
        var bytesWritten = StrictUtf8.GetBytes(value, destination);
        _buffer.Advance(bytesWritten);
    }

    public void WriteLengthPrefixed(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        value.CopyTo(_buffer.GetSpan(value.Length));
        _buffer.Advance(value.Length);
    }

    public byte[] ToArray()
    {
        return _buffer.WrittenSpan.ToArray();
    }
}
