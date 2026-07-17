using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgbSynth.App.MIDI;

namespace AgbSynth.App.MP2K;

/// <summary>
/// Advances an exported MP2K MIDI stream on the same integer clock used by MPlayMain.
/// One call to <see cref="AdvanceVBlank"/> represents one GBA video frame.
/// </summary>
public sealed class Mp2kMidiPlaybackSession
{
    public const int TempoThreshold = 150;
    public const int InitialTempoIncrement = 150;

    private readonly Action<MidiPlaybackEvent> _eventSink;
    private readonly Action<long>? _tickSink;
    private readonly Action? _stopSink;
    private readonly Dictionary<int, TrackLoopDefinition> _loops = new();
    private readonly PriorityQueue<ScheduledMidiOccurrence, (long Tick, int Order)> _queue = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _queueOrder;
    private int _tempoCounter;
    private int _tempoIncrement = InitialTempoIncrement;
    private long _nextTick;
    private long _processedStepCount;
    private long _vblankCount;
    private int _lastEventSourceTick;
    private int _sourceEndTick;
    private int _progressLoopStart = -1;
    private int _progressLoopEnd = -1;
    private int _paused;
    private int _stopped;
    private int _completed;

    public Mp2kMidiPlaybackSession(
        MidiPlaybackFile midi,
        int loopStartController,
        int loopEndController,
        Action<MidiPlaybackEvent> eventSink,
        Action<long>? tickSink = null,
        Action? stopSink = null)
    {
        ArgumentNullException.ThrowIfNull(midi);
        _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
        _tickSink = tickSink;
        _stopSink = stopSink;

        BuildOccurrenceQueue(midi.Events, loopStartController, loopEndController);
        if (_queue.Count == 0)
            Complete();
    }

    public Task Completion => _completion.Task;
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;
    public bool IsStopped => Volatile.Read(ref _stopped) != 0;

    public bool IsPaused
    {
        get => Volatile.Read(ref _paused) != 0;
        set => Volatile.Write(ref _paused, value ? 1 : 0);
    }

    public long NextTick => Interlocked.Read(ref _nextTick);
    public long ProcessedStepCount => Interlocked.Read(ref _processedStepCount);
    public long VBlankCount => Interlocked.Read(ref _vblankCount);
    public int TempoCounter => Volatile.Read(ref _tempoCounter);
    public int TempoIncrement => Volatile.Read(ref _tempoIncrement);
    public int LastEventSourceTick => Volatile.Read(ref _lastEventSourceTick);
    public int SourceEndTick => Volatile.Read(ref _sourceEndTick);

    public int SourcePositionTick
    {
        get
        {
            long absoluteTick = Math.Max(0, NextTick - 1);
            int loopStart = Volatile.Read(ref _progressLoopStart);
            int loopEnd = Volatile.Read(ref _progressLoopEnd);
            if (loopStart >= 0 && loopEnd > loopStart && absoluteTick >= loopEnd)
                return loopStart + (int)((absoluteTick - loopEnd) % (loopEnd - loopStart));
            return (int)Math.Min(absoluteTick, int.MaxValue);
        }
    }

    /// <summary>
    /// Runs one MPlayMain update. The callback is invoked after each MP2K step so
    /// envelope/LFO state can advance in the same order as the driver.
    /// </summary>
    public int AdvanceVBlank(Action? afterStep = null)
    {
        Interlocked.Increment(ref _vblankCount);
        if (IsPaused || IsStopped || IsCompleted)
            return 0;

        int counter = _tempoCounter + _tempoIncrement;
        int steps = 0;
        while (counter >= TempoThreshold && !IsStopped && !IsCompleted)
        {
            ProcessTick(_nextTick);
            _tickSink?.Invoke(_nextTick);
            Interlocked.Increment(ref _nextTick);
            Interlocked.Increment(ref _processedStepCount);
            counter -= TempoThreshold;
            _tempoCounter = counter;
            afterStep?.Invoke();
            steps++;
        }

        _tempoCounter = counter;
        return steps;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _stopSink?.Invoke();
        _completion.TrySetCanceled();
    }

    private void ProcessTick(long tick)
    {
        while (_queue.TryPeek(out ScheduledMidiOccurrence? occurrence, out (long Tick, int Order) priority) &&
               priority.Tick <= tick)
        {
            _queue.Dequeue();
            if (occurrence is null)
                continue;

            if (occurrence.LoopTrackIndex is int loopTrackIndex &&
                _loops.TryGetValue(loopTrackIndex, out TrackLoopDefinition? loop))
            {
                EnqueueNextLoopCycle(loop, occurrence.LoopCycle);
                continue;
            }

            if (occurrence.Event is not { } midiEvent)
                continue;

            if (midiEvent.Kind == MidiPlaybackEventKind.Tempo)
                _tempoIncrement = DecodeTempoIncrement(midiEvent.Data3);
            _lastEventSourceTick = midiEvent.Tick;
            _eventSink(midiEvent);
        }

        if (_queue.Count == 0)
            Complete();
    }

    private void EnqueueNextLoopCycle(TrackLoopDefinition loop, int currentCycle)
    {
        int nextCycle = currentCycle + 1;
        long cycleOffset = (long)nextCycle * loop.Length;
        foreach (MidiPlaybackEvent midiEvent in loop.BodyEvents)
        {
            Enqueue(new ScheduledMidiOccurrence(
                midiEvent.Tick + cycleOffset,
                midiEvent,
                null,
                nextCycle));
        }

        foreach (MidiPlaybackEvent midiEvent in loop.TailNoteOffs)
        {
            Enqueue(new ScheduledMidiOccurrence(
                midiEvent.Tick + cycleOffset,
                midiEvent,
                null,
                nextCycle));
        }

        Enqueue(new ScheduledMidiOccurrence(
            loop.EndTick + cycleOffset,
            null,
            loop.TrackIndex,
            nextCycle));
    }

    private void BuildOccurrenceQueue(
        IReadOnlyList<MidiPlaybackEvent> events,
        int loopStartController,
        int loopEndController)
    {
        _sourceEndTick = events.Count == 0 ? 0 : events.Max(midiEvent => midiEvent.Tick);
        foreach (IGrouping<int, MidiPlaybackEvent> trackGroup in events.GroupBy(midiEvent => midiEvent.TrackIndex))
        {
            MidiPlaybackEvent[] trackEvents = trackGroup
                .OrderBy(midiEvent => midiEvent.Tick)
                .ThenBy(midiEvent => midiEvent.Order)
                .ToArray();
            int loopStart = trackEvents
                .Where(midiEvent => IsLoopStart(midiEvent, loopStartController))
                .Select(midiEvent => midiEvent.Tick)
                .DefaultIfEmpty(-1)
                .Min();
            int loopEnd = loopStart >= 0
                ? trackEvents
                    .Where(midiEvent => IsLoopEnd(midiEvent, loopEndController) && midiEvent.Tick > loopStart)
                    .Select(midiEvent => midiEvent.Tick)
                    .DefaultIfEmpty(-1)
                    .Max()
                : -1;

            if (loopStart >= 0 && loopEnd > loopStart)
            {
                MidiPlaybackEvent[] initialTailNoteOffs = trackEvents
                    .Where(midiEvent =>
                        midiEvent.Kind == MidiPlaybackEventKind.NoteOff &&
                        midiEvent.Tick >= loopEnd)
                    .ToArray();
                var loop = new TrackLoopDefinition(
                    trackGroup.Key,
                    loopStart,
                    loopEnd,
                    trackEvents
                        .Where(midiEvent =>
                            !IsLoopMarker(midiEvent, loopStartController, loopEndController) &&
                            midiEvent.Tick >= loopStart &&
                            midiEvent.Tick < loopEnd)
                        .ToArray(),
                    FindRepeatedTailNoteOffs(trackEvents, loopStart, loopEnd));
                _loops[trackGroup.Key] = loop;

                foreach (MidiPlaybackEvent midiEvent in trackEvents.Where(midiEvent =>
                             !IsLoopMarker(midiEvent, loopStartController, loopEndController) &&
                             midiEvent.Tick < loopEnd))
                {
                    Enqueue(new ScheduledMidiOccurrence(midiEvent.Tick, midiEvent, null, 0));
                }

                foreach (MidiPlaybackEvent midiEvent in initialTailNoteOffs)
                    Enqueue(new ScheduledMidiOccurrence(midiEvent.Tick, midiEvent, null, 0));
                Enqueue(new ScheduledMidiOccurrence(loopEnd, null, loop.TrackIndex, 0));
            }
            else
            {
                foreach (MidiPlaybackEvent midiEvent in trackEvents.Where(midiEvent =>
                             !IsLoopMarker(midiEvent, loopStartController, loopEndController)))
                {
                    Enqueue(new ScheduledMidiOccurrence(midiEvent.Tick, midiEvent, null, 0));
                }
            }
        }

        if (_loops.Count > 0)
        {
            _progressLoopStart = _loops.Values.Min(loop => loop.StartTick);
            _progressLoopEnd = _loops.Values.Max(loop => loop.EndTick);
            _sourceEndTick = _progressLoopEnd;
        }

        AttachLegacyConductorTempos(events);
    }

    private void AttachLegacyConductorTempos(IReadOnlyList<MidiPlaybackEvent> events)
    {
        // Older AgbSynth exports moved every MP2K TEMPO command to the conductor
        // track. That preserved the first pass but lost the command's track loop.
        // New exports also retain a source-track copy, so only repair unmatched
        // conductor events here.
        var sourceTempoKeys = events
            .Where(midiEvent => midiEvent.TrackIndex > 0 && midiEvent.Kind == MidiPlaybackEventKind.Tempo)
            .Select(midiEvent => (midiEvent.Tick, midiEvent.Data3))
            .ToHashSet();

        foreach (MidiPlaybackEvent tempo in events.Where(midiEvent =>
                     midiEvent.TrackIndex == 0 &&
                     midiEvent.Kind == MidiPlaybackEventKind.Tempo &&
                     !sourceTempoKeys.Contains((midiEvent.Tick, midiEvent.Data3))))
        {
            TrackLoopDefinition? target = _loops.Values
                .Where(loop => tempo.Tick >= loop.StartTick && tempo.Tick < loop.EndTick)
                .OrderBy(loop => loop.TrackIndex)
                .FirstOrDefault();
            if (target is null || target.BodyEvents.Any(midiEvent =>
                    midiEvent.Kind == MidiPlaybackEventKind.Tempo &&
                    midiEvent.Tick == tempo.Tick &&
                    midiEvent.Data3 == tempo.Data3))
            {
                continue;
            }

            MidiPlaybackEvent[] body = target.BodyEvents
                .Append(tempo)
                .OrderBy(midiEvent => midiEvent.Tick)
                .ThenBy(midiEvent => midiEvent.Order)
                .ToArray();
            _loops[target.TrackIndex] = target with { BodyEvents = body };
        }
    }

    private void Enqueue(ScheduledMidiOccurrence occurrence)
    {
        _queue.Enqueue(occurrence, (occurrence.AbsoluteTick, _queueOrder++));
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        _completion.TrySetResult();
    }

    internal static int DecodeTempoIncrement(int microsecondsPerQuarter)
    {
        microsecondsPerQuarter = Math.Clamp(microsecondsPerQuarter, 1, 60_000_000);
        int tempoByte = (int)Math.Round(
            60_000_000.0 / microsecondsPerQuarter,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(tempoByte, 1, 255) * 2;
    }

    private static bool IsLoopStart(MidiPlaybackEvent midiEvent, int controller) =>
        midiEvent.Kind == MidiPlaybackEventKind.ControlChange && midiEvent.Data1 == controller;

    private static bool IsLoopEnd(MidiPlaybackEvent midiEvent, int controller) =>
        midiEvent.Kind == MidiPlaybackEventKind.ControlChange && midiEvent.Data1 == controller;

    private static bool IsLoopMarker(MidiPlaybackEvent midiEvent, int startController, int endController) =>
        IsLoopStart(midiEvent, startController) || IsLoopEnd(midiEvent, endController);

    private static IReadOnlyList<MidiPlaybackEvent> FindRepeatedTailNoteOffs(
        IReadOnlyList<MidiPlaybackEvent> trackEvents,
        int loopStart,
        int loopEnd)
    {
        var pendingNotes = new Dictionary<(int Channel, int Note), Queue<MidiPlaybackEvent>>();
        var repeatedTailNoteOffs = new List<MidiPlaybackEvent>();
        foreach (MidiPlaybackEvent midiEvent in trackEvents)
        {
            var key = (midiEvent.Channel, midiEvent.Data1);
            if (midiEvent.Kind == MidiPlaybackEventKind.NoteOn && midiEvent.Data2 > 0)
            {
                if (!pendingNotes.TryGetValue(key, out Queue<MidiPlaybackEvent>? pending))
                {
                    pending = new Queue<MidiPlaybackEvent>();
                    pendingNotes[key] = pending;
                }

                pending.Enqueue(midiEvent);
                continue;
            }

            if (midiEvent.Kind != MidiPlaybackEventKind.NoteOff ||
                !pendingNotes.TryGetValue(key, out Queue<MidiPlaybackEvent>? noteOns) ||
                noteOns.Count == 0)
            {
                continue;
            }

            MidiPlaybackEvent noteOn = noteOns.Dequeue();
            if (noteOn.Tick >= loopStart && noteOn.Tick < loopEnd && midiEvent.Tick >= loopEnd)
                repeatedTailNoteOffs.Add(midiEvent);
        }

        return repeatedTailNoteOffs;
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
}
