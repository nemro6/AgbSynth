namespace AgbSynth.App.Project;

public sealed class AgbSynthProjectManifest
{
    public string Format { get; set; } = "AgbSynthProject";
    public int Version { get; set; } = 1;
    public string Engine { get; set; } = "MP2K";
    public string RomCrc32 { get; set; } = "00000000";
    public string? SongTableAddress { get; set; }
    public string OutputMode { get; set; } = "RelocateToAddress";
}

