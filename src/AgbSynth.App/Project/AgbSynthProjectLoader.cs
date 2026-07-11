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

        var project = JsonSerializer.Deserialize<AgbSynthProjectFile>(File.ReadAllText(projectPath), JsonOptions)
            ?? throw new InvalidDataException("Project file is empty or invalid.");

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
        return project;
    }

    private static void LoadSongTable(AgbSynthProjectFile project, string projectDirectory)
    {
        string? path = ResolveProjectFile(projectDirectory, project.SongTable.FilePath);
        path ??= EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "songtable", "*.agbst").FirstOrDefault();
        if (path is null)
            return;

        var document = JsonSerializer.Deserialize<AgbSongTableDocument>(File.ReadAllText(path), JsonOptions);
        if (document is null)
            return;

        project.SongTable = document.SongTable;
        if (string.IsNullOrWhiteSpace(project.SongTable.FilePath))
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
                var document = JsonSerializer.Deserialize<AgbSongHeaderDocument>(File.ReadAllText(path), JsonOptions);
                SongHeaderProjectInfo? header = document?.Header;
                if (header is null)
                    continue;

                if (string.IsNullOrWhiteSpace(header.FilePath))
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

                project.SongHeaders.Add(header);
            }
            catch (JsonException)
            {
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
                var document = JsonSerializer.Deserialize<AgbVoiceGroupDocument>(File.ReadAllText(path), JsonOptions);
                if (document?.Voices is null)
                    continue;

                project.VoiceGroups.Add(new VoiceGroupProjectInfo
                {
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
            catch (JsonException)
            {
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
                var document = JsonSerializer.Deserialize<AgbKeySplitDocument>(File.ReadAllText(path), JsonOptions);
                if (document?.KeySplit is null)
                    continue;

                string relativePath = ToProjectRelativePath(projectDirectory, path);
                if (string.IsNullOrWhiteSpace(document.KeySplit.Label))
                    document.KeySplit.Label = Path.GetFileNameWithoutExtension(path);
                project.KeySplits.Add(new KeySplitAssetProjectInfo
                {
                    Id = id++,
                    Label = document.KeySplit.Label,
                    FilePath = relativePath,
                    VoiceGroupId = document.VoiceGroupId,
                    ParentVoiceIndex = document.ParentVoiceIndex,
                    KeySplit = document.KeySplit
                });
            }
            catch (JsonException)
            {
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
                var document = JsonSerializer.Deserialize<AgbDrumSetDocument>(File.ReadAllText(path), JsonOptions);
                if (document?.DrumSet is null)
                    continue;

                string relativePath = ToProjectRelativePath(projectDirectory, path);
                if (string.IsNullOrWhiteSpace(document.DrumSet.Label))
                    document.DrumSet.Label = Path.GetFileNameWithoutExtension(path);
                EnsureDrumSetEntryCount(document.DrumSet);
                project.DrumSets.Add(new DrumSetAssetProjectInfo
                {
                    Id = id++,
                    Label = document.DrumSet.Label,
                    FilePath = relativePath,
                    VoiceGroupId = document.VoiceGroupId,
                    ParentVoiceIndex = document.ParentVoiceIndex,
                    DrumSet = document.DrumSet
                });
            }
            catch (JsonException)
            {
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
                var document = JsonSerializer.Deserialize<AgbWaveDataDocument>(File.ReadAllText(path), JsonOptions);
                if (document?.Header is null)
                    continue;

                project.WaveData.Add(new WaveDataProjectInfo
                {
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
            catch (JsonException)
            {
            }
        }
    }

    private static void LoadWaveMemory(AgbSynthProjectFile project, string projectDirectory)
    {
        project.WaveMemory.Clear();
        int id = 0;
        foreach (string path in EnumerateAssetFiles(projectDirectory, GetAssetRoot(project), "wavememory", "*.agbwm"))
        {
            project.WaveMemory.Add(new WaveMemoryProjectInfo
            {
                Id = id++,
                FilePath = ToProjectRelativePath(projectDirectory, path),
                Size = (int)Math.Min(16, new FileInfo(path).Length)
            });
        }
    }

    private static void LinkSongHeadersToVoiceGroups(AgbSynthProjectFile project)
    {
        foreach (var header in project.SongHeaders)
        {
            VoiceGroupProjectInfo? voiceGroup = null;
            if (!string.IsNullOrWhiteSpace(header.VoiceGroupFilePath))
            {
                voiceGroup = project.VoiceGroups.FirstOrDefault(v =>
                    string.Equals(v.FilePath, header.VoiceGroupFilePath, StringComparison.OrdinalIgnoreCase));
            }

            voiceGroup ??= project.VoiceGroups.FirstOrDefault(v =>
                string.Equals(v.Pointer, header.VoiceGroupPointer, StringComparison.OrdinalIgnoreCase));

            if (voiceGroup is null)
                continue;

            header.VoiceGroupId = voiceGroup.Id;
            header.VoiceGroupFilePath = voiceGroup.FilePath;
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

        foreach (var voiceGroup in project.VoiceGroups)
        {
            foreach (var voice in voiceGroup.Voices)
                LinkVoiceToVoiceTables(voice, keySplitsByPath, drumSetsByPath);
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

    private sealed class AgbSongTableDocument
    {
        public SongTableProjectInfo SongTable { get; set; } = new();
        public List<SongTableEntryProjectInfo> Entries { get; set; } = new();
    }

    private sealed class AgbSongHeaderDocument
    {
        public SongHeaderProjectInfo Header { get; set; } = new();
    }

    private sealed class AgbVoiceGroupDocument
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Pointer { get; set; } = string.Empty;
        public int Offset { get; set; }
        public string DiscoverySource { get; set; } = "Referenced";
        public List<int> UsedBySongIds { get; set; } = new();
        public List<VoiceProjectInfo> Voices { get; set; } = new();
    }

    private sealed class AgbKeySplitDocument
    {
        public int VoiceGroupId { get; set; }
        public int ParentVoiceIndex { get; set; }
        public KeySplitProjectInfo KeySplit { get; set; } = new();
    }

    private sealed class AgbDrumSetDocument
    {
        public int VoiceGroupId { get; set; }
        public int ParentVoiceIndex { get; set; }
        public DrumSetProjectInfo DrumSet { get; set; } = new();
    }

    private sealed class AgbWaveDataDocument
    {
        public SampleHeaderProjectInfo Header { get; set; } = new();
        public string DataFormat { get; set; } = "Signed8MonoPcm";
    }
}
