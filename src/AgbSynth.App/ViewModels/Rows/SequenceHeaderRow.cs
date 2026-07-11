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
public sealed class SequenceHeaderRow : INotifyPropertyChanged, ITableRowVisualState
{
    private int _trackCount;
    private int _blockCount;
    private int _priority;
    private int _reverb;
    private string _label = string.Empty;
    private string _voiceGroupPointer = string.Empty;
    private int _voiceGroupOffset;
    private string _voiceGroupFilePath = string.Empty;
    private string _voiceGroupDisplay = string.Empty;
    private string _midiFilePath = string.Empty;
    private string _note = string.Empty;
    private bool _isSelected;
    private bool _isPointerOver;

    public int SongId { get; init; }
    public string FilePath { get; set; } = string.Empty;
    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value ?? string.Empty))
                OnPropertyChanged(nameof(Display));
        }
    }

    public int HeaderOffset { get; init; }
    public string HeaderOffsetHex => $"0x{HeaderOffset:X}";
    public string Display => string.IsNullOrWhiteSpace(Label)
        ? $"SEQ:song_{SongId:D3}"
        : $"SEQ:{Label}";

    public int TrackCount
    {
        get => _trackCount;
        set => SetField(ref _trackCount, value);
    }

    public int BlockCount
    {
        get => _blockCount;
        set => SetField(ref _blockCount, value);
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (SetField(ref _priority, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(PriorityText));
        }
    }

    public int Reverb
    {
        get => _reverb;
        set
        {
            if (SetField(ref _reverb, Math.Clamp(value, 0, 255)))
                OnPropertyChanged(nameof(ReverbText));
        }
    }

    public string PriorityText
    {
        get => Priority.ToString();
        set => Priority = ParseByteText(value);
    }

    public string ReverbText
    {
        get => Reverb.ToString();
        set => Reverb = ParseByteText(value);
    }

    public string VoiceGroupPointer
    {
        get => _voiceGroupPointer;
        set => SetField(ref _voiceGroupPointer, value);
    }

    public int VoiceGroupOffset
    {
        get => _voiceGroupOffset;
        set => SetField(ref _voiceGroupOffset, value);
    }

    public string VoiceGroupFilePath
    {
        get => _voiceGroupFilePath;
        set
        {
            if (SetField(ref _voiceGroupFilePath, value ?? string.Empty))
                OnPropertyChanged(nameof(VoiceGroupDisplay));
        }
    }

    public string VoiceGroupDisplay
    {
        get => string.IsNullOrWhiteSpace(_voiceGroupDisplay)
            ? (string.IsNullOrWhiteSpace(VoiceGroupFilePath) ? string.Empty : Path.GetFileName(VoiceGroupFilePath))
            : _voiceGroupDisplay;
        set => SetField(ref _voiceGroupDisplay, value ?? string.Empty);
    }

    public string MidiFilePath
    {
        get => _midiFilePath;
        set
        {
            if (SetField(ref _midiFilePath, value ?? string.Empty))
                OnPropertyChanged(nameof(MidiDisplay));
        }
    }

    public string MidiDisplay => string.IsNullOrWhiteSpace(MidiFilePath)
        ? string.Empty
        : Path.GetFileName(MidiFilePath);

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

    public static SequenceHeaderRow FromProjectInfo(SongHeaderProjectInfo header)
    {
        return new SequenceHeaderRow
        {
            SongId = header.SongId,
            FilePath = header.FilePath,
            Label = header.Label,
            HeaderOffset = header.HeaderOffset,
            TrackCount = header.TrackCount,
            BlockCount = header.BlockCount,
            Priority = header.Priority,
            Reverb = header.Reverb,
            VoiceGroupPointer = header.VoiceGroupPointer,
            VoiceGroupOffset = header.VoiceGroupOffset,
            VoiceGroupFilePath = header.VoiceGroupFilePath,
            MidiFilePath = header.MidiFilePath,
            Note = header.Note
        };
    }

    public SequenceHeaderRow CloneForInsert(int songId)
    {
        return new SequenceHeaderRow
        {
            SongId = songId,
            Label = Label,
            TrackCount = TrackCount,
            BlockCount = BlockCount,
            Priority = Priority,
            Reverb = Reverb,
            VoiceGroupPointer = VoiceGroupPointer,
            VoiceGroupOffset = VoiceGroupOffset,
            VoiceGroupFilePath = VoiceGroupFilePath,
            MidiFilePath = MidiFilePath,
            Note = Note
        };
    }

    public SongHeaderProjectInfo ToProjectInfo()
    {
        return new SongHeaderProjectInfo
        {
            SongId = SongId,
            FilePath = FilePath,
            Label = Label,
            HeaderOffset = HeaderOffset,
            TrackCount = TrackCount,
            BlockCount = BlockCount,
            Priority = Priority,
            Reverb = Reverb,
            VoiceGroupPointer = VoiceGroupPointer,
            VoiceGroupOffset = VoiceGroupOffset,
            VoiceGroupFilePath = VoiceGroupFilePath,
            MidiFilePath = MidiFilePath,
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

    public override string ToString() => Display;
}
