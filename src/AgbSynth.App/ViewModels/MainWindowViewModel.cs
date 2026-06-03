using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using AgbSynth.App.GBA;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _romStatus = "Open a .gba ROM from the File menu.";
    private GbaRom? _loadedRom;

    public string RomStatus
    {
        get => _romStatus;
        private set => SetField(ref _romStatus, value);
    }

    public GbaRom? LoadedRom => _loadedRom;

    public async Task<bool> LoadRomAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            _loadedRom = await GbaRom.LoadAsync(stream, file.Path?.LocalPath ?? file.Name);
            RomStatus = $"Loaded ROM: {file.Name} ({_loadedRom.Length:N0} bytes, code {_loadedRom.GameCode})";
            OnPropertyChanged(nameof(LoadedRom));
            return true;
        }
        catch (Exception ex)
        {
            _loadedRom = null;
            RomStatus = $"Failed to load ROM: {ex.Message}";
            OnPropertyChanged(nameof(LoadedRom));
            return false;
        }
    }

    public bool CreateProjectFile(string outputPath, int songTableOffset, string songTableAddressText)
    {
        if (_loadedRom is null)
        {
            RomStatus = "Project creation failed: no ROM is loaded.";
            return false;
        }

        try
        {
            var project = AgbSynthProjectExporter.CreateFromRom(_loadedRom, songTableOffset, songTableAddressText);
            AgbSynthProjectExporter.Save(outputPath, project);
            RomStatus = $"Project created: {Path.GetFileName(outputPath)} ({project.SongTable.ValidEntryCount:N0} song table entries)";
            return true;
        }
        catch (Exception ex)
        {
            RomStatus = $"Project creation failed: {ex.Message}";
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName)
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
