using System;
using AgbSynth.App.GBA;

namespace AgbSynth.App.MP2K;

public static class Mp2kSongTableParser
{
    public const int DefaultEntrySize = 8;

    public static bool TryReadEntry(
        GbaRom rom,
        int tableOffset,
        int songId,
        out Mp2kSongTableEntry entry)
    {
        entry = null!;
        int entryOffset = tableOffset + checked(songId * DefaultEntrySize);

        if (!rom.TryReadUInt32LittleEndian(entryOffset, out uint headerPointer))
            return false;
        if (!GbaAddress.TryToOffset(headerPointer, rom.Length, out int headerOffset))
            return false;

        byte[] raw = rom.Slice(entryOffset, DefaultEntrySize).ToArray();
        entry = new Mp2kSongTableEntry(songId, entryOffset, headerPointer, headerOffset, raw);
        return true;
    }
}

