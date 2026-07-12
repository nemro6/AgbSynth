using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgbSynth.App.Project;

public static class ProjectDiagnostics
{
    public static void Validate(AgbSynthProjectFile project, string projectDirectory)
    {
        ValidateUniqueIds(project, project.SongHeaders.Select(value => (value.AssetId, value.FilePath)), "SongHeader");
        ValidateUniqueIds(project, project.VoiceGroups.Select(value => (value.AssetId, value.FilePath)), "VoiceGroup");
        ValidateUniqueIds(project, project.KeySplits.Select(value => (value.AssetId, value.FilePath)), "KeySplit");
        ValidateUniqueIds(project, project.DrumSets.Select(value => (value.AssetId, value.FilePath)), "DrumSet");
        ValidateUniqueIds(project, project.WaveData.Select(value => (value.AssetId, value.FilePath)), "WaveData");
        ValidateUniqueIds(project, project.WaveMemory.Select(value => (value.AssetId, value.FilePath)), "WaveMemory");

        foreach (SongTableEntryProjectInfo song in project.Songs)
        {
            if (string.IsNullOrWhiteSpace(song.SongHeaderAssetId) || project.SongHeaders.All(value => value.AssetId != song.SongHeaderAssetId))
                Add(project, ProjectDiagnosticSeverity.Warning, "MISSING_SONG_HEADER", $"SongTable entry {song.SongId} does not resolve to a SongHeader.", project.SongTable.FilePath);
        }

        foreach (SongHeaderProjectInfo header in project.SongHeaders)
        {
            ValidateFile(project, projectDirectory, header.FilePath, "MISSING_ASSET_FILE");
            if (!string.IsNullOrWhiteSpace(header.SequenceFilePath))
                ValidateFile(project, projectDirectory, header.SequenceFilePath, "MISSING_SEQUENCE_FILE");
            if (!string.IsNullOrWhiteSpace(header.VoiceGroupAssetId) && project.VoiceGroups.All(value => value.AssetId != header.VoiceGroupAssetId))
                Add(project, ProjectDiagnosticSeverity.Error, "MISSING_VOICE_GROUP", $"SongHeader '{header.Label}' references an unknown VoiceGroup AssetId.", header.FilePath);
        }

        foreach (WaveMemoryProjectInfo wave in project.WaveMemory.Where(value => value.Size != 16))
            Add(project, ProjectDiagnosticSeverity.Error, "INVALID_WAVE_MEMORY_SIZE", $"WaveMemory must contain exactly 16 bytes; found {wave.Size}.", wave.FilePath);

        foreach (VoiceGroupProjectInfo group in project.VoiceGroups)
        {
            if (group.Voices.Count != 128)
                Add(project, ProjectDiagnosticSeverity.Warning, "INVALID_VOICE_GROUP_SIZE", $"VoiceGroup '{group.Label}' contains {group.Voices.Count} voices; MP2K banks normally contain 128.", group.FilePath);
            foreach (VoiceProjectInfo voice in group.Voices.Where(value => !string.IsNullOrWhiteSpace(value.DataAssetId)))
            {
                bool exists = project.KeySplits.Any(value => value.AssetId == voice.DataAssetId)
                    || project.DrumSets.Any(value => value.AssetId == voice.DataAssetId)
                    || project.WaveData.Any(value => value.AssetId == voice.DataAssetId)
                    || project.WaveMemory.Any(value => value.AssetId == voice.DataAssetId);
                if (!exists)
                    Add(project, ProjectDiagnosticSeverity.Error, "MISSING_VOICE_DATA", $"Voice {voice.Index} references an unknown data AssetId.", group.FilePath);
            }
        }
    }

    private static void ValidateUniqueIds(
        AgbSynthProjectFile project,
        IEnumerable<(string AssetId, string FilePath)> assets,
        string kind)
    {
        foreach (var duplicate in assets.Where(value => !string.IsNullOrWhiteSpace(value.AssetId)).GroupBy(value => value.AssetId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            Add(project, ProjectDiagnosticSeverity.Error, "DUPLICATE_ASSET_ID", $"{kind} AssetId '{duplicate.Key}' is used by multiple files.", duplicate.First().FilePath);
    }

    private static void ValidateFile(AgbSynthProjectFile project, string projectDirectory, string relativePath, string code)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;
        string path = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(projectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            Add(project, ProjectDiagnosticSeverity.Error, code, "Referenced file was not found.", relativePath);
    }

    private static void Add(AgbSynthProjectFile project, ProjectDiagnosticSeverity severity, string code, string message, string? path)
    {
        if (!project.Diagnostics.Any(value => value.Code == code && value.FilePath == path && value.Message == message))
            project.Diagnostics.Add(new ProjectDiagnostic(severity, code, message, path));
    }
}
