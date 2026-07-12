using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgbSynth.App.GBA;
using AgbSynth.App.Project;

namespace AgbSynth.App.MP2K.Sequence;

public static class Mp2kSequenceAssemblyExporter
{
    private const int MaxCommands = 100_000;
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

    public static string Disassemble(GbaRom rom, SongHeaderProjectInfo header, string songName, SequenceConversionReport? report = null)
    {
        report ??= new SequenceConversionReport();
        string safeName = Sanitize(songName);
        var blocks = new Dictionary<int, Block>();
        var pending = new Queue<(int Offset, bool StopAtPend)>(header.TrackOffsets
            .Where(value => value >= 0 && value < rom.Length)
            .Select(value => (value, false)));
        while (pending.Count > 0)
        {
            (int offset, bool stopAtPend) = pending.Dequeue();
            if (blocks.ContainsKey(offset))
                continue;
            Block block = ParseBlock(rom, offset, stopAtPend, report);
            blocks[offset] = block;
            foreach (int target in block.Targets.Where(value => value >= 0 && value < rom.Length && !blocks.ContainsKey(value)))
                pending.Enqueue((target, true));
        }

        var builder = new StringBuilder();
        builder.AppendLine("\t.include \"MPlayDef.s\"");
        builder.AppendLine("\t.section .rodata");
        builder.AppendLine($"\t.global {safeName}");
        builder.AppendLine();
        builder.AppendLine($"{safeName}:");
        builder.AppendLine($"\t.byte {header.TrackOffsets.Count}, {header.BlockCount}, {header.Priority}, {header.Reverb}");
        builder.AppendLine("\t.word 0");
        for (int index = 0; index < header.TrackOffsets.Count; index++)
            builder.AppendLine($"\t.word {safeName}_track_{index + 1:D2}");

        var aliases = header.TrackOffsets.Select((offset, index) => (offset, label: $"{safeName}_track_{index + 1:D2}"))
            .GroupBy(value => value.offset)
            .ToDictionary(group => group.Key, group => group.Select(value => value.label).ToList());
        var allTargets = blocks.Values.SelectMany(value => value.Targets).ToHashSet();
        foreach (Block block in blocks.Values.OrderBy(value => value.Offset))
        {
            bool isNestedDuplicate = blocks.Values.Any(owner =>
                owner.Offset < block.Offset && owner.EndOffset > block.Offset && owner.Commands.Any(command => command.Offset == block.Offset));
            if (isNestedDuplicate)
                continue;

            builder.AppendLine();
            if (aliases.TryGetValue(block.Offset, out List<string>? trackAliases))
                foreach (string alias in trackAliases)
                    builder.AppendLine($"{alias}:");
            builder.AppendLine($"{Label(safeName, block.Offset)}:");
            foreach (Command command in block.Commands)
            {
                if (command.Offset != block.Offset && aliases.TryGetValue(command.Offset, out List<string>? nestedTrackAliases))
                    foreach (string alias in nestedTrackAliases)
                        builder.AppendLine($"{alias}:");
                if (command.Offset != block.Offset && allTargets.Contains(command.Offset))
                    builder.AppendLine($"{Label(safeName, command.Offset)}:");
                if (command.TargetOffset is int target)
                {
                    builder.AppendLine($"\t.byte {command.Text}");
                    builder.AppendLine($"\t.word {Label(safeName, target)}");
                }
                else
                {
                    builder.AppendLine($"\t.byte {command.Text}");
                }
            }
        }
        return builder.ToString();
    }

    private static Block ParseBlock(GbaRom rom, int start, bool stopAtPend, SequenceConversionReport report)
    {
        ReadOnlyMemory<byte> data = rom.Bytes;
        var commands = new List<Command>();
        var targets = new List<int>();
        int position = start;
        int runningCommand = 0;
        int budget = MaxCommands;
        while (position >= 0 && position < data.Length && budget-- > 0)
        {
            int commandOffset = position;
            int raw = data.Span[position++];
            int? firstParameter = null;
            int command = raw;
            if (raw <= 0x7F)
            {
                if (runningCommand < 0xBD)
                {
                    report.Issues.Add(new SequenceConversionIssue("INVALID_RUNNING_STATUS", $"Data byte 0x{raw:X2} has no running command at ROM offset 0x{commandOffset:X}."));
                    break;
                }
                command = runningCommand;
                firstParameter = raw;
            }
            else if (raw >= 0xBD)
            {
                runningCommand = raw;
            }

            if (command is >= 0x80 and <= 0xB0)
            {
                commands.Add(new Command(commandOffset, $"W{WaitTicks[command - 0x80]:D2}"));
                continue;
            }
            if (command >= 0xD0)
            {
                List<int> values = ReadOptional(data.Span, ref position, firstParameter, 3);
                commands.Add(new Command(commandOffset, Join($"N{NoteTicks[command - 0xD0]:D2}", values)));
                continue;
            }

            switch (command)
            {
                case 0xB1:
                    commands.Add(new Command(commandOffset, "FINE"));
                    return new Block(start, position, commands, targets);
                case 0xB2:
                case 0xB3:
                {
                    if (!TryReadPointer(rom, ref position, out int target))
                        return Invalid("POINTER_OUT_OF_RANGE");
                    targets.Add(target);
                    commands.Add(new Command(commandOffset, command == 0xB2 ? "GOTO" : "PATT", target));
                    if (command == 0xB2)
                        return new Block(start, position, commands, targets);
                    break;
                }
                case 0xB4:
                    commands.Add(new Command(commandOffset, "PEND"));
                    if (stopAtPend)
                        return new Block(start, position, commands, targets);
                    break;
                case 0xB5:
                {
                    if (!TryReadRequired(data.Span, ref position, firstParameter, out int count) || !TryReadPointer(rom, ref position, out int target))
                        return Invalid("REPT_TRUNCATED");
                    targets.Add(target);
                    commands.Add(new Command(commandOffset, $"REPT, {count}", target));
                    break;
                }
                case 0xB9:
                {
                    if (!TryReadByte(data.Span, ref position, out int operation))
                        return Invalid("MEMACC_TRUNCATED");
                    int argumentCount = operation is >= 6 and <= 17 ? 6 : 2;
                    List<int> values = [operation];
                    for (int i = 0; i < argumentCount && TryReadByte(data.Span, ref position, out int value); i++)
                        values.Add(value);
                    commands.Add(new Command(commandOffset, Join("MEMACC", values)));
                    break;
                }
                case 0xBA: AddOne("PRIO"); break;
                case 0xBB: AddOne("TEMPO"); break;
                case 0xBC: AddOne("KEYSH"); break;
                case 0xBD: AddOne("VOICE"); break;
                case 0xBE: AddOne("VOL"); break;
                case 0xBF: AddOne("PAN"); break;
                case 0xC0: AddOne("BEND"); break;
                case 0xC1: AddOne("BENDR"); break;
                case 0xC2: AddOne("LFOS"); break;
                case 0xC3: AddOne("LFODL"); break;
                case 0xC4: AddOne("MOD"); break;
                case 0xC5: AddOne("MODT"); break;
                case 0xC8: AddOne("TUNE"); break;
                case 0xCD:
                {
                    if (!TryReadRequired(data.Span, ref position, firstParameter, out int subcommand))
                        return Invalid("XCMD_TRUNCATED");
                    int length = subcommand switch { 0x00 or 0x03 => 0, 0x01 or 0x0D => 4, 0x0C => 2, >= 0x02 and <= 0x0B => 1, _ => -1 };
                    if (length < 0)
                    {
                        report.Issues.Add(new SequenceConversionIssue("UNKNOWN_XCMD", $"Unknown XCMD 0x{subcommand:X2} at ROM offset 0x{commandOffset:X}."));
                        return new Block(start, position, commands, targets);
                    }
                    List<int> values = [subcommand];
                    for (int i = 0; i < length && TryReadByte(data.Span, ref position, out int value); i++)
                        values.Add(value);
                    commands.Add(new Command(commandOffset, Join("XCMD", values)));
                    if (subcommand is 0x00 or 0x03)
                        return new Block(start, position, commands, targets);
                    break;
                }
                case 0xCE:
                    commands.Add(new Command(commandOffset, Join("EOT", ReadOptional(data.Span, ref position, firstParameter, 1))));
                    break;
                case 0xCF:
                    commands.Add(new Command(commandOffset, Join("TIE", ReadOptional(data.Span, ref position, firstParameter, 2))));
                    break;
                default:
                    report.Issues.Add(new SequenceConversionIssue("UNSUPPORTED_MP2K_COMMAND", $"Unsupported MP2K command 0x{command:X2} at ROM offset 0x{commandOffset:X}."));
                    return new Block(start, position, commands, targets);
            }

            void AddOne(string name)
            {
                if (TryReadRequired(data.Span, ref position, firstParameter, out int value))
                    commands.Add(new Command(commandOffset, $"{name}, {value}"));
                else
                    report.Issues.Add(new SequenceConversionIssue("COMMAND_TRUNCATED", $"{name} is truncated at ROM offset 0x{commandOffset:X}."));
            }

            Block Invalid(string code)
            {
                report.Issues.Add(new SequenceConversionIssue(code, $"Track data is truncated at ROM offset 0x{commandOffset:X}."));
                return new Block(start, position, commands, targets);
            }
        }
        if (budget <= 0)
            report.Issues.Add(new SequenceConversionIssue("COMMAND_LIMIT", $"Track at ROM offset 0x{start:X} exceeded the command limit."));
        return new Block(start, position, commands, targets);
    }

    private static List<int> ReadOptional(ReadOnlySpan<byte> bytes, ref int position, int? first, int maximum)
    {
        var result = new List<int>();
        if (first is int value)
            result.Add(value);
        while (result.Count < maximum && position < bytes.Length && bytes[position] <= 0x7F)
            result.Add(bytes[position++]);
        return result;
    }

    private static bool TryReadRequired(ReadOnlySpan<byte> bytes, ref int position, int? first, out int value)
    {
        if (first is int firstValue)
        {
            value = firstValue;
            return true;
        }
        return TryReadByte(bytes, ref position, out value);
    }

    private static bool TryReadByte(ReadOnlySpan<byte> bytes, ref int position, out int value)
    {
        if ((uint)position >= (uint)bytes.Length)
        {
            value = 0;
            return false;
        }
        value = bytes[position++];
        return true;
    }

    private static bool TryReadPointer(GbaRom rom, ref int position, out int offset)
    {
        offset = -1;
        if (!rom.TryReadUInt32LittleEndian(position, out uint pointer))
            return false;
        position += 4;
        return GbaAddress.TryToOffset(pointer, rom.Length, out offset);
    }

    private static string Join(string command, IReadOnlyList<int> values) =>
        values.Count == 0 ? command : $"{command}, {string.Join(", ", values)}";
    private static string Label(string songName, int offset) => $"{songName}_data_{offset:X6}";
    private static string Sanitize(string value)
    {
        string result = new(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        return result.Length == 0 || char.IsDigit(result[0]) ? $"song_{result}" : result;
    }

    private sealed record Command(int Offset, string Text, int? TargetOffset = null);
    private sealed record Block(int Offset, int EndOffset, List<Command> Commands, List<int> Targets);
}
