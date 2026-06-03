using System.Collections.Generic;
using System.IO;
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

        if (!await _viewModel.LoadRomAsync(files[0]))
            return;

        Title = $"{files[0].Name} - AgbSynth";

        if (_viewModel.LoadedRom is null)
            return;

        var addressWindow = new SongTableAddressWindow(_viewModel.LoadedRom.Length);
        bool accepted = await addressWindow.ShowDialog<bool>(this);
        if (!accepted || addressWindow.SongTableOffset is not int songTableOffset)
            return;

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AgbSynth Project",
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(files[0].Name)}.agbsynth.json",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("AgbSynth project (*.agbsynth.json)") { Patterns = new[] { "*.agbsynth.json" } },
                new("JSON file (*.json)") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });

        string? outputPath = saveFile?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        _viewModel.CreateProjectFile(
            outputPath,
            songTableOffset,
            addressWindow.SongTableAddressText ?? songTableOffset.ToString("X"));
    }
}
