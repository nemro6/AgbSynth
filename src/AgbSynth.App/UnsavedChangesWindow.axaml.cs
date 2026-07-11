using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgbSynth.App;

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesChoice.Save);
    }

    private void OnDiscardClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesChoice.Discard);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesChoice.Cancel);
    }
}
