using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;
using Xunit;

namespace AgbSynth.Tests;

public sealed class Mp2kSongHeaderParserTests
{
    [Fact]
    public async Task TryReadHeader_ReadsVoiceGroupAndTrackPointers()
    {
        byte[] romBytes = new byte[0x500];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 2, voiceGroupPointer: 0x08000300, 0x08000400, 0x08000420);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kSongTableParser.TryReadEntry(rom, 0x100, 0, out var entry));
        Assert.True(Mp2kSongHeaderParser.TryReadHeader(rom, entry, out var header));
        Assert.Equal(2, header.TrackCount);
        Assert.Equal(10, header.Priority);
        Assert.Equal(20, header.Reverb);
        Assert.Equal(0x08000300u, header.VoiceGroupPointer);
        Assert.Equal(0x300, header.VoiceGroupOffset);
        Assert.Equal([0x08000400u, 0x08000420u], header.TrackPointers);
        Assert.Equal([0x400, 0x420], header.TrackOffsets);
        Assert.Equal(Mp2kSongHeaderParser.FixedHeaderSize + 8, header.RawHeader.Length);
    }

    [Fact]
    public async Task TryReadHeader_RejectsInvalidTrackPointer()
    {
        byte[] romBytes = new byte[0x500];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, 0x02000000);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kSongTableParser.TryReadEntry(rom, 0x100, 0, out var entry));
        Assert.False(Mp2kSongHeaderParser.TryReadHeader(rom, entry, out _));
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

    private static void WriteSongHeader(byte[] bytes, int offset, byte trackCount, uint voiceGroupPointer, params uint[] trackPointers)
    {
        bytes[offset + 0] = trackCount;
        bytes[offset + 1] = 0;
        bytes[offset + 2] = 10;
        bytes[offset + 3] = 20;
        WriteU32(bytes, offset + 4, voiceGroupPointer);
        for (int i = 0; i < trackPointers.Length; i++)
            WriteU32(bytes, offset + 8 + i * 4, trackPointers[i]);
    }
}
