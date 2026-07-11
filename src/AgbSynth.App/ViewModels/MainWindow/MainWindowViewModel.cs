using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AgbSynth.App.GBA;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private string _romStatus = "Open a .gba ROM from the File menu.";
    private string _songTableStatus = "Create a project to inspect the MP2K song table.";
    private string _sequenceStatus = "Create a project to inspect MP2K song headers.";
    private string _voiceGroupStatus = "Create a project to inspect MP2K voice groups.";
    private string _keySplitStatus = "Create a project to inspect MP2K key splits.";
    private string _drumSetStatus = "Create a project to inspect MP2K drum sets.";
    private string _waveMemoryStatus = "Create a project to inspect MP2K wave memory.";
    private string _voiceStatus = "Create a project to inspect DirectSound voices.";
    private string? _currentProjectPath;
    private readonly Dictionary<string, string> _songHeaderDisplayByPath = new(StringComparer.OrdinalIgnoreCase);
    private SongTableEntryRow? _selectedSongTableEntry;
    private SequenceHeaderRow? _selectedSequence;
    private PlaybackSelectionSource _playbackSelectionSource = PlaybackSelectionSource.SongHeader;
    private bool _suppressPlaybackSelectionSourceUpdate;
    private VoiceGroupOption? _selectedVoiceGroup;
    private KeySplitOption? _selectedKeySplit;
    private DrumSetOption? _selectedDrumSet;
    private WaveMemoryRow? _selectedWaveMemory;
    private WaveDataRow? _selectedWaveData;
    private VoiceRow? _selectedVoice;
    private VoiceRow? _selectedKeySplitVoice;
    private VoiceRow? _selectedDrumSetVoice;
    private VoiceProjectInfo? _copiedVoice;
    private IReadOnlyList<int> _selectedVoiceKeyMarkers = [];
    private IReadOnlyList<int> _selectedDrumSetKeyMarkers = [];
    private int _selectedKeySplitRegionIndex = -1;
    private GbaRom? _loadedRom;

    public string RomStatus
    {
        get => _romStatus;
        private set => SetField(ref _romStatus, value);
    }

    public GbaRom? LoadedRom => _loadedRom;
    public ObservableCollection<SongTableEntryRow> SongTableEntries { get; } = new();
    public ObservableCollection<string> SongHeaderFileOptions { get; } = new();
    public ObservableCollection<SequenceHeaderRow> Sequences { get; } = new();
    public ObservableCollection<VoiceGroupOption> VoiceGroupOptions { get; } = new();
    public ObservableCollection<KeySplitOption> KeySplitOptions { get; } = new();
    public ObservableCollection<DrumSetOption> DrumSetOptions { get; } = new();
    public ObservableCollection<WaveMemoryRow> WaveMemoryRows { get; } = new();
    public ObservableCollection<WaveDataRow> WaveDataRows { get; } = new();
    public ObservableCollection<VoiceRow> Voices { get; } = new();
    public ObservableCollection<VoiceRow> KeySplitRows { get; } = new();
    public ObservableCollection<VoiceRow> DrumSetRows { get; } = new();

    public string SongTableStatus
    {
        get => _songTableStatus;
        private set => SetField(ref _songTableStatus, value);
    }

    public string SequenceStatus
    {
        get => _sequenceStatus;
        private set => SetField(ref _sequenceStatus, value);
    }

    public string VoiceGroupStatus
    {
        get => _voiceGroupStatus;
        private set => SetField(ref _voiceGroupStatus, value);
    }

    public string KeySplitStatus
    {
        get => _keySplitStatus;
        private set => SetField(ref _keySplitStatus, value);
    }

    public string DrumSetStatus
    {
        get => _drumSetStatus;
        private set => SetField(ref _drumSetStatus, value);
    }

    public string WaveMemoryStatus
    {
        get => _waveMemoryStatus;
        private set => SetField(ref _waveMemoryStatus, value);
    }

    public string VoiceStatus
    {
        get => _voiceStatus;
        private set => SetField(ref _voiceStatus, value);
    }

    public SongTableEntryRow? SelectedSongTableEntry
    {
        get => _selectedSongTableEntry;
        set
        {
            if (ReferenceEquals(_selectedSongTableEntry, value))
                return;

            if (_selectedSongTableEntry is not null)
                _selectedSongTableEntry.IsSelected = false;
            if (!SetField(ref _selectedSongTableEntry, value))
                return;

            if (_selectedSongTableEntry is not null)
                _selectedSongTableEntry.IsSelected = true;
            if (!_suppressPlaybackSelectionSourceUpdate && _selectedSongTableEntry is not null)
                _playbackSelectionSource = PlaybackSelectionSource.SongTable;
            OnPropertyChanged(nameof(CanEditSelectedSongTableEntry));
            OnPropertyChanged(nameof(CanPlaySequence));
        }
    }

    public bool CanEditSelectedSongTableEntry => SelectedSongTableEntry is not null;

    public SequenceHeaderRow? SelectedSequence
    {
        get => _selectedSequence;
        set
        {
            if (!SetField(ref _selectedSequence, value))
                return;

            foreach (var sequence in Sequences)
                sequence.IsSelected = false;
            if (_selectedSequence is not null)
                _selectedSequence.IsSelected = true;
            if (!_suppressPlaybackSelectionSourceUpdate && _selectedSequence is not null)
                _playbackSelectionSource = PlaybackSelectionSource.SongHeader;
            OnPropertyChanged(nameof(SelectedSequenceMidiFilePath));
            OnPropertyChanged(nameof(CanPlaySequence));
            OnPropertyChanged(nameof(CanEditSelectedSequence));
            SelectVoiceGroupForSequence(value);
        }
    }

    public bool CanEditSelectedSequence => SelectedSequence is not null;

    public string SelectedSequenceMidiFilePath => SelectedSequence?.MidiFilePath ?? string.Empty;

    public void SetPlaybackSelectionSourceFromTabIndex(int tabIndex)
    {
        PlaybackSelectionSource? source = tabIndex switch
        {
            1 when SelectedSongTableEntry is not null => PlaybackSelectionSource.SongTable,
            2 when SelectedSequence is not null => PlaybackSelectionSource.SongHeader,
            _ => null
        };

        if (source is null || _playbackSelectionSource == source.Value)
            return;

        _playbackSelectionSource = source.Value;
        OnPropertyChanged(nameof(CanPlaySequence));
    }

    public VoiceGroupOption? SelectedVoiceGroup
    {
        get => _selectedVoiceGroup;
        set
        {
            if (ReferenceEquals(_selectedVoiceGroup, value))
                return;

            if (_selectedVoiceGroup is not null)
                _selectedVoiceGroup.PropertyChanged -= OnSelectedAssetOptionPropertyChanged;

            if (!SetField(ref _selectedVoiceGroup, value))
                return;

            if (_selectedVoiceGroup is not null)
                _selectedVoiceGroup.PropertyChanged += OnSelectedAssetOptionPropertyChanged;

            RefreshVoices();
            OnPropertyChanged(nameof(CanEditSelectedVoiceGroup));
        }
    }

    public string SelectedVoiceGroupVoiceCountText => SelectedVoiceGroup?.VoiceCount.ToString() ?? "0";
    public bool CanEditSelectedVoiceGroup => SelectedVoiceGroup is not null;

    public KeySplitOption? SelectedKeySplit
    {
        get => _selectedKeySplit;
        set
        {
            if (ReferenceEquals(_selectedKeySplit, value))
                return;

            if (_selectedKeySplit is not null)
                _selectedKeySplit.PropertyChanged -= OnSelectedAssetOptionPropertyChanged;

            if (!SetField(ref _selectedKeySplit, value))
                return;

            if (_selectedKeySplit is not null)
                _selectedKeySplit.PropertyChanged += OnSelectedAssetOptionPropertyChanged;

            RefreshKeySplitRows();
            OnPropertyChanged(nameof(CanEditSelectedKeySplit));
            OnPropertyChanged(nameof(CanEditSelectedKeySplitVoice));
            OnPropertyChanged(nameof(SelectedKeySplitKeyMapHex));
            OnPropertyChanged(nameof(KeySplitRangeLabels));
        }
    }

    public bool CanEditSelectedKeySplit => SelectedKeySplit is not null;
    public bool CanEditSelectedKeySplitVoice => SelectedKeySplitVoice is not null;

    public DrumSetOption? SelectedDrumSet
    {
        get => _selectedDrumSet;
        set
        {
            if (ReferenceEquals(_selectedDrumSet, value))
                return;

            if (_selectedDrumSet is not null)
                _selectedDrumSet.PropertyChanged -= OnSelectedAssetOptionPropertyChanged;

            if (!SetField(ref _selectedDrumSet, value))
                return;

            if (_selectedDrumSet is not null)
                _selectedDrumSet.PropertyChanged += OnSelectedAssetOptionPropertyChanged;

            RefreshDrumSetRows();
            OnPropertyChanged(nameof(CanEditSelectedDrumSet));
            OnPropertyChanged(nameof(CanEditSelectedDrumSetVoice));
        }
    }

    public bool CanEditSelectedDrumSet => SelectedDrumSet is not null;
    public bool CanEditSelectedDrumSetVoice => SelectedDrumSetVoice is not null;

    public WaveMemoryRow? SelectedWaveMemory
    {
        get => _selectedWaveMemory;
        set
        {
            if (ReferenceEquals(_selectedWaveMemory, value))
                return;

            if (_selectedWaveMemory is not null)
            {
                _selectedWaveMemory.IsSelected = false;
                _selectedWaveMemory.PropertyChanged -= OnSelectedWaveMemoryPropertyChanged;
            }

            if (!SetField(ref _selectedWaveMemory, value))
                return;

            if (_selectedWaveMemory is not null)
            {
                _selectedWaveMemory.IsSelected = true;
                _selectedWaveMemory.PropertyChanged += OnSelectedWaveMemoryPropertyChanged;
            }

            OnPropertyChanged(nameof(CanEditSelectedWaveMemory));
        }
    }

    public bool CanEditSelectedWaveMemory => SelectedWaveMemory is not null;

    public WaveDataRow? SelectedWaveData
    {
        get => _selectedWaveData;
        set
        {
            if (ReferenceEquals(_selectedWaveData, value))
                return;

            if (_selectedWaveData is not null)
            {
                _selectedWaveData.IsSelected = false;
                _selectedWaveData.PropertyChanged -= OnSelectedWaveDataPropertyChanged;
            }

            if (!SetField(ref _selectedWaveData, value))
                return;

            if (_selectedWaveData is not null)
            {
                _selectedWaveData.IsSelected = true;
                _selectedWaveData.PropertyChanged += OnSelectedWaveDataPropertyChanged;
            }

            OnPropertyChanged(nameof(CanEditSelectedWaveData));
        }
    }

    public bool CanEditSelectedWaveData => SelectedWaveData is not null;

    public VoiceRow? SelectedKeySplitVoice
    {
        get => _selectedKeySplitVoice;
        set
        {
            if (!SetField(ref _selectedKeySplitVoice, value))
                return;

            OnPropertyChanged(nameof(CanEditSelectedKeySplitVoice));

            if (value is not null)
            {
                if (_selectedKeySplitRegionIndex != value.Index)
                {
                    _selectedKeySplitRegionIndex = value.Index;
                    OnPropertyChanged(nameof(SelectedKeySplitRegionIndex));
                }

                SelectedVoice = value;
            }
            else if (_selectedKeySplitRegionIndex != -1)
            {
                _selectedKeySplitRegionIndex = -1;
                OnPropertyChanged(nameof(SelectedKeySplitRegionIndex));
            }
        }
    }

    public int SelectedKeySplitRegionIndex
    {
        get => _selectedKeySplitRegionIndex;
        set
        {
            int index = Math.Clamp(value, -1, 127);
            if (!SetField(ref _selectedKeySplitRegionIndex, index))
                return;

            VoiceRow? row = KeySplitRows.FirstOrDefault(r => r.Index == index);
            if (row is not null && !ReferenceEquals(row, SelectedKeySplitVoice))
                SelectedKeySplitVoice = row;
        }
    }

    public string SelectedKeySplitKeyMapHex
    {
        get => SelectedKeySplit?.KeySplit.KeyMapHex ?? string.Empty;
        set
        {
            if (SelectedKeySplit is null)
                return;

            KeySplitProjectInfo keySplit = SelectedKeySplit.KeySplit;
            string oldValue = keySplit.KeyMapHex;
            byte[] keyMap = DecodeVoiceTableHex(value ?? string.Empty);
            string newValue = EncodeVoiceTableHex(keyMap);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                return;

            keySplit.KeyMapHex = newValue;
            RefreshKeySplitUsage();
            OnPropertyChanged(nameof(SelectedKeySplitKeyMapHex));
            RecordProjectValueEdit(
                keySplit,
                nameof(KeySplitProjectInfo.KeyMapHex),
                "Edit KeySplit range",
                oldValue,
                newValue,
                map => keySplit.KeyMapHex = map,
                allowMerge: true);
        }
    }

    public IReadOnlyList<string> KeySplitRangeLabels => KeySplitRows
        .Select(row =>
        {
            string label = string.IsNullOrWhiteSpace(row.Label) ? row.DataDisplay : row.Label;
            return $"{row.IndexText} {label}";
        })
        .ToArray();

    public VoiceRow? SelectedDrumSetVoice
    {
        get => _selectedDrumSetVoice;
        set
        {
            if (!SetField(ref _selectedDrumSetVoice, value))
                return;

            OnPropertyChanged(nameof(CanEditSelectedDrumSetVoice));

            RefreshSelectedDrumSetKeyMarkers();
            if (value is not null)
                SelectedVoice = value;
        }
    }

    public VoiceRow? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (ReferenceEquals(_selectedVoice, value))
                return;

            if (_selectedVoice is not null)
            {
                _selectedVoice.PropertyChanged -= OnSelectedVoicePropertyChanged;
                _selectedVoice.IsSelected = false;
            }
            SetField(ref _selectedVoice, value);
            if (_selectedVoice is not null)
            {
                _selectedVoice.PropertyChanged += OnSelectedVoicePropertyChanged;
                _selectedVoice.IsSelected = true;
            }
            RefreshSelectedVoiceKeyMarkers();
            RefreshSelectedDrumSetKeyMarkers();
            OnPropertyChanged(nameof(CanEditSelectedVoice));
        }
    }

    public bool CanEditSelectedVoice => SelectedVoice is not null;
    public IReadOnlyList<int> SelectedVoiceKeyMarkers
    {
        get => _selectedVoiceKeyMarkers;
        private set => SetField(ref _selectedVoiceKeyMarkers, value);
    }

    public IReadOnlyList<int> SelectedDrumSetKeyMarkers
    {
        get => _selectedDrumSetKeyMarkers;
        private set => SetField(ref _selectedDrumSetKeyMarkers, value);
    }

    public async Task<bool> LoadRomAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            _loadedRom = await GbaRom.LoadAsync(stream, file.Path?.LocalPath ?? file.Name);
            RomStatus = $"Loaded ROM: {file.Name} ({_loadedRom.Length:N0} bytes, code {_loadedRom.GameCode})";
            SongTableStatus = "Create a project to inspect the MP2K song table.";
            SequenceStatus = "Create a project to inspect MP2K song headers.";
            VoiceGroupStatus = "Create a project to inspect MP2K voice groups.";
            KeySplitStatus = "Create a project to inspect MP2K key splits.";
            DrumSetStatus = "Create a project to inspect MP2K drum sets.";
            WaveMemoryStatus = "Create a project to inspect MP2K wave memory.";
            VoiceStatus = "Create a project to inspect DirectSound voices.";
            SongTableEntries.Clear();
            SongHeaderFileOptions.Clear();
            Sequences.Clear();
            VoiceGroupOptions.Clear();
            KeySplitOptions.Clear();
            DrumSetOptions.Clear();
            WaveMemoryRows.Clear();
            WaveDataRows.Clear();
            Voices.Clear();
            KeySplitRows.Clear();
            DrumSetRows.Clear();
            SelectedSongTableEntry = null;
            SelectedSequence = null;
            SelectedVoiceGroup = null;
            SelectedKeySplit = null;
            SelectedDrumSet = null;
            SelectedWaveMemory = null;
            SelectedWaveData = null;
            CloseProjectSession();
            StopAllPreviewNotes();
            OnPropertyChanged(nameof(LoadedRom));
            return true;
        }
        catch (Exception ex)
        {
            _loadedRom = null;
            RomStatus = $"Failed to load ROM: {ex.Message}";
            SongTableStatus = "No song table data loaded.";
            SequenceStatus = "No sequence data loaded.";
            VoiceGroupStatus = "No voice group data loaded.";
            KeySplitStatus = "No key split data loaded.";
            DrumSetStatus = "No drum set data loaded.";
            WaveMemoryStatus = "No wave memory data loaded.";
            VoiceStatus = "No voice data loaded.";
            SongTableEntries.Clear();
            SongHeaderFileOptions.Clear();
            Sequences.Clear();
            VoiceGroupOptions.Clear();
            KeySplitOptions.Clear();
            DrumSetOptions.Clear();
            WaveMemoryRows.Clear();
            WaveDataRows.Clear();
            Voices.Clear();
            KeySplitRows.Clear();
            DrumSetRows.Clear();
            SelectedSongTableEntry = null;
            SelectedSequence = null;
            SelectedVoiceGroup = null;
            SelectedKeySplit = null;
            SelectedDrumSet = null;
            SelectedWaveMemory = null;
            SelectedWaveData = null;
            SelectedWaveMemory = null;
            CloseProjectSession();
            StopAllPreviewNotes();
            OnPropertyChanged(nameof(LoadedRom));
            return false;
        }
    }

    public async Task<bool> CreateProjectFileAsync(string outputPath, Mp2kImportOptions importOptions)
    {
        if (_loadedRom is null)
        {
            RomStatus = "Project creation failed: no ROM is loaded.";
            return false;
        }

        try
        {
            RomStatus = "Creating project file...";
            SongTableStatus = "Extracting song table...";
            SequenceStatus = "Extracting song headers and MIDI files...";
            VoiceGroupStatus = "Extracting voice groups...";
            KeySplitStatus = "Extracting key splits...";
            DrumSetStatus = "Extracting drum sets...";
            WaveMemoryStatus = "Extracting wave memory...";
            VoiceStatus = "Extracting DirectSound voices...";
            var rom = _loadedRom;
            var result = await Task.Run(() =>
            {
                var created = AgbSynthProjectExporter.CreateFromRom(rom, importOptions);
                int voiceGroupAssetCount = AgbSynthProjectVoiceGroupExporter.ExportVoiceGroups(rom, created, outputPath);
                int midiCount = AgbSynthProjectMidiExporter.ExportMidiFiles(rom, created, outputPath, _midiCcMapping);
                int sequenceAssetCount = AgbSynthProjectSequenceExporter.ExportSongTableAndHeaders(created, outputPath);
                AgbSynthProjectExporter.Save(outputPath, created);
                return (Project: created, MidiCount: midiCount, VoiceGroupAssetCount: voiceGroupAssetCount, SequenceAssetCount: sequenceAssetCount);
            });
            BeginProjectSession(result.Project, outputPath);
            LoadSongTable(result.Project);
            LoadSequences(result.Project);
            LoadVoiceGroups(result.Project);
            LoadVoiceTables(result.Project);
            LoadWaveMemory(result.Project);
            LoadWaveData(result.Project);
            CompleteProjectSession();
            RomStatus = $"Project created: {Path.GetFileName(outputPath)} ({result.Project.SongTable.ValidEntryCount:N0} song table entries)";
            SongTableStatus = $"Loaded {result.Project.Songs.Count:N0} entries. Exported {result.Project.SongTable.FilePath}.";
            SequenceStatus = $"Loaded {result.Project.SongHeaders.Count:N0} headers. Exported {result.MidiCount:N0} MIDI files.";
            VoiceGroupStatus = $"Loaded {result.Project.VoiceGroups.Count:N0} voice groups. Exported {result.VoiceGroupAssetCount:N0} voice assets.";
            KeySplitStatus = $"Loaded {KeySplitOptions.Count:N0} key splits.";
            DrumSetStatus = $"Loaded {DrumSetOptions.Count:N0} drum sets.";
            WaveMemoryStatus = $"Loaded {WaveMemoryRows.Count:N0} wave memory files.";
            VoiceStatus = $"Loaded {WaveDataRows.Count:N0} voice files.";
            return true;
        }
        catch (Exception ex)
        {
            RomStatus = $"Project creation failed: {ex.Message}";
            SongTableStatus = "SongTable extraction failed.";
            SequenceStatus = "Sequence extraction failed.";
            VoiceGroupStatus = "VoiceGroup extraction failed.";
            KeySplitStatus = "KeySplit extraction failed.";
            DrumSetStatus = "DrumSet extraction failed.";
            WaveMemoryStatus = "WaveMemory extraction failed.";
            VoiceStatus = "Voice extraction failed.";
            return false;
        }
    }

    public async Task<bool> CreateBlankProjectAsync(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return false;

        try
        {
            RomStatus = "Creating new project...";
            SongTableStatus = "Creating empty song table...";
            SequenceStatus = "Creating song header folder...";
            VoiceGroupStatus = "Creating voice group folder...";
            KeySplitStatus = "Creating key split folder...";
            DrumSetStatus = "Creating drum set folder...";
            WaveMemoryStatus = "Creating wave memory folder...";
            VoiceStatus = "Creating DirectSound voice folder...";

            var project = await Task.Run(() => AgbSynthProjectExporter.CreateBlankProject(outputPath));
            await StopSelectedSequencePlaybackAsync();
            StopAllPreviewNotes();
            _loadedRom = null;
            OnPropertyChanged(nameof(LoadedRom));
            BeginProjectSession(project, outputPath);

            LoadSongTable(project);
            LoadSequences(project);
            LoadVoiceGroups(project);
            LoadVoiceTables(project);
            LoadWaveMemory(project);
            LoadWaveData(project);
            CompleteProjectSession();

            string projectName = Path.GetFileName(outputPath);
            RomStatus = $"New project created: {projectName}";
            SongTableStatus = $"Loaded an empty song table from {project.SongTable.FilePath}.";
            SequenceStatus = "No song headers. Add one to begin editing a sequence.";
            VoiceGroupStatus = "No voice groups. Create one to begin editing instruments.";
            KeySplitStatus = "No key splits.";
            DrumSetStatus = "No drum sets.";
            WaveMemoryStatus = "No wave memory files.";
            VoiceStatus = "No DirectSound voice files.";
            return true;
        }
        catch (Exception ex)
        {
            RomStatus = $"New project creation failed: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> LoadProjectFileAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        try
        {
            RomStatus = "Opening project file...";
            SongTableStatus = "Loading song table...";
            SequenceStatus = "Loading song headers...";
            VoiceGroupStatus = "Loading voice groups...";
            KeySplitStatus = "Loading key splits...";
            DrumSetStatus = "Loading drum sets...";
            WaveMemoryStatus = "Loading wave memory...";
            VoiceStatus = "Loading DirectSound voices...";

            var project = await Task.Run(() => AgbSynthProjectLoader.Load(projectPath));
            _loadedRom = null;
            OnPropertyChanged(nameof(LoadedRom));
            BeginProjectSession(project, projectPath);

            LoadSongTable(project);
            LoadSequences(project);
            LoadVoiceGroups(project);
            LoadVoiceTables(project);
            LoadWaveMemory(project);
            LoadWaveData(project);
            CompleteProjectSession();

            string projectName = Path.GetFileName(projectPath);
            RomStatus = $"Project opened: {projectName}";
            SongTableStatus = $"Loaded {project.Songs.Count:N0} entries from {project.SongTable.FilePath}.";
            SequenceStatus = $"Loaded {project.SongHeaders.Count:N0} headers.";
            VoiceGroupStatus = $"Loaded {project.VoiceGroups.Count:N0} voice groups from folder.";
            KeySplitStatus = $"Loaded {KeySplitOptions.Count:N0} key splits from folder and voice groups.";
            DrumSetStatus = $"Loaded {DrumSetOptions.Count:N0} drum sets from folder and voice groups.";
            WaveMemoryStatus = $"Loaded {WaveMemoryRows.Count:N0} wave memory files from folder.";
            VoiceStatus = $"Loaded {WaveDataRows.Count:N0} voice files from folder.";
            return true;
        }
        catch (Exception ex)
        {
            RomStatus = $"Project open failed: {ex.Message}";
            SongTableStatus = "SongTable load failed.";
            SequenceStatus = "Sequence load failed.";
            VoiceGroupStatus = "VoiceGroup load failed.";
            KeySplitStatus = "KeySplit load failed.";
            DrumSetStatus = "DrumSet load failed.";
            WaveMemoryStatus = "WaveMemory load failed.";
            VoiceStatus = "Voice load failed.";
            return false;
        }
    }

    public Task<bool> RefreshProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            RomStatus = "Refresh failed: no project is open.";
            return Task.FromResult(false);
        }

        return LoadProjectFileAsync(_currentProjectPath);
    }

    private void LoadSongTable(AgbSynthProjectFile project)
    {
        SongTableEntries.Clear();
        SongHeaderFileOptions.Clear();
        _songHeaderDisplayByPath.Clear();
        foreach (var header in project.SongHeaders)
        {
            AddSongHeaderFileOption(header.FilePath, header.Label);
        }

        foreach (var song in project.Songs)
        {
            string songHeaderFilePath = song.SongHeaderFilePath;
            if (string.IsNullOrWhiteSpace(songHeaderFilePath))
            {
                songHeaderFilePath = project.SongHeaders.FirstOrDefault(h => h.SongId == song.SongId)?.FilePath
                    ?? project.SongHeaders.FirstOrDefault(h => h.HeaderOffset == song.HeaderOffset)?.FilePath
                    ?? string.Empty;
            }

            AddSongHeaderFileOption(songHeaderFilePath, string.Empty);

            SongTableEntries.Add(SongTableEntryRow.FromProjectInfo(song, songHeaderFilePath, GetSongHeaderDisplay(songHeaderFilePath)));
        }
        ReindexSongTableEntries();
        SelectedSongTableEntry = SongTableEntries.FirstOrDefault();
    }

    public void MoveSelectedSongTableEntryUp() => MoveSongTableEntryUp(SelectedSongTableEntry);

    public void MoveSelectedSongTableEntryDown() => MoveSongTableEntryDown(SelectedSongTableEntry);

    public void InsertSongTableEntryBelowSelected(bool copySelected)
    {
        if (SelectedSongTableEntry is not { } selected)
        {
            AddSongTableEntryToEnd();
            return;
        }

        InsertSongTableEntry(SongTableEntries.IndexOf(selected) + 1, copySelected ? selected.CloneForInsert() : CreateBlankSongTableEntry());
    }

    public void DeleteSelectedSongTableEntry() => DeleteSongTableEntry(SelectedSongTableEntry);

    public void AddSongTableEntryToEnd()
    {
        InsertSongTableEntry(SongTableEntries.Count, CreateBlankSongTableEntry());
    }

    public void MoveSongTableEntryUp(SongTableEntryRow? row)
    {
        if (row is null)
            return;

        int index = SongTableEntries.IndexOf(row);
        if (index <= 0)
            return;

        SongTableEntries.Move(index, index - 1);
        ReindexSongTableEntries();
        SelectedSongTableEntry = row;
    }

    public void MoveSongTableEntryDown(SongTableEntryRow? row)
    {
        if (row is null)
            return;

        int index = SongTableEntries.IndexOf(row);
        if (index < 0 || index >= SongTableEntries.Count - 1)
            return;

        SongTableEntries.Move(index, index + 1);
        ReindexSongTableEntries();
        SelectedSongTableEntry = row;
    }

    public void InsertSongTableEntryAbove(SongTableEntryRow? row)
    {
        int index = row is null ? SongTableEntries.Count : SongTableEntries.IndexOf(row);
        InsertSongTableEntry(index < 0 ? SongTableEntries.Count : index, CreateBlankSongTableEntry());
    }

    public void InsertSongTableEntryBelow(SongTableEntryRow? row)
    {
        int index = row is null ? SongTableEntries.Count : SongTableEntries.IndexOf(row) + 1;
        InsertSongTableEntry(index <= 0 ? SongTableEntries.Count : index, CreateBlankSongTableEntry());
    }

    public void CopySongTableEntry(SongTableEntryRow? row)
    {
        if (row is not null)
            _copiedSongTableEntry = row.CloneForInsert();
    }

    public void PasteSongTableEntryBelow(SongTableEntryRow? row)
    {
        if (_copiedSongTableEntry is null)
            return;

        int index = row is null ? SongTableEntries.Count : SongTableEntries.IndexOf(row) + 1;
        InsertSongTableEntry(index <= 0 ? SongTableEntries.Count : index, _copiedSongTableEntry.CloneForInsert());
    }

    public void DeleteSongTableEntry(SongTableEntryRow? row)
    {
        if (row is null)
            return;

        int index = SongTableEntries.IndexOf(row);
        if (index < 0)
            return;

        SongTableEntries.RemoveAt(index);
        ReindexSongTableEntries();
        SelectedSongTableEntry = SongTableEntries.Count == 0
            ? null
            : SongTableEntries[Math.Clamp(index, 0, SongTableEntries.Count - 1)];
    }

    public void MoveSongTableEntryBefore(SongTableEntryRow? source, SongTableEntryRow? target)
    {
        if (source is null || target is null || ReferenceEquals(source, target))
            return;

        int oldIndex = SongTableEntries.IndexOf(source);
        int newIndex = SongTableEntries.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0)
            return;

        SongTableEntries.Move(oldIndex, newIndex);
        ReindexSongTableEntries();
        SelectedSongTableEntry = source;
    }

    private SongTableEntryRow? _copiedSongTableEntry;
    private SequenceHeaderRow? _copiedSequence;
    private WaveMemoryRow? _copiedWaveMemory;
    private WaveDataRow? _copiedWaveData;

    private void InsertSongTableEntry(int index, SongTableEntryRow row)
    {
        int targetIndex = Math.Clamp(index, 0, SongTableEntries.Count);
        SongTableEntries.Insert(targetIndex, row);
        ReindexSongTableEntries();
        SelectedSongTableEntry = row;
    }

    private SongTableEntryRow CreateBlankSongTableEntry()
    {
        string songHeaderFilePath = SongHeaderFileOptions.FirstOrDefault() ?? string.Empty;
        return new SongTableEntryRow
        {
            SongHeaderFilePath = songHeaderFilePath,
            SongHeaderDisplay = GetSongHeaderDisplay(songHeaderFilePath)
        };
    }

    public void InsertSequenceBelowSelected(bool copySelected, string assetPath)
    {
        SequenceHeaderRow? selected = SelectedSequence;
        int index = selected is null ? Sequences.Count : Sequences.IndexOf(selected) + 1;
        var row = copySelected && selected is not null
            ? selected.CloneForInsert(GetNextSequenceSongId())
            : CreateBlankSequence();
        SaveAndInsertSequence(index, row, assetPath);
    }

    public void DeleteSelectedSequence() => DeleteSequence(SelectedSequence);

    public void AddSequenceToEnd(string assetPath)
    {
        SaveAndInsertSequence(Sequences.Count, CreateBlankSequence(), assetPath);
    }

    public void InsertSequenceAbove(SequenceHeaderRow? row, string assetPath)
    {
        int index = row is null ? Sequences.Count : Sequences.IndexOf(row);
        SaveAndInsertSequence(index < 0 ? Sequences.Count : index, CreateBlankSequence(), assetPath);
    }

    public void InsertSequenceBelow(SequenceHeaderRow? row, string assetPath)
    {
        int index = row is null ? Sequences.Count : Sequences.IndexOf(row) + 1;
        SaveAndInsertSequence(index <= 0 ? Sequences.Count : index, CreateBlankSequence(), assetPath);
    }

    public void CopySequence(SequenceHeaderRow? row)
    {
        if (row is not null)
            _copiedSequence = row.CloneForInsert(GetNextSequenceSongId());
    }

    public void PasteSequenceBelow(SequenceHeaderRow? row, string assetPath)
    {
        if (_copiedSequence is null)
            return;

        int index = row is null ? Sequences.Count : Sequences.IndexOf(row) + 1;
        SaveAndInsertSequence(index <= 0 ? Sequences.Count : index, _copiedSequence.CloneForInsert(GetNextSequenceSongId()), assetPath);
    }

    public void DeleteSequence(SequenceHeaderRow? row)
    {
        if (row is null)
            return;

        int index = Sequences.IndexOf(row);
        if (index < 0)
            return;

        Sequences.RemoveAt(index);
        SelectedSequence = Sequences.Count == 0
            ? null
            : Sequences[Math.Clamp(index, 0, Sequences.Count - 1)];
    }

    private void InsertSequence(int index, SequenceHeaderRow row)
    {
        int targetIndex = Math.Clamp(index, 0, Sequences.Count);
        Sequences.Insert(targetIndex, row);
        SelectedSequence = row;
    }

    private void SaveAndInsertSequence(int index, SequenceHeaderRow row, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        try
        {
            row.FilePath = ToProjectRelativePath(assetPath);
            row.Label = Path.GetFileNameWithoutExtension(assetPath);
            AgbSynthProjectAssetWriter.SaveSongHeader(assetPath, row.ToProjectInfo());
            InsertSequence(index, row);
            AddSongHeaderFileOption(row.FilePath, row.Label);
            SequenceStatus = $"Created SongHeader {Path.GetFileName(assetPath)}.";
        }
        catch (Exception ex)
        {
            SequenceStatus = $"SongHeader creation failed: {ex.Message}";
        }
    }

    private SequenceHeaderRow CreateBlankSequence()
    {
        string voiceGroupFilePath = SelectedVoiceGroup?.FilePath ?? VoiceGroupOptions.FirstOrDefault()?.FilePath ?? string.Empty;
        return new SequenceHeaderRow
        {
            SongId = GetNextSequenceSongId(),
            Label = $"songheader_{GetNextSequenceSongId():D3}",
            VoiceGroupFilePath = voiceGroupFilePath
        };
    }

    private int GetNextSequenceSongId()
    {
        return Sequences.Count == 0 ? 0 : Sequences.Max(s => s.SongId) + 1;
    }

    public void SetSongTableEntrySongHeaderFile(SongTableEntryRow row, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        string relativePath = ToProjectRelativePath(selectedPath);
        string label = TryReadSongHeaderLabel(selectedPath);
        AddSongHeaderFileOption(relativePath, label);
        row.SongHeaderFilePath = relativePath;
        row.SongHeaderDisplay = GetSongHeaderDisplay(relativePath);
    }

    public string? GetSongHeaderDirectoryPath()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return null;

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        string? existingHeaderPath = SongHeaderFileOptions
            .Select(path => Path.Combine(projectDirectory, path.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (!string.IsNullOrWhiteSpace(existingHeaderPath))
            return Path.GetDirectoryName(existingHeaderPath);

        string projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);
        string defaultHeaderDirectory = Path.Combine(projectDirectory, $"{projectName}_data", "songheader");
        return Directory.Exists(defaultHeaderDirectory) ? defaultHeaderDirectory : null;
    }

    public string? GetMidiDirectoryPath()
    {
        return GetProjectAssetDirectoryPath("midi", Sequences.Select(s => s.MidiFilePath));
    }

    public string? GetVoiceGroupDirectoryPath()
    {
        return GetProjectAssetDirectoryPath("voicegroup", VoiceGroupOptions.Select(v => v.FilePath));
    }

    public string? GetKeySplitDirectoryPath() => GetProjectAssetDirectoryPath("keysplit", KeySplitOptions.Select(v => v.FilePath));

    public string? GetDrumSetDirectoryPath() => GetProjectAssetDirectoryPath("drumset", DrumSetOptions.Select(v => v.FilePath));

    public string? GetWaveMemoryDirectoryPath() => GetProjectAssetDirectoryPath("wavememory", WaveMemoryRows.Select(v => v.FilePath));

    public string? GetWaveDataDirectoryPath() => GetProjectAssetDirectoryPath("wavedata", WaveDataRows.Select(v => v.FilePath));

    public string GetSuggestedSongHeaderFileName() => $"songheader_{GetNextSequenceSongId():D3}.agbsh";

    public string GetSuggestedVoiceGroupFileName() => $"voicegroup_{GetNextVoiceGroupId():D3}.agbvg";

    public string GetSuggestedKeySplitFileName() => $"keysplit_{GetNextKeySplitId():D3}.agbks";

    public string GetSuggestedDrumSetFileName() => $"drumset_{GetNextDrumSetId():D3}.agbds";

    public string GetSuggestedWaveMemoryFileName() => $"wavememory_{GetNextWaveMemoryId():D3}.agbwm";

    public string GetSuggestedWaveDataFileName() => $"wavedata_{GetNextWaveDataId():D3}.agbwd";

    public void SetSequenceMidiFile(SequenceHeaderRow row, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        row.MidiFilePath = ToProjectRelativePath(selectedPath);
        row.TrackCount = DetectMidiTrackCount(selectedPath);
        if (SelectedSequence == row)
            OnPropertyChanged(nameof(SelectedSequenceMidiFilePath));
    }

    public void SetSequenceVoiceGroupFile(SequenceHeaderRow row, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        string relativePath = ToProjectRelativePath(selectedPath);
        row.VoiceGroupFilePath = relativePath;
        VoiceGroupOption? voiceGroup = VoiceGroupOptions.FirstOrDefault(v =>
            string.Equals(v.FilePath, relativePath, StringComparison.OrdinalIgnoreCase));
        row.VoiceGroupDisplay = voiceGroup?.FileDisplay ?? FormatAssetDisplay(string.Empty, relativePath);
        SelectedVoiceGroup = voiceGroup ?? SelectedVoiceGroup;
    }

    public void AddVoiceGroup(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextVoiceGroupId();
        string label = Path.GetFileNameWithoutExtension(assetPath);
        var source = new VoiceGroupProjectInfo
        {
            Id = id,
            Label = label,
            FilePath = ToProjectRelativePath(assetPath),
            DiscoverySource = "User",
            Voices = CreateBlankVoiceGroupVoices()
        };
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveVoiceGroup(assetPath, source), out string? error))
        {
            VoiceGroupStatus = $"VoiceGroup creation failed: {error}";
            return;
        }
        var voiceGroup = VoiceGroupOption.FromProjectInfo(source);
        VoiceGroupOptions.Add(voiceGroup);
        SelectedVoiceGroup = voiceGroup;
        RefreshSequenceVoiceGroupDisplays();
        VoiceGroupStatus = $"Created VoiceGroup {Path.GetFileName(assetPath)}.";
    }

    public void DuplicateSelectedVoiceGroup(string assetPath)
    {
        if (SelectedVoiceGroup is not { } selected || string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextVoiceGroupId();
        var source = new VoiceGroupProjectInfo
        {
            Id = id,
            Label = Path.GetFileNameWithoutExtension(assetPath),
            FilePath = ToProjectRelativePath(assetPath),
            DiscoverySource = "User",
            Voices = selected.Voices.Select(CloneVoiceProjectInfo).ToList()
        };
        ReindexVoices(source.Voices);
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveVoiceGroup(assetPath, source), out string? error))
        {
            VoiceGroupStatus = $"VoiceGroup copy failed: {error}";
            return;
        }
        var voiceGroup = VoiceGroupOption.FromProjectInfo(source);
        VoiceGroupOptions.Add(voiceGroup);
        SelectedVoiceGroup = voiceGroup;
        RefreshSequenceVoiceGroupDisplays();
        VoiceGroupStatus = $"Created VoiceGroup copy {Path.GetFileName(assetPath)}.";
    }

    public void DeleteSelectedVoiceGroup()
    {
        if (SelectedVoiceGroup is not { } selected)
            return;

        int index = VoiceGroupOptions.IndexOf(selected);
        if (index < 0)
            return;

        VoiceGroupOptions.RemoveAt(index);
        SelectedVoiceGroup = VoiceGroupOptions.Count == 0
            ? null
            : VoiceGroupOptions[Math.Clamp(index, 0, VoiceGroupOptions.Count - 1)];
        VoiceGroupStatus = $"Deleted VoiceGroup {selected.Id:D3}.";
    }

    public void AddKeySplit(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextKeySplitId();
        string label = Path.GetFileNameWithoutExtension(assetPath);
        var asset = new KeySplitAssetProjectInfo
        {
            Id = id,
            Label = label,
            FilePath = ToProjectRelativePath(assetPath),
            VoiceGroupId = -1,
            ParentVoiceIndex = -1,
            KeySplit = CreateBlankKeySplit(label)
        };
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveKeySplit(assetPath, asset), out string? error))
        {
            KeySplitStatus = $"KeySplit creation failed: {error}";
            return;
        }
        var option = KeySplitOption.FromProjectInfo(asset);
        KeySplitOptions.Add(option);
        SelectedKeySplit = option;
        RefreshVoiceDataDisplays();
        KeySplitStatus = $"Created KeySplit {Path.GetFileName(assetPath)}.";
    }

    public void DuplicateSelectedKeySplit(string assetPath)
    {
        if (SelectedKeySplit is not { } selected || string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextKeySplitId();
        string label = Path.GetFileNameWithoutExtension(assetPath);
        var asset = new KeySplitAssetProjectInfo
        {
            Id = id,
            Label = label,
            FilePath = ToProjectRelativePath(assetPath),
            VoiceGroupId = -1,
            ParentVoiceIndex = -1,
            KeySplit = CloneKeySplitProjectInfo(selected.KeySplit)
        };
        asset.KeySplit.Label = label;
        ReindexVoices(asset.KeySplit.Regions);
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveKeySplit(assetPath, asset), out string? error))
        {
            KeySplitStatus = $"KeySplit copy failed: {error}";
            return;
        }
        var option = KeySplitOption.FromProjectInfo(asset);
        KeySplitOptions.Add(option);
        SelectedKeySplit = option;
        RefreshVoiceDataDisplays();
        KeySplitStatus = $"Created KeySplit copy {Path.GetFileName(assetPath)}.";
    }

    public void DeleteSelectedKeySplit()
    {
        if (SelectedKeySplit is not { } selected)
            return;

        int index = KeySplitOptions.IndexOf(selected);
        if (index < 0)
            return;

        KeySplitOptions.RemoveAt(index);
        SelectedKeySplit = KeySplitOptions.Count == 0
            ? null
            : KeySplitOptions[Math.Clamp(index, 0, KeySplitOptions.Count - 1)];
        KeySplitStatus = $"Deleted KeySplit {selected.Id:D3}.";
    }

    public void AddDrumSet(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextDrumSetId();
        string label = Path.GetFileNameWithoutExtension(assetPath);
        var asset = new DrumSetAssetProjectInfo
        {
            Id = id,
            Label = label,
            FilePath = ToProjectRelativePath(assetPath),
            VoiceGroupId = -1,
            ParentVoiceIndex = -1,
            DrumSet = CreateBlankDrumSet(label)
        };
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveDrumSet(assetPath, asset), out string? error))
        {
            DrumSetStatus = $"DrumSet creation failed: {error}";
            return;
        }
        var option = DrumSetOption.FromProjectInfo(asset);
        DrumSetOptions.Add(option);
        SelectedDrumSet = option;
        RefreshVoiceDataDisplays();
        DrumSetStatus = $"Created DrumSet {Path.GetFileName(assetPath)}.";
    }

    public void DuplicateSelectedDrumSet(string assetPath)
    {
        if (SelectedDrumSet is not { } selected || string.IsNullOrWhiteSpace(assetPath))
            return;

        int id = GetNextDrumSetId();
        string label = Path.GetFileNameWithoutExtension(assetPath);
        var asset = new DrumSetAssetProjectInfo
        {
            Id = id,
            Label = label,
            FilePath = ToProjectRelativePath(assetPath),
            VoiceGroupId = -1,
            ParentVoiceIndex = -1,
            DrumSet = CloneDrumSetProjectInfo(selected.DrumSet)
        };
        asset.DrumSet.Label = label;
        EnsureDrumSetEntryCount(asset.DrumSet);
        if (!TryWriteAsset(() => AgbSynthProjectAssetWriter.SaveDrumSet(assetPath, asset), out string? error))
        {
            DrumSetStatus = $"DrumSet copy failed: {error}";
            return;
        }
        var option = DrumSetOption.FromProjectInfo(asset);
        DrumSetOptions.Add(option);
        SelectedDrumSet = option;
        RefreshVoiceDataDisplays();
        DrumSetStatus = $"Created DrumSet copy {Path.GetFileName(assetPath)}.";
    }

    private static bool TryWriteAsset(Action write, out string? error)
    {
        try
        {
            write();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void DeleteSelectedDrumSet()
    {
        if (SelectedDrumSet is not { } selected)
            return;

        int index = DrumSetOptions.IndexOf(selected);
        if (index < 0)
            return;

        DrumSetOptions.RemoveAt(index);
        SelectedDrumSet = DrumSetOptions.Count == 0
            ? null
            : DrumSetOptions[Math.Clamp(index, 0, DrumSetOptions.Count - 1)];
        DrumSetStatus = $"Deleted DrumSet {selected.Id:D3}.";
    }

    public void MoveSelectedVoiceUp()
    {
        MoveSelectedVoice(-1);
    }

    public void MoveSelectedVoiceDown()
    {
        MoveSelectedVoice(1);
    }

    public void MoveVoiceUp(VoiceRow? row) => MoveVoice(row, -1);

    public void MoveVoiceDown(VoiceRow? row) => MoveVoice(row, 1);
    public void InsertKeySplitRegionBelowSelected(bool copySelected)
    {
        InsertKeySplitRegion(SelectedKeySplitVoice, insertBelow: true, copySelected ? SelectedKeySplitVoice?.ToProjectInfo() : null);
    }

    public void InsertKeySplitRegionAbove(VoiceRow? row)
    {
        InsertKeySplitRegion(row, insertBelow: false, voiceToInsert: null);
    }

    public void InsertKeySplitRegionBelow(VoiceRow? row)
    {
        InsertKeySplitRegion(row, insertBelow: true, voiceToInsert: null);
    }

    public void CopyKeySplitRegion(VoiceRow? row)
    {
        CopyVoice(row);
    }

    public void PasteKeySplitRegionBelow(VoiceRow? row)
    {
        if (_copiedVoice is null)
            return;

        InsertKeySplitRegion(row, insertBelow: true, _copiedVoice);
    }

    public void DeleteSelectedKeySplitRegion()
    {
        DeleteKeySplitRegion(SelectedKeySplitVoice);
    }

    public void DeleteKeySplitRegion(VoiceRow? row)
    {
        if (SelectedKeySplit is not { } selected || row is null)
            return;

        int index = KeySplitRows.IndexOf(row);
        if (index < 0 || index >= selected.KeySplit.Regions.Count)
            return;

        KeySplitProjectInfo oldValue = CloneKeySplitProjectInfo(selected.KeySplit);
        int oldCount = selected.KeySplit.Regions.Count;
        byte[] keyMap = EnsureKeySplitKeyMap(selected.KeySplit, oldCount, createIfEmpty: false);
        selected.KeySplit.Regions.RemoveAt(index);

        if (oldCount <= 1 || keyMap.Length == 0)
        {
            selected.KeySplit.KeyMapHex = string.Empty;
        }
        else
        {
            RemapKeySplitMapForDelete(keyMap, oldCount, index);
            selected.KeySplit.KeyMapHex = EncodeVoiceTableHex(keyMap);
        }

        ReindexVoices(selected.KeySplit.Regions);
        RefreshKeySplitRows();
        if (KeySplitRows.Count == 0)
        {
            SelectedKeySplitVoice = null;
            if (ReferenceEquals(SelectedVoice, row))
                SelectedVoice = null;
        }
        else
        {
            SelectedKeySplitVoice = KeySplitRows[Math.Clamp(index, 0, KeySplitRows.Count - 1)];
        }

        KeySplitStatus = $"Deleted KeySplit region {index:D3}.";
        RecordKeySplitStateEdit(selected.KeySplit, oldValue, "Delete KeySplit region");
    }

    public void SelectPreviousVoice()
    {
        if (SelectedVoice is null || !TryGetVoiceTable(SelectedVoice, out var table))
            return;

        int index = table.Rows.IndexOf(SelectedVoice);
        if (index > 0)
            SelectVoiceInTable(table, table.Rows[index - 1]);
    }

    public void SelectNextVoice()
    {
        if (SelectedVoice is null || !TryGetVoiceTable(SelectedVoice, out var table))
            return;

        int index = table.Rows.IndexOf(SelectedVoice);
        if (index >= 0 && index < table.Rows.Count - 1)
            SelectVoiceInTable(table, table.Rows[index + 1]);
    }

    public void CopyVoice(VoiceRow? row)
    {
        if (row is null)
            return;

        _copiedVoice = CloneVoiceProjectInfo(row.ToProjectInfo());
        SetVoiceTableStatus(row, $"Copied Voice {row.Index:D3}.");
    }

    public void PasteVoice(VoiceRow? row)
    {
        if (row is null || _copiedVoice is null)
            return;

        VoiceProjectInfo oldValue = CloneVoiceProjectInfo(row.ToProjectInfo());
        var copy = CloneVoiceProjectInfo(_copiedVoice);
        copy.Index = row.Index;
        ApplyProjectMutationWithoutHistory(() => row.ApplyProjectInfo(copy));
        RecordVoiceStateEdit(row, oldValue, "Paste voice");
        SetVoiceTableStatus(row, $"Pasted Voice to {row.Index:D3}.");
    }

    public void ResetVoice(VoiceRow? row)
    {
        if (row is null)
            return;

        VoiceProjectInfo oldValue = CloneVoiceProjectInfo(row.ToProjectInfo());
        ApplyProjectMutationWithoutHistory(() => row.ApplyProjectInfo(CreateDefaultVoice(row.Index)));
        RecordVoiceStateEdit(row, oldValue, "Reset voice");
        SetVoiceTableStatus(row, $"Reset Voice {row.Index:D3} to Square 1.");
    }

    public string? GetVoiceDataDirectoryPath(VoiceRow? row)
    {
        if (row is null)
            return null;

        string directoryName = row.DataAssetDirectoryName;
        return string.IsNullOrWhiteSpace(directoryName) ? null : GetProjectAssetCategoryDirectoryPath(directoryName);
    }

    public void SetVoiceDataFile(VoiceRow row, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        VoiceProjectInfo oldValue = CloneVoiceProjectInfo(row.ToProjectInfo());
        string relativePath = ToProjectRelativePath(selectedPath);
        ApplyProjectMutationWithoutHistory(() =>
        {
            row.DataFilePath = relativePath;
            if (row.Type == 0x40 && KeySplitOptions.FirstOrDefault(option => AssetPathMatches(option.FilePath, relativePath)) is { } keySplit)
            {
                var voice = row.ToProjectInfo();
                voice.KeySplit = keySplit.KeySplit;
                voice.DrumSet = null;
                row.ApplyProjectInfo(voice);
            }
            else if (row.Type == 0x80 && DrumSetOptions.FirstOrDefault(option => AssetPathMatches(option.FilePath, relativePath)) is { } drumSet)
            {
                var voice = row.ToProjectInfo();
                voice.DrumSet = drumSet.DrumSet;
                voice.KeySplit = null;
                row.ApplyProjectInfo(voice);
            }
        });
        RecordVoiceStateEdit(row, oldValue, "Set voice data");
        SetVoiceTableStatus(row, $"Set Voice {row.Index:D3} data to {Path.GetFileName(selectedPath)}.");
    }

    public void SetSelectedWaveMemoryDataHex(string dataHex)
    {
        if (SelectedWaveMemory is not { } row)
            return;

        row.DataHex = dataHex;
        RestartActiveWaveMemoryKeyboardPreviewNotes();
    }

    public void SetSelectedWaveDataLoopPoints(int loopStart, int loopEnd, bool loops)
    {
        if (SelectedWaveData is not { } row)
            return;

        var oldValue = (row.LoopStart, row.LoopEnd, row.Loops);
        ApplyProjectMutationWithoutHistory(() =>
        {
            row.Loops = loops;
            row.SetLoopPoints(loopStart, loopEnd);
        });
        var newValue = (row.LoopStart, row.LoopEnd, row.Loops);
        RecordProjectValueEdit(
            row,
            "LoopPoints",
            "Edit loop points",
            oldValue,
            newValue,
            value =>
            {
                row.Loops = value.Loops;
                row.SetLoopPoints(value.LoopStart, value.LoopEnd);
            },
            allowMerge: true);
    }

    public void InsertWaveMemoryBelowSelected(bool copySelected, string assetPath)
    {
        WaveMemoryRow? selected = SelectedWaveMemory;
        int id = GetNextWaveMemoryId();
        string relativePath = ToProjectRelativePath(assetPath);
        var created = copySelected && selected is not null
            ? selected.CloneForInsert(id, relativePath)
            : CreateBlankWaveMemoryRow(assetPath);
        InsertWaveMemory(selected is null ? WaveMemoryRows.Count : WaveMemoryRows.IndexOf(selected) + 1, created);
    }

    public void AddWaveMemoryToEnd(string assetPath)
    {
        InsertWaveMemory(WaveMemoryRows.Count, CreateBlankWaveMemoryRow(assetPath));
    }

    public void InsertWaveMemoryAbove(WaveMemoryRow? row, string assetPath)
    {
        int index = row is null ? WaveMemoryRows.Count : WaveMemoryRows.IndexOf(row);
        InsertWaveMemory(index < 0 ? WaveMemoryRows.Count : index, CreateBlankWaveMemoryRow(assetPath));
    }

    public void InsertWaveMemoryBelow(WaveMemoryRow? row, string assetPath)
    {
        int index = row is null ? WaveMemoryRows.Count : WaveMemoryRows.IndexOf(row) + 1;
        InsertWaveMemory(index <= 0 ? WaveMemoryRows.Count : index, CreateBlankWaveMemoryRow(assetPath));
    }

    public void CopyWaveMemory(WaveMemoryRow? row)
    {
        if (row is not null)
            _copiedWaveMemory = row.CloneForInsert(row.Id, row.FilePath);
    }

    public void PasteWaveMemoryBelow(WaveMemoryRow? row, string assetPath)
    {
        if (_copiedWaveMemory is null)
            return;

        int id = GetNextWaveMemoryId();
        int index = row is null ? WaveMemoryRows.Count : WaveMemoryRows.IndexOf(row) + 1;
        InsertWaveMemory(index <= 0 ? WaveMemoryRows.Count : index, _copiedWaveMemory.CloneForInsert(id, ToProjectRelativePath(assetPath)));
    }

    public void DeleteSelectedWaveMemory() => DeleteWaveMemory(SelectedWaveMemory);

    public void DeleteWaveMemory(WaveMemoryRow? row)
    {
        if (row is null)
            return;

        int index = WaveMemoryRows.IndexOf(row);
        if (index < 0)
            return;

        WaveMemoryRows.RemoveAt(index);
        SelectedWaveMemory = WaveMemoryRows.Count == 0
            ? null
            : WaveMemoryRows[Math.Clamp(index, 0, WaveMemoryRows.Count - 1)];
        RefreshVoiceDataDisplays();
    }

    public void InsertWaveDataBelowSelected(bool copySelected, string assetPath)
    {
        WaveDataRow? selected = SelectedWaveData;
        int id = GetNextWaveDataId();
        string relativePath = ToProjectRelativePath(assetPath);
        var created = copySelected && selected is not null
            ? selected.CloneForInsert(id, relativePath)
            : CreateBlankWaveDataRow(assetPath);
        InsertWaveData(selected is null ? WaveDataRows.Count : WaveDataRows.IndexOf(selected) + 1, created);
    }

    public void AddWaveDataToEnd(string assetPath)
    {
        InsertWaveData(WaveDataRows.Count, CreateBlankWaveDataRow(assetPath));
    }

    public void InsertWaveDataAbove(WaveDataRow? row, string assetPath)
    {
        int index = row is null ? WaveDataRows.Count : WaveDataRows.IndexOf(row);
        InsertWaveData(index < 0 ? WaveDataRows.Count : index, CreateBlankWaveDataRow(assetPath));
    }

    public void InsertWaveDataBelow(WaveDataRow? row, string assetPath)
    {
        int index = row is null ? WaveDataRows.Count : WaveDataRows.IndexOf(row) + 1;
        InsertWaveData(index <= 0 ? WaveDataRows.Count : index, CreateBlankWaveDataRow(assetPath));
    }

    public void CopyWaveData(WaveDataRow? row)
    {
        if (row is not null)
            _copiedWaveData = row.CloneForInsert(row.Id, row.FilePath);
    }

    public void PasteWaveDataBelow(WaveDataRow? row, string assetPath)
    {
        if (_copiedWaveData is null)
            return;

        int id = GetNextWaveDataId();
        int index = row is null ? WaveDataRows.Count : WaveDataRows.IndexOf(row) + 1;
        InsertWaveData(index <= 0 ? WaveDataRows.Count : index, _copiedWaveData.CloneForInsert(id, ToProjectRelativePath(assetPath)));
    }

    public void DeleteSelectedWaveData() => DeleteWaveData(SelectedWaveData);

    public void DeleteWaveData(WaveDataRow? row)
    {
        if (row is null)
            return;

        int index = WaveDataRows.IndexOf(row);
        if (index < 0)
            return;

        WaveDataRows.RemoveAt(index);
        SelectedWaveData = WaveDataRows.Count == 0
            ? null
            : WaveDataRows[Math.Clamp(index, 0, WaveDataRows.Count - 1)];
        RefreshVoiceDataDisplays();
    }

    private void MoveSelectedVoice(int direction)
    {
        MoveVoice(SelectedVoice, direction);
    }

    private void MoveVoice(VoiceRow? selected, int direction)
    {
        if (selected is null || !TryGetVoiceTable(selected, out var table))
            return;

        int index = table.Rows.IndexOf(selected);
        int nextIndex = Math.Clamp(index + direction, 0, table.Rows.Count - 1);
        if (index < 0 || nextIndex == index)
            return;

        KeySplitProjectInfo? oldKeySplit = table.Kind == VoiceTableKind.KeySplit && SelectedKeySplit is not null
            ? CloneKeySplitProjectInfo(SelectedKeySplit.KeySplit)
            : null;
        List<VoiceProjectInfo>? oldVoices = oldKeySplit is null
            ? table.Voices.Select(CloneVoiceProjectInfo).ToList()
            : null;
        table.Rows.Move(index, nextIndex);
        table.Voices.RemoveAt(index);
        table.Voices.Insert(nextIndex, selected.Source ?? selected.ToProjectInfo());
        if (table.Kind == VoiceTableKind.KeySplit)
            RemapKeySplitRegionIndex(SelectedKeySplit?.KeySplit, index, nextIndex);
        ReindexVoiceRows(table.Rows, table.UpdateUsage);
        ReindexVoices(table.Voices);
        SelectVoiceInTable(table, selected);
        if (oldKeySplit is not null && SelectedKeySplit is not null)
            RecordKeySplitStateEdit(SelectedKeySplit.KeySplit, oldKeySplit, "Move KeySplit region");
        else if (oldVoices is not null)
            RecordVoiceListStateEdit(table.Voices, oldVoices, "Move voice");
    }

    private void InsertKeySplitRegion(VoiceRow? row, bool insertBelow, VoiceProjectInfo? voiceToInsert)
    {
        if (SelectedKeySplit is not { } selected)
            return;

        KeySplitProjectInfo oldValue = CloneKeySplitProjectInfo(selected.KeySplit);
        int oldCount = selected.KeySplit.Regions.Count;
        int sourceIndex = row is null
            ? (SelectedKeySplitVoice is null ? -1 : KeySplitRows.IndexOf(SelectedKeySplitVoice))
            : KeySplitRows.IndexOf(row);
        if (sourceIndex < 0)
            sourceIndex = oldCount - 1;

        int insertIndex = sourceIndex < 0 ? 0 : sourceIndex + (insertBelow ? 1 : 0);
        insertIndex = Math.Clamp(insertIndex, 0, oldCount);

        byte[] keyMap = EnsureKeySplitKeyMap(selected.KeySplit, oldCount, createIfEmpty: true);
        List<int> sourceNotes = sourceIndex >= 0 && oldCount > 0
            ? Enumerable.Range(0, keyMap.Length).Where(note => GetKeySplitMappedRegion(keyMap, note, oldCount) == sourceIndex).ToList()
            : new List<int>();

        VoiceProjectInfo voice = voiceToInsert is null
            ? CreateDefaultVoice(insertIndex)
            : CloneVoiceProjectInfo(voiceToInsert);
        voice.Index = insertIndex;
        selected.KeySplit.Regions.Insert(insertIndex, voice);

        if (oldCount == 0)
        {
            Array.Fill(keyMap, (byte)0);
        }
        else
        {
            RemapKeySplitMapForInsert(keyMap, oldCount, insertIndex);
            AssignSplitKeySplitNotes(keyMap, sourceNotes, insertIndex, insertBelow);
        }

        selected.KeySplit.KeyMapHex = EncodeVoiceTableHex(keyMap);
        ReindexVoices(selected.KeySplit.Regions);
        RefreshKeySplitRows();
        if (insertIndex < KeySplitRows.Count)
            SelectedKeySplitVoice = KeySplitRows[insertIndex];
        KeySplitStatus = $"Inserted KeySplit region {insertIndex:D3}.";
        RecordKeySplitStateEdit(selected.KeySplit, oldValue, "Insert KeySplit region");
    }
    private void OnSelectedVoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VoiceRow.Key) or nameof(VoiceRow.KeyName))
            RefreshSelectedVoiceKeyMarkers();

        if (ReferenceEquals(sender, SelectedDrumSetVoice) &&
            e.PropertyName is nameof(VoiceRow.Index) or nameof(VoiceRow.IndexText))
            RefreshSelectedDrumSetKeyMarkers();

        if (sender is VoiceRow row && KeySplitRows.Contains(row) &&
            e.PropertyName is nameof(VoiceRow.Label) or nameof(VoiceRow.TypeName) or nameof(VoiceRow.DataDisplay))
        {
            OnPropertyChanged(nameof(KeySplitRangeLabels));
        }
    }

    private void OnSelectedAssetOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(VoiceGroupOption.Label) or nameof(VoiceGroupOption.FileDisplay)))
            return;

        RefreshVoiceDataDisplays();
        RefreshSequenceVoiceGroupDisplays();
    }

    private void OnSelectedWaveMemoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedWaveMemory))
            return;

        if (e.PropertyName is nameof(WaveMemoryRow.Label) or nameof(WaveMemoryRow.FileDisplay))
            RefreshVoiceDataDisplays();

        if (e.PropertyName is nameof(WaveMemoryRow.DataHex) && SelectedWaveMemory is { } waveMemory)
            WaveMemoryStatus = $"Edited {waveMemory.FileName}.";
    }

    private void OnSelectedWaveDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedWaveData))
            return;

        if (e.PropertyName is nameof(WaveDataRow.Label) or nameof(WaveDataRow.FileDisplay))
            RefreshVoiceDataDisplays();

        if ((e.PropertyName is nameof(WaveDataRow.LoopStart) or nameof(WaveDataRow.LoopEnd) or nameof(WaveDataRow.Loops)) &&
            SelectedWaveData is { } waveData)
        {
            VoiceStatus = $"Edited loop points for {waveData.FileName}.";
        }
    }

    private void RefreshSelectedVoiceKeyMarkers()
    {
        _selectedVoiceKeyMarkers = SelectedVoice is { } voice
            ? new[] { voice.Key }
            : Array.Empty<int>();
        OnPropertyChanged(nameof(SelectedVoiceKeyMarkers));
    }

    private void RefreshSelectedDrumSetKeyMarkers()
    {
        _selectedDrumSetKeyMarkers = SelectedVoice is { } voice && ReferenceEquals(voice, SelectedDrumSetVoice)
            ? new[] { voice.Index }
            : Array.Empty<int>();
        OnPropertyChanged(nameof(SelectedDrumSetKeyMarkers));
    }

    private void AddSongHeaderFileOption(string filePath, string label)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (!SongHeaderFileOptions.Contains(filePath))
            SongHeaderFileOptions.Add(filePath);
        _songHeaderDisplayByPath[filePath] = FormatAssetDisplay(label, filePath);
    }

    private string? GetProjectAssetDirectoryPath(string assetDirectoryName, IEnumerable<string> existingRelativePaths)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return null;

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        string? existingPath = existingRelativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(projectDirectory, path.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(existingPath))
            return Path.GetDirectoryName(existingPath);

        string projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);
        string defaultDirectory = Path.Combine(projectDirectory, $"{projectName}_data", assetDirectoryName);
        return Directory.Exists(defaultDirectory) ? defaultDirectory : null;
    }

    private string? GetProjectAssetCategoryDirectoryPath(string assetDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return null;

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        string projectName = Path.GetFileNameWithoutExtension(_currentProjectPath);
        string defaultDirectory = Path.Combine(projectDirectory, $"{projectName}_data", assetDirectoryName);
        return Directory.Exists(defaultDirectory) ? defaultDirectory : null;
    }

    private string GetSongHeaderDisplay(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;
        return _songHeaderDisplayByPath.TryGetValue(filePath, out string? display)
            ? display
            : FormatAssetDisplay(string.Empty, filePath);
    }

    private string GetAssetFileDisplay(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        string? display =
            WaveDataRows.FirstOrDefault(row => AssetPathMatches(row.FilePath, filePath))?.FileDisplay ??
            WaveMemoryRows.FirstOrDefault(row => AssetPathMatches(row.FilePath, filePath))?.FileDisplay ??
            KeySplitOptions.FirstOrDefault(option => AssetPathMatches(option.FilePath, filePath))?.FileDisplay ??
            DrumSetOptions.FirstOrDefault(option => AssetPathMatches(option.FilePath, filePath))?.FileDisplay ??
            VoiceGroupOptions.FirstOrDefault(option => AssetPathMatches(option.FilePath, filePath))?.FileDisplay;

        return string.IsNullOrWhiteSpace(display)
            ? FormatAssetDisplay(string.Empty, filePath)
            : display;
    }

    private bool AssetPathMatches(string candidatePath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(filePath))
            return false;

        string normalizedCandidate = NormalizeAssetPath(candidatePath);
        string normalizedPath = NormalizeAssetPath(filePath);
        string normalizedRelativePath = NormalizeAssetPath(ToProjectRelativePath(filePath));
        return string.Equals(normalizedCandidate, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedCandidate, normalizedRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string path)
    {
        return (path ?? string.Empty)
            .Replace('\\', '/')
            .Trim();
    }

    private void RefreshVoiceDataDisplays()
    {
        foreach (var row in Voices.Concat(KeySplitRows).Concat(DrumSetRows))
            row.RefreshDataDisplay();
    }

    private int GetNextWaveMemoryId()
    {
        return WaveMemoryRows.Count == 0 ? 0 : WaveMemoryRows.Max(row => row.Id) + 1;
    }

    private int GetNextWaveDataId()
    {
        return WaveDataRows.Count == 0 ? 0 : WaveDataRows.Max(row => row.Id) + 1;
    }

    private WaveMemoryRow CreateBlankWaveMemoryRow(string assetPath)
    {
        int id = GetNextWaveMemoryId();
        return WaveMemoryRow.CreateBlank(
            id,
            ToProjectRelativePath(assetPath),
            GetCurrentProjectDirectory());
    }

    private WaveDataRow CreateBlankWaveDataRow(string assetPath)
    {
        int id = GetNextWaveDataId();
        return WaveDataRow.CreateBlank(
            id,
            ToProjectRelativePath(assetPath),
            GetCurrentProjectDirectory());
    }

    private void InsertWaveMemory(int index, WaveMemoryRow row)
    {
        if (!TryWriteAsset(row.SaveAsset, out string? error))
        {
            WaveMemoryStatus = $"WaveMemory creation failed: {error}";
            return;
        }
        int targetIndex = Math.Clamp(index, 0, WaveMemoryRows.Count);
        WaveMemoryRows.Insert(targetIndex, row);
        SelectedWaveMemory = row;
        RefreshVoiceDataDisplays();
    }

    private void InsertWaveData(int index, WaveDataRow row)
    {
        if (!TryWriteAsset(row.SaveAsset, out string? error))
        {
            VoiceStatus = $"Voice creation failed: {error}";
            return;
        }
        int targetIndex = Math.Clamp(index, 0, WaveDataRows.Count);
        WaveDataRows.Insert(targetIndex, row);
        SelectedWaveData = row;
        RefreshVoiceDataDisplays();
    }

    private string ToProjectRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return path.Replace(Path.DirectorySeparatorChar, '/');
        if (!Path.IsPathRooted(path))
            return path.Replace(Path.DirectorySeparatorChar, '/');

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        return Path.GetRelativePath(projectDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string FormatAssetDisplay(string label, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
    }

    private static string TryReadSongHeaderLabel(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Header", out var header) &&
                header.TryGetProperty("Label", out var labelElement))
            {
                return labelElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static int DetectMidiTrackCount(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (ReadAscii(reader, 4) != "MThd")
                return 0;

            int headerLength = ReadBigEndianInt32(reader);
            if (headerLength < 6)
                return 0;

            int format = ReadBigEndianInt16(reader);
            int trackChunks = ReadBigEndianInt16(reader);
            stream.Seek(headerLength - 4, SeekOrigin.Current);
            int musicalTracks = format == 1 && trackChunks > 1 ? trackChunks - 1 : trackChunks;
            return Math.Clamp(musicalTracks, 0, 255);
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(count));
    }

    private static int ReadBigEndianInt16(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        if (reader.Read(bytes) != 2)
            return 0;
        return bytes[0] << 8 | bytes[1];
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != 4)
            return 0;
        return bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3];
    }

    private void ReindexSongTableEntries()
    {
        for (int i = 0; i < SongTableEntries.Count; i++)
            SongTableEntries[i].SongId = i;
    }

    private void LoadSequences(AgbSynthProjectFile project)
    {
        Sequences.Clear();
        foreach (var header in project.SongHeaders)
            Sequences.Add(SequenceHeaderRow.FromProjectInfo(header));
        SelectedSequence = Sequences.FirstOrDefault();
    }

    private void LoadVoiceGroups(AgbSynthProjectFile project)
    {
        ApplyProjectSoundMode(project.SoundMode);
        VoiceGroupOptions.Clear();
        Voices.Clear();
        foreach (var voiceGroup in project.VoiceGroups)
            VoiceGroupOptions.Add(VoiceGroupOption.FromProjectInfo(voiceGroup));

        SelectedVoiceGroup = VoiceGroupOptions.Count > 0 ? VoiceGroupOptions[0] : null;
        RefreshSequenceVoiceGroupDisplays();
        OnPropertyChanged(nameof(SelectedVoiceGroupVoiceCountText));
    }

    private void LoadVoiceTables(AgbSynthProjectFile project)
    {
        KeySplitOptions.Clear();
        DrumSetOptions.Clear();
        KeySplitRows.Clear();
        DrumSetRows.Clear();

        var keySplitsByPath = new Dictionary<string, KeySplitOption>(StringComparer.OrdinalIgnoreCase);
        var drumSetsByPath = new Dictionary<string, DrumSetOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in project.KeySplits)
        {
            string key = string.IsNullOrWhiteSpace(asset.FilePath) ? $"asset:{asset.Id}" : asset.FilePath;
            keySplitsByPath[key] = KeySplitOption.FromProjectInfo(asset);
        }

        foreach (var asset in project.DrumSets)
        {
            string key = string.IsNullOrWhiteSpace(asset.FilePath) ? $"asset:{asset.Id}" : asset.FilePath;
            drumSetsByPath[key] = DrumSetOption.FromProjectInfo(asset);
        }

        foreach (var voiceGroup in project.VoiceGroups)
        {
            foreach (var voice in voiceGroup.Voices)
                CollectNestedVoiceTables(voiceGroup.Id, voice, keySplitsByPath, drumSetsByPath);
        }

        int keySplitId = 0;
        foreach (var option in keySplitsByPath.Values.OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            option.Id = keySplitId++;
            KeySplitOptions.Add(option);
        }

        int drumSetId = 0;
        foreach (var option in drumSetsByPath.Values.OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            option.Id = drumSetId++;
            DrumSetOptions.Add(option);
        }

        SelectedKeySplit = KeySplitOptions.FirstOrDefault();
        SelectedDrumSet = DrumSetOptions.FirstOrDefault();
        RefreshVoiceDataDisplays();
    }

    private void LoadWaveMemory(AgbSynthProjectFile project)
    {
        SelectedWaveMemory = null;
        WaveMemoryRows.Clear();

        string projectDirectory = GetCurrentProjectDirectory();
        foreach (var waveMemory in project.WaveMemory.OrderBy(w => w.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = WaveMemoryRow.FromProjectInfo(waveMemory, projectDirectory);
            row.ReloadFromFile();
            WaveMemoryRows.Add(row);
        }

        SelectedWaveMemory = WaveMemoryRows.FirstOrDefault();
        RefreshVoiceDataDisplays();
    }

    private void LoadWaveData(AgbSynthProjectFile project)
    {
        SelectedWaveData = null;
        WaveDataRows.Clear();

        string projectDirectory = GetCurrentProjectDirectory();
        foreach (var waveData in project.WaveData.OrderBy(w => w.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = WaveDataRow.FromProjectInfo(waveData, projectDirectory);
            row.ReloadFromFile();
            WaveDataRows.Add(row);
        }

        SelectedWaveData = WaveDataRows.FirstOrDefault();
        RefreshVoiceDataDisplays();
    }

    private static void CollectNestedVoiceTables(
        int voiceGroupId,
        VoiceProjectInfo voice,
        IDictionary<string, KeySplitOption> keySplitsByPath,
        IDictionary<string, DrumSetOption> drumSetsByPath)
    {
        if (voice.KeySplit is not null)
        {
            string key = string.IsNullOrWhiteSpace(voice.DataFilePath)
                ? $"vg:{voiceGroupId}:ks:{voice.Index}"
                : voice.DataFilePath;
            if (!keySplitsByPath.ContainsKey(key))
            {
                keySplitsByPath[key] = new KeySplitOption
                {
                    Label = string.IsNullOrWhiteSpace(voice.KeySplit.Label)
                        ? Path.GetFileNameWithoutExtension(voice.DataFilePath)
                        : voice.KeySplit.Label,
                    FilePath = voice.DataFilePath,
                    VoiceGroupId = voiceGroupId,
                    ParentVoiceIndex = voice.Index,
                    KeySplit = voice.KeySplit
                };
            }

            foreach (var region in voice.KeySplit.Regions)
                CollectNestedVoiceTables(voiceGroupId, region, keySplitsByPath, drumSetsByPath);
        }

        if (voice.DrumSet is not null)
        {
            string key = string.IsNullOrWhiteSpace(voice.DataFilePath)
                ? $"vg:{voiceGroupId}:ds:{voice.Index}"
                : voice.DataFilePath;
            if (!drumSetsByPath.ContainsKey(key))
            {
                EnsureDrumSetEntryCount(voice.DrumSet);
                drumSetsByPath[key] = new DrumSetOption
                {
                    Label = string.IsNullOrWhiteSpace(voice.DrumSet.Label)
                        ? Path.GetFileNameWithoutExtension(voice.DataFilePath)
                        : voice.DrumSet.Label,
                    FilePath = voice.DataFilePath,
                    VoiceGroupId = voiceGroupId,
                    ParentVoiceIndex = voice.Index,
                    DrumSet = voice.DrumSet
                };
            }

            foreach (var entry in voice.DrumSet.Entries)
                CollectNestedVoiceTables(voiceGroupId, entry, keySplitsByPath, drumSetsByPath);
        }
    }

    private int GetNextVoiceGroupId()
    {
        return VoiceGroupOptions.Count == 0 ? 0 : VoiceGroupOptions.Max(v => v.Id) + 1;
    }

    private int GetNextKeySplitId()
    {
        return KeySplitOptions.Count == 0 ? 0 : KeySplitOptions.Max(v => v.Id) + 1;
    }

    private int GetNextDrumSetId()
    {
        return DrumSetOptions.Count == 0 ? 0 : DrumSetOptions.Max(v => v.Id) + 1;
    }

    private static KeySplitProjectInfo CreateBlankKeySplit(string label)
    {
        return new KeySplitProjectInfo
        {
            Label = label,
            KeyMapHex = EncodeVoiceTableHex(Enumerable.Repeat((byte)0, 128)),
            Regions = new List<VoiceProjectInfo> { CreateDefaultVoice(0) }
        };
    }

    private static DrumSetProjectInfo CreateBlankDrumSet(string label)
    {
        var drumSet = new DrumSetProjectInfo { Label = label };
        EnsureDrumSetEntryCount(drumSet);
        return drumSet;
    }

    private static KeySplitProjectInfo CloneKeySplitProjectInfo(KeySplitProjectInfo keySplit)
    {
        return new KeySplitProjectInfo
        {
            Label = keySplit.Label,
            RegionTableOffset = keySplit.RegionTableOffset,
            KeyMapOffset = keySplit.KeyMapOffset,
            KeyMapHex = keySplit.KeyMapHex,
            RawRegionTableHex = keySplit.RawRegionTableHex,
            Regions = keySplit.Regions.Select(CloneVoiceProjectInfo).ToList()
        };
    }

    private static DrumSetProjectInfo CloneDrumSetProjectInfo(DrumSetProjectInfo drumSet)
    {
        return new DrumSetProjectInfo
        {
            Label = drumSet.Label,
            TableOffset = drumSet.TableOffset,
            RawHex = drumSet.RawHex,
            Entries = drumSet.Entries.Select(CloneVoiceProjectInfo).ToList()
        };
    }

    private static List<VoiceProjectInfo> CreateBlankVoiceGroupVoices()
    {
        var voices = new List<VoiceProjectInfo>(128);
        for (int i = 0; i < 128; i++)
            voices.Add(CreateDefaultVoice(i));

        return voices;
    }

    private static VoiceProjectInfo CreateDefaultVoice(int index)
    {
        return new VoiceProjectInfo
        {
            Index = index,
            Label = "Square 1",
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
        };
    }

    private static void EnsureDrumSetEntryCount(DrumSetProjectInfo drumSet)
    {
        for (int i = drumSet.Entries.Count; i < 128; i++)
            drumSet.Entries.Add(CreateDefaultVoice(i));

        if (drumSet.Entries.Count > 128)
            drumSet.Entries.RemoveRange(128, drumSet.Entries.Count - 128);

        ReindexVoices(drumSet.Entries);
    }

    private static VoiceProjectInfo CloneVoiceProjectInfo(VoiceProjectInfo voice)
    {
        return new VoiceProjectInfo
        {
            Index = voice.Index,
            Label = voice.Label,
            Type = voice.Type,
            TypeName = voice.TypeName,
            Key = voice.Key,
            Length = voice.Length,
            PanOrSweep = voice.PanOrSweep,
            DataPointer = voice.DataPointer,
            DataOffset = voice.DataOffset,
            DataFilePath = voice.DataFilePath,
            Attack = voice.Attack,
            Decay = voice.Decay,
            Sustain = voice.Sustain,
            Release = voice.Release,
            Sample = voice.Sample,
            PsgSquare = voice.PsgSquare,
            PsgWaveMemory = voice.PsgWaveMemory,
            PsgNoise = voice.PsgNoise,
            DrumSet = voice.DrumSet is null
                ? null
                : new DrumSetProjectInfo
                {
                    Label = voice.DrumSet.Label,
                    TableOffset = voice.DrumSet.TableOffset,
                    RawHex = voice.DrumSet.RawHex,
                    Entries = voice.DrumSet.Entries.Select(CloneVoiceProjectInfo).ToList()
                },
            KeySplit = voice.KeySplit is null
                ? null
                : new KeySplitProjectInfo
                {
                    Label = voice.KeySplit.Label,
                    RegionTableOffset = voice.KeySplit.RegionTableOffset,
                    KeyMapOffset = voice.KeySplit.KeyMapOffset,
                    KeyMapHex = voice.KeySplit.KeyMapHex,
                    RawRegionTableHex = voice.KeySplit.RawRegionTableHex,
                    Regions = voice.KeySplit.Regions.Select(CloneVoiceProjectInfo).ToList()
                },
            RawEntryHex = voice.RawEntryHex
        };
    }

    private static void ReindexVoices(IList<VoiceProjectInfo> voices)
    {
        for (int i = 0; i < voices.Count; i++)
            voices[i].Index = i;
    }

    private void ReindexVoiceRows()
    {
        ReindexVoiceRows(Voices, updateUsage: false);
    }

    private static void ReindexVoiceRows(IList<VoiceRow> rows, bool updateUsage)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Index = i;
            if (updateUsage)
                rows[i].UsageText = NoteNameFromMidi(i);
        }
    }

    private bool TryGetVoiceTable(VoiceRow row, out VoiceTableSelection table)
    {
        if (SelectedVoiceGroup is not null && Voices.Contains(row))
        {
            table = new VoiceTableSelection(VoiceTableKind.VoiceGroup, Voices, SelectedVoiceGroup.Voices, UpdateUsage: false);
            return true;
        }

        if (SelectedKeySplit is not null && KeySplitRows.Contains(row))
        {
            table = new VoiceTableSelection(VoiceTableKind.KeySplit, KeySplitRows, SelectedKeySplit.KeySplit.Regions, UpdateUsage: false);
            return true;
        }

        if (SelectedDrumSet is not null && DrumSetRows.Contains(row))
        {
            table = new VoiceTableSelection(VoiceTableKind.DrumSet, DrumSetRows, SelectedDrumSet.DrumSet.Entries, UpdateUsage: true);
            return true;
        }

        table = default;
        return false;
    }

    private void SelectVoiceInTable(VoiceTableSelection table, VoiceRow row)
    {
        SelectedVoice = row;
        if (table.Kind == VoiceTableKind.KeySplit)
            SelectedKeySplitVoice = row;
        else if (table.Kind == VoiceTableKind.DrumSet)
            SelectedDrumSetVoice = row;
    }

    private void SetVoiceTableStatus(VoiceRow row, string message)
    {
        if (KeySplitRows.Contains(row))
            KeySplitStatus = message;
        else if (DrumSetRows.Contains(row))
            DrumSetStatus = message;
        else
            VoiceGroupStatus = message;
    }

    private void RemapKeySplitRegionIndex(KeySplitProjectInfo? keySplit, int fromIndex, int toIndex)
    {
        if (keySplit is null || string.IsNullOrWhiteSpace(keySplit.KeyMapHex))
            return;

        byte[] keyMap = DecodeVoiceTableHex(keySplit.KeyMapHex);
        if (keyMap.Length == 0)
            return;

        for (int i = 0; i < keyMap.Length; i++)
        {
            int value = keyMap[i];
            if (value == fromIndex)
                keyMap[i] = (byte)toIndex;
            else if (fromIndex < toIndex && value > fromIndex && value <= toIndex)
                keyMap[i] = (byte)(value - 1);
            else if (fromIndex > toIndex && value >= toIndex && value < fromIndex)
                keyMap[i] = (byte)(value + 1);
        }

        keySplit.KeyMapHex = EncodeVoiceTableHex(keyMap);
        RefreshKeySplitUsage();
    }

    private static byte[] EnsureKeySplitKeyMap(KeySplitProjectInfo keySplit, int regionCount, bool createIfEmpty)
    {
        byte[] source = DecodeVoiceTableHex(keySplit.KeyMapHex);
        if (source.Length == 0)
            return createIfEmpty ? Enumerable.Repeat((byte)0, 128).ToArray() : Array.Empty<byte>();

        byte[] map = new byte[128];
        Array.Fill(map, (byte)0xFF);
        Array.Copy(source, map, Math.Min(source.Length, map.Length));
        if (regionCount > 0)
        {
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i] >= regionCount)
                    map[i] = 0xFF;
            }
        }

        return map;
    }

    private static void RemapKeySplitMapForInsert(byte[] keyMap, int oldRegionCount, int insertIndex)
    {
        for (int i = 0; i < keyMap.Length; i++)
        {
            int region = keyMap[i];
            if (region >= oldRegionCount)
            {
                keyMap[i] = 0xFF;
                continue;
            }

            if (region >= insertIndex)
                keyMap[i] = (byte)(region + 1);
        }
    }

    private static void RemapKeySplitMapForDelete(byte[] keyMap, int oldRegionCount, int deleteIndex)
    {
        for (int i = 0; i < keyMap.Length; i++)
        {
            int region = keyMap[i];
            if (region >= oldRegionCount)
            {
                keyMap[i] = 0xFF;
                continue;
            }

            if (region == deleteIndex)
                region = deleteIndex > 0 ? deleteIndex - 1 : 0;
            else if (region > deleteIndex)
                region--;

            keyMap[i] = (byte)region;
        }
    }

    private static void AssignSplitKeySplitNotes(byte[] keyMap, IReadOnlyList<int> sourceNotes, int insertedIndex, bool insertedBelow)
    {
        if (sourceNotes.Count <= 1)
            return;

        int splitIndex = insertedBelow ? (sourceNotes.Count + 1) / 2 : sourceNotes.Count / 2;
        IEnumerable<int> movedNotes = insertedBelow
            ? sourceNotes.Skip(splitIndex)
            : sourceNotes.Take(splitIndex);

        foreach (int note in movedNotes)
        {
            if ((uint)note < (uint)keyMap.Length)
                keyMap[note] = (byte)insertedIndex;
        }
    }
    private void RefreshSequenceVoiceGroupDisplays()
    {
        foreach (var sequence in Sequences)
        {
            VoiceGroupOption? voiceGroup = VoiceGroupOptions.FirstOrDefault(v =>
                string.Equals(v.FilePath, sequence.VoiceGroupFilePath, StringComparison.OrdinalIgnoreCase));
            sequence.VoiceGroupDisplay = voiceGroup?.FileDisplay ?? FormatAssetDisplay(string.Empty, sequence.VoiceGroupFilePath);
        }
    }

    private void SelectVoiceGroupForSequence(SequenceHeaderRow? sequence)
    {
        if (sequence is null || VoiceGroupOptions.Count == 0)
            return;

        VoiceGroupOption? match = VoiceGroupOptions.FirstOrDefault(v =>
            !string.IsNullOrWhiteSpace(sequence.VoiceGroupFilePath) &&
            string.Equals(v.FilePath, sequence.VoiceGroupFilePath, StringComparison.OrdinalIgnoreCase));
        match ??= VoiceGroupOptions.FirstOrDefault(v => v.Offset == sequence.VoiceGroupOffset);
        if (match is not null)
            SelectedVoiceGroup = match;
    }

    private void RefreshVoices()
    {
        Voices.Clear();
        if (SelectedVoiceGroup is not { } selected)
        {
            OnPropertyChanged(nameof(SelectedVoiceGroupVoiceCountText));
            return;
        }

        foreach (var voice in selected.Voices)
        {
            var row = VoiceRow.FromProjectInfo(selected, voice);
            row.ProjectDirectory = string.IsNullOrWhiteSpace(_currentProjectPath)
                ? string.Empty
                : Path.GetDirectoryName(_currentProjectPath) ?? string.Empty;
            row.DataFileDisplayResolver = GetAssetFileDisplay;
            Voices.Add(row);
            TrackEditableVoiceRow(row);
        }

        SelectedVoice = Voices.FirstOrDefault(v => v.SampleSize is not null) ?? Voices.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedVoiceGroupVoiceCountText));
    }

    private void RefreshKeySplitRows()
    {
        KeySplitRows.Clear();
        if (SelectedKeySplit is not { } selected)
        {
            SelectedKeySplitVoice = null;
            OnPropertyChanged(nameof(SelectedKeySplitKeyMapHex));
            OnPropertyChanged(nameof(KeySplitRangeLabels));
            return;
        }

        byte[] keyMap = DecodeVoiceTableHex(selected.KeySplit.KeyMapHex);
        for (int i = 0; i < selected.KeySplit.Regions.Count; i++)
        {
            var row = VoiceRow.FromProjectInfo(new VoiceGroupOption(), selected.KeySplit.Regions[i]);
            row.ProjectDirectory = GetCurrentProjectDirectory();
            row.DataFileDisplayResolver = GetAssetFileDisplay;
            row.UsageText = GetKeySplitRegionRangeText(i, keyMap, selected.KeySplit.Regions.Count);
            KeySplitRows.Add(row);
            TrackEditableVoiceRow(row);
        }

        SelectedKeySplitVoice = KeySplitRows.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedKeySplitKeyMapHex));
        OnPropertyChanged(nameof(KeySplitRangeLabels));
    }

    private void RefreshKeySplitUsage()
    {
        byte[] keyMap = DecodeVoiceTableHex(SelectedKeySplit?.KeySplit.KeyMapHex ?? string.Empty);
        foreach (var row in KeySplitRows)
            row.UsageText = GetKeySplitRegionRangeText(row.Index, keyMap, KeySplitRows.Count);

        OnPropertyChanged(nameof(KeySplitRangeLabels));
    }

    private void RefreshDrumSetRows()
    {
        DrumSetRows.Clear();
        if (SelectedDrumSet is not { } selected)
        {
            SelectedDrumSetVoice = null;
            RefreshSelectedDrumSetKeyMarkers();
            return;
        }

        EnsureDrumSetEntryCount(selected.DrumSet);
        foreach (var entry in selected.DrumSet.Entries)
        {
            var row = VoiceRow.FromProjectInfo(new VoiceGroupOption(), entry);
            row.ProjectDirectory = GetCurrentProjectDirectory();
            row.DataFileDisplayResolver = GetAssetFileDisplay;
            row.UsageText = NoteNameFromMidi(entry.Index);
            DrumSetRows.Add(row);
            TrackEditableVoiceRow(row);
        }

        SelectedDrumSetVoice = DrumSetRows.FirstOrDefault();
    }

    private string GetCurrentProjectDirectory()
    {
        return string.IsNullOrWhiteSpace(_currentProjectPath)
            ? string.Empty
            : Path.GetDirectoryName(_currentProjectPath) ?? string.Empty;
    }

    private static string GetKeySplitRegionRangeText(int regionIndex, byte[] keyMap, int regionCount)
    {
        if (keyMap.Length == 0)
            return string.Empty;

        var ranges = new List<string>();
        int start = -1;
        for (int note = 0; note < Math.Min(128, keyMap.Length); note++)
        {
            bool matches = GetKeySplitMappedRegion(keyMap, note, regionCount) == regionIndex;
            if (matches && start < 0)
                start = note;
            if ((!matches || note == Math.Min(128, keyMap.Length) - 1) && start >= 0)
            {
                int end = matches && note == Math.Min(128, keyMap.Length) - 1 ? note : note - 1;
                ranges.Add(start == end
                    ? NoteNameFromMidi(start)
                    : $"{NoteNameFromMidi(start)}-{NoteNameFromMidi(end)}");
                start = -1;
            }
        }

        return ranges.Count == 0 ? "--" : string.Join(", ", ranges.Take(3)) + (ranges.Count > 3 ? "..." : string.Empty);
    }

    private static int GetKeySplitMappedRegion(byte[] keyMap, int note, int regionCount)
    {
        if (note < 0 || note >= keyMap.Length || regionCount <= 0)
            return -1;

        if (note < GetKeySplitFirstMappedNote(keyMap, regionCount))
            return -1;

        int region = keyMap[note];
        return region >= 0 && region < regionCount ? region : -1;
    }

    private static int GetKeySplitFirstMappedNote(byte[] keyMap, int regionCount)
    {
        if (keyMap.Length == 0 || regionCount <= 0)
            return 0;

        int requiredRun = Math.Min(16, keyMap.Length);
        for (int start = 0; start < keyMap.Length; start++)
        {
            int run = 0;
            for (int i = start; i < keyMap.Length && keyMap[i] < regionCount; i++)
                run++;

            if (run >= requiredRun)
                return start;
        }

        return 0;
    }

    private static string NoteNameFromMidi(int note)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int clamped = Math.Clamp(note, 0, 127);
        return $"{names[clamped % 12]}{clamped / 12 - 1}";
    }

    private static byte[] DecodeVoiceTableHex(string hex)
    {
        string normalized = new((hex ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length < 2)
            return [];
        if (normalized.Length % 2 != 0)
            normalized = normalized[..^1];

        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string EncodeVoiceTableHex(IEnumerable<byte> bytes)
    {
        return Convert.ToHexString(bytes.ToArray());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
