using System;
using System.Collections.Generic;
using System.Linq;
using AgbSynth.App.GBA;

namespace AgbSynth.App.MP2K;

public static class Mp2kRomDiscovery
{
    private const int MinimumSongTableRun = 4;
    private const int MaximumSongTableEntries = 2048;
    private const int MaximumSongTableGap = 128;
    private const int MaximumMusicPlayers = 32;
    private const int VoiceGroupSize = Mp2kVoiceGroupParser.VoiceCount * Mp2kVoiceGroupParser.VoiceEntrySize;
    private const int VoiceGroupSearchPadding = 0x4000;
    private const int MaximumUnreferencedVoiceGroups = 512;
    private static readonly int[] FixedSampleRates =
    [
        0,
        5734,
        7884,
        10512,
        13379,
        15768,
        18157,
        21024,
        26758,
        31536,
        36314,
        40137,
        42048
    ];

    public static bool TryFindSongTable(GbaRom rom, out int tableOffset, out int validEntryCount)
    {
        tableOffset = 0;
        validEntryCount = 0;
        if (TryFindKnownSongTable(rom, out int knownOffset))
        {
            int knownCount = CountValidSongTableEntries(rom, knownOffset);
            if (knownCount >= MinimumSongTableRun)
            {
                tableOffset = knownOffset;
                validEntryCount = knownCount;
                return true;
            }
        }

        var referencedSongTables = FindReferencedSongTableOffsets(rom);
        int referencedTableOffset = 0;
        int referencedEntryCount = 0;

        for (int offset = 0; offset + Mp2kSongTableParser.DefaultEntrySize <= rom.Length; offset += 4)
        {
            int count = CountValidSongTableEntries(rom, offset);
            if (count < MinimumSongTableRun)
                continue;

            if (referencedSongTables.Contains(offset) && count > referencedEntryCount)
            {
                referencedTableOffset = offset;
                referencedEntryCount = count;
            }

            if (count <= validEntryCount)
                continue;

            tableOffset = offset;
            validEntryCount = count;
        }

        if (referencedEntryCount >= MinimumSongTableRun)
        {
            tableOffset = referencedTableOffset;
            validEntryCount = referencedEntryCount;
            return true;
        }

        return validEntryCount >= MinimumSongTableRun;
    }

    private static bool TryFindKnownSongTable(GbaRom rom, out int tableOffset)
    {
        tableOffset = rom.GameCode switch
        {
            "BPEE" => 0x6B49F0,
            "BPEJ" => 0x63C2AC,
            _ => -1
        };

        return tableOffset >= 0 && tableOffset + Mp2kSongTableParser.DefaultEntrySize <= rom.Length;
    }

    private static HashSet<int> FindReferencedSongTableOffsets(GbaRom rom)
    {
        var offsets = new HashSet<int>();
        for (int referenceOffset = 4; referenceOffset + 4 <= rom.Length; referenceOffset += 4)
        {
            if (!rom.TryReadUInt32LittleEndian(referenceOffset, out uint songTablePointer) ||
                !GbaAddress.TryToOffset(songTablePointer, rom.Length, out int songTableOffset) ||
                !rom.TryReadUInt32LittleEndian(referenceOffset - 4, out uint playerTablePointer) ||
                !GbaAddress.TryToOffset(playerTablePointer, rom.Length, out int playerTableOffset) ||
                !IsPlausiblePlayerTable(rom, playerTableOffset))
            {
                continue;
            }

            offsets.Add(songTableOffset);
        }

        return offsets;
    }

    public static IReadOnlyList<Mp2kVoiceGroup> FindUnreferencedVoiceGroups(
        GbaRom rom,
        IEnumerable<int> excludedOffsets)
    {
        var knownOffsets = excludedOffsets.ToHashSet();
        var groups = new List<Mp2kVoiceGroup>();
        if (knownOffsets.Count == 0)
            return groups;

        int minKnownOffset = knownOffsets.Min();
        int maxKnownOffset = knownOffsets.Max();
        int startOffset = Math.Max(0, minKnownOffset - VoiceGroupSearchPadding);
        int endOffset = Math.Min(rom.Length - VoiceGroupSize, maxKnownOffset + VoiceGroupSearchPadding);
        startOffset &= ~3;
        endOffset &= ~3;

        for (int offset = startOffset; offset <= endOffset; offset += 4)
        {
            if (knownOffsets.Contains(offset) || !IsPlausibleVoiceGroup(rom, offset))
                continue;

            uint groupPointer = GbaAddress.ToPointer(offset);
            if (!Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, groupPointer, offset, out var voiceGroup))
                continue;

            groups.Add(voiceGroup);
            knownOffsets.Add(offset);

            if (groups.Count >= MaximumUnreferencedVoiceGroups)
                break;
        }

        return groups;
    }

    public static bool TryFindSoundMode(GbaRom rom, int songTableOffset, out Mp2kSoundMode soundMode)
    {
        soundMode = Mp2kSoundMode.Default;
        if (!TryFindPlayerTable(rom, songTableOffset, out int playerTableOffset))
            return false;

        uint playerTablePointer = GbaAddress.ToPointer(playerTableOffset);
        for (int referenceOffset = 0; referenceOffset + 4 <= rom.Length; referenceOffset += 4)
        {
            if (!rom.TryReadUInt32LittleEndian(referenceOffset, out uint pointer) || pointer != playerTablePointer)
                continue;
            if (referenceOffset < 0x1C)
                continue;

            int signatureOffset = referenceOffset - 0x1C;
            if (!TryReadNormalSoundMode(rom, signatureOffset, out soundMode))
                continue;

            return true;
        }

        return false;
    }

    private static int CountValidSongTableEntries(GbaRom rom, int tableOffset)
    {
        int lastValidCount = 0;
        int distinctHeaders = 0;
        int lastHeaderOffset = -1;
        int gap = 0;

        for (int songId = 0; songId < MaximumSongTableEntries; songId++)
        {
            int entryOffset = tableOffset + songId * Mp2kSongTableParser.DefaultEntrySize;
            if (!rom.TryReadUInt32LittleEndian(entryOffset, out uint headerPointer) ||
                !GbaAddress.TryToOffset(headerPointer, rom.Length, out int headerOffset) ||
                !IsPlausibleSongHeader(rom, headerOffset))
            {
                if (lastValidCount == 0)
                    break;

                gap++;
                if (gap > MaximumSongTableGap)
                    break;

                continue;
            }

            gap = 0;
            lastValidCount = songId + 1;
            if (headerOffset != lastHeaderOffset)
            {
                distinctHeaders++;
                lastHeaderOffset = headerOffset;
            }
        }

        return distinctHeaders == 0 ? 0 : lastValidCount;
    }

    private static bool TryFindPlayerTable(GbaRom rom, int songTableOffset, out int playerTableOffset)
    {
        playerTableOffset = 0;
        uint songTablePointer = GbaAddress.ToPointer(songTableOffset);
        for (int referenceOffset = 4; referenceOffset + 4 <= rom.Length; referenceOffset += 4)
        {
            if (!rom.TryReadUInt32LittleEndian(referenceOffset, out uint pointer) || pointer != songTablePointer)
                continue;
            if (!rom.TryReadUInt32LittleEndian(referenceOffset - 4, out uint candidatePointer) ||
                !GbaAddress.TryToOffset(candidatePointer, rom.Length, out int candidateOffset) ||
                !IsPlausiblePlayerTable(rom, candidateOffset))
            {
                continue;
            }

            playerTableOffset = candidateOffset;
            return true;
        }

        return false;
    }

    private static bool TryReadNormalSoundMode(GbaRom rom, int signatureOffset, out Mp2kSoundMode soundMode)
    {
        soundMode = Mp2kSoundMode.Default;
        if (signatureOffset < 0 || signatureOffset + 0x24 > rom.Length)
            return false;
        if (!rom.TryReadUInt32LittleEndian(signatureOffset + 0x00, out uint mixCodePointer) ||
            !GbaAddress.TryToOffset(mixCodePointer, rom.Length, out _) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x04, out uint mixCodeRamPointer) ||
            !IsRamPointer(mixCodeRamPointer) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x08, out uint cpuSetArgument) ||
            (cpuSetArgument & (1u << 26)) == 0 ||
            (cpuSetArgument & 0x1FFFFFu) >= 0x800 ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x0C, out uint soundInfoPointer) ||
            !IsRamPointer(soundInfoPointer) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x10, out uint cgbChannelPointer) ||
            !IsRamPointer(cgbChannelPointer) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x18, out uint playerTableLength) ||
            playerTableLength > MaximumMusicPlayers ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x1C, out uint playerTablePointer) ||
            !GbaAddress.TryToOffset(playerTablePointer, rom.Length, out _) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x20, out uint memoryAccessPointer) ||
            !IsRamPointer(memoryAccessPointer) ||
            !rom.TryReadUInt32LittleEndian(signatureOffset + 0x14, out uint rawValue))
        {
            return false;
        }

        int reverb = (int)(rawValue & 0xFF);
        int maxChannels = (int)((rawValue >> 8) & 0xF);
        int volume = (int)((rawValue >> 12) & 0xF);
        int frequencyIndex = (int)((rawValue >> 16) & 0xF);
        int dacConfig = (int)((rawValue >> 20) & 0xF);
        if (reverb != 0 || maxChannels is < 1 or > 12 || frequencyIndex is < 1 or > 12 || dacConfig is < 8 or > 11)
            return false;

        soundMode = new Mp2kSoundMode(
            signatureOffset + 0x14,
            rawValue,
            reverb,
            maxChannels,
            volume,
            frequencyIndex,
            dacConfig,
            FixedSampleRates[frequencyIndex]);
        return true;
    }

    private static bool IsPlausiblePlayerTable(GbaRom rom, int offset)
    {
        int validEntries = 0;
        for (int index = 0; index < MaximumMusicPlayers; index++)
        {
            int entryOffset = offset + index * 12;
            if (entryOffset < 0 || entryOffset + 12 > rom.Length ||
                !rom.TryReadUInt32LittleEndian(entryOffset, out uint playerPointer) ||
                !rom.TryReadUInt32LittleEndian(entryOffset + 4, out uint trackPointer))
            {
                break;
            }

            ReadOnlySpan<byte> entry = rom.Slice(entryOffset, 12);
            int trackLimit = BitConverter.ToUInt16(entry[8..10]);
            int unknown = BitConverter.ToUInt16(entry[10..12]);
            if (playerPointer == 0 && trackPointer == 0 && trackLimit == 0 && unknown == 0)
            {
                validEntries++;
                continue;
            }

            if (!IsRamPointer(playerPointer) || !IsRamPointer(trackPointer) ||
                trackLimit > Mp2kSongHeaderParser.MaxTrackCount || unknown > 1)
            {
                break;
            }

            validEntries++;
        }

        return validEntries > 0;
    }

    private static bool IsRamPointer(uint pointer)
    {
        return pointer is >= 0x02000000 and <= 0x0203FFFF or >= 0x03000000 and <= 0x03007FFF;
    }

    private static bool IsPlausibleSongHeader(GbaRom rom, int offset)
    {
        if (offset < 0 || offset + Mp2kSongHeaderParser.FixedHeaderSize > rom.Length)
            return false;

        ReadOnlySpan<byte> header = rom.Slice(offset, Mp2kSongHeaderParser.FixedHeaderSize);
        int trackCount = header[0];
        if (trackCount > Mp2kSongHeaderParser.MaxTrackCount)
            return false;
        if (!GbaAddress.TryToOffset(BitConverter.ToUInt32(header[4..8]), rom.Length, out _))
            return false;
        if (offset + Mp2kSongHeaderParser.FixedHeaderSize + trackCount * 4 > rom.Length)
            return false;

        for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            int pointerOffset = offset + Mp2kSongHeaderParser.FixedHeaderSize + trackIndex * 4;
            if (!rom.TryReadUInt32LittleEndian(pointerOffset, out uint trackPointer) ||
                !GbaAddress.TryToOffset(trackPointer, rom.Length, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlausibleVoiceGroup(GbaRom rom, int offset)
    {
        int meaningfulVoices = 0;
        int validVoices = 0;
        int strongPointers = 0;

        for (int index = 0; index < Mp2kVoiceGroupParser.VoiceCount; index++)
        {
            ReadOnlySpan<byte> raw = rom.Slice(offset + index * Mp2kVoiceGroupParser.VoiceEntrySize, Mp2kVoiceGroupParser.VoiceEntrySize);
            byte type = raw[0];
            uint dataPointer = BitConverter.ToUInt32(raw[4..8]);

            if (!IsKnownVoiceType(type))
                return false;

            if (IsUnusedVoice(raw))
            {
                validVoices++;
                continue;
            }

            if (type == 0x00 && dataPointer == 0)
            {
                validVoices++;
                continue;
            }

            meaningfulVoices++;
            if (type is 0x40 or 0x80 || (type & 0x07) == 0)
            {
                if (!GbaAddress.TryToOffset(dataPointer, rom.Length, out _))
                    return false;
                strongPointers++;
            }

            validVoices++;
        }

        return validVoices == Mp2kVoiceGroupParser.VoiceCount &&
               meaningfulVoices > 0 &&
               strongPointers > 0;
    }

    private static bool IsKnownVoiceType(byte type)
    {
        if (type is 0x40 or 0x80)
            return true;

        int source = type & 0x07;
        int flags = type & ~0x0F;
        return source <= 4 && flags == 0;
    }

    private static bool IsUnusedVoice(ReadOnlySpan<byte> raw)
    {
        return raw.SequenceEqual(new byte[]
        {
            0x01, 0x3C, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00
        });
    }
}

public sealed record Mp2kSoundMode(
    int Offset,
    uint RawValue,
    int Reverb,
    int MaxChannels,
    int Volume,
    int FrequencyIndex,
    int DacConfig,
    int FixedSampleRate)
{
    public static Mp2kSoundMode Default { get; } = new(
        Offset: -1,
        RawValue: 0,
        Reverb: 0,
        MaxChannels: 12,
        Volume: 15,
        FrequencyIndex: 4,
        DacConfig: 9,
        FixedSampleRate: 13379);
}
