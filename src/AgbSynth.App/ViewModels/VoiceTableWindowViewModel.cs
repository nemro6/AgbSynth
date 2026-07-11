using System.Collections.Generic;
using System.Collections.ObjectModel;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed class VoiceTableWindowViewModel
{
    public VoiceTableWindowViewModel(string title, string headerText, string filePath, IEnumerable<VoiceProjectInfo> voices)
    {
        Title = title;
        HeaderText = headerText;
        FilePath = filePath;
        foreach (var voice in voices)
            Rows.Add(VoiceTableRow.FromProjectInfo(voice));
    }

    public string Title { get; }
    public string HeaderText { get; }
    public string FilePath { get; }
    public ObservableCollection<VoiceTableRow> Rows { get; } = new();
}

public sealed class VoiceTableRow
{
    public int Index { get; init; }
    public string IndexHex => $"0x{Index:X2}";
    public int Type { get; init; }
    public string TypeHex => $"0x{Type:X2}";
    public string TypeName { get; set; } = string.Empty;
    public string DataPointer { get; set; } = string.Empty;
    public int? DataOffset { get; init; }
    public string DataOffsetHex => DataOffset is int offset ? $"0x{offset:X}" : string.Empty;
    public int Key { get; init; }
    public int Attack { get; set; }
    public int Decay { get; set; }
    public int Sustain { get; set; }
    public int Release { get; set; }
    public uint? SampleSize { get; init; }

    public static VoiceTableRow FromProjectInfo(VoiceProjectInfo voice)
    {
        return new VoiceTableRow
        {
            Index = voice.Index,
            Type = voice.Type,
            TypeName = voice.TypeName,
            DataPointer = voice.DataPointer,
            DataOffset = voice.DataOffset,
            Key = voice.Key,
            Attack = voice.Attack,
            Decay = voice.Decay,
            Sustain = voice.Sustain,
            Release = voice.Release,
            SampleSize = voice.Sample?.Size
        };
    }
}
