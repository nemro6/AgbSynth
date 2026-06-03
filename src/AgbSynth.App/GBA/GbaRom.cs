using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgbSynth.App.GBA;

public sealed class GbaRom
{
    private const int HeaderGameTitleOffset = 0xA0;
    private const int HeaderGameTitleLength = 12;
    private const int HeaderGameCodeOffset = 0xAC;
    private const int HeaderGameCodeLength = 4;
    private readonly byte[] _bytes;

    private GbaRom(byte[] bytes, string sourcePath)
    {
        _bytes = bytes;
        SourcePath = sourcePath;
        GameTitle = ReadAscii(bytes, HeaderGameTitleOffset, HeaderGameTitleLength).Trim();
        GameCode = ReadAscii(bytes, HeaderGameCodeOffset, HeaderGameCodeLength).Trim();
    }

    public string SourcePath { get; }
    public string GameTitle { get; }
    public string GameCode { get; }
    public int Length => _bytes.Length;
    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static async Task<GbaRom> LoadAsync(Stream stream, string sourcePath, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        byte[] bytes = ms.ToArray();

        if (bytes.Length < 0xC0)
            throw new InvalidDataException("File is too small to be a GBA ROM.");
        if (bytes.Length > GbaAddress.MaxRomSize)
            throw new InvalidDataException("File exceeds the 32 MiB GBA ROM address range.");

        return new GbaRom(bytes, sourcePath);
    }

    public bool TryReadUInt32LittleEndian(int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > _bytes.Length)
            return false;

        value = BitConverter.ToUInt32(_bytes, offset);
        return true;
    }

    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return _bytes.AsSpan(offset, length);
    }

    private static string ReadAscii(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || offset + length > bytes.Length)
            return string.Empty;

        Span<char> chars = stackalloc char[length];
        int count = 0;
        for (int i = 0; i < length; i++)
        {
            byte b = bytes[offset + i];
            if (b == 0)
                break;

            chars[count++] = b is >= 0x20 and <= 0x7E ? (char)b : ' ';
        }

        return new string(chars[..count]);
    }
}

