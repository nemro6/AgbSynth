using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgbSynth.App.Project;

public sealed class AgbSynthProjectFile
{
    public string Format { get; set; } = AgbSynthFormatContracts.ProjectFormat;
    public int Version { get; set; } = AgbSynthFormatContracts.ProjectVersion;
    public string Engine { get; set; } = AgbSynthFormatContracts.Engine;
    public ImportProjectInfo Import { get; set; } = new();
    public RomProjectInfo Rom { get; set; } = new();
    public Mp2kSoundModeProjectInfo SoundMode { get; set; } = new();
    public SongTableProjectInfo SongTable { get; set; } = new();

    [JsonIgnore]
    public List<SongTableEntryProjectInfo> Songs { get; set; } = new();

    [JsonIgnore]
    public List<SongHeaderProjectInfo> SongHeaders { get; set; } = new();

    [JsonIgnore]
    public List<VoiceGroupProjectInfo> VoiceGroups { get; set; } = new();

    [JsonIgnore]
    public List<KeySplitAssetProjectInfo> KeySplits { get; set; } = new();

    [JsonIgnore]
    public List<DrumSetAssetProjectInfo> DrumSets { get; set; } = new();

    [JsonIgnore]
    public List<WaveDataProjectInfo> WaveData { get; set; } = new();

    [JsonIgnore]
    public List<WaveMemoryProjectInfo> WaveMemory { get; set; } = new();

    [JsonIgnore]
    public bool IsReadOnly { get; set; }

    [JsonIgnore]
    public List<ProjectDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class Mp2kSoundModeProjectInfo
{
    [JsonIgnore]
    public string Address { get; set; } = string.Empty;
    [JsonIgnore]
    public int? Offset { get; set; }
    [JsonIgnore]
    public string RawValue { get; set; } = string.Empty;
    public int Reverb { get; set; }
    public int MaxChannels { get; set; } = 12;
    public int Volume { get; set; } = 15;
    public int FrequencyIndex { get; set; } = 4;
    public int DacConfig { get; set; } = 9;
    public int FixedSampleRate { get; set; } = 13379;
    [JsonIgnore]
    public bool Detected { get; set; }
}

public sealed class ImportProjectInfo
{
    public string ReadMode { get; set; } = nameof(Mp2kRomReadMode.ManualSongTableAddress);
    public bool IncludeUnreferencedVoiceGroups { get; set; }
    public SequenceExportMode SequenceExportMode { get; set; } = SequenceExportMode.Midi;
}

public sealed class RomProjectInfo
{
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;
    public string GameTitle { get; set; } = string.Empty;
    public string GameCode { get; set; } = string.Empty;
    public string Crc32 { get; set; } = "00000000";
    public int SizeBytes { get; set; }
}

public sealed class SongTableProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    [JsonIgnore]
    public string Address { get; set; } = string.Empty;
    [JsonIgnore]
    public int Offset { get; set; }
    public int EntrySize { get; set; }
    public int ValidEntryCount { get; set; }
}

public sealed class SongTableEntryProjectInfo
{
    public int SongId { get; set; }
    public string Label { get; set; } = string.Empty;
    [JsonIgnore]
    public int TableOffset { get; set; }
    [JsonIgnore]
    public string HeaderPointer { get; set; } = string.Empty;
    [JsonIgnore]
    public int HeaderOffset { get; set; }
    public string SongHeaderFilePath { get; set; } = string.Empty;
    public string SongHeaderAssetId { get; set; } = string.Empty;
    public int Group1 { get; set; }
    public int Group2 { get; set; }
    public string Note { get; set; } = string.Empty;
    public string RawEntryHex { get; set; } = string.Empty;
}

public sealed class SongHeaderProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int SongId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    [JsonIgnore]
    public int HeaderOffset { get; set; }
    public int TrackCount { get; set; }
    public int BlockCount { get; set; }
    public int Priority { get; set; }
    public int Reverb { get; set; }
    [JsonIgnore]
    public string VoiceGroupPointer { get; set; } = string.Empty;
    [JsonIgnore]
    public int VoiceGroupOffset { get; set; }
    public int? VoiceGroupId { get; set; }
    public string VoiceGroupFilePath { get; set; } = string.Empty;
    public string VoiceGroupAssetId { get; set; } = string.Empty;
    [JsonIgnore]
    public List<string> TrackPointers { get; set; } = new();
    [JsonIgnore]
    public List<int> TrackOffsets { get; set; } = new();
    public string MidiFilePath { get; set; } = string.Empty;
    public string Midi2AgbFilePath { get; set; } = string.Empty;
    public SequenceAssetFormat SequenceFormat { get; set; } = SequenceAssetFormat.Midi;
    public string SequenceFilePath { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    [JsonIgnore]
    public string RawHeaderHex { get; set; } = string.Empty;
}

public sealed class VoiceGroupProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    [JsonIgnore]
    public string Pointer { get; set; } = string.Empty;
    [JsonIgnore]
    public int Offset { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DiscoverySource { get; set; } = "Referenced";
    public List<int> UsedBySongIds { get; set; } = new();
    public List<VoiceProjectInfo> Voices { get; set; } = new();
}

public sealed class VoiceProjectInfo
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Key { get; set; }
    public int Length { get; set; }
    public int PanOrSweep { get; set; }
    [JsonIgnore]
    public string DataPointer { get; set; } = string.Empty;
    [JsonIgnore]
    public int? DataOffset { get; set; }
    public string DataFilePath { get; set; } = string.Empty;
    public string DataAssetId { get; set; } = string.Empty;
    public int Attack { get; set; }
    public int Decay { get; set; }
    public int Sustain { get; set; }
    public int Release { get; set; }
    public SampleHeaderProjectInfo? Sample { get; set; }
    public PsgSquareProjectInfo? PsgSquare { get; set; }
    public PsgWaveMemoryProjectInfo? PsgWaveMemory { get; set; }
    public PsgNoiseProjectInfo? PsgNoise { get; set; }
    public DrumSetProjectInfo? DrumSet { get; set; }
    public KeySplitProjectInfo? KeySplit { get; set; }
    [JsonIgnore]
    public string RawEntryHex { get; set; } = string.Empty;
}

public sealed class PsgSquareProjectInfo
{
    public int DutyIndex { get; set; }
    public double DutyRatio { get; set; }
}

public sealed class PsgNoiseProjectInfo
{
    public int Control { get; set; }
    public int ClockDivider { get; set; }
    public int PrescalerShift { get; set; }
    public bool ShortLfsr { get; set; }
    public bool PinkNoise { get; set; }
}

public sealed class PsgWaveMemoryProjectInfo
{
    [JsonIgnore]
    public int DataOffset { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DataFormat { get; set; } = "Mp2kPcm4WaveRam";
}

public sealed class SampleHeaderProjectInfo
{
    [JsonIgnore]
    public int HeaderOffset { get; set; }
    public int LoopFlags { get; set; }
    public bool Loops { get; set; }
    public uint Frequency { get; set; }
    public uint LoopStart { get; set; }
    public uint Size { get; set; }
    [JsonIgnore]
    public int DataOffset { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public sealed class WaveDataProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DataFormat { get; set; } = "Signed8MonoPcm";
    [JsonIgnore]
    public int HeaderOffset { get; set; }
    public int LoopFlags { get; set; }
    public bool Loops { get; set; }
    public uint Frequency { get; set; }
    public uint LoopStart { get; set; }
    public uint Size { get; set; }
    [JsonIgnore]
    public int DataOffset { get; set; }
}

public sealed class WaveMemoryProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DataFormat { get; set; } = "Mp2kPcm4WaveRam";
    [JsonIgnore]
    public int DataOffset { get; set; }
    public int Size { get; set; } = 16;
}

public sealed class KeySplitAssetProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int VoiceGroupId { get; set; }
    public int ParentVoiceIndex { get; set; }
    public KeySplitProjectInfo KeySplit { get; set; } = new();
}

public sealed class DrumSetAssetProjectInfo
{
    public string AssetId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int VoiceGroupId { get; set; }
    public int ParentVoiceIndex { get; set; }
    public DrumSetProjectInfo DrumSet { get; set; } = new();
}

public sealed class DrumSetProjectInfo
{
    public string Label { get; set; } = string.Empty;
    [JsonIgnore]
    public int TableOffset { get; set; }
    public List<VoiceProjectInfo> Entries { get; set; } = new();
    [JsonIgnore]
    public string RawHex { get; set; } = string.Empty;
}

public sealed class KeySplitProjectInfo
{
    public string Label { get; set; } = string.Empty;
    [JsonIgnore]
    public int RegionTableOffset { get; set; }
    [JsonIgnore]
    public int? KeyMapOffset { get; set; }
    public List<VoiceProjectInfo> Regions { get; set; } = new();
    public string KeyMapHex { get; set; } = string.Empty;
    [JsonIgnore]
    public string RawRegionTableHex { get; set; } = string.Empty;
}
