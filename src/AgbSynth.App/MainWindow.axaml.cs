using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgbSynth.App.Controls;
using AgbSynth.App.MP2K;
using AgbSynth.App.ViewModels;

namespace AgbSynth.App;

public partial class MainWindow : Window
{
    private const double SideNavFirstTop = 6;
    private const double SideNavStep = 46;
    private const double SideNavAnimationMilliseconds = 220;
    private readonly MainWindowViewModel _viewModel = new();
    private DispatcherTimer? _sideNavAnimationTimer;
    private SongTableEntryRow? _draggedSongTableEntry;
    private VoiceRow? _baseKeyDragVoice;
    private double _baseKeyDragStartX;
    private int _baseKeyDragStartKey;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SettingsPage.MidiCcMappingRequested += OnMidiCcMappingClicked;
        Piano.NoteOn += (_, note) =>
        {
            if (MainTabs.SelectedIndex == 6)
                _viewModel.PreviewWaveMemoryKeyboardNoteOn(note, velocity: 110);
            else if (MainTabs.SelectedIndex == 7)
                _viewModel.PreviewWaveDataKeyboardNoteOn(note, velocity: 110);
            else
                _viewModel.PreviewKeyboardNoteOn(note, velocity: 110);
        };
        Piano.NoteOff += (_, note) =>
        {
            if (MainTabs.SelectedIndex == 6)
                _viewModel.PreviewWaveMemoryKeyboardNoteOff(note);
            else if (MainTabs.SelectedIndex == 7)
                _viewModel.PreviewWaveDataKeyboardNoteOff(note);
            else
                _viewModel.PreviewKeyboardNoteOff(note);
        };
        _viewModel.InitializeUserSettings();
        ApplyThemeVariant();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        MoveSideNavIndicator(MainTabs.SelectedIndex, animate: false);
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            SettingsPage.MidiCcMappingRequested -= OnMidiCcMappingClicked;
            _viewModel.CloseMidiInput();
            _ = _viewModel.StopSelectedSequencePlaybackAsync();
            _viewModel.DisposePlayback();
        };
        _ = _viewModel.RefreshMidiInputsAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedThemeColor))
            ApplyThemeVariant();
    }

    private void ApplyThemeVariant()
    {
        if (Application.Current is null)
            return;

        string? themeKey = _viewModel.SelectedThemeColor?.Key;
        Application.Current.RequestedThemeVariant = themeKey switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        TableRowVisuals.SetLightTheme(themeKey == "Light");
    }

    private async void OnOpenRomClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open GBA ROM",
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("GBA ROM (*.gba)") { Patterns = new[] { "*.gba" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0)
            return;

        if (!await _viewModel.LoadRomAsync(files[0]))
            return;

        Title = $"{files[0].Name} - AgbSynth";

        if (_viewModel.LoadedRom is null)
            return;

        var addressWindow = new SongTableAddressWindow(_viewModel.LoadedRom.Length);
        bool accepted = await addressWindow.ShowDialog<bool>(this);
        if (!accepted)
            return;

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AgbSynth Project",
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(files[0].Name)}.agbsynth",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("AgbSynth project (*.agbsynth)") { Patterns = new[] { "*.agbsynth" } },
                FilePickerFileTypes.All
            }
        });

        string? outputPath = saveFile?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;
        if (!string.Equals(Path.GetExtension(outputPath), ".agbsynth", System.StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".agbsynth");

        await _viewModel.CreateProjectFileAsync(
            outputPath,
            new Project.Mp2kImportOptions
            {
                ReadMode = addressWindow.ReadMode,
                SongTableOffset = addressWindow.SongTableOffset,
                SongTableAddressText = addressWindow.SongTableAddressText ?? string.Empty,
                IncludeUnreferencedVoiceGroups = addressWindow.IncludeUnreferencedVoiceGroups
            });
    }

    private async void OnNewProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
            return;

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create AgbSynth Project",
            SuggestedFileName = "NewProject.agbsynth",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("AgbSynth project (*.agbsynth)") { Patterns = new[] { "*.agbsynth" } },
                FilePickerFileTypes.All
            }
        });

        string? outputPath = saveFile?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;
        if (!string.Equals(Path.GetExtension(outputPath), ".agbsynth", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".agbsynth");

        if (await _viewModel.CreateBlankProjectAsync(outputPath))
            Title = $"{Path.GetFileName(outputPath)} - AgbSynth";
    }

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open AgbSynth Project",
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("AgbSynth project (*.agbsynth)") { Patterns = new[] { "*.agbsynth" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0)
            return;

        string? projectPath = files[0].Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(projectPath))
            return;

        if (await _viewModel.LoadProjectFileAsync(projectPath))
            Title = $"{Path.GetFileName(projectPath)} - AgbSynth";
    }

    private async void OnRefreshProjectClicked(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshProjectAsync();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Control control &&
            (control is Button ||
             control is Menu ||
             control is MenuItem ||
             control.FindAncestorOfType<Button>() is not null ||
             control.FindAncestorOfType<Menu>() is not null ||
             control.FindAncestorOfType<MenuItem>() is not null))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 9;
        MoveSideNavIndicator(MainTabs.SelectedIndex, animate: true);
    }

    private void OnMainTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The keyboard is a voice audition tool, not a global transport control.
        if (PreviewKeyboardHost is null || SideNavSelectionIndicator is null)
            return;

        bool isVoiceEditorPage = MainTabs.SelectedIndex is 3 or 4 or 5 or 6 or 7;
        bool isKeySplitPage = MainTabs.SelectedIndex == 4;
        bool isDrumSetPage = MainTabs.SelectedIndex == 5;
        PreviewKeyboardHost.IsVisible = isVoiceEditorPage;
        if (Piano is not null)
        {
            Piano.ShowKeySplitRanges = isKeySplitPage;
            Piano.ShowMappedNotes = isDrumSetPage;
            Piano.Height = 104;
            Piano.MinNote = 0;
            Piano.MaxNote = 127;
        }

        MoveSideNavIndicator(MainTabs.SelectedIndex, animate: true);
    }

    private void OnSideNavClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int index))
            return;

        MainTabs.SelectedIndex = index;
        MoveSideNavIndicator(index, animate: true);
    }

    private void MoveSideNavIndicator(int selectedIndex, bool animate)
    {
        if (SideNavSelectionIndicator is null)
            return;

        bool hasVisibleNavItem = selectedIndex is >= 0 and <= 8;
        SideNavSelectionIndicator.IsVisible = hasVisibleNavItem;
        if (!hasVisibleNavItem)
            return;

        double targetTop = SideNavFirstTop + selectedIndex * SideNavStep;
        double currentTop = Canvas.GetTop(SideNavSelectionIndicator);
        if (double.IsNaN(currentTop))
            currentTop = targetTop;

        _sideNavAnimationTimer?.Stop();
        _sideNavAnimationTimer = null;

        if (!animate || Math.Abs(currentTop - targetTop) < 0.5)
        {
            Canvas.SetTop(SideNavSelectionIndicator, targetTop);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            double progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / SideNavAnimationMilliseconds, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 3);
            Canvas.SetTop(SideNavSelectionIndicator, currentTop + (targetTop - currentTop) * eased);

            if (progress < 1)
                return;

            timer.Stop();
            Canvas.SetTop(SideNavSelectionIndicator, targetTop);
            if (ReferenceEquals(_sideNavAnimationTimer, timer))
                _sideNavAnimationTimer = null;
        };

        _sideNavAnimationTimer = timer;
        timer.Start();
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClicked(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnSequencePlayClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetPlaybackSelectionSourceFromTabIndex(MainTabs.SelectedIndex);
        await _viewModel.PlaySelectedSequenceAsync();
    }

    private void OnPlayAreaMidiResetClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetMidiDefaults();
    }

    private async void OnPlayAreaPreviousClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetPlaybackSelectionSourceFromTabIndex(MainTabs.SelectedIndex);
        await _viewModel.PlayPreviousSequenceAsync();
    }

    private async void OnPlayAreaPlayStopClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetPlaybackSelectionSourceFromTabIndex(MainTabs.SelectedIndex);
        await _viewModel.TogglePlayAreaPlaybackAsync();
    }

    private async void OnPlayAreaNextClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetPlaybackSelectionSourceFromTabIndex(MainTabs.SelectedIndex);
        await _viewModel.PlayNextSequenceAsync();
    }

    private async void OnPlayAreaRecordClicked(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsRecording)
        {
            _viewModel.StartOutputRecording();
            PlayAreaRecordButton.Classes.Add("recording");
            return;
        }

        var recording = _viewModel.StopOutputRecording();
        PlayAreaRecordButton.Classes.Remove("recording");

        if (recording is null || !recording.HasSamples)
            return;

        if (StorageProvider is null)
            return;

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Recording",
            SuggestedFileName = "recording.wav",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Wave audio (*.wav)") { Patterns = new[] { "*.wav" } },
                FilePickerFileTypes.All
            }
        });

        string? outputPath = saveFile?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;
        if (!string.Equals(Path.GetExtension(outputPath), ".wav", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".wav");

        try
        {
            recording.Save(outputPath);
            _viewModel.NotifyRecordingSaved(Path.GetFileName(outputPath), recording.DurationSeconds);
        }
        catch (Exception ex)
        {
            _viewModel.NotifyRecordingSaveFailed(ex.Message);
        }
    }

    private void OnPlayAreaMuteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsMasterMuted = !_viewModel.IsMasterMuted;
    }

    private void OnSongTableMoveSelectedUpClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedSongTableEntryUp();
    }

    private void OnSongTableMoveSelectedDownClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedSongTableEntryDown();
    }

    private void OnSongTableInsertBelowSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertSongTableEntryBelowSelected(copySelected: false);
    }

    private void OnSongTableDuplicateSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertSongTableEntryBelowSelected(copySelected: true);
    }

    private void OnSongTableDeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedSongTableEntry();
    }

    private void OnSongTableAddEndClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.AddSongTableEntryToEnd();
    }

    private async void OnSongHeaderInsertBelowSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync(
            "Create SongHeader",
            _viewModel.GetSuggestedSongHeaderFileName(),
            ".agbsh",
            "AgbSynth SongHeader (*.agbsh)",
            _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.InsertSequenceBelowSelected(copySelected: false, path);
    }

    private async void OnMidiCcMappingClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new MidiCcMappingWindow(_viewModel.MidiCcMapping);
        MidiCcMapping? mapping = await dialog.ShowDialog<MidiCcMapping?>(this);
        if (mapping is not null)
            _viewModel.UpdateMidiCcMapping(mapping);
    }

    private async void OnSongHeaderDuplicateSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync(
            "Save SongHeader Copy",
            _viewModel.GetSuggestedSongHeaderFileName(),
            ".agbsh",
            "AgbSynth SongHeader (*.agbsh)",
            _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.InsertSequenceBelowSelected(copySelected: true, path);
    }

    private void OnSongHeaderDeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedSequence();
    }

    private async void OnSongHeaderAddEndClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync(
            "Create SongHeader",
            _viewModel.GetSuggestedSongHeaderFileName(),
            ".agbsh",
            "AgbSynth SongHeader (*.agbsh)",
            _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.AddSequenceToEnd(path);
    }

    private async void OnWaveMemoryInsertBelowSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create WaveMemory", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveMemoryBelowSelected(copySelected: false, path);
    }

    private async void OnWaveMemoryDuplicateSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save WaveMemory Copy", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveMemoryBelowSelected(copySelected: true, path);
    }

    private void OnWaveMemoryDeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedWaveMemory();
    }

    private async void OnWaveMemoryAddEndClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create WaveMemory", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.AddWaveMemoryToEnd(path);
    }

    private async void OnWaveDataInsertBelowSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create Voice", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveDataBelowSelected(copySelected: false, path);
    }

    private async void OnWaveDataDuplicateSelectedClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save Voice Copy", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveDataBelowSelected(copySelected: true, path);
    }

    private void OnWaveDataDeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedWaveData();
    }

    private async void OnWaveDataAddEndClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create Voice", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.AddWaveDataToEnd(path);
    }

    private void OnSongHeaderRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SequenceHeaderRow row })
            _viewModel.SelectedSequence = row;
    }

    private void OnWaveMemoryRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WaveMemoryRow row })
            _viewModel.SelectedWaveMemory = row;
    }

    private void OnWaveDataRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WaveDataRow row })
            _viewModel.SelectedWaveData = row;
    }

    private void OnSongTableRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SongTableEntryRow row })
            _viewModel.SelectedSongTableEntry = row;
    }

    private static void OnTableRowPointerEntered(object? sender, PointerEventArgs e)
    {
        SetTableRowPointerOver(sender, isPointerOver: true);
    }

    private static void OnTableRowPointerExited(object? sender, PointerEventArgs e)
    {
        SetTableRowPointerOver(sender, isPointerOver: false);
    }

    private static void SetTableRowPointerOver(object? sender, bool isPointerOver)
    {
        if (sender is Control { DataContext: ITableRowVisualState row })
            row.IsPointerOver = isPointerOver;
    }

    private async void OnSongTableSelectHeaderClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || GetSongTableRow(sender) is not { } row)
            return;

        _viewModel.SelectedSongTableEntry = row;
        IStorageFolder? startLocation = null;
        string? songHeaderDirectory = _viewModel.GetSongHeaderDirectoryPath();
        if (!string.IsNullOrWhiteSpace(songHeaderDirectory) && Directory.Exists(songHeaderDirectory))
        {
            try
            {
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(songHeaderDirectory)));
            }
            catch
            {
                startLocation = null;
            }
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select SongHeader",
            SuggestedStartLocation = startLocation,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("AgbSynth SongHeader (*.agbsh)") { Patterns = new[] { "*.agbsh" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.Count == 0 ? null : files[0].Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.SetSongTableEntrySongHeaderFile(row, path);
    }

    private async void OnSongHeaderSelectMidiClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || GetSongHeaderRow(sender) is not { } row)
            return;

        _viewModel.SelectedSequence = row;
        IStorageFolder? startLocation = await TryGetStorageFolderAsync(_viewModel.GetMidiDirectoryPath());
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select MIDI",
            SuggestedStartLocation = startLocation,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("MIDI (*.mid)") { Patterns = new[] { "*.mid", "*.midi" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.Count == 0 ? null : files[0].Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.SetSequenceMidiFile(row, path);
    }

    private async void OnSongHeaderSelectVoiceGroupClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || GetSongHeaderRow(sender) is not { } row)
            return;

        _viewModel.SelectedSequence = row;
        IStorageFolder? startLocation = await TryGetStorageFolderAsync(_viewModel.GetVoiceGroupDirectoryPath());
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select VoiceGroup",
            SuggestedStartLocation = startLocation,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("AgbSynth VoiceGroup (*.agbvg)") { Patterns = new[] { "*.agbvg" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.Count == 0 ? null : files[0].Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.SetSequenceVoiceGroupFile(row, path);
    }

    private async Task<IStorageFolder?> TryGetStorageFolderAsync(string? directory)
    {
        if (StorageProvider is null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(directory)));
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> PickNewAssetPathAsync(
        string title,
        string suggestedFileName,
        string extension,
        string fileTypeName,
        string? directory)
    {
        if (StorageProvider is null)
            return null;

        IStorageFolder? startLocation = await TryGetStorageFolderAsync(directory);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = startLocation,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(fileTypeName) { Patterns = new[] { $"*{extension}" } },
                FilePickerFileTypes.All
            }
        });

        string? path = file?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, extension);
    }

    private void OnSongTableNumberTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        string current = textBox.Text ?? string.Empty;
        string digits = new(current.Where(char.IsDigit).ToArray());
        if (digits.Length > 0 && int.TryParse(digits, out int value) && value > 255)
            digits = "255";

        if (current == digits)
            return;

        int caret = Math.Min(textBox.CaretIndex, digits.Length);
        textBox.Text = digits;
        textBox.CaretIndex = caret;
    }

    private async void OnSongTableDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SongTableEntryRow row } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _viewModel.SelectedSongTableEntry = row;
        _draggedSongTableEntry = row;
        var data = new DataObject();
        data.Set(DataFormats.Text, row.IndexText);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        _draggedSongTableEntry = null;
    }

    private void OnSongTableRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: SongTableEntryRow target })
        {
            _viewModel.MoveSongTableEntryBefore(_draggedSongTableEntry, target);
            e.Handled = true;
        }
    }

    private void OnSongTableRowMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSongTableEntryUp(GetSongTableRow(sender));
    }

    private void OnSongTableRowMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSongTableEntryDown(GetSongTableRow(sender));
    }

    private void OnSongTableRowInsertAboveClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertSongTableEntryAbove(GetSongTableRow(sender));
    }

    private void OnSongTableRowInsertBelowClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertSongTableEntryBelow(GetSongTableRow(sender));
    }

    private void OnSongTableRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopySongTableEntry(GetSongTableRow(sender));
    }

    private void OnSongTableRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.PasteSongTableEntryBelow(GetSongTableRow(sender));
    }

    private void OnSongTableRowDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSongTableEntry(GetSongTableRow(sender));
    }

    private void OnSongTableRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
            return;

        Control? owner = control.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c.ContextMenu is not null);
        if (owner?.ContextMenu is { } menu)
            menu.Open(owner);
    }

    private async void OnSongHeaderRowInsertAboveClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create SongHeader", _viewModel.GetSuggestedSongHeaderFileName(), ".agbsh", "AgbSynth SongHeader (*.agbsh)", _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.InsertSequenceAbove(GetSongHeaderRow(sender), path);
    }

    private async void OnSongHeaderRowInsertBelowClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create SongHeader", _viewModel.GetSuggestedSongHeaderFileName(), ".agbsh", "AgbSynth SongHeader (*.agbsh)", _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.InsertSequenceBelow(GetSongHeaderRow(sender), path);
    }

    private void OnSongHeaderRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopySequence(GetSongHeaderRow(sender));
    }

    private async void OnSongHeaderRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save SongHeader Copy", _viewModel.GetSuggestedSongHeaderFileName(), ".agbsh", "AgbSynth SongHeader (*.agbsh)", _viewModel.GetSongHeaderDirectoryPath());
        if (path is not null)
            _viewModel.PasteSequenceBelow(GetSongHeaderRow(sender), path);
    }

    private void OnSongHeaderRowDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSequence(GetSongHeaderRow(sender));
    }

    private void OnSongHeaderRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        OnSongTableRowMoreClicked(sender, e);
    }

    private async void OnWaveMemoryRowInsertAboveClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create WaveMemory", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveMemoryAbove(GetWaveMemoryRow(sender), path);
    }

    private async void OnWaveMemoryRowInsertBelowClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create WaveMemory", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveMemoryBelow(GetWaveMemoryRow(sender), path);
    }

    private void OnWaveMemoryRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopyWaveMemory(GetWaveMemoryRow(sender));
    }

    private async void OnWaveMemoryRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save WaveMemory Copy", _viewModel.GetSuggestedWaveMemoryFileName(), ".agbwm", "AgbSynth WaveMemory (*.agbwm)", _viewModel.GetWaveMemoryDirectoryPath());
        if (path is not null)
            _viewModel.PasteWaveMemoryBelow(GetWaveMemoryRow(sender), path);
    }

    private void OnWaveMemoryRowDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteWaveMemory(GetWaveMemoryRow(sender));
    }

    private void OnWaveMemoryRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        OnSongTableRowMoreClicked(sender, e);
    }

    private async void OnWaveDataRowInsertAboveClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create Voice", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveDataAbove(GetWaveDataRow(sender), path);
    }

    private async void OnWaveDataRowInsertBelowClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create Voice", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.InsertWaveDataBelow(GetWaveDataRow(sender), path);
    }

    private void OnWaveDataRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopyWaveData(GetWaveDataRow(sender));
    }

    private async void OnWaveDataRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save Voice Copy", _viewModel.GetSuggestedWaveDataFileName(), ".agbwd", "AgbSynth Voice (*.agbwd)", _viewModel.GetWaveDataDirectoryPath());
        if (path is not null)
            _viewModel.PasteWaveDataBelow(GetWaveDataRow(sender), path);
    }

    private void OnWaveDataRowDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteWaveData(GetWaveDataRow(sender));
    }

    private void OnWaveDataRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        OnSongTableRowMoreClicked(sender, e);
    }

    private async void OnVoiceGroupAddClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create VoiceGroup", _viewModel.GetSuggestedVoiceGroupFileName(), ".agbvg", "AgbSynth VoiceGroup (*.agbvg)", _viewModel.GetVoiceGroupDirectoryPath());
        if (path is not null)
            _viewModel.AddVoiceGroup(path);
    }

    private async void OnVoiceGroupDuplicateClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save VoiceGroup Copy", _viewModel.GetSuggestedVoiceGroupFileName(), ".agbvg", "AgbSynth VoiceGroup (*.agbvg)", _viewModel.GetVoiceGroupDirectoryPath());
        if (path is not null)
            _viewModel.DuplicateSelectedVoiceGroup(path);
    }

    private void OnVoiceGroupDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedVoiceGroup();
    }

    private async void OnKeySplitAddClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create KeySplit", _viewModel.GetSuggestedKeySplitFileName(), ".agbks", "AgbSynth KeySplit (*.agbks)", _viewModel.GetKeySplitDirectoryPath());
        if (path is not null)
            _viewModel.AddKeySplit(path);
    }

    private async void OnKeySplitDuplicateClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save KeySplit Copy", _viewModel.GetSuggestedKeySplitFileName(), ".agbks", "AgbSynth KeySplit (*.agbks)", _viewModel.GetKeySplitDirectoryPath());
        if (path is not null)
            _viewModel.DuplicateSelectedKeySplit(path);
    }

    private void OnKeySplitDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedKeySplit();
    }

    private async void OnDrumSetAddClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Create DrumSet", _viewModel.GetSuggestedDrumSetFileName(), ".agbds", "AgbSynth DrumSet (*.agbds)", _viewModel.GetDrumSetDirectoryPath());
        if (path is not null)
            _viewModel.AddDrumSet(path);
    }

    private async void OnDrumSetDuplicateClicked(object? sender, RoutedEventArgs e)
    {
        string? path = await PickNewAssetPathAsync("Save DrumSet Copy", _viewModel.GetSuggestedDrumSetFileName(), ".agbds", "AgbSynth DrumSet (*.agbds)", _viewModel.GetDrumSetDirectoryPath());
        if (path is not null)
            _viewModel.DuplicateSelectedDrumSet(path);
    }

    private void OnDrumSetDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedDrumSet();
    }

    private void OnVoiceMoveSelectedUpClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedVoiceUp();
    }

    private void OnVoiceMoveSelectedDownClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveSelectedVoiceDown();
    }

    private void OnVoicePreviousClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SelectPreviousVoice();
    }

    private void OnVoiceNextClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.SelectNextVoice();
    }

    private void OnVoiceRowMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveVoiceUp(GetVoiceRow(sender));
    }

    private void OnVoiceRowMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.MoveVoiceDown(GetVoiceRow(sender));
    }

    private void OnVoiceRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopyVoice(GetVoiceRow(sender));
    }

    private void OnVoiceRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.PasteVoice(GetVoiceRow(sender));
    }

    private void OnVoiceRowResetClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetVoice(GetVoiceRow(sender));
    }

    private void OnVoiceRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        OnSongTableRowMoreClicked(sender, e);
    }

    private void OnKeySplitInsertBelowSelectedClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertKeySplitRegionBelowSelected(copySelected: false);
    }

    private void OnKeySplitDuplicateSelectedRegionClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertKeySplitRegionBelowSelected(copySelected: true);
    }

    private void OnKeySplitDeleteSelectedRegionClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedKeySplitRegion();
    }

    private void OnKeySplitRowInsertAboveClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertKeySplitRegionAbove(GetVoiceRow(sender));
    }

    private void OnKeySplitRowInsertBelowClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.InsertKeySplitRegionBelow(GetVoiceRow(sender));
    }

    private void OnKeySplitRowCopyClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.CopyKeySplitRegion(GetVoiceRow(sender));
    }

    private void OnKeySplitRowPasteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.PasteKeySplitRegionBelow(GetVoiceRow(sender));
    }

    private void OnKeySplitRowDeleteClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.DeleteKeySplitRegion(GetVoiceRow(sender));
    }

    private void OnKeySplitRowMoreClicked(object? sender, RoutedEventArgs e)
    {
        OnSongTableRowMoreClicked(sender, e);
    }
    private async void OnVoiceDataSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || GetVoiceRow(sender) is not { } row)
            return;

        _viewModel.SelectedVoice = row;
        string? dataDirectory = _viewModel.GetVoiceDataDirectoryPath(row);
        IStorageFolder? startLocation = await TryGetStorageFolderAsync(dataDirectory);
        var options = new FilePickerOpenOptions
        {
            Title = "Select Voice Data",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter = GetVoiceDataFileTypes(row)
        };

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return;

        string path = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        _viewModel.SetVoiceDataFile(row, path);
    }

    private void OnVoiceRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: VoiceRow row })
            _viewModel.SelectedVoice = row;
    }

    private void OnVoiceBaseKeyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GetVoiceRow(sender) is not { } row || !row.IsBaseKeyEditable)
            return;

        _viewModel.SelectedVoice = row;
        _baseKeyDragVoice = row;
        _baseKeyDragStartX = e.GetPosition(this).X;
        _baseKeyDragStartKey = row.Key;
        if (sender is Control control)
            e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnVoiceBaseKeyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_baseKeyDragVoice is null || !_baseKeyDragVoice.IsBaseKeyEditable)
            return;

        double delta = e.GetPosition(this).X - _baseKeyDragStartX;
        _baseKeyDragVoice.Key = Math.Clamp(_baseKeyDragStartKey + (int)Math.Round(delta / 6.0), 0, 127);
        e.Handled = true;
    }

    private void OnVoiceBaseKeyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _baseKeyDragVoice = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnWaveMemoryDataHexEdited(object? sender, WaveMemoryDataHexEditedEventArgs e)
    {
        _viewModel.SetSelectedWaveMemoryDataHex(e.DataHex);
        if (WaveMemoryBinaryDataText is not null)
            WaveMemoryBinaryDataText.Text = e.DataHex;
    }

    private void OnWaveDataLoopPointsEdited(object? sender, WaveDataLoopPointsEditedEventArgs e)
    {
        _viewModel.SetSelectedWaveDataLoopPoints(e.LoopStart, e.LoopEnd, e.Loops);
    }

    private static SongTableEntryRow? GetSongTableRow(object? sender)
    {
        if (sender is Control { DataContext: SongTableEntryRow row })
            return row;

        if (sender is Control control)
        {
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is SongTableEntryRow ancestorRow)
                    return ancestorRow;
            }
        }

        return null;
    }

    private static SequenceHeaderRow? GetSongHeaderRow(object? sender)
    {
        if (sender is Control { DataContext: SequenceHeaderRow row })
            return row;

        if (sender is Control control)
        {
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is SequenceHeaderRow ancestorRow)
                    return ancestorRow;
            }
        }

        return null;
    }

    private static VoiceRow? GetVoiceRow(object? sender)
    {
        if (sender is Control { DataContext: VoiceRow row })
            return row;

        if (sender is Control control)
        {
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is VoiceRow ancestorRow)
                    return ancestorRow;
            }
        }

        return null;
    }

    private static WaveMemoryRow? GetWaveMemoryRow(object? sender)
    {
        if (sender is Control { DataContext: WaveMemoryRow row })
            return row;

        if (sender is Control control)
        {
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is WaveMemoryRow ancestorRow)
                    return ancestorRow;
            }
        }

        return null;
    }

    private static WaveDataRow? GetWaveDataRow(object? sender)
    {
        if (sender is Control { DataContext: WaveDataRow row })
            return row;

        if (sender is Control control)
        {
            foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            {
                if (ancestor.DataContext is WaveDataRow ancestorRow)
                    return ancestorRow;
            }
        }

        return null;
    }

    private static IReadOnlyList<FilePickerFileType> GetVoiceDataFileTypes(VoiceRow row)
    {
        return row.DataAssetDirectoryName switch
        {
            "wavedata" => new[]
            {
                new FilePickerFileType("AgbSynth Voice / WaveData (*.agbwd)") { Patterns = new[] { "*.agbwd" } },
                FilePickerFileTypes.All
            },
            "wavememory" => new[]
            {
                new FilePickerFileType("AgbSynth WaveMemory (*.agbwm)") { Patterns = new[] { "*.agbwm" } },
                FilePickerFileTypes.All
            },
            "keysplit" => new[]
            {
                new FilePickerFileType("AgbSynth KeySplit (*.agbks)") { Patterns = new[] { "*.agbks" } },
                FilePickerFileTypes.All
            },
            "drumset" => new[]
            {
                new FilePickerFileType("AgbSynth DrumSet (*.agbds)") { Patterns = new[] { "*.agbds" } },
                FilePickerFileTypes.All
            },
            _ => new[] { FilePickerFileTypes.All }
        };
    }

    private void OnSequencePauseClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel.ToggleSelectedSequencePause();
    }

    private async void OnSequenceStopClicked(object? sender, RoutedEventArgs e)
    {
        await _viewModel.StopSelectedSequencePlaybackAsync();
    }

    private void OnVoiceDoubleTapped(object? sender, TappedEventArgs e)
    {
        var row = GetVoiceRow(sender) ?? _viewModel.SelectedVoice;
        if (row is null)
            return;

        if (row.DrumSet is not null)
        {
            var vm = new VoiceTableWindowViewModel(
                $"DrumSet - Voice {row.Index:D3}",
                $"DrumSet Voice {row.IndexHex}",
                row.DataFilePath,
                row.DrumSet.Entries);
            new VoiceTableWindow { DataContext = vm }.Show(this);
            return;
        }

        if (row.KeySplit is not null)
        {
            var vm = new VoiceTableWindowViewModel(
                $"KeySplit - Voice {row.Index:D3}",
                $"KeySplit Voice {row.IndexHex}",
                row.DataFilePath,
                row.KeySplit.Regions);
            new VoiceTableWindow { DataContext = vm }.Show(this);
        }
    }
}
