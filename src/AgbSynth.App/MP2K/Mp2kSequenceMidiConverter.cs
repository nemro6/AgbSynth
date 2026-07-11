using System;
using System.Collections.Generic;
using System.IO;
using AgbSynth.App.GBA;
using AgbSynth.App.MIDI;
using AgbSynth.App.Project;

namespace AgbSynth.App.MP2K;

public static class Mp2kSequenceMidiConverter
{
    public const short TicksPerQuarter = 48;
    private const int MaxEventsPerTrack = 100_000;
    private const int MaxCallDepth = 16;
    private const int LoopExtraPasses = 0;

    private static readonly int[] WaitTicks =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 28, 30, 32, 36, 40, 42, 44,
        48, 52, 54, 56, 60, 64, 66, 68, 72, 76, 78, 80, 84, 88, 90, 92, 96
    ];

    private static readonly int[] NoteTicks =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 28, 30, 32, 36, 40, 42, 44, 48,
        52, 54, 56, 60, 64, 66, 68, 72, 76, 78, 80, 84, 88, 90, 92, 96
    ];

    public static void WriteMidiFile(
        GbaRom rom,
        SongHeaderProjectInfo songHeader,
        string path,
        MidiCcMapping? midiCcMapping = null)
    {
        MidiFileWriter.Write(path, BuildMidi(rom, songHeader, midiCcMapping), TicksPerQuarter);
    }

    public static IReadOnlyList<MidiTrack> BuildMidi(
        GbaRom rom,
        SongHeaderProjectInfo songHeader,
        MidiCcMapping? midiCcMapping = null)
    {
        midiCcMapping ??= MidiCcMapping.Default;
        var tracks = new List<MidiTrack>();
        var conductor = new MidiTrack($"Song {songHeader.SongId:D3}");
        conductor.AddMetaText(0, 0x01, $"AgbSynth MP2K export: SongId={songHeader.SongId}, Header=0x{songHeader.HeaderOffset:X}");
        if (songHeader.VoiceGroupId is int voiceGroupId)
            conductor.AddMetaText(0, 0x01, $"VoiceGroupId={voiceGroupId}");
        if (!string.IsNullOrWhiteSpace(songHeader.VoiceGroupFilePath))
            conductor.AddMetaText(0, 0x01, $"VoiceGroupFile={songHeader.VoiceGroupFilePath}");
        if (!string.IsNullOrWhiteSpace(songHeader.VoiceGroupPointer))
            conductor.AddMetaText(0, 0x01, $"VoiceGroupPointer={songHeader.VoiceGroupPointer}");
        tracks.Add(conductor);

        for (int i = 0; i < songHeader.TrackOffsets.Count; i++)
        {
            var track = new MidiTrack($"Track {i + 1:D2}");
            ParseTrack(rom, songHeader.TrackOffsets[i], i, track, conductor, midiCcMapping);
            tracks.Add(track);
        }

        if (!HasTempoAtTick(conductor, 0))
            // MP2K initializes tempoD to 150; TEMPO command bytes are doubled by the driver.
            conductor.AddTempo(0, 75);

        return tracks;
    }

    private static void ParseTrack(
        GbaRom rom,
        int startOffset,
        int trackIndex,
        MidiTrack track,
        MidiTrack conductor,
        MidiCcMapping midiCcMapping)
    {
        if (startOffset < 0 || startOffset >= rom.Length)
            return;

        ReadOnlySpan<byte> bytes = rom.Bytes.Span;
        int position = startOffset;
        int tick = 0;
        int keyShift = 0;
        int lastNote = 60;
        int lastVelocity = 100;
        int runningCommand = 0;
        int channel = trackIndex % 16;

        var callStack = new Stack<CallFrame>();
        var activeTies = new Dictionary<int, int>();
        var offsetTicks = new Dictionary<int, int>();
        var loopCounts = new Dictionary<int, int>();
        int eventBudget = MaxEventsPerTrack;

        while (position >= 0 && position < rom.Length && eventBudget-- > 0)
        {
            offsetTicks.TryAdd(position, tick);
            byte command = bytes[position++];
            int? firstParameter = null;

            if (command <= 0x7F)
            {
                if (!CanUseRunningCommand(runningCommand))
                    continue;

                firstParameter = command;
                command = (byte)runningCommand;
            }
            else if (command >= 0xBD)
            {
                // MP2K only keeps commands from VOICE (0xBD) upward as running status.
                runningCommand = command;
            }

            if (command is >= 0x80 and <= 0xB0)
            {
                tick += WaitTicks[command - 0x80];
                continue;
            }

            if (command >= 0xD0)
            {
                int noteLength = NoteTicks[command - 0xD0];
                ReadNote(
                    bytes,
                    ref position,
                    keyShift,
                    firstParameter,
                    allowAddedDuration: true,
                    ref lastNote,
                    ref lastVelocity,
                    out int note,
                    out int velocity,
                    out int addedDuration);
                AddNote(track, tick, channel, note, velocity, noteLength + addedDuration);
                continue;
            }

            switch (command)
            {
                case 0xB1: // FINE
                    if (TryCompleteCall(callStack, ref position))
                        break;

                    EndTies(track, tick, channel, activeTies);
                    return;

                case 0xB2: // GOTO
                    if (!TryReadPointer(rom, ref position, out uint gotoPointer, out int gotoOffset))
                        return;

                    if (gotoOffset >= position)
                    {
                        position = gotoOffset;
                        break;
                    }

                    AddControlChange(track, tick, channel, midiCcMapping.LoopEnd, 127);
                    if (offsetTicks.TryGetValue(gotoOffset, out int loopStartTick))
                        AddControlChange(track, loopStartTick, channel, midiCcMapping.LoopStart, 127);

                    int loopKey = position - 5;
                    loopCounts.TryGetValue(loopKey, out int loopCount);
                    if (loopCount < LoopExtraPasses)
                    {
                        loopCounts[loopKey] = loopCount + 1;
                        position = gotoOffset;
                        break;
                    }

                    EndTies(track, tick, channel, activeTies);
                    return;

                case 0xB3: // PATT
                    if (!TryReadPointer(rom, ref position, out uint patternPointer, out int patternOffset))
                        break;
                    if (callStack.Count >= MaxCallDepth)
                        break;
                    callStack.Push(new CallFrame(position, patternOffset, 1));
                    position = patternOffset;
                    break;

                case 0xB4: // PEND
                    if (!TryCompleteCall(callStack, ref position))
                        break;
                    break;

                case 0xB5: // REPT count + pattern pointer
                    if (!ReadRequiredByte(bytes, ref position, firstParameter, out int repeatCount))
                        break;
                    if (!TryReadPointer(rom, ref position, out uint repeatPointer, out int repeatOffset))
                        break;
                    if (repeatCount <= 0)
                        break;
                    if (callStack.Count >= MaxCallDepth)
                        break;

                    callStack.Push(new CallFrame(position, repeatOffset, repeatCount));
                    position = repeatOffset;
                    break;

                case 0xB9: // MEMACC
                    ConsumeMemoryAccess(bytes, ref position);
                    break;

                case 0xBA: // PRIO
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int priority))
                        AddControlChange(track, tick, channel, midiCcMapping.Priority, priority);
                    break;

                case 0xBB: // TEMPO
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int tempo))
                        conductor.AddTempo(tick, tempo);
                    break;

                case 0xBC: // KEYSH
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int rawKeyShift))
                        keyShift = ToSigned7Bit(rawKeyShift);
                    break;

                case 0xBD: // VOICE
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int voice))
                        track.Add(tick, 0, (byte)(0xC0 | channel), (byte)Math.Clamp(voice, 0, 127));
                    break;

                case 0xBE: // VOL
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int volume))
                        AddControlChange(track, tick, channel, midiCcMapping.Volume, volume);
                    break;

                case 0xBF: // PAN
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int pan))
                        AddControlChange(track, tick, channel, midiCcMapping.Pan, pan);
                    break;

                case 0xC0: // BEND
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int bend))
                        AddPitchBend(track, tick, channel, bend);
                    break;

                case 0xC1: // BENDR
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int bendRange))
                        AddBendRange(track, tick, channel, bendRange, midiCcMapping);
                    break;

                case 0xC2: // LFOS
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int lfoSpeed))
                        AddControlChange(track, tick, channel, midiCcMapping.LfoSpeed, lfoSpeed);
                    break;

                case 0xC3: // LFODL
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int lfoDelay))
                        AddControlChange(track, tick, channel, midiCcMapping.LfoDelay, lfoDelay);
                    break;

                case 0xC4: // MOD
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int modulation))
                        AddControlChange(track, tick, channel, midiCcMapping.Modulation, modulation);
                    break;

                case 0xC5: // MODT
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int modulationType))
                        AddControlChange(track, tick, channel, midiCcMapping.ModulationType, modulationType);
                    break;

                case 0xCD: // XCMD
                    if (!ParseExtendedCommand(bytes, ref position, ref tick, channel, track, midiCcMapping))
                    {
                        EndTies(track, tick, channel, activeTies);
                        return;
                    }
                    break;

                case 0xC8: // TUNE
                    if (ReadRequiredByte(bytes, ref position, firstParameter, out int tune))
                        AddControlChange(track, tick, channel, midiCcMapping.Tune, tune);
                    break;

                case 0xCE: // EOT
                    if (ReadOptionalParameter(bytes, ref position, firstParameter, out int offNote))
                        EndTie(track, tick, channel, offNote + keyShift, activeTies);
                    else
                        EndTies(track, tick, channel, activeTies);
                    break;

                case 0xCF: // TIE
                    ReadNote(
                        bytes,
                        ref position,
                        keyShift,
                        firstParameter,
                        allowAddedDuration: false,
                        ref lastNote,
                        ref lastVelocity,
                        out int tiedNote,
                        out int tiedVelocity,
                        out _);
                    track.Add(tick, 1, (byte)(0x90 | channel), (byte)tiedNote, (byte)tiedVelocity);
                    activeTies[tiedNote] = tick;
                    break;

                default:
                    track.AddMetaText(tick, 0x01, $"Unsupported MP2K command 0x{command:X2} at 0x{position - 1:X}");
                    EndTies(track, tick, channel, activeTies);
                    return;
            }
        }

        EndTies(track, tick, channel, activeTies);
    }

    private static bool CanUseRunningCommand(int command)
    {
        return command >= 0xBD;
    }

    private static void ConsumeMemoryAccess(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (!TryReadByte(bytes, ref position, out int operation))
            return;

        Skip(bytes, ref position, 2);
        if (operation is >= 6 and <= 17)
            Skip(bytes, ref position, 4);
    }

    private static bool ParseExtendedCommand(
        ReadOnlySpan<byte> bytes,
        ref int position,
        ref int tick,
        int channel,
        MidiTrack track,
        MidiCcMapping midiCcMapping)
    {
        if (!TryReadByte(bytes, ref position, out int subcommand))
            return false;

        int argumentLength = subcommand switch
        {
            0x00 or 0x03 => 0,
            0x01 or 0x0D => 4,
            0x0C => 2,
            >= 0x02 and <= 0x0B => 1,
            _ => -1
        };
        if (argumentLength < 0 || position + argumentLength > bytes.Length)
            return false;

        uint argument = 0;
        for (int i = 0; i < argumentLength; i++)
            argument |= (uint)bytes[position + i] << (i * 8);
        position += argumentLength;

        if (subcommand == 0x0C)
            tick += (int)(argument & 0xFFFF);

        string name = subcommand switch
        {
            0x00 => "xFINE0",
            0x01 => "xWAVE",
            0x02 => "xTYPE",
            0x03 => "xFINE3",
            0x04 => "xATTA",
            0x05 => "xDECA",
            0x06 => "xSUST",
            0x07 => "xRELE",
            0x08 => "xIECV",
            0x09 => "xIECL",
            0x0A => "xLENG",
            0x0B => "xSWEE",
            0x0C => "xWAIT",
            0x0D => "xCMD_0D",
            _ => $"XCMD_{subcommand:X2}"
        };
        int hexWidth = Math.Max(2, argumentLength * 2);
        string argumentHex = argument.ToString("X" + hexWidth);
        track.AddMetaText(tick, 0x01, $"{name}=0x{argumentHex}");
        if (argumentLength == 1 && midiCcMapping.GetControllerForSubcommand(subcommand) is int controller)
            AddControlChange(track, tick, channel, controller, (int)argument);
        return subcommand is not 0x00 and not 0x03;
    }

    private static bool HasTempoAtTick(MidiTrack track, int tick)
    {
        foreach (var ev in track.Events)
        {
            if (ev.Tick == tick && ev.Data.Length >= 2 && ev.Data[0] == 0xFF && ev.Data[1] == 0x51)
                return true;
        }

        return false;
    }

    private static bool TryCompleteCall(Stack<CallFrame> callStack, ref int position)
    {
        if (callStack.Count == 0)
            return false;

        var frame = callStack.Pop();
        if (frame.RemainingRepeats > 1)
        {
            callStack.Push(frame with { RemainingRepeats = frame.RemainingRepeats - 1 });
            position = frame.TargetOffset;
        }
        else
        {
            position = frame.ReturnOffset;
        }

        return true;
    }

    private readonly record struct CallFrame(int ReturnOffset, int TargetOffset, int RemainingRepeats);

    private static void ReadNote(
        ReadOnlySpan<byte> bytes,
        ref int position,
        int keyShift,
        int? firstParameter,
        bool allowAddedDuration,
        ref int lastNote,
        ref int lastVelocity,
        out int note,
        out int velocity,
        out int addedDuration)
    {
        if (ReadOptionalParameter(bytes, ref position, firstParameter, out int rawNote))
            lastNote = rawNote;
        if (TryReadOptionalByte(bytes, ref position, out int rawVelocity))
            lastVelocity = rawVelocity;

        addedDuration = 0;
        if (allowAddedDuration &&
            position >= 0 &&
            position < bytes.Length &&
            bytes[position] <= 3)
        {
            addedDuration = bytes[position++];
        }

        note = Math.Clamp(lastNote + keyShift, 0, 127);
        velocity = Math.Clamp(lastVelocity, 1, 127);
    }

    private static void AddNote(MidiTrack track, int tick, int channel, int note, int velocity, int length)
    {
        track.Add(tick, 1, (byte)(0x90 | channel), (byte)note, (byte)velocity);
        track.Add(tick + Math.Max(1, length), -1, (byte)(0x80 | channel), (byte)note, 0);
    }

    private static void AddControlChange(MidiTrack track, int tick, int channel, int controller, int value)
    {
        track.Add(tick, 0, (byte)(0xB0 | channel), (byte)controller, (byte)Math.Clamp(value, 0, 127));
    }

    private static void AddBendRange(
        MidiTrack track,
        int tick,
        int channel,
        int value,
        MidiCcMapping midiCcMapping)
    {
        int clamped = Math.Clamp(value, 0, 255);
        AddControlChange(track, tick, channel, midiCcMapping.BendRangeLow, clamped & 0x7F);
        AddControlChange(track, tick, channel, midiCcMapping.BendRangeHigh, clamped >> 7);
    }

    private static void AddPitchBend(MidiTrack track, int tick, int channel, int gbaValue)
    {
        int clamped = Math.Clamp(gbaValue, 0, 127);
        int midiValue = clamped >= 64
            ? 8192 + (int)Math.Round((clamped - 64) * (8191.0 / 63.0))
            : (int)Math.Round(clamped * (8192.0 / 64.0));
        track.Add(tick, 0, (byte)(0xE0 | channel), (byte)(midiValue & 0x7F), (byte)((midiValue >> 7) & 0x7F));
    }

    private static void EndTie(MidiTrack track, int tick, int channel, int note, Dictionary<int, int> activeTies)
    {
        int clamped = Math.Clamp(note, 0, 127);
        if (!activeTies.Remove(clamped))
            return;

        track.Add(tick, -1, (byte)(0x80 | channel), (byte)clamped, 0);
    }

    private static void EndTies(MidiTrack track, int tick, int channel, Dictionary<int, int> activeTies)
    {
        foreach (int note in activeTies.Keys)
            track.Add(tick, -1, (byte)(0x80 | channel), (byte)note, 0);
        activeTies.Clear();
    }

    private static bool TryReadPointer(GbaRom rom, ref int position, out uint pointer, out int offset)
    {
        pointer = 0;
        offset = 0;
        if (!rom.TryReadUInt32LittleEndian(position, out pointer))
            return false;

        position += 4;
        return GbaAddress.TryToOffset(pointer, rom.Length, out offset);
    }

    private static bool TryReadByte(ReadOnlySpan<byte> bytes, ref int position, out int value)
    {
        value = 0;
        if (position < 0 || position >= bytes.Length)
            return false;

        value = bytes[position++];
        return true;
    }

    private static bool ReadRequiredByte(ReadOnlySpan<byte> bytes, ref int position, int? firstParameter, out int value)
    {
        if (firstParameter is int provided)
        {
            value = provided;
            return true;
        }

        return TryReadByte(bytes, ref position, out value);
    }

    private static bool ReadOptionalParameter(ReadOnlySpan<byte> bytes, ref int position, int? firstParameter, out int value)
    {
        if (firstParameter is int provided)
        {
            value = provided;
            return true;
        }

        return TryReadOptionalByte(bytes, ref position, out value);
    }

    private static bool TryReadOptionalByte(ReadOnlySpan<byte> bytes, ref int position, out int value)
    {
        value = 0;
        if (position < 0 || position >= bytes.Length || bytes[position] >= 0x80)
            return false;

        value = bytes[position++];
        return true;
    }

    private static void Skip(ReadOnlySpan<byte> bytes, ref int position, int count)
    {
        position = Math.Min(bytes.Length, position + count);
    }

    private static int ToSigned7Bit(int value)
    {
        int safe = value & 0x7F;
        return safe >= 64 ? safe - 128 : safe;
    }
}
