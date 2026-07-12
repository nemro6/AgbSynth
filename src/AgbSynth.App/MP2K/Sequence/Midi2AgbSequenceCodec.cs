using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgbSynth.App.MIDI;

namespace AgbSynth.App.MP2K.Sequence;

public static partial class Midi2AgbSequenceCodec
{
    private const int TicksPerQuarter = 48;
    private const int MaxCommandsPerTrack = 200_000;
    private static readonly int[] LegalDurations =
    [
        96, 92, 90, 88, 84, 80, 78, 76, 72, 68, 66, 64, 60, 56, 54, 52,
        48, 44, 42, 40, 36, 32, 30, 28, 24, 23, 22, 21, 20, 19, 18, 17,
        16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1
    ];

    public static MidiPlaybackFile Parse(string source, MidiCcMapping? mapping = null, SequenceConversionReport? report = null)
    {
        mapping ??= MidiCcMapping.Default;
        report ??= new SequenceConversionReport();
        ParsedSource parsed = Tokenize(source);
        List<(string Label, int Position)> starts = FindTrackStarts(parsed);
        var events = new List<MidiPlaybackEvent>();
        int order = 0;

        for (int trackIndex = 0; trackIndex < starts.Count; trackIndex++)
            ParseTrack(parsed, starts[trackIndex].Position, trackIndex, mapping, report, events, ref order);

        if (starts.Count == 0)
            throw new InvalidDataException("No Midi2agb track labels were found.");

        return new MidiPlaybackFile(
            TicksPerQuarter,
            events.OrderBy(value => value.Tick).ThenBy(value => value.Order).ToList());
    }

    public static string Write(MidiPlaybackFile midi, string songName, MidiCcMapping? mapping = null, SequenceConversionReport? report = null)
    {
        mapping ??= MidiCcMapping.Default;
        report ??= new SequenceConversionReport();
        string safeName = SanitizeLabel(songName);
        int[] sourceTracks = midi.Events.Select(value => value.TrackIndex).Where(value => value > 0).Distinct().OrderBy(value => value).ToArray();
        if (sourceTracks.Length == 0)
            sourceTracks = [0];

        var builder = new StringBuilder();
        builder.AppendLine("\t.include \"MPlayDef.s\"");
        builder.AppendLine("\t.section .rodata");
        builder.AppendLine($"\t.global {safeName}");
        builder.AppendLine();
        builder.AppendLine($"{safeName}:");
        builder.AppendLine($"\t.byte {sourceTracks.Length}, 0, 0, 0");
        builder.AppendLine("\t.word 0");
        foreach (int sourceTrack in sourceTracks)
            builder.AppendLine($"\t.word {safeName}_track_{sourceTrack:D2}");

        IReadOnlyList<MidiPlaybackEvent> conductorTempo = midi.Events
            .Where(value => value.Kind == MidiPlaybackEventKind.Tempo && value.TrackIndex == 0)
            .ToList();
        foreach (int sourceTrack in sourceTracks)
        {
            builder.AppendLine();
            builder.AppendLine($"{safeName}_track_{sourceTrack:D2}:");
            builder.AppendLine("\t.byte KEYSH, 0");
            List<MidiPlaybackEvent> trackEvents = midi.Events
                .Where(value => value.TrackIndex == sourceTrack || (sourceTrack == sourceTracks[0] && value.Kind == MidiPlaybackEventKind.Tempo && value.TrackIndex == 0))
                .OrderBy(value => value.Tick).ThenBy(value => EventWritePriority(value.Kind)).ThenBy(value => value.Order)
                .ToList();
            WriteTrack(builder, trackEvents, sourceTrack, mapping, report, $"{safeName}_track_{sourceTrack:D2}");
        }

        return builder.ToString();
    }

    private static void WriteTrack(
        StringBuilder builder,
        IReadOnlyList<MidiPlaybackEvent> events,
        int trackIndex,
        MidiCcMapping mapping,
        SequenceConversionReport report,
        string trackLabel)
    {
        var noteEnds = BuildNoteEndLookup(events);
        var tiedNoteOffOrders = new HashSet<int>();
        int currentTick = 0;
        int? loopStartTick = events.FirstOrDefault(value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == mapping.LoopStart)?.Tick;
        string loopLabel = $"{trackLabel}_loop";

        foreach (IGrouping<int, MidiPlaybackEvent> group in events.GroupBy(value => value.Tick).OrderBy(value => value.Key))
        {
            if (group.Key < currentTick)
                continue;
            WriteWait(builder, group.Key - currentTick);
            currentTick = group.Key;
            if (loopStartTick == currentTick)
                builder.AppendLine($"{loopLabel}:");

            foreach (MidiPlaybackEvent value in group)
            {
                switch (value.Kind)
                {
                    case MidiPlaybackEventKind.NoteOn:
                    {
                        NoteEnd end = noteEnds.TryGetValue(value.Order, out NoteEnd pair) ? pair : new NoteEnd(value.Tick + 1, -1);
                        int length = Math.Max(1, end.Tick - value.Tick);
                        if (length > 223 && end.Order >= 0)
                        {
                            builder.AppendLine($"\t.byte TIE, {value.Data1}, {value.Data2}");
                            tiedNoteOffOrders.Add(end.Order);
                            break;
                        }
                        int commandLength = LegalDurations.First(candidate => candidate <= Math.Min(96, length));
                        int addedDuration = length - commandLength;
                        string added = addedDuration > 0 ? $", {addedDuration}" : string.Empty;
                        builder.AppendLine($"\t.byte N{commandLength:D2}, {value.Data1}, {value.Data2}{added}");
                        break;
                    }
                    case MidiPlaybackEventKind.NoteOff:
                        if (tiedNoteOffOrders.Contains(value.Order))
                            builder.AppendLine($"\t.byte EOT, {value.Data1}");
                        break;
                    case MidiPlaybackEventKind.ProgramChange:
                        builder.AppendLine($"\t.byte VOICE, {value.Data1}");
                        break;
                    case MidiPlaybackEventKind.PitchBend:
                        builder.AppendLine($"\t.byte BEND, {Math.Clamp((int)Math.Round((value.Data1 - 8192) / 128.0) + 64, 0, 127)}");
                        break;
                    case MidiPlaybackEventKind.Tempo:
                    {
                        int bpm = value.Data3 <= 0 ? 75 : Math.Clamp((int)Math.Round(60_000_000.0 / value.Data3), 1, 255);
                        builder.AppendLine($"\t.byte TEMPO, {bpm}");
                        break;
                    }
                    case MidiPlaybackEventKind.ControlChange:
                        if (value.Data1 == mapping.LoopStart)
                            break;
                        if (value.Data1 == mapping.LoopEnd && loopStartTick is not null)
                        {
                            builder.AppendLine("\t.byte GOTO");
                            builder.AppendLine($"\t.word {loopLabel}");
                            break;
                        }
                        WriteControlChange(builder, value, mapping, trackIndex, report);
                        break;
                }
            }
        }

        builder.AppendLine("\t.byte FINE");
    }

    private static Dictionary<int, NoteEnd> BuildNoteEndLookup(IReadOnlyList<MidiPlaybackEvent> events)
    {
        var active = new Dictionary<(int Channel, int Note), Queue<MidiPlaybackEvent>>();
        var result = new Dictionary<int, NoteEnd>();
        foreach (MidiPlaybackEvent value in events.OrderBy(value => value.Tick).ThenBy(value => value.Order))
        {
            var key = (value.Channel, value.Data1);
            if (value.Kind == MidiPlaybackEventKind.NoteOn)
            {
                if (!active.TryGetValue(key, out Queue<MidiPlaybackEvent>? queue))
                    active[key] = queue = new Queue<MidiPlaybackEvent>();
                queue.Enqueue(value);
            }
            else if (value.Kind == MidiPlaybackEventKind.NoteOff && active.TryGetValue(key, out Queue<MidiPlaybackEvent>? queue) && queue.Count > 0)
            {
                result[queue.Dequeue().Order] = new NoteEnd(value.Tick, value.Order);
            }
        }
        return result;
    }

    private static void WriteControlChange(StringBuilder builder, MidiPlaybackEvent value, MidiCcMapping mapping, int trackIndex, SequenceConversionReport report)
    {
        string? command = value.Data1 switch
        {
            var cc when cc == mapping.Modulation => "MOD",
            var cc when cc == mapping.Volume => "VOL",
            var cc when cc == mapping.Pan => "PAN",
            var cc when cc == mapping.Tune => "TUNE",
            var cc when cc == mapping.Priority => "PRIO",
            var cc when cc == mapping.LfoSpeed => "LFOS",
            var cc when cc == mapping.ModulationType => "MODT",
            var cc when cc == mapping.LfoDelay => "LFODL",
            _ => null
        };
        if (command is not null)
        {
            builder.AppendLine($"\t.byte {command}, {value.Data2}");
            return;
        }
        if (value.Data1 == mapping.BendRangeLow)
        {
            builder.AppendLine($"\t.byte BENDR, {value.Data2}");
            return;
        }
        MidiCcEntry? extended = mapping.GetExtendedEntries().FirstOrDefault(entry => entry.Controller == value.Data1);
        if (extended?.Subcommand is int subcommand)
        {
            builder.AppendLine($"\t.byte XCMD, 0x{subcommand:X2}, {value.Data2}");
            return;
        }
        if (value.Data1 != mapping.BendRangeHigh)
            report.Issues.Add(new SequenceConversionIssue("UNMAPPED_CC", $"CC {value.Data1} is not representable as an MP2K command.", trackIndex, value.Tick));
    }

    private static void WriteWait(StringBuilder builder, int ticks)
    {
        while (ticks > 0)
        {
            int wait = LegalDurations.First(value => value <= ticks);
            builder.AppendLine($"\t.byte W{wait:D2}");
            ticks -= wait;
        }
    }

    private static void ParseTrack(
        ParsedSource parsed,
        int start,
        int trackIndex,
        MidiCcMapping mapping,
        SequenceConversionReport report,
        List<MidiPlaybackEvent> output,
        ref int order)
    {
        int position = start;
        int tick = 0;
        int channel = trackIndex % 16;
        int lastNote = 60;
        int lastVelocity = 100;
        int keyShift = 0;
        int bendRange = 2;
        int eventOrder = order;
        int? loopStartTick = null;
        var calls = new Stack<(int Return, int Target, int Remaining)>();
        var activeTies = new HashSet<int>();
        int budget = MaxCommandsPerTrack;

        while (position >= 0 && position < parsed.Statements.Count && budget-- > 0)
        {
            Statement statement = parsed.Statements[position++];
            if (statement.Kind != StatementKind.Bytes || statement.Values.Count == 0)
                continue;
            string command = NormalizeCommand(statement.Values[0]);
            List<string> args = statement.Values.Skip(1).ToList();

            if (TryCommandNumber(command, 'W', out int wait))
            {
                tick += wait;
                continue;
            }
            if (TryCommandNumber(command, 'N', out int duration))
            {
                int rawNote = args.Count > 0 ? ParseValue(args[0], lastNote) : lastNote;
                int velocity = args.Count > 1 ? ParseValue(args[1], lastVelocity) : lastVelocity;
                lastNote = rawNote;
                lastVelocity = velocity;
                int note = rawNote + keyShift;
                output.Add(new MidiPlaybackEvent(tick, eventOrder++, trackIndex, MidiPlaybackEventKind.NoteOn, channel, Math.Clamp(note, 0, 127), Math.Clamp(velocity, 1, 127), 0));
                output.Add(new MidiPlaybackEvent(tick + duration, eventOrder++, trackIndex, MidiPlaybackEventKind.NoteOff, channel, Math.Clamp(note, 0, 127), 0, 0));
                continue;
            }

            switch (command)
            {
                case "FINE":
                    if (CompleteCall(calls, ref position))
                        continue;
                    EndTies(output, activeTies, tick, trackIndex, channel, ref eventOrder);
                    return;
                case "PEND":
                    CompleteCall(calls, ref position);
                    break;
                case "PATT":
                    if (TryReadTarget(parsed, args, ref position, out int pattern))
                    {
                        calls.Push((position, pattern, 1));
                        position = pattern;
                    }
                    break;
                case "REPT":
                {
                    int count = args.Count > 0 ? ParseValue(args[0], 0) : 0;
                    List<string> targetArgs = args.Skip(1).ToList();
                    if (count > 0 && TryReadTarget(parsed, targetArgs, ref position, out int target))
                    {
                        calls.Push((position, target, count));
                        position = target;
                    }
                    break;
                }
                case "GOTO":
                    if (TryReadTarget(parsed, args, ref position, out int targetPosition))
                    {
                        loopStartTick ??= FindApproximateLoopTick(parsed, start, targetPosition);
                        output.Add(new MidiPlaybackEvent(loopStartTick.Value, eventOrder++, trackIndex, MidiPlaybackEventKind.ControlChange, channel, mapping.LoopStart, 127, 0));
                        output.Add(new MidiPlaybackEvent(tick, eventOrder++, trackIndex, MidiPlaybackEventKind.ControlChange, channel, mapping.LoopEnd, 127, 0));
                    }
                    EndTies(output, activeTies, tick, trackIndex, channel, ref eventOrder);
                    return;
                case "TEMPO":
                {
                    int bpm = Math.Clamp(GetArg(args, 0, 75), 1, 255);
                    output.Add(new MidiPlaybackEvent(tick, eventOrder++, trackIndex, MidiPlaybackEventKind.Tempo, channel, 0, 0, 60_000_000 / bpm));
                    break;
                }
                case "VOICE": AddEvent(MidiPlaybackEventKind.ProgramChange, GetArg(args, 0), 0, 0); break;
                case "VOL": AddCc(mapping.Volume, GetArg(args, 0)); break;
                case "PAN": AddCc(mapping.Pan, GetArg(args, 0)); break;
                case "PRIO": AddCc(mapping.Priority, GetArg(args, 0)); break;
                case "LFOS": AddCc(mapping.LfoSpeed, GetArg(args, 0)); break;
                case "LFODL": AddCc(mapping.LfoDelay, GetArg(args, 0)); break;
                case "MOD": AddCc(mapping.Modulation, GetArg(args, 0)); break;
                case "MODT": AddCc(mapping.ModulationType, GetArg(args, 0)); break;
                case "TUNE": AddCc(mapping.Tune, GetArg(args, 0)); break;
                case "BENDR":
                    bendRange = Math.Clamp(GetArg(args, 0, 2), 0, 255);
                    AddCc(mapping.BendRangeLow, bendRange & 0x7F);
                    AddCc(mapping.BendRangeHigh, (bendRange >> 7) & 1);
                    break;
                case "BEND":
                {
                    int raw = Math.Clamp(GetArg(args, 0, 64), 0, 127);
                    int bend14 = Math.Clamp(8192 + (raw - 64) * 128, 0, 16383);
                    AddEvent(MidiPlaybackEventKind.PitchBend, bend14, bendRange, 0);
                    break;
                }
                case "TIE":
                {
                    int rawNote = args.Count > 0 ? ParseValue(args[0], lastNote) : lastNote;
                    int velocity = args.Count > 1 ? ParseValue(args[1], lastVelocity) : lastVelocity;
                    lastNote = rawNote;
                    lastVelocity = velocity;
                    int note = rawNote + keyShift;
                    activeTies.Add(note);
                    AddEvent(MidiPlaybackEventKind.NoteOn, note, velocity, 0);
                    break;
                }
                case "EOT":
                    if (args.Count > 0)
                    {
                        int note = ParseValue(args[0], lastNote) + keyShift;
                        activeTies.Remove(note);
                        AddEvent(MidiPlaybackEventKind.NoteOff, note, 0, 0);
                    }
                    else
                    {
                        EndTies(output, activeTies, tick, trackIndex, channel, ref eventOrder);
                    }
                    break;
                case "XCMD":
                {
                    int subcommand = GetArg(args, 0, -1);
                    if (subcommand == 0x0C)
                    {
                        tick += GetArg(args, 1) | (GetArg(args, 2) << 8);
                        break;
                    }
                    if (subcommand is 0x00 or 0x03)
                    {
                        EndTies(output, activeTies, tick, trackIndex, channel, ref eventOrder);
                        order = eventOrder;
                        return;
                    }
                    int value = GetArg(args, 1);
                    if (mapping.GetControllerForSubcommand(subcommand) is int xcmdController)
                        AddCc(xcmdController, value);
                    else
                        report.Issues.Add(new SequenceConversionIssue("UNMAPPED_XCMD", $"XCMD 0x{subcommand:X2} has no MIDI CC mapping.", trackIndex, tick));
                    break;
                }
                case "MEMACC":
                    report.Issues.Add(new SequenceConversionIssue("MEMACC_PLAYBACK_ONLY", "MEMACC was retained as a diagnostic but is not executed by MIDI playback.", trackIndex, tick));
                    break;
                case "KEYSH":
                    keyShift = ToSignedByte(GetArg(args, 0));
                    break;
                default:
                    if (TryExtendedName(command, mapping, out int extendedController))
                        AddCc(extendedController, GetArg(args, 0));
                    else
                        report.Issues.Add(new SequenceConversionIssue("UNSUPPORTED_COMMAND", $"Unsupported Midi2agb command '{command}'.", trackIndex, tick));
                    break;
            }

            void AddCc(int controller, int value) => AddEvent(MidiPlaybackEventKind.ControlChange, controller, Math.Clamp(value, 0, 127), 0);
            void AddEvent(MidiPlaybackEventKind kind, int data1, int data2, int data3) =>
                output.Add(new MidiPlaybackEvent(tick, eventOrder++, trackIndex, kind, channel, data1, data2, data3));
        }

        EndTies(output, activeTies, tick, trackIndex, channel, ref eventOrder);
        order = eventOrder;
        if (budget <= 0)
            report.Issues.Add(new SequenceConversionIssue("COMMAND_LIMIT", "Track parsing stopped at the safety command limit.", trackIndex, tick));
    }

    private static ParsedSource Tokenize(string source)
    {
        var statements = new List<Statement>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in source.Replace("\r", string.Empty).Split('\n'))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;
            int colon = line.IndexOf(':');
            if (colon >= 0)
            {
                string label = line[..colon].Trim();
                if (label.Length > 0)
                    labels[label] = statements.Count;
                line = line[(colon + 1)..].Trim();
                if (line.Length == 0)
                    continue;
            }
            if (line.StartsWith(".byte", StringComparison.OrdinalIgnoreCase))
                statements.Add(new Statement(StatementKind.Bytes, SplitValues(line[5..])));
            else if (line.StartsWith(".word", StringComparison.OrdinalIgnoreCase) || line.StartsWith(".4byte", StringComparison.OrdinalIgnoreCase))
                statements.Add(new Statement(StatementKind.Word, SplitValues(line[(line[1] == '4' ? 6 : 5)..])));
        }
        return new ParsedSource(statements, labels);
    }

    private static List<(string Label, int Position)> FindTrackStarts(ParsedSource source)
    {
        var referenced = source.Statements.Where(value => value.Kind == StatementKind.Word).SelectMany(value => value.Values).Select(CleanSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<(string Label, int Position)> tracks = source.Labels
            .Where(value => referenced.Contains(value.Key) && value.Key.Contains("track", StringComparison.OrdinalIgnoreCase) && IsTrackLabel(value.Key, source, value.Value))
            .OrderBy(value => value.Value)
            .Select(value => (value.Key, value.Value))
            .ToList();
        if (tracks.Count > 0)
            return tracks;
        return source.Labels.Where(value => referenced.Contains(value.Key) && TrackLabelRegex().IsMatch(value.Key) && IsTrackLabel(value.Key, source, value.Value)).OrderBy(value => value.Value).Select(value => (value.Key, value.Value)).ToList();
    }

    private static bool IsTrackLabel(string label, ParsedSource source, int position)
    {
        if (label.Contains("track", StringComparison.OrdinalIgnoreCase) || TrackLabelRegex().IsMatch(label))
            return position < source.Statements.Count && source.Statements[position].Kind == StatementKind.Bytes;
        return false;
    }

    private static bool TryReadTarget(ParsedSource source, IReadOnlyList<string> args, ref int position, out int target)
    {
        target = -1;
        string? label = args.Count > 0 ? CleanSymbol(args[^1]) : null;
        if (label is null && position < source.Statements.Count && source.Statements[position].Kind == StatementKind.Word)
        {
            label = source.Statements[position].Values.Select(CleanSymbol).FirstOrDefault();
            position++;
        }
        return label is not null && source.Labels.TryGetValue(label, out target);
    }

    private static bool CompleteCall(Stack<(int Return, int Target, int Remaining)> calls, ref int position)
    {
        if (calls.Count == 0)
            return false;
        var frame = calls.Pop();
        if (frame.Remaining > 1)
        {
            calls.Push((frame.Return, frame.Target, frame.Remaining - 1));
            position = frame.Target;
        }
        else
        {
            position = frame.Return;
        }
        return true;
    }

    private static int FindApproximateLoopTick(ParsedSource source, int trackStart, int target)
    {
        int tick = 0;
        for (int index = trackStart; index < target && index < source.Statements.Count; index++)
        {
            Statement statement = source.Statements[index];
            if (statement.Kind == StatementKind.Bytes && statement.Values.Count > 0 && TryCommandNumber(NormalizeCommand(statement.Values[0]), 'W', out int wait))
                tick += wait;
        }
        return tick;
    }

    private static void EndTies(List<MidiPlaybackEvent> output, HashSet<int> ties, int tick, int track, int channel, ref int order)
    {
        foreach (int note in ties)
            output.Add(new MidiPlaybackEvent(tick, order++, track, MidiPlaybackEventKind.NoteOff, channel, note, 0, 0));
        ties.Clear();
    }

    private static bool TryExtendedName(string command, MidiCcMapping mapping, out int controller)
    {
        MidiCcEntry? entry = mapping.GetExtendedEntries().FirstOrDefault(value => string.Equals(value.Name, command, StringComparison.OrdinalIgnoreCase));
        controller = entry?.Controller ?? -1;
        return entry is not null;
    }

    private static int GetArg(IReadOnlyList<string> args, int index, int fallback = 0) => index < args.Count ? ParseValue(args[index], fallback) : fallback;

    private static int ParseValue(string token, int fallback)
    {
        string value = CleanSymbol(token);
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
            return hex;
        if (value.StartsWith('$') && int.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex))
            return hex;
        if (value.Length > 1 && (value[0] is 'v' or 'V') && int.TryParse(value[1..], out int velocity))
            return velocity;
        if (TryParseNoteName(value, out int note))
            return note;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : value.ToLowerInvariant() switch
        {
            "mod_vib" => 0,
            "mod_tre" => 1,
            "mod_pan" => 2,
            "mxv" => 127,
            _ => fallback
        };
    }

    private static bool TryParseNoteName(string value, out int note)
    {
        note = 0;
        Match match = NoteRegex().Match(value);
        if (!match.Success)
            return false;
        int pitch = match.Groups[1].Value.ToUpperInvariant() switch { "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, "B" => 11, _ => 0 };
        if (match.Groups[2].Value is "s" or "S" or "#")
            pitch++;
        if (!int.TryParse(match.Groups[3].Value, out int octave))
            return false;
        note = Math.Clamp((octave + 2) * 12 + pitch, 0, 127);
        return true;
    }

    private static int EventWritePriority(MidiPlaybackEventKind kind) => kind switch
    {
        MidiPlaybackEventKind.Tempo => -10,
        MidiPlaybackEventKind.ProgramChange or MidiPlaybackEventKind.ControlChange or MidiPlaybackEventKind.PitchBend => -5,
        MidiPlaybackEventKind.NoteOff => 0,
        MidiPlaybackEventKind.NoteOn => 1,
        _ => 0
    };

    private static int ToSignedByte(int value)
    {
        int safe = value & 0x7F;
        return safe >= 64 ? safe - 128 : safe;
    }

    private static bool TryCommandNumber(string command, char prefix, out int value)
    {
        value = 0;
        return command.Length > 1 && command[0] == prefix && int.TryParse(command[1..], out value);
    }

    private static string NormalizeCommand(string value) => CleanSymbol(value).ToUpperInvariant();
    private static string CleanSymbol(string value) => value.Trim().TrimEnd(',').Trim();
    private static List<string> SplitValues(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    private static string StripComment(string line)
    {
        int at = line.IndexOf('@');
        int slash = line.IndexOf("//", StringComparison.Ordinal);
        int semicolon = line.IndexOf(';');
        int cut = new[] { at, slash, semicolon }.Where(value => value >= 0).DefaultIfEmpty(line.Length).Min();
        return line[..cut];
    }
    private static string SanitizeLabel(string value)
    {
        string result = Regex.Replace(value, "[^A-Za-z0-9_]", "_");
        return string.IsNullOrWhiteSpace(result) || char.IsDigit(result[0]) ? $"song_{result}" : result;
    }

    private enum StatementKind { Bytes, Word }
    private sealed record Statement(StatementKind Kind, List<string> Values);
    private sealed record ParsedSource(List<Statement> Statements, Dictionary<string, int> Labels);
    private readonly record struct NoteEnd(int Tick, int Order);

    [GeneratedRegex(@"(?:_track_?\d+|_\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackLabelRegex();
    [GeneratedRegex(@"^([A-Ga-g])([ns#]?)(-?\d+)$")]
    private static partial Regex NoteRegex();
}
