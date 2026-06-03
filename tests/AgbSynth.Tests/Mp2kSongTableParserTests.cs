using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;
using Xunit;

namespace AgbSynth.Tests;

public sealed class Mp2kSongTableParserTests
{
    [Fact]
    public async Task TryReadEntry_ReadsHeaderPointer()
    {
        byte[] romBytes = new byte[0x400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kSongTableParser.TryReadEntry(rom, 0x100, 0, out var entry));
        Assert.Equal(0, entry.SongId);
        Assert.Equal(0x100, entry.TableOffset);
        Assert.Equal(0x08000200u, entry.HeaderPointer);
        Assert.Equal(0x200, entry.HeaderOffset);
        Assert.Equal(Mp2kSongTableParser.DefaultEntrySize, entry.RawEntry.Length);
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
    }

    private static void WriteAscii(byte[] bytes, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            bytes[offset + i] = (byte)text[i];
    }
}
