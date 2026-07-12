using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<bool> SaveProjectAsync()
    {
        if (_currentProject is null || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            RomStatus = "Save failed: no project is open.";
            return false;
        }
        if (IsSavingProject)
            return false;

        try
        {
            IsSavingProject = true;
            ProjectSavePlan plan = CreateProjectSavePlan();
            await Task.Run(plan.Transaction.Commit);

            HashSet<string> filesAddedDuringSave = GetCurrentManagedFiles();
            _knownManagedFiles.Clear();
            _knownManagedFiles.UnionWith(plan.ManagedFiles);
            _knownManagedFiles.UnionWith(filesAddedDuringSave);
            _savedProjectHistoryPosition = plan.HistoryPosition;
            _savedUntrackedProjectRevision = plan.UntrackedRevision;
            UpdateProjectDirtyState();
            RomStatus = $"Project saved: {Path.GetFileName(_currentProjectPath)}";
            return true;
        }
        catch (Exception ex)
        {
            RomStatus = $"Project save failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsSavingProject = false;
        }
    }

    private ProjectSavePlan CreateProjectSavePlan()
    {
        SynchronizeCurrentProjectModel();

        var transaction = new ProjectFileTransaction();
        var managedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AgbSynthProjectFile project = _currentProject!;

        AddManagedWrite(
            transaction,
            managedFiles,
            project.SongTable.FilePath,
            AgbSynthProjectAssetWriter.SerializeSongTable(project.SongTable, project.Songs));

        foreach (SongHeaderProjectInfo header in project.SongHeaders)
        {
            AddManagedWrite(
                transaction,
                managedFiles,
                header.FilePath,
                AgbSynthProjectAssetWriter.SerializeSongHeader(header));
        }

        foreach (VoiceGroupProjectInfo voiceGroup in project.VoiceGroups)
        {
            AddManagedWrite(
                transaction,
                managedFiles,
                voiceGroup.FilePath,
                AgbSynthProjectAssetWriter.SerializeVoiceGroup(voiceGroup));
        }

        foreach (KeySplitAssetProjectInfo keySplit in project.KeySplits)
        {
            AddManagedWrite(
                transaction,
                managedFiles,
                keySplit.FilePath,
                AgbSynthProjectAssetWriter.SerializeKeySplit(keySplit));
        }

        foreach (DrumSetAssetProjectInfo drumSet in project.DrumSets)
        {
            AddManagedWrite(
                transaction,
                managedFiles,
                drumSet.FilePath,
                AgbSynthProjectAssetWriter.SerializeDrumSet(drumSet));
        }

        foreach (WaveMemoryRow row in WaveMemoryRows)
        {
            string path = ResolveProjectAssetPath(row.FilePath);
            transaction.AddWrite(path, row.DataBytes);
            transaction.AddWrite($"{path}.meta.json", row.CreateMetadataBytes());
            managedFiles.Add(path);
            managedFiles.Add($"{path}.meta.json");
        }

        foreach (WaveDataRow row in WaveDataRows)
        {
            AddManagedWrite(transaction, managedFiles, row.FilePath, row.CreateAssetBytes());
        }

        foreach (string removedPath in _knownManagedFiles.Except(managedFiles, StringComparer.OrdinalIgnoreCase))
            transaction.AddDelete(removedPath);

        transaction.AddWrite(_currentProjectPath!, AgbSynthProjectExporter.Serialize(project));
        return new ProjectSavePlan(
            transaction,
            managedFiles,
            _projectHistoryPosition,
            _untrackedProjectRevision);
    }

    private void SynchronizeCurrentProjectModel()
    {
        if (_currentProject is null)
            return;

        _currentProject.SongTable.ValidEntryCount = SongTableEntries.Count;
        _currentProject.Songs = SongTableEntries.Select(row => row.ToProjectInfo()).ToList();
        _currentProject.SongHeaders = Sequences.Select(row =>
        {
            SongHeaderProjectInfo header = row.ToProjectInfo();
            VoiceGroupOption? voiceGroup = VoiceGroupOptions.FirstOrDefault(option =>
                AssetPathMatches(option.FilePath, header.VoiceGroupFilePath));
            header.VoiceGroupId = voiceGroup?.Id;
            header.VoiceGroupAssetId = voiceGroup?.AssetId ?? string.Empty;
            return header;
        }).ToList();
        foreach (SongTableEntryProjectInfo song in _currentProject.Songs)
        {
            song.SongHeaderAssetId = _currentProject.SongHeaders.FirstOrDefault(header =>
                AssetPathMatches(header.FilePath, song.SongHeaderFilePath))?.AssetId ?? string.Empty;
        }

        _currentProject.VoiceGroups = VoiceGroupOptions.Select(option =>
        {
            VoiceGroupProjectInfo source = option.Source ?? new VoiceGroupProjectInfo();
            source.AssetId = option.AssetId;
            source.Id = option.Id;
            source.Label = option.Label;
            source.FilePath = option.FilePath;
            source.Voices = option.Voices;
            return source;
        }).ToList();

        _currentProject.KeySplits = KeySplitOptions.Select(option => new KeySplitAssetProjectInfo
        {
            AssetId = option.AssetId,
            Id = option.Id,
            Label = option.Label,
            FilePath = option.FilePath,
            VoiceGroupId = option.VoiceGroupId,
            ParentVoiceIndex = option.ParentVoiceIndex,
            KeySplit = option.KeySplit
        }).ToList();

        _currentProject.DrumSets = DrumSetOptions.Select(option => new DrumSetAssetProjectInfo
        {
            AssetId = option.AssetId,
            Id = option.Id,
            Label = option.Label,
            FilePath = option.FilePath,
            VoiceGroupId = option.VoiceGroupId,
            ParentVoiceIndex = option.ParentVoiceIndex,
            DrumSet = option.DrumSet
        }).ToList();

        _currentProject.WaveMemory = WaveMemoryRows.Select(row => new WaveMemoryProjectInfo
        {
            AssetId = row.AssetId,
            Id = row.Id,
            FilePath = row.FilePath,
            DataFormat = row.DataFormat,
            Size = row.Size
        }).ToList();

        _currentProject.WaveData = WaveDataRows.Select(row =>
        {
            SampleHeaderProjectInfo header = row.ToSampleHeader();
            return new WaveDataProjectInfo
            {
                AssetId = row.AssetId,
                Id = row.Id,
                FilePath = row.FilePath,
                DataFormat = row.DataFormat,
                HeaderOffset = header.HeaderOffset,
                LoopFlags = header.LoopFlags,
                Loops = header.Loops,
                Frequency = header.Frequency,
                LoopStart = header.LoopStart,
                Size = header.Size,
                DataOffset = header.DataOffset
            };
        }).ToList();

        var dataIdsByPath = _currentProject.KeySplits.Select(value => (value.FilePath, value.AssetId))
            .Concat(_currentProject.DrumSets.Select(value => (value.FilePath, value.AssetId)))
            .Concat(_currentProject.WaveData.Select(value => (value.FilePath, value.AssetId)))
            .Concat(_currentProject.WaveMemory.Select(value => (value.FilePath, value.AssetId)))
            .Where(value => !string.IsNullOrWhiteSpace(value.FilePath))
            .ToDictionary(value => value.FilePath, value => value.AssetId, StringComparer.OrdinalIgnoreCase);
        foreach (VoiceProjectInfo voice in _currentProject.VoiceGroups.SelectMany(value => value.Voices))
            AssignVoiceDataAssetId(voice, dataIdsByPath);
    }

    private static void AssignVoiceDataAssetId(VoiceProjectInfo voice, IReadOnlyDictionary<string, string> idsByPath)
    {
        voice.DataAssetId = idsByPath.TryGetValue(voice.DataFilePath, out string? assetId) ? assetId : string.Empty;
        if (voice.KeySplit is not null)
            foreach (VoiceProjectInfo child in voice.KeySplit.Regions)
                AssignVoiceDataAssetId(child, idsByPath);
        if (voice.DrumSet is not null)
            foreach (VoiceProjectInfo child in voice.DrumSet.Entries)
                AssignVoiceDataAssetId(child, idsByPath);
    }

    private HashSet<string> GetCurrentManagedFiles()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddManagedPath(paths, _currentProject?.SongTable.FilePath);
        foreach (SequenceHeaderRow row in Sequences)
            AddManagedPath(paths, row.FilePath);
        foreach (VoiceGroupOption option in VoiceGroupOptions)
            AddManagedPath(paths, option.FilePath);
        foreach (KeySplitOption option in KeySplitOptions)
            AddManagedPath(paths, option.FilePath);
        foreach (DrumSetOption option in DrumSetOptions)
            AddManagedPath(paths, option.FilePath);
        foreach (WaveMemoryRow row in WaveMemoryRows)
        {
            AddManagedPath(paths, row.FilePath);
            if (!string.IsNullOrWhiteSpace(row.FilePath))
                paths.Add($"{ResolveProjectAssetPath(row.FilePath)}.meta.json");
        }
        foreach (WaveDataRow row in WaveDataRows)
            AddManagedPath(paths, row.FilePath);
        return paths;
    }

    private void AddManagedWrite(ProjectFileTransaction transaction, ISet<string> managedFiles, string relativePath, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("An asset does not have a file path. Save the asset with a name before saving the project.");

        string path = ResolveProjectAssetPath(relativePath);
        transaction.AddWrite(path, data);
        managedFiles.Add(path);
    }

    private void AddManagedPath(ISet<string> paths, string? relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
            paths.Add(ResolveProjectAssetPath(relativePath));
    }

    private string ResolveProjectAssetPath(string path)
    {
        string normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        string projectDirectory = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(_currentProjectPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(projectDirectory, normalized));
    }

    private sealed record ProjectSavePlan(
        ProjectFileTransaction Transaction,
        HashSet<string> ManagedFiles,
        int HistoryPosition,
        long UntrackedRevision);

}
