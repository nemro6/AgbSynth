using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using AgbSynth.App.Audio;
using AgbSynth.App.MP2K;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Partial-view-model constructors assign several settings before the window
    // loads the persisted file. Suppress writes until that initial load completes.
    private bool _isLoadingUserSettings = true;
    private int _selectedAudioOutputDeviceNumber = -1;
    private AudioOutputDeviceOption? _selectedAudioOutputDevice;
    private RenderFrameRateOption? _selectedRenderFrameRate;
    private PlaybackSampleRateOption? _selectedPlaybackSampleRate;
    private ThemeColorOption? _selectedThemeColor;
    private OutputChannelModeOption? _selectedOutputChannelMode;
    private bool _linearInterpolationEnabled = true;
    private bool _reverbEnabled = true;
    private int _outputSampleRate = AgbAudioEngine.GbaOutputSampleRate;
    private double _emulationVolume = 255;
    private double _meterDecayMilliseconds = 100;
    private double _peakDecayMilliseconds = 1000;
    private MidiCcMapping _midiCcMapping = MidiCcMapping.Default;

    public ObservableCollection<AudioOutputDeviceOption> AudioOutputOptions { get; } = new();

    public IReadOnlyList<RenderFrameRateOption> RenderFrameRateOptions { get; } =
    [
        new(24),
        new(30),
        new(60),
        new(120),
        new(144),
        new(240),
        new(280),
        new(320),
        new(360),
        new(400),
        new(480),
        new(0, "Unlimited")
    ];

    public IReadOnlyList<PlaybackSampleRateOption> PlaybackSampleRateOptions { get; } =
    [
        new(11025),
        new(16000),
        new(16384),
        new(22050),
        new(32000),
        new(32768),
        new(44100),
        new(48000),
        new(96000),
        new(192000)
    ];

    public IReadOnlyList<ThemeColorOption> ThemeColorOptions { get; } =
    [
        new("System", "System Default"),
        new("Dark", "Dark"),
        new("Light", "Light")
    ];

    public IReadOnlyList<OutputChannelModeOption> OutputChannelModeOptions { get; } =
    [
        new(true, "Stereo"),
        new(false, "Mono")
    ];

    public RenderFrameRateOption? SelectedRenderFrameRate
    {
        get => _selectedRenderFrameRate;
        set
        {
            if (!SetField(ref _selectedRenderFrameRate, value) || value is null)
                return;

            ApplyRenderFrameRate(value);
            SaveUserSettingsIfReady();
        }
    }

    public AudioOutputDeviceOption? SelectedAudioOutput
    {
        get => _selectedAudioOutputDevice;
        set
        {
            if (!SetField(ref _selectedAudioOutputDevice, value) || value is null)
                return;

            _selectedAudioOutputDeviceNumber = value.DeviceNumber;
            if (_audioEngine is not null && !_audioEngine.TrySetOutputDevice(value.DeviceNumber, out _))
                return;

            SaveUserSettingsIfReady();
        }
    }

    public bool ReverbEnabled
    {
        get => _reverbEnabled;
        set
        {
            if (!SetField(ref _reverbEnabled, value))
                return;

            if (_audioEngine is not null)
            {
                if (IsSequencePlaying && ResolvePlaybackSequence() is { } sequence)
                    ApplySequenceReverb(sequence);
                else
                    _audioEngine.ConfigureReverb(value ? _soundModeReverbLevel : 0, _fixedDirectSoundSampleRate);
            }
            SaveUserSettingsIfReady();
        }
    }

    public PlaybackSampleRateOption? SelectedPlaybackSampleRate
    {
        get => _selectedPlaybackSampleRate;
        set
        {
            if (!SetField(ref _selectedPlaybackSampleRate, value) || value is null)
                return;

            _outputSampleRate = value.SampleRate;
            if (_audioEngine is not null && !_audioEngine.TrySetOutputSampleRate(value.SampleRate, out var error))
                VoiceGroupStatus = $"Failed to set output sample rate: {error}";
            SaveUserSettingsIfReady();
        }
    }

    public ThemeColorOption? SelectedThemeColor
    {
        get => _selectedThemeColor;
        set
        {
            if (!SetField(ref _selectedThemeColor, value) || value is null)
                return;

            SaveUserSettingsIfReady();
        }
    }

    public OutputChannelModeOption? SelectedOutputChannelMode
    {
        get => _selectedOutputChannelMode;
        set
        {
            if (!SetField(ref _selectedOutputChannelMode, value) || value is null)
                return;

            if (_audioEngine is not null)
                _audioEngine.StereoOutputEnabled = value.IsStereo;
            SaveUserSettingsIfReady();
        }
    }

    public double MeterDecayMilliseconds
    {
        get => _meterDecayMilliseconds;
        set
        {
            double normalized = Math.Round(Math.Clamp(value, 0, 200));
            if (!SetField(ref _meterDecayMilliseconds, normalized))
                return;

            OnPropertyChanged(nameof(MeterDecayDisplay));
            SaveUserSettingsIfReady();
        }
    }

    public string MeterDecayDisplay => $"{MeterDecayMilliseconds:0} ms";

    public double EmulationVolume
    {
        get => _emulationVolume;
        set
        {
            double normalized = Math.Round(Math.Clamp(value, 0, 255));
            if (!SetField(ref _emulationVolume, normalized))
                return;

            OnPropertyChanged(nameof(EmulationVolumeDisplay));
            if (_audioEngine is not null)
                _audioEngine.EmulationGain = (float)(normalized / 255.0);
            SaveUserSettingsIfReady();
        }
    }

    public string EmulationVolumeDisplay => $"{EmulationVolume:0}";

    public double PeakDecayMilliseconds
    {
        get => _peakDecayMilliseconds;
        set
        {
            double normalized = Math.Round(Math.Clamp(value, 0, 2000));
            if (!SetField(ref _peakDecayMilliseconds, normalized))
                return;

            OnPropertyChanged(nameof(PeakDecayDisplay));
            SaveUserSettingsIfReady();
        }
    }

    public string PeakDecayDisplay => $"{PeakDecayMilliseconds:0} ms";

    public MidiCcMapping MidiCcMapping => _midiCcMapping.Clone();

    public void UpdateMidiCcMapping(MidiCcMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (!mapping.TryValidate(out string? error))
            throw new ArgumentException(error, nameof(mapping));

        _midiCcMapping = mapping.Clone();
        SaveUserSettingsIfReady();
    }

    public void InitializeUserSettings()
    {
        var settings = AgbSynthUserSettings.Load();
        _isLoadingUserSettings = true;

        RefreshAudioOutputDevices(settings.LastAudioOutputName, settings.LastAudioOutputDeviceNumber);

        SelectedRenderFrameRate = RenderFrameRateOptions.FirstOrDefault(o => o.FramesPerSecond == settings.RenderFramesPerSecond)
            ?? RenderFrameRateOptions.FirstOrDefault(o => o.FramesPerSecond == 60);
        SelectedAudioBufferSize = AudioBufferSizeOptions.FirstOrDefault(o => o.LatencyMs == settings.AudioBufferLatencyMs)
            ?? AudioBufferSizeOptions.FirstOrDefault(o => o.LatencyMs == _preferredAudioBufferLatencyMs)
            ?? AudioBufferSizeOptions.FirstOrDefault();
        SelectedDirectSoundChannelCount = Math.Clamp(settings.DirectSoundChannelCount ?? _directSoundMixerChannelCount, 0, 12);
        OutputQuantizeEnabled = settings.OutputQuantizeEnabled ?? _outputQuantizeEnabled;
        Mp2kPcmProcessingEnabled = settings.Mp2kPcmProcessingEnabled ?? true;
        int emulationVolume = settings.EmulationVolumeLevel
            ?? (settings.EmulationVolume is int legacyPercent
                ? (int)Math.Round(Math.Clamp(legacyPercent, 0, 100) * 255.0 / 100.0)
                : 255);
        EmulationVolume = Math.Clamp(emulationVolume, 0, 255);
        ReverbEnabled = settings.ReverbEnabled ?? true;
        SelectedPlaybackSampleRate = PlaybackSampleRateOptions.FirstOrDefault(o => o.SampleRate == settings.OutputSampleRate)
            ?? PlaybackSampleRateOptions.FirstOrDefault(o => o.SampleRate == AgbAudioEngine.GbaOutputSampleRate)
            ?? PlaybackSampleRateOptions.FirstOrDefault();
        SelectedThemeColor = ThemeColorOptions.FirstOrDefault(o => string.Equals(o.Key, settings.ThemeColorKey, StringComparison.Ordinal))
            ?? ThemeColorOptions.FirstOrDefault();
        SelectedOutputChannelMode = OutputChannelModeOptions.FirstOrDefault(o => o.IsStereo == (settings.StereoOutputEnabled ?? true))
            ?? OutputChannelModeOptions.FirstOrDefault();
        MeterDecayMilliseconds = Math.Clamp(settings.MeterDecayMilliseconds ?? 100, 0, 200);
        PeakDecayMilliseconds = Math.Clamp(settings.PeakDecayMilliseconds ?? 1000, 0, 2000);
        _midiCcMapping = settings.MidiCcMapping?.Clone()
            ?? settings.XcmdMidiCcMapping?.Clone()
            ?? MidiCcMapping.Default;
        _midiCcMapping.Normalize();
        if (!_midiCcMapping.TryValidate(out _))
            _midiCcMapping = MidiCcMapping.Default;

        // Apply explicitly after the complete load. Relying only on the property
        // setter can leave the constructor's timer interval active when a binding
        // has already assigned the same option instance.
        ApplyRenderFrameRate(SelectedRenderFrameRate
            ?? RenderFrameRateOptions.First(option => option.FramesPerSecond == 60));
        _isLoadingUserSettings = false;
    }

    public void PersistUserSettings()
    {
        SaveUserSettingsIfReady();
    }

    private void RefreshAudioOutputDevices(string? preferredName, int? preferredDeviceNumber)
    {
        AudioOutputOptions.Clear();
        AudioOutputOptions.Add(new AudioOutputDeviceOption(-1, "System Default"));

        for (int device = 0; device < NativeWaveOut.GetDeviceCount(); device++)
        {
            string name = NativeWaveOut.GetDeviceName(device) ?? $"WaveOut {device}";
            AudioOutputOptions.Add(new AudioOutputDeviceOption(device, name));
        }

        SelectedAudioOutput = FindAudioOutput(preferredName, preferredDeviceNumber)
            ?? AudioOutputOptions.FirstOrDefault();
    }

    private AudioOutputDeviceOption? FindAudioOutput(string? name, int? deviceNumber)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var option in AudioOutputOptions)
            {
                if (string.Equals(option.Name, name, StringComparison.Ordinal))
                    return option;
            }
        }

        if (deviceNumber is int number)
        {
            foreach (var option in AudioOutputOptions)
            {
                if (option.DeviceNumber == number)
                    return option;
            }
        }

        return null;
    }

    private void ApplyRenderFrameRate(RenderFrameRateOption option)
    {
        int fps = option.FramesPerSecond <= 0 ? 1000 : option.FramesPerSecond;
        _mixerTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, 1000.0 / fps));
    }

    private void SaveUserSettingsIfReady()
    {
        if (_isLoadingUserSettings)
            return;

        AgbSynthUserSettings.Update(settings =>
        {
            settings.RenderFramesPerSecond = SelectedRenderFrameRate?.FramesPerSecond;
            settings.LastAudioOutputName = SelectedAudioOutput?.Name;
            settings.LastAudioOutputDeviceNumber = SelectedAudioOutput?.DeviceNumber;
            settings.AudioBufferLatencyMs = SelectedAudioBufferSize?.LatencyMs;
            settings.DirectSoundChannelCount = SelectedDirectSoundChannelCount;
            settings.OutputQuantizeEnabled = OutputQuantizeEnabled;
            settings.Mp2kPcmProcessingEnabled = Mp2kPcmProcessingEnabled;
            settings.EmulationVolumeLevel = (int)EmulationVolume;
            settings.EmulationVolume = null;
            settings.ReverbEnabled = ReverbEnabled;
            settings.OutputSampleRate = SelectedPlaybackSampleRate?.SampleRate ?? _outputSampleRate;
            settings.ThemeColorKey = SelectedThemeColor?.Key;
            settings.StereoOutputEnabled = SelectedOutputChannelMode?.IsStereo ?? true;
            settings.MeterDecayMilliseconds = (int)MeterDecayMilliseconds;
            settings.PeakDecayMilliseconds = (int)PeakDecayMilliseconds;
            settings.MidiCcMapping = _midiCcMapping.Clone();
            settings.XcmdMidiCcMapping = null;
        });
    }
}

public sealed class AudioOutputDeviceOption
{
    public AudioOutputDeviceOption(int deviceNumber, string name)
    {
        DeviceNumber = deviceNumber;
        Name = name;
    }

    public int DeviceNumber { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

public sealed class RenderFrameRateOption
{
    public RenderFrameRateOption(int framesPerSecond, string? display = null)
    {
        FramesPerSecond = framesPerSecond;
        Display = display ?? $"{framesPerSecond} FPS";
    }

    public int FramesPerSecond { get; }
    public string Display { get; }

    public override string ToString() => Display;
}

public sealed class PlaybackSampleRateOption
{
    public PlaybackSampleRateOption(int sampleRate)
    {
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }
    public string Display => $"{SampleRate} Hz";

    public override string ToString() => Display;
}

public sealed class ThemeColorOption
{
    public ThemeColorOption(string key, string display)
    {
        Key = key;
        Display = display;
    }

    public string Key { get; }
    public string Display { get; }

    public override string ToString() => Display;
}

public sealed class OutputChannelModeOption
{
    public OutputChannelModeOption(bool isStereo, string display)
    {
        IsStereo = isStereo;
        Display = display;
    }

    public bool IsStereo { get; }
    public string Display { get; }

    public override string ToString() => Display;
}

internal static class NativeWaveOut
{
    public static int GetDeviceCount()
    {
        try
        {
            return (int)waveOutGetNumDevs();
        }
        catch
        {
            return 0;
        }
    }

    public static string? GetDeviceName(int deviceNumber)
    {
        try
        {
            if (waveOutGetDevCaps((UIntPtr)(uint)deviceNumber, out var caps, Marshal.SizeOf<WaveOutCaps>()) != 0)
                return null;
            return caps.ProductName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int waveOutGetDevCaps(UIntPtr uDeviceID, out WaveOutCaps caps, int cbwoc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WaveOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
        public uint Support;
    }
}
