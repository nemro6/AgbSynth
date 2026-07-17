using AgbSynth.App.Audio;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class AgbAudioEngineTests
{
    [Fact]
    public void WaveFormat_UsesGbaDefaultOutputSampleRate()
    {
        using var engine = new AgbAudioEngine();

        Assert.Equal(AgbAudioEngine.GbaOutputSampleRate, engine.WaveFormat.SampleRate);
    }

    [Fact]
    public void NoteOn_CapsActiveVoicesToGbaDirectSoundMixerChannelCount()
    {
        using var engine = new AgbAudioEngine();
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Range(0, 64).Select(i => unchecked((byte)(sbyte)(i - 32))).ToArray();

        for (int i = 0; i < AgbAudioEngine.DefaultDirectSoundMixerChannelCount + 8; i++)
            engine.NoteOn(pcm, header, baseKey: 60, midiNote: 60 + i % 12, velocity: 100, volume: 127, pan: 64, priority: 64);

        Assert.Equal(AgbAudioEngine.DefaultDirectSoundMixerChannelCount, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOn_DoesNotStealHigherPriorityVoices()
    {
        using var engine = new AgbAudioEngine();
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Range(0, 64).Select(i => unchecked((byte)(sbyte)(i - 32))).ToArray();

        for (int i = 0; i < AgbAudioEngine.DefaultDirectSoundMixerChannelCount; i++)
            Assert.True(engine.NoteOn(pcm, header, 60, 60, 100, 127, 64, priority: 100) >= 0);

        int lowPriorityVoiceId = engine.NoteOn(pcm, header, 60, 72, 100, 127, 64, priority: 10);

        Assert.Equal(-1, lowPriorityVoiceId);
        Assert.Equal(AgbAudioEngine.DefaultDirectSoundMixerChannelCount, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOn_SamePriorityEarlierTrackStealsLatestTrack()
    {
        using var engine = new AgbAudioEngine(directSoundMixerChannelCount: 2, outputDeviceNumber: int.MaxValue);
        var header = new SampleHeaderProjectInfo { Frequency = 13379 * 1024u, Size = 2, Loops = true };
        byte[] pcm = [0x20, 0xE0];

        int earlier = engine.NoteOn(pcm, header, 60, 60, 100, 100, 64, priority: 10, ownerRank: 1);
        int later = engine.NoteOn(pcm, header, 60, 62, 100, 100, 64, priority: 10, ownerRank: 9);
        int incoming = engine.NoteOn(pcm, header, 60, 64, 100, 100, 64, priority: 10, ownerRank: 0);

        Assert.True(incoming >= 0);
        Assert.True(engine.TryGetVoiceLevel(earlier, out _));
        Assert.False(engine.TryGetVoiceLevel(later, out _));
    }

    [Fact]
    public void NoteOn_SamePriorityLaterTrackCannotStealEarlierTrack()
    {
        using var engine = new AgbAudioEngine(directSoundMixerChannelCount: 1, outputDeviceNumber: int.MaxValue);
        var header = new SampleHeaderProjectInfo { Frequency = 13379 * 1024u, Size = 2, Loops = true };
        byte[] pcm = [0x20, 0xE0];

        Assert.True(engine.NoteOn(pcm, header, 60, 60, 100, 100, 64, priority: 10, ownerRank: 1) >= 0);
        int incoming = engine.NoteOn(pcm, header, 60, 64, 100, 100, 64, priority: 10, ownerRank: 9);

        Assert.Equal(-1, incoming);
    }

    [Fact]
    public void NoteOn_AlwaysReusesStoppedChannelBeforeActiveChannel()
    {
        using var engine = new AgbAudioEngine(directSoundMixerChannelCount: 2, outputDeviceNumber: int.MaxValue);
        var header = new SampleHeaderProjectInfo { Frequency = 13379 * 1024u, Size = 2, Loops = true };
        byte[] pcm = [0x20, 0xE0];

        int releasing = engine.NoteOn(pcm, header, 60, 60, 100, 100, 64, priority: 200, release: 250, ownerRank: 0);
        int active = engine.NoteOn(pcm, header, 60, 62, 100, 100, 64, priority: 1, ownerRank: 1);
        engine.NoteOff(releasing);
        int incoming = engine.NoteOn(pcm, header, 60, 64, 100, 100, 64, priority: 0, ownerRank: 15);

        Assert.True(incoming >= 0);
        Assert.False(engine.TryGetVoiceLevel(releasing, out _));
        Assert.True(engine.TryGetVoiceLevel(active, out _));
    }

    [Fact]
    public void NoteOnSquare_DoesNotStealSamePriorityEarlierTrack()
    {
        using var engine = new AgbAudioEngine();

        int first = engine.NoteOnSquare(2, midiNote: 72, velocity: 120, volume: 96, pan: 64, priority: 0, ownerRank: 7);
        int laterTrack = engine.NoteOnSquare(2, midiNote: 94, velocity: 120, volume: 96, pan: 64, priority: 0, ownerRank: 9);

        Assert.True(first >= 0);
        Assert.Equal(-1, laterTrack);
        Assert.Equal(1, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_SamePriorityEarlierTrackCanStealLaterTrack()
    {
        using var engine = new AgbAudioEngine();

        int later = engine.NoteOnSquare(2, midiNote: 94, velocity: 120, volume: 96, pan: 64, priority: 0, ownerRank: 9);
        int earlier = engine.NoteOnSquare(2, midiNote: 72, velocity: 120, volume: 96, pan: 64, priority: 0, ownerRank: 7);

        Assert.True(later >= 0);
        Assert.True(earlier >= 0);
        Assert.Equal(1, engine.ActiveVoiceCount);
    }

    [Fact]
    public void SetDirectSoundMixerChannelCount_ZeroDisablesDirectSoundVoices()
    {
        using var engine = new AgbAudioEngine();
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Range(0, 64).Select(i => unchecked((byte)(sbyte)(i - 32))).ToArray();

        Assert.True(engine.NoteOn(pcm, header, 60, 60, 100, 127, 64, priority: 64) >= 0);
        engine.SetDirectSoundMixerChannelCount(0);

        Assert.Equal(0, engine.DirectSoundMixerChannelCount);
        Assert.Equal(0, engine.ActiveVoiceCount);
        Assert.Equal(-1, engine.NoteOn(pcm, header, 60, 60, 100, 127, 64, priority: 64));
    }

    [Fact]
    public void SetVoiceVolume_UpdatesActiveDirectSoundVoice()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 2,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x80, 0x7F];
        float[] quiet = new float[4096];
        float[] loud = new float[4096];

        int voiceId = engine.NoteOn(pcm, header, baseKey: 60, midiNote: 60, velocity: 127, volume: 16, pan: 64, priority: 64);
        Assert.True(voiceId >= 0);
        engine.Read(quiet, 0, quiet.Length);

        engine.SetVoiceVolume(voiceId, 127);
        engine.Read(loud, 0, loud.Length);

        float quietPeak = GetChannelPeak(quiet, channel: 0);
        float loudPeak = GetChannelPeak(loud, channel: 0);
        Assert.True(loudPeak > quietPeak * 4f, $"Quiet peak: {quietPeak}, loud peak: {loudPeak}");
    }

    [Fact]
    public void NoteOn_DirectSoundStartLatencyDoesNotDependOnDmaBlockPosition()
    {
        int blockStartLatency = MeasureDirectSoundOnsetLatency(preRollFrames: 0);
        int middleOfBlockLatency = MeasureDirectSoundOnsetLatency(preRollFrames: 200);

        Assert.InRange(
            Math.Abs(blockStartLatency - middleOfBlockLatency),
            0,
            4);
    }

    [Fact]
    public void Mp2kPlayback_PsgPrecedesDirectSoundUntilNextDmaBlock()
    {
        int directSoundLatency = MeasureDirectSoundOnsetLatency(preRollFrames: 0);
        int psgLatency = MeasurePsgOnsetLatency();
        int expectedLatencyFrames = (int)Math.Round(
            AgbAudioEngine.GbaOutputSampleRate / 59.7275 * 218.0 / 228.0);

        Assert.InRange(
            directSoundLatency - psgLatency,
            expectedLatencyFrames - 4,
            expectedLatencyFrames + 4);
    }

    [Fact]
    public void NoteOn_UsesMp2kLinearDirectSoundPan()
    {
        float hardLeft = RenderConstantPcmPeak(pan: 0, directSoundMasterVolume: 15);
        float centerLeft = RenderConstantPcmPeak(pan: 64, directSoundMasterVolume: 15);

        Assert.InRange(centerLeft / hardLeft, 0.49f, 0.51f);
    }

    [Fact]
    public void NoteOn_UsesMp2kIntegerDirectSoundVolumeRounding()
    {
        float actual = RenderConstantPcmPeak(pan: 0, directSoundMasterVolume: 15);
        int envelope = ((15 + 1) * 255) >> 4;
        int leftEnvelope = (249 * envelope) >> 8;
        float expected = ((127 * leftEnvelope) >> 8) / 128f;

        Assert.InRange(Math.Abs(actual - expected), 0f, 0.0001f);
    }

    [Fact]
    public void Mp2kMixer_WrapsSignedEightBitOutputInsteadOfSaturating()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false,
            Mp2kPcmProcessingEnabled = true
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 13379 * 1024u,
            Size = 2,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x7F, 0x7F];

        Assert.True(engine.NoteOn(pcm, header, 60, 60, 127, 127, 0, 64) >= 0);
        Assert.True(engine.NoteOn(pcm, header, 60, 60, 127, 127, 0, 64) >= 0);
        float[] buffer = new float[2048];
        engine.Read(buffer, 0, buffer.Length);

        Assert.Contains(buffer.Where((_, index) => index % 2 == 0), sample => sample < 0f);
    }

    [Fact]
    public void Mp2kPcmProcessing_CanUseCleanFloatingPointOutput()
    {
        float actual = RenderConstantPcmPeak(
            pan: 0,
            directSoundMasterVolume: 15,
            mp2kPcmProcessingEnabled: false);
        float expected = (127f / 128f) * (249f / 256f) * (255f / 256f);

        Assert.InRange(Math.Abs(actual - expected), 0f, 0.0001f);
    }

    [Fact]
    public void OutputQuantize_UsesSoundModeDacBitDepth()
    {
        float unquantized = RenderPsgPeak(outputQuantizeEnabled: false, dacConfig: 9);
        float eightBit = RenderPsgPeak(outputQuantizeEnabled: true, dacConfig: 9);
        float nineBit = RenderPsgPeak(outputQuantizeEnabled: true, dacConfig: 8);

        Assert.Equal(MathF.Round(unquantized * 128f) / 128f, eightBit);
        Assert.Equal(MathF.Round(unquantized * 256f) / 256f, nineBit);
        // A full PSG channel lands on a multiple of both DAC steps. PCM tests
        // cover values where the selected PWM resolution changes the result.
        Assert.Equal(eightBit, nineBit);
    }

    [Fact]
    public void OutputVolume_IsAppliedAfterPwmQuantization()
    {
        float full = RenderPsgPeak(outputQuantizeEnabled: true, dacConfig: 9, masterGain: 1f, emulationGain: 1f, dutyIndex: 1);
        float outputHalf = RenderPsgPeak(outputQuantizeEnabled: true, dacConfig: 9, masterGain: 0.5f, emulationGain: 1f, dutyIndex: 1);
        float emulationHalf = RenderPsgPeak(outputQuantizeEnabled: true, dacConfig: 9, masterGain: 1f, emulationGain: 0.5f, dutyIndex: 1);

        Assert.Equal(full * 0.5f, outputHalf);
        Assert.Equal(full * 0.5f, emulationHalf);
    }

    [Fact]
    public void DirectSoundMasterVolume_UsesMp2kSoundModeScale()
    {
        float full = RenderConstantPcmPeak(pan: 0, directSoundMasterVolume: 15);
        float half = RenderConstantPcmPeak(pan: 0, directSoundMasterVolume: 7);

        Assert.InRange(half / full, 0.49f, 0.51f);
    }

    [Fact]
    public void Reverb_AddsTailToDirectSound()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        engine.OutputQuantizeEnabled = false;
        engine.ConfigureReverb(127, AgbAudioEngine.DefaultMp2kFixedSampleRate);
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 1,
            DataOffset = 0,
            Loops = false,
            LoopStart = 0
        };
        float[] buffer = new float[10_000];

        Assert.True(engine.NoteOn([0x7F], header, 60, 60, 127, 127, 64, 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        Assert.Contains(buffer.Skip(2000), sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void Reverb_DoesNotAffectPsgSubmix()
    {
        float[] dry = RenderSquareWithReverb(0);
        float[] wet = RenderSquareWithReverb(127);

        Assert.Equal(dry, wet);
    }

    [Fact]
    public void Mp2kPcmOutput_BeginsAtTheNextVCountDmaBlock()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 65536)
        {
            OutputQuantizeEnabled = false
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = AgbAudioEngine.DefaultMp2kFixedSampleRate * 1024u,
            Size = 1,
            DataOffset = 0,
            Loops = false,
            LoopStart = 0
        };
        float[] buffer = new float[1400 * 2];

        Assert.True(engine.NoteOn([0x7F], header, 60, 60, 127, 127, 0, 64, attack: 255) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        int firstAudibleFrame = Enumerable.Range(0, buffer.Length / 2)
            .First(frame => Math.Abs(buffer[frame * 2]) > 0.001f);
        int expectedLatencyFrames = (int)Math.Round(
            65536 / 59.7275 * 218.0 / 228.0);
        Assert.InRange(firstAudibleFrame, expectedLatencyFrames - 8, expectedLatencyFrames + 8);
    }

    [Fact]
    public void NoteOn_UsesSteppedPcm8OutputWithoutInterpolation()
    {
        using var engine = new AgbAudioEngine();
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 2,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x80, 0x7F];
        float[] buffer = new float[128];

        int voiceId = engine.NoteOn(pcm, header, baseKey: 60, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64);
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, buffer.Length);

        int distinctLeftValues = buffer.Where((_, i) => i % 2 == 0).Select(v => MathF.Round(v, 4)).Distinct().Count();
        Assert.True(distinctLeftValues <= 3);
    }

    [Fact]
    public void NoteOn_UsesMp2k23BitIntegerLinearInterpolation()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false,
            Mp2kPcmProcessingEnabled = false,
            LinearInterpolationEnabled = true
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 16384 * 1024u,
            Size = 2,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        float[] buffer = new float[8];

        Assert.True(engine.NoteOn([0x00, 0x7F], header, 60, 60, 127, 127, 0, 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        float expectedHalfSample = (63f / 128f) * (255f / 256f) * (249f / 256f);
        Assert.InRange(Math.Abs(buffer[2] - expectedHalfSample), 0f, 0.000001f);
    }

    [Fact]
    public void NoteOn_FixedDirectSoundUsesMixerRateInsteadOfSampleHeaderRate()
    {
        var highRateHeader = new SampleHeaderProjectInfo
        {
            Frequency = 26758 * 1024u,
            Size = 4,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        var mixerRateHeader = new SampleHeaderProjectInfo
        {
            Frequency = AgbAudioEngine.DefaultMp2kFixedSampleRate * 1024u,
            Size = 4,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x80, 0xC0, 0x00, 0x40];

        float[] fixedOutput = RenderPcm(pcm, highRateHeader, midiNote: 60, fixedPitch: true);
        float[] expectedOutput = RenderPcm(pcm, mixerRateHeader, midiNote: 60, fixedPitch: false);

        Assert.InRange(Math.Abs(CountPcmSignChanges(fixedOutput) - CountPcmSignChanges(expectedOutput)), 0, 2);
    }

    [Fact]
    public void NoteOn_FixedDirectSoundIgnoresNoteAndPitchOffset()
    {
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 26758 * 1024u,
            Size = 4,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x80, 0xC0, 0x00, 0x40];

        float[] lowOutput = RenderPcm(pcm, header, midiNote: 48, fixedPitch: true, pitchOffsetSemitones: -12);
        float[] highOutput = RenderPcm(pcm, header, midiNote: 72, fixedPitch: true, pitchOffsetSemitones: 12);

        Assert.InRange(Math.Abs(CountPcmSignChanges(lowOutput) - CountPcmSignChanges(highOutput)), 0, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputSampleRateChange_PreservesFixedDirectSoundPitch(bool outputQuantizeEnabled)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = outputQuantizeEnabled
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 26758 * 1024u,
            Size = 2,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x80, 0x7F];
        float[] before = new float[AgbAudioEngine.GbaOutputSampleRate * 2];

        Assert.True(engine.NoteOn(pcm, header, 60, 60, 127, 127, 0, 64, fixedPitch: true) >= 0);
        engine.Read(before, 0, before.Length);

        const int changedSampleRate = 48000;
        engine.TrySetOutputSampleRate(changedSampleRate, out _);
        float[] after = new float[changedSampleRate * 2];
        Assert.True(engine.NoteOn(pcm, header, 60, 60, 127, 127, 0, 64, fixedPitch: true) >= 0);
        engine.Read(after, 0, after.Length);

        Assert.InRange(Math.Abs(CountPcmSignChanges(before) - CountPcmSignChanges(after)), 0, 2);
    }

    [Fact]
    public void NoteOnSquare_CapsEachSquareHardwareChannelToOneVoice()
    {
        using var engine = new AgbAudioEngine();

        for (int i = 0; i < AgbAudioEngine.PsgSquareHardwareChannelCount + 4; i++)
            engine.NoteOnSquare(dutyIndex: i, midiNote: 60 + i, velocity: 100, volume: 127, pan: 64, priority: 64, squareChannel: 1);

        Assert.Equal(1, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_SeparatesSquare1AndSquare2HardwareChannels()
    {
        using var engine = new AgbAudioEngine();

        Assert.True(engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 100, volume: 127, pan: 64, priority: 64, squareChannel: 1) >= 0);
        Assert.True(engine.NoteOnSquare(dutyIndex: 2, midiNote: 64, velocity: 100, volume: 127, pan: 64, priority: 64, squareChannel: 2) >= 0);

        Assert.Equal(AgbAudioEngine.PsgSquareHardwareChannelCount, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_ProducesAudibleAlternatingWaveform()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[4096];

        Assert.True(engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        Assert.True(buffer.Max() > 0.05f);
        Assert.Equal(0f, buffer.Min());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void NoteOnSquare_StartsAtGbaDutyPatternPhase(int dutyIndex, bool startsHigh)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false
        };
        float[] frame = new float[2];

        Assert.True(engine.NoteOnSquare(dutyIndex, 60, 127, 127, 0, 64, attack: 0, decay: 0, sustain: 15, release: 7) >= 0);
        engine.Read(frame, 0, frame.Length);

        Assert.Equal(startsHigh, frame[0] > 0f);
    }

    [Fact]
    public void NoteOnSquare_RetriggerPreservesHardwareChannelPhase()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false
        };
        float[] advance = new float[40 * 2];
        float[] retriggered = new float[2];

        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 0, priority: 0, ownerRank: 9) >= 0);
        engine.Read(advance, 0, advance.Length);
        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 0, priority: 1, ownerRank: 7) >= 0);
        engine.Read(retriggered, 0, retriggered.Length);

        Assert.Equal(0f, retriggered[0]);
    }

    [Fact]
    public void NoteOnSquare_RetriggerPreservesFractionalHardwarePhase()
    {
        using var retriggered = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false
        };
        using var uninterrupted = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false
        };
        float[] advance = new float[37 * 2];

        Assert.True(retriggered.NoteOnSquare(2, 60, 127, 127, 0, priority: 0, ownerRank: 9) >= 0);
        Assert.True(uninterrupted.NoteOnSquare(2, 60, 127, 127, 0, priority: 0, ownerRank: 9) >= 0);
        retriggered.Read(advance, 0, advance.Length);
        uninterrupted.Read(new float[advance.Length], 0, advance.Length);

        Assert.True(retriggered.NoteOnSquare(2, 60, 127, 127, 0, priority: 1, ownerRank: 7) >= 0);
        float[] actual = new float[1024];
        float[] expected = new float[1024];
        retriggered.Read(actual, 0, actual.Length);
        uninterrupted.Read(expected, 0, expected.Length);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PsgMixer_UsesGbaFullRatioBeforeSoundBiasClipping()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] frame = new float[2];

        Assert.True(engine.NoteOnSquare(1, 60, 127, 127, 0, 64, squareChannel: 1) >= 0);
        Assert.True(engine.NoteOnSquare(1, 60, 127, 127, 0, 64, squareChannel: 2) >= 0);
        engine.Read(frame, 0, frame.Length);

        Assert.Equal(0.9375f, frame[0]);
        Assert.Equal(0f, frame[1]);
    }

    [Fact]
    public void NoteOnSquare_UsesThreeStatePsgPan()
    {
        float[] left = RenderSquarePan(0);
        float[] center = RenderSquarePan(64);
        float[] right = RenderSquarePan(127);

        Assert.True(GetChannelPeak(left, channel: 0) > 0.10f);
        Assert.True(GetChannelPeak(left, channel: 1) < 0.01f);
        Assert.True(GetChannelPeak(center, channel: 0) > 0.10f);
        Assert.True(GetChannelPeak(center, channel: 1) > 0.10f);
        Assert.True(GetChannelPeak(right, channel: 0) < 0.01f);
        Assert.True(GetChannelPeak(right, channel: 1) > 0.10f);
    }

    [Fact]
    public void SetVoiceVolume_UpdatesActivePsgVoice()
    {
        using var engine = new AgbAudioEngine();
        float[] quiet = new float[4096];
        float[] loud = new float[4096];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 16, pan: 64, priority: 64);
        Assert.True(voiceId >= 0);
        engine.Read(quiet, 0, quiet.Length);

        engine.SetVoiceVolume(voiceId, 127);
        engine.Read(loud, 0, loud.Length);

        Assert.True(GetChannelPeak(loud, channel: 0) > GetChannelPeak(quiet, channel: 0) * 4f);
    }

    [Fact]
    public void SetVoiceVolume_RevivesNoiseStartedAtZeroTrackVolume()
    {
        using var engine = new AgbAudioEngine();
        float[] silent = new float[4096];
        float[] audible = new float[4096];

        int voiceId = engine.NoteOnNoise(
            control: 0x23,
            baseKey: 60,
            midiNote: 60,
            velocity: 127,
            volume: 0,
            pan: 64,
            priority: 64);
        Assert.True(voiceId >= 0);
        engine.Read(silent, 0, silent.Length);

        engine.SetVoiceVolume(voiceId, 127);
        engine.Read(audible, 0, audible.Length);

        Assert.Equal(0f, GetChannelPeak(silent, channel: 0));
        Assert.True(GetChannelPeak(audible, channel: 0) > 0.05f);
    }

    [Fact]
    public void NoteOnSquare_AppliesVolumeLfo()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 2];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 100, pan: 64, priority: 64, lfoSettings: new AgbLfoSettings(127, 64, 1, 0));
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, buffer.Length);

        (float quietLevel, float loudLevel) = GetWindowAverageAbsRange(buffer, channel: 0, frameCount: 128, frameStep: 128);

        Assert.True(loudLevel > quietLevel * 1.5f);
    }

    [Fact]
    public void SetVoiceVolume_AppliesAtNextMixerBlockWithoutChangingLfoPhase()
    {
        using var changed = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false,
            LfoStepRate = 1000
        };
        using var reference = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false,
            LfoStepRate = 1000
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Repeat(unchecked((byte)(sbyte)64), 64).ToArray();
        var lfo = new AgbLfoSettings(64, 64, 1, 0);

        int changedVoice = changed.NoteOn(pcm, header, 60, 60, 127, 100, 64, 64, lfoSettings: lfo);
        int referenceVoice = reference.NoteOn(pcm, header, 60, 60, 127, 40, 64, 64, lfoSettings: lfo);
        Assert.True(changedVoice >= 0);
        Assert.True(referenceVoice >= 0);

        changed.Read(new float[16], 0, 16);
        reference.Read(new float[16], 0, 16);
        Assert.True(changed.TryGetVoiceLfoWave(changedVoice, out float phaseBefore));

        changed.SetVoiceVolume(changedVoice, 40);
        Assert.True(changed.TryGetVoiceLfoWave(changedVoice, out float phaseAfter));

        float[] actual = new float[1024];
        float[] expected = new float[1024];
        changed.Read(actual, 0, actual.Length);
        reference.Read(expected, 0, expected.Length);

        Assert.Equal(phaseBefore, phaseAfter);
        for (int i = actual.Length - 64; i < actual.Length; i++)
            Assert.Equal(expected[i], actual[i], precision: 6);
    }

    [Fact]
    public void NoteOnSquare_AppliesPitchLfo()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 4];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64, lfoSettings: new AgbLfoSettings(127, 16, 0, 0));
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, buffer.Length);

        (float minRate, float maxRate) = GetWindowLevelTransitionRateRange(buffer, frameCount: 4096, frameStep: 2048);

        Assert.True(maxRate > minRate * 1.15f);
    }

    [Fact]
    public void NoteOnSquare_AppliesPanLfo()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 2];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64, lfoSettings: new AgbLfoSettings(127, 64, 2, 0));
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, buffer.Length);

        Assert.True(HasPanDominantWindow(buffer, leftDominant: true));
        Assert.True(HasPanDominantWindow(buffer, leftDominant: false));
    }

    [Fact]
    public void LfoStepRate_FollowsTempoDependentStepTime()
    {
        int slowCrossings = CountLfoCrossings(stepRate: 24.0);
        int fastCrossings = CountLfoCrossings(stepRate: 48.0);

        Assert.True(fastCrossings >= slowCrossings * 1.8f);
    }

    [Fact]
    public void Lfo_UsesMp2kBytePhaseTriangleWave()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false,
            LfoStepRate = 1000
        };
        int voiceId = engine.NoteOnSquare(
            2,
            60,
            127,
            127,
            64,
            64,
            lfoSettings: new AgbLfoSettings(127, 64, 0, 0));
        Assert.True(voiceId >= 0);
        float[] frame = new float[16];

        engine.Read(frame, 0, frame.Length);
        Assert.True(engine.TryGetVoiceLfoWave(voiceId, out float positive));
        engine.Read(frame, 0, frame.Length);
        Assert.True(engine.TryGetVoiceLfoWave(voiceId, out float center));
        engine.Read(frame, 0, frame.Length);
        Assert.True(engine.TryGetVoiceLfoWave(voiceId, out float negative));

        Assert.Equal(1f, positive);
        Assert.Equal(0f, center);
        Assert.Equal(-1f, negative);
    }

    [Fact]
    public void Lfo_IsSharedByOverlappingVoicesOnTheSameTrack()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false,
            LfoStepRate = 1000
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Repeat(unchecked((byte)(sbyte)64), 64).ToArray();
        var lfo = new AgbLfoSettings(127, 64, 0, 0);

        int first = engine.NoteOn(pcm, header, 60, 60, 127, 127, 64, 64, lfoSettings: lfo, ownerRank: 3);
        Assert.True(first >= 0);
        engine.Read(new float[16], 0, 16);
        Assert.True(engine.TryGetVoiceLfoWave(first, out float phaseBefore));
        Assert.Equal(1f, phaseBefore);

        int second = engine.NoteOn(pcm, header, 60, 67, 127, 127, 64, 64, lfoSettings: lfo, ownerRank: 3);
        Assert.True(second >= 0);
        Assert.True(engine.TryGetVoiceLfoWave(first, out float firstPhase));
        Assert.True(engine.TryGetVoiceLfoWave(second, out float secondPhase));

        Assert.Equal(phaseBefore, firstPhase);
        Assert.Equal(firstPhase, secondPhase);
    }

    [Fact]
    public void LfoDelayOnNewNoteResetsTheSharedTrackPhase()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false,
            LfoStepRate = 1000
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 64,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = Enumerable.Repeat(unchecked((byte)(sbyte)64), 64).ToArray();
        var lfo = new AgbLfoSettings(127, 64, 0, 2);

        int first = engine.NoteOn(pcm, header, 60, 60, 127, 127, 64, 64, lfoSettings: lfo, ownerRank: 2);
        Assert.True(first >= 0);
        engine.Read(new float[48], 0, 48);
        Assert.True(engine.TryGetVoiceLfoWave(first, out float activePhase));
        Assert.Equal(1f, activePhase);

        int second = engine.NoteOn(pcm, header, 60, 67, 127, 127, 64, 64, lfoSettings: lfo, ownerRank: 2);
        Assert.True(second >= 0);
        Assert.True(engine.TryGetVoiceLfoWave(first, out float resetFirst));
        Assert.True(engine.TryGetVoiceLfoWave(second, out float resetSecond));
        Assert.Equal(0f, resetFirst);
        Assert.Equal(0f, resetSecond);
    }

    [Fact]
    public void NoteOnSquare_UsesMidiNoteForPitch()
    {
        float lowPitchChangeRate = RenderSquareChangeRate(60);
        float highPitchChangeRate = RenderSquareChangeRate(72);

        Assert.True(highPitchChangeRate > lowPitchChangeRate * 1.7f);
    }

    [Fact]
    public void NoteOnSquare_Midi60UsesGbaPsgClockRate()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 65536)
        {
            OutputQuantizeEnabled = false
        };
        float[] oneSecond = new float[65536 * 2];

        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 0, 64) >= 0);
        engine.Read(oneSecond, 0, oneSecond.Length);

        float transitions = CountChannelLevelTransitions(oneSecond, channel: 0, frameStart: 0, frameCount: 65536);
        Assert.InRange(transitions, 520, 526);
    }

    [Fact]
    public void SetVoicePitchOffset_UpdatesActiveSquarePitch()
    {
        using var engine = new AgbAudioEngine();
        float[] low = new float[AgbAudioEngine.GbaOutputSampleRate / 4 * 2];
        float[] high = new float[AgbAudioEngine.GbaOutputSampleRate / 4 * 2];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64);
        Assert.True(voiceId >= 0);
        engine.Read(low, 0, low.Length);

        engine.SetVoicePitchOffset(voiceId, 12);
        engine.Read(high, 0, high.Length);

        Assert.True(CountStereoLevelTransitions(high) > CountStereoLevelTransitions(low) * 1.7f);
    }

    [Fact]
    public void NoteOff_ReleasesVoiceInsteadOfStoppingImmediately()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 2];

        int voiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64, attack: 0, decay: 0, sustain: 15, release: 1);
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, AgbAudioEngine.GbaOutputSampleRate / 20);

        engine.NoteOff(voiceId);
        Assert.Equal(1, engine.ActiveVoiceCount);

        engine.Read(buffer, 0, buffer.Length);

        Assert.Equal(0, engine.ActiveVoiceCount);
        Assert.True(buffer.Max() > 0.01f);
        Assert.Equal(0f, buffer.Min());
    }

    [Fact]
    public void NoteOnSquare_StealsReleasingVoiceBeforeBlockingNewNote()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate / 20];

        int releasingVoiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 127, attack: 0, decay: 0, sustain: 15, release: 7, squareChannel: 1);
        Assert.True(releasingVoiceId >= 0);
        engine.Read(buffer, 0, buffer.Length);
        engine.NoteOff(releasingVoiceId);

        int newVoiceId = engine.NoteOnSquare(dutyIndex: 2, midiNote: 72, velocity: 127, volume: 127, pan: 64, priority: 0, attack: 0, decay: 0, sustain: 15, release: 7, squareChannel: 1);

        Assert.True(newVoiceId >= 0);
        Assert.Equal(1, engine.ActiveVoiceCount);
    }

    [Fact]
    public void WaveMemoryAttack_UsesFullEightBitMp2kCounter()
    {
        int fastOnset = MeasureWaveMemoryOnset(attack: 1);
        int slowOnset = MeasureWaveMemoryOnset(attack: 9);

        Assert.True(slowOnset > fastOnset * 4);
    }

    [Fact]
    public void PsgAttack_AdvancesOnTheHardware64HzEnvelopeClock()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 65536);
        float[] beforeFirstStep = new float[7000 * 2];
        float[] afterFirstStep = new float[3000 * 2];

        Assert.True(engine.NoteOnSquare(1, 60, 127, 127, 0, 64, attack: 7, decay: 0, sustain: 15) >= 0);
        engine.Read(beforeFirstStep, 0, beforeFirstStep.Length);
        engine.Read(afterFirstStep, 0, afterFirstStep.Length);

        Assert.All(beforeFirstStep, sample => Assert.Equal(0f, sample));
        Assert.Contains(afterFirstStep, sample => sample > 0f);
    }

    [Fact]
    public void PsgLength_StopsSquareWaveMemoryAndNoiseAtHardwareLength()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] buffer = new float[1024];

        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 64, 64, length: 63) >= 0);
        Assert.True(engine.NoteOnWaveMemory(CreateAlternatingWaveRam(), 60, 60, 127, 127, 64, 64, length: 255) >= 0);
        Assert.True(engine.NoteOnNoise(0x23, 60, 60, 127, 127, 64, 64, length: 63) >= 0);
        Assert.Equal(3, engine.ActiveVoiceCount);

        engine.Read(buffer, 0, buffer.Length);

        Assert.Equal(0, engine.ActiveVoiceCount);
        Assert.Contains(buffer.Take(256), sample => Math.Abs(sample) > 0.001f);
        Assert.All(buffer.Skip(320), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void WaveMemoryLengthUsesEightBitHardwareCounter()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] buffer = new float[1024];

        Assert.True(engine.NoteOnWaveMemory(
            CreateAlternatingWaveRam(),
            60,
            60,
            127,
            127,
            64,
            64,
            length: 63) >= 0);

        engine.Read(buffer, 0, buffer.Length);

        Assert.Equal(1, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_SweepChangesSquare1Pitch()
    {
        float[] buffer = RenderSquareSweep(squareChannel: 1, sweep: 0x77);
        int frameCount = buffer.Length / 2;
        float early = CountChannelLevelTransitions(buffer, channel: 0, frameStart: 0, frameCount: 4096);
        float late = CountChannelLevelTransitions(buffer, channel: 0, frameStart: frameCount - 4096, frameCount: 4096);

        Assert.True(late > early * 1.25f);
    }

    [Fact]
    public void NoteOnSquare_AscendingSweepStopsOnInitialOverflow()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false
        };
        float[] frame = new float[2];

        Assert.True(engine.NoteOnSquare(2, 127, 127, 127, 64, 64, squareChannel: 1, sweep: 0x11) >= 0);
        engine.Read(frame, 0, frame.Length);

        Assert.Equal(0, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_AscendingSweepUsesPostUpdateOverflowCheck()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue, outputSampleRate: 8000)
        {
            OutputQuantizeEnabled = false
        };
        float[] beforeSweepClock = new float[46 * 2];
        float[] sweepClock = new float[2];

        Assert.True(engine.NoteOnSquare(2, 47, 127, 127, 64, 64, squareChannel: 1, sweep: 0x11) >= 0);
        engine.Read(beforeSweepClock, 0, beforeSweepClock.Length);
        Assert.Equal(1, engine.ActiveVoiceCount);

        engine.Read(sweepClock, 0, sweepClock.Length);

        Assert.Equal(0, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnSquare_Square2IgnoresSweepByte()
    {
        float[] buffer = RenderSquareSweep(squareChannel: 2, sweep: 0x77);
        int frameCount = buffer.Length / 2;
        float early = CountChannelLevelTransitions(buffer, channel: 0, frameStart: 0, frameCount: 4096);
        float late = CountChannelLevelTransitions(buffer, channel: 0, frameStart: frameCount - 4096, frameCount: 4096);

        Assert.InRange(late / early, 0.95f, 1.05f);
    }

    [Fact]
    public void NoteOnSquare_ZeroPeriodSweepDoesNotApplyPeriodicFrequencyChanges()
    {
        float[] buffer = RenderSquareSweep(squareChannel: 1, sweep: 0x01, midiNote: 36);
        int frameCount = buffer.Length / 2;
        float early = CountChannelLevelTransitions(buffer, channel: 0, frameStart: 0, frameCount: 4096);
        float late = CountChannelLevelTransitions(buffer, channel: 0, frameStart: frameCount - 4096, frameCount: 4096);

        Assert.InRange(late / early, 0.90f, 1.10f);
    }

    private static float RenderSquareChangeRate(int midiNote)
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate / 4 * 2];
        Assert.True(engine.NoteOnSquare(dutyIndex: 2, midiNote: midiNote, velocity: 127, volume: 127, pan: 64, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        return CountStereoLevelTransitions(buffer);
    }

    private static float[] RenderSquareEnvelope(int attack, int decay, int release)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] buffer = new float[8192];
        int voiceId = engine.NoteOnSquare(2, 60, 127, 127, 64, 64, attack: attack, decay: decay, sustain: 8, release: release);
        Assert.True(voiceId >= 0);
        engine.Read(buffer, 0, 4096);
        engine.NoteOff(voiceId);
        engine.Read(buffer, 4096, 4096);
        return buffer;
    }

    private static int MeasureWaveMemoryOnset(int attack)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false
        };
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 2];
        Assert.True(engine.NoteOnWaveMemory(
            CreateAlternatingWaveRam(),
            baseKey: 60,
            midiNote: 60,
            velocity: 127,
            volume: 127,
            pan: 64,
            priority: 64,
            attack: attack,
            decay: 0,
            sustain: 15,
            release: 1) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        for (int frame = 0; frame < buffer.Length / 2; frame++)
        {
            if (Math.Abs(buffer[frame * 2]) > 0.001f || Math.Abs(buffer[frame * 2 + 1]) > 0.001f)
                return frame;
        }

        return int.MaxValue;
    }

    private static float[] RenderSquareSweep(int squareChannel, int sweep, int midiNote = 60)
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate * 2];
        Assert.True(engine.NoteOnSquare(2, midiNote, 127, 127, 64, 64, squareChannel: squareChannel, sweep: sweep) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float CountChannelLevelTransitions(float[] stereoBuffer, int channel, int frameStart, int frameCount)
    {
        int start = frameStart * 2 + channel;
        int end = Math.Min(stereoBuffer.Length, start + frameCount * 2);
        int changes = 0;
        float previous = stereoBuffer[start];
        for (int i = start + 2; i < end; i += 2)
        {
            float current = stereoBuffer[i];
            if (Math.Abs(current - previous) > 0.0001f)
                changes++;
            previous = current;
        }

        return changes;
    }

    private static float CountStereoLevelTransitions(float[] stereoBuffer)
    {
        int changes = 0;
        float previous = stereoBuffer[0];
        for (int i = 2; i < stereoBuffer.Length; i += 2)
        {
            float current = stereoBuffer[i];
            if (Math.Abs(current - previous) > 0.0001f)
                changes++;
            previous = current;
        }

        return changes / (stereoBuffer.Length / 2f);
    }

    private static float[] RenderSquarePan(int pan)
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[4096];
        Assert.True(engine.NoteOnSquare(dutyIndex: 2, midiNote: 60, velocity: 127, volume: 127, pan: pan, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float[] RenderSquareWithReverb(int level)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        engine.OutputQuantizeEnabled = false;
        engine.ConfigureReverb(level);
        float[] buffer = new float[4096];
        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 64, 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static int CountLfoCrossings(double stepRate)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        engine.LfoStepRate = stepRate;
        int voiceId = engine.NoteOnSquare(
            2,
            60,
            127,
            127,
            64,
            64,
            lfoSettings: new AgbLfoSettings(1, 16, 0, 0));
        Assert.True(voiceId >= 0);

        float[] frame = new float[2];
        float previous = 0f;
        int crossings = 0;
        for (int i = 0; i < AgbAudioEngine.GbaOutputSampleRate; i++)
        {
            engine.Read(frame, 0, frame.Length);
            Assert.True(engine.TryGetVoiceLfoWave(voiceId, out float current));
            if ((previous < 0f && current >= 0f) || (previous > 0f && current <= 0f))
                crossings++;
            previous = current;
        }

        return crossings;
    }

    private static float GetChannelPeak(float[] stereoBuffer, int channel)
    {
        float peak = 0f;
        for (int i = channel; i < stereoBuffer.Length; i += 2)
            peak = Math.Max(peak, Math.Abs(stereoBuffer[i]));
        return peak;
    }

    private static float GetWindowPeak(float[] stereoBuffer, int channel, int frameStart, int frameCount)
    {
        float peak = 0f;
        int start = Math.Clamp(frameStart * 2 + channel, channel, stereoBuffer.Length - 1);
        int end = Math.Min(stereoBuffer.Length, start + frameCount * 2);
        for (int i = start; i < end; i += 2)
            peak = Math.Max(peak, Math.Abs(stereoBuffer[i]));
        return peak;
    }

    private static float GetWindowAverageAbs(float[] stereoBuffer, int channel, int frameStart, int frameCount)
    {
        float sum = 0f;
        int count = 0;
        int start = Math.Clamp(frameStart * 2 + channel, channel, stereoBuffer.Length - 1);
        int end = Math.Min(stereoBuffer.Length, start + frameCount * 2);
        for (int i = start; i < end; i += 2)
        {
            sum += Math.Abs(stereoBuffer[i]);
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }

    private static (float Min, float Max) GetWindowAverageAbsRange(float[] stereoBuffer, int channel, int frameCount, int frameStep)
    {
        float min = float.MaxValue;
        float max = 0f;
        int totalFrames = stereoBuffer.Length / 2;
        for (int frameStart = 0; frameStart + frameCount <= totalFrames; frameStart += frameStep)
        {
            float level = GetWindowAverageAbs(stereoBuffer, channel, frameStart, frameCount);
            min = Math.Min(min, level);
            max = Math.Max(max, level);
        }

        return (min, max);
    }

    private static bool HasPanDominantWindow(float[] stereoBuffer, bool leftDominant)
    {
        const int frameCount = 128;
        const int frameStep = 128;
        int totalFrames = stereoBuffer.Length / 2;
        for (int frameStart = 0; frameStart + frameCount <= totalFrames; frameStart += frameStep)
        {
            float left = GetWindowPeak(stereoBuffer, channel: 0, frameStart, frameCount);
            float right = GetWindowPeak(stereoBuffer, channel: 1, frameStart, frameCount);
            if (leftDominant && left > right * 4f)
                return true;
            if (!leftDominant && right > left * 4f)
                return true;
        }

        return false;
    }

    private static (float Min, float Max) GetWindowLevelTransitionRateRange(float[] stereoBuffer, int frameCount, int frameStep)
    {
        float min = float.MaxValue;
        float max = 0f;
        int totalFrames = stereoBuffer.Length / 2;
        for (int frameStart = 0; frameStart + frameCount <= totalFrames; frameStart += frameStep)
        {
            int start = frameStart * 2;
            int end = Math.Min(stereoBuffer.Length, start + frameCount * 2);
            int changes = 0;
            float previous = stereoBuffer[start];
            for (int i = start + 2; i < end; i += 2)
            {
                float current = stereoBuffer[i];
                if (Math.Abs(current - previous) > 0.0001f)
                    changes++;
                previous = current;
            }

            float rate = changes / (float)frameCount;
            min = Math.Min(min, rate);
            max = Math.Max(max, rate);
        }

        return (min, max);
    }

    [Fact]
    public void NoteOnWaveMemory_CapsActiveVoicesToGbaWaveMemoryHardwareChannelCount()
    {
        using var engine = new AgbAudioEngine();
        byte[] waveRam = CreateAlternatingWaveRam();

        for (int i = 0; i < AgbAudioEngine.PsgWaveMemoryHardwareChannelCount + 4; i++)
            engine.NoteOnWaveMemory(waveRam, baseKey: 60, midiNote: 60 + i, velocity: 100, volume: 127, pan: 64, priority: 64);

        Assert.Equal(AgbAudioEngine.PsgWaveMemoryHardwareChannelCount, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnWaveMemory_ProducesAudibleWaveRamOutput()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[4096];

        Assert.True(engine.NoteOnWaveMemory(CreateAlternatingWaveRam(), baseKey: 60, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        Assert.Equal(0.46875f, buffer.Max());
        Assert.Equal(0f, buffer.Min());
    }

    [Fact]
    public void NoteOnWaveMemory_UsesSteppedPcm4OutputWithoutInterpolation()
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[128];

        Assert.True(engine.NoteOnWaveMemory(CreateAlternatingWaveRam(), baseKey: 60, midiNote: 72, velocity: 127, volume: 127, pan: 64, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        int distinctLeftValues = buffer.Where((_, i) => i % 2 == 0).Select(v => MathF.Round(v, 4)).Distinct().Count();
        Assert.True(distinctLeftValues <= 4);
    }

    private static byte[] CreateAlternatingWaveRam()
    {
        return Enumerable.Repeat((byte)0xF0, 16).ToArray();
    }

    private static float[] RenderPcm(byte[] pcm, SampleHeaderProjectInfo header, int midiNote, bool fixedPitch, double pitchOffsetSemitones = 0)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] buffer = new float[512];
        Assert.True(engine.NoteOn(
            pcm,
            header,
            baseKey: 60,
            midiNote: midiNote,
            velocity: 127,
            volume: 127,
            pan: 64,
            priority: 64,
            pitchOffsetSemitones: pitchOffsetSemitones,
            fixedPitch: fixedPitch) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float RenderConstantPcmPeak(
        int pan,
        int directSoundMasterVolume,
        bool mp2kPcmProcessingEnabled = true)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false,
            Mp2kPcmProcessingEnabled = mp2kPcmProcessingEnabled,
            DirectSoundMasterVolume = directSoundMasterVolume
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 2,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        byte[] pcm = [0x7F, 0x7F];
        float[] buffer = new float[4096];

        Assert.True(engine.NoteOn(pcm, header, baseKey: 60, midiNote: 60, velocity: 127, volume: 127, pan: pan, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);

        return GetChannelPeak(buffer, channel: 0);
    }

    private static int MeasureDirectSoundOnsetLatency(int preRollFrames)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false,
            Mp2kPcmProcessingEnabled = true
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 13379 * 1024u,
            Size = 2,
            Loops = true,
            LoopStart = 0
        };

        if (preRollFrames > 0)
            engine.Read(new float[preRollFrames * 2], 0, preRollFrames * 2);

        Assert.True(engine.NoteOn([0x7F, 0x7F], header, 60, 60, 127, 127, 0, 64) >= 0);
        float[] output = new float[4096];
        engine.Read(output, 0, output.Length);

        for (int frame = 0; frame < output.Length / 2; frame++)
        {
            if (Math.Abs(output[frame * 2]) > 0.0001f || Math.Abs(output[frame * 2 + 1]) > 0.0001f)
                return frame;
        }

        return int.MaxValue;
    }

    private static int MeasurePsgOnsetLatency()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = false,
            Mp2kPcmProcessingEnabled = true
        };

        Assert.True(engine.NoteOnSquare(2, 60, 127, 127, 0, 64, attack: 0, decay: 0, sustain: 15) >= 0);
        float[] output = new float[4096];
        engine.Read(output, 0, output.Length);

        for (int frame = 0; frame < output.Length / 2; frame++)
        {
            if (Math.Abs(output[frame * 2]) > 0.0001f || Math.Abs(output[frame * 2 + 1]) > 0.0001f)
                return frame;
        }

        return int.MaxValue;
    }

    private static float RenderCleanPcmPeak(bool outputQuantizeEnabled, int dacConfig)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = outputQuantizeEnabled,
            Mp2kPcmProcessingEnabled = false,
            GbaDacConfig = dacConfig,
            DirectSoundMasterVolume = 15
        };
        var header = new SampleHeaderProjectInfo
        {
            Frequency = 32768 * 1024u,
            Size = 1,
            DataOffset = 0,
            Loops = true,
            LoopStart = 0
        };
        float[] buffer = new float[256];

        Assert.True(engine.NoteOn([0x7F], header, 60, 60, 127, 2, 0, 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return GetChannelPeak(buffer, channel: 0);
    }

    private static float RenderPsgPeak(
        bool outputQuantizeEnabled,
        int dacConfig,
        float masterGain = 1f,
        float emulationGain = 1f,
        int dutyIndex = 0)
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue)
        {
            OutputQuantizeEnabled = outputQuantizeEnabled,
            GbaDacConfig = dacConfig,
            MasterGain = masterGain,
            EmulationGain = emulationGain
        };
        float[] buffer = new float[4096];

        Assert.True(engine.NoteOnSquare(dutyIndex, 60, 127, 127, 0, 64, attack: 0, decay: 0, sustain: 15, release: 7) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return GetChannelPeak(buffer, channel: 0);
    }

    private static int CountPcmSignChanges(float[] stereoBuffer)
    {
        int changes = 0;
        float previous = stereoBuffer[0];
        for (int i = 2; i < stereoBuffer.Length; i += 2)
        {
            float current = stereoBuffer[i];
            if ((previous < 0f && current >= 0f) || (previous >= 0f && current < 0f))
                changes++;
            previous = current;
        }

        return changes;
    }

    [Fact]
    public void NoteOnNoise_CapsActiveVoicesToGbaNoiseHardwareChannelCount()
    {
        using var engine = new AgbAudioEngine();

        for (int i = 0; i < AgbAudioEngine.PsgNoiseHardwareChannelCount + 4; i++)
            engine.NoteOnNoise(control: i, baseKey: 60, midiNote: 60, velocity: 100, volume: 127, pan: 64, priority: 64);

        Assert.Equal(AgbAudioEngine.PsgNoiseHardwareChannelCount, engine.ActiveVoiceCount);
    }

    [Fact]
    public void NoteOnNoise_ProducesAudibleLfsrWaveform()
    {
        float[] buffer = RenderNoise(midiNote: 60);

        Assert.True(buffer.Max() > 0.05f);
        Assert.Equal(0f, buffer.Min());
    }

    [Fact]
    public void NoteOnNoise_UsesMidiNoteForNoiseClock()
    {
        float lowPitchChangeRate = CountNoiseLevelTransitions(RenderNoise(midiNote: 48));
        float highPitchChangeRate = CountNoiseLevelTransitions(RenderNoise(midiNote: 72));

        Assert.True(highPitchChangeRate > lowPitchChangeRate * 1.5f);
    }

    [Fact]
    public void NoteOnNoise_StartsSilentUntilTheFirstHardwareLfsrClock()
    {
        using var engine = new AgbAudioEngine(outputDeviceNumber: int.MaxValue);
        float[] beforeFirstClock = new float[2048 * 2];
        float[] afterFirstClock = new float[8192 * 2];

        Assert.True(engine.NoteOnNoise(
            control: 0,
            baseKey: 60,
            midiNote: 21,
            velocity: 127,
            volume: 127,
            pan: 64,
            priority: 64) >= 0);

        engine.Read(beforeFirstClock, 0, beforeFirstClock.Length);
        engine.Read(afterFirstClock, 0, afterFirstClock.Length);

        Assert.Equal(0f, GetChannelPeak(beforeFirstClock, channel: 0));
        Assert.True(GetChannelPeak(afterFirstClock, channel: 0) > 0.05f);
    }

    [Fact]
    public void SetVoicePitchOffset_UpdatesActiveNoiseClock()
    {
        float lowPitchChangeRate = CountNoiseLevelTransitions(RenderNoiseWithPitchOffset(-12));
        float highPitchChangeRate = CountNoiseLevelTransitions(RenderNoiseWithPitchOffset(12));

        Assert.True(highPitchChangeRate > lowPitchChangeRate * 1.5f);
    }

    private static float[] RenderNoise(int midiNote)
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate];
        Assert.True(engine.NoteOnNoise(control: 0x23, baseKey: 60, midiNote: midiNote, velocity: 127, volume: 127, pan: 64, priority: 64) >= 0);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float[] RenderNoiseWithPitchOffset(double semitones)
    {
        using var engine = new AgbAudioEngine();
        float[] buffer = new float[AgbAudioEngine.GbaOutputSampleRate];
        int voiceId = engine.NoteOnNoise(control: 0x23, baseKey: 60, midiNote: 60, velocity: 127, volume: 127, pan: 64, priority: 64);
        Assert.True(voiceId >= 0);
        engine.SetVoicePitchOffset(voiceId, semitones);
        engine.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float CountNoiseLevelTransitions(float[] stereoBuffer)
    {
        int changes = 0;
        float previous = stereoBuffer[0];
        for (int i = 2; i < stereoBuffer.Length; i += 2)
        {
            float current = stereoBuffer[i];
            if (Math.Abs(current - previous) > 0.0001f)
                changes++;
            previous = current;
        }

        return changes;
    }
}
