using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using NAudio.Midi;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private MidiIn? _midiIn;
    private MidiInputOption? _selectedMidiIn;
    private string _midiInStatus = "MIDI IN: (none)";
    private bool _isRefreshingMidiInputs;

    public ObservableCollection<MidiInputOption> MidiInOptions { get; } = new();

    public MidiInputOption? SelectedMidiIn
    {
        get => _selectedMidiIn;
        set
        {
            if (!SetField(ref _selectedMidiIn, value))
                return;

            if (_isRefreshingMidiInputs)
                return;

            _ = OpenSelectedMidiInputAsync();
        }
    }

    public string MidiInStatus
    {
        get => _midiInStatus;
        private set => SetField(ref _midiInStatus, value);
    }

    public Task RefreshMidiInputsAsync()
    {
        _isRefreshingMidiInputs = true;
        CloseMidiInput();
        MidiInOptions.Clear();
        for (int device = 0; device < MidiIn.NumberOfDevices; device++)
        {
            var caps = MidiIn.DeviceInfo(device);
            MidiInOptions.Add(new MidiInputOption(device, caps.ProductName));
        }

        var settings = AgbSynthUserSettings.Load();
        SelectedMidiIn = FindMidiInput(settings.LastMidiInputName, settings.LastMidiInputDeviceNumber)
            ?? MidiInOptions.FirstOrDefault();
        _isRefreshingMidiInputs = false;
        if (SelectedMidiIn is null)
            MidiInStatus = "MIDI IN: no device";
        else
            _ = OpenSelectedMidiInputAsync();
        return Task.CompletedTask;
    }

    public Task OpenSelectedMidiInputAsync()
    {
        CloseMidiInput();
        if (SelectedMidiIn is null)
        {
            MidiInStatus = "MIDI IN: (none)";
            return Task.CompletedTask;
        }

        try
        {
            _midiIn = new MidiIn(SelectedMidiIn.DeviceNumber);
            _midiIn.MessageReceived += OnMidiMessageReceived;
            _midiIn.ErrorReceived += OnMidiErrorReceived;
            _midiIn.Start();
            MidiInStatus = $"MIDI IN: {SelectedMidiIn.Name}";
            AgbSynthUserSettings.Update(settings =>
            {
                settings.LastMidiInputName = SelectedMidiIn.Name;
                settings.LastMidiInputDeviceNumber = SelectedMidiIn.DeviceNumber;
            });
        }
        catch (Exception ex)
        {
            CloseMidiInput();
            MidiInStatus = $"MIDI IN failed: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private MidiInputOption? FindMidiInput(string? name, int? deviceNumber)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var option in MidiInOptions)
            {
                if (string.Equals(option.Name, name, StringComparison.Ordinal))
                    return option;
            }
        }

        if (deviceNumber is int number)
        {
            foreach (var option in MidiInOptions)
            {
                if (option.DeviceNumber == number)
                    return option;
            }
        }

        return null;
    }

    public void CloseMidiInput()
    {
        if (_midiIn is null)
            return;

        try
        {
            _midiIn.Stop();
        }
        catch
        {
        }

        _midiIn.MessageReceived -= OnMidiMessageReceived;
        _midiIn.ErrorReceived -= OnMidiErrorReceived;
        _midiIn.Dispose();
        _midiIn = null;
    }

    private void OnMidiErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        Dispatcher.UIThread.Post(() => MidiInStatus = $"MIDI IN error: 0x{e.RawMessage:X8}");
    }

    private void OnMidiMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        int raw = e.RawMessage;
        Dispatcher.UIThread.Post(() => HandleMidiRawMessage(raw));
    }

    private void HandleMidiRawMessage(int raw)
    {
        int status = raw & 0xFF;
        int data1 = (raw >> 8) & 0xFF;
        int data2 = (raw >> 16) & 0xFF;
        int command = status & 0xF0;
        int channel = status & 0x0F;

        switch (command)
        {
            case 0x80:
                PreviewNoteOff(data1, channel);
                break;
            case 0x90:
                if (data2 == 0)
                    PreviewNoteOff(data1, channel);
                else
                    PreviewNoteOn(data1, data2, channel);
                break;
            case 0xB0:
                ApplyMidiControlChange(channel, data1, data2);
                break;
            case 0xC0:
                ApplyMidiProgramChange(channel, data1);
                break;
            case 0xE0:
                ApplyMidiPitchBend(channel, data1 | (data2 << 7));
                break;
        }
    }
}

public sealed class MidiInputOption
{
    public MidiInputOption(int deviceNumber, string name)
    {
        DeviceNumber = deviceNumber;
        Name = name;
    }

    public int DeviceNumber { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

internal sealed class AgbSynthUserSettings
{
    public string? LastMidiInputName { get; set; }
    public int? LastMidiInputDeviceNumber { get; set; }
    public string? LastAudioOutputName { get; set; }
    public int? LastAudioOutputDeviceNumber { get; set; }
    public int? RenderFramesPerSecond { get; set; }
    public int? AudioBufferLatencyMs { get; set; }
    public int? DirectSoundChannelCount { get; set; }
    public bool? OutputQuantizeEnabled { get; set; }
    public bool? Mp2kPcmProcessingEnabled { get; set; }
    public int? EmulationVolume { get; set; }
    public int? EmulationVolumeLevel { get; set; }
    public bool? ReverbEnabled { get; set; }
    public int? OutputSampleRate { get; set; }
    public string? ThemeColorKey { get; set; }
    public bool? StereoOutputEnabled { get; set; }
    public int? MeterDecayMilliseconds { get; set; }
    public int? PeakDecayMilliseconds { get; set; }
    public AgbSynth.App.MP2K.MidiCcMapping? MidiCcMapping { get; set; }
    public AgbSynth.App.MP2K.MidiCcMapping? XcmdMidiCcMapping { get; set; }

    public static AgbSynthUserSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new AgbSynthUserSettings();

            return JsonSerializer.Deserialize<AgbSynthUserSettings>(File.ReadAllText(path))
                ?? new AgbSynthUserSettings();
        }
        catch
        {
            return new AgbSynthUserSettings();
        }
    }

    public static void Save(AgbSynthUserSettings settings)
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public static void Update(Action<AgbSynthUserSettings> update)
    {
        var settings = Load();
        update(settings);
        Save(settings);
    }

    private static string GetSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AgbSynth", "settings.json");
    }
}
