using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using AgbSynth.App.GBA;

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

    public async Task LoadRomAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            _loadedRom = await GbaRom.LoadAsync(stream, file.Path?.LocalPath ?? file.Name);
            RomStatus = $"Loaded ROM: {file.Name} ({_loadedRom.Length:N0} bytes, code {_loadedRom.GameCode})";
        }
        catch (Exception ex)
        {
            _loadedRom = null;
            RomStatus = $"Failed to load ROM: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

