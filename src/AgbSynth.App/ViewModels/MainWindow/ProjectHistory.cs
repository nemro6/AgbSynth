using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void OnTrackedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UntrackItems(e.OldItems);
        TrackItems(e.NewItems);

        if (e.Action == NotifyCollectionChangedAction.Reset && sender is IEnumerable items)
            TrackItems(items);

        if (_suppressProjectTracking || _isApplyingProjectHistory)
            return;

        if (sender is IList list && CollectionProjectEdit.TryCreate(list, e) is { } edit)
            RecordProjectEdit(edit);
        else
            MarkProjectDirty();
    }

    private void TrackItems(IEnumerable? items)
    {
        if (items is null)
            return;

        foreach (object? item in items)
        {
            if (item is INotifyPropertyChanged observable)
            {
                observable.PropertyChanged -= OnEditableItemPropertyChanged;
                observable.PropertyChanged += OnEditableItemPropertyChanged;
            }
            if (item is INotifyPropertyChanging changing)
            {
                changing.PropertyChanging -= OnEditableItemPropertyChanging;
                changing.PropertyChanging += OnEditableItemPropertyChanging;
            }

            RegisterKnownManagedFiles(item);
        }
    }

    private void UntrackItems(IEnumerable? items)
    {
        if (items is null)
            return;

        foreach (object? item in items)
        {
            if (item is INotifyPropertyChanged observable)
                observable.PropertyChanged -= OnEditableItemPropertyChanged;
            if (item is INotifyPropertyChanging changing)
                changing.PropertyChanging -= OnEditableItemPropertyChanging;
        }
    }

    private void TrackEditableVoiceRow(VoiceRow row)
    {
        row.PropertyChanged -= OnEditableItemPropertyChanged;
        row.PropertyChanged += OnEditableItemPropertyChanged;
        row.PropertyChanging -= OnEditableItemPropertyChanging;
        row.PropertyChanging += OnEditableItemPropertyChanging;
    }

    private void RegisterKnownManagedFiles(object? item)
    {
        switch (item)
        {
            case SequenceHeaderRow sequence:
                AddManagedPath(_knownManagedFiles, sequence.FilePath);
                break;
            case VoiceGroupOption voiceGroup:
                AddManagedPath(_knownManagedFiles, voiceGroup.FilePath);
                break;
            case KeySplitOption keySplit:
                AddManagedPath(_knownManagedFiles, keySplit.FilePath);
                break;
            case DrumSetOption drumSet:
                AddManagedPath(_knownManagedFiles, drumSet.FilePath);
                break;
            case WaveMemoryRow waveMemory:
                AddManagedPath(_knownManagedFiles, waveMemory.FilePath);
                if (!string.IsNullOrWhiteSpace(waveMemory.FilePath))
                    _knownManagedFiles.Add($"{ResolveProjectAssetPath(waveMemory.FilePath)}.meta.json");
                break;
            case WaveDataRow waveData:
                AddManagedPath(_knownManagedFiles, waveData.FilePath);
                break;
        }
    }

    private void OnEditableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressProjectTracking || _isApplyingProjectHistory || sender is null || string.IsNullOrWhiteSpace(e.PropertyName))
            return;

        if (!IsEditableProjectProperty(sender, e.PropertyName))
            return;

        var key = new PropertyChangeKey(sender, e.PropertyName);
        if (!_pendingPropertyChanges.Remove(key, out object? oldValue))
            return;

        PropertyInfo? property = sender.GetType().GetProperty(e.PropertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanRead || !property.CanWrite)
        {
            MarkProjectDirty();
            return;
        }

        object? newValue = property.GetValue(sender);
        if (!Equals(oldValue, newValue))
            RecordProjectEdit(new PropertyProjectEdit(sender, property, oldValue, newValue));
    }

    private void OnEditableItemPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (_suppressProjectTracking || _isApplyingProjectHistory || sender is null || string.IsNullOrWhiteSpace(e.PropertyName))
            return;
        if (!IsEditableProjectProperty(sender, e.PropertyName))
            return;

        PropertyInfo? property = sender.GetType().GetProperty(e.PropertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanRead || !property.CanWrite)
            return;

        var key = new PropertyChangeKey(sender, e.PropertyName);
        _pendingPropertyChanges.TryAdd(key, property.GetValue(sender));
    }

    private static bool IsEditableProjectProperty(object sender, string propertyName)
    {
        return sender switch
        {
            SongTableEntryRow => SongTableEditableProperties.Contains(propertyName),
            SequenceHeaderRow => SequenceEditableProperties.Contains(propertyName),
            VoiceGroupOption or KeySplitOption or DrumSetOption => propertyName == nameof(VoiceGroupOption.Label),
            WaveMemoryRow => propertyName is nameof(WaveMemoryRow.Label) or nameof(WaveMemoryRow.Note) or nameof(WaveMemoryRow.DataHex),
            WaveDataRow => propertyName is nameof(WaveDataRow.Label) or nameof(WaveDataRow.Note) or nameof(WaveDataRow.Loops) or nameof(WaveDataRow.LoopStart) or nameof(WaveDataRow.LoopEnd),
            VoiceRow => VoiceEditableProperties.Contains(propertyName),
            _ => false
        };
    }

    public void UndoProjectEdit()
    {
        if (!CanUndoProject)
            return;

        IProjectEdit edit = _projectHistory[_projectHistoryPosition - 1];
        ApplyProjectHistory(edit.Undo);
        _projectHistoryPosition--;
        UpdateProjectHistoryState();
        RomStatus = $"Undid: {edit.Description}";
    }

    public void RedoProjectEdit()
    {
        if (!CanRedoProject)
            return;

        IProjectEdit edit = _projectHistory[_projectHistoryPosition];
        ApplyProjectHistory(edit.Redo);
        _projectHistoryPosition++;
        UpdateProjectHistoryState();
        RomStatus = $"Redid: {edit.Description}";
    }

    private void ApplyProjectHistory(Action apply)
    {
        _isApplyingProjectHistory = true;
        try
        {
            apply();
            RefreshAfterProjectHistory();
        }
        finally
        {
            _pendingPropertyChanges.Clear();
            _isApplyingProjectHistory = false;
        }
    }

    private void RefreshAfterProjectHistory()
    {
        ReindexSongTableEntries();
        if (SelectedSongTableEntry is not null && !SongTableEntries.Contains(SelectedSongTableEntry))
            SelectedSongTableEntry = SongTableEntries.FirstOrDefault();
        if (SelectedSequence is not null && !Sequences.Contains(SelectedSequence))
            SelectedSequence = Sequences.FirstOrDefault();
        if (SelectedVoiceGroup is not null && !VoiceGroupOptions.Contains(SelectedVoiceGroup))
            SelectedVoiceGroup = VoiceGroupOptions.FirstOrDefault();
        else
            RefreshVoices();
        if (SelectedKeySplit is not null && !KeySplitOptions.Contains(SelectedKeySplit))
            SelectedKeySplit = KeySplitOptions.FirstOrDefault();
        else
            RefreshKeySplitRows();
        if (SelectedDrumSet is not null && !DrumSetOptions.Contains(SelectedDrumSet))
            SelectedDrumSet = DrumSetOptions.FirstOrDefault();
        else
            RefreshDrumSetRows();
        if (SelectedWaveMemory is not null && !WaveMemoryRows.Contains(SelectedWaveMemory))
            SelectedWaveMemory = WaveMemoryRows.FirstOrDefault();
        if (SelectedWaveData is not null && !WaveDataRows.Contains(SelectedWaveData))
            SelectedWaveData = WaveDataRows.FirstOrDefault();

        RefreshSequenceVoiceGroupDisplays();
        RefreshVoiceDataDisplays();
        OnPropertyChanged(nameof(CanEditSelectedSongTableEntry));
        OnPropertyChanged(nameof(CanEditSelectedSequence));
    }

    private void RecordProjectEdit(IProjectEdit edit)
    {
        if (_suppressProjectTracking || _isApplyingProjectHistory || _currentProject is null)
            return;

        if (_projectHistoryPosition < _projectHistory.Count)
        {
            _projectHistory.RemoveRange(_projectHistoryPosition, _projectHistory.Count - _projectHistoryPosition);
            if (_savedProjectHistoryPosition > _projectHistoryPosition)
                _savedProjectHistoryPosition = -1;
        }

        bool crossesSavePoint = _projectHistoryPosition == _savedProjectHistoryPosition;
        if (!crossesSavePoint &&
            _projectHistoryPosition > 0 &&
            _projectHistoryPosition == _projectHistory.Count &&
            _projectHistory[^1].TryMerge(edit))
        {
            if (_projectHistory[^1].IsNoOp)
            {
                _projectHistory.RemoveAt(_projectHistory.Count - 1);
                _projectHistoryPosition--;
            }
            UpdateProjectHistoryState();
            return;
        }

        _projectHistory.Add(edit);
        _projectHistoryPosition++;
        UpdateProjectHistoryState();
    }

    private void RecordProjectValueEdit<T>(
        object target,
        string key,
        string description,
        T oldValue,
        T newValue,
        Action<T> apply,
        bool allowMerge = false)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        RecordProjectEdit(new ValueProjectEdit<T>(target, key, description, oldValue, newValue, apply, allowMerge));
    }

    private void RecordKeySplitStateEdit(KeySplitProjectInfo target, KeySplitProjectInfo oldValue, string description)
    {
        KeySplitProjectInfo newValue = CloneKeySplitProjectInfo(target);
        RecordProjectValueEdit(
            target,
            "KeySplitState",
            description,
            oldValue,
            newValue,
            value => ApplyKeySplitState(target, value));
    }

    private void RecordVoiceListStateEdit(IList<VoiceProjectInfo> target, List<VoiceProjectInfo> oldValue, string description)
    {
        List<VoiceProjectInfo> newValue = target.Select(CloneVoiceProjectInfo).ToList();
        RecordProjectValueEdit(
            target,
            "VoiceListState",
            description,
            oldValue,
            newValue,
            value => ApplyVoiceListState(target, value));
    }

    private void RecordVoiceStateEdit(VoiceRow target, VoiceProjectInfo oldValue, string description)
    {
        VoiceProjectInfo newValue = CloneVoiceProjectInfo(target.ToProjectInfo());
        RecordProjectValueEdit(
            target,
            "VoiceState",
            description,
            oldValue,
            newValue,
            value => target.ApplyProjectInfo(CloneVoiceProjectInfo(value)));
    }

    private void ApplyProjectMutationWithoutHistory(Action mutation)
    {
        bool wasApplying = _isApplyingProjectHistory;
        _isApplyingProjectHistory = true;
        try
        {
            mutation();
        }
        finally
        {
            _pendingPropertyChanges.Clear();
            _isApplyingProjectHistory = wasApplying;
        }
    }

    private static void ApplyKeySplitState(KeySplitProjectInfo target, KeySplitProjectInfo value)
    {
        KeySplitProjectInfo copy = CloneKeySplitProjectInfo(value);
        target.Label = copy.Label;
        target.RegionTableOffset = copy.RegionTableOffset;
        target.KeyMapOffset = copy.KeyMapOffset;
        target.KeyMapHex = copy.KeyMapHex;
        target.RawRegionTableHex = copy.RawRegionTableHex;
        target.Regions.Clear();
        target.Regions.AddRange(copy.Regions);
    }

    private static void ApplyVoiceListState(IList<VoiceProjectInfo> target, IEnumerable<VoiceProjectInfo> value)
    {
        target.Clear();
        foreach (VoiceProjectInfo voice in value)
            target.Add(CloneVoiceProjectInfo(voice));
    }

    private void ResetProjectHistory()
    {
        _projectHistory.Clear();
        _pendingPropertyChanges.Clear();
        _projectHistoryPosition = 0;
        _savedProjectHistoryPosition = 0;
        _untrackedProjectRevision = 0;
        _savedUntrackedProjectRevision = 0;
        OnPropertyChanged(nameof(CanUndoProject));
        OnPropertyChanged(nameof(CanRedoProject));
    }

    private void UpdateProjectHistoryState()
    {
        OnPropertyChanged(nameof(CanUndoProject));
        OnPropertyChanged(nameof(CanRedoProject));
        UpdateProjectDirtyState();
    }

    private void UpdateProjectDirtyState()
    {
        IsProjectDirty = _untrackedProjectRevision != _savedUntrackedProjectRevision ||
            _projectHistoryPosition != _savedProjectHistoryPosition;
    }

    private void MarkProjectDirty()
    {
        if (_suppressProjectTracking || _isApplyingProjectHistory || _currentProject is null)
            return;

        _untrackedProjectRevision++;
        UpdateProjectDirtyState();
    }

    private readonly record struct PropertyChangeKey(object Target, string PropertyName);

    private interface IProjectEdit
    {
        string Description { get; }
        void Undo();
        void Redo();
        bool TryMerge(IProjectEdit newerEdit);
        bool IsNoOp { get; }
    }

    private sealed class PropertyProjectEdit(
        object target,
        PropertyInfo property,
        object? oldValue,
        object? newValue) : IProjectEdit
    {
        private object? _newValue = newValue;

        public string Description => $"Edit {property.Name}";
        public bool IsNoOp => Equals(oldValue, _newValue);

        public void Undo() => property.SetValue(target, oldValue);

        public void Redo() => property.SetValue(target, _newValue);

        public bool TryMerge(IProjectEdit newerEdit)
        {
            if (newerEdit is not PropertyProjectEdit newer ||
                !ReferenceEquals(target, newer.Target) ||
                property != newer.Property)
            {
                return false;
            }

            _newValue = newer._newValue;
            return true;
        }

        private object Target => target;
        private PropertyInfo Property => property;
    }

    private sealed class ValueProjectEdit<T>(
        object target,
        string key,
        string description,
        T oldValue,
        T newValue,
        Action<T> apply,
        bool allowMerge) : IProjectEdit
    {
        private T _newValue = newValue;

        public string Description => description;
        public bool IsNoOp => EqualityComparer<T>.Default.Equals(oldValue, _newValue);

        public void Undo() => apply(oldValue);

        public void Redo() => apply(_newValue);

        public bool TryMerge(IProjectEdit newerEdit)
        {
            if (!allowMerge || newerEdit is not ValueProjectEdit<T> newer)
                return false;
            if (!newer.AllowMerge ||
                !ReferenceEquals(target, newer.Target) ||
                !string.Equals(key, newer.Key, StringComparison.Ordinal))
            {
                return false;
            }

            _newValue = newer._newValue;
            return true;
        }

        private object Target => target;
        private string Key => key;
        private bool AllowMerge => allowMerge;
    }

    private sealed class CollectionProjectEdit : IProjectEdit
    {
        private readonly IList _collection;
        private readonly NotifyCollectionChangedAction _action;
        private readonly object?[] _items;
        private readonly int _oldIndex;
        private readonly int _newIndex;

        private CollectionProjectEdit(
            IList collection,
            NotifyCollectionChangedAction action,
            object?[] items,
            int oldIndex,
            int newIndex)
        {
            _collection = collection;
            _action = action;
            _items = items;
            _oldIndex = oldIndex;
            _newIndex = newIndex;
        }

        public string Description => _action switch
        {
            NotifyCollectionChangedAction.Add => "Add item",
            NotifyCollectionChangedAction.Remove => "Delete item",
            NotifyCollectionChangedAction.Move => "Move item",
            NotifyCollectionChangedAction.Replace => "Replace item",
            _ => "Edit list"
        };
        public bool IsNoOp => false;

        public static CollectionProjectEdit? TryCreate(IList collection, NotifyCollectionChangedEventArgs e)
        {
            object?[] items = (e.NewItems ?? e.OldItems)?.Cast<object?>().ToArray() ?? [];
            return e.Action switch
            {
                NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0 =>
                    new CollectionProjectEdit(collection, e.Action, items, -1, e.NewStartingIndex),
                NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 =>
                    new CollectionProjectEdit(collection, e.Action, items, e.OldStartingIndex, -1),
                NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0 =>
                    new CollectionProjectEdit(collection, e.Action, items, e.OldStartingIndex, e.NewStartingIndex),
                _ => null
            };
        }

        public void Undo()
        {
            switch (_action)
            {
                case NotifyCollectionChangedAction.Add:
                    RemoveAt(_newIndex, _items.Length);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    InsertAt(_oldIndex, _items);
                    break;
                case NotifyCollectionChangedAction.Move:
                    Move(_newIndex, _oldIndex);
                    break;
            }
        }

        public void Redo()
        {
            switch (_action)
            {
                case NotifyCollectionChangedAction.Add:
                    InsertAt(_newIndex, _items);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemoveAt(_oldIndex, _items.Length);
                    break;
                case NotifyCollectionChangedAction.Move:
                    Move(_oldIndex, _newIndex);
                    break;
            }
        }

        public bool TryMerge(IProjectEdit newerEdit) => false;

        private void InsertAt(int index, IEnumerable<object?> items)
        {
            int target = index;
            foreach (object? item in items)
                _collection.Insert(target++, item);
        }

        private void RemoveAt(int index, int count)
        {
            for (int i = 0; i < count; i++)
                _collection.RemoveAt(index);
        }

        private void Move(int fromIndex, int toIndex)
        {
            object? item = _collection[fromIndex];
            _collection.RemoveAt(fromIndex);
            _collection.Insert(toIndex, item);
        }
    }
}
