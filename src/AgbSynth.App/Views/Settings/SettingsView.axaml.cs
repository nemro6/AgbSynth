using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgbSynth.App.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? MidiCcMappingRequested;

    private void OnMidiCcMappingClicked(object? sender, RoutedEventArgs e)
    {
        MidiCcMappingRequested?.Invoke(this, e);
    }
}
