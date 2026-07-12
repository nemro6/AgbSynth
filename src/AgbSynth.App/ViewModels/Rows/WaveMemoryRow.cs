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
public sealed class WaveMemoryRow : INotifyPropertyChanged, INotifyPropertyChanging, ITableRowVisualState
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private string _dataHex = "00000000000000000000000000000000";
    private string _label = string.Empty;
    private string _note = string.Empty;
    private bool _isSelected;
    private bool _isPointerOver;

    public int Id { get; init; }
    public string AssetId { get; init; } = string.Empty;
    public string IdText => Id.ToString("D3");
    public string FilePath { get; init; } = string.Empty;
    public string ProjectDirectory { get; init; } = string.Empty;
    public string DataFormat { get; init; } = "Mp2kPcm4WaveRam";
    public int Size { get; init; } = 16;
    public string FileName => Path.GetFileName(FilePath);
    public string FileDisplay => FormatFileDisplay(Label, FilePath, $"wavememory_{Id:D3}");
    public string ByteCountText => $"{Math.Min(16, NormalizeHex(_dataHex).Length / 2)} / 16 bytes";
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

    public string DataHex
    {
        get => _dataHex;
        set => SetDataHex(value, saveWhenComplete: false);
    }

    public string DataHexDisplay => _dataHex;

    public string NormalizedDataHex
    {
        get
        {
            string normalized = NormalizeHex(_dataHex);
            return normalized.Length >= 32
                ? normalized[..32]
                : normalized.PadRight(32, '0');
        }
    }

    public byte[] DataBytes => Convert.FromHexString(NormalizedDataHex);

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

    public static WaveMemoryRow FromProjectInfo(WaveMemoryProjectInfo waveMemory, string projectDirectory)
    {
        return new WaveMemoryRow
        {
            Id = waveMemory.Id,
            AssetId = waveMemory.AssetId,
            FilePath = waveMemory.FilePath,
            ProjectDirectory = projectDirectory,
            DataFormat = string.IsNullOrWhiteSpace(waveMemory.DataFormat) ? "Mp2kPcm4WaveRam" : waveMemory.DataFormat,
            Size = waveMemory.Size
        };
    }

    public static WaveMemoryRow CreateBlank(int id, string filePath, string projectDirectory)
    {
        var row = new WaveMemoryRow
        {
            Id = id,
            AssetId = AgbSynthFormatContracts.NewAssetId(),
            FilePath = filePath,
            ProjectDirectory = projectDirectory,
            DataFormat = "Mp2kPcm4WaveRam",
            Size = 16
        };
        row._label = Path.GetFileNameWithoutExtension(row.FileName);
        row._dataHex = "00000000000000000000000000000000";
        return row;
    }

    public WaveMemoryRow CloneForInsert(int id, string filePath)
    {
        var row = new WaveMemoryRow
        {
            Id = id,
            AssetId = AgbSynthFormatContracts.NewAssetId(),
            FilePath = filePath,
            ProjectDirectory = ProjectDirectory,
            DataFormat = DataFormat,
            Size = Size
        };
        row._label = Label;
        row._note = Note;
        row._dataHex = _dataHex;
        return row;
    }

    public void ReloadFromFile()
    {
        ReloadMetadata();
        string? path = ResolveAbsolutePath();
        if (path is null || !File.Exists(path))
            return;

        try
        {
            byte[] bytes = File.ReadAllBytes(path).Take(16).ToArray();
            if (bytes.Length < 16)
                bytes = bytes.Concat(new byte[16 - bytes.Length]).ToArray();
            SetDataHex(Convert.ToHexString(bytes), saveWhenComplete: false);
        }
        catch (IOException)
        {
        }
    }

    public void SaveAsset()
    {
        string path = ResolveAbsolutePath() ?? throw new InvalidOperationException("WaveMemory path is not set.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, DataBytes);

        string? metadataPath = ResolveMetadataPath();
        if (metadataPath is not null)
        {
            var metadata = CreateMetadata();
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
        }
    }

    public byte[] CreateMetadataBytes()
    {
        var metadata = CreateMetadata();
        return JsonSerializer.SerializeToUtf8Bytes(metadata, MetadataJsonOptions);
    }

    private void ReloadMetadata()
    {
        _label = Path.GetFileNameWithoutExtension(FileName);
        _note = string.Empty;

        string? path = ResolveMetadataPath();
        if (path is not null && File.Exists(path))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<WaveMemoryMetadataDocument>(File.ReadAllText(path), MetadataJsonOptions);
                if (metadata is not null)
                {
                    _label = metadata.Label ?? _label;
                    _note = metadata.Note ?? string.Empty;
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(FileDisplay));
    }

    private void SetDataHex(string? value, bool saveWhenComplete)
    {
        string normalized = NormalizeHex(value ?? string.Empty);
        if (normalized.Length > 32)
            normalized = normalized[..32];

        if (!SetField(ref _dataHex, normalized))
            return;

        OnPropertyChanged(nameof(NormalizedDataHex));
        OnPropertyChanged(nameof(DataBytes));
        OnPropertyChanged(nameof(ByteCountText));
        OnPropertyChanged(nameof(DataHexDisplay));

        if (saveWhenComplete && normalized.Length == 32)
            SaveToFile();
    }

    private void SaveToFile()
    {
        string? path = ResolveAbsolutePath();
        if (path is null)
            return;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, Convert.FromHexString(NormalizedDataHex));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (FormatException)
        {
        }
    }

    private void SaveMetadata()
    {
        string? path = ResolveMetadataPath();
        if (path is null)
            return;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var metadata = CreateMetadata();
            File.WriteAllText(path, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    private string? ResolveMetadataPath()
    {
        string? path = ResolveAbsolutePath();
        return path is null ? null : $"{path}.meta.json";
    }

    private static string NormalizeHex(string value)
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

    private WaveMemoryMetadataDocument CreateMetadata()
    {
        return new WaveMemoryMetadataDocument
        {
            AssetId = AssetId,
            Label = Label,
            Note = Note,
            DataFormat = DataFormat,
            Size = Size
        };
    }
}
