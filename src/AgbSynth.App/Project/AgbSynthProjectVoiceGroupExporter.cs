using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgbSynth.App.GBA;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectVoiceGroupExporter
{
    public static int ExportVoiceGroups(GbaRom rom, AgbSynthProjectFile project, string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string assetRootName = $"{Path.GetFileNameWithoutExtension(projectPath)}_data";
        string voiceGroupDirectory = Path.Combine(projectDirectory, assetRootName, "voicegroup");
        string drumSetDirectory = Path.Combine(projectDirectory, assetRootName, "drumset");
        string keySplitDirectory = Path.Combine(projectDirectory, assetRootName, "keysplit");
        string waveDataDirectory = Path.Combine(projectDirectory, assetRootName, "wavedata");
        string waveMemoryDirectory = Path.Combine(projectDirectory, assetRootName, "wavememory");
        Directory.CreateDirectory(voiceGroupDirectory);
        Directory.CreateDirectory(drumSetDirectory);
        Directory.CreateDirectory(keySplitDirectory);
        Directory.CreateDirectory(waveDataDirectory);
        Directory.CreateDirectory(waveMemoryDirectory);
        project.WaveData.Clear();
        project.WaveMemory.Clear();

        string relativeDrumSetDirectory = $"{assetRootName}/drumset";
        string relativeKeySplitDirectory = $"{assetRootName}/keysplit";
        string relativeWaveDataDirectory = $"{assetRootName}/wavedata";
        string relativeWaveMemoryDirectory = $"{assetRootName}/wavememory";
        var options = new JsonSerializerOptions { WriteIndented = true };
        var context = new ExportContext(
            rom,
            drumSetDirectory,
            keySplitDirectory,
            waveDataDirectory,
            waveMemoryDirectory,
            relativeDrumSetDirectory,
            relativeKeySplitDirectory,
            relativeWaveDataDirectory,
            relativeWaveMemoryDirectory,
            options);

        foreach (var voiceGroup in project.VoiceGroups)
            ExportVoiceSubFiles(context, voiceGroup);
        project.WaveData.AddRange(context.WaveData);
        project.WaveMemory.AddRange(context.WaveMemory);

        int exportedCount = context.ExportedFileCount;
        foreach (var voiceGroup in project.VoiceGroups)
        {
            string fileName = $"voicegroup_{voiceGroup.Id:D3}.agbvg";
            string path = Path.Combine(voiceGroupDirectory, fileName);
            if (string.IsNullOrWhiteSpace(voiceGroup.Label))
                voiceGroup.Label = Path.GetFileNameWithoutExtension(fileName);
            var document = new AgbVoiceGroupDocument
            {
                Pointer = voiceGroup.Pointer,
                Offset = voiceGroup.Offset,
                Id = voiceGroup.Id,
                Label = voiceGroup.Label,
                DiscoverySource = voiceGroup.DiscoverySource,
                UsedBySongIds = voiceGroup.UsedBySongIds,
                Voices = voiceGroup.Voices
            };

            File.WriteAllText(path, JsonSerializer.Serialize(document, options));
            voiceGroup.FilePath = $"{assetRootName}/voicegroup/{fileName}";
            LinkSongHeaders(project, voiceGroup);
            exportedCount++;
        }

        return exportedCount;
    }

    private static void ExportVoiceSubFiles(ExportContext context, VoiceGroupProjectInfo voiceGroup)
    {
        foreach (var voice in voiceGroup.Voices)
            ExportVoiceSubFiles(context, voiceGroup, voice);
    }

    private static void ExportVoiceSubFiles(ExportContext context, VoiceGroupProjectInfo voiceGroup, VoiceProjectInfo voice)
    {
        ExportWaveData(context, voice);
        ExportWaveMemory(context, voice);

        if (voice.DrumSet is not null)
        {
            foreach (var entry in voice.DrumSet.Entries)
                ExportVoiceSubFiles(context, voiceGroup, entry);

            if (voice.DataOffset is not int sourceOffset)
                return;

            if (!context.DrumSetPathsByOffset.TryGetValue(sourceOffset, out string? relativePath))
            {
                string fileName = $"drumset_{context.NextDrumSetId++:D3}.agbds";
                relativePath = $"{context.RelativeDrumSetDirectory}/{fileName}";
                string path = Path.Combine(context.DrumSetDirectory, fileName);
                if (string.IsNullOrWhiteSpace(voice.DrumSet.Label))
                    voice.DrumSet.Label = Path.GetFileNameWithoutExtension(fileName);
                var document = new AgbDrumSetDocument
                {
                    VoiceGroupId = voiceGroup.Id,
                    VoiceGroupPointer = voiceGroup.Pointer,
                    ParentVoiceIndex = voice.Index,
                    SourcePointer = voice.DataPointer,
                    SourceOffset = voice.DataOffset,
                    DrumSet = voice.DrumSet
                };
                File.WriteAllText(path, JsonSerializer.Serialize(document, context.Options));
                context.DrumSetPathsByOffset.Add(sourceOffset, relativePath);
                context.ExportedFileCount++;
            }

            voice.DataFilePath = relativePath;
            return;
        }

        if (voice.KeySplit is not null)
        {
            foreach (var region in voice.KeySplit.Regions)
                ExportVoiceSubFiles(context, voiceGroup, region);

            if (voice.DataOffset is not int sourceOffset)
                return;

            if (!context.KeySplitPathsByOffset.TryGetValue(sourceOffset, out string? relativePath))
            {
                string fileName = $"keysplit_{context.NextKeySplitId++:D3}.agbks";
                relativePath = $"{context.RelativeKeySplitDirectory}/{fileName}";
                string path = Path.Combine(context.KeySplitDirectory, fileName);
                if (string.IsNullOrWhiteSpace(voice.KeySplit.Label))
                    voice.KeySplit.Label = Path.GetFileNameWithoutExtension(fileName);
                var document = new AgbKeySplitDocument
                {
                    VoiceGroupId = voiceGroup.Id,
                    VoiceGroupPointer = voiceGroup.Pointer,
                    ParentVoiceIndex = voice.Index,
                    SourcePointer = voice.DataPointer,
                    SourceOffset = voice.DataOffset,
                    KeySplit = voice.KeySplit
                };
                File.WriteAllText(path, JsonSerializer.Serialize(document, context.Options));
                context.KeySplitPathsByOffset.Add(sourceOffset, relativePath);
                context.ExportedFileCount++;
            }

            voice.DataFilePath = relativePath;
        }
    }

    private static void ExportWaveData(ExportContext context, VoiceProjectInfo voice)
    {
        if (voice.Sample is not { } sample)
            return;
        if (sample.Size > int.MaxValue)
            return;
        if (sample.DataOffset < 0 || sample.DataOffset + (long)sample.Size > context.Rom.Length)
            return;

        if (!context.WaveDataPathsByOffset.TryGetValue(sample.HeaderOffset, out string? relativePath))
        {
            string fileName = $"wavedata_{context.NextWaveDataId++:D3}.agbwd";
            relativePath = $"{context.RelativeWaveDataDirectory}/{fileName}";
            sample.FilePath = relativePath;
            string path = Path.Combine(context.WaveDataDirectory, fileName);
            var document = new AgbWaveDataDocument
            {
                SourceOffset = sample.HeaderOffset,
                Header = sample,
                DataHex = Convert.ToHexString(context.Rom.Slice(sample.DataOffset, (int)sample.Size))
            };
            File.WriteAllText(path, JsonSerializer.Serialize(document, context.Options));
            context.WaveDataPathsByOffset.Add(sample.HeaderOffset, relativePath);
            context.WaveData.Add(new WaveDataProjectInfo
            {
                Id = context.NextWaveDataId - 1,
                FilePath = relativePath,
                DataFormat = document.DataFormat,
                HeaderOffset = sample.HeaderOffset,
                LoopFlags = sample.LoopFlags,
                Loops = sample.Loops,
                Frequency = sample.Frequency,
                LoopStart = sample.LoopStart,
                Size = sample.Size,
                DataOffset = sample.DataOffset
            });
            context.ExportedFileCount++;
        }

        sample.FilePath = relativePath;
        voice.DataFilePath = relativePath;
    }

    private static void ExportWaveMemory(ExportContext context, VoiceProjectInfo voice)
    {
        if (voice.PsgWaveMemory is not { } waveMemory)
            return;
        if (waveMemory.DataOffset < 0 || waveMemory.DataOffset + 16 > context.Rom.Length)
            return;

        if (!context.WaveMemoryPathsByOffset.TryGetValue(waveMemory.DataOffset, out string? relativePath))
        {
            string fileName = $"wavememory_{context.NextWaveMemoryId++:D3}.agbwm";
            relativePath = $"{context.RelativeWaveMemoryDirectory}/{fileName}";
            string path = Path.Combine(context.WaveMemoryDirectory, fileName);
            File.WriteAllBytes(path, context.Rom.Slice(waveMemory.DataOffset, 16).ToArray());
            context.WaveMemoryPathsByOffset.Add(waveMemory.DataOffset, relativePath);
            context.WaveMemory.Add(new WaveMemoryProjectInfo
            {
                Id = context.NextWaveMemoryId - 1,
                FilePath = relativePath,
                DataOffset = waveMemory.DataOffset,
                Size = 16
            });
            context.ExportedFileCount++;
        }

        waveMemory.FilePath = relativePath;
        voice.DataFilePath = relativePath;
    }

    private static void LinkSongHeaders(AgbSynthProjectFile project, VoiceGroupProjectInfo voiceGroup)
    {
        foreach (var header in project.SongHeaders)
        {
            if (!string.Equals(header.VoiceGroupPointer, voiceGroup.Pointer, StringComparison.OrdinalIgnoreCase))
                continue;

            header.VoiceGroupId = voiceGroup.Id;
            header.VoiceGroupFilePath = voiceGroup.FilePath;
        }
    }

    private sealed class ExportContext
    {
        public ExportContext(
            GbaRom rom,
            string drumSetDirectory,
            string keySplitDirectory,
            string waveDataDirectory,
            string waveMemoryDirectory,
            string relativeDrumSetDirectory,
            string relativeKeySplitDirectory,
            string relativeWaveDataDirectory,
            string relativeWaveMemoryDirectory,
            JsonSerializerOptions options)
        {
            Rom = rom;
            DrumSetDirectory = drumSetDirectory;
            KeySplitDirectory = keySplitDirectory;
            WaveDataDirectory = waveDataDirectory;
            WaveMemoryDirectory = waveMemoryDirectory;
            RelativeDrumSetDirectory = relativeDrumSetDirectory;
            RelativeKeySplitDirectory = relativeKeySplitDirectory;
            RelativeWaveDataDirectory = relativeWaveDataDirectory;
            RelativeWaveMemoryDirectory = relativeWaveMemoryDirectory;
            Options = options;
        }

        public GbaRom Rom { get; }
        public string DrumSetDirectory { get; }
        public string KeySplitDirectory { get; }
        public string WaveDataDirectory { get; }
        public string WaveMemoryDirectory { get; }
        public string RelativeDrumSetDirectory { get; }
        public string RelativeKeySplitDirectory { get; }
        public string RelativeWaveDataDirectory { get; }
        public string RelativeWaveMemoryDirectory { get; }
        public JsonSerializerOptions Options { get; }
        public List<WaveDataProjectInfo> WaveData { get; } = new();
        public List<WaveMemoryProjectInfo> WaveMemory { get; } = new();
        public Dictionary<int, string> DrumSetPathsByOffset { get; } = new();
        public Dictionary<int, string> KeySplitPathsByOffset { get; } = new();
        public Dictionary<int, string> WaveDataPathsByOffset { get; } = new();
        public Dictionary<int, string> WaveMemoryPathsByOffset { get; } = new();
        public int NextDrumSetId { get; set; }
        public int NextKeySplitId { get; set; }
        public int NextWaveDataId { get; set; }
        public int NextWaveMemoryId { get; set; }
        public int ExportedFileCount { get; set; }
    }

    private sealed class AgbVoiceGroupDocument
    {
        public string Format { get; set; } = "AgbSynthVoiceGroup";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        [JsonIgnore]
        public string Pointer { get; set; } = string.Empty;
        [JsonIgnore]
        public int Offset { get; set; }
        public string DiscoverySource { get; set; } = "Referenced";
        public List<int> UsedBySongIds { get; set; } = new();
        public List<VoiceProjectInfo> Voices { get; set; } = new();
    }

    private sealed class AgbDrumSetDocument
    {
        public string Format { get; set; } = "AgbSynthDrumSet";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int VoiceGroupId { get; set; }
        [JsonIgnore]
        public string VoiceGroupPointer { get; set; } = string.Empty;
        public int ParentVoiceIndex { get; set; }
        [JsonIgnore]
        public string SourcePointer { get; set; } = string.Empty;
        [JsonIgnore]
        public int? SourceOffset { get; set; }
        public DrumSetProjectInfo DrumSet { get; set; } = new();
    }

    private sealed class AgbKeySplitDocument
    {
        public string Format { get; set; } = "AgbSynthKeySplit";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int VoiceGroupId { get; set; }
        [JsonIgnore]
        public string VoiceGroupPointer { get; set; } = string.Empty;
        public int ParentVoiceIndex { get; set; }
        [JsonIgnore]
        public string SourcePointer { get; set; } = string.Empty;
        [JsonIgnore]
        public int? SourceOffset { get; set; }
        public KeySplitProjectInfo KeySplit { get; set; } = new();
    }

    private sealed class AgbWaveDataDocument
    {
        public string Format { get; set; } = "AgbSynthWaveData";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        [JsonIgnore]
        public int SourceOffset { get; set; }
        public SampleHeaderProjectInfo Header { get; set; } = new();
        public string DataFormat { get; set; } = "Signed8MonoPcm";
        public string DataHex { get; set; } = string.Empty;
    }
}
