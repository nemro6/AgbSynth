using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AgbSynth.App.Audio;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly DispatcherTimer _mixerTimer;

    public ObservableCollection<AgbMixerStrip> MixerStrips { get; } = new();
    public IReadOnlyList<AgbMixerStrip> MixerStripsByChannel { get; }
    public IReadOnlyList<IBrush> MixerKeyboardChannelBrushes { get; } =
    [
        new SolidColorBrush(Color.Parse("#8FB7FF")),
        new SolidColorBrush(Color.Parse("#D88983")),
        new SolidColorBrush(Color.Parse("#D8A06A")),
        new SolidColorBrush(Color.Parse("#7FA6D8")),
        new SolidColorBrush(Color.Parse("#6D8FC5")),
        new SolidColorBrush(Color.Parse("#B9906F")),
        new SolidColorBrush(Color.Parse("#9F765A")),
        new SolidColorBrush(Color.Parse("#CE8D9A")),
        new SolidColorBrush(Color.Parse("#CDAA73")),
        new SolidColorBrush(Color.Parse("#8EE6C1")),
        new SolidColorBrush(Color.Parse("#C2CEF0")),
        new SolidColorBrush(Color.Parse("#EDC3CB")),
        new SolidColorBrush(Color.Parse("#E8D0A6")),
        new SolidColorBrush(Color.Parse("#B8D894")),
        new SolidColorBrush(Color.Parse("#D6B7E6")),
        new SolidColorBrush(Color.Parse("#A9D6D0"))
    ];

    public MainWindowViewModel()
    {
        for (int channel = 0; channel < 16; channel++)
            MixerStrips.Add(new AgbMixerStrip(channel, OnMixerOutputEnabledChanged));
        MixerStripsByChannel = MixerStrips.OrderBy(strip => strip.Channel).ToArray();

        _mixerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _mixerTimer.Tick += (_, _) => TickMixer();
        _mixerTimer.Start();
        InitializeProjectEditing();
    }

    private void TickMixer()
    {
        ApplySequencePlaybackSnapshot();
        foreach (var strip in MixerStrips)
        {
            double actualLevel = 0;
            double lfoWave = 0;
            bool hasLfoWave = false;
            if (_audioEngine is not null &&
                _audioEngine.TryGetTrackMetrics(strip.Channel, out float trackLevel, out float trackLfoWave))
            {
                actualLevel = trackLevel;
                lfoWave = trackLfoWave;
                hasLfoWave = Math.Abs(trackLfoWave) > float.Epsilon;
            }

            strip.Tick(
                actualLevel > 0 ? actualLevel : null,
                hasLfoWave ? lfoWave : 0,
                _meterDecayMilliseconds,
                _peakDecayMilliseconds,
                _mixerTimer.Interval.TotalMilliseconds);
        }
    }

    private void ApplySequencePlaybackSnapshot()
    {
        if (_sequenceAudioRuntime is not { } runtime || _midiPlaybackSession is not { } session)
            return;

        PlaybackProgress = Math.Clamp(
            session.LastEventSourceTick / (double)Math.Max(1, _sequencePlaybackLastTick) * 100.0,
            0,
            100);

        Mp2kSequencePlaybackSnapshot snapshot = runtime.Snapshot;
        if (snapshot.Revision == _lastSequenceSnapshotRevision)
            return;
        _lastSequenceSnapshotRevision = snapshot.Revision;

        foreach (Mp2kSequenceChannelSnapshot channel in snapshot.Channels)
        {
            if ((uint)channel.Channel >= (uint)MixerStrips.Count)
                continue;

            AgbMixerStrip strip = MixerStrips[channel.Channel];
            strip.ActiveNote = channel.ActiveNote;
            strip.Velocity = channel.Velocity;
            strip.ProgramId = channel.Program;
            strip.InstrumentType = channel.InstrumentLabel;
            strip.Volume = channel.Volume;
            strip.Pan = channel.Pan - 64;
            strip.BendRange = channel.BendRange;
            strip.PitchBend = channel.PitchBend;
            strip.Modulation = channel.Modulation;
            strip.ModSpeed = channel.ModSpeed;
            strip.ModType = channel.ModType;
            strip.ModDelay = channel.ModDelay;
            strip.Tune = channel.Tune;
            strip.Priority = channel.Priority;
            strip.IssueLog = channel.IssueLog;
            if (channel.VoiceRejected)
                strip.AlertActive = true;
        }

        OnPropertyChanged(nameof(ActiveNoteChannelMasks));
    }

    private bool IsMixerChannelOutputEnabled(int channel)
    {
        return (uint)channel >= (uint)MixerStrips.Count || MixerStrips[channel].OutputEnabled;
    }

    private void OnMixerOutputEnabledChanged(int channel, bool enabled)
    {
        _sequenceAudioRuntime?.SetChannelEnabled(channel, enabled);
        if (enabled)
            return;

        _audioEngine?.StopVoicesForOwnerRank(channel);
        StopPreviewNotesForChannel(channel);
    }

    public void ToggleMixerSolo(int channel)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        bool onlyThisEnabled = MixerStrips[channel].OutputEnabled &&
            MixerStrips.Where(s => s.Channel != channel).All(s => !s.OutputEnabled);
        if (onlyThisEnabled)
        {
            foreach (var strip in MixerStrips)
                strip.OutputEnabled = true;
            return;
        }

        foreach (var strip in MixerStrips)
            strip.OutputEnabled = strip.Channel == channel;
    }

    public void DismissMixerAlert(AgbMixerStrip strip)
    {
        strip.AlertActive = false;
    }

    private void UpdateMixerNoteOn(int channel, int note, int velocity, ResolvedPlayableVoice resolved)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        var strip = MixerStrips[channel];
        strip.ActiveNote = note;
        strip.Velocity = velocity;
        strip.ProgramId = resolved.ProgramId;
        strip.InstrumentType = ResolveVoiceLabel(resolved.Voice);
        strip.IssueLog = "-";
    }

    private void UpdateMixerVoiceRejected(int channel, string reason)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        var strip = MixerStrips[channel];
        strip.IssueLog = reason;
        strip.AlertActive = true;
    }

    private void UpdateMixerNoteOff(int channel, int note)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        bool anyActive = _activePreviewVoices.Keys.Any(k => k.Channel == channel && k.Note == note);
        if (!anyActive && MixerStrips[channel].ActiveNote == note)
            MixerStrips[channel].ActiveNote = -1;
    }

    private void UpdateMixerControlChange(int channel, int controller, int value)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        var strip = MixerStrips[channel];
        MidiCcMapping mapping = _midiCcMapping;
        if (controller == mapping.Modulation)
            strip.Modulation = value;
        else if (controller == mapping.Volume)
            strip.Volume = value;
        else if (controller == mapping.Pan)
            strip.Pan = value - 64;
        else if (controller == mapping.BendRangeLow || controller == mapping.BendRangeHigh)
            strip.BendRange = _channelBendRanges[channel];
        else if (controller == mapping.LfoSpeed)
            strip.ModSpeed = value;
        else if (controller == mapping.ModulationType)
            strip.ModType = value;
        else if (controller == mapping.LfoDelay)
            strip.ModDelay = value;
        else if (controller == mapping.Tune)
            strip.Tune = value;
        else if (controller == mapping.Priority)
            strip.Priority = value;
    }

    private void UpdateMixerProgramChange(int channel, int program)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        var strip = MixerStrips[channel];
        strip.ProgramId = program;
        strip.InstrumentType = ResolveProgramTypeName(program);
    }

    private void UpdateMixerPitchBend(int channel, int value)
    {
        if ((uint)channel >= (uint)MixerStrips.Count)
            return;

        int clamped = Math.Clamp(value, 0, 16383);
        int signed = clamped >= 8192
            ? (int)Math.Round((clamped - 8192) * (63.0 / 8191.0))
            : -(int)Math.Round((8192 - clamped) * (64.0 / 8192.0));
        MixerStrips[channel].PitchBend = signed;
    }

    private string ResolveProgramTypeName(int program)
    {
        VoiceProjectInfo? voice = SelectedVoiceGroup?.Voices.FirstOrDefault(v => v.Index == program);
        return voice is null ? "-" : ResolveVoiceLabel(voice);
    }

    private static string ResolveVoiceLabel(VoiceProjectInfo voice)
    {
        if (!string.IsNullOrWhiteSpace(voice.Label))
            return voice.Label;
        if (!string.IsNullOrWhiteSpace(voice.TypeName))
            return voice.TypeName;
        return "-";
    }

    private void ResetMixerStrips()
    {
        foreach (var strip in MixerStrips)
            strip.Reset();
    }
}

public sealed class AgbMixerStrip : INotifyPropertyChanged
{
    private static readonly IBrush MuteEnabledBrush = new SolidColorBrush(Color.Parse("#675757"));
    private static readonly IBrush MuteDisabledBrush = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IBrush VolumeFillBrush = new SolidColorBrush(Color.Parse("#D88983"));
    private static readonly IBrush VelocityFillBrush = new SolidColorBrush(Color.Parse("#D8A06A"));
    private static readonly IBrush ModSpeedFillBrush = new SolidColorBrush(Color.Parse("#7FA6D8"));
    private static readonly IBrush ModDelayFillBrush = new SolidColorBrush(Color.Parse("#6D8FC5"));
    private static readonly IBrush BendRangeFillBrush = new SolidColorBrush(Color.Parse("#B9906F"));
    private static readonly IBrush PitchBendFillBrush = new SolidColorBrush(Color.Parse("#9F765A"));
    private static readonly IBrush ModDefaultFillBrush = new SolidColorBrush(Color.Parse("#8EA3D5"));
    private static readonly IBrush ModDefaultPulseBrush = new SolidColorBrush(Color.Parse("#C2CEF0"));
    private static readonly IBrush VolumeModFillBrush = new SolidColorBrush(Color.Parse("#CE8D9A"));
    private static readonly IBrush VolumeModPulseBrush = new SolidColorBrush(Color.Parse("#EDC3CB"));
    private static readonly IBrush PanModFillBrush = new SolidColorBrush(Color.Parse("#CDAA73"));
    private static readonly IBrush PanModPulseBrush = new SolidColorBrush(Color.Parse("#E8D0A6"));

    private readonly Action<int, bool> _onOutputEnabledChanged;
    private bool _outputEnabled = true;
    private int _activeNote = -1;
    private int _programId;
    private int _velocity;
    private double _level;
    private int _volume = 100;
    private int _pan;
    private int _modulation;
    private int _modSpeed = 22;
    private int _modType;
    private int _modDelay;
    private int _bendRange = 2;
    private int _pitchBend;
    private int _tune = 64;
    private int _priority;
    private string _instrumentType = "-";
    private string _issueLog = "-";
    private double _peakLevel;
    private double _modSwingNegWidth;
    private double _modSwingPosWidth;
    private bool _alertActive;

    public AgbMixerStrip(int channel, Action<int, bool> onOutputEnabledChanged)
    {
        Channel = channel;
        _onOutputEnabledChanged = onOutputEnabledChanged;
    }

    public int Channel { get; }
    public string ChannelLabel => $"Tr{Channel:X1}h";
    public string ChannelText => Channel.ToString("X1");
    public string TrackNumberText => (Channel + 1).ToString("D2");

    public bool OutputEnabled
    {
        get => _outputEnabled;
        set
        {
            if (_outputEnabled == value)
                return;

            _outputEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputText));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(OutputBrush));
            OnPropertyChanged(nameof(MuteBrush));
            _onOutputEnabledChanged(Channel, value);
        }
    }

    public string OutputText => OutputEnabled ? "ON" : "OFF";
    public bool IsMuted
    {
        get => !OutputEnabled;
        set => OutputEnabled = !value;
    }
    public IBrush OutputBrush => OutputEnabled ? Brushes.DarkCyan : Brushes.DimGray;
    public IBrush MuteBrush => OutputEnabled ? MuteEnabledBrush : MuteDisabledBrush;
    public IBrush VolumeMeterBrush => VolumeFillBrush;
    public IBrush VelocityMeterBrush => VelocityFillBrush;
    public IBrush ModSpeedMeterBrush => ModSpeedFillBrush;
    public IBrush ModDelayMeterBrush => ModDelayFillBrush;
    public IBrush BendRangeMeterBrush => BendRangeFillBrush;
    public IBrush PitchBendMeterBrush => PitchBendFillBrush;

    public int ActiveNote
    {
        get => _activeNote;
        set
        {
            int normalized = Math.Clamp(value, -1, 127);
            if (_activeNote == normalized)
                return;

            _activeNote = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveNoteText));
        }
    }

    public string ActiveNoteText => ActiveNote >= 0 ? ActiveNote.ToString("X2") : "--";

    public int ProgramId
    {
        get => _programId;
        set
        {
            if (SetField(ref _programId, Math.Clamp(value, 0, 127)))
                OnPropertyChanged(nameof(ProgramDisplay));
        }
    }

    public string ProgramDisplay => $"{ProgramId} (0x{ProgramId:X2})";
    public string InstrumentLabel => InstrumentType;

    public int Velocity
    {
        get => _velocity;
        set
        {
            if (SetField(ref _velocity, Math.Clamp(value, 0, 127)))
            {
                OnPropertyChanged(nameof(VelocityRatio));
                OnPropertyChanged(nameof(VelocityWidth));
            }
        }
    }

    public double VelocityRatio => Velocity / 127.0;
    public double VelocityWidth => Velocity / 127.0 * 96.0;

    public double Level
    {
        get => _level;
        set
        {
            double normalized = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_level - normalized) <= 0.000000001)
                return;

            _level = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LevelWidth));
            OnPropertyChanged(nameof(MacLevelWidth));
        }
    }

    public double LevelWidth => ToMeterScale(Level) * 104.0;
    public double MacLevelWidth => ToMeterScale(Level) * 256.0;
    public double PeakLevel
    {
        get => _peakLevel;
        private set
        {
            double normalized = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_peakLevel - normalized) <= 0.000000001)
                return;

            _peakLevel = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacPeakLeft));
            OnPropertyChanged(nameof(HasPeakLevel));
        }
    }

    public double MacPeakLeft => Math.Clamp(ToMeterScale(PeakLevel) * 256.0 - 1.0, 0.0, 255.0);
    public bool HasPeakLevel => ToMeterScale(PeakLevel) > 0;

    private static double ToMeterScale(double amplitude)
    {
        if (amplitude <= 0.001)
            return 0;

        const double minimumDb = -60.0;
        double db = 20.0 * Math.Log10(Math.Clamp(amplitude, 0.000001, 1.0));
        return Math.Clamp((db - minimumDb) / -minimumDb, 0.0, 1.0);
    }

    private static double FromMeterScale(double scale)
    {
        scale = Math.Clamp(scale, 0.0, 1.0);
        if (scale <= 0)
            return 0;

        const double minimumDb = -60.0;
        double db = minimumDb + scale * -minimumDb;
        return Math.Pow(10.0, db / 20.0);
    }

    public int Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, Math.Clamp(value, 0, 127)))
            {
                OnPropertyChanged(nameof(VolumeWidth));
                OnPropertyChanged(nameof(MacVolumeWidth));
            }
        }
    }

    public double VolumeWidth => Volume / 127.0 * 44.0;
    public double MacVolumeWidth => Volume / 127.0 * 96.0;

    public int Pan
    {
        get => _pan;
        set
        {
            if (SetField(ref _pan, Math.Clamp(value, -64, 63)))
            {
                OnPropertyChanged(nameof(PanLeftWidth));
                OnPropertyChanged(nameof(PanRightWidth));
                OnPropertyChanged(nameof(MacPanLeftWidth));
                OnPropertyChanged(nameof(MacPanRightWidth));
                OnPropertyChanged(nameof(PanAngle));
            }
        }
    }

    public double PanLeftWidth => Pan < 0 ? -Pan / 64.0 * 22.0 : 0.0;
    public double PanRightWidth => Pan > 0 ? Pan / 63.0 * 22.0 : 0.0;
    public double MacPanLeftWidth => Pan < 0 ? -Pan / 64.0 * 48.0 : 0.0;
    public double MacPanRightWidth => Pan > 0 ? Pan / 63.0 * 48.0 : 0.0;
    public double PanAngle => Pan / 64.0 * 135.0;

    public int Modulation
    {
        get => _modulation;
        set
        {
            if (SetField(ref _modulation, Math.Clamp(value, 0, 127)))
            {
                OnPropertyChanged(nameof(ModulationWidth));
                OnPropertyChanged(nameof(MacModulationWidth));
                UpdateModSwing();
            }
        }
    }

    public double ModulationWidth => Modulation / 127.0 * 44.0;
    public double MacModulationWidth => Modulation / 127.0 * 96.0;
    public string ModTypeLabel => ModType switch
    {
        1 => "Volume",
        2 => "Pan",
        _ => "Pitch"
    };
    public IBrush ModulationMeterBrush => ModType switch
    {
        1 => VolumeModFillBrush,
        2 => PanModFillBrush,
        _ => ModDefaultFillBrush
    };
    public IBrush ModulationPulseBrush => ModType switch
    {
        1 => VolumeModPulseBrush,
        2 => PanModPulseBrush,
        _ => ModDefaultPulseBrush
    };
    public double ModSwingNegWidth
    {
        get => _modSwingNegWidth;
        private set => SetField(ref _modSwingNegWidth, Math.Clamp(value, 0.0, 48.0));
    }

    public double ModSwingPosWidth
    {
        get => _modSwingPosWidth;
        private set => SetField(ref _modSwingPosWidth, Math.Clamp(value, 0.0, 48.0));
    }

    public int ModSpeed
    {
        get => _modSpeed;
        set
        {
            if (SetField(ref _modSpeed, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(ModSpeedWidth));
        }
    }
    public double ModSpeedWidth => ModSpeed / 255.0 * 96.0;

    public int ModType
    {
        get => _modType;
        set
        {
            if (SetField(ref _modType, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(ModTypeLabel));
                OnPropertyChanged(nameof(ModulationMeterBrush));
                OnPropertyChanged(nameof(ModulationPulseBrush));
            }
        }
    }

    public int ModDelay
    {
        get => _modDelay;
        set
        {
            if (SetField(ref _modDelay, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(ModDelayWidth));
        }
    }
    public double ModDelayWidth => ModDelay / 255.0 * 96.0;

    public int BendRange
    {
        get => _bendRange;
        set
        {
            if (SetField(ref _bendRange, Math.Clamp(value, 0, 255)))
            {
                OnPropertyChanged(nameof(BendRangeWidth));
                OnPropertyChanged(nameof(MacBendRangeWidth));
            }
        }
    }

    public double BendRangeWidth => BendRange / 255.0 * 44.0;
    public double MacBendRangeWidth => BendRange / 255.0 * 96.0;

    public int PitchBend
    {
        get => _pitchBend;
        set
        {
            if (SetField(ref _pitchBend, Math.Clamp(value, -64, 63)))
            {
                OnPropertyChanged(nameof(PitchBendLeftWidth));
                OnPropertyChanged(nameof(PitchBendRightWidth));
                OnPropertyChanged(nameof(MacPitchBendLeftWidth));
                OnPropertyChanged(nameof(MacPitchBendRightWidth));
            }
        }
    }

    public double PitchBendLeftWidth => PitchBend < 0 ? -PitchBend / 64.0 * 22.0 : 0.0;
    public double PitchBendRightWidth => PitchBend > 0 ? PitchBend / 63.0 * 22.0 : 0.0;
    public double MacPitchBendLeftWidth => PitchBend < 0 ? -PitchBend / 64.0 * 48.0 : 0.0;
    public double MacPitchBendRightWidth => PitchBend > 0 ? PitchBend / 63.0 * 48.0 : 0.0;

    public int Tune
    {
        get => _tune;
        set => SetField(ref _tune, Math.Clamp(value, 0, 127));
    }

    public int Priority
    {
        get => _priority;
        set => SetField(ref _priority, Math.Clamp(value, 0, 127));
    }

    public string InstrumentType
    {
        get => _instrumentType;
        set
        {
            if (SetField(ref _instrumentType, string.IsNullOrWhiteSpace(value) ? "-" : value))
            {
                OnPropertyChanged(nameof(ProgramDisplay));
                OnPropertyChanged(nameof(InstrumentLabel));
            }
        }
    }

    public string IssueLog
    {
        get => _issueLog;
        set => SetField(ref _issueLog, string.IsNullOrWhiteSpace(value) ? "-" : value);
    }

    public bool AlertActive
    {
        get => _alertActive;
        set
        {
            if (SetField(ref _alertActive, value))
                OnPropertyChanged(nameof(AlertOpacity));
        }
    }

    public double AlertOpacity => AlertActive ? 1.0 : 0.28;

    public void Tick(
        double? actualLevel = null,
        double lfoWave = 0,
        double decayMilliseconds = 100,
        double peakDecayMilliseconds = 1000,
        double elapsedMilliseconds = 33)
    {
        double target = actualLevel is double level
            ? Math.Clamp(level * 1.2, 0.0, 1.0)
            : 0.0;
        double meterDrop = decayMilliseconds <= 0
            ? 1.0
            : Math.Clamp(elapsedMilliseconds / decayMilliseconds, 0.0, 1.0);

        double currentScale = ToMeterScale(Level);
        double targetScale = ToMeterScale(target);
        double nextScale = targetScale >= currentScale
            ? targetScale
            : Math.Max(targetScale, currentScale - meterDrop);
        Level = FromMeterScale(nextScale);

        double peakDrop = peakDecayMilliseconds <= 0
            ? 1.0
            : Math.Clamp(elapsedMilliseconds / peakDecayMilliseconds, 0.0, 1.0);
        double peakScale = Math.Max(nextScale, ToMeterScale(PeakLevel) - peakDrop);
        PeakLevel = FromMeterScale(peakScale);

        SetModSwingFromWave(lfoWave);
    }

    public void SetModSwingFromWave(double wave)
    {
        if (Modulation <= 0)
        {
            ModSwingNegWidth = 0;
            ModSwingPosWidth = 0;
            return;
        }

        double clamped = Math.Clamp(wave, -1.0, 1.0);
        ModSwingNegWidth = clamped < 0 ? -clamped * 48.0 : 0;
        ModSwingPosWidth = clamped > 0 ? clamped * 48.0 : 0;
    }

    private void UpdateModSwing()
    {
        SetModSwingFromWave(0);
    }

    public void Reset()
    {
        ActiveNote = -1;
        ProgramId = 0;
        Velocity = 0;
        Level = 0;
        PeakLevel = 0;
        Volume = 100;
        Pan = 0;
        Modulation = 0;
        ModSpeed = 22;
        ModType = 0;
        ModDelay = 0;
        BendRange = 2;
        PitchBend = 0;
        Tune = 64;
        Priority = 0;
        InstrumentType = "-";
        IssueLog = "-";
        AlertActive = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
