using System;
using System.Collections.Generic;
using AgbSynth.App.GBA;

namespace AgbSynth.App.MP2K;

public static class Mp2kVoiceGroupParser
{
    public const int VoiceCount = 128;
    public const int VoiceEntrySize = 12;
    public const int SampleHeaderSize = 16;
    private const int KeySplitMapSize = 128;
    private const int MinTruncatedKeySplitMapSize = 64;
    private const int MaxKeySplitRegionIndex = 15;

    public static bool TryReadVoiceGroup(GbaRom rom, uint pointer, int offset, out Mp2kVoiceGroup voiceGroup)
    {
        voiceGroup = null!;
        if (offset < 0 || offset + VoiceCount * VoiceEntrySize > rom.Length)
            return false;

        var voices = new List<Mp2kVoice>(VoiceCount);
        for (int i = 0; i < VoiceCount; i++)
        {
            int entryOffset = offset + i * VoiceEntrySize;
            ReadOnlySpan<byte> raw = rom.Slice(entryOffset, VoiceEntrySize);
            byte type = raw[0];
            byte key = raw[1];
            byte length = raw[2];
            byte panOrSweep = raw[3];
            uint dataPointer = BitConverter.ToUInt32(raw[4..8]);
            byte attack = raw[8];
            byte decay = raw[9];
            byte sustain = raw[10];
            byte release = raw[11];
            uint? extraPointer = type is 0x40
                ? BitConverter.ToUInt32(raw[8..12])
                : null;
            int? extraOffset = extraPointer is uint rawExtraPointer && GbaAddress.TryToOffset(rawExtraPointer, rom.Length, out int resolvedExtraOffset)
                ? resolvedExtraOffset
                : null;

            int? dataOffset = GbaAddress.TryToOffset(dataPointer, rom.Length, out int resolvedOffset)
                ? resolvedOffset
                : null;
            Mp2kSampleHeader? sample = dataOffset is int sampleOffset && IsDirectSoundVoice(type, dataPointer)
                ? TryReadSampleHeader(rom, sampleOffset)
                : null;
            Mp2kDrumSet? drumSet = type == 0x80 && dataOffset is int drumSetOffset
                ? TryReadDrumSet(rom, drumSetOffset)
                : null;
            Mp2kKeySplit? keySplit = type == 0x40 && dataOffset is int keySplitOffset
                ? TryReadKeySplit(rom, keySplitOffset, extraOffset)
                : null;

            voices.Add(new Mp2kVoice(
                i,
                type,
                key,
                length,
                panOrSweep,
                dataPointer,
                dataOffset,
                attack,
                decay,
                sustain,
                release,
                extraPointer,
                extraOffset,
                sample,
                drumSet,
                keySplit,
                raw.ToArray()));
        }

        voiceGroup = new Mp2kVoiceGroup(pointer, offset, voices);
        return true;
    }

    public static string GetVoiceTypeName(byte type, uint dataPointer = 0)
    {
        int source = type & 0x07;
        bool fixedPitch = (type & 0x08) != 0;
        return type switch
        {
            0x00 when dataPointer == 0 => "Empty",
            0x40 => "KeySplit",
            0x80 => "DrumSet",
            _ when source == 0 => fixedPitch ? "DirectSound Fixed" : "DirectSound",
            _ when source == 1 => "Square 1",
            _ when source == 2 => "Square 2",
            _ when source == 3 => "PSG Wave",
            _ when source == 4 => "Noise",
            _ => $"Unknown 0x{type:X2}"
        };
    }

    private static bool IsDirectSoundVoice(byte type, uint dataPointer)
    {
        if (type is 0x40 or 0x80)
            return false;
        return (type & 0x07) == 0 && dataPointer != 0;
    }

    private static Mp2kDrumSet? TryReadDrumSet(GbaRom rom, int offset)
    {
        int size = VoiceCount * VoiceEntrySize;
        if (offset < 0 || offset + size > rom.Length)
            return null;

        var entries = new List<Mp2kVoice>(VoiceCount);
        for (int i = 0; i < VoiceCount; i++)
            entries.Add(ReadFlatVoice(rom, offset + i * VoiceEntrySize, i));

        return new Mp2kDrumSet(offset, entries, rom.Slice(offset, size).ToArray());
    }

    private static Mp2kKeySplit? TryReadKeySplit(GbaRom rom, int regionTableOffset, int? keyMapOffset)
    {
        if (regionTableOffset < 0 || regionTableOffset + VoiceEntrySize > rom.Length)
            return null;

        byte[] keyMap = [];
        int regionCount = 16;
        int? resolvedKeyMapOffset = null;
        if (keyMapOffset is int mapOffset && mapOffset >= 0)
        {
            if (TryReadKeySplitMap(rom, mapOffset, out keyMap, out int maxRegionIndex))
            {
                resolvedKeyMapOffset = mapOffset;
                regionCount = Math.Clamp(maxRegionIndex + 1, 1, 64);
            }
            else
            {
                // MP2K uses the +8 pointer as the key table address. Some titles place dummy
                // voice-looking bytes at the low-key side of the table, so keep the raw table
                // at the original address and only use later valid runs to infer region count.
                if (TryReadRawKeySplitMap(rom, mapOffset, out keyMap))
                    resolvedKeyMapOffset = mapOffset;

                for (int inlineRegionCount = 1; inlineRegionCount <= 16; inlineRegionCount++)
                {
                    int candidateOffset = mapOffset + inlineRegionCount * VoiceEntrySize;
                    if (!TryReadKeySplitMap(rom, candidateOffset, out byte[] candidateMap, out maxRegionIndex))
                        continue;
                    if (maxRegionIndex + 1 > inlineRegionCount)
                        continue;

                    regionCount = inlineRegionCount;
                    break;
                }
            }
        }

        regionCount = Math.Min(regionCount, (rom.Length - regionTableOffset) / VoiceEntrySize);
        var regions = new List<Mp2kVoice>(regionCount);
        for (int i = 0; i < regionCount; i++)
            regions.Add(ReadFlatVoice(rom, regionTableOffset + i * VoiceEntrySize, i));

        return new Mp2kKeySplit(
            regionTableOffset,
            resolvedKeyMapOffset ?? keyMapOffset,
            regions,
            keyMap,
            rom.Slice(regionTableOffset, regionCount * VoiceEntrySize).ToArray());
    }

    private static bool TryReadRawKeySplitMap(GbaRom rom, int offset, out byte[] keyMap)
    {
        keyMap = [];
        if (offset < 0 || offset >= rom.Length)
            return false;

        int available = Math.Min(KeySplitMapSize, rom.Length - offset);
        if (available <= 0)
            return false;

        keyMap = rom.Slice(offset, available).ToArray();
        return true;
    }

    private static bool TryReadKeySplitMap(GbaRom rom, int offset, out byte[] keyMap, out int maxRegionIndex)
    {
        keyMap = [];
        maxRegionIndex = 0;
        if (offset < 0 || offset >= rom.Length)
            return false;

        int available = Math.Min(KeySplitMapSize, rom.Length - offset);
        ReadOnlySpan<byte> candidate = rom.Slice(offset, available);
        int length = 0;
        for (; length < available; length++)
        {
            byte value = candidate[length];
            if (value > MaxKeySplitRegionIndex)
                break;
            if (value > maxRegionIndex)
                maxRegionIndex = value;
        }

        if (length == KeySplitMapSize)
        {
            keyMap = rom.Slice(offset, KeySplitMapSize).ToArray();
            return true;
        }

        if (length < MinTruncatedKeySplitMapSize)
            return false;

        keyMap = rom.Slice(offset, length).ToArray();
        return true;
    }

    private static Mp2kVoice ReadFlatVoice(GbaRom rom, int entryOffset, int index)
    {
        ReadOnlySpan<byte> raw = rom.Slice(entryOffset, VoiceEntrySize);
        byte type = raw[0];
        byte key = raw[1];
        byte length = raw[2];
        byte panOrSweep = raw[3];
        uint dataPointer = BitConverter.ToUInt32(raw[4..8]);
        byte attack = raw[8];
        byte decay = raw[9];
        byte sustain = raw[10];
        byte release = raw[11];
        int? dataOffset = GbaAddress.TryToOffset(dataPointer, rom.Length, out int resolvedOffset)
            ? resolvedOffset
            : null;
        Mp2kSampleHeader? sample = dataOffset is int sampleOffset && IsDirectSoundVoice(type, dataPointer)
            ? TryReadSampleHeader(rom, sampleOffset)
            : null;

        return new Mp2kVoice(
            index,
            type,
            key,
            length,
            panOrSweep,
            dataPointer,
            dataOffset,
            attack,
            decay,
            sustain,
            release,
            null,
            null,
            sample,
            null,
            null,
            raw.ToArray());
    }

    private static Mp2kSampleHeader? TryReadSampleHeader(GbaRom rom, int offset)
    {
        if (offset < 0 || offset + SampleHeaderSize > rom.Length)
            return null;

        ReadOnlySpan<byte> header = rom.Slice(offset, SampleHeaderSize);
        byte loopFlags = header[3];
        uint frequency = BitConverter.ToUInt32(header[4..8]);
        uint loopStart = BitConverter.ToUInt32(header[8..12]);
        uint size = BitConverter.ToUInt32(header[12..16]);
        int dataOffset = offset + SampleHeaderSize;

        if (size == 0 || size > rom.Length || dataOffset >= rom.Length)
            return null;
        if (dataOffset + (long)size > rom.Length)
            return null;

        return new Mp2kSampleHeader(offset, loopFlags, frequency, loopStart, size, dataOffset);
    }
}
