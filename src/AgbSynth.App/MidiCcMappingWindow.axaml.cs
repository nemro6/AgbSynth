using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgbSynth.App.MP2K;

namespace AgbSynth.App;

public partial class MidiCcMappingWindow : Window
{
    public MidiCcMappingWindow()
        : this(MidiCcMapping.Default)
    {
    }

    public MidiCcMappingWindow(MidiCcMapping mapping)
    {
        InitializeComponent();
        DataContext = this;
        SetValues(mapping);
    }

    public ObservableCollection<MidiCcMappingRow> StandardMappings { get; } = [];
    public ObservableCollection<MidiCcMappingRow> ExtendedMappings { get; } = [];

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnDefaultsClicked(object? sender, RoutedEventArgs e)
    {
        SetValues(MidiCcMapping.Default);
        ErrorTextBlock.Text = string.Empty;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close((MidiCcMapping?)null);

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        MidiCcMapping mapping = MidiCcMapping.Default;
        foreach (MidiCcMappingRow row in StandardMappings.Concat(ExtendedMappings))
        {
            if (!int.TryParse(row.ControllerText, out int controller) || controller is < 0 or > 127)
            {
                ErrorTextBlock.Text = $"{row.Name} must be an integer from 0 to 127.";
                return;
            }
            mapping.SetController(row.Key, controller);
        }

        if (!mapping.TryValidate(out string? error))
        {
            ErrorTextBlock.Text = error;
            return;
        }

        Close(mapping);
    }

    private void SetValues(MidiCcMapping mapping)
    {
        StandardMappings.Clear();
        ExtendedMappings.Clear();
        foreach (MidiCcEntry entry in mapping.GetStandardEntries())
            StandardMappings.Add(MidiCcMappingRow.FromEntry(entry));
        foreach (MidiCcEntry entry in mapping.GetExtendedEntries())
            ExtendedMappings.Add(MidiCcMappingRow.FromEntry(entry));
    }
}

public sealed class MidiCcMappingRow
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string ControllerText { get; set; } = string.Empty;

    public static MidiCcMappingRow FromEntry(MidiCcEntry entry) => new()
    {
        Key = entry.Key,
        Name = entry.Name,
        Description = entry.Description,
        ControllerText = entry.Controller.ToString()
    };
}
