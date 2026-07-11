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
public enum PlaybackSelectionSource
{
    SongHeader,
    SongTable
}

public enum VoiceTableKind
{
    VoiceGroup,
    KeySplit,
    DrumSet
}

public readonly record struct VoiceTableSelection(
    VoiceTableKind Kind,
    ObservableCollection<VoiceRow> Rows,
    IList<VoiceProjectInfo> Voices,
    bool UpdateUsage);

public interface ITableRowVisualState
{
    bool IsPointerOver { get; set; }
}

internal static class TableRowVisuals
{
    private static readonly SolidColorBrush Normal = new(Color.Parse("#262626"));
    private static readonly SolidColorBrush PointerOver = new(Color.Parse("#303030"));
    private static readonly SolidColorBrush Selected = new(Color.Parse("#3A3A3A"));

    public static void SetLightTheme(bool isLight)
    {
        Normal.Color = Color.Parse(isLight ? "#FFFFFF" : "#262626");
        PointerOver.Color = Color.Parse(isLight ? "#ECECEC" : "#303030");
        Selected.Color = Color.Parse(isLight ? "#D7E2F7" : "#3A3A3A");
    }

    public static IBrush GetBackground(bool isSelected, bool isPointerOver)
    {
        if (isSelected)
            return Selected;

        return isPointerOver ? PointerOver : Normal;
    }
}
