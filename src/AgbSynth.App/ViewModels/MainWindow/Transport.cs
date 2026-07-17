using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgbSynth.App.Audio;
using AgbSynth.App.MIDI;
using AgbSynth.App.MP2K;
using AgbSynth.App.MP2K.Sequence;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _sequencePlaybackCts;
    private Task? _sequencePlaybackTask;
    private Mp2kMidiPlaybackSession? _midiPlaybackSession;
    private Mp2kSequenceAudioRuntime? _sequenceAudioRuntime;
    private long _lastSequenceSnapshotRevision;
    private bool _isSequencePlaying;
    private bool _isSequencePaused;
    private double _playbackProgress;
    private bool _isRecording;

    public bool IsSequencePlaying
    {
        get => _isSequencePlaying;
        private set
        {
            if (!SetField(ref _isSequencePlaying, value))
                return;

            OnPropertyChanged(nameof(CanPlaySequence));
            OnPropertyChanged(nameof(CanPauseSequence));
            OnPropertyChanged(nameof(CanStopSequence));
            OnPropertyChanged(nameof(PlayAreaPlayButtonText));
        }
    }

    public bool IsSequencePaused
    {
        get => _isSequencePaused;
        private set
        {
            if (!SetField(ref _isSequencePaused, value))
                return;

            OnPropertyChanged(nameof(SequencePauseButtonText));
        }
    }

    public bool CanPlaySequence => ResolvePlaybackSequence() is not null && !IsSequencePlaying;
    public bool CanPauseSequence => IsSequencePlaying;
    public bool CanStopSequence => IsSequencePlaying;
    public string SequencePauseButtonText => IsSequencePaused ? "RESUME" : "PAUSE";
    public string PlayAreaPlayButtonText => IsSequencePlaying ? "STOP" : "PLAY";

    public double PlaybackProgress
    {
        get => _playbackProgress;
        private set => SetField(ref _playbackProgress, Math.Clamp(value, 0, 100));
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (!SetField(ref _isRecording, value))
                return;

            OnPropertyChanged(nameof(RecordButtonText));
        }
    }

    public string RecordButtonText => IsRecording ? "REC ON" : "REC";

    public void StartOutputRecording()
    {
        AudioEngine.StartRecording();
        IsRecording = true;
        SequenceStatus = "Recording started.";
    }

    public AgbAudioRecording? StopOutputRecording()
    {
        if (_audioEngine is null || !_audioEngine.IsRecording)
        {
            IsRecording = false;
            SequenceStatus = "No recording is active.";
            return null;
        }

        AgbAudioRecording recording = _audioEngine.StopRecording();
        IsRecording = false;
        SequenceStatus = recording.HasSamples
            ? $"Recording stopped: {recording.DurationSeconds:0.00}s captured."
            : "Recording stopped: no audio data captured.";
        return recording;
    }

    public void CancelOutputRecording()
    {
        _audioEngine?.CancelRecording();
        IsRecording = false;
        SequenceStatus = "Recording canceled.";
    }

    public void NotifyRecordingSaved(string fileName, double durationSeconds)
    {
        SequenceStatus = $"Saved recording: {fileName} ({durationSeconds:0.00}s)";
    }

    public void NotifyRecordingSaveFailed(string message)
    {
        SequenceStatus = $"Recording save failed: {message}";
    }

    public async Task PlaySelectedSequenceAsync()
    {
        if (IsSequencePlaying)
            return;

        SequenceHeaderRow? sequence = ResolvePlaybackSequence();
        if (sequence is null)
            return;

        SynchronizePlaybackSelection(sequence);

        string? sequencePath = ResolveSequencePath(sequence);
        if (string.IsNullOrWhiteSpace(sequencePath) || !File.Exists(sequencePath))
        {
            SequenceStatus = $"Sequence file not found: {sequence.SequenceFilePath}";
            return;
        }

        SelectVoiceGroupForSequence(sequence);
        StopAllPreviewNotes();
        _currentPlayerPriority = 0;
        ResetMidiPlaybackState(defaultVolume: 100);
        PlaybackProgress = 0;

        MidiPlaybackFile midi;
        try
        {
            midi = await Task.Run(() => SequenceFileService.Load(sequencePath, sequence.SequenceFormat, _midiCcMapping));
        }
        catch (Exception ex)
        {
            SequenceStatus = $"Sequence load failed: {ex.Message}";
            return;
        }

        if (SelectedVoiceGroup is null)
        {
            SequenceStatus = "VoiceGroup is not available for this sequence.";
            return;
        }

        Mp2kPreparedVoiceBank voiceBank = BuildSequenceVoiceBank(midi);
        _currentPlayerPriority = Math.Clamp(sequence.Priority, 0, 255);
        ApplySequenceReverb(sequence);

        var cts = new CancellationTokenSource();
        _sequencePlaybackCts = cts;
        IsSequencePaused = false;
        IsSequencePlaying = true;
        SequenceStatus = $"Playing Song {sequence.SongId:D3}: {Path.GetFileName(sequencePath)}";

        _sequencePlaybackTask = RunSequencePlaybackAsync(sequence, midi, voiceBank, cts.Token);
        try
        {
            await _sequencePlaybackTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_sequencePlaybackCts, cts))
            {
                _sequencePlaybackCts?.Dispose();
                _sequencePlaybackCts = null;
                _sequencePlaybackTask = null;
            }

            StopPreviewNotesForSource(PreviewInputSource.Sequence);
            if (_midiPlaybackSession is { } activeSession)
                _audioEngine?.StopMp2kMidiPlaybackSession(activeSession);
            _midiPlaybackSession = null;
            _sequenceAudioRuntime = null;
            _lastSequenceSnapshotRevision = 0;
            _currentPlayerPriority = 0;
            if (_audioEngine is not null)
            {
                _audioEngine.ConfigureReverb(_reverbEnabled ? _soundModeReverbLevel : 0, _fixedDirectSoundSampleRate);
                _audioEngine.LfoStepRate = 48.0;
            }
            IsSequencePlaying = false;
            IsSequencePaused = false;

            if (cts.IsCancellationRequested)
                SequenceStatus = $"Stopped Song {sequence.SongId:D3}";
            else
                SequenceStatus = $"Finished Song {sequence.SongId:D3}";
            PlaybackProgress = 0;
        }
    }

    public void ResetMidiDefaults()
    {
        StopAllPreviewNotes();
        ResetMidiPlaybackState(defaultVolume: 100);
        SequenceStatus = "MIDI state reset.";
    }

    public async Task TogglePlayAreaPlaybackAsync()
    {
        if (IsSequencePlaying)
            await StopSelectedSequencePlaybackAsync();
        else
            await PlaySelectedSequenceAsync();
    }

    public async Task PlayPreviousSequenceAsync()
    {
        await SelectAdjacentSequenceAsync(-1);
    }

    public async Task PlayNextSequenceAsync()
    {
        await SelectAdjacentSequenceAsync(1);
    }

    private async Task SelectAdjacentSequenceAsync(int direction)
    {
        if (_playbackSelectionSource == PlaybackSelectionSource.SongTable)
        {
            await SelectAdjacentSongTableEntryAsync(direction);
            return;
        }

        await SelectAdjacentSongHeaderAsync(direction);
    }

    private async Task SelectAdjacentSongTableEntryAsync(int direction)
    {
        if (SongTableEntries.Count == 0)
            return;

        int currentIndex = SelectedSongTableEntry is null ? -1 : SongTableEntries.IndexOf(SelectedSongTableEntry);
        if (currentIndex < 0)
            currentIndex = direction > 0 ? -1 : 0;

        int nextIndex = Math.Clamp(currentIndex + direction, 0, SongTableEntries.Count - 1);
        if (nextIndex == currentIndex && SelectedSongTableEntry is not null)
            return;

        SongTableEntryRow nextSongTableEntry = SongTableEntries[nextIndex];
        SequenceHeaderRow? nextSequence = FindSequenceForSongTableEntry(nextSongTableEntry);
        if (nextSequence is null)
        {
            SequenceStatus = $"SongHeader not found for SongTable {nextSongTableEntry.IndexText}: {nextSongTableEntry.SongHeaderDisplay}";
            return;
        }

        bool restart = IsSequencePlaying;
        if (restart)
            await StopSelectedSequencePlaybackAsync();

        SetPlaybackSelection(nextSequence, nextSongTableEntry, PlaybackSelectionSource.SongTable);

        if (restart)
            await PlaySelectedSequenceAsync();
    }

    private async Task SelectAdjacentSongHeaderAsync(int direction)
    {
        if (Sequences.Count == 0)
            return;

        int currentIndex = SelectedSequence is null ? -1 : Sequences.IndexOf(SelectedSequence);
        if (currentIndex < 0 && SelectedSongTableEntry is not null)
            currentIndex = FindSequenceIndexForSongTableEntry(SelectedSongTableEntry);
        if (currentIndex < 0)
            currentIndex = direction > 0 ? -1 : 0;

        int nextIndex = Math.Clamp(currentIndex + direction, 0, Sequences.Count - 1);
        if (nextIndex == currentIndex && SelectedSequence is not null)
            return;

        SequenceHeaderRow nextSequence = Sequences[nextIndex];

        bool restart = IsSequencePlaying;
        if (restart)
            await StopSelectedSequencePlaybackAsync();

        SetPlaybackSelection(nextSequence, FindSongTableEntryForSequence(nextSequence), PlaybackSelectionSource.SongHeader);

        if (restart)
            await PlaySelectedSequenceAsync();
    }

    private SequenceHeaderRow? ResolvePlaybackSequence()
    {
        if (_playbackSelectionSource == PlaybackSelectionSource.SongTable &&
            SelectedSongTableEntry is { } songTableEntry &&
            FindSequenceForSongTableEntry(songTableEntry) is { } tableSequence)
        {
            return tableSequence;
        }

        return SelectedSequence
            ?? (SelectedSongTableEntry is { } fallbackEntry ? FindSequenceForSongTableEntry(fallbackEntry) : null);
    }

    private void SynchronizePlaybackSelection(SequenceHeaderRow sequence)
    {
        SongTableEntryRow? songTableEntry = _playbackSelectionSource == PlaybackSelectionSource.SongTable
            ? SelectedSongTableEntry
            : FindSongTableEntryForSequence(sequence);
        SetPlaybackSelection(sequence, songTableEntry, _playbackSelectionSource);
    }

    private void SetPlaybackSelection(SequenceHeaderRow sequence, SongTableEntryRow? songTableEntry, PlaybackSelectionSource source)
    {
        _suppressPlaybackSelectionSourceUpdate = true;
        try
        {
            SelectedSequence = sequence;
            if (songTableEntry is not null)
                SelectedSongTableEntry = songTableEntry;
        }
        finally
        {
            _suppressPlaybackSelectionSourceUpdate = false;
        }

        _playbackSelectionSource = source;
        OnPropertyChanged(nameof(CanPlaySequence));
    }

    private SequenceHeaderRow? FindSequenceForSongTableEntry(SongTableEntryRow songTableEntry)
    {
        int index = FindSequenceIndexForSongTableEntry(songTableEntry);
        return index >= 0 ? Sequences[index] : null;
    }

    private int FindSequenceIndexForSongTableEntry(SongTableEntryRow songTableEntry)
    {
        if (!string.IsNullOrWhiteSpace(songTableEntry.SongHeaderFilePath))
        {
            for (int i = 0; i < Sequences.Count; i++)
            {
                if (string.Equals(Sequences[i].FilePath, songTableEntry.SongHeaderFilePath, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        for (int i = 0; i < Sequences.Count; i++)
        {
            if (Sequences[i].SongId == songTableEntry.SongId ||
                Sequences[i].HeaderOffset == songTableEntry.HeaderOffset)
                return i;
        }

        return -1;
    }

    private SongTableEntryRow? FindSongTableEntryForSequence(SequenceHeaderRow sequence)
    {
        if (!string.IsNullOrWhiteSpace(sequence.FilePath))
        {
            SongTableEntryRow? byFile = SongTableEntries.FirstOrDefault(s =>
                string.Equals(s.SongHeaderFilePath, sequence.FilePath, StringComparison.OrdinalIgnoreCase));
            if (byFile is not null)
                return byFile;
        }

        return SongTableEntries.FirstOrDefault(s => s.SongId == sequence.SongId)
            ?? SongTableEntries.FirstOrDefault(s => s.HeaderOffset == sequence.HeaderOffset);
    }

    public void ToggleSelectedSequencePause()
    {
        if (!IsSequencePlaying)
            return;

        IsSequencePaused = !IsSequencePaused;
        if (_midiPlaybackSession is not null)
            _midiPlaybackSession.IsPaused = IsSequencePaused;
        SequenceStatus = IsSequencePaused
            ? $"Paused Song {SelectedSequence?.SongId:D3}"
            : $"Playing Song {SelectedSequence?.SongId:D3}";
    }

    public async Task StopSelectedSequencePlaybackAsync()
    {
        var cts = _sequencePlaybackCts;
        if (cts is null)
        {
            StopPreviewNotesForSource(PreviewInputSource.Sequence);
            return;
        }

        cts.Cancel();
        if (_midiPlaybackSession is { } session)
            _audioEngine?.StopMp2kMidiPlaybackSession(session);
        StopPreviewNotesForSource(PreviewInputSource.Sequence);
        if (_sequencePlaybackTask is not null)
        {
            try
            {
                await _sequencePlaybackTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunSequencePlaybackAsync(
        SequenceHeaderRow sequence,
        MidiPlaybackFile midi,
        Mp2kPreparedVoiceBank voiceBank,
        CancellationToken cancellationToken)
    {
        if (midi.Events.Count == 0)
            return;

        var runtime = new Mp2kSequenceAudioRuntime(
            AudioEngine,
            voiceBank,
            _midiCcMapping,
            sequence.Priority,
            _fixedDirectSoundSampleRate);
        foreach (AgbMixerStrip strip in MixerStrips)
            runtime.SetChannelEnabled(strip.Channel, strip.OutputEnabled);
        var session = new Mp2kMidiPlaybackSession(
            midi,
            _midiCcMapping.LoopStart,
            _midiCcMapping.LoopEnd,
            runtime.ProcessEvent,
            runtime.Tick,
            runtime.StopAll);
        _sequenceAudioRuntime = runtime;
        _midiPlaybackSession = session;
        AudioEngine.StartMp2kMidiPlaybackSession(session);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            AudioEngine.StopMp2kMidiPlaybackSession(session));
        try
        {
            await session.Completion.WaitAsync(cancellationToken);
        }
        finally
        {
            AudioEngine.StopMp2kMidiPlaybackSession(session);
        }
    }

    private void ApplySequenceReverb(SequenceHeaderRow sequence)
    {
        int level = (sequence.Reverb & 0x80) != 0
            ? sequence.Reverb & 0x7F
            : _soundModeReverbLevel;
        AudioEngine.ConfigureReverb(_reverbEnabled ? level : 0, _fixedDirectSoundSampleRate);
    }

    private string? ResolveSequencePath(SequenceHeaderRow sequence)
    {
        string sequenceFilePath = sequence.SequenceFilePath;
        if (string.IsNullOrWhiteSpace(sequenceFilePath))
            return null;
        if (Path.IsPathRooted(sequenceFilePath))
            return sequenceFilePath;
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return Path.GetFullPath(sequenceFilePath);

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        return Path.GetFullPath(Path.Combine(projectDirectory, sequenceFilePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void ResetMidiPlaybackState(int defaultVolume)
    {
        _audioEngine?.ResetTrackLfoStates();
        for (int channel = 0; channel < 16; channel++)
        {
            _channelVolumes[channel] = Math.Clamp(defaultVolume, 0, 127);
            _channelPans[channel] = 64;
            _channelBendRanges[channel] = 2;
            _channelBendRangeHighBits[channel] = 0;
            _channelPitchBends[channel] = 8192;
            _channelModDepths[channel] = 0;
            _channelModSpeeds[channel] = 22;
            _channelModTypes[channel] = 0;
            _channelModDelays[channel] = 0;
            _channelTunes[channel] = 64;
            _channelPriorities[channel] = 0;
            _channelPrograms[channel] = 0;
            _channelHasProgram[channel] = false;
            _channelXcmdTypes[channel] = -1;
            _channelXcmdAttacks[channel] = -1;
            _channelXcmdDecays[channel] = -1;
            _channelXcmdSustains[channel] = -1;
            _channelXcmdReleases[channel] = -1;
            _channelXcmdEchoVolumes[channel] = 0;
            _channelXcmdEchoLengths[channel] = 0;
            _channelXcmdLengths[channel] = -1;
            _channelXcmdSweeps[channel] = -1;
        }
    }

}
