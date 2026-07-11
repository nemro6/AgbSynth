using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AgbSynth.App.MIDI;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;

namespace AgbSynth.App.Audio;

public sealed class Mp2kPreparedVoiceBank
{
    private readonly Mp2kPreparedVoice?[,] _voices = new Mp2kPreparedVoice[128, 128];
    private readonly string[] _programLabels = Enumerable.Repeat("-", 128).ToArray();

    public void Set(int program, int note, Mp2kPreparedVoice? voice)
    {
        if ((uint)program < 128 && (uint)note < 128)
            _voices[program, note] = voice;
    }

    public Mp2kPreparedVoice? Get(int program, int note)
    {
        return (uint)program < 128 && (uint)note < 128
            ? _voices[program, note]
            : null;
    }

    public void SetProgramLabel(int program, string label)
    {
        if ((uint)program < 128)
            _programLabels[program] = string.IsNullOrWhiteSpace(label) ? "-" : label;
    }

    public string GetProgramLabel(int program)
    {
        return (uint)program < 128 ? _programLabels[program] : "-";
    }
}

public sealed record Mp2kPreparedVoice(
    VoiceProjectInfo Voice,
    int BaseKey,
    int PlaybackNote,
    int? ForcedPan,
    int ProgramId,
    float[]? Pcm,
    SampleHeaderProjectInfo? SampleHeader,
    byte[]? WaveRam);

public readonly record struct Mp2kSequenceChannelSnapshot(
    int Channel,
    int ActiveNote,
    int Velocity,
    int Program,
    bool HasProgram,
    string InstrumentLabel,
    int Volume,
    int Pan,
    int BendRange,
    int PitchBend,
    int Modulation,
    int ModSpeed,
    int ModType,
    int ModDelay,
    int Tune,
    int Priority,
    bool VoiceRejected,
    string IssueLog);

public sealed record Mp2kSequencePlaybackSnapshot(
    long Revision,
    IReadOnlyList<ushort> ActiveNoteChannelMasks,
    IReadOnlyList<Mp2kSequenceChannelSnapshot> Channels);

/// <summary>
/// Audio-thread-owned MP2K channel state. UI code reads immutable snapshots only.
/// </summary>
public sealed class Mp2kSequenceAudioRuntime
{
    private readonly AgbAudioEngine _engine;
    private readonly Mp2kPreparedVoiceBank _voiceBank;
    private readonly MidiCcMapping _mapping;
    private readonly int _playerPriority;
    private readonly int _fixedSampleRate;
    private readonly ChannelState[] _channels = CreateChannels();
    private readonly List<RuntimeVoice> _ownedVoices = new();
    private readonly Dictionary<(int Channel, int Note), Queue<RuntimeVoice>> _noteVoices = new();
    private readonly ushort[] _activeNoteMasks = new ushort[128];
    private readonly int[,] _activeNoteCounts = new int[128, 16];
    private Mp2kSequencePlaybackSnapshot _snapshot;
    private long _revision;
    private int _enabledChannelMask = 0xFFFF;
    private bool _stateDirty;

    public Mp2kSequenceAudioRuntime(
        AgbAudioEngine engine,
        Mp2kPreparedVoiceBank voiceBank,
        MidiCcMapping mapping,
        int playerPriority,
        int fixedSampleRate)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _voiceBank = voiceBank ?? throw new ArgumentNullException(nameof(voiceBank));
        _mapping = (mapping ?? throw new ArgumentNullException(nameof(mapping))).Clone();
        _playerPriority = Math.Clamp(playerPriority, 0, 255);
        _fixedSampleRate = Math.Max(1, fixedSampleRate);
        _snapshot = BuildSnapshot();
    }

    public Mp2kSequencePlaybackSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void SetChannelEnabled(int channel, bool enabled)
    {
        if ((uint)channel >= 16)
            return;

        int bit = 1 << channel;
        int original;
        int updated;
        do
        {
            original = Volatile.Read(ref _enabledChannelMask);
            updated = enabled ? original | bit : original & ~bit;
        }
        while (Interlocked.CompareExchange(ref _enabledChannelMask, updated, original) != original);
    }

    public void ProcessEvent(MidiPlaybackEvent midiEvent)
    {
        PruneInactiveVoices();
        int channel = Math.Clamp(midiEvent.Channel, 0, 15);
        switch (midiEvent.Kind)
        {
            case MidiPlaybackEventKind.NoteOn:
                if (midiEvent.Data2 == 0)
                    NoteOff(channel, midiEvent.Data1);
                else
                    NoteOn(channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiPlaybackEventKind.NoteOff:
                NoteOff(channel, midiEvent.Data1);
                break;
            case MidiPlaybackEventKind.ControlChange:
                ApplyControlChange(channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiPlaybackEventKind.ProgramChange:
                _channels[channel].Program = Math.Clamp(midiEvent.Data1, 0, 127);
                _channels[channel].HasProgram = true;
                _channels[channel].InstrumentLabel = _voiceBank.GetProgramLabel(_channels[channel].Program);
                break;
            case MidiPlaybackEventKind.PitchBend:
                _channels[channel].PitchBend = Math.Clamp(midiEvent.Data1, 0, 16383);
                ApplyChannelPitch(channel);
                break;
            case MidiPlaybackEventKind.Tempo:
                break;
        }

        _stateDirty = true;
    }

    public void Tick(long _)
    {
        if (PruneInactiveVoices())
            _stateDirty = true;
        if (!_stateDirty)
            return;

        PublishSnapshot();
        _stateDirty = false;
    }

    public void StopAll()
    {
        foreach (RuntimeVoice voice in _ownedVoices)
        {
            if (_engine.IsVoiceActive(voice.VoiceId))
                _engine.NoteOff(voice.VoiceId);
        }

        _ownedVoices.Clear();
        _noteVoices.Clear();
        Array.Clear(_activeNoteMasks);
        Array.Clear(_activeNoteCounts);
        foreach (ChannelState channel in _channels)
            channel.ActiveNote = -1;
        PublishSnapshot();
        _stateDirty = false;
    }

    private void NoteOn(int channel, int note, int velocity)
    {
        note = Math.Clamp(note, 0, 127);
        velocity = Math.Clamp(velocity, 0, 127);
        ChannelState state = _channels[channel];
        if ((Volatile.Read(ref _enabledChannelMask) & (1 << channel)) == 0 || !state.HasProgram)
            return;

        Mp2kPreparedVoice? prepared = _voiceBank.Get(state.Program, note);
        if (prepared is null)
            return;

        VoiceProjectInfo source = prepared.Voice;
        int type = state.XcmdType >= 0 ? state.XcmdType : source.Type;
        int attack = state.XcmdAttack >= 0 ? state.XcmdAttack : source.Attack;
        int decay = state.XcmdDecay >= 0 ? state.XcmdDecay : source.Decay;
        int sustain = state.XcmdSustain >= 0 ? state.XcmdSustain : source.Sustain;
        int release = state.XcmdRelease >= 0 ? state.XcmdRelease : source.Release;
        int length = state.XcmdLength >= 0 ? state.XcmdLength : source.Length;
        int sweep = state.XcmdSweep >= 0 ? state.XcmdSweep : source.PanOrSweep;
        bool directSound = (type & 0x07) == 0 && prepared.Pcm is { Length: > 0 } && prepared.SampleHeader is not null;
        bool fixedPitch = directSound && (type & 0x08) != 0;
        int playbackNote = fixedPitch ? prepared.BaseKey : prepared.PlaybackNote;
        int rhythmPan = prepared.ForcedPan is int forcedPan
            ? (Math.Clamp(forcedPan, 0, 127) - 64) << 1
            : 0;
        int priority = Math.Min(255, _playerPriority + state.Priority);
        double pitchOffset = GetPitchOffsetSemitones(state);
        var lfo = new AgbLfoSettings(state.Modulation, state.ModSpeed, state.ModType, state.ModDelay);
        int voiceId;
        int psgType = type & 0x07;
        if (psgType is 0x01 or 0x02)
        {
            voiceId = _engine.NoteOnSquare(
                GetSquareDuty(source),
                playbackNote,
                velocity,
                state.Volume,
                state.Pan,
                priority,
                attack,
                decay,
                sustain,
                release,
                squareChannel: psgType == 0x02 ? 2 : 1,
                pitchOffsetSemitones: pitchOffset,
                lfoSettings: lfo,
                ownerRank: channel,
                length: length,
                sweep: psgType == 0x01 ? sweep : 0,
                rhythmPan: rhythmPan);
        }
        else if (psgType == 0x03 && prepared.WaveRam is { Length: >= 16 } waveRam)
        {
            voiceId = _engine.NoteOnWaveMemory(
                waveRam,
                prepared.BaseKey,
                playbackNote,
                velocity,
                state.Volume,
                state.Pan,
                priority,
                attack,
                decay,
                sustain,
                release,
                pitchOffset,
                lfo,
                channel,
                length,
                rhythmPan);
        }
        else if (psgType == 0x04)
        {
            voiceId = _engine.NoteOnNoise(
                GetNoiseControl(source),
                prepared.BaseKey,
                playbackNote,
                velocity,
                state.Volume,
                state.Pan,
                priority,
                attack,
                decay,
                sustain,
                release,
                pitchOffset,
                lfo,
                channel,
                length,
                rhythmPan);
        }
        else if (directSound)
        {
            voiceId = _engine.NoteOnPrepared(
                prepared.Pcm!,
                prepared.SampleHeader!,
                prepared.BaseKey,
                playbackNote,
                velocity,
                state.Volume,
                state.Pan,
                priority,
                attack,
                decay,
                sustain,
                release,
                pitchOffset,
                lfo,
                fixedPitch,
                _fixedSampleRate,
                channel,
                rhythmPan);
        }
        else
        {
            return;
        }

        if (voiceId < 0)
        {
            state.VoiceRejected = true;
            state.IssueLog = "Hardware channel limit exceeded.";
            return;
        }

        var runtimeVoice = new RuntimeVoice(voiceId, channel, note);
        _ownedVoices.Add(runtimeVoice);
        var key = (channel, note);
        if (!_noteVoices.TryGetValue(key, out Queue<RuntimeVoice>? voices))
        {
            voices = new Queue<RuntimeVoice>();
            _noteVoices[key] = voices;
        }
        voices.Enqueue(runtimeVoice);
        SetNoteActive(note, channel, true);

        state.ActiveNote = note;
        state.Velocity = velocity;
        state.Program = prepared.ProgramId;
        state.InstrumentLabel = ResolveVoiceLabel(source);
        state.VoiceRejected = false;
        state.IssueLog = "-";
        PruneInactiveVoices();
    }

    private void NoteOff(int channel, int note)
    {
        note = Math.Clamp(note, 0, 127);
        var key = (channel, note);
        if (!_noteVoices.TryGetValue(key, out Queue<RuntimeVoice>? voices) || voices.Count == 0)
            return;

        RuntimeVoice voice = voices.Dequeue();
        voice.Released = true;
        _engine.NoteOff(voice.VoiceId);
        SetNoteActive(note, channel, false);
        if (voices.Count == 0)
            _noteVoices.Remove(key);
        RefreshActiveNote(channel);
    }

    private void ApplyControlChange(int channel, int controller, int value)
    {
        value = Math.Clamp(value, 0, 127);
        ChannelState state = _channels[channel];
        if (controller == _mapping.Type)
            state.XcmdType = value;
        else if (controller == _mapping.Attack)
            state.XcmdAttack = value;
        else if (controller == _mapping.Decay)
            state.XcmdDecay = value;
        else if (controller == _mapping.Sustain)
            state.XcmdSustain = value;
        else if (controller == _mapping.Release)
            state.XcmdRelease = value;
        else if (controller == _mapping.EchoVolume)
            state.XcmdEchoVolume = value;
        else if (controller == _mapping.EchoLength)
            state.XcmdEchoLength = value;
        else if (controller == _mapping.Length)
            state.XcmdLength = value;
        else if (controller == _mapping.Sweep)
            state.XcmdSweep = value;

        if (controller == _mapping.Modulation)
        {
            state.Modulation = value;
            ApplyChannelLfo(channel);
        }
        else if (controller == _mapping.Volume)
        {
            state.Volume = value;
            ForEachActiveVoice(channel, voiceId => _engine.SetVoiceVolume(voiceId, value));
        }
        else if (controller == _mapping.Pan)
        {
            state.Pan = value;
            ForEachActiveVoice(channel, voiceId => _engine.SetVoicePan(voiceId, value));
        }
        else if (controller == _mapping.BendRangeLow)
        {
            state.BendRange = (state.BendRangeHigh << 7) | value;
            ApplyChannelPitch(channel);
        }
        else if (controller == _mapping.BendRangeHigh)
        {
            state.BendRangeHigh = value & 1;
            state.BendRange = (state.BendRangeHigh << 7) | (state.BendRange & 0x7F);
            ApplyChannelPitch(channel);
        }
        else if (controller == _mapping.LfoSpeed)
        {
            state.ModSpeed = value;
            ApplyChannelLfo(channel);
        }
        else if (controller == _mapping.ModulationType)
        {
            state.ModType = Math.Clamp(value, 0, 2);
            ApplyChannelLfo(channel);
        }
        else if (controller == _mapping.LfoDelay)
        {
            state.ModDelay = value;
            ApplyChannelLfo(channel);
        }
        else if (controller == _mapping.Tune)
        {
            state.Tune = value;
            ApplyChannelPitch(channel);
        }
        else if (controller == _mapping.Priority)
        {
            state.Priority = value;
        }
    }

    private void ApplyChannelPitch(int channel)
    {
        double semitones = GetPitchOffsetSemitones(_channels[channel]);
        ForEachActiveVoice(channel, voiceId => _engine.SetVoicePitchOffset(voiceId, semitones));
    }

    private void ApplyChannelLfo(int channel)
    {
        ChannelState state = _channels[channel];
        _engine.SetTrackLfoSettings(
            channel,
            new AgbLfoSettings(state.Modulation, state.ModSpeed, state.ModType, state.ModDelay));
    }

    private void ForEachActiveVoice(int channel, Action<int> action)
    {
        foreach (RuntimeVoice voice in _ownedVoices)
        {
            if (voice.Channel == channel && _engine.IsVoiceActive(voice.VoiceId))
                action(voice.VoiceId);
        }
    }

    private bool PruneInactiveVoices()
    {
        bool changed = false;
        for (int i = _ownedVoices.Count - 1; i >= 0; i--)
        {
            RuntimeVoice voice = _ownedVoices[i];
            if (_engine.IsVoiceActive(voice.VoiceId))
                continue;

            _ownedVoices.RemoveAt(i);
            if (!voice.Released)
            {
                RemoveQueuedVoice(voice);
                SetNoteActive(voice.Note, voice.Channel, false);
                RefreshActiveNote(voice.Channel);
            }
            changed = true;
        }

        return changed;
    }

    private void RemoveQueuedVoice(RuntimeVoice voice)
    {
        var key = (voice.Channel, voice.Note);
        if (!_noteVoices.TryGetValue(key, out Queue<RuntimeVoice>? queue))
            return;

        RuntimeVoice[] retained = queue.Where(candidate => !ReferenceEquals(candidate, voice)).ToArray();
        if (retained.Length == 0)
            _noteVoices.Remove(key);
        else
            _noteVoices[key] = new Queue<RuntimeVoice>(retained);
    }

    private void RefreshActiveNote(int channel)
    {
        RuntimeVoice? latest = _ownedVoices.LastOrDefault(voice =>
            voice.Channel == channel &&
            !voice.Released &&
            _engine.IsVoiceActive(voice.VoiceId));
        _channels[channel].ActiveNote = latest?.Note ?? -1;
    }

    private void SetNoteActive(int note, int channel, bool active)
    {
        ushort mask = (ushort)(1 << channel);
        if (active)
        {
            _activeNoteCounts[note, channel]++;
            _activeNoteMasks[note] |= mask;
        }
        else
        {
            if (_activeNoteCounts[note, channel] > 0)
                _activeNoteCounts[note, channel]--;
            if (_activeNoteCounts[note, channel] == 0)
                _activeNoteMasks[note] &= (ushort)~mask;
        }
    }

    private void PublishSnapshot()
    {
        Volatile.Write(ref _snapshot, BuildSnapshot());
    }

    private Mp2kSequencePlaybackSnapshot BuildSnapshot()
    {
        var channels = new Mp2kSequenceChannelSnapshot[16];
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelState state = _channels[i];
            channels[i] = new Mp2kSequenceChannelSnapshot(
                i,
                state.ActiveNote,
                state.Velocity,
                state.Program,
                state.HasProgram,
                state.InstrumentLabel,
                state.Volume,
                state.Pan,
                state.BendRange,
                ToMixerPitchBend(state.PitchBend),
                state.Modulation,
                state.ModSpeed,
                state.ModType,
                state.ModDelay,
                state.Tune,
                state.Priority,
                state.VoiceRejected,
                state.IssueLog);
        }

        return new Mp2kSequencePlaybackSnapshot(
            Interlocked.Increment(ref _revision),
            _activeNoteMasks.ToArray(),
            channels);
    }

    private static ChannelState[] CreateChannels()
    {
        var channels = new ChannelState[16];
        for (int i = 0; i < channels.Length; i++)
            channels[i] = new ChannelState();
        return channels;
    }

    private static double GetPitchOffsetSemitones(ChannelState state)
    {
        double bend = state.PitchBend >= 8192
            ? (state.PitchBend - 8192) / 8191.0
            : (state.PitchBend - 8192) / 8192.0;
        double bendSemitones = bend * Math.Clamp(state.BendRange, 0, 255);
        double tuneSemitones = state.Tune >= 64
            ? (state.Tune - 64) / 63.0
            : (state.Tune - 64) / 64.0;
        return bendSemitones + tuneSemitones;
    }

    private static int ToMixerPitchBend(int value)
    {
        int clamped = Math.Clamp(value, 0, 16383);
        return clamped >= 8192
            ? (int)Math.Round((clamped - 8192) * (63.0 / 8191.0))
            : -(int)Math.Round((8192 - clamped) * (64.0 / 8192.0));
    }

    private static int GetSquareDuty(VoiceProjectInfo voice)
    {
        if (voice.PsgSquare is not null)
            return Math.Clamp(voice.PsgSquare.DutyIndex, 0, 3);
        byte[] raw = DecodeHex(voice.RawEntryHex);
        return raw.Length > 4 ? raw[4] & 3 : 2;
    }

    private static int GetNoiseControl(VoiceProjectInfo voice)
    {
        if (voice.PsgNoise is not null)
            return Math.Clamp(voice.PsgNoise.Control, 0, 255);
        byte[] raw = DecodeHex(voice.RawEntryHex);
        return raw.Length > 4 ? raw[4] : 0;
    }

    private static byte[] DecodeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0)
            return [];
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string ResolveVoiceLabel(VoiceProjectInfo voice)
    {
        if (!string.IsNullOrWhiteSpace(voice.Label))
            return voice.Label;
        if (!string.IsNullOrWhiteSpace(voice.TypeName))
            return voice.TypeName;
        return "-";
    }

    private sealed class RuntimeVoice(int voiceId, int channel, int note)
    {
        public int VoiceId { get; } = voiceId;
        public int Channel { get; } = channel;
        public int Note { get; } = note;
        public bool Released { get; set; }
    }

    private sealed class ChannelState
    {
        public int ActiveNote { get; set; } = -1;
        public int Velocity { get; set; }
        public int Program { get; set; }
        public bool HasProgram { get; set; }
        public string InstrumentLabel { get; set; } = "-";
        public int Volume { get; set; } = 100;
        public int Pan { get; set; } = 64;
        public int BendRange { get; set; } = 2;
        public int BendRangeHigh { get; set; }
        public int PitchBend { get; set; } = 8192;
        public int Modulation { get; set; }
        public int ModSpeed { get; set; } = 22;
        public int ModType { get; set; }
        public int ModDelay { get; set; }
        public int Tune { get; set; } = 64;
        public int Priority { get; set; }
        public bool VoiceRejected { get; set; }
        public string IssueLog { get; set; } = "-";
        public int XcmdType { get; set; } = -1;
        public int XcmdAttack { get; set; } = -1;
        public int XcmdDecay { get; set; } = -1;
        public int XcmdSustain { get; set; } = -1;
        public int XcmdRelease { get; set; } = -1;
        public int XcmdEchoVolume { get; set; }
        public int XcmdEchoLength { get; set; }
        public int XcmdLength { get; set; } = -1;
        public int XcmdSweep { get; set; } = -1;
    }
}
