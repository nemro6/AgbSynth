using System;
using System.IO;
using AgbSynth.App.MIDI;
using AgbSynth.App.Project;

namespace AgbSynth.App.MP2K.Sequence;

public static class SequenceFileService
{
    public static MidiPlaybackFile Load(string path, SequenceAssetFormat format, MidiCcMapping? mapping = null)
    {
        return format switch
        {
            SequenceAssetFormat.Midi => MidiFileReader.Read(path),
            SequenceAssetFormat.Midi2Agb => Midi2AgbSequenceCodec.Parse(File.ReadAllText(path), mapping),
            _ => throw new InvalidDataException($"Unsupported sequence format '{format}'.")
        };
    }

    public static SequenceAssetFormat DetectFormat(string path)
    {
        return Path.GetExtension(path).Equals(".s", StringComparison.OrdinalIgnoreCase)
            ? SequenceAssetFormat.Midi2Agb
            : SequenceAssetFormat.Midi;
    }
}

public sealed record SequenceConversionIssue(string Code, string Message, int TrackIndex = -1, int Tick = -1);

public sealed class SequenceConversionReport
{
    public System.Collections.Generic.List<SequenceConversionIssue> Issues { get; } = new();
    public bool HasLoss => Issues.Count > 0;
}
