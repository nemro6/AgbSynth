using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgbSynth.App.Audio;
using AgbSynth.App.MIDI;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _sequencePlaybackCts;
    private Task? _sequencePlaybackTask;
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

        string? midiPath = ResolveSequenceMidiPath(sequence);
        if (string.IsNullOrWhiteSpace(midiPath) || !File.Exists(midiPath))
        {
            SequenceStatus = $"MIDI file not found: {sequence.MidiFilePath}";
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
            midi = await Task.Run(() => MidiFileReader.Read(midiPath));
        }
        catch (Exception ex)
        {
            SequenceStatus = $"MIDI load failed: {ex.Message}";
            return;
        }

        _currentPlayerPriority = Math.Clamp(sequence.Priority, 0, 255);
        ApplySequenceReverb(sequence);
        ApplyLfoTempo(500_000);

        var cts = new CancellationTokenSource();
        _sequencePlaybackCts = cts;
        IsSequencePaused = false;
        IsSequencePlaying = true;
        SequenceStatus = $"Playing Song {sequence.SongId:D3}: {Path.GetFileName(midiPath)}";

        _sequencePlaybackTask = RunSequencePlaybackAsync(sequence, midi, cts.Token);
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

    private async Task RunSequencePlaybackAsync(SequenceHeaderRow sequence, MidiPlaybackFile midi, CancellationToken cancellationToken)
    {
        IReadOnlyList<MidiPlaybackEvent> events = midi.Events;
        if (events.Count == 0)
            return;

        int lastTick = Math.Max(1, events[^1].Tick);
        int microsecondsPerQuarter = 500_000;
        long currentTick = 0;
        var stopwatch = Stopwatch.StartNew();
        double targetMilliseconds = 0;
        int queueOrder = 0;
        var loops = new Dictionary<int, TrackLoopDefinition>();
        var queue = new PriorityQueue<ScheduledMidiOccurrence, (long Tick, int Order)>();

        foreach (IGrouping<int, MidiPlaybackEvent> trackGroup in events.GroupBy(e => e.TrackIndex))
        {
            MidiPlaybackEvent[] trackEvents = trackGroup.OrderBy(e => e.Tick).ThenBy(e => e.Order).ToArray();
            int loopStart = trackEvents
                .Where(IsLoopStartEvent)
                .Select(e => e.Tick)
                .DefaultIfEmpty(-1)
                .Min();
            int loopEnd = loopStart >= 0
                ? trackEvents.Where(IsLoopEndEvent).Where(e => e.Tick > loopStart).Select(e => e.Tick).DefaultIfEmpty(-1).Max()
                : -1;

            if (loopStart >= 0 && loopEnd > loopStart)
            {
                var definition = new TrackLoopDefinition(
                    trackGroup.Key,
                    loopStart,
                    loopEnd,
                    trackEvents.Where(e => !IsLoopMarker(e) && e.Tick >= loopStart && e.Tick < loopEnd).ToArray(),
                    trackEvents.Where(e => e.Kind == MidiPlaybackEventKind.NoteOff && e.Tick >= loopEnd).ToArray());
                loops[trackGroup.Key] = definition;

                foreach (MidiPlaybackEvent ev in trackEvents.Where(e => !IsLoopMarker(e) && e.Tick < loopEnd))
                    EnqueueMidiOccurrence(queue, new ScheduledMidiOccurrence(ev.Tick, ev, null, 0), ref queueOrder);
                foreach (MidiPlaybackEvent ev in definition.TailNoteOffs)
                    EnqueueMidiOccurrence(queue, new ScheduledMidiOccurrence(ev.Tick, ev, null, 0), ref queueOrder);
                EnqueueMidiOccurrence(
                    queue,
                    new ScheduledMidiOccurrence(loopEnd, null, definition.TrackIndex, 0),
                    ref queueOrder);
            }
            else
            {
                foreach (MidiPlaybackEvent ev in trackEvents.Where(e => !IsLoopMarker(e)))
                    EnqueueMidiOccurrence(queue, new ScheduledMidiOccurrence(ev.Tick, ev, null, 0), ref queueOrder);
            }
        }

        while (queue.TryDequeue(out ScheduledMidiOccurrence? occurrence, out _))
        {
            if (occurrence is null)
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            while (IsSequencePaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(20, cancellationToken);
                stopwatch.Restart();
            }

            long deltaTicks = Math.Max(0, occurrence.AbsoluteTick - currentTick);
            targetMilliseconds += TicksToMilliseconds(deltaTicks, microsecondsPerQuarter, midi.TicksPerQuarter);
            await WaitUntilAsync(stopwatch, targetMilliseconds, cancellationToken);
            currentTick = occurrence.AbsoluteTick;

            if (occurrence.LoopTrackIndex is int loopTrackIndex &&
                loops.TryGetValue(loopTrackIndex, out TrackLoopDefinition? loop) &&
                loop is not null)
            {
                int nextCycle = occurrence.LoopCycle + 1;
                long cycleOffset = (long)nextCycle * loop.Length;
                foreach (MidiPlaybackEvent ev in loop.BodyEvents)
                {
                    EnqueueMidiOccurrence(
                        queue,
                        new ScheduledMidiOccurrence(ev.Tick + cycleOffset, ev, null, nextCycle),
                        ref queueOrder);
                }
                foreach (MidiPlaybackEvent ev in loop.TailNoteOffs)
                {
                    EnqueueMidiOccurrence(
                        queue,
                        new ScheduledMidiOccurrence(ev.Tick + cycleOffset, ev, null, nextCycle),
                        ref queueOrder);
                }
                EnqueueMidiOccurrence(
                    queue,
                    new ScheduledMidiOccurrence(loop.EndTick + cycleOffset, null, loop.TrackIndex, nextCycle),
                    ref queueOrder);
                SequenceStatus = $"Looping Song {sequence.SongId:D3}: track {loop.TrackIndex}, tick {loop.StartTick}-{loop.EndTick}";
                continue;
            }

            if (occurrence.Event is not { } midiEvent)
                continue;

            PlaybackProgress = midiEvent.Tick / (double)lastTick * 100.0;
            ProcessSequenceMidiEvent(midiEvent, ref microsecondsPerQuarter);
        }
    }

    private bool IsLoopStartEvent(MidiPlaybackEvent ev) =>
        ev.Kind == MidiPlaybackEventKind.ControlChange && ev.Data1 == _midiCcMapping.LoopStart;

    private bool IsLoopEndEvent(MidiPlaybackEvent ev) =>
        ev.Kind == MidiPlaybackEventKind.ControlChange && ev.Data1 == _midiCcMapping.LoopEnd;

    private bool IsLoopMarker(MidiPlaybackEvent ev) => IsLoopStartEvent(ev) || IsLoopEndEvent(ev);

    private static void EnqueueMidiOccurrence(
        PriorityQueue<ScheduledMidiOccurrence, (long Tick, int Order)> queue,
        ScheduledMidiOccurrence occurrence,
        ref int order)
    {
        queue.Enqueue(occurrence, (occurrence.AbsoluteTick, order++));
    }

    private sealed record TrackLoopDefinition(
        int TrackIndex,
        int StartTick,
        int EndTick,
        IReadOnlyList<MidiPlaybackEvent> BodyEvents,
        IReadOnlyList<MidiPlaybackEvent> TailNoteOffs)
    {
        public int Length => EndTick - StartTick;
    }

    private sealed record ScheduledMidiOccurrence(
        long AbsoluteTick,
        MidiPlaybackEvent? Event,
        int? LoopTrackIndex,
        int LoopCycle);

    private void ProcessSequenceMidiEvent(MidiPlaybackEvent ev, ref int microsecondsPerQuarter)
    {
        int channel = Math.Clamp(ev.Channel, 0, 15);
        switch (ev.Kind)
        {
            case MidiPlaybackEventKind.NoteOn:
                PreviewNoteOnCore(ev.Data1, ev.Data2, channel, PreviewInputSource.Sequence, useMidiProgram: true, replacePsgSourceChannelNotes: true);
                break;
            case MidiPlaybackEventKind.NoteOff:
                PreviewNoteOffCore(ev.Data1, channel, PreviewInputSource.Sequence);
                break;
            case MidiPlaybackEventKind.ControlChange:
                ApplyMidiControlChange(channel, ev.Data1, ev.Data2);
                break;
            case MidiPlaybackEventKind.ProgramChange:
                ApplyMidiProgramChange(channel, ev.Data1);
                break;
            case MidiPlaybackEventKind.PitchBend:
                ApplyMidiPitchBend(channel, ev.Data1);
                break;
            case MidiPlaybackEventKind.Tempo:
                microsecondsPerQuarter = Math.Clamp(ev.Data3, 1, 60_000_000);
                ApplyLfoTempo(microsecondsPerQuarter);
                break;
        }
    }

    private void ApplySequenceReverb(SequenceHeaderRow sequence)
    {
        int level = (sequence.Reverb & 0x80) != 0
            ? sequence.Reverb & 0x7F
            : _soundModeReverbLevel;
        AudioEngine.ConfigureReverb(_reverbEnabled ? level : 0, _fixedDirectSoundSampleRate);
    }

    private void ApplyLfoTempo(int microsecondsPerQuarter)
    {
        microsecondsPerQuarter = Math.Clamp(microsecondsPerQuarter, 1, 60_000_000);
        // Exported files store the raw MP2K TEMPO byte as MIDI BPM at PPQN 48.
        int tempoByte = (int)Math.Round(60_000_000.0 / microsecondsPerQuarter, MidpointRounding.AwayFromZero);
        AudioEngine.SetMp2kTempoByte(tempoByte);
    }

    private string? ResolveSequenceMidiPath(SequenceHeaderRow sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence.MidiFilePath))
            return null;
        if (Path.IsPathRooted(sequence.MidiFilePath))
            return sequence.MidiFilePath;
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return Path.GetFullPath(sequence.MidiFilePath);

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        return Path.GetFullPath(Path.Combine(projectDirectory, sequence.MidiFilePath.Replace('/', Path.DirectorySeparatorChar)));
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

    private ChannelPlaybackSnapshot BuildSnapshotAt(IReadOnlyList<MidiPlaybackEvent> events, int tick)
    {
        var snapshot = ChannelPlaybackSnapshot.Create(defaultVolume: 100);
        foreach (var ev in events)
        {
            if (ev.Tick >= tick)
                break;

            int channel = Math.Clamp(ev.Channel, 0, 15);
            switch (ev.Kind)
            {
                case MidiPlaybackEventKind.ControlChange:
                    snapshot.ApplyControlChange(channel, ev.Data1, ev.Data2, _midiCcMapping);
                    break;
                case MidiPlaybackEventKind.ProgramChange:
                    snapshot.Programs[channel] = Math.Clamp(ev.Data1, 0, 127);
                    snapshot.HasProgram[channel] = true;
                    break;
                case MidiPlaybackEventKind.PitchBend:
                    snapshot.PitchBends[channel] = Math.Clamp(ev.Data1, 0, 16383);
                    break;
            }
        }

        return snapshot;
    }

    private void ApplyChannelSnapshot(ChannelPlaybackSnapshot snapshot)
    {
        Array.Copy(snapshot.Volumes, _channelVolumes, 16);
        Array.Copy(snapshot.Pans, _channelPans, 16);
        Array.Copy(snapshot.BendRanges, _channelBendRanges, 16);
        Array.Copy(snapshot.BendRangeHighBits, _channelBendRangeHighBits, 16);
        Array.Copy(snapshot.PitchBends, _channelPitchBends, 16);
        Array.Copy(snapshot.ModDepths, _channelModDepths, 16);
        Array.Copy(snapshot.ModSpeeds, _channelModSpeeds, 16);
        Array.Copy(snapshot.ModTypes, _channelModTypes, 16);
        Array.Copy(snapshot.ModDelays, _channelModDelays, 16);
        Array.Copy(snapshot.Tunes, _channelTunes, 16);
        Array.Copy(snapshot.Priorities, _channelPriorities, 16);
        Array.Copy(snapshot.Programs, _channelPrograms, 16);
        Array.Copy(snapshot.HasProgram, _channelHasProgram, 16);
        Array.Copy(snapshot.XcmdTypes, _channelXcmdTypes, 16);
        Array.Copy(snapshot.XcmdAttacks, _channelXcmdAttacks, 16);
        Array.Copy(snapshot.XcmdDecays, _channelXcmdDecays, 16);
        Array.Copy(snapshot.XcmdSustains, _channelXcmdSustains, 16);
        Array.Copy(snapshot.XcmdReleases, _channelXcmdReleases, 16);
        Array.Copy(snapshot.XcmdEchoVolumes, _channelXcmdEchoVolumes, 16);
        Array.Copy(snapshot.XcmdEchoLengths, _channelXcmdEchoLengths, 16);
        Array.Copy(snapshot.XcmdLengths, _channelXcmdLengths, 16);
        Array.Copy(snapshot.XcmdSweeps, _channelXcmdSweeps, 16);

        for (int channel = 0; channel < MixerStrips.Count && channel < 16; channel++)
        {
            var strip = MixerStrips[channel];
            strip.Volume = _channelVolumes[channel];
            strip.Pan = _channelPans[channel] - 64;
            strip.BendRange = _channelBendRanges[channel];
            strip.Modulation = _channelModDepths[channel];
            strip.ModSpeed = _channelModSpeeds[channel];
            strip.ModType = _channelModTypes[channel];
            strip.ModDelay = _channelModDelays[channel];
            strip.Tune = _channelTunes[channel];
            strip.Priority = _channelPriorities[channel];
            strip.ProgramId = _channelPrograms[channel];
            strip.InstrumentType = _channelHasProgram[channel] ? ResolveProgramTypeName(_channelPrograms[channel]) : "-";
            strip.PitchBend = 0;
        }
    }

    private static int FindFirstEventIndexAtOrAfter(IReadOnlyList<MidiPlaybackEvent> events, int tick)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Tick >= tick)
                return i;
        }

        return events.Count;
    }

    private static int FindTempoAt(IReadOnlyList<MidiPlaybackEvent> events, int tick)
    {
        int microsecondsPerQuarter = 500_000;
        foreach (var ev in events)
        {
            if (ev.Tick >= tick)
                break;
            if (ev.Kind == MidiPlaybackEventKind.Tempo)
                microsecondsPerQuarter = Math.Clamp(ev.Data3, 1, 60_000_000);
        }

        return microsecondsPerQuarter;
    }

    private static double TicksToMilliseconds(long ticks, int microsecondsPerQuarter, int ticksPerQuarter)
    {
        return ticks * (microsecondsPerQuarter / 1000.0) / Math.Max(1, ticksPerQuarter);
    }

    private static async Task WaitUntilAsync(Stopwatch stopwatch, double targetMilliseconds, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double remaining = targetMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
                return;

            // Leave the final millisecond to Stopwatch-based waiting. Task.Delay(1)
            // commonly overshoots on Windows, making adjacent MIDI intervals alternate
            // between late and short as the cumulative schedule catches up.
            if (remaining > 2.0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining - 1.0, 10.0)), cancellationToken);
                continue;
            }

            Thread.SpinWait(64);
        }
    }

    private sealed class ChannelPlaybackSnapshot
    {
        public int[] Volumes { get; } = new int[16];
        public int[] Pans { get; } = new int[16];
        public int[] BendRanges { get; } = new int[16];
        public int[] BendRangeHighBits { get; } = new int[16];
        public int[] PitchBends { get; } = new int[16];
        public int[] ModDepths { get; } = new int[16];
        public int[] ModSpeeds { get; } = new int[16];
        public int[] ModTypes { get; } = new int[16];
        public int[] ModDelays { get; } = new int[16];
        public int[] Tunes { get; } = new int[16];
        public int[] Priorities { get; } = new int[16];
        public int[] Programs { get; } = new int[16];
        public bool[] HasProgram { get; } = new bool[16];
        public int[] XcmdTypes { get; } = new int[16];
        public int[] XcmdAttacks { get; } = new int[16];
        public int[] XcmdDecays { get; } = new int[16];
        public int[] XcmdSustains { get; } = new int[16];
        public int[] XcmdReleases { get; } = new int[16];
        public int[] XcmdEchoVolumes { get; } = new int[16];
        public int[] XcmdEchoLengths { get; } = new int[16];
        public int[] XcmdLengths { get; } = new int[16];
        public int[] XcmdSweeps { get; } = new int[16];

        public static ChannelPlaybackSnapshot Create(int defaultVolume)
        {
            var snapshot = new ChannelPlaybackSnapshot();
            Array.Fill(snapshot.Volumes, Math.Clamp(defaultVolume, 0, 127));
            Array.Fill(snapshot.Pans, 64);
            Array.Fill(snapshot.BendRanges, 2);
            Array.Fill(snapshot.PitchBends, 8192);
            Array.Fill(snapshot.ModSpeeds, 22);
            Array.Fill(snapshot.Tunes, 64);
            Array.Fill(snapshot.XcmdTypes, -1);
            Array.Fill(snapshot.XcmdAttacks, -1);
            Array.Fill(snapshot.XcmdDecays, -1);
            Array.Fill(snapshot.XcmdSustains, -1);
            Array.Fill(snapshot.XcmdReleases, -1);
            Array.Fill(snapshot.XcmdLengths, -1);
            Array.Fill(snapshot.XcmdSweeps, -1);
            return snapshot;
        }

        public void ApplyControlChange(
            int channel,
            int controller,
            int value,
            AgbSynth.App.MP2K.MidiCcMapping midiCcMapping)
        {
            channel = Math.Clamp(channel, 0, 15);
            value = Math.Clamp(value, 0, 127);
            if (controller == midiCcMapping.Type)
                XcmdTypes[channel] = value;
            else if (controller == midiCcMapping.Attack)
                XcmdAttacks[channel] = value;
            else if (controller == midiCcMapping.Decay)
                XcmdDecays[channel] = value;
            else if (controller == midiCcMapping.Sustain)
                XcmdSustains[channel] = value;
            else if (controller == midiCcMapping.Release)
                XcmdReleases[channel] = value;
            else if (controller == midiCcMapping.EchoVolume)
                XcmdEchoVolumes[channel] = value;
            else if (controller == midiCcMapping.EchoLength)
                XcmdEchoLengths[channel] = value;
            else if (controller == midiCcMapping.Length)
                XcmdLengths[channel] = value;
            else if (controller == midiCcMapping.Sweep)
                XcmdSweeps[channel] = value;
            if (controller == midiCcMapping.Modulation)
                ModDepths[channel] = value;
            else if (controller == midiCcMapping.Volume)
                Volumes[channel] = value;
            else if (controller == midiCcMapping.Pan)
                Pans[channel] = value;
            else if (controller == midiCcMapping.BendRangeLow)
                BendRanges[channel] = (Math.Clamp(BendRangeHighBits[channel], 0, 1) << 7) | value;
            else if (controller == midiCcMapping.BendRangeHigh)
            {
                BendRangeHighBits[channel] = value & 0x01;
                BendRanges[channel] = (BendRangeHighBits[channel] << 7) | (BendRanges[channel] & 0x7F);
            }
            else if (controller == midiCcMapping.LfoSpeed)
                ModSpeeds[channel] = value;
            else if (controller == midiCcMapping.ModulationType)
                ModTypes[channel] = value;
            else if (controller == midiCcMapping.LfoDelay)
                ModDelays[channel] = value;
            else if (controller == midiCcMapping.Tune)
                Tunes[channel] = value;
            else if (controller == midiCcMapping.Priority)
                Priorities[channel] = value;
        }
    }
}
