namespace AgbSynth.App.GBA;

using System;

public static class Crc32Calculator
{
    private const uint Polynomial = 0xEDB88320;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
        }

        return ~crc;
    }
}
