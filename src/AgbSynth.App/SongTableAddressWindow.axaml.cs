using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgbSynth.App.GBA;
using AgbSynth.App.Project;

namespace AgbSynth.App;

public partial class SongTableAddressWindow : Window
{
    private readonly int _romLength;

    public SongTableAddressWindow()
        : this(0)
    {
    }

    public SongTableAddressWindow(int romLength)
    {
        InitializeComponent();
        _romLength = romLength;
        ReadModeComboBox.SelectedIndex = 0;
        AddressTextBox.KeyDown += OnAddressKeyDown;
    }

    public int? SongTableOffset { get; private set; }
    public string? SongTableAddressText { get; private set; }
    public Mp2kRomReadMode ReadMode { get; private set; } = Mp2kRomReadMode.ManualSongTableAddress;
    public bool IncludeUnreferencedVoiceGroups { get; private set; }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        TryAccept();
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        TryAccept();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnReadModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        bool manual = ReadModeComboBox.SelectedIndex != 1;
        AddressInputGrid.IsEnabled = manual;
        ErrorTextBlock.Text = string.Empty;
    }

    private void TryAccept()
    {
        ReadMode = ReadModeComboBox.SelectedIndex == 1
            ? Mp2kRomReadMode.AutomaticDiscovery
            : Mp2kRomReadMode.ManualSongTableAddress;
        IncludeUnreferencedVoiceGroups = IncludeUnreferencedCheckBox.IsChecked == true;

        if (ReadMode == Mp2kRomReadMode.AutomaticDiscovery)
        {
            SongTableOffset = null;
            SongTableAddressText = string.Empty;
            Close(true);
            return;
        }

        string text = AddressTextBox.Text?.Trim() ?? string.Empty;
        string hexText = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"0x{text}";

        if (!GbaAddressParser.TryParseRomAddressOrOffset(hexText, _romLength, out int offset, out string? error))
        {
            ErrorTextBlock.Text = error ?? "Invalid address.";
            return;
        }

        SongTableOffset = offset;
        SongTableAddressText = hexText.ToUpperInvariant();
        Close(true);
    }
}
