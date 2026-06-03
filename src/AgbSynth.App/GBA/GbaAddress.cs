using System;

namespace AgbSynth.App.GBA;

public static class GbaAddress
{
    public const uint RomBase = 0x08000000;
    public const uint RomMirrorEndExclusive = 0x0E000000;
    public const int MaxRomSize = 32 * 1024 * 1024;

    public static bool IsRomPointer(uint address)
        => address >= RomBase && address < RomMirrorEndExclusive;

    public static bool TryToOffset(uint address, int romLength, out int offset)
    {
        offset = 0;
        if (!IsRomPointer(address))
            return false;

        uint normalized = (address - RomBase) % MaxRomSize;
        if (normalized >= romLength)
            return false;

        offset = (int)normalized;
        return true;
    }

    public static uint ToPointer(int offset)
    {
        if (offset < 0 || offset >= MaxRomSize)
            throw new ArgumentOutOfRangeException(nameof(offset), "GBA ROM offset is out of range.");

        return RomBase + (uint)offset;
    }
}

