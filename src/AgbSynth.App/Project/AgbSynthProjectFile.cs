using System.Collections.Generic;

namespace AgbSynth.App.Project;

public sealed class AgbSynthProjectFile
{
    public string Format { get; set; } = "AgbSynthProject";
    public int Version { get; set; } = 1;
    public string Engine { get; set; } = "MP2K";
    public RomProjectInfo Rom { get; set; } = new();
    public SongTableProjectInfo SongTable { get; set; } = new();
    public List<SongTableEntryProjectInfo> Songs { get; set; } = new();
}

public sealed class RomProjectInfo
{
    public string SourcePath { get; set; } = string.Empty;
    public string GameTitle { get; set; } = string.Empty;
    public string GameCode { get; set; } = string.Empty;
    public string Crc32 { get; set; } = "00000000";
    public int SizeBytes { get; set; }
}

public sealed class SongTableProjectInfo
{
    public string Address { get; set; } = string.Empty;
    public int Offset { get; set; }
    public int EntrySize { get; set; }
    public int ValidEntryCount { get; set; }
}

public sealed class SongTableEntryProjectInfo
{
    public int SongId { get; set; }
    public int TableOffset { get; set; }
    public string HeaderPointer { get; set; } = string.Empty;
    public int HeaderOffset { get; set; }
    public string RawEntryHex { get; set; } = string.Empty;
}

