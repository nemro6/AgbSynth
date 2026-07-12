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
        songTable.AssetId = EnsureAssetId(songTable.AssetId);
        return SerializeJson(new SongTableDocument
        {
            AssetId = songTable.AssetId,
            SongTable = songTable,
            Entries = entries
        });
    }

    public static byte[] SerializeSongHeader(SongHeaderProjectInfo header)
    {
        header.AssetId = EnsureAssetId(header.AssetId);
        return SerializeJson(new SongHeaderDocument { AssetId = header.AssetId, Header = header });
    }

    public static byte[] SerializeVoiceGroup(VoiceGroupProjectInfo voiceGroup)
    {
        voiceGroup.AssetId = EnsureAssetId(voiceGroup.AssetId);
        return SerializeJson(new VoiceGroupDocument
        {
            AssetId = voiceGroup.AssetId,
            Id = voiceGroup.Id,
            Label = voiceGroup.Label,
            Pointer = voiceGroup.Pointer,
            Offset = voiceGroup.Offset,
            DiscoverySource = voiceGroup.DiscoverySource,
            UsedBySongIds = voiceGroup.UsedBySongIds,
            Voices = voiceGroup.Voices
        });
    }

    public static byte[] SerializeKeySplit(KeySplitAssetProjectInfo keySplit)
    {
        keySplit.AssetId = EnsureAssetId(keySplit.AssetId);
        return SerializeJson(new KeySplitDocument
        {
            AssetId = keySplit.AssetId,
            VoiceGroupId = keySplit.VoiceGroupId,
            ParentVoiceIndex = keySplit.ParentVoiceIndex,
            KeySplit = keySplit.KeySplit
        });
    }

    public static byte[] SerializeDrumSet(DrumSetAssetProjectInfo drumSet)
    {
        drumSet.AssetId = EnsureAssetId(drumSet.AssetId);
        return SerializeJson(new DrumSetDocument
        {
            AssetId = drumSet.AssetId,
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

    private static string EnsureAssetId(string assetId) =>
        string.IsNullOrWhiteSpace(assetId) ? AgbSynthFormatContracts.NewAssetId() : assetId;
}
