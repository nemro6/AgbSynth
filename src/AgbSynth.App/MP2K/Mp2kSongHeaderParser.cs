using AgbSynth.App.GBA;

namespace AgbSynth.App.MP2K;

public static class Mp2kSongHeaderParser
{
    public const int FixedHeaderSize = 8;
    public const int MaxTrackCount = 16;

    public static bool TryReadHeader(
        GbaRom rom,
        Mp2kSongTableEntry tableEntry,
        out Mp2kSongHeader header)
    {
        header = null!;
        int offset = tableEntry.HeaderOffset;
        if (offset < 0 || offset + FixedHeaderSize > rom.Length)
            return false;

        var fixedHeader = rom.Slice(offset, FixedHeaderSize);
        byte trackCount = fixedHeader[0];
        if (trackCount > MaxTrackCount)
            return false;

        byte blockCount = fixedHeader[1];
        byte priority = fixedHeader[2];
        byte reverb = fixedHeader[3];

        if (!rom.TryReadUInt32LittleEndian(offset + 4, out uint voiceGroupPointer))
            return false;
        if (!GbaAddress.TryToOffset(voiceGroupPointer, rom.Length, out int voiceGroupOffset))
            return false;

        int headerSize = FixedHeaderSize + trackCount * 4;
        if (offset + headerSize > rom.Length)
            return false;

        uint[] trackPointers = new uint[trackCount];
        int[] trackOffsets = new int[trackCount];
        for (int i = 0; i < trackCount; i++)
        {
            int pointerOffset = offset + FixedHeaderSize + i * 4;
            if (!rom.TryReadUInt32LittleEndian(pointerOffset, out uint trackPointer))
                return false;
            if (!GbaAddress.TryToOffset(trackPointer, rom.Length, out int trackOffset))
                return false;

            trackPointers[i] = trackPointer;
            trackOffsets[i] = trackOffset;
        }

        header = new Mp2kSongHeader(
            tableEntry.SongId,
            offset,
            trackCount,
            blockCount,
            priority,
            reverb,
            voiceGroupPointer,
            voiceGroupOffset,
            trackPointers,
            trackOffsets,
            rom.Slice(offset, headerSize).ToArray());

        return true;
    }
}

