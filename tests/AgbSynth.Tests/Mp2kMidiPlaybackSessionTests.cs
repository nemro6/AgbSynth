using AgbSynth.App.Audio;
using AgbSynth.App.MIDI;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class Mp2kMidiPlaybackSessionTests
{
    [Fact]
    public void AdvanceVBlank_DefaultAndTempo75_ProcessOneStepPerFrame()
    {
        var processed = new List<(int VBlank, int Tick)>();
        int vblank = 0;
        var session = CreateSession(
            [
                Tempo(tick: 0, order: 0, bpm: 75),
                NoteOn(tick: 0, order: 1, note: 60),
                NoteOn(tick: 1, order: 2, note: 61),
                NoteOn(tick: 2, order: 3, note: 62)
            ],
            midiEvent =>
            {
                if (midiEvent.Kind == MidiPlaybackEventKind.NoteOn)
                    processed.Add((vblank, midiEvent.Tick));
            });

        for (vblank = 1; vblank <= 3; vblank++)
            Assert.Equal(1, session.AdvanceVBlank());

        Assert.Equal([(1, 0), (2, 1), (3, 2)], processed);
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void AdvanceVBlank_Tempo150_ProcessesTwoStepsFromSecondFrame()
    {
        var processed = new List<(int VBlank, int Tick)>();
        int vblank = 0;
        var session = CreateSession(
            [
                Tempo(tick: 0, order: 0, bpm: 150),
                NoteOn(tick: 0, order: 1, note: 60),
                NoteOn(tick: 1, order: 2, note: 61),
                NoteOn(tick: 2, order: 3, note: 62)
            ],
            midiEvent =>
            {
                if (midiEvent.Kind == MidiPlaybackEventKind.NoteOn)
                    processed.Add((vblank, midiEvent.Tick));
            });

        vblank = 1;
        Assert.Equal(1, session.AdvanceVBlank());
        vblank = 2;
        Assert.Equal(2, session.AdvanceVBlank());

        Assert.Equal([(1, 0), (2, 1), (2, 2)], processed);
    }

    [Fact]
    public void AdvanceVBlank_Tempo50_PreservesIntegerTempoRemainder()
    {
        var processed = new List<(int VBlank, int Tick)>();
        int vblank = 0;
        var session = CreateSession(
            [
                Tempo(tick: 0, order: 0, bpm: 50),
                NoteOn(tick: 0, order: 1, note: 60),
                NoteOn(tick: 1, order: 2, note: 61),
                NoteOn(tick: 2, order: 3, note: 62),
                NoteOn(tick: 3, order: 4, note: 63)
            ],
            midiEvent =>
            {
                if (midiEvent.Kind == MidiPlaybackEventKind.NoteOn)
                    processed.Add((vblank, midiEvent.Tick));
            });

        for (vblank = 1; vblank <= 6; vblank++)
            session.AdvanceVBlank();

        Assert.Equal([(1, 0), (3, 1), (4, 2), (6, 3)], processed);
    }

    [Fact]
    public void AdvanceVBlank_PerTrackLoop_DoesNotRestartOtherTracks()
    {
        const int loopStart = 89;
        const int loopEnd = 90;
        var processed = new List<(int VBlank, int Track, int Note)>();
        int vblank = 0;
        var midi = new MidiPlaybackFile(
            48,
            [
                Control(tick: 1, order: 0, track: 1, loopStart),
                NoteOn(tick: 1, order: 1, note: 60, track: 1),
                NoteOff(tick: 2, order: 2, note: 60, track: 1),
                Control(tick: 3, order: 3, track: 1, loopEnd),
                NoteOn(tick: 4, order: 4, note: 72, track: 2)
            ]);
        var session = new Mp2kMidiPlaybackSession(
            midi,
            loopStart,
            loopEnd,
            midiEvent =>
            {
                if (midiEvent.Kind == MidiPlaybackEventKind.NoteOn)
                    processed.Add((vblank, midiEvent.TrackIndex, midiEvent.Data1));
            });

        for (vblank = 1; vblank <= 7; vblank++)
            session.AdvanceVBlank();

        Assert.Equal(3, processed.Count(item => item.Track == 1 && item.Note == 60));
        Assert.Single(processed, item => item.Track == 2 && item.Note == 72);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public void AdvanceVBlank_SourceTrackTempoRepeatsWithTrackLoop()
    {
        const int loopStart = 89;
        const int loopEnd = 90;
        var midi = new MidiPlaybackFile(
            48,
            [
                Control(tick: 1, order: 0, track: 1, loopStart),
                Tempo(tick: 1, order: 1, bpm: 50) with { TrackIndex = 1 },
                Tempo(tick: 2, order: 2, bpm: 75) with { TrackIndex = 2 },
                Control(tick: 3, order: 3, track: 1, loopEnd),
                NoteOn(tick: 8, order: 4, note: 72, track: 2)
            ]);
        var session = new Mp2kMidiPlaybackSession(midi, loopStart, loopEnd, _ => { });

        while (session.NextTick <= 3)
            session.AdvanceVBlank();

        Assert.Equal(100, session.TempoIncrement);
    }

    [Fact]
    public void AdvanceVBlank_PauseDoesNotConsumeTempoOrTicks()
    {
        var session = CreateSession(
            [NoteOn(tick: 0, order: 0, note: 60), NoteOn(tick: 1, order: 1, note: 61)],
            _ => { });

        session.IsPaused = true;
        Assert.Equal(0, session.AdvanceVBlank());
        Assert.Equal(0, session.NextTick);
        Assert.Equal(0, session.TempoCounter);

        session.IsPaused = false;
        Assert.Equal(1, session.AdvanceVBlank());
        Assert.Equal(1, session.NextTick);
    }

    [Fact]
    public void AudioEngine_DispatchesFirstTickOnExactGbaVBlankBoundary()
    {
        int eventCount = 0;
        var session = CreateSession([NoteOn(tick: 0, order: 0, note: 60)], _ => eventCount++);
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false
        };
        engine.StartMp2kMidiPlaybackSession(session);

        int framesBeforeVBlank = (int)Math.Ceiling(
            (double)AgbAudioEngine.GbaCyclesPerFrame * AgbAudioEngine.GbaOutputSampleRate /
            (double)AgbAudioEngine.GbaCpuFrequency) - 1;
        engine.Read(new float[framesBeforeVBlank * 2], 0, framesBeforeVBlank * 2);
        Assert.Equal(0, eventCount);

        engine.Read(new float[2], 0, 2);
        Assert.Equal(1, eventCount);
        Assert.Equal(1, session.VBlankCount);
    }

    [Fact]
    public void AudioRuntime_ProcessesProgramAndNoteWithoutUiThreadState()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false
        };
        var voice = new VoiceProjectInfo
        {
            Index = 3,
            Label = "Test PCM",
            Type = 0,
            Key = 60,
            Attack = 255,
            Decay = 255,
            Sustain = 255,
            Release = 0
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768u * 1024,
            Size = 4,
            Loops = true,
            LoopStart = 0
        };
        var bank = new Mp2kPreparedVoiceBank();
        bank.Set(3, 60, new Mp2kPreparedVoice(
            voice,
            BaseKey: 60,
            PlaybackNote: 60,
            ForcedPan: null,
            ProgramId: 3,
            Pcm: [0.25f, -0.25f, 0.5f, -0.5f],
            SampleHeader: header,
            WaveRam: null));
        var runtime = new Mp2kSequenceAudioRuntime(
            engine,
            bank,
            MidiCcMapping.Default,
            playerPriority: 0,
            fixedSampleRate: AgbAudioEngine.DefaultMp2kFixedSampleRate);
        var midi = new MidiPlaybackFile(
            48,
            [
                new MidiPlaybackEvent(0, 0, 1, MidiPlaybackEventKind.ProgramChange, 0, 3, 0, 0),
                NoteOn(tick: 0, order: 1, note: 60),
                NoteOff(tick: 1, order: 2, note: 60)
            ]);
        var session = new Mp2kMidiPlaybackSession(
            midi,
            89,
            90,
            runtime.ProcessEvent,
            runtime.Tick,
            runtime.StopAll);
        engine.StartMp2kMidiPlaybackSession(session);

        RenderOneVBlank(engine);

        Assert.Equal(1, engine.ActiveVoiceCount);
        Assert.Equal(60, runtime.Snapshot.Channels[0].ActiveNote);
        Assert.Equal("Test PCM", runtime.Snapshot.Channels[0].InstrumentLabel);
        Assert.NotEqual(0, runtime.Snapshot.ActiveNoteChannelMasks[60] & 1);

        RenderOneVBlank(engine);

        Assert.Equal(-1, runtime.Snapshot.Channels[0].ActiveNote);
        Assert.Equal(0, runtime.Snapshot.ActiveNoteChannelMasks[60] & 1);
    }

    private static Mp2kMidiPlaybackSession CreateSession(
        IReadOnlyList<MidiPlaybackEvent> events,
        Action<MidiPlaybackEvent> sink)
    {
        return new Mp2kMidiPlaybackSession(new MidiPlaybackFile(48, events), 89, 90, sink);
    }

    private static MidiPlaybackEvent Tempo(int tick, int order, int bpm) =>
        new(tick, order, 0, MidiPlaybackEventKind.Tempo, 0, 0, 0, 60_000_000 / bpm);

    private static MidiPlaybackEvent NoteOn(int tick, int order, int note, int track = 1) =>
        new(tick, order, track, MidiPlaybackEventKind.NoteOn, Math.Clamp(track - 1, 0, 15), note, 100, 0);

    private static MidiPlaybackEvent NoteOff(int tick, int order, int note, int track = 1) =>
        new(tick, order, track, MidiPlaybackEventKind.NoteOff, Math.Clamp(track - 1, 0, 15), note, 0, 0);

    private static MidiPlaybackEvent Control(int tick, int order, int track, int controller) =>
        new(tick, order, track, MidiPlaybackEventKind.ControlChange, Math.Clamp(track - 1, 0, 15), controller, 127, 0);

    private static void RenderOneVBlank(AgbAudioEngine engine)
    {
        int frames = (int)Math.Ceiling(
            (double)AgbAudioEngine.GbaCyclesPerFrame * AgbAudioEngine.GbaOutputSampleRate /
            AgbAudioEngine.GbaCpuFrequency);
        engine.Read(new float[frames * 2], 0, frames * 2);
    }
}
