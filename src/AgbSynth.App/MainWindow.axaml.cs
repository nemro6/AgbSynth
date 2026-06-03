using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AgbSynth.App.ViewModels;

namespace AgbSynth.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnOpenRomClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open GBA ROM",
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("GBA ROM (*.gba)") { Patterns = new[] { "*.gba" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0)
            return;

        await _viewModel.LoadRomAsync(files[0]);
        Title = $"{files[0].Name} - AgbSynth";
    }
}

