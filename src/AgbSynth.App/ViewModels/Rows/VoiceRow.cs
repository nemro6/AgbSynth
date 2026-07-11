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
public sealed class VoiceRow : INotifyPropertyChanged, INotifyPropertyChanging, ITableRowVisualState
{
    public static IReadOnlyList<VoiceTypeOption> TypeOptions { get; } =
    [
        new(0x00, "DirectSound"),
        new(0x08, "DirectSound Fixed"),
        new(0x01, "Square 1"),
        new(0x02, "Square 2"),
        new(0x03, "Wave Memory"),
        new(0x0B, "Wave Memory Fixed"),
        new(0x04, "Noise"),
        new(0x40, "Key Split"),
        new(0x80, "Drum Set"),
        new(0xFF, "--")
    ];

    public static IReadOnlyList<VoiceDataOption> SquareDutyOptions { get; } =
    [
        new(0, "12.5%"),
        new(1, "25%"),
        new(2, "50%"),
        new(3, "75%")
    ];

    public static IReadOnlyList<VoiceDataOption> NoiseControlOptions { get; } =
        Enumerable.Range(0, 256).Select(value => new VoiceDataOption(value, $"0x{value:X2}")).ToArray();
    public static IReadOnlyList<VoiceDataOption> EmptyFormatOptions { get; } = [new(0, "None")];
    public static IReadOnlyList<VoiceDataOption> DirectSoundFormatOptions { get; } =
    [
        new(0, "Normal"),
        new(1, "Fixed")
    ];
    public static IReadOnlyList<VoiceDataOption> SquareFormatOptions { get; } =
    [
        new(1, "Square 1"),
        new(2, "Square 2")
    ];
    public static IReadOnlyList<VoiceDataOption> NoiseFormatOptions { get; } =
    [
        new(0, "White"),
        new(1, "Pink")
    ];

    public IReadOnlyList<VoiceTypeOption> AvailableTypeOptions => TypeOptions;
    public IReadOnlyList<VoiceDataOption> AvailableSquareDutyOptions => SquareDutyOptions;
    public IReadOnlyList<VoiceDataOption> AvailableNoiseControlOptions => NoiseControlOptions;
    public IReadOnlyList<VoiceDataOption> AvailableNoiseKindOptions => NoiseFormatOptions;

    private int _index;
    private string _label = string.Empty;
    private int _type;
    private string _typeName = string.Empty;
    private string _usageText = string.Empty;
    private string _dataPointer = string.Empty;
    private string _dataFilePath = string.Empty;
    private string _projectDirectory = string.Empty;
    private int _squareDutyIndex = 2;
    private int _noiseControl;
    private bool _isSelected;
    private bool _isPointerOver;
    private int _key;
    private int _length;
    private int _panOrSweep;
    private int _attack;
    private int _decay;
    private int _sustain;
    private int _release;

    public int Index
    {
        get => _index;
        set
        {
            if (!SetField(ref _index, Math.Clamp(value, 0, 127)))
                return;

            if (Source is not null)
                Source.Index = _index;
            OnPropertyChanged(nameof(IndexHex));
            OnPropertyChanged(nameof(IndexText));
        }
    }
    public string IndexHex => $"0x{Index:X2}";
    public string IndexText => Index.ToString("D3");
    public int Type
    {
        get => _type;
        set
        {
            int clamped = Math.Clamp(value, 0, 255);
            if (!SetField(ref _type, clamped))
                return;

            if (Source is not null)
                Source.Type = _type;
            TypeName = TypeOptions.FirstOrDefault(option => option.Type == _type)?.Label ?? $"Unknown 0x{_type:X2}";
            OnPropertyChanged(nameof(TypeHex));
            OnPropertyChanged(nameof(SelectedTypeOption));
            OnPropertyChanged(nameof(IsFileDataVisible));
            OnPropertyChanged(nameof(IsPsgSquareDataVisible));
            OnPropertyChanged(nameof(IsPsgWaveMemoryDataVisible));
            OnPropertyChanged(nameof(IsPsgNoiseDataVisible));
            OnPropertyChanged(nameof(DataAssetDirectoryName));
            OnPropertyChanged(nameof(DataDisplay));
            OnPropertyChanged(nameof(FormatOptions));
            OnPropertyChanged(nameof(SelectedFormatOption));
            OnPropertyChanged(nameof(IsFormatEnabled));
            OnPropertyChanged(nameof(OverviewKind));
            OnPropertyChanged(nameof(IsOverviewEnabled));
            OnPropertyChanged(nameof(IsPsgAdsr));
            OnPropertyChanged(nameof(AdsrSustainMaximum));
            OnPropertyChanged(nameof(AdsrSustainText));
            OnPropertyChanged(nameof(SelectedNoiseKindOption));
            OnPropertyChanged(nameof(IsBaseKeyEditable));
            if (IsPsgAdsr && _sustain > 0x0F)
                Sustain = 0x0F;
            if (!IsBaseKeyEditable)
                Key = 0;
        }
    }
    public string TypeHex => $"0x{Type:X2}";
    public string UsageText
    {
        get => _usageText;
        set => SetField(ref _usageText, value ?? string.Empty);
    }
    private bool IsDirectSoundDataVoice => (Type & 0x07) == 0 && Type is not 0x40 and not 0x80 and not 0xFF;
    public IReadOnlyList<VoiceDataOption> FormatOptions
    {
        get
        {
            if (IsDirectSoundDataVoice)
                return DirectSoundFormatOptions;
            if (IsPsgSquareDataVisible)
                return SquareFormatOptions;
            if (IsPsgNoiseDataVisible)
                return NoiseFormatOptions;
            return EmptyFormatOptions;
        }
    }
    public bool IsFormatEnabled => IsDirectSoundDataVoice || IsPsgSquareDataVisible || IsPsgNoiseDataVisible;
    public VoiceDataOption? SelectedFormatOption
    {
        get
        {
            if (IsDirectSoundDataVoice)
                return DirectSoundFormatOptions[(Type & 0x08) != 0 ? 1 : 0];
            if (IsPsgSquareDataVisible)
                return SquareFormatOptions[(Type & 0x07) == 0x02 ? 1 : 0];
            if (IsPsgNoiseDataVisible)
                return NoiseFormatOptions[Source?.PsgNoise?.PinkNoise == true ? 1 : 0];
            return EmptyFormatOptions[0];
        }
        set
        {
            if (value is null)
                return;

            if (IsDirectSoundDataVoice)
            {
                Type = value.Value == 1 ? Type | 0x08 : Type & ~0x08;
                return;
            }

            if (IsPsgSquareDataVisible)
            {
                Type = value.Value == 2 ? (Type & ~0x07) | 0x02 : (Type & ~0x07) | 0x01;
                return;
            }

            if (IsPsgNoiseDataVisible && Source is not null)
            {
                SelectedNoiseKindOption = NoiseFormatOptions[value.Value == 1 ? 1 : 0];
            }
        }
    }
    public VoiceDataOption? SelectedNoiseKindOption
    {
        get => NoiseFormatOptions[Source?.PsgNoise?.PinkNoise == true ? 1 : 0];
        set
        {
            if (value is null || Source is null)
                return;

            VoiceDataOption oldValue = SelectedNoiseKindOption ?? NoiseFormatOptions[0];
            if (oldValue.Value == value.Value)
                return;

            OnPropertyChanging(nameof(SelectedNoiseKindOption));
            Source.PsgNoise ??= new PsgNoiseProjectInfo();
            Source.PsgNoise.PinkNoise = value.Value == 1;
            OnPropertyChanged(nameof(SelectedNoiseKindOption));
            OnPropertyChanged(nameof(SelectedFormatOption));
            OnPropertyChanged(nameof(DataDisplay));
        }
    }
    public VoiceTypeOption? SelectedTypeOption
    {
        get => TypeOptions.FirstOrDefault(option => option.Type == Type);
        set
        {
            if (value is null)
                return;

            Type = value.Type;
            if (Type is 0x01 or 0x02)
                SelectedSquareDutyOption = SquareDutyOptions.FirstOrDefault(option => option.Value == _squareDutyIndex) ?? SquareDutyOptions[2];
            else if ((Type & 0x07) == 0x04)
                SelectedNoiseControlOption = NoiseControlOptions[Math.Clamp(_noiseControl, 0, NoiseControlOptions.Count - 1)];
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value ?? string.Empty) && Source is not null)
                Source.Label = _label;
        }
    }

    public int Key
    {
        get => _key;
        set
        {
            int key = IsBaseKeyEditable ? Math.Clamp(value, 0, 127) : 0;
            if (SetField(ref _key, key) && Source is not null)
                Source.Key = _key;
            OnPropertyChanged(nameof(KeyName));
        }
    }
    public bool IsBaseKeyEditable => Type is not 0x40 and not 0x80;
    public string KeyName
    {
        get => NoteNameFromMidi(Key);
        set
        {
            if (TryParseNoteName(value, out int note))
                Key = note;
        }
    }

    public int Length
    {
        get => _length;
        set
        {
            if (SetField(ref _length, Math.Clamp(value, 0, 255)) && Source is not null)
                Source.Length = _length;
        }
    }

    public int PanOrSweep
    {
        get => _panOrSweep;
        set
        {
            if (SetField(ref _panOrSweep, Math.Clamp(value, 0, 255)) && Source is not null)
                Source.PanOrSweep = _panOrSweep;
            OnPropertyChanged(nameof(PanOverrideEnabled));
            OnPropertyChanged(nameof(PanSigned));
            OnPropertyChanged(nameof(PanRawHex));
        }
    }
    public bool PanOverrideEnabled
    {
        get => (PanOrSweep & 0x80) != 0;
        set
        {
            int pan = PanOverrideEnabled ? Math.Clamp(PanOrSweep & 0x7F, 0, 127) : 64;
            PanOrSweep = value ? 0x80 | pan : 0;
        }
    }
    public int PanSigned
    {
        get => PanOverrideEnabled ? Math.Clamp(PanOrSweep & 0x7F, 0, 127) - 64 : 0;
        set
        {
            int pan = Math.Clamp(value, -64, 63) + 64;
            PanOrSweep = PanOverrideEnabled ? 0x80 | pan : 0;
        }
    }
    public string PanRawHex => $"0x{PanOrSweep:X2}";
    public int? DataOffset { get; private set; }
    public string DataOffsetHex => DataOffset is int offset ? $"0x{offset:X}" : string.Empty;
    public string ProjectDirectory
    {
        get => _projectDirectory;
        set => SetField(ref _projectDirectory, value ?? string.Empty);
    }
    public uint? SampleFrequency { get; private set; }
    public uint? SampleSize { get; private set; }
    public bool? SampleLoops { get; private set; }
    public DrumSetProjectInfo? DrumSet { get; private set; }
    public KeySplitProjectInfo? KeySplit { get; private set; }
    public VoiceProjectInfo? Source { get; init; }
    public Func<string, string>? DataFileDisplayResolver { get; set; }

    public string TypeName
    {
        get => _typeName;
        set
        {
            if (SetField(ref _typeName, value) && Source is not null)
                Source.TypeName = _typeName;
        }
    }

    public string DataPointer
    {
        get => _dataPointer;
        set
        {
            if (SetField(ref _dataPointer, value) && Source is not null)
                Source.DataPointer = _dataPointer;
            OnPropertyChanged(nameof(DataDisplay));
        }
    }

    public string DataFilePath
    {
        get => _dataFilePath;
        set
        {
            if (SetField(ref _dataFilePath, value ?? string.Empty) && Source is not null)
            {
                Source.DataFilePath = _dataFilePath;
                if (IsDirectSoundDataVoice && Source.Sample is not null)
                    Source.Sample.FilePath = _dataFilePath;
                if (IsPsgWaveMemoryDataVisible)
                {
                    Source.PsgWaveMemory ??= new PsgWaveMemoryProjectInfo();
                    Source.PsgWaveMemory.FilePath = _dataFilePath;
                }
            }
            OnPropertyChanged(nameof(DataDisplay));
        }
    }

    public string DataDisplay => string.IsNullOrWhiteSpace(DataFilePath)
        ? GetInlineDataDisplay()
        : DataFileDisplayResolver?.Invoke(DataFilePath) ?? FormatFileDisplay(string.Empty, DataFilePath);

    public bool IsFileDataVisible => IsDirectSoundDataVoice || IsPsgWaveMemoryDataVisible || Type is 0x40 or 0x80;
    public bool IsPsgSquareDataVisible => (Type & 0x07) is 0x01 or 0x02;
    public bool IsPsgWaveMemoryDataVisible => (Type & 0x07) == 0x03;
    public bool IsPsgNoiseDataVisible => (Type & 0x07) == 0x04;
    public bool IsPsgAdsr => IsPsgSquareDataVisible || IsPsgNoiseDataVisible || IsPsgWaveMemoryDataVisible;
    public int AdsrSustainMaximum => IsPsgAdsr ? 15 : 255;
    public string DataAssetDirectoryName => Type switch
    {
        0x40 => "keysplit",
        0x80 => "drumset",
        _ when IsPsgWaveMemoryDataVisible => "wavememory",
        _ when IsDirectSoundDataVoice => "wavedata",
        _ => string.Empty
    };
    public string OverviewKind
    {
        get
        {
            if (IsPsgSquareDataVisible)
                return "Square";
            if (IsPsgWaveMemoryDataVisible)
                return "WaveMemory";
            if (IsDirectSoundDataVoice)
                return "WaveData";
            return string.Empty;
        }
    }
    public bool IsOverviewEnabled => !string.IsNullOrWhiteSpace(OverviewKind);

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
    public VoiceDataOption? SelectedSquareDutyOption
    {
        get => SquareDutyOptions.FirstOrDefault(option => option.Value == _squareDutyIndex) ?? SquareDutyOptions[2];
        set
        {
            if (value is null)
                return;

            VoiceDataOption oldValue = SelectedSquareDutyOption ?? SquareDutyOptions[2];
            if (oldValue.Value == value.Value)
                return;

            OnPropertyChanging(nameof(SelectedSquareDutyOption));
            _squareDutyIndex = Math.Clamp(value.Value, 0, 3);
            if (Source is not null)
            {
                Source.PsgSquare ??= new PsgSquareProjectInfo();
                Source.PsgSquare.DutyIndex = _squareDutyIndex;
                Source.PsgSquare.DutyRatio = _squareDutyIndex switch
                {
                    0 => 0.125,
                    1 => 0.25,
                    2 => 0.5,
                    3 => 0.75,
                    _ => 0.5
                };
                Source.DataPointer = $"0x{_squareDutyIndex:X8}";
            }
            DataPointer = $"0x{_squareDutyIndex:X8}";
            OnPropertyChanged(nameof(SelectedSquareDutyOption));
            OnPropertyChanged(nameof(SquareDutyIndex));
            OnPropertyChanged(nameof(DataDisplay));
        }
    }
    public int SquareDutyIndex => _squareDutyIndex;

    public VoiceDataOption? SelectedNoiseControlOption
    {
        get => NoiseControlOptions[Math.Clamp(_noiseControl, 0, NoiseControlOptions.Count - 1)];
        set
        {
            if (value is null)
                return;

            VoiceDataOption oldValue = SelectedNoiseControlOption ?? NoiseControlOptions[0];
            if (oldValue.Value == value.Value)
                return;

            OnPropertyChanging(nameof(SelectedNoiseControlOption));
            _noiseControl = Math.Clamp(value.Value, 0, 255);
            if (Source is not null)
            {
                Source.PsgNoise ??= new PsgNoiseProjectInfo();
                Source.PsgNoise.Control = _noiseControl;
                Source.PsgNoise.ClockDivider = _noiseControl & 0x07;
                Source.PsgNoise.ShortLfsr = (_noiseControl & 0x08) != 0;
                Source.PsgNoise.PrescalerShift = (_noiseControl >> 4) & 0x0F;
                Source.DataPointer = $"0x{_noiseControl:X8}";
            }
            DataPointer = $"0x{_noiseControl:X8}";
            OnPropertyChanged(nameof(SelectedNoiseControlOption));
            OnPropertyChanged(nameof(DataDisplay));
        }
    }

    public int Attack
    {
        get => _attack;
        set
        {
            if (SetField(ref _attack, Math.Clamp(value, 0, 255)) && Source is not null)
                Source.Attack = _attack;
            OnPropertyChanged(nameof(Attack));
        }
    }

    public int Decay
    {
        get => _decay;
        set
        {
            if (SetField(ref _decay, Math.Clamp(value, 0, 255)) && Source is not null)
                Source.Decay = _decay;
            OnPropertyChanged(nameof(Decay));
        }
    }

    public int Sustain
    {
        get => _sustain;
        set
        {
            int maximum = IsPsgAdsr ? 0x0F : 0xFF;
            if (SetField(ref _sustain, Math.Clamp(value, 0, maximum)) && Source is not null)
                Source.Sustain = _sustain;
            OnPropertyChanged(nameof(Sustain));
            OnPropertyChanged(nameof(AdsrSustainText));
        }
    }
    public string AdsrSustainText
    {
        get => Sustain.ToString();
        set
        {
            string text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (int.TryParse(text, out int decimalValue))
                Sustain = decimalValue;
        }
    }

    public int Release
    {
        get => _release;
        set
        {
            if (SetField(ref _release, Math.Clamp(value, 0, 255)) && Source is not null)
                Source.Release = _release;
            OnPropertyChanged(nameof(Release));
        }
    }

    public VoiceProjectInfo ToProjectInfo()
    {
        return new VoiceProjectInfo
        {
            Index = Index,
            Label = Label,
            Type = Type,
            TypeName = TypeName,
            Key = Key,
            Length = Length,
            PanOrSweep = PanOrSweep,
            DataPointer = DataPointer,
            DataOffset = DataOffset,
            DataFilePath = DataFilePath,
            Attack = Attack,
            Decay = Decay,
            Sustain = Sustain,
            Release = Release,
            Sample = Source?.Sample,
            PsgSquare = Source?.PsgSquare,
            PsgWaveMemory = Source?.PsgWaveMemory,
            PsgNoise = Source?.PsgNoise,
            DrumSet = DrumSet,
            KeySplit = KeySplit,
            RawEntryHex = Source?.RawEntryHex ?? string.Empty
        };
    }

    public void ApplyProjectInfo(VoiceProjectInfo voice)
    {
        string typeName = ResolveVoiceTypeName(voice);
        string label = ResolveVoiceLabel(voice, typeName);
        voice.TypeName = typeName;
        voice.Label = label;

        Label = label;
        Type = voice.Type;
        TypeName = typeName;
        Key = voice.Key;
        Length = voice.Length;
        PanOrSweep = voice.PanOrSweep;
        DataPointer = voice.DataPointer;
        DataFilePath = voice.DataFilePath;
        Attack = voice.Attack;
        Decay = voice.Decay;
        Sustain = voice.Sustain;
        Release = voice.Release;

        if (Source is null)
            return;

        Source.Label = Label;
        Source.Type = Type;
        Source.TypeName = TypeName;
        Source.Key = Key;
        Source.Length = Length;
        Source.PanOrSweep = PanOrSweep;
        Source.DataPointer = DataPointer;
        Source.DataOffset = voice.DataOffset;
        Source.DataFilePath = DataFilePath;
        Source.Attack = Attack;
        Source.Decay = Decay;
        Source.Sustain = Sustain;
        Source.Release = Release;
        Source.Sample = voice.Sample;
        Source.PsgSquare = voice.PsgSquare;
        Source.PsgWaveMemory = voice.PsgWaveMemory;
        Source.PsgNoise = voice.PsgNoise;
        Source.DrumSet = voice.DrumSet;
        Source.KeySplit = voice.KeySplit;
        Source.RawEntryHex = voice.RawEntryHex;
        DataOffset = voice.DataOffset;
        SampleFrequency = voice.Sample?.Frequency;
        SampleSize = voice.Sample?.Size;
        SampleLoops = voice.Sample?.Loops;
        DrumSet = voice.DrumSet;
        KeySplit = voice.KeySplit;
        OnPropertyChanged(nameof(DataOffset));
        OnPropertyChanged(nameof(DataOffsetHex));
        OnPropertyChanged(nameof(SampleFrequency));
        OnPropertyChanged(nameof(SampleSize));
        OnPropertyChanged(nameof(SampleLoops));
        OnPropertyChanged(nameof(DrumSet));
        OnPropertyChanged(nameof(KeySplit));
        RefreshInlineDataState();
    }

    public static VoiceRow FromProjectInfo(VoiceGroupOption voiceGroup, VoiceProjectInfo voice)
    {
        string typeName = ResolveVoiceTypeName(voice);
        string label = ResolveVoiceLabel(voice, typeName);
        voice.TypeName = typeName;
        voice.Label = label;

        var row = new VoiceRow
        {
            Index = voice.Index,
            Label = label,
            Type = voice.Type,
            TypeName = typeName,
            Key = voice.Key,
            Length = voice.Length,
            PanOrSweep = voice.PanOrSweep,
            DataPointer = voice.DataPointer,
            DataOffset = voice.DataOffset,
            DataFilePath = GetVoiceDataFilePath(voice),
            Attack = voice.Attack,
            Decay = voice.Decay,
            Sustain = voice.Sustain,
            Release = voice.Release,
            SampleFrequency = voice.Sample?.Frequency,
            SampleSize = voice.Sample?.Size,
            SampleLoops = voice.Sample?.Loops,
            DrumSet = voice.DrumSet,
            KeySplit = voice.KeySplit,
            Source = voice
        };
        row.RefreshInlineDataState();
        return row;
    }

    public void RefreshDataDisplay()
    {
        OnPropertyChanged(nameof(DataDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanging(string? propertyName)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        OnPropertyChanging(propertyName);
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RefreshInlineDataState()
    {
        _squareDutyIndex = Source?.PsgSquare?.DutyIndex ?? TryParseDataByte(DataPointer, fallback: 2);
        _squareDutyIndex = Math.Clamp(_squareDutyIndex, 0, 3);
        _noiseControl = Source?.PsgNoise?.Control ?? TryParseDataByte(DataPointer, fallback: 0);
        _noiseControl = Math.Clamp(_noiseControl, 0, 255);
        OnPropertyChanged(nameof(SelectedSquareDutyOption));
        OnPropertyChanged(nameof(SquareDutyIndex));
        OnPropertyChanged(nameof(SelectedNoiseControlOption));
        OnPropertyChanged(nameof(DataDisplay));
        OnPropertyChanged(nameof(IsFileDataVisible));
        OnPropertyChanged(nameof(IsPsgSquareDataVisible));
        OnPropertyChanged(nameof(IsPsgWaveMemoryDataVisible));
        OnPropertyChanged(nameof(IsPsgNoiseDataVisible));
    }

    private string GetInlineDataDisplay()
    {
        if (IsPsgSquareDataVisible)
            return $"Duty {SelectedSquareDutyOption?.Label ?? "50%"}";
        if (IsPsgNoiseDataVisible)
            return $"Noise {SelectedNoiseControlOption?.Label ?? "0x00"}";
        return DataPointer;
    }

    private static string ResolveVoiceTypeName(VoiceProjectInfo voice)
    {
        if (!string.IsNullOrWhiteSpace(voice.TypeName))
            return voice.TypeName;

        return TypeOptions.FirstOrDefault(option => option.Type == voice.Type)?.Label ?? $"Unknown 0x{voice.Type:X2}";
    }

    private static string ResolveVoiceLabel(VoiceProjectInfo voice, string typeName)
    {
        return string.IsNullOrWhiteSpace(voice.Label) ? typeName : voice.Label;
    }

    private static string FormatFileDisplay(string label, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
    }

    private static string GetVoiceDataFilePath(VoiceProjectInfo voice)
    {
        if (!string.IsNullOrWhiteSpace(voice.DataFilePath))
            return voice.DataFilePath;
        if (!string.IsNullOrWhiteSpace(voice.Sample?.FilePath))
            return voice.Sample.FilePath;
        if (!string.IsNullOrWhiteSpace(voice.PsgWaveMemory?.FilePath))
            return voice.PsgWaveMemory.FilePath;
        return string.Empty;
    }

    private static int TryParseDataByte(string text, int fallback)
    {
        string normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return uint.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out uint value)
            ? (int)(value & 0xFF)
            : fallback;
    }

    private static string NoteNameFromMidi(int note)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int clamped = Math.Clamp(note, 0, 127);
        return $"{names[clamped % 12]}{clamped / 12 - 1}";
    }

    private static bool TryParseNoteName(string? text, out int note)
    {
        note = 60;
        string value = text?.Trim() ?? string.Empty;
        if (int.TryParse(value, out int numeric))
        {
            note = Math.Clamp(numeric, 0, 127);
            return true;
        }

        if (value.Length < 2)
            return false;

        string upper = value.ToUpperInvariant();
        int nameLength = upper.Length >= 2 && upper[1] is '#' or 'B' ? 2 : 1;
        string name = upper[..nameLength];
        if (!int.TryParse(upper[nameLength..], out int octave))
            return false;

        int semitone = name switch
        {
            "C" => 0,
            "C#" or "DB" => 1,
            "D" => 2,
            "D#" or "EB" => 3,
            "E" => 4,
            "F" => 5,
            "F#" or "GB" => 6,
            "G" => 7,
            "G#" or "AB" => 8,
            "A" => 9,
            "A#" or "BB" => 10,
            "B" => 11,
            _ => -1
        };
        if (semitone < 0)
            return false;

        note = Math.Clamp((octave + 1) * 12 + semitone, 0, 127);
        return true;
    }
}
