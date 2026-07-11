using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgbSynth.App.MIDI;

public static class MidiFileWriter
{
    public static void Write(string path, IReadOnlyList<MidiTrack> tracks, short ticksPerQuarter)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, Build(tracks, ticksPerQuarter));
    }

    public static byte[] Build(IReadOnlyList<MidiTrack> tracks, short ticksPerQuarter)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "MThd");
        WriteU32(stream, 6);
        WriteU16(stream, tracks.Count > 1 ? (ushort)1 : (ushort)0);
        WriteU16(stream, checked((ushort)tracks.Count));
        WriteU16(stream, checked((ushort)ticksPerQuarter));

        foreach (var track in tracks)
            WriteTrack(stream, track);

        return stream.ToArray();
    }

    private static void WriteTrack(Stream output, MidiTrack track)
    {
        using var trackData = new MemoryStream();
        int previousTick = 0;

        foreach (var ev in track.Events.OrderBy(e => e.Tick).ThenBy(e => e.Priority).ThenBy(e => e.Order))
        {
            WriteVariableLength(trackData, ev.Tick - previousTick);
            trackData.Write(ev.Data);
            previousTick = ev.Tick;
        }

        WriteVariableLength(trackData, 0);
        trackData.WriteByte(0xFF);
        trackData.WriteByte(0x2F);
        trackData.WriteByte(0x00);

        WriteAscii(output, "MTrk");
        WriteU32(output, checked((uint)trackData.Length));
        trackData.Position = 0;
        trackData.CopyTo(output);
    }

    private static void WriteVariableLength(Stream stream, int value)
    {
        uint buffer = (uint)Math.Max(0, value) & 0x7F;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= ((uint)value & 0x7F) | 0x80;
        }

        while (true)
        {
            stream.WriteByte((byte)buffer);
            if ((buffer & 0x80) == 0)
                break;
            buffer >>= 8;
        }
    }

    private static void WriteAscii(Stream stream, string text)
    {
        stream.Write(Encoding.ASCII.GetBytes(text));
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }
}
