using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AgbSynth.App.Audio;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ushort[] _activeNoteChannelMasks = new ushort[128];
    private readonly int[,] _activeNoteCounts = new int[128, 16];
    private readonly Dictionary<(PreviewInputSource Source, int Channel, int Note), List<ActivePreviewVoice>> _activePreviewVoices = new();
    private readonly int[] _channelVolumes = Enumerable.Repeat(127, 16).ToArray();
    private readonly int[] _channelPans = Enumerable.Repeat(64, 16).ToArray();
    private readonly int[] _channelBendRanges = Enumerable.Repeat(2, 16).ToArray();
    private readonly int[] _channelBendRangeHighBits = new int[16];
    private readonly int[] _channelPitchBends = Enumerable.Repeat(8192, 16).ToArray();
    private readonly int[] _channelModDepths = new int[16];
    private readonly int[] _channelModSpeeds = Enumerable.Repeat(22, 16).ToArray();
    private readonly int[] _channelModTypes = new int[16];
    private readonly int[] _channelModDelays = new int[16];
    private readonly int[] _channelTunes = Enumerable.Repeat(64, 16).ToArray();
    private readonly int[] _channelPriorities = new int[16];
    private readonly int[] _channelPrograms = new int[16];
    private readonly bool[] _channelHasProgram = new bool[16];
    private readonly int[] _channelXcmdTypes = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdAttacks = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdDecays = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdSustains = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdReleases = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdEchoVolumes = new int[16];
    private readonly int[] _channelXcmdEchoLengths = new int[16];
    private readonly int[] _channelXcmdLengths = Enumerable.Repeat(-1, 16).ToArray();
    private readonly int[] _channelXcmdSweeps = Enumerable.Repeat(-1, 16).ToArray();
    private AgbAudioEngine? _audioEngine;
    private AudioBufferSizeOption? _selectedAudioBufferSize;
    private int _preferredAudioBufferLatencyMs = 64;
    private bool _outputQuantizeEnabled = true;
    private bool _mp2kPcmProcessingEnabled = true;
    private int _fixedDirectSoundSampleRate = AgbAudioEngine.DefaultMp2kFixedSampleRate;
    private int _directSoundMasterVolume = 15;
    private int _gbaDacConfig = 9;
    private int _soundModeReverbLevel;
    private int _directSoundMixerChannelCount = 5;
    private int _currentPlayerPriority;
    private int _masterVolume = 90;
    private bool _isMasterMuted;

    public IReadOnlyList<ushort> ActiveNoteChannelMasks => _activeNoteChannelMasks.ToArray();
    public IReadOnlyList<AudioBufferSizeOption> AudioBufferSizeOptions { get; } =
    [
        new AudioBufferSizeOption(48),
        new AudioBufferSizeOption(64),
        new AudioBufferSizeOption(96),
        new AudioBufferSizeOption(128),
        new AudioBufferSizeOption(192),
        new AudioBufferSizeOption(256),
        new AudioBufferSizeOption(384),
        new AudioBufferSizeOption(512)
    ];
    public IReadOnlyList<int> DirectSoundChannelCountOptions { get; } = Enumerable.Range(0, 13).ToArray();

    public AudioBufferSizeOption? SelectedAudioBufferSize
    {
        get => _selectedAudioBufferSize;
        set
        {
            if (!SetField(ref _selectedAudioBufferSize, value) || value is null)
                return;

            _preferredAudioBufferLatencyMs = value.LatencyMs;
            if (_audioEngine is not null)
                ApplySelectedAudioBufferSize();
            SaveUserSettingsIfReady();
        }
    }

    private AgbAudioEngine AudioEngine
    {
        get
        {
            if (_audioEngine is null)
            {
                _audioEngine = new AgbAudioEngine(
                    _preferredAudioBufferLatencyMs,
                    _directSoundMixerChannelCount,
                    _selectedAudioOutputDeviceNumber,
                    _outputSampleRate)
                {
                    OutputQuantizeEnabled = _outputQuantizeEnabled,
                    LinearInterpolationEnabled = _linearInterpolationEnabled,
                    Mp2kPcmProcessingEnabled = _mp2kPcmProcessingEnabled,
                    StereoOutputEnabled = SelectedOutputChannelMode?.IsStereo ?? true,
                    GbaDacConfig = _gbaDacConfig,
                    DirectSoundMasterVolume = _directSoundMasterVolume,
                    EmulationGain = (float)(EmulationVolume / 255.0),
                    MasterGain = EffectiveMasterGain
                };
                _audioEngine.ConfigureReverb(_reverbEnabled ? _soundModeReverbLevel : 0, _fixedDirectSoundSampleRate);
            }

            return _audioEngine;
        }
    }

    public int MasterVolume
    {
        get => _masterVolume;
        set
        {
            int volume = Math.Clamp(value, 0, 100);
            if (!SetField(ref _masterVolume, volume))
                return;

            ApplyMasterGain();
        }
    }

    public bool IsMasterMuted
    {
        get => _isMasterMuted;
        set
        {
            if (!SetField(ref _isMasterMuted, value))
                return;

            OnPropertyChanged(nameof(MasterMuteButtonText));
            ApplyMasterGain();
        }
    }

    public string MasterMuteButtonText => IsMasterMuted ? "MUTED" : "MUTE";

    private float EffectiveMasterGain => IsMasterMuted ? 0f : MasterVolume / 100f;

    public bool OutputQuantizeEnabled
    {
        get => _outputQuantizeEnabled;
        set
        {
            if (!SetField(ref _outputQuantizeEnabled, value))
                return;

            if (_audioEngine is not null)
                _audioEngine.OutputQuantizeEnabled = value;
            SaveUserSettingsIfReady();
        }
    }

    public bool Mp2kPcmProcessingEnabled
    {
        get => _mp2kPcmProcessingEnabled;
        set
        {
            if (!SetField(ref _mp2kPcmProcessingEnabled, value))
                return;

            _linearInterpolationEnabled = value;
            if (_audioEngine is not null)
            {
                _audioEngine.Mp2kPcmProcessingEnabled = value;
                _audioEngine.LinearInterpolationEnabled = value;
            }
            SaveUserSettingsIfReady();
        }
    }

    public int SelectedDirectSoundChannelCount
    {
        get => _directSoundMixerChannelCount;
        set
        {
            int channelCount = Math.Clamp(value, 0, 12);
            if (!SetField(ref _directSoundMixerChannelCount, channelCount))
                return;

            _audioEngine?.SetDirectSoundMixerChannelCount(channelCount);
            SaveUserSettingsIfReady();
        }
    }

    public void InitializeAudioBufferSize()
    {
        SelectedAudioBufferSize = AudioBufferSizeOptions.FirstOrDefault(o => o.LatencyMs == _preferredAudioBufferLatencyMs)
            ?? AudioBufferSizeOptions.FirstOrDefault();
        if (_audioEngine is not null)
            _audioEngine.OutputQuantizeEnabled = _outputQuantizeEnabled;
        SaveUserSettingsIfReady();
    }

    private void ApplyMasterGain()
    {
        if (_audioEngine is not null)
            _audioEngine.MasterGain = EffectiveMasterGain;
    }

    public void PreviewKeyboardNoteOn(int note, int velocity = 100)
    {
        PreviewNoteOnCore(note, velocity, channel: 0, PreviewInputSource.Keyboard, useMidiProgram: false);
    }

    public void PreviewKeyboardNoteOff(int note)
    {
        PreviewNoteOffCore(note, channel: 0, PreviewInputSource.Keyboard);
    }

    public void PreviewWaveMemoryKeyboardNoteOn(int note, int velocity = 100)
    {
        if (SelectedWaveMemory is not { } waveMemory)
            return;

        PreviewNoteOnCore(
            note,
            velocity,
            channel: 0,
            PreviewInputSource.Keyboard,
            useMidiProgram: false,
            overrideVoice: CreateWaveMemoryPreviewVoice(waveMemory));
    }

    public void PreviewWaveMemoryKeyboardNoteOff(int note)
    {
        PreviewNoteOffCore(note, channel: 0, PreviewInputSource.Keyboard);
    }

    private void RestartActiveWaveMemoryKeyboardPreviewNotes()
    {
        if (SelectedWaveMemory is null)
            return;

        var activeNotes = _activePreviewVoices
            .Where(pair =>
                pair.Key.Source == PreviewInputSource.Keyboard &&
                pair.Key.Channel == 0 &&
                pair.Value.Any(voice => voice.IsPsg))
            .Select(pair => pair.Key.Note)
            .Distinct()
            .ToArray();

        foreach (int note in activeNotes)
        {
            PreviewNoteOffCore(note, channel: 0, PreviewInputSource.Keyboard);
            PreviewWaveMemoryKeyboardNoteOn(note, velocity: 110);
        }
    }

    public void PreviewWaveDataKeyboardNoteOn(int note, int velocity = 100)
    {
        if (SelectedWaveData is not { } waveData)
            return;

        note = Math.Clamp(note, 0, 127);
        velocity = Math.Clamp(velocity, 0, 127);
        if (velocity == 0)
        {
            PreviewNoteOffCore(note, channel: 0, PreviewInputSource.Keyboard);
            return;
        }

        const int channel = 0;
        if (!IsMixerChannelOutputEnabled(channel))
            return;

        byte[] pcm = waveData.DataBytes;
        if (pcm.Length == 0)
            return;

        PreviewNoteOffCore(note, channel, PreviewInputSource.Keyboard);

        SampleHeaderProjectInfo header = waveData.ToSampleHeader();
        if (header.Size == 0 || header.Size > pcm.Length)
            header.Size = (uint)pcm.Length;

        VoiceProjectInfo voice = CreateWaveDataPreviewVoice(waveData, header);
        int voiceId = AudioEngine.NoteOn(
            pcm,
            header,
            baseKey: 60,
            midiNote: note,
            velocity,
            _channelVolumes[channel],
            _channelPans[channel],
            _channelPriorities[channel],
            attack: 255,
            decay: 255,
            sustain: 255,
            release: 0,
            GetChannelPitchOffsetSemitones(channel),
            GetChannelLfoSettings(channel),
            fixedPitch: false,
            fixedSampleRate: _fixedDirectSoundSampleRate,
            ownerRank: channel);

        if (voiceId < 0)
        {
            UpdateMixerVoiceRejected(channel, "DirectSound mixer channel limit exceeded.");
            return;
        }

        AddActivePreviewVoice(PreviewInputSource.Keyboard, channel, note, new ActivePreviewVoice(voiceId, IsPsg: false));
        UpdateMixerNoteOn(channel, note, velocity, new ResolvedPlayableVoice(
            voice,
            60,
            note,
            ForcedPan: null,
            DirectSoundFixed: false,
            ProgramId: voice.Index));
        SetActiveNote(note, channel, true);
    }

    public void PreviewWaveDataKeyboardNoteOff(int note)
    {
        PreviewNoteOffCore(note, channel: 0, PreviewInputSource.Keyboard);
    }

    public void PreviewNoteOn(int note, int velocity = 100, int channel = 0)
    {
        PreviewNoteOnCore(note, velocity, channel, PreviewInputSource.Midi, useMidiProgram: true);
    }

    public void PreviewNoteOff(int note, int channel = 0)
    {
        PreviewNoteOffCore(note, channel, PreviewInputSource.Midi);
    }

    private readonly record struct ActivePreviewVoice(int VoiceId, bool IsPsg);

    private void AddActivePreviewVoice(
        PreviewInputSource source,
        int channel,
        int note,
        ActivePreviewVoice voice)
    {
        var key = (source, channel, note);
        if (!_activePreviewVoices.TryGetValue(key, out List<ActivePreviewVoice>? voices))
        {
            voices = [];
            _activePreviewVoices[key] = voices;
        }

        voices.Add(voice);
    }

    private void PreviewNoteOnCore(
        int note,
        int velocity,
        int channel,
        PreviewInputSource source,
        bool useMidiProgram,
        bool replacePsgSourceChannelNotes = false,
        VoiceProjectInfo? overrideVoice = null)
    {
        note = Math.Clamp(note, 0, 127);
        channel = Math.Clamp(channel, 0, 15);
        velocity = Math.Clamp(velocity, 0, 127);
        if (velocity == 0)
        {
            PreviewNoteOffCore(note, channel, source);
            return;
        }

        if (!IsMixerChannelOutputEnabled(channel))
            return;

        ResolvedPlayableVoice? resolved = overrideVoice is null
            ? ResolveSelectedVoiceForNote(note, channel, useMidiProgram)
            : ResolvePlayableVoice(overrideVoice, note, isDrumEntry: false);
        if (resolved is null)
            return;
        if (useMidiProgram)
            resolved = ApplyXcmdVoiceOverrides(resolved, channel);

        bool isPsgVoice =
            IsPsgSquareVoice(resolved.Voice) ||
            IsPsgWaveMemoryVoice(resolved.Voice) ||
            IsPsgNoiseVoice(resolved.Voice);
        if (replacePsgSourceChannelNotes && isPsgVoice)
        {
            StopPreviewNotesForSourceChannel(source, channel, psgOnly: true);
            PreviewNoteOffCore(note, channel, source);
        }
        else if (source != PreviewInputSource.Sequence || isPsgVoice)
        {
            PreviewNoteOffCore(note, channel, source);
        }

        int pan = _channelPans[channel];
        int priority = useMidiProgram ? GetEffectiveChannelPriority(channel) : _channelPriorities[channel];
        int rhythmPan = resolved.ForcedPan is int forcedPan
            ? (Math.Clamp(forcedPan, 0, 127) - 64) << 1
            : 0;
        int voiceId;
        if (IsPsgSquareVoice(resolved.Voice))
        {
            voiceId = AudioEngine.NoteOnSquare(
                GetPsgSquareDutyIndex(resolved.Voice),
                resolved.PlaybackNote,
                velocity,
                _channelVolumes[channel],
                pan,
                priority,
                resolved.Voice.Attack,
                resolved.Voice.Decay,
                resolved.Voice.Sustain,
                resolved.Voice.Release,
                GetPsgSquareChannel(resolved.Voice),
                GetChannelPitchOffsetSemitones(channel),
                GetChannelLfoSettings(channel),
                ownerRank: channel,
                length: resolved.Voice.Length,
                sweep: resolved.Voice.PanOrSweep,
                rhythmPan: rhythmPan);
        }
        else if (IsPsgWaveMemoryVoice(resolved.Voice))
        {
            if (GetPsgWaveMemoryDataOffset(resolved.Voice) is not int waveMemoryOffset)
                return;
            if (!TryLoadWaveMemoryData(resolved.Voice, waveMemoryOffset, out byte[] waveRam))
                return;

            voiceId = AudioEngine.NoteOnWaveMemory(
                waveRam,
                resolved.BaseKey,
                resolved.PlaybackNote,
                velocity,
                _channelVolumes[channel],
                pan,
                priority,
                resolved.Voice.Attack,
                resolved.Voice.Decay,
                resolved.Voice.Sustain,
                resolved.Voice.Release,
                GetChannelPitchOffsetSemitones(channel),
                GetChannelLfoSettings(channel),
                ownerRank: channel,
                length: resolved.Voice.Length,
                rhythmPan: rhythmPan);
        }
        else if (IsPsgNoiseVoice(resolved.Voice))
        {
            voiceId = AudioEngine.NoteOnNoise(
                GetPsgNoiseControl(resolved.Voice),
                resolved.BaseKey,
                resolved.PlaybackNote,
                velocity,
                _channelVolumes[channel],
                pan,
                priority,
                resolved.Voice.Attack,
                resolved.Voice.Decay,
                resolved.Voice.Sustain,
                resolved.Voice.Release,
                GetChannelPitchOffsetSemitones(channel),
                GetChannelLfoSettings(channel),
                ownerRank: channel,
                length: resolved.Voice.Length,
                rhythmPan: rhythmPan);
        }
        else
        {
            if (resolved.Voice.Sample is not { } sample)
                return;
            if (!TryLoadSamplePcm(resolved.Voice, sample, out byte[] pcm, out SampleHeaderProjectInfo playbackHeader))
                return;

            voiceId = AudioEngine.NoteOn(
                pcm,
                playbackHeader,
                resolved.BaseKey,
                resolved.PlaybackNote,
                velocity,
                _channelVolumes[channel],
                pan,
                priority,
                resolved.Voice.Attack,
                resolved.Voice.Decay,
                resolved.Voice.Sustain,
                resolved.Voice.Release,
                GetChannelPitchOffsetSemitones(channel),
                GetChannelLfoSettings(channel),
                fixedPitch: resolved.DirectSoundFixed,
                fixedSampleRate: _fixedDirectSoundSampleRate,
                ownerRank: channel,
                rhythmPan: rhythmPan);
        }

        if (voiceId < 0)
        {
            UpdateMixerVoiceRejected(channel, "Hardware channel limit exceeded.");
            return;
        }

        AddActivePreviewVoice(source, channel, note, new ActivePreviewVoice(voiceId, isPsgVoice));
        UpdateMixerNoteOn(channel, note, velocity, resolved);
        SetActiveNote(note, channel, true);
    }

    private void PreviewNoteOffCore(int note, int channel, PreviewInputSource source)
    {
        note = Math.Clamp(note, 0, 127);
        channel = Math.Clamp(channel, 0, 15);
        var key = (source, channel, note);
        if (!_activePreviewVoices.TryGetValue(key, out List<ActivePreviewVoice>? voices) || voices.Count == 0)
            return;

        ActivePreviewVoice activeVoice = voices[0];
        voices.RemoveAt(0);
        if (voices.Count == 0)
            _activePreviewVoices.Remove(key);
        AudioEngine.NoteOff(activeVoice.VoiceId);
        UpdateMixerNoteOff(channel, note);
        SetActiveNote(note, channel, false);
    }

    private void StopPreviewNotesForSourceChannel(PreviewInputSource source, int channel, bool psgOnly = false)
    {
        channel = Math.Clamp(channel, 0, 15);
        foreach (var pair in _activePreviewVoices.Where(p =>
                     p.Key.Source == source &&
                     p.Key.Channel == channel &&
                     (!psgOnly || p.Value.Any(voice => voice.IsPsg))).ToArray())
        {
            List<ActivePreviewVoice> removed = psgOnly
                ? pair.Value.Where(voice => voice.IsPsg).ToList()
                : pair.Value.ToList();
            pair.Value.RemoveAll(voice => !psgOnly || voice.IsPsg);
            if (pair.Value.Count == 0)
                _activePreviewVoices.Remove(pair.Key);
            foreach (ActivePreviewVoice voice in removed)
            {
                _audioEngine?.NoteOff(voice.VoiceId);
                SetActiveNote(pair.Key.Note, pair.Key.Channel, false);
            }
            UpdateMixerNoteOff(pair.Key.Channel, pair.Key.Note);
        }
    }

    public void StopAllPreviewNotes()
    {
        _activePreviewVoices.Clear();
        _audioEngine?.AllNotesOff();
        Array.Clear(_activeNoteChannelMasks, 0, _activeNoteChannelMasks.Length);
        Array.Clear(_activeNoteCounts, 0, _activeNoteCounts.Length);
        ResetMixerStrips();
        OnPropertyChanged(nameof(ActiveNoteChannelMasks));
    }

    private void StopPreviewNotesForSource(PreviewInputSource source)
    {
        if (_activePreviewVoices.Count == 0)
            return;

        foreach (var pair in _activePreviewVoices.Where(p => p.Key.Source == source).ToArray())
        {
            _activePreviewVoices.Remove(pair.Key);
            foreach (ActivePreviewVoice voice in pair.Value)
            {
                _audioEngine?.NoteOff(voice.VoiceId);
                SetActiveNote(pair.Key.Note, pair.Key.Channel, false);
            }
            UpdateMixerNoteOff(pair.Key.Channel, pair.Key.Note);
        }
    }

    private void StopPreviewNotesForChannel(int channel)
    {
        foreach (var pair in _activePreviewVoices.Where(p => p.Key.Channel == channel).ToArray())
        {
            _activePreviewVoices.Remove(pair.Key);
            foreach (ActivePreviewVoice voice in pair.Value)
            {
                _audioEngine?.NoteOff(voice.VoiceId);
                SetActiveNote(pair.Key.Note, pair.Key.Channel, false);
            }
        }

        if ((uint)channel < (uint)MixerStrips.Count)
            MixerStrips[channel].ActiveNote = -1;
    }

    public void DisposePlayback()
    {
        StopAllPreviewNotes();
        _audioEngine?.Dispose();
        _audioEngine = null;
    }

    private void ApplySelectedAudioBufferSize()
    {
        if (SelectedAudioBufferSize is null || _audioEngine is null)
            return;

        if (!_audioEngine.TrySetBufferLatency(SelectedAudioBufferSize.LatencyMs, out var error))
            VoiceGroupStatus = $"Failed to set audio buffer size: {error}";
    }

    private void ApplyProjectSoundMode(Mp2kSoundModeProjectInfo soundMode)
    {
        _fixedDirectSoundSampleRate = soundMode.FixedSampleRate > 0
            ? soundMode.FixedSampleRate
            : AgbAudioEngine.DefaultMp2kFixedSampleRate;
        _directSoundMasterVolume = Math.Clamp(soundMode.Volume, 0, 15);
        _gbaDacConfig = Math.Clamp(soundMode.DacConfig, 8, 11);
        _soundModeReverbLevel = (soundMode.Reverb & 0x80) != 0
            ? soundMode.Reverb & 0x7F
            : 0;
        if (_audioEngine is not null)
        {
            _audioEngine.DirectSoundMasterVolume = _directSoundMasterVolume;
            _audioEngine.GbaDacConfig = _gbaDacConfig;
            _audioEngine.ConfigureReverb(_reverbEnabled ? _soundModeReverbLevel : 0, _fixedDirectSoundSampleRate);
        }
        SelectedDirectSoundChannelCount = Math.Clamp(
            soundMode.MaxChannels,
            0,
            12);
    }

    public void ApplyMidiControlChange(int channel, int controller, int value)
    {
        channel = Math.Clamp(channel, 0, 15);
        value = Math.Clamp(value, 0, 127);
        ApplyXcmdControlChange(channel, controller, value);
        MidiCcMapping mapping = _midiCcMapping;
        if (controller == mapping.Modulation)
        {
            _channelModDepths[channel] = value;
            ApplyActiveChannelLfo(channel);
        }
        else if (controller == mapping.Volume)
        {
            _channelVolumes[channel] = value;
            ApplyActiveChannelVolume(channel, value);
        }
        else if (controller == mapping.Pan)
        {
            _channelPans[channel] = value;
            ApplyActiveChannelPan(channel, value);
        }
        else if (controller == mapping.BendRangeLow)
        {
            _channelBendRanges[channel] = (Math.Clamp(_channelBendRangeHighBits[channel], 0, 1) << 7) | value;
            ApplyActiveChannelPitch(channel);
        }
        else if (controller == mapping.BendRangeHigh)
        {
            _channelBendRangeHighBits[channel] = value & 0x01;
            _channelBendRanges[channel] = (_channelBendRangeHighBits[channel] << 7) | (_channelBendRanges[channel] & 0x7F);
            ApplyActiveChannelPitch(channel);
        }
        else if (controller == mapping.LfoSpeed)
        {
            _channelModSpeeds[channel] = value;
            ApplyActiveChannelLfo(channel);
        }
        else if (controller == mapping.ModulationType)
        {
            _channelModTypes[channel] = value;
            ApplyActiveChannelLfo(channel);
        }
        else if (controller == mapping.LfoDelay)
        {
            _channelModDelays[channel] = value;
            ApplyActiveChannelLfo(channel);
        }
        else if (controller == mapping.Tune)
        {
            _channelTunes[channel] = value;
            ApplyActiveChannelPitch(channel);
        }
        else if (controller == mapping.Priority)
        {
            _channelPriorities[channel] = value;
        }

        UpdateMixerControlChange(channel, controller, value);
    }

    private void ApplyXcmdControlChange(int channel, int controller, int value)
    {
        MidiCcMapping mapping = _midiCcMapping;
        if (controller == mapping.Type)
            _channelXcmdTypes[channel] = value;
        else if (controller == mapping.Attack)
            _channelXcmdAttacks[channel] = value;
        else if (controller == mapping.Decay)
            _channelXcmdDecays[channel] = value;
        else if (controller == mapping.Sustain)
            _channelXcmdSustains[channel] = value;
        else if (controller == mapping.Release)
            _channelXcmdReleases[channel] = value;
        else if (controller == mapping.EchoVolume)
            _channelXcmdEchoVolumes[channel] = value;
        else if (controller == mapping.EchoLength)
            _channelXcmdEchoLengths[channel] = value;
        else if (controller == mapping.Length)
            _channelXcmdLengths[channel] = value;
        else if (controller == mapping.Sweep)
            _channelXcmdSweeps[channel] = value;
    }

    private ResolvedPlayableVoice ApplyXcmdVoiceOverrides(ResolvedPlayableVoice resolved, int channel)
    {
        VoiceProjectInfo source = resolved.Voice;
        var voice = new VoiceProjectInfo
        {
            Index = source.Index,
            Label = source.Label,
            Type = _channelXcmdTypes[channel] >= 0 ? _channelXcmdTypes[channel] : source.Type,
            TypeName = source.TypeName,
            Key = source.Key,
            Length = _channelXcmdLengths[channel] >= 0 ? _channelXcmdLengths[channel] : source.Length,
            PanOrSweep = _channelXcmdSweeps[channel] >= 0 ? _channelXcmdSweeps[channel] : source.PanOrSweep,
            DataPointer = source.DataPointer,
            DataOffset = source.DataOffset,
            DataFilePath = source.DataFilePath,
            Attack = _channelXcmdAttacks[channel] >= 0 ? _channelXcmdAttacks[channel] : source.Attack,
            Decay = _channelXcmdDecays[channel] >= 0 ? _channelXcmdDecays[channel] : source.Decay,
            Sustain = _channelXcmdSustains[channel] >= 0 ? _channelXcmdSustains[channel] : source.Sustain,
            Release = _channelXcmdReleases[channel] >= 0 ? _channelXcmdReleases[channel] : source.Release,
            Sample = source.Sample,
            PsgSquare = source.PsgSquare,
            PsgWaveMemory = source.PsgWaveMemory,
            PsgNoise = source.PsgNoise,
            DrumSet = source.DrumSet,
            KeySplit = source.KeySplit,
            RawEntryHex = source.RawEntryHex
        };

        bool fixedPitch = IsDirectSoundVoice(voice) && (voice.Type & 0x08) != 0;
        return resolved with
        {
            Voice = voice,
            DirectSoundFixed = fixedPitch,
            PlaybackNote = fixedPitch ? resolved.BaseKey : resolved.PlaybackNote
        };
    }

    public void ApplyMidiPitchBend(int channel, int value)
    {
        channel = Math.Clamp(channel, 0, 15);
        _channelPitchBends[channel] = Math.Clamp(value, 0, 16383);
        ApplyActiveChannelPitch(channel);
        UpdateMixerPitchBend(channel, value);
    }

    private void ApplyActiveChannelVolume(int channel, int volume)
    {
        if (_audioEngine is null)
            return;

        foreach (var pair in _activePreviewVoices)
        {
            if (pair.Key.Channel == channel)
            {
                foreach (ActivePreviewVoice voice in pair.Value)
                    _audioEngine.SetVoiceVolume(voice.VoiceId, volume);
            }
        }
    }

    private void ApplyActiveChannelPan(int channel, int pan)
    {
        if (_audioEngine is null)
            return;

        foreach (var pair in _activePreviewVoices)
        {
            if (pair.Key.Channel == channel)
            {
                foreach (ActivePreviewVoice voice in pair.Value)
                    _audioEngine.SetVoicePan(voice.VoiceId, pan);
            }
        }
    }

    private void ApplyActiveChannelPitch(int channel)
    {
        if (_audioEngine is null)
            return;

        double semitones = GetChannelPitchOffsetSemitones(channel);
        foreach (var pair in _activePreviewVoices)
        {
            if (pair.Key.Channel == channel)
            {
                foreach (ActivePreviewVoice voice in pair.Value)
                    _audioEngine.SetVoicePitchOffset(voice.VoiceId, semitones);
            }
        }
    }

    private void ApplyActiveChannelLfo(int channel)
    {
        if (_audioEngine is null)
            return;

        _audioEngine.SetTrackLfoSettings(channel, GetChannelLfoSettings(channel));
    }

    private double GetChannelPitchOffsetSemitones(int channel)
    {
        channel = Math.Clamp(channel, 0, 15);
        int pitchBend = _channelPitchBends[channel];
        double bend;
        if (pitchBend >= 8192)
            bend = (pitchBend - 8192) / 8191.0;
        else
            bend = (pitchBend - 8192) / 8192.0;

        double bendSemitones = bend * Math.Clamp(_channelBendRanges[channel], 0, 255);
        double tuneSemitones = _channelTunes[channel] >= 64
            ? (_channelTunes[channel] - 64) / 63.0
            : (_channelTunes[channel] - 64) / 64.0;
        return bendSemitones + tuneSemitones;
    }

    private AgbLfoSettings GetChannelLfoSettings(int channel)
    {
        channel = Math.Clamp(channel, 0, 15);
        return new AgbLfoSettings(
            Math.Clamp(_channelModDepths[channel], 0, 127),
            Math.Clamp(_channelModSpeeds[channel], 0, 255),
            Math.Clamp(_channelModTypes[channel], 0, 2),
            Math.Clamp(_channelModDelays[channel], 0, 255));
    }

    private int GetEffectiveChannelPriority(int channel)
    {
        channel = Math.Clamp(channel, 0, 15);
        return Math.Min(255, Math.Clamp(_currentPlayerPriority, 0, 255) + _channelPriorities[channel]);
    }

    public void ApplyMidiProgramChange(int channel, int program)
    {
        channel = Math.Clamp(channel, 0, 15);
        program = Math.Clamp(program, 0, 127);
        _channelPrograms[channel] = program;
        _channelHasProgram[channel] = true;
        UpdateMixerProgramChange(channel, program);

        // MIDI ProgramChange is channel state. It must not change the UI table selection used by the piano keyboard.
    }

    private ResolvedPlayableVoice? ResolveSelectedVoiceForNote(int note, int channel, bool useMidiProgram)
    {
        if (useMidiProgram)
        {
            if (!_channelHasProgram[channel])
                return null;

            VoiceProjectInfo? channelVoice = SelectedVoiceGroup?.Voices.FirstOrDefault(v => v.Index == _channelPrograms[channel]);
            return channelVoice is null
                ? null
                : ResolvePlayableVoice(channelVoice, note, isDrumEntry: false, programId: _channelPrograms[channel]);
        }

        VoiceProjectInfo? selected = SelectedVoice?.Source;
        if (selected is not null)
            return ResolvePlayableVoice(selected, note, isDrumEntry: false);

        return SelectedVoiceGroup?.Voices.Select(v => ResolvePlayableVoice(v, note, isDrumEntry: false)).FirstOrDefault(v => v is not null);
    }

    private static ResolvedPlayableVoice? ResolvePlayableVoice(
        VoiceProjectInfo voice,
        int note,
        bool isDrumEntry,
        int? programId = null)
    {
        int rootProgramId = programId ?? voice.Index;

        if (voice.Sample is not null || IsPsgSquareVoice(voice) || IsPsgWaveMemoryVoice(voice) || IsPsgNoiseVoice(voice))
        {
            bool fixedPitch = IsDirectSoundVoice(voice) && (voice.Type & 0x08) != 0;
            int baseKey = isDrumEntry ? 60 : NormalizeBaseKey(voice.Key);
            int playbackNote = note;

            if (isDrumEntry)
                playbackNote = fixedPitch
                    ? baseKey
                    : IsPsgSquareVoice(voice) || IsPsgWaveMemoryVoice(voice) || IsPsgNoiseVoice(voice)
                        ? note
                        : Math.Clamp(voice.Key, 0, 127);
            else if (fixedPitch)
                playbackNote = baseKey;

            int? forcedPan = isDrumEntry && (voice.PanOrSweep & 0x80) != 0
                ? voice.PanOrSweep & 0x7F
                : null;

            return new ResolvedPlayableVoice(voice, baseKey, playbackNote, forcedPan, fixedPitch, rootProgramId);
        }

        if (voice.DrumSet is not null && note >= 0 && note < voice.DrumSet.Entries.Count)
            return ResolvePlayableVoice(voice.DrumSet.Entries[note], note, isDrumEntry: true, rootProgramId);

        if (voice.KeySplit is not null)
        {
            byte[] keyMap = DecodeHex(voice.KeySplit.KeyMapHex);
            int regionIndex = GetKeySplitMappedRegion(keyMap, note, voice.KeySplit.Regions.Count);

            if (regionIndex >= 0 && regionIndex < voice.KeySplit.Regions.Count)
                return ResolvePlayableVoice(voice.KeySplit.Regions[regionIndex], note, isDrumEntry: false, rootProgramId);

            return keyMap.Length == 0
                ? voice.KeySplit.Regions
                    .Select(v => ResolvePlayableVoice(v, note, isDrumEntry: false, rootProgramId))
                    .FirstOrDefault(v => v is not null)
                : null;
        }

        return null;
    }

    private static bool IsPsgSquareVoice(VoiceProjectInfo voice)
    {
        return (voice.Type & 0x07) is 0x01 or 0x02;
    }

    private static bool IsPsgNoiseVoice(VoiceProjectInfo voice)
    {
        return (voice.Type & 0x07) == 0x04;
    }

    private static bool IsPsgWaveMemoryVoice(VoiceProjectInfo voice)
    {
        return (voice.Type & 0x07) == 0x03;
    }

    private static bool IsDirectSoundVoice(VoiceProjectInfo voice)
    {
        return (voice.Type & 0x07) == 0 && voice.Sample is not null;
    }

    private static int GetPsgSquareDutyIndex(VoiceProjectInfo voice)
    {
        if (voice.PsgSquare is not null)
            return Math.Clamp(voice.PsgSquare.DutyIndex, 0, 3);

        byte[] raw = DecodeHex(voice.RawEntryHex);
        if (raw.Length > 4)
            return raw[4] & 0x03;

        if (TryParseHex(voice.DataPointer, out uint data))
            return (int)(data & 0x03);

        return 2;
    }

    private static int GetPsgSquareChannel(VoiceProjectInfo voice)
    {
        return (voice.Type & 0x07) == 0x02 ? 2 : 1;
    }

    private static int GetPsgNoiseControl(VoiceProjectInfo voice)
    {
        if (voice.PsgNoise is not null)
            return Math.Clamp(voice.PsgNoise.Control, 0, 0xFF);

        byte[] raw = DecodeHex(voice.RawEntryHex);
        if (raw.Length > 4)
            return raw[4];

        if (TryParseHex(voice.DataPointer, out uint data))
            return (int)(data & 0xFF);

        return 0;
    }

    private static int? GetPsgWaveMemoryDataOffset(VoiceProjectInfo voice)
    {
        if (voice.PsgWaveMemory is not null)
            return voice.PsgWaveMemory.DataOffset;

        return voice.DataOffset;
    }

    private static VoiceProjectInfo CreateWaveMemoryPreviewVoice(WaveMemoryRow waveMemory)
    {
        return new VoiceProjectInfo
        {
            Index = waveMemory.Id,
            Label = waveMemory.FileDisplay,
            Type = 0x03,
            TypeName = "Wave Memory",
            Key = 60,
            Length = 0,
            PanOrSweep = 0,
            DataPointer = "0x00000000",
            DataOffset = 0,
            DataFilePath = waveMemory.FilePath,
            Attack = 0,
            Decay = 255,
            Sustain = 15,
            Release = 0,
            PsgWaveMemory = new PsgWaveMemoryProjectInfo
            {
                FilePath = waveMemory.FilePath,
                DataFormat = waveMemory.DataFormat
            }
        };
    }

    private static VoiceProjectInfo CreateWaveDataPreviewVoice(WaveDataRow waveData, SampleHeaderProjectInfo header)
    {
        return new VoiceProjectInfo
        {
            Index = waveData.Id,
            Label = waveData.FileDisplay,
            Type = 0x00,
            TypeName = "DirectSound",
            Key = 60,
            Length = 0,
            PanOrSweep = 0,
            DataPointer = "0x00000000",
            DataOffset = header.DataOffset,
            DataFilePath = waveData.FilePath,
            Attack = 255,
            Decay = 255,
            Sustain = 255,
            Release = 0,
            Sample = header
        };
    }

    private bool TryLoadSamplePcm(
        VoiceProjectInfo voice,
        SampleHeaderProjectInfo sample,
        out byte[] pcm,
        out SampleHeaderProjectInfo playbackHeader)
    {
        playbackHeader = sample;
        pcm = [];

        string? assetPath = ResolveProjectAssetPath(sample.FilePath, voice.DataFilePath);
        if (assetPath is not null)
        {
            try
            {
                var document = JsonSerializer.Deserialize<AgbWaveDataAssetDocument>(
                    File.ReadAllText(assetPath),
                    AssetJsonOptions);
                byte[] assetPcm = DecodeHex(document?.DataHex ?? string.Empty);
                if (document?.Header is not null && assetPcm.Length > 0)
                {
                    playbackHeader = document.Header;
                    playbackHeader.FilePath = ToCurrentProjectRelativePath(assetPath);
                    if (playbackHeader.Size == 0 || playbackHeader.Size > int.MaxValue)
                        playbackHeader.Size = (uint)assetPcm.Length;
                    pcm = assetPcm;
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private bool TryLoadWaveMemoryData(VoiceProjectInfo voice, int waveMemoryOffset, out byte[] waveRam)
    {
        waveRam = [];
        string? assetPath = ResolveProjectAssetPath(voice.PsgWaveMemory?.FilePath, voice.DataFilePath);
        foreach (string? path in new[] { voice.PsgWaveMemory?.FilePath, voice.DataFilePath, assetPath })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var row = WaveMemoryRows.FirstOrDefault(waveMemory => AssetPathMatches(waveMemory.FilePath, path));
            if (row is not null)
            {
                waveRam = row.DataBytes;
                return waveRam.Length >= 16;
            }
        }

        if (assetPath is not null)
        {
            try
            {
                byte[] data = File.ReadAllBytes(assetPath);
                if (data.Length >= 16)
                {
                    waveRam = data.Take(16).ToArray();
                    return true;
                }
            }
            catch (IOException)
            {
            }
        }

        return false;
    }

    private string? ResolveProjectAssetPath(params string?[] relativePaths)
    {
        foreach (string? relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath) && File.Exists(normalizedPath))
                return normalizedPath;
            if (string.IsNullOrWhiteSpace(_currentProjectPath))
                continue;

            string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
            string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedPath));
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private string ToCurrentProjectRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectPath))
            return path;

        string projectDirectory = Path.GetDirectoryName(_currentProjectPath) ?? ".";
        return Path.GetRelativePath(projectDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static int NormalizeBaseKey(int key)
    {
        return key is > 0 and <= 127 ? key : 60;
    }

    private static byte[] DecodeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0)
            return [];

        try
        {
            return Convert.FromHexString(hex);
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseHex(string text, out uint value)
    {
        string normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? text[2..]
            : text;
        return uint.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private void SetActiveNote(int note, int channel, bool active)
    {
        ushort mask = (ushort)(1 << channel);
        if (active)
        {
            _activeNoteCounts[note, channel]++;
            _activeNoteChannelMasks[note] |= mask;
        }
        else
        {
            if (_activeNoteCounts[note, channel] > 0)
                _activeNoteCounts[note, channel]--;
            if (_activeNoteCounts[note, channel] == 0)
                _activeNoteChannelMasks[note] &= (ushort)~mask;
        }

        OnPropertyChanged(nameof(ActiveNoteChannelMasks));
    }

    private static readonly JsonSerializerOptions AssetJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class AgbWaveDataAssetDocument
    {
        public SampleHeaderProjectInfo Header { get; set; } = new();
        public string DataHex { get; set; } = string.Empty;
    }
}

internal enum PreviewInputSource
{
    Keyboard,
    Midi,
    Sequence
}

public sealed class AudioBufferSizeOption
{
    public AudioBufferSizeOption(int latencyMs)
    {
        LatencyMs = latencyMs;
        Name = $"{latencyMs} ms";
    }

    public int LatencyMs { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

internal sealed record ResolvedPlayableVoice(
    VoiceProjectInfo Voice,
    int BaseKey,
    int PlaybackNote,
    int? ForcedPan,
    bool DirectSoundFixed,
    int ProgramId);
