using System;
using System.Collections.Generic;
using System.IO;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly HashSet<string> SongTableEditableProperties =
    [
        nameof(SongTableEntryRow.Label),
        nameof(SongTableEntryRow.SongHeaderFilePath),
        nameof(SongTableEntryRow.Group1),
        nameof(SongTableEntryRow.Group2),
        nameof(SongTableEntryRow.Note)
    ];

    private static readonly HashSet<string> SequenceEditableProperties =
    [
        nameof(SequenceHeaderRow.Label),
        nameof(SequenceHeaderRow.TrackCount),
        nameof(SequenceHeaderRow.BlockCount),
        nameof(SequenceHeaderRow.Priority),
        nameof(SequenceHeaderRow.Reverb),
        nameof(SequenceHeaderRow.VoiceGroupFilePath),
        nameof(SequenceHeaderRow.MidiFilePath),
        nameof(SequenceHeaderRow.Note)
    ];

    private static readonly HashSet<string> VoiceEditableProperties =
    [
        nameof(VoiceRow.Label),
        nameof(VoiceRow.Type),
        nameof(VoiceRow.Key),
        nameof(VoiceRow.Length),
        nameof(VoiceRow.PanOrSweep),
        nameof(VoiceRow.DataFilePath),
        nameof(VoiceRow.SelectedSquareDutyOption),
        nameof(VoiceRow.SelectedNoiseControlOption),
        nameof(VoiceRow.SelectedNoiseKindOption),
        nameof(VoiceRow.Attack),
        nameof(VoiceRow.Decay),
        nameof(VoiceRow.Sustain),
        nameof(VoiceRow.Release)
    ];

    private AgbSynthProjectFile? _currentProject;
    private bool _isProjectDirty;
    private bool _isSavingProject;
    private bool _suppressProjectTracking;
    private bool _isApplyingProjectHistory;
    private long _untrackedProjectRevision;
    private long _savedUntrackedProjectRevision;
    private int _projectHistoryPosition;
    private int _savedProjectHistoryPosition;
    private readonly HashSet<string> _knownManagedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IProjectEdit> _projectHistory = new();
    private readonly Dictionary<PropertyChangeKey, object?> _pendingPropertyChanges = new();

    public bool IsProjectDirty
    {
        get => _isProjectDirty;
        private set
        {
            if (!SetField(ref _isProjectDirty, value))
                return;

            OnPropertyChanged(nameof(CanSaveProject));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool IsSavingProject
    {
        get => _isSavingProject;
        private set
        {
            if (!SetField(ref _isSavingProject, value))
                return;

            OnPropertyChanged(nameof(CanSaveProject));
            OnPropertyChanged(nameof(CanUndoProject));
            OnPropertyChanged(nameof(CanRedoProject));
        }
    }

    public bool CanSaveProject => _currentProject is not null && IsProjectDirty && !IsSavingProject;
    public bool CanUndoProject => _currentProject is not null && _projectHistoryPosition > 0 && !IsSavingProject;
    public bool CanRedoProject => _currentProject is not null && _projectHistoryPosition < _projectHistory.Count && !IsSavingProject;

    public string WindowTitle
    {
        get
        {
            string projectName = string.IsNullOrWhiteSpace(_currentProjectPath)
                ? string.Empty
                : Path.GetFileName(_currentProjectPath);
            if (string.IsNullOrWhiteSpace(projectName))
                return "AgbSynth";

            return $"{projectName}{(IsProjectDirty ? " *" : string.Empty)} - AgbSynth";
        }
    }

    private void InitializeProjectEditing()
    {
        SongTableEntries.CollectionChanged += OnTrackedCollectionChanged;
        Sequences.CollectionChanged += OnTrackedCollectionChanged;
        VoiceGroupOptions.CollectionChanged += OnTrackedCollectionChanged;
        KeySplitOptions.CollectionChanged += OnTrackedCollectionChanged;
        DrumSetOptions.CollectionChanged += OnTrackedCollectionChanged;
        WaveMemoryRows.CollectionChanged += OnTrackedCollectionChanged;
        WaveDataRows.CollectionChanged += OnTrackedCollectionChanged;
    }

    private void BeginProjectSession(AgbSynthProjectFile project, string projectPath)
    {
        _suppressProjectTracking = true;
        _currentProject = project;
        _currentProjectPath = Path.GetFullPath(projectPath);
        _knownManagedFiles.Clear();
        ResetProjectHistory();
        IsProjectDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void CompleteProjectSession()
    {
        SynchronizeCurrentProjectModel();
        _knownManagedFiles.Clear();
        _knownManagedFiles.UnionWith(GetCurrentManagedFiles());
        ResetProjectHistory();
        IsProjectDirty = false;
        _suppressProjectTracking = false;
        OnPropertyChanged(nameof(CanSaveProject));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void CloseProjectSession()
    {
        _suppressProjectTracking = true;
        _currentProject = null;
        _currentProjectPath = null;
        _knownManagedFiles.Clear();
        ResetProjectHistory();
        IsProjectDirty = false;
        _suppressProjectTracking = false;
        OnPropertyChanged(nameof(CanSaveProject));
        OnPropertyChanged(nameof(WindowTitle));
    }

}
