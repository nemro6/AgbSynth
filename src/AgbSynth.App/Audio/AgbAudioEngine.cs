using System;
using System.Collections.Generic;
using NAudio.Wave;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;

namespace AgbSynth.App.Audio;

public readonly record struct AgbLfoSettings(int Depth, int Speed, int Type, int Delay)
{
    public static AgbLfoSettings Default { get; } = new(0, 22, 0, 0);
}

public sealed class AgbAudioEngine : ISampleProvider, IDisposable
{
    public const int DirectSoundHardwareFifoCount = 2;
    public const int PsgHardwareChannelCount = 4;
    public const int PsgSquareHardwareChannelCount = 2;
    public const int PsgWaveMemoryHardwareChannelCount = 1;
    public const int PsgNoiseHardwareChannelCount = 1;
    // MP2K mixes DirectSound voices in software before sending them to GBA DirectSound FIFO A/B.
    public const int DefaultDirectSoundMixerChannelCount = 12;
    public const int DefaultMp2kFixedSampleRate = 13379;
    public const int GbaOutputSampleRate = 32768;
    public const int GbaCpuFrequency = 16_777_216;
    public const int GbaCyclesPerFrame = 280_896;
    private const int PsgFrameSequencerCycles = 32_768;
    private const int GbaPsgTimingFactor = 4;
    private const double EnvelopeUpdateRate = 59.7275;
    private const ulong FixedOneQ32 = 1UL << 32;
    private static readonly byte[] NoiseFrequencyTable =
    [
        0xD7, 0xD6, 0xD5, 0xD4,
        0xC7, 0xC6, 0xC5, 0xC4,
        0xB7, 0xB6, 0xB5, 0xB4,
        0xA7, 0xA6, 0xA5, 0xA4,
        0x97, 0x96, 0x95, 0x94,
        0x87, 0x86, 0x85, 0x84,
        0x77, 0x76, 0x75, 0x74,
        0x67, 0x66, 0x65, 0x64,
        0x57, 0x56, 0x55, 0x54,
        0x47, 0x46, 0x45, 0x44,
        0x37, 0x36, 0x35, 0x34,
        0x27, 0x26, 0x25, 0x24,
        0x17, 0x16, 0x15, 0x14,
        0x07, 0x06, 0x05, 0x04,
        0x03, 0x02, 0x01, 0x00
    ];
    private static readonly uint[] Mp2kFrequencyTable =
    [
        2147483648u, 2275179671u, 2410468894u, 2553802834u,
        2705659852u, 2866546760u, 3037000500u, 3217589947u,
        3408917802u, 3611622603u, 3826380858u, 4053909305u
    ];
    private static readonly int[] CgbFrequencyTable =
    [
        -2004, -1891, -1785, -1685, -1591, -1501,
        -1417, -1337, -1262, -1192, -1125, -1062
    ];
    private readonly object _lock = new();
    private readonly List<ActiveVoice> _voices = new();
    private readonly int[] _squarePhases = new int[PsgSquareHardwareChannelCount];
    private readonly ulong[] _squareCycleAccumulatorsQ32 = new ulong[PsgSquareHardwareChannelCount];
    private readonly TrackLfoState[] _trackLfos = CreateTrackLfoStates();
    private Mp2kMidiPlaybackSession? _midiPlaybackSession;
    private bool _processingPlaybackVBlank;
    private WaveOutEvent? _output;
    private int _nextVoiceId;
    private int _desiredLatencyMs;
    private int _outputDeviceNumber;
    private int _outputSampleRate;
    private long _nextStartOrder;
    private int _directSoundMixerChannelCount;
    private bool _outputQuantizeEnabled = true;
    private bool _linearInterpolationEnabled;
    private bool _mp2kPcmProcessingEnabled = true;
    private bool _stereoOutputEnabled = true;
    private int _gbaDacConfig = 9;
    private int _directSoundMasterVolume = 15;
    private float _emulationGain = 1.0f;
    private float _masterGain = 1.0f;
    private double _lfoStepRate = 48.0;
    private ulong _lfoTickAccumulatorQ32;
    private long _vblankClockAccumulator;
    private int _mplayTempoCounter;
    private int _mp2kTempoByte = 75;
    private bool _useMp2kLfoClock;
    private int _psgC15;
    private long _psgHardwareFrameAccumulator;
    private int _psgHardwareFrameStep;
    private double _pwmOutputPhase;
    private float _heldOutputLeft;
    private float _heldOutputRight;
    private bool _isRecording;
    private WaveFormat? _recordingWaveFormat;
    private readonly List<float> _recordingSamples = new();
    private readonly Mp2kNormalReverb _reverb;

    public AgbAudioEngine(int desiredLatencyMs = 48, int directSoundMixerChannelCount = DefaultDirectSoundMixerChannelCount, int outputDeviceNumber = -1, int outputSampleRate = GbaOutputSampleRate)
    {
        _outputSampleRate = ClampOutputSampleRate(outputSampleRate);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_outputSampleRate, 2);
        _desiredLatencyMs = Math.Max(16, desiredLatencyMs);
        _outputDeviceNumber = outputDeviceNumber;
        _directSoundMixerChannelCount = Math.Clamp(directSoundMixerChannelCount, 0, 32);
        _reverb = new Mp2kNormalReverb(GetGbaPwmSampleRate(_gbaDacConfig), DefaultMp2kFixedSampleRate);
        TryStartOutput();
    }

    public WaveFormat WaveFormat { get; private set; }
    public bool IsRecording
    {
        get
        {
            lock (_lock)
                return _isRecording;
        }
    }

    public int ActiveVoiceCount
    {
        get
        {
            lock (_lock)
                return _voices.Count;
        }
    }

    public bool TryGetVoiceLevel(int voiceId, out float level)
    {
        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id != voiceId)
                    continue;

                level = Math.Clamp(_voices[i].MeterLevel, 0f, 1f);
                return true;
            }
        }

        level = 0f;
        return false;
    }

    public void StartRecording()
    {
        lock (_lock)
        {
            _recordingSamples.Clear();
            _recordingWaveFormat = WaveFormat;
            _isRecording = true;
        }
    }

    public AgbAudioRecording StopRecording()
    {
        lock (_lock)
        {
            _isRecording = false;
            var recording = new AgbAudioRecording(
                _recordingWaveFormat ?? WaveFormat,
                _recordingSamples.ToArray());
            _recordingSamples.Clear();
            _recordingWaveFormat = null;
            return recording;
        }
    }

    public void CancelRecording()
    {
        lock (_lock)
        {
            _isRecording = false;
            _recordingSamples.Clear();
            _recordingWaveFormat = null;
        }
    }

    public bool TryGetVoiceLfoWave(int voiceId, out float wave)
    {
        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id != voiceId)
                    continue;

                wave = _voices[i].CurrentLfoWave;
                return true;
            }
        }

        wave = 0f;
        return false;
    }

    public int DirectSoundMixerChannelCount
    {
        get
        {
            lock (_lock)
                return _directSoundMixerChannelCount;
        }
    }

    public bool OutputQuantizeEnabled
    {
        get
        {
            lock (_lock)
                return _outputQuantizeEnabled;
        }
        set
        {
            lock (_lock)
            {
                if (_outputQuantizeEnabled == value)
                    return;

                _outputQuantizeEnabled = value;
                _voices.Clear();
                _reverb.SetOutputSampleRate(GetSynthesisSampleRateNoLock());
                ResetPwmOutputStateNoLock();
            }
        }
    }

    public bool LinearInterpolationEnabled
    {
        get
        {
            lock (_lock)
                return _linearInterpolationEnabled;
        }
        set
        {
            lock (_lock)
                _linearInterpolationEnabled = value;
        }
    }

    public bool Mp2kPcmProcessingEnabled
    {
        get
        {
            lock (_lock)
                return _mp2kPcmProcessingEnabled;
        }
        set
        {
            lock (_lock)
            {
                if (_mp2kPcmProcessingEnabled == value)
                    return;

                _mp2kPcmProcessingEnabled = value;
                _voices.RemoveAll(voice => voice.Kind == VoiceKind.DirectSound);
            }
        }
    }

    public bool StereoOutputEnabled
    {
        get
        {
            lock (_lock)
                return _stereoOutputEnabled;
        }
        set
        {
            lock (_lock)
                _stereoOutputEnabled = value;
        }
    }

    public int GbaDacConfig
    {
        get
        {
            lock (_lock)
                return _gbaDacConfig;
        }
        set
        {
            lock (_lock)
            {
                int normalized = Math.Clamp(value, 8, 11);
                if (_gbaDacConfig == normalized)
                    return;

                _gbaDacConfig = normalized;
                if (_outputQuantizeEnabled)
                {
                    _voices.Clear();
                    _reverb.SetOutputSampleRate(GetSynthesisSampleRateNoLock());
                    ResetPwmOutputStateNoLock();
                }
            }
        }
    }

    public int DirectSoundMasterVolume
    {
        get
        {
            lock (_lock)
                return _directSoundMasterVolume;
        }
        set
        {
            lock (_lock)
                _directSoundMasterVolume = Math.Clamp(value, 0, 15);
        }
    }

    public float MasterGain
    {
        get
        {
            lock (_lock)
                return _masterGain;
        }
        set
        {
            lock (_lock)
                _masterGain = Math.Clamp(value, 0f, 1f);
        }
    }

    public float EmulationGain
    {
        get
        {
            lock (_lock)
                return _emulationGain;
        }
        set
        {
            lock (_lock)
                _emulationGain = Math.Clamp(value, 0f, 1f);
        }
    }

    public double LfoStepRate
    {
        get
        {
            lock (_lock)
                return _lfoStepRate;
        }
        set
        {
            lock (_lock)
            {
                _lfoStepRate = Math.Clamp(value, 0.0, 1000.0);
                _useMp2kLfoClock = false;
            }
        }
    }

    public void SetMp2kTempoByte(int tempo)
    {
        lock (_lock)
        {
            _mp2kTempoByte = Math.Clamp(tempo, 0, 255);
            _useMp2kLfoClock = true;
        }
    }

    public void StartMp2kMidiPlaybackSession(Mp2kMidiPlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_lock)
        {
            if (!ReferenceEquals(_midiPlaybackSession, session))
                _midiPlaybackSession?.Stop();
            _midiPlaybackSession = session;
            _mplayTempoCounter = 0;
        }

        TryStartOutput();
    }

    public void StopMp2kMidiPlaybackSession(Mp2kMidiPlaybackSession? session = null)
    {
        lock (_lock)
        {
            if (_midiPlaybackSession is null ||
                (session is not null && !ReferenceEquals(_midiPlaybackSession, session)))
            {
                return;
            }

            Mp2kMidiPlaybackSession activeSession = _midiPlaybackSession;
            _midiPlaybackSession = null;
            activeSession.Stop();
        }
    }

    public bool IsVoiceActive(int voiceId)
    {
        lock (_lock)
            return _voices.Exists(voice => voice.Id == voiceId && !voice.Done);
    }

    public void StopVoicesForOwnerRank(int ownerRank)
    {
        ownerRank = Math.Clamp(ownerRank, 0, 127);
        lock (_lock)
        {
            foreach (ActiveVoice voice in _voices)
            {
                if (voice.OwnerRank == ownerRank)
                    voice.Release();
            }
        }
    }

    public bool TryGetTrackMetrics(int ownerRank, out float level, out float lfoWave)
    {
        ownerRank = Math.Clamp(ownerRank, 0, 127);
        level = 0;
        lfoWave = 0;
        bool found = false;
        lock (_lock)
        {
            foreach (ActiveVoice voice in _voices)
            {
                if (voice.OwnerRank != ownerRank || voice.Done)
                    continue;

                found = true;
                level = Math.Max(level, voice.MeterLevel);
                if (Math.Abs(voice.CurrentLfoWave) >= Math.Abs(lfoWave))
                    lfoWave = voice.CurrentLfoWave;
            }
        }

        return found;
    }

    public int ReverbLevel
    {
        get
        {
            lock (_lock)
                return _reverb.Level;
        }
    }

    public void ConfigureReverb(int level, int fixedSampleRate = DefaultMp2kFixedSampleRate)
    {
        lock (_lock)
        {
            int normalizedLevel = Math.Clamp(level, 0, 127);
            bool clockModeChanges = !_mp2kPcmProcessingEnabled && ((_reverb.Level == 0) != (normalizedLevel == 0));
            if (clockModeChanges)
                _voices.RemoveAll(voice => voice.Kind == VoiceKind.DirectSound);
            _reverb.Configure(normalizedLevel, Math.Max(1, fixedSampleRate));
        }
    }

    public int NoteOn(byte[] signedPcm, SampleHeaderProjectInfo header, int baseKey, int midiNote, int velocity, int volume, int pan, int priority, int attack = 255, int decay = 255, int sustain = 255, int release = 255, double pitchOffsetSemitones = 0, AgbLfoSettings lfoSettings = default, bool fixedPitch = false, int fixedSampleRate = DefaultMp2kFixedSampleRate, int ownerRank = 0, int rhythmPan = 0)
    {
        if (signedPcm.Length == 0)
            return -1;

        float[] samples = new float[signedPcm.Length];
        for (int i = 0; i < signedPcm.Length; i++)
            samples[i] = unchecked((sbyte)signedPcm[i]) / 128f;

        return NoteOnPrepared(
            samples,
            header,
            baseKey,
            midiNote,
            velocity,
            volume,
            pan,
            priority,
            attack,
            decay,
            sustain,
            release,
            pitchOffsetSemitones,
            lfoSettings,
            fixedPitch,
            fixedSampleRate,
            ownerRank,
            rhythmPan);
    }

    public int NoteOnPrepared(float[] samples, SampleHeaderProjectInfo header, int baseKey, int midiNote, int velocity, int volume, int pan, int priority, int attack = 255, int decay = 255, int sustain = 255, int release = 255, double pitchOffsetSemitones = 0, AgbLfoSettings lfoSettings = default, bool fixedPitch = false, int fixedSampleRate = DefaultMp2kFixedSampleRate, int ownerRank = 0, int rhythmPan = 0)
    {
        if (samples.Length == 0)
            return -1;

        bool useMp2kMixerClock;
        lock (_lock)
            useMp2kMixerClock = _mp2kPcmProcessingEnabled || _reverb.Level > 0;
        int synthesisSampleRate = GetSynthesisSampleRateNoLock();
        int processingSampleRate = useMp2kMixerClock
            ? Math.Max(1, fixedSampleRate)
            : synthesisSampleRate;
        int pcmBaseKey = Math.Clamp(60 + midiNote - baseKey, 0, 178);
        double sourceRate = fixedPitch
            ? Math.Max(1, fixedSampleRate)
            : Math.Max(1.0, MidiKeyToFrequency(header.Frequency, pcmBaseKey, 0));
        double ratio = sourceRate / processingSampleRate;
        (float left, float right) = GetDirectSoundGains(velocity, volume, pan);
        int loopStart = checked((int)Math.Min(header.LoopStart, (uint)Math.Max(0, samples.Length - 1)));
        bool loops = header.Loops && loopStart < samples.Length - 1;

        var voice = ActiveVoice.CreatePcm(
            id: _nextVoiceId++,
            slot: -1,
            midiNote: Math.Clamp(midiNote, 0, 127),
            priority: Math.Clamp(priority, 0, 255),
            ownerRank: Math.Clamp(ownerRank, 0, 127),
            startOrder: _nextStartOrder++,
            samples: samples,
            step: ratio,
            loopStart: loopStart,
            loops: loops,
            envelope: Envelope.CreateDirectSound(velocity, volume, attack, decay, sustain, release),
            baseVolume: volume,
            basePan: pan,
            leftGain: left,
            rightGain: right,
            outputSampleRate: processingSampleRate,
            pitchLocked: fixedPitch,
            rhythmPan: rhythmPan);
        voice.ConfigurePcmPitch(header.Frequency, pcmBaseKey, useMp2kMixerClock, processingSampleRate);
        voice.SetPitchOffset(pitchOffsetSemitones);

        lock (_lock)
        {
            RemoveDoneVoicesNoLock();
            int slot = FindFreeSlotNoLock(voice.Kind);
            if (slot < 0)
                slot = StealSlotNoLock(voice);
            if (slot < 0)
                return -1;

            voice.Slot = slot;
            StartTrackLfoForVoiceNoLock(voice, NormalizeLfoSettings(lfoSettings));
            _voices.Add(voice);
        }

        TryStartOutput();
        return voice.Id;
    }

    public int NoteOnSquare(int dutyIndex, int midiNote, int velocity, int volume, int pan, int priority, int attack = 0, int decay = 0, int sustain = 15, int release = 7, int squareChannel = 1, double pitchOffsetSemitones = 0, AgbLfoSettings lfoSettings = default, int ownerRank = 0, int length = 0, int sweep = 0, int rhythmPan = 0)
    {
        midiNote = Math.Clamp(midiNote, 0, 127);
        VoiceKind kind = squareChannel == 2 ? VoiceKind.PsgSquare2 : VoiceKind.PsgSquare1;
        double frequency = GetCgbToneFrequencyHz(channel: squareChannel == 2 ? 2 : 1, midiNote, fineAdjust: 0);
        int synthesisSampleRate = GetSynthesisSampleRateNoLock();
        double phaseStep = frequency / synthesisSampleRate;
        (float left, float right) = GetPsgPanGains(pan);

        var voice = ActiveVoice.CreatePsgSquare(
            id: _nextVoiceId++,
            slot: -1,
            kind: kind,
            midiNote: midiNote,
            priority: Math.Clamp(priority, 0, 255),
            ownerRank: Math.Clamp(ownerRank, 0, 127),
            startOrder: _nextStartOrder++,
            dutyIndex: dutyIndex,
            phaseStep: phaseStep,
            envelope: Envelope.CreatePsg(velocity, volume, attack, decay, sustain, release, usesHardwareEnvelope: true),
            baseVolume: volume,
            basePan: pan,
            leftGain: left,
            rightGain: right,
            outputSampleRate: synthesisSampleRate,
            length: length,
            sweep: squareChannel == 1 ? sweep : 0,
            rhythmPan: rhythmPan);
        voice.SetPitchOffset(pitchOffsetSemitones);

        lock (_lock)
        {
            RemoveDoneVoicesNoLock();
            int slot = FindFreeSlotNoLock(voice.Kind);
            if (slot < 0)
                slot = StealSlotNoLock(voice);
            if (slot < 0)
                return -1;

            voice.Slot = slot;
            int hardwareChannel = kind == VoiceKind.PsgSquare2 ? 1 : 0;
            voice.SetSquareDutyPosition(_squarePhases[hardwareChannel]);
            voice.SetPsgCycleAccumulatorQ32(_squareCycleAccumulatorsQ32[hardwareChannel]);
            if (_processingPlaybackVBlank)
                voice.SkipNextPsgEnvelopeStep();
            StartTrackLfoForVoiceNoLock(voice, NormalizeLfoSettings(lfoSettings));
            _voices.Add(voice);
        }

        TryStartOutput();
        return voice.Id;
    }

    public int NoteOnWaveMemory(byte[] waveRam, int baseKey, int midiNote, int velocity, int volume, int pan, int priority, int attack = 0, int decay = 0, int sustain = 15, int release = 7, double pitchOffsetSemitones = 0, AgbLfoSettings lfoSettings = default, int ownerRank = 0, int length = 0, int rhythmPan = 0)
    {
        if (waveRam.Length < 16)
            return -1;

        baseKey = Math.Clamp(baseKey, 0, 127);
        midiNote = Math.Clamp(midiNote, 0, 127);
        byte[] waveSamples = DecodeGbaWaveRam(waveRam);
        double sampleIndexRate = GetCgbToneFrequencyHz(channel: 3, midiNote, fineAdjust: 0);
        int synthesisSampleRate = GetSynthesisSampleRateNoLock();
        double step = sampleIndexRate / synthesisSampleRate;
        (float left, float right) = GetPsgPanGains(pan);

        var voice = ActiveVoice.CreatePsgWaveMemory(
            id: _nextVoiceId++,
            slot: -1,
            midiNote: midiNote,
            priority: Math.Clamp(priority, 0, 255),
            ownerRank: Math.Clamp(ownerRank, 0, 127),
            startOrder: _nextStartOrder++,
            waveSamples: waveSamples,
            step: step,
            envelope: Envelope.CreatePsg(velocity, volume, attack, decay, sustain, release, usesHardwareEnvelope: false),
            baseVolume: volume,
            basePan: pan,
            leftGain: left,
            rightGain: right,
            outputSampleRate: synthesisSampleRate,
            length: length,
            rhythmPan: rhythmPan);
        voice.SetPitchOffset(pitchOffsetSemitones);

        lock (_lock)
        {
            RemoveDoneVoicesNoLock();
            int slot = FindFreeSlotNoLock(voice.Kind);
            if (slot < 0)
                slot = StealSlotNoLock(voice);
            if (slot < 0)
                return -1;

            voice.Slot = slot;
            if (_processingPlaybackVBlank)
                voice.SkipNextPsgEnvelopeStep();
            StartTrackLfoForVoiceNoLock(voice, NormalizeLfoSettings(lfoSettings));
            _voices.Add(voice);
        }

        TryStartOutput();
        return voice.Id;
    }

    public int NoteOnNoise(int control, int baseKey, int midiNote, int velocity, int volume, int pan, int priority, int attack = 0, int decay = 0, int sustain = 15, int release = 7, double pitchOffsetSemitones = 0, AgbLfoSettings lfoSettings = default, int ownerRank = 0, int length = 0, int rhythmPan = 0)
    {
        baseKey = Math.Clamp(baseKey, 0, 127);
        midiNote = Math.Clamp(midiNote, 0, 127);
        int transposedControl = SelectNoiseControl(control, midiNote);
        double clockHz = GetGbaNoiseClockHz(transposedControl);
        (float left, float right) = GetPsgPanGains(pan);

        int synthesisSampleRate = GetSynthesisSampleRateNoLock();
        var voice = ActiveVoice.CreatePsgNoise(
            id: _nextVoiceId++,
            slot: -1,
            midiNote: midiNote,
            priority: Math.Clamp(priority, 0, 255),
            ownerRank: Math.Clamp(ownerRank, 0, 127),
            startOrder: _nextStartOrder++,
            control: transposedControl,
            baseControl: control,
            noiseClockStep: clockHz / synthesisSampleRate,
            envelope: Envelope.CreatePsg(velocity, volume, attack, decay, sustain, release, usesHardwareEnvelope: true),
            baseVolume: volume,
            basePan: pan,
            leftGain: left,
            rightGain: right,
            outputSampleRate: synthesisSampleRate,
            length: length,
            rhythmPan: rhythmPan);
        voice.SetPitchOffset(pitchOffsetSemitones);

        lock (_lock)
        {
            RemoveDoneVoicesNoLock();
            int slot = FindFreeSlotNoLock(voice.Kind);
            if (slot < 0)
                slot = StealSlotNoLock(voice);
            if (slot < 0)
                return -1;

            voice.Slot = slot;
            if (_processingPlaybackVBlank)
                voice.SkipNextPsgEnvelopeStep();
            StartTrackLfoForVoiceNoLock(voice, NormalizeLfoSettings(lfoSettings));
            _voices.Add(voice);
        }

        TryStartOutput();
        return voice.Id;
    }

    private static int SelectNoiseControl(int control, int midiNote)
    {
        int width = control & 0x08;
        int key = Math.Clamp(midiNote, 0, 127);
        int tableIndex = key <= 20
            ? 0
            : Math.Min(key - 21, NoiseFrequencyTable.Length - 1);
        return (NoiseFrequencyTable[tableIndex] & ~0x08) | width;
    }

    private static double PitchFactor(double semitones)
    {
        return Math.Pow(2.0, semitones / 12.0);
    }

    private static uint MidiKeyToFrequency(uint waveFrequency, int key, int fineAdjust)
    {
        key = Math.Clamp(key, 0, 178);
        fineAdjust = Math.Clamp(fineAdjust, 0, 255);
        uint first = GetMp2kScaleValue(key);
        uint second = GetMp2kScaleValue(key + 1);
        uint fraction = (uint)fineAdjust << 24;
        uint interpolated = first + MultiplyHigh(second - first, fraction);
        return MultiplyHigh(waveFrequency, interpolated);
    }

    private static uint GetMp2kScaleValue(int key)
    {
        key = Math.Clamp(key, 0, 179);
        int octaveShift = 14 - (key / 12);
        return Mp2kFrequencyTable[key % 12] >> octaveShift;
    }

    private static uint MultiplyHigh(uint left, uint right)
    {
        return (uint)(((ulong)left * right) >> 32);
    }

    private static double GetGbaNoiseClockHz(int control)
    {
        int divider = control & 0x07;
        int shift = (control >> 4) & 0x0F;
        double baseClock = 4_194_304.0 / 8.0;
        double divided = divider == 0
            ? baseClock * 2.0
            : baseClock / divider;
        int prescaler = 1 << Math.Clamp(shift + 1, 1, 14);
        return divided / prescaler;
    }

    private static double QuantizeToGbaPsgFrequency(double requestedFrequency)
    {
        int registerValue = (int)Math.Round(2048.0 - 131072.0 / Math.Max(1.0, requestedFrequency));
        registerValue = Math.Clamp(registerValue, 0, 2047);
        return 131072.0 / (2048 - registerValue);
    }

    private static int MidiKeyToCgbFrequencyRegister(int channel, int key, int fineAdjust)
    {
        if (channel == 4)
            return SelectNoiseControl(control: 0, key);

        if (key <= 35)
        {
            key = 0;
            fineAdjust = 0;
        }
        else
        {
            key -= 36;
            if (key > 130)
            {
                key = 130;
                fineAdjust = 255;
            }
        }

        key = Math.Clamp(key, 0, 130);
        fineAdjust = Math.Clamp(fineAdjust, 0, 255);
        int current = GetCgbScaleFrequency(key);
        int next = GetCgbScaleFrequency(key + 1);
        return current + ((fineAdjust * (next - current)) >> 8) + 2048;
    }

    private static int GetCgbScaleFrequency(int key)
    {
        key = Math.Clamp(key, 0, 131);
        return CgbFrequencyTable[key % 12] >> (key / 12);
    }

    private static double GetCgbToneFrequencyHz(int channel, int key, int fineAdjust)
    {
        int register = MidiKeyToCgbFrequencyRegister(channel, key, fineAdjust);
        double numerator = channel == 3 ? 2_097_152.0 : 131_072.0;
        return numerator / Math.Max(1, 2048 - register);
    }

    private static (float Left, float Right) GetPsgPanGains(int pan)
    {
        int signedPan = Math.Clamp(pan, 0, 127) - 64;
        if (signedPan < -21)
            return (1f, 0f);
        if (signedPan > 20)
            return (0f, 1f);
        return (1f, 1f);
    }

    private static (float Left, float Right) GetPsgPanGains(int leftVolume, int rightVolume)
    {
        leftVolume = Math.Clamp(leftVolume, 0, 255);
        rightVolume = Math.Clamp(rightVolume, 0, 255);
        if (rightVolume >= leftVolume && rightVolume / 2 >= leftVolume)
            return (0f, 1f);
        if (leftVolume > rightVolume && leftVolume / 2 >= rightVolume)
            return (1f, 0f);
        return (1f, 1f);
    }

    private static (float Left, float Right) GetDirectSoundGains(int velocity, int volume, int pan)
    {
        (int left, int right) = GetDirectSoundVolumes(velocity, volume, pan);
        return (left / 256f, right / 256f);
    }

    private static (int Left, int Right) GetDirectSoundVolumes(
        int velocity,
        int volume,
        int pan,
        int rhythmPan = 0,
        int panModulation = 0)
    {
        int trackVolume = Math.Clamp(volume, 0, 127) << 1;
        int combinedPan = Math.Clamp(
            ((Math.Clamp(pan, 0, 127) - 64) << 1) + panModulation,
            -128,
            127);
        int volumeRight = ((combinedPan + 128) * trackVolume) >> 8;
        int volumeLeft = ((127 - combinedPan) * trackVolume) >> 8;
        rhythmPan = Math.Clamp(rhythmPan, -128, 126);

        int noteVelocity = Math.Clamp(velocity, 0, 127);
        int rightVolume = Math.Clamp((noteVelocity * (rhythmPan + 128) * volumeRight) >> 14, 0, 255);
        int leftVolume = Math.Clamp((noteVelocity * (127 - rhythmPan) * volumeLeft) >> 14, 0, 255);
        return (leftVolume, rightVolume);
    }

    private static AgbLfoSettings NormalizeLfoSettings(AgbLfoSettings settings)
    {
        int speed = settings.Speed == 0 && settings != default ? 0 : settings.Speed;
        if (settings == default)
            speed = 22;

        return new AgbLfoSettings(
            Math.Clamp(settings.Depth, 0, 127),
            Math.Clamp(speed, 0, 255),
            Math.Clamp(settings.Type, 0, 2),
            Math.Clamp(settings.Delay, 0, 255));
    }

    private static TrackLfoState[] CreateTrackLfoStates()
    {
        var states = new TrackLfoState[16];
        for (int i = 0; i < states.Length; i++)
            states[i] = new TrackLfoState();
        return states;
    }

    private void ConfigureTrackLfoNoLock(int track, AgbLfoSettings settings)
    {
        TrackLfoState state = _trackLfos[track];
        if (state.Configure(settings))
            ApplyTrackLfoNoLock(track);
    }

    private void StartTrackLfoForVoiceNoLock(ActiveVoice voice, AgbLfoSettings settings)
    {
        int track = Math.Clamp(voice.OwnerRank, 0, _trackLfos.Length - 1);
        ConfigureTrackLfoNoLock(track, settings);

        TrackLfoState state = _trackLfos[track];
        if (state.TriggerNote())
            ApplyTrackLfoNoLock(track);
        voice.ApplyTrackLfo(state.ModM, state.Settings.Type, state.CurrentWave);
    }

    private void AdvanceTrackLfoClockNoLock(int sampleRate, int vblankTicks)
    {
        if (_useMp2kLfoClock)
        {
            for (int i = 0; i < vblankTicks; i++)
            {
                _mplayTempoCounter += _mp2kTempoByte * 2;
                while (_mplayTempoCounter >= 150)
                {
                    _mplayTempoCounter -= 150;
                    TickTrackLfosNoLock();
                }
            }
            return;
        }

        _lfoTickAccumulatorQ32 += RateToQ32(_lfoStepRate, sampleRate);
        while (_lfoTickAccumulatorQ32 >= FixedOneQ32)
        {
            _lfoTickAccumulatorQ32 -= FixedOneQ32;
            TickTrackLfosNoLock();
        }
    }

    private int AdvanceVBlankClockNoLock(int sampleRate)
    {
        _vblankClockAccumulator += GbaCpuFrequency;
        long period = (long)GbaCyclesPerFrame * sampleRate;
        int ticks = (int)(_vblankClockAccumulator / period);
        _vblankClockAccumulator %= period;
        return ticks;
    }

    private void TickTrackLfosNoLock()
    {
        for (int track = 0; track < _trackLfos.Length; track++)
        {
            if (_trackLfos[track].Tick())
                ApplyTrackLfoNoLock(track);
        }
    }

    private int AdvancePsgEnvelopeClockNoLock(int vblankTicks)
    {
        int steps = 0;
        for (int i = 0; i < vblankTicks; i++)
        {
            if (_psgC15 != 0)
                _psgC15--;
            else
                _psgC15 = 14;

            steps++;
            if (_psgC15 == 0)
                steps++;
        }

        return steps;
    }

    private void AdvancePsgHardwareFrameNoLock(int sampleRate)
    {
        _psgHardwareFrameAccumulator += GbaCpuFrequency;
        long period = (long)PsgFrameSequencerCycles * sampleRate;
        while (_psgHardwareFrameAccumulator >= period)
        {
            _psgHardwareFrameAccumulator -= period;

            if ((_psgHardwareFrameStep & 1) == 0)
            {
                foreach (ActiveVoice voice in _voices)
                    voice.ClockPsgLength();
            }

            if (_psgHardwareFrameStep is 2 or 6)
            {
                foreach (ActiveVoice voice in _voices)
                {
                    if (voice.Kind == VoiceKind.PsgSquare1)
                        voice.ClockSquareSweep();
                }
            }

            if (_psgHardwareFrameStep == 7)
            {
                foreach (ActiveVoice voice in _voices)
                    voice.ClockPsgEnvelope();
            }

            _psgHardwareFrameStep = (_psgHardwareFrameStep + 1) & 7;
        }
    }

    private void ApplyTrackLfoNoLock(int track)
    {
        TrackLfoState state = _trackLfos[track];
        foreach (ActiveVoice voice in _voices)
        {
            if (Math.Clamp(voice.OwnerRank, 0, _trackLfos.Length - 1) == track)
                voice.ApplyTrackLfo(state.ModM, state.Settings.Type, state.CurrentWave);
        }
    }

    private void ApplyAllTrackLfosNoLock()
    {
        for (int track = 0; track < _trackLfos.Length; track++)
            ApplyTrackLfoNoLock(track);
    }

    private static ulong RateToQ32(double eventsPerSecond, int sampleRate)
    {
        double increment = Math.Max(0.0, eventsPerSecond) * FixedOneQ32 / Math.Max(1, sampleRate);
        return (ulong)Math.Max(0.0, Math.Round(increment));
    }

    private static byte[] DecodeGbaWaveRam(byte[] waveRam)
    {
        byte[] samples = new byte[32];
        for (int i = 0; i < 16; i++)
        {
            byte value = waveRam[i];
            samples[i * 2] = (byte)(value >> 4);
            samples[i * 2 + 1] = (byte)(value & 0x0F);
        }
        return samples;
    }

    public void NoteOff(int voiceId)
    {
        if (voiceId < 0)
            return;

        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id == voiceId)
                {
                    _voices[i].Release();
                    break;
                }
            }
        }
    }

    public void SetVoiceVolume(int voiceId, int volume)
    {
        if (voiceId < 0)
            return;

        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id == voiceId)
                {
                    _voices[i].SetVolume(volume);
                    break;
                }
            }
        }
    }

    public void SetVoicePan(int voiceId, int pan)
    {
        if (voiceId < 0)
            return;

        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id == voiceId)
                {
                    _voices[i].SetPan(pan);
                    break;
                }
            }
        }
    }

    public void SetVoicePitchOffset(int voiceId, double semitones)
    {
        if (voiceId < 0)
            return;

        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id == voiceId)
                {
                    _voices[i].SetPitchOffset(semitones);
                    break;
                }
            }
        }
    }

    public void SetVoiceLfoSettings(int voiceId, AgbLfoSettings settings)
    {
        if (voiceId < 0)
            return;

        settings = NormalizeLfoSettings(settings);
        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Id == voiceId)
                {
                    int track = Math.Clamp(_voices[i].OwnerRank, 0, _trackLfos.Length - 1);
                    ConfigureTrackLfoNoLock(track, settings);
                    break;
                }
            }
        }
    }

    public void SetTrackLfoSettings(int track, AgbLfoSettings settings)
    {
        track = Math.Clamp(track, 0, _trackLfos.Length - 1);
        settings = NormalizeLfoSettings(settings);
        lock (_lock)
            ConfigureTrackLfoNoLock(track, settings);
    }

    public void ResetTrackLfoStates()
    {
        lock (_lock)
        {
            foreach (TrackLfoState state in _trackLfos)
                state.Reset();
            _lfoTickAccumulatorQ32 = 0;
            _mplayTempoCounter = 0;
            ApplyAllTrackLfosNoLock();
        }
    }

    public void AllNotesOff()
    {
        lock (_lock)
            _voices.Clear();
    }

    public void SetChannelEnabledMask(ushort enabledMask)
    {
        lock (_lock)
        {
            _voices.RemoveAll(v => ((enabledMask >> v.Slot) & 0x1) == 0);
        }
    }

    public void SetDirectSoundMixerChannelCount(int channelCount)
    {
        lock (_lock)
        {
            _directSoundMixerChannelCount = Math.Clamp(channelCount, 0, 32);
            _voices.RemoveAll(v => v.Kind == VoiceKind.DirectSound && v.Slot >= _directSoundMixerChannelCount);
        }
    }

    public bool TrySetBufferLatency(int desiredLatencyMs, out string? error)
    {
        error = null;
        if (desiredLatencyMs < 16)
        {
            error = "Buffer size must be 16 ms or larger.";
            return false;
        }

        lock (_lock)
        {
            if (_desiredLatencyMs == desiredLatencyMs)
                return true;

            _desiredLatencyMs = desiredLatencyMs;
            _output?.Stop();
            _output?.Dispose();
            _output = null;
        }

        TryStartOutput();
        if (_output is null)
        {
            error = "Failed to restart audio output.";
            return false;
        }

        return true;
    }

    public bool TrySetOutputDevice(int deviceNumber, out string? error)
    {
        error = null;
        lock (_lock)
        {
            if (_outputDeviceNumber == deviceNumber)
                return true;

            _outputDeviceNumber = deviceNumber;
            _output?.Stop();
            _output?.Dispose();
            _output = null;
        }

        TryStartOutput();
        if (_output is null)
        {
            error = "Failed to restart audio output.";
            return false;
        }

        return true;
    }

    public bool TrySetOutputSampleRate(int outputSampleRate, out string? error)
    {
        error = null;
        outputSampleRate = ClampOutputSampleRate(outputSampleRate);
        lock (_lock)
        {
            if (_outputSampleRate == outputSampleRate)
                return true;

            _outputSampleRate = outputSampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_outputSampleRate, 2);
            _reverb.SetOutputSampleRate(GetSynthesisSampleRateNoLock());
            _reverb.Reset();
            ResetPwmOutputStateNoLock();
            _voices.Clear();
            _output?.Stop();
            _output?.Dispose();
            _output = null;
        }

        TryStartOutput();
        if (_output is null)
        {
            error = "Failed to restart audio output.";
            return false;
        }

        return true;
    }

    private static int ClampOutputSampleRate(int sampleRate)
    {
        return Math.Clamp(sampleRate, 8000, 192000);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        lock (_lock)
        {
            foreach (var voice in _voices)
                voice.MeterLevel = 0f;

            if (_isRecording)
                _recordingSamples.EnsureCapacity(_recordingSamples.Count + count);

            for (int frame = 0; frame < count / 2; frame++)
            {
                float left;
                float right;
                if (_outputQuantizeEnabled)
                {
                    _pwmOutputPhase += GetGbaPwmSampleRate(_gbaDacConfig) / (double)_outputSampleRate;
                    int pwmSamples = (int)_pwmOutputPhase;
                    _pwmOutputPhase -= pwmSamples;
                    for (int pwmSample = 0; pwmSample < pwmSamples; pwmSample++)
                    {
                        RenderSynthesisFrame(quantizePwm: true, out _heldOutputLeft, out _heldOutputRight);
                    }

                    left = _heldOutputLeft;
                    right = _heldOutputRight;
                }
                else
                {
                    RenderSynthesisFrame(quantizePwm: false, out left, out right);
                }

                float recordedLeft = left;
                float recordedRight = right;

                left = Math.Clamp(left * _masterGain, -1f, 1f);
                right = Math.Clamp(right * _masterGain, -1f, 1f);

                int sampleOffset = offset + frame * 2;
                buffer[sampleOffset] = left;
                buffer[sampleOffset + 1] = right;

                if (_isRecording)
                {
                    _recordingSamples.Add(recordedLeft);
                    _recordingSamples.Add(recordedRight);
                }
            }
        }

        return count;
    }

    private void RenderSynthesisFrame(bool quantizePwm, out float left, out float right)
    {
        int synthesisSampleRate = GetSynthesisSampleRateNoLock();
        int vblankTicks = AdvanceVBlankClockNoLock(synthesisSampleRate);
        if (_midiPlaybackSession is { } session)
        {
            for (int i = 0; i < vblankTicks; i++)
            {
                _processingPlaybackVBlank = true;
                try
                {
                    session.AdvanceVBlank(TickTrackLfosNoLock);
                }
                finally
                {
                    _processingPlaybackVBlank = false;
                }
            }
        }
        else
        {
            AdvanceTrackLfoClockNoLock(synthesisSampleRate, vblankTicks);
        }
        int psgEnvelopeSteps = AdvancePsgEnvelopeClockNoLock(vblankTicks);
        AdvancePsgHardwareFrameNoLock(synthesisSampleRate);

        float directLeft = 0f;
        float directRight = 0f;
        int psgLeft = 0;
        int psgRight = 0;
        bool useMp2kMixerClock = _mp2kPcmProcessingEnabled || _reverb.Level > 0;

        if (useMp2kMixerClock)
        {
            int mixerSamples = _reverb.AdvanceOutputClock();
            for (int mixerSample = 0; mixerSample < mixerSamples; mixerSample++)
            {
                float mixerLeft = 0f;
                float mixerRight = 0f;
                MixVoiceKind(
                    VoiceKind.DirectSound,
                    useIntegerMixer: true,
                    directSoundEnvelopeSteps: _reverb.IsBlockStart ? 1 : 0,
                    ref mixerLeft,
                    ref mixerRight);
                _reverb.ProcessMixerSample(mixerLeft, mixerRight);
            }

            _reverb.GetOutput(out directLeft, out directRight);
        }
        else
        {
            MixVoiceKind(
                VoiceKind.DirectSound,
                useIntegerMixer: false,
                directSoundEnvelopeSteps: -1,
                ref directLeft,
                ref directRight);
        }

        MixPsgVoices(psgEnvelopeSteps, ref psgLeft, ref psgRight);

        if (quantizePwm)
        {
            int pwmBitDepth = 17 - _gbaDacConfig;
            int directHardwareLeft = (int)Math.Round(directLeft * 128f, MidpointRounding.AwayFromZero) << 2;
            int directHardwareRight = (int)Math.Round(directRight * 128f, MidpointRounding.AwayFromZero) << 2;
            int mixedLeft = (int)Math.Round((directHardwareLeft + psgLeft * 16) * _emulationGain);
            int mixedRight = (int)Math.Round((directHardwareRight + psgRight * 16) * _emulationGain);
            left = QuantizeGbaOutput(mixedLeft, pwmBitDepth);
            right = QuantizeGbaOutput(mixedRight, pwmBitDepth);
        }
        else
        {
            left = Math.Clamp((directLeft + psgLeft / 32f) * _emulationGain, -1f, 1f);
            right = Math.Clamp((directRight + psgRight / 32f) * _emulationGain, -1f, 1f);
        }

        if (!_stereoOutputEnabled)
            left = right = (left + right) * 0.5f;
    }

    private int GetSynthesisSampleRateNoLock()
    {
        return _outputQuantizeEnabled ? GetGbaPwmSampleRate(_gbaDacConfig) : _outputSampleRate;
    }

    private static int GetGbaPwmSampleRate(int dacConfig)
    {
        return 32768 << Math.Clamp(dacConfig - 8, 0, 3);
    }

    private void ResetPwmOutputStateNoLock()
    {
        _pwmOutputPhase = 0.0;
        _heldOutputLeft = 0f;
        _heldOutputRight = 0f;
        _vblankClockAccumulator = 0;
        _psgHardwareFrameAccumulator = 0;
        _psgHardwareFrameStep = 0;
    }

    private void MixVoiceKind(
        VoiceKind kind,
        bool useIntegerMixer,
        int directSoundEnvelopeSteps,
        ref float left,
        ref float right)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (voice.Kind != kind)
                continue;
            if (!voice.TryRead(
                    _linearInterpolationEnabled,
                    psgEnvelopeSteps: 0,
                    directSoundEnvelopeSteps,
                    out float sample))
            {
                _voices.RemoveAt(i);
                continue;
            }

            float leftSample;
            float rightSample;
            if (useIntegerMixer)
            {
                voice.GetDirectSoundOutput(_directSoundMasterVolume, out leftSample, out rightSample);
            }
            else
            {
                float envelopedSample = sample * (voice.Envelope.Level / 256f);
                float masterGain = (_directSoundMasterVolume + 1) / 16f;
                leftSample = envelopedSample * voice.LeftGain * masterGain;
                rightSample = envelopedSample * voice.RightGain * masterGain;
            }

            voice.CurrentLevel = Math.Max(voice.CurrentLevel * 0.995f, Math.Max(Math.Abs(leftSample), Math.Abs(rightSample)));
            voice.MeterLevel = Math.Max(voice.MeterLevel, Math.Max(Math.Abs(leftSample), Math.Abs(rightSample)));
            left += leftSample;
            right += rightSample;
        }
    }

    private void MixPsgVoices(int envelopeSteps, ref int left, ref int right)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (voice.Kind == VoiceKind.DirectSound)
                continue;
            if (!voice.TryRead(
                    _linearInterpolationEnabled,
                    envelopeSteps,
                    directSoundEnvelopeSteps: -1,
                    out _))
            {
                RememberSquarePhaseNoLock(voice);
                _voices.RemoveAt(i);
                continue;
            }

            RememberSquarePhaseNoLock(voice);
            int hardwareSample = voice.LastPsgHardwareSample;
            int leftSample = voice.LeftGain > 0f ? hardwareSample : 0;
            int rightSample = voice.RightGain > 0f ? hardwareSample : 0;
            float meterSample = Math.Max(leftSample, rightSample) / 32f;
            voice.CurrentLevel = Math.Max(voice.CurrentLevel * 0.995f, meterSample);
            voice.MeterLevel = Math.Max(voice.MeterLevel, meterSample);
            left += leftSample;
            right += rightSample;
        }
    }

    private void RememberSquarePhaseNoLock(ActiveVoice voice)
    {
        if (voice.Kind == VoiceKind.PsgSquare1)
        {
            _squarePhases[0] = voice.SquareDutyPosition;
            _squareCycleAccumulatorsQ32[0] = voice.PsgCycleAccumulatorQ32;
        }
        else if (voice.Kind == VoiceKind.PsgSquare2)
        {
            _squarePhases[1] = voice.SquareDutyPosition;
            _squareCycleAccumulatorsQ32[1] = voice.PsgCycleAccumulatorQ32;
        }
    }

    private static void AccumulateVoiceSample(ActiveVoice voice, float sample, float sourceGain, ref float left, ref float right)
    {
        voice.CurrentLevel = Math.Max(voice.CurrentLevel * 0.995f, Math.Abs(sample));
        float leftSample = sample * voice.LeftGain * sourceGain;
        float rightSample = sample * voice.RightGain * sourceGain;
        voice.MeterLevel = Math.Max(voice.MeterLevel, Math.Max(Math.Abs(leftSample), Math.Abs(rightSample)));
        left += leftSample;
        right += rightSample;
    }

    private static float QuantizeGbaOutput(int mixed, int bitDepth)
    {
        // SOUNDBIAS converts the signed six-channel sum to unsigned 10-bit,
        // clips it, then discards low PWM bits selected by SOUNDBIAS[15:14].
        int resolutionShift = 10 - Math.Clamp(bitDepth, 6, 9);
        int biased = Math.Clamp(mixed + 512, 0, 1023);
        int quantized = ((biased >> resolutionShift) << resolutionShift) - 512;
        return quantized / 512f;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _midiPlaybackSession?.Stop();
            _midiPlaybackSession = null;
            _voices.Clear();
        }
        try
        {
            _output?.Stop();
        }
        catch
        {
        }

        _output?.Dispose();
        _output = null;
    }

    private void TryStartOutput()
    {
        if (_output is not null)
            return;

        try
        {
            _output = new WaveOutEvent
            {
                DeviceNumber = _outputDeviceNumber,
                DesiredLatency = _desiredLatencyMs,
                NumberOfBuffers = 8
            };
            _output.Init(this);
            _output.Play();
        }
        catch
        {
            _output?.Dispose();
            _output = null;
        }
    }

    private void RemoveDoneVoicesNoLock()
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (_voices[i].Done)
            {
                RememberSquarePhaseNoLock(_voices[i]);
                _voices.RemoveAt(i);
            }
        }
    }

    private int FindFreeSlotNoLock(VoiceKind kind)
    {
        int slotCount = GetSlotCountNoLock(kind);

        for (int slot = 0; slot < slotCount; slot++)
        {
            bool used = false;
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Kind == kind && _voices[i].Slot == slot)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
                return slot;
        }

        return -1;
    }

    private int StealSlotNoLock(ActiveVoice incomingVoice)
    {
        int victimIndex = -1;
        bool foundReleasing = false;

        for (int slot = 0; slot < GetSlotCountNoLock(incomingVoice.Kind); slot++)
        {
            int i = _voices.FindIndex(voice => voice.Kind == incomingVoice.Kind && voice.Slot == slot);
            if (i < 0)
                continue;
            var candidate = _voices[i];

            bool candidateIsReleasing = candidate.IsReleasing;
            if (candidateIsReleasing && !foundReleasing)
            {
                victimIndex = i;
                foundReleasing = true;
                continue;
            }
            if (candidateIsReleasing != foundReleasing)
                continue;

            if (!foundReleasing &&
                (candidate.Priority > incomingVoice.Priority ||
                 candidate.Priority == incomingVoice.Priority && candidate.OwnerRank < incomingVoice.OwnerRank))
            {
                continue;
            }

            if (victimIndex < 0 ||
                candidate.Priority < _voices[victimIndex].Priority ||
                candidate.Priority == _voices[victimIndex].Priority && candidate.OwnerRank >= _voices[victimIndex].OwnerRank)
            {
                victimIndex = i;
            }
        }

        if (victimIndex < 0)
            return -1;

        int reusedSlot = _voices[victimIndex].Slot;
        RememberSquarePhaseNoLock(_voices[victimIndex]);
        _voices.RemoveAt(victimIndex);
        return reusedSlot;
    }

    private int GetSlotCountNoLock(VoiceKind kind)
    {
        return kind switch
        {
            VoiceKind.DirectSound => _directSoundMixerChannelCount,
            VoiceKind.PsgSquare1 => 1,
            VoiceKind.PsgSquare2 => 1,
            VoiceKind.PsgWaveMemory => PsgWaveMemoryHardwareChannelCount,
            VoiceKind.PsgNoise => PsgNoiseHardwareChannelCount,
            _ => 0
        };
    }

    private enum VoiceKind
    {
        DirectSound,
        PsgSquare1,
        PsgSquare2,
        PsgWaveMemory,
        PsgNoise
    }

    private enum EnvelopeState
    {
        Initializing,
        Rising,
        Decaying,
        Playing,
        Releasing,
        Dying,
        Dead
    }

    private sealed class TrackLfoState
    {
        private int _phase;
        private int _delayCounter;

        public AgbLfoSettings Settings { get; private set; } = AgbLfoSettings.Default;
        public int ModM { get; private set; }
        public float CurrentWave { get; private set; }

        public void Reset()
        {
            Settings = AgbLfoSettings.Default;
            _phase = 0;
            _delayCounter = 0;
            ModM = 0;
            CurrentWave = 0;
        }

        public bool Configure(AgbLfoSettings settings)
        {
            bool typeChanged = Settings.Type != settings.Type;
            Settings = settings;
            if (settings.Depth == 0 || settings.Speed == 0)
                return ClearModulation();

            return typeChanged;
        }

        public bool TriggerNote()
        {
            _delayCounter = Settings.Delay;
            return Settings.Delay != 0 && ClearModulation();
        }

        public bool Tick()
        {
            if (Settings.Depth == 0 || Settings.Speed == 0)
                return false;

            if (_delayCounter > 0)
            {
                _delayCounter--;
                return false;
            }

            _phase = (_phase + Settings.Speed) & 0xFF;
            int triangle = TriangleLfo(_phase);
            int modM = (triangle * Settings.Depth) >> 6;
            float wave = Math.Clamp(triangle / 64f, -1f, 1f);
            if (ModM == modM && Math.Abs(CurrentWave - wave) < 0.0001f)
                return false;

            ModM = modM;
            CurrentWave = wave;
            return true;
        }

        private bool ClearModulation()
        {
            bool changed = _phase != 0 || ModM != 0 || Math.Abs(CurrentWave) > 0.0001f;
            _phase = 0;
            ModM = 0;
            CurrentWave = 0;
            return changed;
        }

        private static int TriangleLfo(int index)
        {
            int phase = index & 0xFF;
            return phase < 64 || phase >= 192
                ? unchecked((sbyte)phase)
                : 128 - phase;
        }
    }

    private sealed class Envelope
    {
        private readonly bool _psg;
        private readonly int _attack;
        private readonly int _decay;
        private readonly int _sustain;
        private readonly int _release;
        private readonly int _noteVelocity;
        private readonly bool _usesPsgHardwareEnvelope;
        private int _volume;
        private int _velocity;
        private int _processStep;
        private int _peakVelocity;
        private int _sustainVelocity;
        private int _psgHardwareVolume;
        private int _psgHardwarePeriod;
        private int _psgHardwareCounter;
        private bool _psgHardwareIncrease;
        private bool _psgHardwareActive;
        private bool _psgReleasePending;
        private EnvelopeState _state = EnvelopeState.Initializing;
        private ulong _tickAccumulatorQ32 = FixedOneQ32;

        private Envelope(bool psg, bool usesPsgHardwareEnvelope, int noteVelocity, int volume, int attack, int decay, int sustain, int release)
        {
            _psg = psg;
            _usesPsgHardwareEnvelope = psg && usesPsgHardwareEnvelope;
            _noteVelocity = Math.Clamp(noteVelocity, 0, 127);
            _volume = Math.Clamp(volume, 0, 127);
            _attack = Math.Clamp(attack, 0, 255);
            _decay = Math.Clamp(decay, 0, 255);
            _sustain = Math.Clamp(sustain, 0, 255);
            _release = Math.Clamp(release, 0, 255);

            if (_psg)
            {
                _sustain &= 0x0F;
                RecalculatePsgTargets();
            }
        }

        private void RecalculatePsgTargets()
        {
            _peakVelocity = (_noteVelocity * _volume) >> 10;
            _sustainVelocity = ((_peakVelocity * _sustain) + 0x0F) >> 4;
        }

        public static Envelope CreateDirectSound(int noteVelocity, int volume, int attack, int decay, int sustain, int release)
        {
            return new Envelope(psg: false, usesPsgHardwareEnvelope: false, noteVelocity, volume, attack, decay, sustain, release);
        }

        public static Envelope CreatePsg(int noteVelocity, int volume, int attack, int decay, int sustain, int release, bool usesHardwareEnvelope)
        {
            var envelope = new Envelope(psg: true, usesHardwareEnvelope, noteVelocity, volume, attack, decay, sustain, release);
            envelope.StepPsg();
            return envelope;
        }

        public bool Done => _state == EnvelopeState.Dead;
        public bool IsReleasing => _state >= EnvelopeState.Releasing;

        public void SetVolume(int volume)
        {
            _volume = Math.Clamp(volume, 0, 127);
            if (_psg)
            {
                RecalculatePsgTargets();
                if (_state == EnvelopeState.Playing)
                {
                    _velocity = _sustainVelocity;
                    ConfigurePsgHardwareEnvelope(_velocity, period: 0, increase: true);
                }
            }
        }

        public void SetPsgMixerVolume(int leftVolume, int rightVolume)
        {
            if (!_psg)
                return;

            _peakVelocity = Math.Clamp((leftVolume + rightVolume) / 16, 0, 15);
            _sustainVelocity = ((_peakVelocity * _sustain) + 0x0F) >> 4;
            if (_state == EnvelopeState.Playing)
            {
                _velocity = _sustainVelocity;
                ConfigurePsgHardwareEnvelope(_velocity, period: 0, increase: true);
            }
        }

        public void Release()
        {
            if (_state >= EnvelopeState.Releasing)
                return;

            if (_psg)
            {
                // MP2K consumes STOP in CgbSound. Defer even an immediate release
                // until that VBlank pass instead of changing output mid-frame.
                _state = EnvelopeState.Releasing;
                _processStep = 0;
                _psgReleasePending = true;
                return;
            }

            _state = EnvelopeState.Releasing;
        }

        public void FastStop()
        {
            Stop();
        }

        public float Advance(
            ulong tickIncrementQ32,
            int psgSteps = 0,
            int directSoundSteps = -1)
        {
            if (_psg)
            {
                for (int i = 0; i < psgSteps && !Done; i++)
                    StepPsg();
            }
            else if (directSoundSteps >= 0)
            {
                for (int i = 0; i < directSoundSteps && !Done; i++)
                    StepDirectSound();
            }
            else
            {
                _tickAccumulatorQ32 += tickIncrementQ32;
                while (_tickAccumulatorQ32 >= FixedOneQ32 && !Done)
                {
                    StepDirectSound();
                    _tickAccumulatorQ32 -= FixedOneQ32;
                }
            }

            if (Done)
                return 0f;

            return _psg
                ? _velocity / 32f
                : _velocity / 256f;
        }

        public int NoteVelocity => _noteVelocity;
        public int Level => _velocity;
        public int PsgVolume => Math.Clamp(
            _usesPsgHardwareEnvelope ? _psgHardwareVolume : _velocity,
            0,
            15);

        public void ClockPsgHardwareEnvelope()
        {
            if (!_usesPsgHardwareEnvelope || !_psgHardwareActive || _psgHardwarePeriod == 0)
                return;

            if (--_psgHardwareCounter > 0)
                return;

            _psgHardwareCounter = _psgHardwarePeriod;
            if (_psgHardwareIncrease)
            {
                if (_psgHardwareVolume < 15)
                    _psgHardwareVolume++;
                if (_psgHardwareVolume >= 15)
                    _psgHardwareActive = false;
            }
            else
            {
                if (_psgHardwareVolume > 0)
                    _psgHardwareVolume--;
                if (_psgHardwareVolume <= 0)
                    _psgHardwareActive = false;
            }
        }

        private void ConfigurePsgHardwareEnvelope(int volume, int period, bool increase)
        {
            if (!_usesPsgHardwareEnvelope)
                return;

            _psgHardwareVolume = Math.Clamp(volume, 0, 15);
            _psgHardwarePeriod = period & 0x07;
            _psgHardwareCounter = _psgHardwarePeriod;
            _psgHardwareIncrease = increase;
            _psgHardwareActive = _psgHardwarePeriod != 0 &&
                                 (increase ? _psgHardwareVolume < 15 : _psgHardwareVolume > 0);
        }

        private void StepDirectSound()
        {
            switch (_state)
            {
                case EnvelopeState.Initializing:
                    _velocity = _attack;
                    _state = EnvelopeState.Rising;
                    break;
                case EnvelopeState.Rising:
                {
                    int nextVelocity = _velocity + _attack;
                    if (nextVelocity >= 0xFF)
                    {
                        _state = EnvelopeState.Decaying;
                        _velocity = 0xFF;
                    }
                    else
                    {
                        _velocity = nextVelocity;
                    }
                    break;
                }
                case EnvelopeState.Decaying:
                {
                    int nextVelocity = (_velocity * _decay) >> 8;
                    if (nextVelocity <= _sustain)
                    {
                        _state = EnvelopeState.Playing;
                        _velocity = _sustain;
                    }
                    else
                    {
                        _velocity = nextVelocity;
                    }
                    break;
                }
                case EnvelopeState.Playing:
                    break;
                case EnvelopeState.Releasing:
                {
                    int nextVelocity = (_velocity * _release) >> 8;
                    if (nextVelocity <= 0)
                    {
                        _state = EnvelopeState.Dying;
                        _velocity = 0;
                    }
                    else
                    {
                        _velocity = nextVelocity;
                    }
                    break;
                }
                case EnvelopeState.Dying:
                    Stop();
                    break;
            }
        }

        private void StepPsg()
        {
            void EnterSustain()
            {
                _velocity = _sustainVelocity;
                _processStep = 0;
                ConfigurePsgHardwareEnvelope(_velocity, period: 0, increase: true);
                if (_velocity == 0)
                    Stop();
                else
                    _state = EnvelopeState.Playing;
            }

            switch (_state)
            {
                case EnvelopeState.Initializing:
                    _processStep = 0;
                    _velocity = 0;
                    if (_peakVelocity == 0)
                    {
                        Stop();
                        return;
                    }

                    if (_attack > 0)
                    {
                        _state = EnvelopeState.Rising;
                        ConfigurePsgHardwareEnvelope(volume: 0, period: _attack, increase: true);
                        return;
                    }

                    _velocity = _peakVelocity;
                    if (_decay == 0 || _velocity <= _sustainVelocity)
                    {
                        EnterSustain();
                        return;
                    }

                    _state = EnvelopeState.Decaying;
                    ConfigurePsgHardwareEnvelope(_velocity, _decay, increase: false);
                    return;

                case EnvelopeState.Rising:
                    if (++_processStep < _attack)
                        return;

                    _processStep = 0;
                    _velocity++;
                    if (_velocity < _peakVelocity)
                        return;

                    _velocity = _peakVelocity;
                    if (_decay == 0 || _velocity <= _sustainVelocity)
                        EnterSustain();
                    else
                    {
                        _state = EnvelopeState.Decaying;
                        ConfigurePsgHardwareEnvelope(_velocity, _decay, increase: false);
                    }
                    return;

                case EnvelopeState.Decaying:
                    if (_decay == 0 || ++_processStep >= _decay)
                    {
                        _processStep = 0;
                        _velocity--;
                        if (_velocity <= _sustainVelocity)
                            EnterSustain();
                    }
                    return;

                case EnvelopeState.Playing:
                    return;

                case EnvelopeState.Releasing:
                    if (_psgReleasePending)
                    {
                        _psgReleasePending = false;
                        if (_release == 0 || _velocity == 0)
                        {
                            Stop();
                            return;
                        }

                        ConfigurePsgHardwareEnvelope(_velocity, _release, increase: false);
                        // CgbSound initializes and decrements the release counter on
                        // this pass; the first volume step occurs on a later pass.
                        return;
                    }
                    if (_release == 0 || ++_processStep >= _release)
                    {
                        _processStep = 0;
                        _velocity--;
                        if (_velocity <= 0)
                            Stop();
                    }
                    return;

                case EnvelopeState.Dying:
                    Stop();
                    return;
            }
        }

        private void Stop()
        {
            _state = EnvelopeState.Dead;
            _velocity = 0;
        }
    }

    private sealed class Mp2kNormalReverb
    {
        private const int DefaultDmaBufferLength = 0x630;
        private const double GbaFrameRate = 59.7275;
        private const int GbaScanlinesPerFrame = 228;
        private const int GbaVBlankStartLine = 160;
        private const int Mp2kDmaSyncLine = 150;
        private sbyte[] _left = [];
        private sbyte[] _right = [];
        private sbyte[] _outputDelayLeft = [];
        private sbyte[] _outputDelayRight = [];
        private int _position;
        private int _outputDelayPosition;
        private int _blockSamples;
        private int _fixedSampleRate;
        private int _timerPeriod;
        private long _outputClockAccumulator;
        private float _heldLeft;
        private float _heldRight;
        private int _outputSampleRate;

        public Mp2kNormalReverb(int outputSampleRate, int fixedSampleRate)
        {
            _outputSampleRate = ClampOutputSampleRate(outputSampleRate);
            Configure(0, fixedSampleRate);
        }

        public int Level { get; private set; }
        public bool IsBlockStart => _position % _blockSamples == 0;

        public void SetOutputSampleRate(int outputSampleRate)
        {
            outputSampleRate = ClampOutputSampleRate(outputSampleRate);
            if (_outputSampleRate == outputSampleRate)
                return;

            _outputSampleRate = outputSampleRate;
            ResetState();
        }

        public void Configure(int level, int fixedSampleRate)
        {
            int normalizedLevel = Math.Clamp(level, 0, 127);
            fixedSampleRate = Math.Max(1, fixedSampleRate);
            Level = normalizedLevel;
            if (_fixedSampleRate == fixedSampleRate && _left.Length > 0)
                return;

            _fixedSampleRate = fixedSampleRate;
            _blockSamples = Math.Max(1, (int)Math.Round(fixedSampleRate / GbaFrameRate));
            _timerPeriod = Math.Max(1, 280_896 / _blockSamples);
            int dmaBufferCount = Math.Max(2, DefaultDmaBufferLength / _blockSamples);
            int bufferLength = Math.Max(_blockSamples * 2, _blockSamples * dmaBufferCount);
            _left = new sbyte[bufferLength];
            _right = new sbyte[bufferLength];
            // Emerald runs SoundMain at VBlank (line 160), while the DirectSound
            // DMA block boundary is synchronized at VCount line 150. Samples mixed
            // by SoundMain therefore become audible at the following line 150.
            int outputDelaySamples = Math.Max(
                1,
                (int)Math.Round(
                    _blockSamples *
                    (GbaScanlinesPerFrame - (GbaVBlankStartLine - Mp2kDmaSyncLine)) /
                    (double)GbaScanlinesPerFrame));
            _outputDelayLeft = new sbyte[outputDelaySamples];
            _outputDelayRight = new sbyte[outputDelaySamples];
            ResetState();
        }

        private void ResetState()
        {
            Array.Clear(_left);
            Array.Clear(_right);
            Array.Clear(_outputDelayLeft);
            Array.Clear(_outputDelayRight);
            _position = 0;
            _outputDelayPosition = 0;
            _outputClockAccumulator = 0;
            _heldLeft = 0f;
            _heldRight = 0f;
        }

        public void Reset() => ResetState();

        public int AdvanceOutputClock()
        {
            _outputClockAccumulator += GbaCpuFrequency;
            long denominator = (long)_timerPeriod * _outputSampleRate;
            int mixerSamples = (int)(_outputClockAccumulator / denominator);
            _outputClockAccumulator %= denominator;
            return mixerSamples;
        }

        public void GetOutput(out float left, out float right)
        {
            left = _heldLeft;
            right = _heldRight;
        }

        public void ProcessMixerSample(float dryLeft, float dryRight)
        {
            int nextBlockPosition = (_position + _blockSamples) % _left.Length;
            int historySum = _left[_position] + _right[_position] +
                             _left[nextBlockPosition] + _right[nextBlockPosition];
            int reverb = (historySum * Level) >> 9;
            if ((reverb & 0x80) != 0)
                reverb++;

            sbyte mixedLeft = unchecked((sbyte)(ToMixerInteger(dryLeft) + reverb));
            sbyte mixedRight = unchecked((sbyte)(ToMixerInteger(dryRight) + reverb));
            _left[_position] = mixedLeft;
            _right[_position] = mixedRight;
            _heldLeft = _outputDelayLeft[_outputDelayPosition] / 128f;
            _heldRight = _outputDelayRight[_outputDelayPosition] / 128f;
            _outputDelayLeft[_outputDelayPosition] = mixedLeft;
            _outputDelayRight[_outputDelayPosition] = mixedRight;

            _outputDelayPosition++;
            if (_outputDelayPosition >= _outputDelayLeft.Length)
                _outputDelayPosition = 0;

            _position++;
            if (_position >= _left.Length)
                _position = 0;
        }

        private static int ToMixerInteger(float sample)
        {
            return (int)Math.Round(sample * 128f, MidpointRounding.AwayFromZero);
        }
    }

    private sealed class ActiveVoice
    {
        private const int PcmFractionBits = 23;
        private const long PcmFractionOne = 1L << PcmFractionBits;
        private const long PcmFractionMask = PcmFractionOne - 1;
        private static readonly byte[][] SquareDutyPatterns =
        [
            [0, 0, 0, 0, 0, 0, 0, 1],
            [1, 0, 0, 0, 0, 0, 0, 1],
            [1, 0, 0, 0, 0, 1, 1, 1],
            [0, 1, 1, 1, 1, 1, 1, 0]
        ];
        private long _pcmPositionFixed;
        private long _pcmStepFixed;
        private int _lastPcmSample;
        private bool _directSoundEnvelopeStarted;
        private uint _pcmWaveFrequency;
        private int _pcmBaseKey;
        private int _pcmDivFrequency;
        private bool _useMp2kPcmPitch;
        private ulong _psgCycleAccumulatorQ32;
        private ulong _psgCyclesPerSampleQ32;
        private ulong _psgStartupDelayQ32;
        private int _psgFrequencyRegister;
        private int _squareDutyPosition;
        private int _lfsr;
        private bool _noiseShortLfsr;
        private int _noiseBaseControl;
        private int _lastNoiseSample;
        private int _baseVolume;
        private int _basePan;
        private readonly int _rhythmPan;
        private double _basePitchOffsetSemitones;
        private double _lfoPitchOffsetSemitones;
        private int _lfoVolumeOffset;
        private int _lfoPanOffset;
        private int _lfoModM;
        private int _lfoType;
        private int _directLeftVolume;
        private int _directRightVolume;
        private byte[]? _waveMemorySamples;
        private int _waveMemoryPosition;
        private int _psgLengthCounter;
        private bool _psgLengthEnabled;
        private int _squareDutyIndex;
        private bool _sweepEnabled;
        private bool _sweepInitialized;
        private bool _sweepDecrease;
        private int _sweepShift;
        private int _sweepPeriod;
        private int _sweepTimerCounter;
        private int _sweepTimer;
        private bool _skipNextPsgEnvelopeStep;

        private readonly bool _pitchLocked;
        private readonly int _outputSampleRate;
        private readonly ulong _envelopeTickIncrementQ32;

        private ActiveVoice(int id, int slot, VoiceKind kind, int midiNote, int priority, int ownerRank, long startOrder, float[]? samples, double step, int loopStart, bool loops, double dutyRatio, Envelope envelope, int baseVolume, int basePan, float leftGain, float rightGain, int outputSampleRate, bool pitchLocked = false, int rhythmPan = 0)
        {
            Id = id;
            Slot = slot;
            Kind = kind;
            MidiNote = midiNote;
            Priority = priority;
            OwnerRank = ownerRank;
            StartOrder = startOrder;
            Samples = samples;
            Step = step;
            BaseStep = step;
            LoopStart = loopStart;
            Loops = loops;
            DutyRatio = dutyRatio;
            Envelope = envelope;
            _baseVolume = Math.Clamp(baseVolume, 0, 127);
            _basePan = Math.Clamp(basePan, 0, 127);
            _rhythmPan = Math.Clamp(rhythmPan, -128, 126);
            LeftGain = leftGain;
            RightGain = rightGain;
            _outputSampleRate = ClampOutputSampleRate(outputSampleRate);
            _psgCyclesPerSampleQ32 = ((ulong)GbaCpuFrequency << 32) / (uint)_outputSampleRate;
            _envelopeTickIncrementQ32 = RateToQ32(EnvelopeUpdateRate, _outputSampleRate);
            _pitchLocked = pitchLocked;
            if (kind == VoiceKind.DirectSound)
            {
                _pcmStepFixed = StepToPcmFixed(step);
                _directLeftVolume = Math.Clamp((int)Math.Round(leftGain * 256f), 0, 255);
                _directRightVolume = Math.Clamp((int)Math.Round(rightGain * 256f), 0, 255);
            }
        }

        public static ActiveVoice CreatePcm(int id, int slot, int midiNote, int priority, int ownerRank, long startOrder, float[] samples, double step, int loopStart, bool loops, Envelope envelope, int baseVolume, int basePan, float leftGain, float rightGain, int outputSampleRate, bool pitchLocked, int rhythmPan)
        {
            return new ActiveVoice(id, slot, VoiceKind.DirectSound, midiNote, priority, ownerRank, startOrder, samples, step, loopStart, loops, dutyRatio: 0.5, envelope, baseVolume, basePan, leftGain, rightGain, outputSampleRate, pitchLocked, rhythmPan);
        }

        public static ActiveVoice CreatePsgSquare(int id, int slot, VoiceKind kind, int midiNote, int priority, int ownerRank, long startOrder, int dutyIndex, double phaseStep, Envelope envelope, int baseVolume, int basePan, float leftGain, float rightGain, int outputSampleRate, int length, int sweep, int rhythmPan)
        {
            var voice = new ActiveVoice(id, slot, kind, midiNote, priority, ownerRank, startOrder, samples: null, phaseStep, loopStart: 0, loops: true, GetPsgSquareDutyRatio(dutyIndex), envelope, baseVolume, basePan, leftGain, rightGain, outputSampleRate, rhythmPan: rhythmPan)
            {
                _squareDutyIndex = dutyIndex & 0x03
            };
            voice.ConfigurePsgLength(length);
            if (kind == VoiceKind.PsgSquare1)
                voice.ConfigureSquareSweep(sweep);
            return voice;
        }

        public static ActiveVoice CreatePsgWaveMemory(int id, int slot, int midiNote, int priority, int ownerRank, long startOrder, byte[] waveSamples, double step, Envelope envelope, int baseVolume, int basePan, float leftGain, float rightGain, int outputSampleRate, int length, int rhythmPan)
        {
            var voice = new ActiveVoice(id, slot, VoiceKind.PsgWaveMemory, midiNote, priority, ownerRank, startOrder, samples: null, step, loopStart: 0, loops: true, dutyRatio: 0.5, envelope, baseVolume, basePan, leftGain, rightGain, outputSampleRate, rhythmPan: rhythmPan)
            {
                _waveMemorySamples = waveSamples,
                _psgStartupDelayQ32 = (ulong)(6 * GbaPsgTimingFactor) << 32
            };
            voice.ConfigurePsgLength(length);
            return voice;
        }

        public static ActiveVoice CreatePsgNoise(int id, int slot, int midiNote, int priority, int ownerRank, long startOrder, int control, int baseControl, double noiseClockStep, Envelope envelope, int baseVolume, int basePan, float leftGain, float rightGain, int outputSampleRate, int length, int rhythmPan)
        {
            bool shortLfsr = (control & 0x08) != 0;
            var voice = new ActiveVoice(id, slot, VoiceKind.PsgNoise, midiNote, priority, ownerRank, startOrder, samples: null, noiseClockStep, loopStart: 0, loops: true, dutyRatio: 0.0, envelope, baseVolume, basePan, leftGain, rightGain, outputSampleRate, rhythmPan: rhythmPan)
            {
                _noiseShortLfsr = shortLfsr,
                _noiseBaseControl = baseControl,
                _lfsr = 0,
                _lastNoiseSample = 1
            };
            voice.ConfigurePsgLength(length);
            return voice;
        }

        public int Id { get; }
        public int Slot { get; set; }
        public VoiceKind Kind { get; }
        public int MidiNote { get; }
        public int Priority { get; }
        public int OwnerRank { get; }
        public long StartOrder { get; }
        public float[]? Samples { get; }
        public double Step { get; private set; }
        public double BaseStep { get; }
        public int LoopStart { get; }
        public bool Loops { get; }
        public double DutyRatio { get; }
        public Envelope Envelope { get; }
        public float LeftGain { get; private set; }
        public float RightGain { get; private set; }
        public float CurrentLevel { get; set; }
        public float MeterLevel { get; set; }
        public float CurrentLfoWave { get; private set; }
        public int LastPsgHardwareSample { get; private set; }
        public bool Done { get; private set; }
        public bool IsReleasing => Envelope.IsReleasing;
        public int SquareDutyPosition => _squareDutyPosition;
        public ulong PsgCycleAccumulatorQ32 => _psgCycleAccumulatorQ32;

        public void SetSquareDutyPosition(int position)
        {
            if (Kind is VoiceKind.PsgSquare1 or VoiceKind.PsgSquare2)
                _squareDutyPosition = position & 7;
        }

        public void SetPsgCycleAccumulatorQ32(ulong accumulator)
        {
            if (Kind is VoiceKind.PsgSquare1 or VoiceKind.PsgSquare2)
                _psgCycleAccumulatorQ32 = accumulator;
        }

        public void SkipNextPsgEnvelopeStep()
        {
            if (Kind != VoiceKind.DirectSound)
                _skipNextPsgEnvelopeStep = true;
        }

        public void Release()
        {
            Envelope.Release();
        }

        private void ConfigurePsgLength(int length)
        {
            int rawLength = Math.Clamp(length, 0, 255);
            if (rawLength == 0)
            {
                _psgLengthEnabled = false;
                _psgLengthCounter = 0;
                return;
            }

            int counterSize = Kind == VoiceKind.PsgWaveMemory ? 256 : 64;
            _psgLengthCounter = counterSize - (rawLength & (counterSize - 1));
            _psgLengthEnabled = true;
        }

        private void ConfigureSquareSweep(int sweep)
        {
            int rawSweep = Math.Clamp(sweep, 0, 255);
            _sweepPeriod = (rawSweep >> 4) & 0x07;
            _sweepShift = rawSweep & 0x07;
            _sweepDecrease = (rawSweep & 0x08) != 0;
            _sweepEnabled = rawSweep < 0x80 && (_sweepPeriod != 0 || _sweepShift != 0);
            _sweepTimerCounter = _sweepPeriod == 0 ? 8 : _sweepPeriod;
        }

        public void SetVolume(int volume)
        {
            _baseVolume = Math.Clamp(volume, 0, 127);
            if (_lfoType == 1)
                _lfoVolumeOffset = CalculateLfoVolumeOffset(_baseVolume, _lfoModM);
            ApplyEffectiveVolume();
        }

        public void SetPan(int pan)
        {
            _basePan = Math.Clamp(pan, 0, 127);
            ApplyEffectivePan();
        }

        public void ApplyTrackLfo(int modM, int type, float wave)
        {
            _lfoModM = Math.Clamp(modM, -127, 127);
            _lfoType = Math.Clamp(type, 0, 2);
            CurrentLfoWave = Math.Clamp(wave, -1f, 1f);

            double pitchOffset = _lfoType == 0 ? _lfoModM / 16.0 : 0;
            int volumeOffset = _lfoType == 1 ? CalculateLfoVolumeOffset(_baseVolume, _lfoModM) : 0;
            int panOffset = _lfoType == 2 ? _lfoModM : 0;

            _lfoPitchOffsetSemitones = pitchOffset;
            _lfoVolumeOffset = volumeOffset;
            _lfoPanOffset = panOffset;
            ApplyEffectivePitch();
            ApplyEffectiveVolume();
            ApplyEffectivePan();
        }

        public void GetDirectSoundOutput(int masterVolume, out float left, out float right)
        {
            int envelope = ((Math.Clamp(masterVolume, 0, 15) + 1) * Envelope.Level) >> 4;
            int leftEnvelope = (_directLeftVolume * envelope) >> 8;
            int rightEnvelope = (_directRightVolume * envelope) >> 8;
            int leftSample = (_lastPcmSample * leftEnvelope) >> 8;
            int rightSample = (_lastPcmSample * rightEnvelope) >> 8;
            left = leftSample / 128f;
            right = rightSample / 128f;
        }

        public void SetPitchOffset(double semitones)
        {
            _basePitchOffsetSemitones = semitones;
            ApplyEffectivePitch();
        }

        public void ConfigurePcmPitch(uint waveFrequency, int baseKey, bool useMp2kPcmPitch, int processingSampleRate)
        {
            if (Kind != VoiceKind.DirectSound)
                return;

            _pcmWaveFrequency = waveFrequency;
            _pcmBaseKey = Math.Clamp(baseKey, 0, 178);
            _useMp2kPcmPitch = useMp2kPcmPitch;
            _pcmDivFrequency = (16_777_216 / Math.Max(1, processingSampleRate) + 1) >> 1;
            ApplyEffectivePitch();
        }

        private void ApplyEffectivePitch()
        {
            if (_pitchLocked)
            {
                SetEffectiveStep(BaseStep);
                return;
            }

            if (Kind == VoiceKind.DirectSound && _useMp2kPcmPitch)
            {
                int pitch256 = (int)Math.Round((_basePitchOffsetSemitones + _lfoPitchOffsetSemitones) * 256.0);
                int keyAndFine = Math.Clamp((_pcmBaseKey << 8) + pitch256, 0, (178 << 8) | 0xFF);
                uint frequency = MidiKeyToFrequency(_pcmWaveFrequency, keyAndFine >> 8, keyAndFine & 0xFF);
                _pcmStepFixed = Math.Max(1L, (long)_pcmDivFrequency * frequency);
                Step = _pcmStepFixed / (double)PcmFractionOne;
                return;
            }

            int psgPitch256 = (int)Math.Round(
                (_basePitchOffsetSemitones + _lfoPitchOffsetSemitones) * 256.0,
                MidpointRounding.AwayFromZero);
            int key = MidiNote + (psgPitch256 >> 8);
            int fineAdjust = psgPitch256 & 0xFF;

            if (Kind == VoiceKind.PsgNoise)
            {
                int control = SelectNoiseControl(_noiseBaseControl, key);
                _noiseShortLfsr = (control & 0x08) != 0;
                _psgFrequencyRegister = control;
                Step = GetGbaNoiseClockHz(control) / _outputSampleRate;
                return;
            }

            int channel = Kind switch
            {
                VoiceKind.PsgSquare1 => 1,
                VoiceKind.PsgSquare2 => 2,
                VoiceKind.PsgWaveMemory => 3,
                _ => 0
            };
            int frequencyRegister = channel == 0
                ? 0
                : MidiKeyToCgbFrequencyRegister(channel, key, fineAdjust);
            double effectiveStep = channel == 0
                ? BaseStep * PitchFactor(_basePitchOffsetSemitones + _lfoPitchOffsetSemitones)
                : (channel == 3 ? 2_097_152.0 : 131_072.0) /
                  Math.Max(1, 2048 - frequencyRegister) / _outputSampleRate;
            if (_sweepEnabled)
            {
                if (!_sweepInitialized)
                    InitializeSquareSweep(frequencyRegister);
                return;
            }

            _psgFrequencyRegister = frequencyRegister;
            SetEffectiveStep(effectiveStep);
        }

        private void InitializeSquareSweep(int frequencyRegister)
        {
            _sweepTimer = Math.Clamp(frequencyRegister, 0, 2047);
            _psgFrequencyRegister = _sweepTimer;
            _sweepTimerCounter = _sweepPeriod == 0 ? 8 : _sweepPeriod;
            _sweepInitialized = true;
            Step = SweepTimerToFrequency(_sweepTimer) / _outputSampleRate;

            // Triggering channel 1 performs an immediate overflow calculation without
            // applying the result to the frequency register.
            if (_sweepShift != 0 && !_sweepDecrease && !TryCalculateSquareSweepTimer(_sweepTimer, out _))
                StopForSquareSweepOverflow();
        }

        public void ClockSquareSweep()
        {
            if (!_sweepEnabled || !_sweepInitialized)
                return;

            if (--_sweepTimerCounter > 0)
                return;
            _sweepTimerCounter = _sweepPeriod == 0 ? 8 : _sweepPeriod;
            if (_sweepPeriod == 0)
                return;
            if (!TryCalculateSquareSweepTimer(_sweepTimer, out int nextTimer))
            {
                StopForSquareSweepOverflow();
                return;
            }

            if (_sweepDecrease || _sweepShift != 0)
            {
                _sweepTimer = nextTimer;
                _psgFrequencyRegister = nextTimer;
                Step = SweepTimerToFrequency(_sweepTimer) / _outputSampleRate;
            }

            // An ascending hardware sweep calculates the next value a second time.
            if (!_sweepDecrease && _sweepShift != 0 && !TryCalculateSquareSweepTimer(_sweepTimer, out _))
                StopForSquareSweepOverflow();
        }

        public void ClockPsgLength()
        {
            if (!_psgLengthEnabled || Done || _psgLengthCounter <= 0)
                return;
            if (--_psgLengthCounter == 0)
            {
                Envelope.FastStop();
                Done = true;
            }
        }

        public void ClockPsgEnvelope()
        {
            if (Kind is VoiceKind.PsgSquare1 or VoiceKind.PsgSquare2 or VoiceKind.PsgNoise)
                Envelope.ClockPsgHardwareEnvelope();
        }

        private bool TryCalculateSquareSweepTimer(int timer, out int nextTimer)
        {
            int delta = timer >> _sweepShift;
            nextTimer = _sweepDecrease ? timer - delta : timer + delta;
            return (uint)nextTimer <= 2047u;
        }

        private void StopForSquareSweepOverflow()
        {
            Envelope.FastStop();
            Done = true;
        }

        private static double SweepTimerToFrequency(int timer)
        {
            return 131072.0 / (2048 - Math.Clamp(timer, 0, 2047));
        }

        private void ApplyEffectiveVolume()
        {
            int volume = Math.Clamp(_baseVolume + _lfoVolumeOffset, 0, 127);
            if (Kind == VoiceKind.DirectSound)
            {
                ApplyEffectiveDirectSoundMix(volume);
                return;
            }

            ApplyEffectivePsgMix(volume);
        }

        private void ApplyEffectivePan()
        {
            if (Kind == VoiceKind.DirectSound)
            {
                ApplyEffectiveDirectSoundMix(Math.Clamp(_baseVolume + _lfoVolumeOffset, 0, 127));
                return;
            }

            ApplyEffectivePsgMix(Math.Clamp(_baseVolume + _lfoVolumeOffset, 0, 127));
        }

        private void ApplyEffectiveDirectSoundMix(int volume)
        {
            (_directLeftVolume, _directRightVolume) = GetDirectSoundVolumes(
                Envelope.NoteVelocity,
                volume,
                _basePan,
                _rhythmPan,
                _lfoPanOffset);
            LeftGain = _directLeftVolume / 256f;
            RightGain = _directRightVolume / 256f;
        }

        private void ApplyEffectivePsgMix(int volume)
        {
            (int leftVolume, int rightVolume) = GetDirectSoundVolumes(
                Envelope.NoteVelocity,
                volume,
                _basePan,
                _rhythmPan,
                _lfoPanOffset);
            Envelope.SetPsgMixerVolume(leftVolume, rightVolume);
            (LeftGain, RightGain) = GetPsgPanGains(leftVolume, rightVolume);
        }

        private static int CalculateLfoVolumeOffset(int baseVolume, int modM)
        {
            return ((baseVolume * (modM + 128)) >> 7) - baseVolume;
        }

        public bool TryRead(
            bool useLinearInterpolation,
            int psgEnvelopeSteps,
            int directSoundEnvelopeSteps,
            out float sample)
        {
            // MPlay applies a new channel before the next mix, not after an arbitrary
            // future DMA boundary. Start its envelope once on the first rendered sample;
            // subsequent DirectSound envelope steps remain tied to DMA block starts.
            if (Kind == VoiceKind.DirectSound && !_directSoundEnvelopeStarted)
            {
                directSoundEnvelopeSteps = Math.Max(1, directSoundEnvelopeSteps);
                _directSoundEnvelopeStarted = true;
            }

            if (Done)
            {
                sample = 0f;
                return false;
            }

            if (_skipNextPsgEnvelopeStep && psgEnvelopeSteps > 0)
            {
                // The factory already performed the START branch of CgbSound.
                // Do not also apply the ordinary envelope step from that VBlank.
                psgEnvelopeSteps--;
                _skipNextPsgEnvelopeStep = false;
            }

            float envelopeLevel = Envelope.Advance(
                _envelopeTickIncrementQ32,
                psgEnvelopeSteps,
                directSoundEnvelopeSteps);
            if (Envelope.Done)
            {
                sample = 0f;
                Done = true;
                return false;
            }

            if (Kind is VoiceKind.PsgSquare1 or VoiceKind.PsgSquare2)
            {
                AdvancePsgHardwareClock();
                LastPsgHardwareSample = SquareDutyPatterns[_squareDutyIndex][_squareDutyPosition] != 0
                    ? Envelope.PsgVolume
                    : 0;
                sample = LastPsgHardwareSample / 32f;
                return true;
            }

            if (Kind == VoiceKind.PsgNoise)
            {
                AdvancePsgHardwareClock();
                LastPsgHardwareSample = _lastNoiseSample * Envelope.PsgVolume;
                sample = LastPsgHardwareSample / 32f;
                return true;
            }

            if (Kind == VoiceKind.PsgWaveMemory)
            {
                if (_waveMemorySamples is null || _waveMemorySamples.Length != 32)
                {
                    sample = 0f;
                    Done = true;
                    return false;
                }

                AdvancePsgHardwareClock();
                LastPsgHardwareSample = ScaleWaveMemorySample(
                    _waveMemorySamples[_waveMemoryPosition],
                    Envelope.PsgVolume);
                sample = LastPsgHardwareSample / 32f;
                return true;
            }

            if (Samples is null)
            {
                sample = 0f;
                Done = true;
                return false;
            }

            int index = (int)(_pcmPositionFixed >> PcmFractionBits);
            if (index >= Samples.Length)
            {
                sample = 0f;
                Done = true;
                return false;
            }

            _lastPcmSample = ReadPcmSample(index, useLinearInterpolation);
            sample = _lastPcmSample / 128f;
            _pcmPositionFixed += _pcmStepFixed;

            long endPosition = (long)Samples.Length << PcmFractionBits;
            if (Loops && _pcmPositionFixed >= endPosition)
            {
                long loopStart = (long)LoopStart << PcmFractionBits;
                long loopLength = endPosition - loopStart;
                _pcmPositionFixed = loopLength > 0
                    ? loopStart + ((_pcmPositionFixed - loopStart) % loopLength)
                    : loopStart;
            }

            return true;
        }

        private void AdvancePsgHardwareClock()
        {
            ulong elapsedCyclesQ32 = _psgCyclesPerSampleQ32;
            if (_psgStartupDelayQ32 != 0)
            {
                ulong consumed = Math.Min(elapsedCyclesQ32, _psgStartupDelayQ32);
                _psgStartupDelayQ32 -= consumed;
                elapsedCyclesQ32 -= consumed;
            }
            _psgCycleAccumulatorQ32 += elapsedCyclesQ32;
            ulong periodCycles = Kind switch
            {
                VoiceKind.PsgSquare1 or VoiceKind.PsgSquare2 =>
                    (ulong)(4 * GbaPsgTimingFactor * Math.Max(1, 2048 - _psgFrequencyRegister)),
                VoiceKind.PsgWaveMemory =>
                    (ulong)(2 * GbaPsgTimingFactor * Math.Max(1, 2048 - _psgFrequencyRegister)),
                VoiceKind.PsgNoise => GetNoisePeriodCycles(_psgFrequencyRegister),
                _ => ulong.MaxValue >> 32
            };
            ulong periodQ32 = periodCycles << 32;
            while (_psgCycleAccumulatorQ32 >= periodQ32)
            {
                _psgCycleAccumulatorQ32 -= periodQ32;
                switch (Kind)
                {
                    case VoiceKind.PsgSquare1:
                    case VoiceKind.PsgSquare2:
                        _squareDutyPosition = (_squareDutyPosition + 1) & 7;
                        break;
                    case VoiceKind.PsgWaveMemory:
                        _waveMemoryPosition = (_waveMemoryPosition + 1) & 31;
                        break;
                    case VoiceKind.PsgNoise:
                        ClockNoiseLfsr();
                        break;
                }
            }
        }

        private static ulong GetNoisePeriodCycles(int control)
        {
            int ratio = control & 0x07;
            int shift = (control >> 4) & 0x0F;
            ulong divisor = ratio == 0 ? 1u : (uint)(ratio * 2);
            return (divisor << (shift + 3)) * GbaPsgTimingFactor;
        }

        private static int ScaleWaveMemorySample(int sample, int envelopeVolume)
        {
            if (envelopeVolume <= 1)
                return 0;
            if (envelopeVolume <= 5)
                return sample >> 2;
            if (envelopeVolume <= 9)
                return sample >> 1;
            if (envelopeVolume <= 13)
                return (sample * 3) >> 2;
            return sample;
        }

        private int ReadPcmSample(int index, bool useLinearInterpolation)
        {
            if (Samples is null || Samples.Length == 0)
                return 0;
            int current = Math.Clamp((int)Math.Round(Samples[index] * 128f), -128, 127);
            if (!useLinearInterpolation)
                return current;

            int next = index + 1;
            if (next >= Samples.Length)
                next = Loops ? LoopStart : index;

            int nextSample = Math.Clamp((int)Math.Round(Samples[next] * 128f), -128, 127);
            long fraction = _pcmPositionFixed & PcmFractionMask;
            return current + (int)((fraction * (nextSample - current)) >> PcmFractionBits);
        }

        private void SetEffectiveStep(double step)
        {
            Step = step;
            if (Kind == VoiceKind.DirectSound)
                _pcmStepFixed = StepToPcmFixed(step);
        }

        private static long StepToPcmFixed(double step)
        {
            return Math.Max(1L, (long)Math.Round(Math.Max(0.0, step) * PcmFractionOne));
        }

        private static ulong RateToQ32(double eventsPerSecond, int sampleRate)
        {
            double increment = Math.Max(0.0, eventsPerSecond) * FixedOneQ32 / Math.Max(1, sampleRate);
            return (ulong)Math.Max(0.0, Math.Round(increment));
        }

        private static double GetPsgSquareDutyRatio(int dutyIndex)
        {
            return (dutyIndex & 0x03) switch
            {
                0 => 0.125,
                1 => 0.25,
                2 => 0.5,
                3 => 0.75,
                _ => 0.5
            };
        }

        private void ClockNoiseLfsr()
        {
            int output = (_lfsr ^ (_lfsr >> 1) ^ 1) & 0x01;
            _lfsr >>= 1;
            int coefficient = _noiseShortLfsr ? 0x4040 : 0x4000;
            if (output != 0)
                _lfsr |= coefficient;
            else
                _lfsr &= ~coefficient;
            _lfsr &= 0x7FFF;
            _lastNoiseSample = output;
        }
    }
}

public sealed class AgbAudioRecording
{
    public AgbAudioRecording(WaveFormat waveFormat, float[] samples)
    {
        WaveFormat = waveFormat;
        Samples = samples;
    }

    public WaveFormat WaveFormat { get; }
    public float[] Samples { get; }
    public bool HasSamples => Samples.Length > 0;
    public double DurationSeconds => WaveFormat.Channels <= 0 || WaveFormat.SampleRate <= 0
        ? 0
        : Samples.Length / (double)(WaveFormat.Channels * WaveFormat.SampleRate);

    public void Save(string path)
    {
        using var writer = new WaveFileWriter(path, WaveFormat);
        writer.WriteSamples(Samples, 0, Samples.Length);
    }
}
