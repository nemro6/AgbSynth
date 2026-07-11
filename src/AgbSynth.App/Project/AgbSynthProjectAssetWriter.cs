using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectAssetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void SaveSongHeader(string path, SongHeaderProjectInfo header)
    {
        WriteJson(path, new SongHeaderDocument { Header = header });
    }

    public static void SaveVoiceGroup(string path, VoiceGroupProjectInfo voiceGroup)
    {
        WriteJson(path, new VoiceGroupDocument
        {
            Id = voiceGroup.Id,
            Label = voiceGroup.Label,
            DiscoverySource = voiceGroup.DiscoverySource,
            UsedBySongIds = voiceGroup.UsedBySongIds,
            Voices = voiceGroup.Voices
        });
    }

    public static void SaveKeySplit(string path, KeySplitAssetProjectInfo keySplit)
    {
        WriteJson(path, new KeySplitDocument
        {
            VoiceGroupId = keySplit.VoiceGroupId,
            ParentVoiceIndex = keySplit.ParentVoiceIndex,
            KeySplit = keySplit.KeySplit
        });
    }

    public static void SaveDrumSet(string path, DrumSetAssetProjectInfo drumSet)
    {
        WriteJson(path, new DrumSetDocument
        {
            VoiceGroupId = drumSet.VoiceGroupId,
            ParentVoiceIndex = drumSet.ParentVoiceIndex,
            DrumSet = drumSet.DrumSet
        });
    }

    private static void WriteJson<T>(string path, T document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private sealed class SongHeaderDocument
    {
        public string Format { get; set; } = "AgbSynthSongHeader";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public SongHeaderProjectInfo Header { get; set; } = new();
    }

    private sealed class VoiceGroupDocument
    {
        public string Format { get; set; } = "AgbSynthVoiceGroup";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string DiscoverySource { get; set; } = "User";
        public List<int> UsedBySongIds { get; set; } = new();
        public List<VoiceProjectInfo> Voices { get; set; } = new();
    }

    private sealed class KeySplitDocument
    {
        public string Format { get; set; } = "AgbSynthKeySplit";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int VoiceGroupId { get; set; } = -1;
        public int ParentVoiceIndex { get; set; } = -1;
        public KeySplitProjectInfo KeySplit { get; set; } = new();
    }

    private sealed class DrumSetDocument
    {
        public string Format { get; set; } = "AgbSynthDrumSet";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public int VoiceGroupId { get; set; } = -1;
        public int ParentVoiceIndex { get; set; } = -1;
        public DrumSetProjectInfo DrumSet { get; set; } = new();
    }
}
