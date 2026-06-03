using AgbSynth.App.GBA;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class AgbSynthProjectExporterTests
{
    [Fact]
    public async Task CreateFromRom_StoresRomAndSongTableMetadata()
    {
        byte[] romBytes = new byte[0x500];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        Assert.Equal("AgbSynthProject", project.Format);
        Assert.Equal("MP2K", project.Engine);
        Assert.Equal("TEST", project.Rom.GameCode);
        Assert.Equal(0x100, project.SongTable.Offset);
        Assert.Contains(project.Songs, s => s.SongId == 0 && s.HeaderPointer == "0x08000200");
        Assert.Contains(project.SongHeaders, h =>
            h.SongId == 0 &&
            h.TrackCount == 1 &&
            h.VoiceGroupPointer == "0x08000300" &&
            h.TrackPointers.Contains("0x08000400"));
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

    private static void WriteSongHeader(byte[] bytes, int offset, byte trackCount, uint voiceGroupPointer, uint trackPointer)
    {
        bytes[offset + 0] = trackCount;
        bytes[offset + 1] = 0;
        bytes[offset + 2] = 10;
        bytes[offset + 3] = 20;
        WriteU32(bytes, offset + 4, voiceGroupPointer);
        WriteU32(bytes, offset + 8, trackPointer);
    }
}
