using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AgbSynth.App.Project;

public static class AgbSynthFormatContracts
{
    public const string Engine = "MP2K";
    public const string ProjectFormat = "AgbSynthProject";
    public const int ProjectVersion = 4;
    public const int AssetVersion = 2;

    public const string SongTableFormat = "AgbSynthSongTable";
    public const string SongHeaderFormat = "AgbSynthSongHeader";
    public const string VoiceGroupFormat = "AgbSynthVoiceGroup";
    public const string KeySplitFormat = "AgbSynthKeySplit";
    public const string DrumSetFormat = "AgbSynthDrumSet";
    public const string WaveDataFormat = "AgbSynthWaveData";
    public const string WaveMemoryMetadataFormat = "AgbSynthWaveMemoryMetadata";

    public static string NewAssetId() => Guid.NewGuid().ToString("N");

    public static string CreateLegacyAssetId(string format, string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{format}\n{path.Replace('\\', '/').ToLowerInvariant()}"));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}

public enum SequenceAssetFormat
{
    Midi,
    Midi2Agb
}

public enum ProjectDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ProjectDiagnostic(
    ProjectDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? FilePath = null);

public abstract class AgbAssetDocument
{
    public string Format { get; set; } = string.Empty;
    public int Version { get; set; } = AgbSynthFormatContracts.AssetVersion;
    public string Engine { get; set; } = AgbSynthFormatContracts.Engine;
    public string AssetId { get; set; } = string.Empty;
}

public sealed class SongTableDocument : AgbAssetDocument
{
    public SongTableDocument() => Format = AgbSynthFormatContracts.SongTableFormat;
    public SongTableProjectInfo SongTable { get; set; } = new();
    public List<SongTableEntryProjectInfo> Entries { get; set; } = new();
}

public sealed class SongHeaderDocument : AgbAssetDocument
{
    public SongHeaderDocument() => Format = AgbSynthFormatContracts.SongHeaderFormat;
    public SongHeaderProjectInfo Header { get; set; } = new();
}

public sealed class VoiceGroupDocument : AgbAssetDocument
{
    public VoiceGroupDocument() => Format = AgbSynthFormatContracts.VoiceGroupFormat;
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    [JsonIgnore]
    public string Pointer { get; set; } = string.Empty;
    [JsonIgnore]
    public int Offset { get; set; }
    public string DiscoverySource { get; set; } = "User";
    public List<int> UsedBySongIds { get; set; } = new();
    public List<VoiceProjectInfo> Voices { get; set; } = new();
}

public sealed class KeySplitDocument : AgbAssetDocument
{
    public KeySplitDocument() => Format = AgbSynthFormatContracts.KeySplitFormat;
    public int VoiceGroupId { get; set; } = -1;
    public int ParentVoiceIndex { get; set; } = -1;
    public KeySplitProjectInfo KeySplit { get; set; } = new();
}

public sealed class DrumSetDocument : AgbAssetDocument
{
    public DrumSetDocument() => Format = AgbSynthFormatContracts.DrumSetFormat;
    public int VoiceGroupId { get; set; } = -1;
    public int ParentVoiceIndex { get; set; } = -1;
    public DrumSetProjectInfo DrumSet { get; set; } = new();
}

public sealed class WaveDataDocument : AgbAssetDocument
{
    public WaveDataDocument() => Format = AgbSynthFormatContracts.WaveDataFormat;
    public SampleHeaderProjectInfo Header { get; set; } = new();
    public string DataFormat { get; set; } = "Signed8MonoPcm";
    public string DataHex { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed class WaveMemoryMetadataDocument : AgbAssetDocument
{
    public WaveMemoryMetadataDocument() => Format = AgbSynthFormatContracts.WaveMemoryMetadataFormat;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string DataFormat { get; set; } = "Mp2kPcm4WaveRam";
    public int Size { get; set; } = 16;
}
