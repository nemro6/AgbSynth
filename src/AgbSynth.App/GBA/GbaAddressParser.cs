using System;
using System.Globalization;

namespace AgbSynth.App.GBA;

public static class GbaAddressParser
{
    public static bool TryParseRomAddressOrOffset(string text, int romLength, out int offset, out string? error)
    {
        offset = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Address is required.";
            return false;
        }

        string normalized = text.Trim();
        NumberStyles style = NumberStyles.Integer;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
            style = NumberStyles.HexNumber;
        }

        if (!uint.TryParse(normalized, style, CultureInfo.InvariantCulture, out uint value))
        {
            error = "Address must be a decimal or hex value.";
            return false;
        }

        if (GbaAddress.IsRomPointer(value))
        {
            if (!GbaAddress.TryToOffset(value, romLength, out offset))
            {
                error = "GBA pointer is outside this ROM.";
                return false;
            }

            return true;
        }

        if (value >= romLength)
        {
            error = "ROM offset is outside this ROM.";
            return false;
        }

        offset = (int)value;
        return true;
    }
}

