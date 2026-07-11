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
public sealed class WaveDataRow : INotifyPropertyChanged, INotifyPropertyChanging, ITableRowVisualState
{
    private static readonly JsonSerializerOptions AssetJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private string _dataHex = string.Empty;
    private string _label = string.Empty;
    private string _note = string.Empty;
    private bool _loops;
    private int _loopStart;
    private int _loopEnd;
    private bool _isSelected;
    private bool _isPointerOver;

    public int Id { get; init; }
    public string IdText => Id.ToString("D3");
    public string FilePath { get; init; } = string.Empty;
    public string ProjectDirectory { get; init; } = string.Empty;
    public string DataFormat { get; private set; } = "Signed8MonoPcm";
    public int HeaderOffset { get; private set; }
    public int DataOffset { get; private set; }
    public int LoopFlags { get; private set; }
    public uint Frequency { get; private set; }
    public string FileName => Path.GetFileName(FilePath);
    public string FileDisplay => FormatFileDisplay(Label, FilePath, $"wavedata_{Id:D3}");
    public int SampleCount => DataBytes.Length;
    public string SampleCountText => $"{SampleCount:N0}";
    public string FrequencyText => Frequency == 0 ? "--" : $"{Frequency / 1024.0:0.##} Hz";
    public string DataHex => _dataHex;
    public string DataHexDisplay => _dataHex.Length <= 48 ? _dataHex : $"{_dataHex[..48]}...";

    public byte[] DataBytes
    {
        get
        {
            string normalized = NormalizeHex(_dataHex);
            if (normalized.Length % 2 != 0)
                normalized = normalized[..^1];
            return normalized.Length == 0 ? [] : Convert.FromHexString(normalized);
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value ?? string.Empty))
                OnPropertyChanged(nameof(FileDisplay));
        }
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value ?? string.Empty);
    }

    public bool Loops
    {
        get => _loops;
        set
        {
            if (!SetField(ref _loops, value))
                return;

            OnPropertyChanged(nameof(LoopText));
        }
    }

    public int LoopStart
    {
        get => _loopStart;
        set
        {
            int max = Math.Max(0, SampleCount - 1);
            int loopStart = Math.Clamp(value, 0, max);
            if (loopStart >= LoopEnd)
                loopStart = Math.Max(0, LoopEnd - 1);
            if (!SetField(ref _loopStart, loopStart))
                return;

            OnPropertyChanged(nameof(LoopStartText));
            OnPropertyChanged(nameof(LoopText));
        }
    }

    public int LoopEnd
    {
        get => _loopEnd;
        set
        {
            int max = Math.Max(1, SampleCount);
            int loopEnd = Math.Clamp(value, 1, max);
            if (loopEnd <= LoopStart)
                loopEnd = Math.Min(max, LoopStart + 1);
            if (!SetField(ref _loopEnd, loopEnd))
                return;

            OnPropertyChanged(nameof(LoopEndText));
            OnPropertyChanged(nameof(LoopText));
            OnPropertyChanged(nameof(SampleCountText));
        }
    }

    public string LoopStartText
    {
        get => LoopStart.ToString();
        set => LoopStart = ParseSampleIndex(value, LoopStart);
    }

    public string LoopEndText
    {
        get => LoopEnd.ToString();
        set => LoopEnd = ParseSampleIndex(value, LoopEnd);
    }

    public string LoopText => Loops ? $"{LoopStart}-{LoopEnd}" : "Off";

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

    public static WaveDataRow FromProjectInfo(WaveDataProjectInfo waveData, string projectDirectory)
    {
        var row = new WaveDataRow
        {
            Id = waveData.Id,
            FilePath = waveData.FilePath,
            ProjectDirectory = projectDirectory,
            DataFormat = string.IsNullOrWhiteSpace(waveData.DataFormat) ? "Signed8MonoPcm" : waveData.DataFormat,
            HeaderOffset = waveData.HeaderOffset,
            LoopFlags = waveData.LoopFlags,
            Loops = waveData.Loops,
            Frequency = waveData.Frequency,
            LoopStart = unchecked((int)Math.Min(waveData.LoopStart, int.MaxValue)),
            LoopEnd = unchecked((int)Math.Min(waveData.Size, int.MaxValue)),
            DataOffset = waveData.DataOffset
        };
        row.Label = Path.GetFileNameWithoutExtension(row.FileName);
        return row;
    }

    public static WaveDataRow CreateBlank(int id, string filePath, string projectDirectory)
    {
        var row = new WaveDataRow
        {
            Id = id,
            FilePath = filePath,
            ProjectDirectory = projectDirectory,
            DataFormat = "Signed8MonoPcm",
            HeaderOffset = 0,
            DataOffset = 0,
            LoopFlags = 0,
            Loops = false,
            Frequency = 32768u * 1024u,
            LoopStart = 0,
            LoopEnd = 256
        };
        row._label = Path.GetFileNameWithoutExtension(row.FileName);
        row._dataHex = string.Concat(Enumerable.Repeat("00", 256));
        return row;
    }

    public WaveDataRow CloneForInsert(int id, string filePath)
    {
        var row = new WaveDataRow
        {
            Id = id,
            FilePath = filePath,
            ProjectDirectory = ProjectDirectory,
            DataFormat = DataFormat,
            HeaderOffset = HeaderOffset,
            DataOffset = DataOffset,
            LoopFlags = LoopFlags,
            Loops = Loops,
            Frequency = Frequency,
            LoopStart = LoopStart,
            LoopEnd = LoopEnd
        };
        row._label = Label;
        row._note = Note;
        row._dataHex = _dataHex;
        return row;
    }

    public void ReloadFromFile()
    {
        string? path = ResolveAbsolutePath();
        if (path is null || !File.Exists(path))
            return;

        try
        {
            var document = JsonSerializer.Deserialize<WaveDataAssetDocument>(File.ReadAllText(path), AssetJsonOptions);
            if (document?.Header is not null)
            {
                DataFormat = string.IsNullOrWhiteSpace(document.DataFormat) ? "Signed8MonoPcm" : document.DataFormat;
                HeaderOffset = document.Header.HeaderOffset;
                DataOffset = document.Header.DataOffset;
                LoopFlags = document.Header.LoopFlags;
                Loops = document.Header.Loops;
                Frequency = document.Header.Frequency;
                _loopStart = unchecked((int)Math.Min(document.Header.LoopStart, int.MaxValue));
                _loopEnd = unchecked((int)Math.Min(document.Header.Size, int.MaxValue));
            }

            _label = string.IsNullOrWhiteSpace(document?.Label)
                ? Path.GetFileNameWithoutExtension(FileName)
                : document.Label;
            _note = document?.Note ?? string.Empty;

            _dataHex = NormalizeHex(document?.DataHex ?? string.Empty);
            if (_loopEnd <= 0 || _loopEnd > SampleCount)
                _loopEnd = SampleCount;
            if (_loopStart >= _loopEnd)
                _loopStart = Math.Max(0, _loopEnd - 1);

            OnPropertyChanged(nameof(DataHex));
            OnPropertyChanged(nameof(DataHexDisplay));
            OnPropertyChanged(nameof(DataBytes));
            OnPropertyChanged(nameof(SampleCount));
            OnPropertyChanged(nameof(SampleCountText));
            OnPropertyChanged(nameof(FrequencyText));
            OnPropertyChanged(nameof(LoopStart));
            OnPropertyChanged(nameof(LoopEnd));
            OnPropertyChanged(nameof(LoopStartText));
            OnPropertyChanged(nameof(LoopEndText));
            OnPropertyChanged(nameof(LoopText));
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(FileDisplay));
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    public void SetLoopPoints(int loopStart, int loopEnd)
    {
        LoopEnd = loopEnd;
        LoopStart = loopStart;
    }

    public SampleHeaderProjectInfo ToSampleHeader()
    {
        return new SampleHeaderProjectInfo
        {
            HeaderOffset = HeaderOffset,
            LoopFlags = LoopFlags,
            Loops = Loops,
            Frequency = Frequency,
            LoopStart = unchecked((uint)Math.Clamp(LoopStart, 0, int.MaxValue)),
            Size = unchecked((uint)Math.Clamp(LoopEnd, 0, int.MaxValue)),
            DataOffset = DataOffset,
            FilePath = FilePath
        };
    }

    public void SaveAsset()
    {
        string path = ResolveAbsolutePath() ?? throw new InvalidOperationException("WaveData path is not set.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, CreateAssetBytes());
    }

    public byte[] CreateAssetBytes()
    {
        var document = new WaveDataAssetDocument
        {
            Header = ToSampleHeader(),
            DataFormat = DataFormat,
            DataHex = Convert.ToHexString(DataBytes),
            Label = Label,
            Note = Note
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, AssetJsonOptions);
    }

    private string? ResolveAbsolutePath()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return null;

        string normalized = FilePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return normalized;

        return string.IsNullOrWhiteSpace(ProjectDirectory)
            ? normalized
            : Path.Combine(ProjectDirectory, normalized);
    }

    private static int ParseSampleIndex(string? text, int fallback)
    {
        string value = text?.Trim() ?? string.Empty;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex))
            return Math.Max(0, hex);

        string digits = new(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int parsed) ? Math.Max(0, parsed) : fallback;
    }

    private static string NormalizeHex(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string FormatFileDisplay(string label, string filePath, string fallback)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        if (string.IsNullOrWhiteSpace(displayLabel))
            displayLabel = fallback;

        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
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

    private sealed class WaveDataAssetDocument
    {
        public string Format { get; set; } = "AgbSynthWaveData";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public SampleHeaderProjectInfo Header { get; set; } = new();
        public string DataFormat { get; set; } = "Signed8MonoPcm";
        public string DataHex { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
