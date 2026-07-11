using System;
using System.Collections.Generic;
using System.Linq;

namespace AgbSynth.App.MP2K;

public sealed class MidiCcMapping
{
    public int Modulation { get; set; } = 1;
    public int Volume { get; set; } = 7;
    public int Pan { get; set; } = 10;
    public int Tune { get; set; } = 13;
    public int Priority { get; set; } = 14;
    public int BendRangeLow { get; set; } = 20;
    public int BendRangeHigh { get; set; } = 52;
    public int LfoSpeed { get; set; } = 21;
    public int ModulationType { get; set; } = 22;
    public int LfoDelay { get; set; } = 26;
    public int LoopStart { get; set; } = 89;
    public int LoopEnd { get; set; } = 90;

    public int Type { get; set; } = 80;
    public int Attack { get; set; } = 85;
    public int Decay { get; set; } = 86;
    public int Sustain { get; set; } = 87;
    public int Release { get; set; } = 88;
    public int EchoVolume { get; set; } = 100;
    public int EchoLength { get; set; } = 101;
    public int Length { get; set; } = 102;
    public int Sweep { get; set; } = 28;

    public static MidiCcMapping Default => new();

    public MidiCcMapping Clone() => new()
    {
        Modulation = Modulation,
        Volume = Volume,
        Pan = Pan,
        Tune = Tune,
        Priority = Priority,
        BendRangeLow = BendRangeLow,
        BendRangeHigh = BendRangeHigh,
        LfoSpeed = LfoSpeed,
        ModulationType = ModulationType,
        LfoDelay = LfoDelay,
        LoopStart = LoopStart,
        LoopEnd = LoopEnd,
        Type = Type,
        Attack = Attack,
        Decay = Decay,
        Sustain = Sustain,
        Release = Release,
        EchoVolume = EchoVolume,
        EchoLength = EchoLength,
        Length = Length,
        Sweep = Sweep
    };

    public void Normalize()
    {
        foreach (MidiCcEntry entry in GetEntries().ToArray())
            SetController(entry.Key, Math.Clamp(entry.Controller, 0, 127));
    }

    public bool TryValidate(out string? error)
    {
        MidiCcEntry[] entries = GetEntries().ToArray();
        MidiCcEntry? invalid = entries.FirstOrDefault(entry => entry.Controller is < 0 or > 127);
        if (invalid is not null)
        {
            error = $"{invalid.Name} must be between 0 and 127.";
            return false;
        }

        IGrouping<int, MidiCcEntry>? duplicate = entries
            .GroupBy(entry => entry.Controller)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            error = $"CC {duplicate.Key} is assigned to {string.Join(" and ", duplicate.Select(entry => entry.Name))}.";
            return false;
        }

        error = null;
        return true;
    }

    public IEnumerable<MidiCcEntry> GetStandardEntries()
    {
        yield return new("modulation", "MOD", Modulation, "Modulation depth", false);
        yield return new("volume", "VOL", Volume, "Track volume", false);
        yield return new("pan", "PAN", Pan, "Track pan", false);
        yield return new("tune", "TUNE", Tune, "Fine tuning", false);
        yield return new("priority", "PRIO", Priority, "Track priority", false);
        yield return new("bendRangeLow", "BENDR Low", BendRangeLow, "Bend range bits 0-6", false);
        yield return new("bendRangeHigh", "BENDR High", BendRangeHigh, "Bend range bit 7", false);
        yield return new("lfoSpeed", "LFOS", LfoSpeed, "LFO speed", false);
        yield return new("modulationType", "MODT", ModulationType, "LFO target", false);
        yield return new("lfoDelay", "LFODL", LfoDelay, "LFO delay", false);
        yield return new("loopStart", "Loop Start", LoopStart, "Per-track loop start", false);
        yield return new("loopEnd", "Loop End", LoopEnd, "Per-track loop end", false);
    }

    public IEnumerable<MidiCcEntry> GetExtendedEntries()
    {
        yield return new("type", "xTYPE", Type, "Tone type", true, 0x02);
        yield return new("attack", "xATTA", Attack, "Attack", true, 0x04);
        yield return new("decay", "xDECA", Decay, "Decay", true, 0x05);
        yield return new("sustain", "xSUST", Sustain, "Sustain", true, 0x06);
        yield return new("release", "xRELE", Release, "Release", true, 0x07);
        yield return new("echoVolume", "xIECV", EchoVolume, "Pseudo-echo volume", true, 0x08);
        yield return new("echoLength", "xIECL", EchoLength, "Pseudo-echo length", true, 0x09);
        yield return new("length", "xLENG", Length, "PSG length", true, 0x0A);
        yield return new("sweep", "xSWEE", Sweep, "Square 1 sweep", true, 0x0B);
    }

    public IEnumerable<MidiCcEntry> GetEntries() => GetStandardEntries().Concat(GetExtendedEntries());

    public int? GetControllerForSubcommand(int subcommand) =>
        GetExtendedEntries().FirstOrDefault(entry => entry.Subcommand == subcommand)?.Controller;

    public void SetController(string key, int controller)
    {
        switch (key)
        {
            case "modulation": Modulation = controller; break;
            case "volume": Volume = controller; break;
            case "pan": Pan = controller; break;
            case "tune": Tune = controller; break;
            case "priority": Priority = controller; break;
            case "bendRangeLow": BendRangeLow = controller; break;
            case "bendRangeHigh": BendRangeHigh = controller; break;
            case "lfoSpeed": LfoSpeed = controller; break;
            case "modulationType": ModulationType = controller; break;
            case "lfoDelay": LfoDelay = controller; break;
            case "loopStart": LoopStart = controller; break;
            case "loopEnd": LoopEnd = controller; break;
            case "type": Type = controller; break;
            case "attack": Attack = controller; break;
            case "decay": Decay = controller; break;
            case "sustain": Sustain = controller; break;
            case "release": Release = controller; break;
            case "echoVolume": EchoVolume = controller; break;
            case "echoLength": EchoLength = controller; break;
            case "length": Length = controller; break;
            case "sweep": Sweep = controller; break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown MIDI CC mapping key.");
        }
    }
}

public sealed record MidiCcEntry(
    string Key,
    string Name,
    int Controller,
    string Description,
    bool IsExtended,
    int? Subcommand = null);
