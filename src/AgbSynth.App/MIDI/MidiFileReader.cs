using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgbSynth.App.MIDI;

public static class MidiFileReader
{
    public static MidiPlaybackFile Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var reader = new Reader(bytes);
        if (reader.ReadAscii(4) != "MThd")
            throw new InvalidDataException("MIDI header chunk was not found.");

        uint headerLength = reader.ReadU32();
        if (headerLength < 6)
            throw new InvalidDataException("MIDI header chunk is too short.");

        ushort format = reader.ReadU16();
        ushort trackCount = reader.ReadU16();
        short division = unchecked((short)reader.ReadU16());
        if (division <= 0)
            throw new InvalidDataException("SMPTE MIDI time division is not supported.");

        reader.Skip((int)headerLength - 6);

        var events = new List<MidiPlaybackEvent>();
        int order = 0;
        for (int trackIndex = 0; trackIndex < trackCount && !reader.End; trackIndex++)
        {
            string chunkId = reader.ReadAscii(4);
            uint chunkLength = reader.ReadU32();
            if (chunkId != "MTrk")
            {
                reader.Skip((int)chunkLength);
                continue;
            }

            int trackEnd = checked(reader.Position + (int)chunkLength);
            ReadTrack(reader, trackEnd, trackIndex, events, ref order);
            reader.Position = trackEnd;
        }

        if (format > 1)
            throw new InvalidDataException($"MIDI format {format} is not supported.");

        return new MidiPlaybackFile(
            division,
            events.OrderBy(e => e.Tick).ThenBy(e => e.Order).ToList());
    }

    private static void ReadTrack(Reader reader, int trackEnd, int trackIndex, List<MidiPlaybackEvent> events, ref int order)
    {
        int tick = 0;
        int runningStatus = 0;
        while (reader.Position < trackEnd)
        {
            tick += reader.ReadVariableLength();
            int status = reader.ReadByte();
            if (status < 0x80)
            {
                if (runningStatus == 0)
                    throw new InvalidDataException("MIDI running status appeared before a status byte.");
                reader.Position--;
                status = runningStatus;
            }
            else if (status < 0xF0)
            {
                runningStatus = status;
            }

            if (status == 0xFF)
            {
                int metaType = reader.ReadByte();
                int length = reader.ReadVariableLength();
                if (metaType == 0x2F)
                {
                    reader.Skip(length);
                    return;
                }

                if (metaType == 0x51 && length == 3)
                {
                    int usPerQuarter = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
                    events.Add(new MidiPlaybackEvent(tick, order++, trackIndex, MidiPlaybackEventKind.Tempo, 0, 0, 0, usPerQuarter));
                }
                else
                {
                    reader.Skip(length);
                }

                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                reader.Skip(reader.ReadVariableLength());
                continue;
            }

            int command = status & 0xF0;
            int channel = status & 0x0F;
            switch (command)
            {
                case 0x80:
                {
                    int note = reader.ReadByte();
                    reader.Skip(1);
                    events.Add(new MidiPlaybackEvent(tick, order++, trackIndex, MidiPlaybackEventKind.NoteOff, channel, note, 0, 0));
                    break;
                }
                case 0x90:
                {
                    int note = reader.ReadByte();
                    int velocity = reader.ReadByte();
                    events.Add(new MidiPlaybackEvent(
                        tick,
                        order++,
                        trackIndex,
                        velocity == 0 ? MidiPlaybackEventKind.NoteOff : MidiPlaybackEventKind.NoteOn,
                        channel,
                        note,
                        velocity,
                        0));
                    break;
                }
                case 0xB0:
                {
                    int controller = reader.ReadByte();
                    int value = reader.ReadByte();
                    events.Add(new MidiPlaybackEvent(tick, order++, trackIndex, MidiPlaybackEventKind.ControlChange, channel, controller, value, 0));
                    break;
                }
                case 0xC0:
                {
                    int program = reader.ReadByte();
                    events.Add(new MidiPlaybackEvent(tick, order++, trackIndex, MidiPlaybackEventKind.ProgramChange, channel, program, 0, 0));
                    break;
                }
                case 0xE0:
                {
                    int lsb = reader.ReadByte();
                    int msb = reader.ReadByte();
                    events.Add(new MidiPlaybackEvent(tick, order++, trackIndex, MidiPlaybackEventKind.PitchBend, channel, lsb | (msb << 7), 0, 0));
                    break;
                }
                case 0xA0:
                case 0xD0:
                    reader.Skip(command == 0xD0 ? 1 : 2);
                    break;
                default:
                    int dataLength = command == 0xC0 || command == 0xD0 ? 1 : 2;
                    reader.Skip(dataLength);
                    break;
            }
        }
    }

    private sealed class Reader
    {
        private readonly byte[] _bytes;

        public Reader(byte[] bytes)
        {
            _bytes = bytes;
        }

        public int Position { get; set; }
        public bool End => Position >= _bytes.Length;

        public string ReadAscii(int length)
        {
            Ensure(length);
            string result = Encoding.ASCII.GetString(_bytes, Position, length);
            Position += length;
            return result;
        }

        public int ReadByte()
        {
            Ensure(1);
            return _bytes[Position++];
        }

        public ushort ReadU16()
        {
            Ensure(2);
            return (ushort)((_bytes[Position++] << 8) | _bytes[Position++]);
        }

        public uint ReadU32()
        {
            Ensure(4);
            return ((uint)_bytes[Position++] << 24)
                | ((uint)_bytes[Position++] << 16)
                | ((uint)_bytes[Position++] << 8)
                | _bytes[Position++];
        }

        public int ReadVariableLength()
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = ReadByte();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("MIDI variable length quantity is too long.");
        }

        public void Skip(int length)
        {
            Ensure(length);
            Position += length;
        }

        private void Ensure(int length)
        {
            if (length < 0 || Position + length > _bytes.Length)
                throw new EndOfStreamException("Unexpected end of MIDI file.");
        }
    }
}

public sealed record MidiPlaybackFile(int TicksPerQuarter, IReadOnlyList<MidiPlaybackEvent> Events);

public sealed record MidiPlaybackEvent(
    int Tick,
    int Order,
    int TrackIndex,
    MidiPlaybackEventKind Kind,
    int Channel,
    int Data1,
    int Data2,
    int Data3);

public enum MidiPlaybackEventKind
{
    NoteOff,
    NoteOn,
    ControlChange,
    ProgramChange,
    PitchBend,
    Tempo
}
