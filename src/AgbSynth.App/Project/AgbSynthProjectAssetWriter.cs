using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectAssetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static byte[] SerializeSongTable(SongTableProjectInfo songTable, List<SongTableEntryProjectInfo> entries)
    {
        return SerializeJson(new SongTableDocument
        {
            SongTable = songTable,
            Entries = entries
        });
    }

    public static byte[] SerializeSongHeader(SongHeaderProjectInfo header)
    {
        return SerializeJson(new SongHeaderDocument { Header = header });
    }

    public static byte[] SerializeVoiceGroup(VoiceGroupProjectInfo voiceGroup)
    {
        return SerializeJson(new VoiceGroupDocument
        {
            Id = voiceGroup.Id,
            Label = voiceGroup.Label,
            DiscoverySource = voiceGroup.DiscoverySource,
            UsedBySongIds = voiceGroup.UsedBySongIds,
            Voices = voiceGroup.Voices
        });
    }

    public static byte[] SerializeKeySplit(KeySplitAssetProjectInfo keySplit)
    {
        return SerializeJson(new KeySplitDocument
        {
            VoiceGroupId = keySplit.VoiceGroupId,
            ParentVoiceIndex = keySplit.ParentVoiceIndex,
            KeySplit = keySplit.KeySplit
        });
    }

    public static byte[] SerializeDrumSet(DrumSetAssetProjectInfo drumSet)
    {
        return SerializeJson(new DrumSetDocument
        {
            VoiceGroupId = drumSet.VoiceGroupId,
            ParentVoiceIndex = drumSet.ParentVoiceIndex,
            DrumSet = drumSet.DrumSet
        });
    }

    public static void SaveSongHeader(string path, SongHeaderProjectInfo header)
    {
        WriteBytes(path, SerializeSongHeader(header));
    }

    public static void SaveVoiceGroup(string path, VoiceGroupProjectInfo voiceGroup)
    {
        WriteBytes(path, SerializeVoiceGroup(voiceGroup));
    }

    public static void SaveKeySplit(string path, KeySplitAssetProjectInfo keySplit)
    {
        WriteBytes(path, SerializeKeySplit(keySplit));
    }

    public static void SaveDrumSet(string path, DrumSetAssetProjectInfo drumSet)
    {
        WriteBytes(path, SerializeDrumSet(drumSet));
    }

    private static byte[] SerializeJson<T>(T document)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, JsonOptions));
    }

    private static void WriteBytes(string path, byte[] data)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, data);
    }

    private sealed class SongTableDocument
    {
        public string Format { get; set; } = "AgbSynthSongTable";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public SongTableProjectInfo SongTable { get; set; } = new();
        public List<SongTableEntryProjectInfo> Entries { get; set; } = new();
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
