using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgbSynth.App.GBA;

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
        AddressTextBox.KeyDown += OnAddressKeyDown;
    }

    public int? SongTableOffset { get; private set; }
    public string? SongTableAddressText { get; private set; }

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

    private void TryAccept()
    {
        string text = AddressTextBox.Text?.Trim() ?? string.Empty;
        if (!GbaAddressParser.TryParseRomAddressOrOffset(text, _romLength, out int offset, out string? error))
        {
            ErrorTextBlock.Text = error ?? "Invalid address.";
            return;
        }

        SongTableOffset = offset;
        SongTableAddressText = text;
        Close(true);
    }
}
