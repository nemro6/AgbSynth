namespace AgbSynth.App.Project;

public enum Mp2kRomReadMode
{
    ManualSongTableAddress,
    AutomaticDiscovery
}

public enum SequenceExportMode
{
    Midi,
    Midi2Agb,
    Both
}

public sealed class Mp2kImportOptions
{
    public Mp2kRomReadMode ReadMode { get; set; } = Mp2kRomReadMode.ManualSongTableAddress;
    public int? SongTableOffset { get; set; }
    public string SongTableAddressText { get; set; } = string.Empty;
    public bool IncludeUnreferencedVoiceGroups { get; set; }
    public SequenceExportMode SequenceExportMode { get; set; } = SequenceExportMode.Midi;
}
