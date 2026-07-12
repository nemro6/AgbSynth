using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AgbSynthProjectFile Load(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path is required.", nameof(projectPath));
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("Project file was not found.", projectPath);

        string projectJson = File.ReadAllText(projectPath);
        var project = JsonSerializer.Deserialize<AgbSynthProjectFile>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Project file is empty or invalid.");

        ValidateProjectEnvelope(project, projectPath);

        string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        LoadSongTable(project, projectDirectory);
        LoadSongHeaders(project, projectDirectory);
        LoadKeySplits(project, projectDirectory);
        LoadDrumSets(project, projectDirectory);
        LoadVoiceGroups(project, projectDirectory);
        LoadWaveData(project, projectDirectory);
        LoadWaveMemory(project, projectDirectory);
        LinkVoiceGroupsToVoiceTables(project);
        LinkSongHeadersToVoiceGroups(project);
        LinkSongTableToHeaders(project);
        LinkVoiceDataAssets(project);
        ProjectDiagnostics.Validate(project, projectDirectory);
        return project;
    }

    private static void LoadSongTable(AgbSynthProjectFile project, string projectDirectory)
    {
        string? path = ResolveProjectFile(projectDirectory, project.SongTable.FilePath);
        path ??= EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "songtable", "*.agbst").FirstOrDefault();
        if (path is null)
            return;

        SongTableDocument? document;
        try
        {
            document = ReadDocument<SongTableDocument>(project, path, AgbSynthFormatContracts.SongTableFormat);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            AddAssetDiagnostic(project, path, ex);
            return;
        }
        if (document is null)
            return;

        project.SongTable = document.SongTable;
        project.SongTable.AssetId = EnsureAssetId(document.AssetId, document.SongTable.AssetId, document.Format, path);
        project.SongTable.FilePath = ToProjectRelativePath(projectDirectory, path);
        project.Songs = document.Entries ?? new List<SongTableEntryProjectInfo>();
    }

    private static void LoadSongHeaders(AgbSynthProjectFile project, string projectDirectory)
    {
        project.SongHeaders.Clear();
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "songheader", "*.agbsh"))
        {
            try
            {
                var document = ReadDocument<SongHeaderDocument>(project, path, AgbSynthFormatContracts.SongHeaderFormat);
                SongHeaderProjectInfo? header = document?.Header;
                if (header is null)
                    continue;

                header.AssetId = EnsureAssetId(document!.AssetId, header.AssetId, document.Format, path);
                // The file discovered on disk is authoritative; FilePath is only a rename hint.
                header.FilePath = ToProjectRelativePath(projectDirectory, path);
                if (string.IsNullOrWhiteSpace(header.MidiFilePath))
                {
                    string inferredMidiPath = Path.Combine(
                        Path.GetDirectoryName(Path.GetDirectoryName(path) ?? projectDirectory) ?? projectDirectory,
                        "midi",
                        $"song_{header.SongId:D3}.mid");
                    if (File.Exists(inferredMidiPath))
                        header.MidiFilePath = ToProjectRelativePath(projectDirectory, inferredMidiPath);
                }

                MigrateSequenceReference(header);

                project.SongHeaders.Add(header);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                AddAssetDiagnostic(project, path, ex);
            }
        }

        project.SongHeaders = project.SongHeaders
            .OrderBy(h => h.SongId)
            .ThenBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void LoadVoiceGroups(AgbSynthProjectFile project, string projectDirectory)
    {
        project.VoiceGroups.Clear();
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "voicegroup", "*.agbvg"))
        {
            try
            {
                var document = ReadDocument<VoiceGroupDocument>(project, path, AgbSynthFormatContracts.VoiceGroupFormat);
                if (document?.Voices is null)
                    continue;

                project.VoiceGroups.Add(new VoiceGroupProjectInfo
                {
                    AssetId = EnsureAssetId(document.AssetId, string.Empty, document.Format, path),
                    Id = document.Id,
                    Label = string.IsNullOrWhiteSpace(document.Label)
                        ? Path.GetFileNameWithoutExtension(path)
                        : document.Label,
                    Pointer = document.Pointer,
                    Offset = document.Offset,
                    FilePath = ToProjectRelativePath(projectDirectory, path),
                    DiscoverySource = string.IsNullOrWhiteSpace(document.DiscoverySource) ? "Referenced" : document.DiscoverySource,
                    UsedBySongIds = document.UsedBySongIds ?? new List<int>(),
                    Voices = document.Voices
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                AddAssetDiagnostic(project, path, ex);
            }
        }

        project.VoiceGroups = project.VoiceGroups
            .OrderBy(v => v.Id)
            .ThenBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void LoadKeySplits(AgbSynthProjectFile project, string projectDirectory)
    {
        project.KeySplits.Clear();
        int id = 0;
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "keysplit", "*.agbks"))
        {
            try
            {
                var document = ReadDocument<KeySplitDocument>(project, path, AgbSynthFormatContracts.KeySplitFormat);
                if (document?.KeySplit is null)
                    continue;

                string relativePath = ToProjectRelativePath(projectDirectory, path);
                if (string.IsNullOrWhiteSpace(document.KeySplit.Label))
                    document.KeySplit.Label = Path.GetFileNameWithoutExtension(path);
                project.KeySplits.Add(new KeySplitAssetProjectInfo
                {
                    AssetId = EnsureAssetId(document.AssetId, string.Empty, document.Format, path),
                    Id = id++,
                    Label = document.KeySplit.Label,
                    FilePath = relativePath,
                    VoiceGroupId = document.VoiceGroupId,
                    ParentVoiceIndex = document.ParentVoiceIndex,
                    KeySplit = document.KeySplit
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                AddAssetDiagnostic(project, path, ex);
            }
        }
    }

    private static void LoadDrumSets(AgbSynthProjectFile project, string projectDirectory)
    {
        project.DrumSets.Clear();
        int id = 0;
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "drumset", "*.agbds"))
        {
            try
            {
                var document = ReadDocument<DrumSetDocument>(project, path, AgbSynthFormatContracts.DrumSetFormat);
                if (document?.DrumSet is null)
                    continue;

                string relativePath = ToProjectRelativePath(projectDirectory, path);
                if (string.IsNullOrWhiteSpace(document.DrumSet.Label))
                    document.DrumSet.Label = Path.GetFileNameWithoutExtension(path);
                EnsureDrumSetEntryCount(document.DrumSet);
                project.DrumSets.Add(new DrumSetAssetProjectInfo
                {
                    AssetId = EnsureAssetId(document.AssetId, string.Empty, document.Format, path),
                    Id = id++,
                    Label = document.DrumSet.Label,
                    FilePath = relativePath,
                    VoiceGroupId = document.VoiceGroupId,
                    ParentVoiceIndex = document.ParentVoiceIndex,
                    DrumSet = document.DrumSet
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                AddAssetDiagnostic(project, path, ex);
            }
        }
    }

    private static void LoadWaveData(AgbSynthProjectFile project, string projectDirectory)
    {
        project.WaveData.Clear();
        int id = 0;
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "wavedata", "*.agbwd"))
        {
            try
            {
                var document = ReadDocument<WaveDataDocument>(project, path, AgbSynthFormatContracts.WaveDataFormat, allowLegacyMissingEnvelope: true);
                if (document?.Header is null)
                    continue;

                project.WaveData.Add(new WaveDataProjectInfo
                {
                    AssetId = EnsureAssetId(document.AssetId, string.Empty, document.Format, path),
                    Id = id++,
                    FilePath = ToProjectRelativePath(projectDirectory, path),
                    DataFormat = string.IsNullOrWhiteSpace(document.DataFormat) ? "Signed8MonoPcm" : document.DataFormat,
                    HeaderOffset = document.Header.HeaderOffset,
                    LoopFlags = document.Header.LoopFlags,
                    Loops = document.Header.Loops,
                    Frequency = document.Header.Frequency,
                    LoopStart = document.Header.LoopStart,
                    Size = document.Header.Size,
                    DataOffset = document.Header.DataOffset
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                AddAssetDiagnostic(project, path, ex);
            }
        }
    }

    private static void LoadWaveMemory(AgbSynthProjectFile project, string projectDirectory)
    {
        project.WaveMemory.Clear();
        int id = 0;
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "wavememory", "*.agbwm"))
        {
            string metadataPath = $"{path}.meta.json";
            WaveMemoryMetadataDocument? metadata = null;
            if (File.Exists(metadataPath))
            {
                try
                {
                    metadata = ReadDocument<WaveMemoryMetadataDocument>(project, metadataPath, AgbSynthFormatContracts.WaveMemoryMetadataFormat, allowLegacyMissingEnvelope: true);
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
                {
                    AddAssetDiagnostic(project, metadataPath, ex);
                }
            }
            project.WaveMemory.Add(new WaveMemoryProjectInfo
            {
                AssetId = EnsureAssetId(metadata?.AssetId ?? string.Empty, string.Empty, AgbSynthFormatContracts.WaveMemoryMetadataFormat, path),
                Id = id++,
                FilePath = ToProjectRelativePath(projectDirectory, path),
                DataFormat = metadata?.DataFormat ?? "Mp2kPcm4WaveRam",
                Size = (int)new FileInfo(path).Length
            });
        }
    }

    private static void LinkSongHeadersToVoiceGroups(AgbSynthProjectFile project)
    {
        foreach (var header in project.SongHeaders)
        {
            VoiceGroupProjectInfo? voiceGroup = null;
            if (!string.IsNullOrWhiteSpace(header.VoiceGroupAssetId))
            {
                voiceGroup = project.VoiceGroups.FirstOrDefault(v =>
                    string.Equals(v.AssetId, header.VoiceGroupAssetId, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(header.VoiceGroupFilePath))
            {
                voiceGroup ??= project.VoiceGroups.FirstOrDefault(v =>
                    string.Equals(v.FilePath, header.VoiceGroupFilePath, StringComparison.OrdinalIgnoreCase));
            }

            voiceGroup ??= project.VoiceGroups.FirstOrDefault(v =>
                string.Equals(v.Pointer, header.VoiceGroupPointer, StringComparison.OrdinalIgnoreCase));

            if (voiceGroup is null)
                continue;

            header.VoiceGroupId = voiceGroup.Id;
            header.VoiceGroupFilePath = voiceGroup.FilePath;
            header.VoiceGroupAssetId = voiceGroup.AssetId;
        }
    }

    private static void LinkVoiceGroupsToVoiceTables(AgbSynthProjectFile project)
    {
        var keySplitsByPath = new Dictionary<string, KeySplitAssetProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var keySplit in project.KeySplits.Where(k => !string.IsNullOrWhiteSpace(k.FilePath)))
            keySplitsByPath.TryAdd(keySplit.FilePath, keySplit);

        var drumSetsByPath = new Dictionary<string, DrumSetAssetProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var drumSet in project.DrumSets.Where(d => !string.IsNullOrWhiteSpace(d.FilePath)))
            drumSetsByPath.TryAdd(drumSet.FilePath, drumSet);

        var assetsById = project.KeySplits.Cast<object>()
            .Concat(project.DrumSets)
            .Concat(project.WaveData)
            .Concat(project.WaveMemory)
            .Select(asset => asset switch
            {
                KeySplitAssetProjectInfo value => (AssetId: value.AssetId, FilePath: value.FilePath),
                DrumSetAssetProjectInfo value => (AssetId: value.AssetId, FilePath: value.FilePath),
                WaveDataProjectInfo value => (AssetId: value.AssetId, FilePath: value.FilePath),
                WaveMemoryProjectInfo value => (AssetId: value.AssetId, FilePath: value.FilePath),
                _ => (AssetId: string.Empty, FilePath: string.Empty)
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.AssetId))
            .ToDictionary(value => value.AssetId, value => value.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var voiceGroup in project.VoiceGroups)
        {
            foreach (var voice in voiceGroup.Voices)
            {
                if (!string.IsNullOrWhiteSpace(voice.DataAssetId) && assetsById.TryGetValue(voice.DataAssetId, out string? resolvedPath))
                    voice.DataFilePath = resolvedPath;
                LinkVoiceToVoiceTables(voice, keySplitsByPath, drumSetsByPath);
            }
        }
    }

    private static void LinkVoiceToVoiceTables(
        VoiceProjectInfo voice,
        IReadOnlyDictionary<string, KeySplitAssetProjectInfo> keySplitsByPath,
        IReadOnlyDictionary<string, DrumSetAssetProjectInfo> drumSetsByPath)
    {
        if (!string.IsNullOrWhiteSpace(voice.DataFilePath))
        {
            if (voice.Type == 0x40 && keySplitsByPath.TryGetValue(voice.DataFilePath, out var keySplit))
                voice.KeySplit = keySplit.KeySplit;
            if (voice.Type == 0x80 && drumSetsByPath.TryGetValue(voice.DataFilePath, out var drumSet))
            {
                EnsureDrumSetEntryCount(drumSet.DrumSet);
                voice.DrumSet = drumSet.DrumSet;
            }
        }

        if (voice.KeySplit is not null)
        {
            foreach (var region in voice.KeySplit.Regions)
                LinkVoiceToVoiceTables(region, keySplitsByPath, drumSetsByPath);
        }

        if (voice.DrumSet is not null)
        {
            EnsureDrumSetEntryCount(voice.DrumSet);
            foreach (var entry in voice.DrumSet.Entries)
                LinkVoiceToVoiceTables(entry, keySplitsByPath, drumSetsByPath);
        }
    }

    private static void EnsureDrumSetEntryCount(DrumSetProjectInfo drumSet)
    {
        for (int i = drumSet.Entries.Count; i < 128; i++)
        {
            drumSet.Entries.Add(new VoiceProjectInfo
            {
                Index = i,
                Type = 0x01,
                TypeName = "Square 1",
                Key = 60,
                Length = 0,
                PanOrSweep = 0,
                DataPointer = "0x00000002",
                Attack = 255,
                Decay = 255,
                Sustain = 15,
                Release = 255,
                PsgSquare = new PsgSquareProjectInfo
                {
                    DutyIndex = 2,
                    DutyRatio = 0.5
                }
            });
        }

        if (drumSet.Entries.Count > 128)
            drumSet.Entries.RemoveRange(128, drumSet.Entries.Count - 128);
        for (int i = 0; i < drumSet.Entries.Count; i++)
            drumSet.Entries[i].Index = i;
    }

    private static IEnumerable<string> EnumerateAssetFiles(
        string projectDirectory,
        string? assetRoot,
        string category,
        string pattern)
    {
        string? preferredDirectory = assetRoot is null
            ? null
            : Path.Combine(projectDirectory, assetRoot, category);

        if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
            return Directory.EnumerateFiles(preferredDirectory, pattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(projectDirectory, pattern, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveProjectFile(string projectDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string path = Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.Combine(projectDirectory, normalizedPath);
        return File.Exists(path) ? path : null;
    }

    private static string? GetAssetRoot(AgbSynthProjectFile project)
    {
        string path = project.SongTable.FilePath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized = path.Replace('\\', '/');
        const string marker = "/songtable/";
        int markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex > 0 ? normalized[..markerIndex] : null;
    }

    private static string ToProjectRelativePath(string projectDirectory, string path)
    {
        return Path.GetRelativePath(projectDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void ValidateProjectEnvelope(AgbSynthProjectFile project, string path)
    {
        if (!string.Equals(project.Format, AgbSynthFormatContracts.ProjectFormat, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported project format '{project.Format}' in {path}.");
        if (!string.Equals(project.Engine, AgbSynthFormatContracts.Engine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported sound engine '{project.Engine}' in {path}.");
        if (project.Version <= 0)
            throw new InvalidDataException($"Invalid project version {project.Version} in {path}.");
        if (project.Version > AgbSynthFormatContracts.ProjectVersion)
        {
            project.IsReadOnly = true;
            project.Diagnostics.Add(new ProjectDiagnostic(
                ProjectDiagnosticSeverity.Warning,
                "PROJECT_NEWER_VERSION",
                $"Project version {project.Version} is newer than supported version {AgbSynthFormatContracts.ProjectVersion}. The project is read-only.",
                path));
        }
        else if (project.Version < AgbSynthFormatContracts.ProjectVersion)
        {
            project.Diagnostics.Add(new ProjectDiagnostic(
                ProjectDiagnosticSeverity.Info,
                "PROJECT_MIGRATED",
                $"Project version {project.Version} was migrated in memory to version {AgbSynthFormatContracts.ProjectVersion}.",
                path));
            project.Version = AgbSynthFormatContracts.ProjectVersion;
        }
    }

    private static T? ReadDocument<T>(
        AgbSynthProjectFile project,
        string path,
        string expectedFormat,
        bool allowLegacyMissingEnvelope = false)
        where T : AgbAssetDocument
    {
        T? document = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        if (document is null)
            return null;
        if (string.IsNullOrWhiteSpace(document.Format) && allowLegacyMissingEnvelope)
            document.Format = expectedFormat;
        if (!string.Equals(document.Format, expectedFormat, StringComparison.Ordinal))
            throw new InvalidDataException($"Expected {expectedFormat}, found '{document.Format}'.");
        if (!string.Equals(document.Engine, AgbSynthFormatContracts.Engine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported sound engine '{document.Engine}'.");
        if (document.Version <= 0)
            throw new InvalidDataException($"Invalid asset version {document.Version}.");
        if (document.Version > AgbSynthFormatContracts.AssetVersion)
        {
            project.IsReadOnly = true;
            project.Diagnostics.Add(new ProjectDiagnostic(ProjectDiagnosticSeverity.Warning, "ASSET_NEWER_VERSION", $"Asset version {document.Version} is newer than supported version {AgbSynthFormatContracts.AssetVersion}.", path));
        }
        return document;
    }

    private static string EnsureAssetId(string documentId, string modelId, string format, string path)
    {
        if (!string.IsNullOrWhiteSpace(documentId))
            return documentId;
        if (!string.IsNullOrWhiteSpace(modelId))
            return modelId;
        return AgbSynthFormatContracts.CreateLegacyAssetId(format, path);
    }

    private static void MigrateSequenceReference(SongHeaderProjectInfo header)
    {
        if (string.IsNullOrWhiteSpace(header.SequenceFilePath))
        {
            header.SequenceFilePath = header.SequenceFormat == SequenceAssetFormat.Midi2Agb
                ? header.Midi2AgbFilePath
                : header.MidiFilePath;
        }
        if (string.IsNullOrWhiteSpace(header.MidiFilePath) && header.SequenceFormat == SequenceAssetFormat.Midi)
            header.MidiFilePath = header.SequenceFilePath;
        if (string.IsNullOrWhiteSpace(header.Midi2AgbFilePath) && header.SequenceFormat == SequenceAssetFormat.Midi2Agb)
            header.Midi2AgbFilePath = header.SequenceFilePath;
    }

    private static void LinkSongTableToHeaders(AgbSynthProjectFile project)
    {
        foreach (var song in project.Songs)
        {
            SongHeaderProjectInfo? header = null;
            if (!string.IsNullOrWhiteSpace(song.SongHeaderAssetId))
                header = project.SongHeaders.FirstOrDefault(value => string.Equals(value.AssetId, song.SongHeaderAssetId, StringComparison.OrdinalIgnoreCase));
            if (header is null && !string.IsNullOrWhiteSpace(song.SongHeaderFilePath))
                header = project.SongHeaders.FirstOrDefault(value => string.Equals(value.FilePath, song.SongHeaderFilePath, StringComparison.OrdinalIgnoreCase));
            header ??= project.SongHeaders.FirstOrDefault(value => value.SongId == song.SongId);
            if (header is null)
                continue;
            song.SongHeaderAssetId = header.AssetId;
            song.SongHeaderFilePath = header.FilePath;
        }
    }

    private static void LinkVoiceDataAssets(AgbSynthProjectFile project)
    {
        var idsByPath = project.KeySplits.Select(value => (value.FilePath, value.AssetId))
            .Concat(project.DrumSets.Select(value => (value.FilePath, value.AssetId)))
            .Concat(project.WaveData.Select(value => (value.FilePath, value.AssetId)))
            .Concat(project.WaveMemory.Select(value => (value.FilePath, value.AssetId)))
            .Where(value => !string.IsNullOrWhiteSpace(value.FilePath))
            .ToDictionary(value => value.FilePath, value => value.AssetId, StringComparer.OrdinalIgnoreCase);
        foreach (var voice in project.VoiceGroups.SelectMany(group => group.Voices))
            LinkVoiceDataAsset(voice, idsByPath);
    }

    private static void LinkVoiceDataAsset(VoiceProjectInfo voice, IReadOnlyDictionary<string, string> idsByPath)
    {
        if (string.IsNullOrWhiteSpace(voice.DataAssetId) && idsByPath.TryGetValue(voice.DataFilePath, out string? assetId))
            voice.DataAssetId = assetId;
        if (voice.KeySplit is not null)
            foreach (var child in voice.KeySplit.Regions)
                LinkVoiceDataAsset(child, idsByPath);
        if (voice.DrumSet is not null)
            foreach (var child in voice.DrumSet.Entries)
                LinkVoiceDataAsset(child, idsByPath);
    }

    private static void AddAssetDiagnostic(AgbSynthProjectFile project, string path, Exception exception)
    {
        project.Diagnostics.Add(new ProjectDiagnostic(ProjectDiagnosticSeverity.Error, "ASSET_LOAD_FAILED", exception.Message, path));
    }
}
