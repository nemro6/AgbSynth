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
public sealed class VoiceGroupOption : INotifyPropertyChanged, INotifyPropertyChanging
{
    private string _label = string.Empty;

    public int Id { get; init; }
    public string AssetId { get; init; } = string.Empty;
    public string Label
    {
        get => _label;
        set
        {
            if (!SetField(ref _label, value ?? string.Empty))
                return;

            if (Source is not null)
                Source.Label = _label;
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(FileDisplay));
        }
    }
    public string Pointer { get; init; } = string.Empty;
    public int Offset { get; init; }
    public string OffsetHex => $"0x{Offset:X}";
    public string FilePath { get; init; } = string.Empty;
    public string DiscoverySource { get; init; } = "Referenced";
    public string UsedBySongs { get; init; } = string.Empty;
    public int VoiceCount => Voices.Count;
    public List<VoiceProjectInfo> Voices { get; init; } = new();
    public VoiceGroupProjectInfo? Source { get; init; }

    public string Display => $"VoiceGroup {Id:D3} ({VoiceCount} voices, {DiscoverySource})";
    public string FileDisplay => FormatFileDisplay(Label, FilePath);

    public static VoiceGroupOption FromProjectInfo(VoiceGroupProjectInfo voiceGroup)
    {
        return new VoiceGroupOption
        {
            Id = voiceGroup.Id,
            AssetId = voiceGroup.AssetId,
            Label = voiceGroup.Label,
            Pointer = voiceGroup.Pointer,
            Offset = voiceGroup.Offset,
            FilePath = voiceGroup.FilePath,
            DiscoverySource = voiceGroup.DiscoverySource,
            UsedBySongs = string.Join(", ", voiceGroup.UsedBySongIds),
            Voices = voiceGroup.Voices,
            Source = voiceGroup
        };
    }

    public override string ToString() => Display;

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

    private static string FormatFileDisplay(string label, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
    }
}

public sealed class KeySplitOption : INotifyPropertyChanged, INotifyPropertyChanging
{
    private string _label = string.Empty;

    public int Id { get; set; }
    public string AssetId { get; init; } = string.Empty;
    public string Label
    {
        get => _label;
        set
        {
            if (!SetField(ref _label, value ?? string.Empty))
                return;

            KeySplit.Label = _label;
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(FileDisplay));
        }
    }
    public string FilePath { get; init; } = string.Empty;
    public int VoiceGroupId { get; init; }
    public int ParentVoiceIndex { get; init; }
    public KeySplitProjectInfo KeySplit { get; init; } = new();
    public int RegionCount => KeySplit.Regions.Count;
    public string Display => $"KeySplit {Id:D3} ({RegionCount} regions)";
    public string FileDisplay => FormatFileDisplay(Label, FilePath);

    public static KeySplitOption FromProjectInfo(KeySplitAssetProjectInfo asset)
    {
        return new KeySplitOption
        {
            Id = asset.Id,
            AssetId = asset.AssetId,
            Label = string.IsNullOrWhiteSpace(asset.Label) ? Path.GetFileNameWithoutExtension(asset.FilePath) : asset.Label,
            FilePath = asset.FilePath,
            VoiceGroupId = asset.VoiceGroupId,
            ParentVoiceIndex = asset.ParentVoiceIndex,
            KeySplit = asset.KeySplit
        };
    }

    public override string ToString() => Display;

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

    private static string FormatFileDisplay(string label, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
    }
}

public sealed class DrumSetOption : INotifyPropertyChanged, INotifyPropertyChanging
{
    private string _label = string.Empty;

    public int Id { get; set; }
    public string AssetId { get; init; } = string.Empty;
    public string Label
    {
        get => _label;
        set
        {
            if (!SetField(ref _label, value ?? string.Empty))
                return;

            DrumSet.Label = _label;
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(FileDisplay));
        }
    }
    public string FilePath { get; init; } = string.Empty;
    public int VoiceGroupId { get; init; }
    public int ParentVoiceIndex { get; init; }
    public DrumSetProjectInfo DrumSet { get; init; } = new();
    public int EntryCount => DrumSet.Entries.Count;
    public string Display => $"DrumSet {Id:D3} ({EntryCount} entries)";
    public string FileDisplay => FormatFileDisplay(Label, FilePath);

    public static DrumSetOption FromProjectInfo(DrumSetAssetProjectInfo asset)
    {
        return new DrumSetOption
        {
            Id = asset.Id,
            AssetId = asset.AssetId,
            Label = string.IsNullOrWhiteSpace(asset.Label) ? Path.GetFileNameWithoutExtension(asset.FilePath) : asset.Label,
            FilePath = asset.FilePath,
            VoiceGroupId = asset.VoiceGroupId,
            ParentVoiceIndex = asset.ParentVoiceIndex,
            DrumSet = asset.DrumSet
        };
    }

    public override string ToString() => Display;

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

    private static string FormatFileDisplay(string label, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label;
        return string.IsNullOrWhiteSpace(fileName) ? displayLabel : $"{displayLabel} ({fileName})";
    }
}
