using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;
using Xunit;

namespace AgbSynth.Tests;

public sealed class Mp2kVoiceGroupParserTests
{
    [Fact]
    public async Task TryReadVoiceGroup_ReadsToneEntriesAndSampleHeader()
    {
        byte[] romBytes = new byte[0x1200];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteVoice(romBytes, 0x300, 10, type: 0x08, key: 60, dataPointer: 0x08000A00);
        WriteVoice(romBytes, 0x300, 11, type: 0x09, key: 60, dataPointer: 0x00000002);
        WriteSampleHeader(romBytes, 0xA00, loopFlags: 0x40, frequency: 0x00344300, loopStart: 12, size: 32);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, 0x08000300, 0x300, out var voiceGroup));
        var voice = voiceGroup.Voices[10];
        Assert.Equal(0x08, voice.Type);
        Assert.Equal("DirectSound Fixed", Mp2kVoiceGroupParser.GetVoiceTypeName(voice.Type));
        Assert.Equal(60, voice.Key);
        Assert.Equal(0xA00, voice.DataOffset);
        Assert.NotNull(voice.Sample);
        Assert.Equal(0x40, voice.Sample!.LoopFlags);
        Assert.Equal(0x00344300u, voice.Sample.Frequency);
        Assert.Equal(32u, voice.Sample.Size);

        var fixedSquare = voiceGroup.Voices[11];
        Assert.Equal("Square 1", Mp2kVoiceGroupParser.GetVoiceTypeName(fixedSquare.Type, fixedSquare.DataPointer));
        Assert.Null(fixedSquare.Sample);
        Assert.Equal("Noise", Mp2kVoiceGroupParser.GetVoiceTypeName(0x0C, 0x00000023));
    }

    [Fact]
    public async Task TryReadVoiceGroup_ReadsDrumSetAndKeySplitSubTables()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteVoice(romBytes, 0x300, 0, type: 0x80, key: 0, dataPointer: 0x08000A00);
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08001800, keyMapPointer: 0x08001A00);
        WriteVoice(romBytes, 0xA00, 60, type: 0x01, key: 60, dataPointer: 0x00000002);
        WriteVoice(romBytes, 0x1800, 0, type: 0x01, key: 48, dataPointer: 0x00000002);
        WriteVoice(romBytes, 0x1800, 1, type: 0x02, key: 72, dataPointer: 0x00000003);
        for (int i = 0; i < 128; i++)
            romBytes[0x1A00 + i] = i < 64 ? (byte)0 : (byte)1;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, 0x08000300, 0x300, out var voiceGroup));
        Assert.NotNull(voiceGroup.Voices[0].DrumSet);
        Assert.Equal(128, voiceGroup.Voices[0].DrumSet!.Entries.Count);
        Assert.Equal(0x01, voiceGroup.Voices[0].DrumSet!.Entries[60].Type);
        Assert.NotNull(voiceGroup.Voices[1].KeySplit);
        Assert.Equal(2, voiceGroup.Voices[1].KeySplit!.Regions.Count);
        Assert.Equal(128, voiceGroup.Voices[1].KeySplit!.KeyMap.Length);
    }

    [Fact]
    public async Task TryReadVoiceGroup_ReadsEmbeddedKeySplitMapAfterInlineRegionTable()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08001800, keyMapPointer: 0x08001B00);
        for (int i = 0; i < 4; i++)
        {
            WriteVoice(romBytes, 0x1800, i, type: 0x00, key: 60, dataPointer: 0x08002000);
            WriteVoice(romBytes, 0x1B00, i, type: 0x01, key: 60, dataPointer: 0x00000002);
        }

        for (int i = 0; i < 128; i++)
            romBytes[0x1B00 + 4 * Mp2kVoiceGroupParser.VoiceEntrySize + i] = i < 32 ? (byte)0 : i < 64 ? (byte)1 : i < 96 ? (byte)2 : (byte)3;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, 0x08000300, 0x300, out var voiceGroup));
        var keySplit = voiceGroup.Voices[1].KeySplit;
        Assert.NotNull(keySplit);
        Assert.Equal(4, keySplit!.Regions.Count);
        Assert.Equal(0x1B00, keySplit.KeyMapOffset);
        Assert.Equal(1, keySplit.KeyMap[0]);
        Assert.Equal(0x3C, keySplit.KeyMap[1]);
        Assert.Equal(0, keySplit.KeyMap[4 * Mp2kVoiceGroupParser.VoiceEntrySize]);
        Assert.Equal(1, keySplit.KeyMap[4 * Mp2kVoiceGroupParser.VoiceEntrySize + 32]);
    }

    [Fact]
    public async Task TryReadVoiceGroup_TruncatesKeySplitMapBeforeFollowingTable()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08001800, keyMapPointer: 0x08001C00);
        WriteVoice(romBytes, 0x1800, 0, type: 0x00, key: 60, dataPointer: 0x08002000);
        WriteVoice(romBytes, 0x1800, 1, type: 0x00, key: 60, dataPointer: 0x08002100);
        for (int i = 0; i < 108; i++)
            romBytes[0x1C00 + i] = i < 48 ? (byte)1 : (byte)0;
        for (int i = 108; i < 128; i++)
            romBytes[0x1C00 + i] = 0xFE;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        Assert.True(Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, 0x08000300, 0x300, out var voiceGroup));
        var keySplit = voiceGroup.Voices[1].KeySplit;
        Assert.NotNull(keySplit);
        Assert.Equal(2, keySplit!.Regions.Count);
        Assert.Equal(108, keySplit.KeyMap.Length);
        Assert.Equal(1, keySplit.KeyMap[0]);
        Assert.Equal(0, keySplit.KeyMap[107]);
    }

    private static void WriteVoice(byte[] bytes, int voiceGroupOffset, int index, byte type, byte key, uint dataPointer)
    {
        int offset = voiceGroupOffset + index * Mp2kVoiceGroupParser.VoiceEntrySize;
        bytes[offset + 0] = type;
        bytes[offset + 1] = key;
        bytes[offset + 2] = 0;
        bytes[offset + 3] = 0;
        WriteU32(bytes, offset + 4, dataPointer);
        bytes[offset + 8] = 1;
        bytes[offset + 9] = 2;
        bytes[offset + 10] = 3;
        bytes[offset + 11] = 4;
    }

    private static void WriteKeySplitVoice(byte[] bytes, int voiceGroupOffset, int index, uint regionPointer, uint keyMapPointer)
    {
        int offset = voiceGroupOffset + index * Mp2kVoiceGroupParser.VoiceEntrySize;
        bytes[offset + 0] = 0x40;
        WriteU32(bytes, offset + 4, regionPointer);
        WriteU32(bytes, offset + 8, keyMapPointer);
    }

    private static void WriteSampleHeader(byte[] bytes, int offset, byte loopFlags, uint frequency, uint loopStart, uint size)
    {
        bytes[offset + 3] = loopFlags;
        WriteU32(bytes, offset + 4, frequency);
        WriteU32(bytes, offset + 8, loopStart);
        WriteU32(bytes, offset + 12, size);
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
