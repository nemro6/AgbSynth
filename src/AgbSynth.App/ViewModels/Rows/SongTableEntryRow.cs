using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;
public sealed class SongTableEntryRow : INotifyPropertyChanged, ITableRowVisualState
{
    private int _songId;
    private string _label = string.Empty;
    private string _songHeaderFilePath = string.Empty;
    private string _songHeaderDisplay = string.Empty;
    private int _group1;
    private int _group2;
    private string _note = string.Empty;
    private bool _isSelected;
    private bool _isPointerOver;

    public int SongId
    {
        get => _songId;
        set
        {
            if (SetField(ref _songId, Math.Max(0, value)))
                OnPropertyChanged(nameof(IndexText));
        }
    }

    public string IndexText => SongId.ToString("D3");

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value ?? string.Empty);
    }

    public int TableOffset { get; init; }
    public string TableOffsetHex => $"0x{TableOffset:X}";
    public string HeaderPointer { get; init; } = string.Empty;
    public int HeaderOffset { get; init; }
    public string HeaderOffsetHex => $"0x{HeaderOffset:X}";

    public string SongHeaderFilePath
    {
        get => _songHeaderFilePath;
        set => SetField(ref _songHeaderFilePath, value ?? string.Empty);
    }

    public string SongHeaderDisplay
    {
        get => string.IsNullOrWhiteSpace(_songHeaderDisplay) ? SongHeaderFilePath : _songHeaderDisplay;
        set => SetField(ref _songHeaderDisplay, value ?? string.Empty);
    }

    public int Group1
    {
        get => _group1;
        set
        {
            if (SetField(ref _group1, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(Group1Text));
        }
    }

    public int Group2
    {
        get => _group2;
        set
        {
            if (SetField(ref _group2, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(Group2Text));
        }
    }

    public string Group1Text
    {
        get => Group1.ToString();
        set => Group1 = ParseByteText(value);
    }

    public string Group2Text
    {
        get => Group2.ToString();
        set => Group2 = ParseByteText(value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value ?? string.Empty);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
                OnPropertyChanged(nameof(RowBackground));
        }
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set
        {
            if (SetField(ref _isPointerOver, value))
                OnPropertyChanged(nameof(RowBackground));
        }
    }

    public IBrush RowBackground => TableRowVisuals.GetBackground(IsSelected, IsPointerOver);

    public static SongTableEntryRow FromProjectInfo(SongTableEntryProjectInfo song, string? songHeaderFilePath = null, string? songHeaderDisplay = null)
    {
        return new SongTableEntryRow
        {
            SongId = song.SongId,
            Label = song.Label,
            TableOffset = song.TableOffset,
            HeaderPointer = song.HeaderPointer,
            HeaderOffset = song.HeaderOffset,
            SongHeaderFilePath = string.IsNullOrWhiteSpace(songHeaderFilePath) ? song.SongHeaderFilePath : songHeaderFilePath,
            SongHeaderDisplay = songHeaderDisplay ?? string.Empty,
            Group1 = song.Group1,
            Group2 = song.Group2,
            Note = song.Note
        };
    }

    public SongTableEntryRow CloneForInsert()
    {
        return new SongTableEntryRow
        {
            Label = Label,
            HeaderPointer = HeaderPointer,
            HeaderOffset = HeaderOffset,
            SongHeaderFilePath = SongHeaderFilePath,
            SongHeaderDisplay = SongHeaderDisplay,
            Group1 = Group1,
            Group2 = Group2,
            Note = Note
        };
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

    private static int ParseByteText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        string digits = new(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return 0;

        return int.TryParse(digits, out int value)
            ? Math.Clamp(value, 0, 255)
            : 255;
    }
}
