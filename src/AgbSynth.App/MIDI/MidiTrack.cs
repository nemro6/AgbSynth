using System;
using System.Collections.Generic;
using System.Text;

namespace AgbSynth.App.MIDI;

public sealed class MidiTrack
{
    private int _order;

    public MidiTrack(string name)
    {
        Name = name;
        AddMetaText(0, 0x03, name);
    }

    public string Name { get; }
    public List<MidiTrackEvent> Events { get; } = new();

    public void Add(int tick, int priority, params byte[] data)
    {
        Events.Add(new MidiTrackEvent(Math.Max(0, tick), priority, _order++, data));
    }

    public void AddTempo(int tick, int bpm)
    {
        int safeBpm = Math.Clamp(bpm, 1, 300);
        int microsecondsPerQuarter = 60_000_000 / safeBpm;
        Add(
            tick,
            -10,
            0xFF,
            0x51,
            0x03,
            (byte)((microsecondsPerQuarter >> 16) & 0xFF),
            (byte)((microsecondsPerQuarter >> 8) & 0xFF),
            (byte)(microsecondsPerQuarter & 0xFF));
    }

    public void AddMetaText(int tick, byte type, string text)
    {
        byte[] encoded = Encoding.ASCII.GetBytes(text);
        int length = Math.Min(127, encoded.Length);
        byte[] data = new byte[3 + length];
        data[0] = 0xFF;
        data[1] = type;
        data[2] = (byte)length;
        Array.Copy(encoded, 0, data, 3, length);
        Add(tick, -5, data);
    }
}

public sealed record MidiTrackEvent(int Tick, int Priority, int Order, byte[] Data);
