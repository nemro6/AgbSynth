namespace AgbSynth.App.Project;

public enum Mp2kRomReadMode
{
    ManualSongTableAddress,
    AutomaticDiscovery
}

public sealed class Mp2kImportOptions
{
    public Mp2kRomReadMode ReadMode { get; set; } = Mp2kRomReadMode.ManualSongTableAddress;
    public int? SongTableOffset { get; set; }
    public string SongTableAddressText { get; set; } = string.Empty;
    public bool IncludeUnreferencedVoiceGroups { get; set; }
}
