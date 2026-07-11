using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace AgbSynth.App.Controls;

public sealed class WaveDataEditorControl : Control
{
    public static readonly StyledProperty<string> DataHexProperty =
        AvaloniaProperty.Register<WaveDataEditorControl, string>(
            nameof(DataHex),
            string.Empty,
            defaultBindingMode: BindingMode.OneWay);

    public static readonly StyledProperty<int> LoopStartProperty =
        AvaloniaProperty.Register<WaveDataEditorControl, int>(
            nameof(LoopStart),
            0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> LoopEndProperty =
        AvaloniaProperty.Register<WaveDataEditorControl, int>(
            nameof(LoopEnd),
            0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLoopingProperty =
        AvaloniaProperty.Register<WaveDataEditorControl, bool>(
            nameof(IsLooping),
            false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<WaveDataEditorControl, bool>(nameof(IsEditable), true);

    private const double MarkerHitWidth = 8.0;
    private DragMarker _dragMarker = DragMarker.None;

    public event EventHandler<WaveDataLoopPointsEditedEventArgs>? LoopPointsEdited;

    public string DataHex
    {
        get => GetValue(DataHexProperty);
        set => SetValue(DataHexProperty, value ?? string.Empty);
    }

    public int LoopStart
    {
        get => GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, Math.Max(0, value));
    }

    public int LoopEnd
    {
        get => GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, Math.Max(0, value));
    }

    public bool IsLooping
    {
        get => GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    public WaveDataEditorControl()
    {
        Focusable = true;
        MinHeight = 24;
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataHexProperty ||
                e.Property == LoopStartProperty ||
                e.Property == LoopEndProperty ||
                e.Property == IsLoopingProperty ||
                e.Property.Name is "ActualThemeVariant" or "RequestedThemeVariant")
            {
                InvalidateVisual();
            }
        };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _dragMarker = DragMarker.None;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        context.FillRectangle(ThemeDrawingPalette.Canvas(this), rect);
        context.DrawRectangle(new Pen(ThemeDrawingPalette.Border(this), 1), rect.Deflate(0.5));

        byte[] samples = DecodePcm(DataHex);
        if (samples.Length == 0 || rect.Width < 4 || rect.Height < 4)
            return;

        var plot = new Rect(rect.X + 5, rect.Y + 4, Math.Max(1, rect.Width - 10), Math.Max(1, rect.Height - 8));
        double mid = plot.Center.Y;
        context.DrawLine(new Pen(ThemeDrawingPalette.Grid(this), 1), new Point(plot.Left, mid), new Point(plot.Right, mid));

        if (IsLooping)
        {
            double startX = SampleToX(plot, LoopStart, samples.Length);
            double endX = SampleToX(plot, LoopEnd, samples.Length);
            if (endX > startX)
            {
                context.FillRectangle(new SolidColorBrush(Color.Parse("#233526"), 0.65), new Rect(startX, plot.Top, endX - startX, plot.Height));
            }
        }

        DrawWaveform(context, samples, plot);

        if (IsLooping)
        {
            DrawMarker(context, SampleToX(plot, LoopStart, samples.Length), plot, "#F0C66A");
            DrawMarker(context, SampleToX(plot, LoopEnd, samples.Length), plot, "#EF8F6B");
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEditable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        byte[] samples = DecodePcm(DataHex);
        if (samples.Length == 0)
            return;

        Focus();
        IsLooping = true;
        var point = e.GetPosition(this);
        var plot = GetPlotRect();
        double startX = SampleToX(plot, LoopStart, samples.Length);
        double endX = SampleToX(plot, LoopEnd, samples.Length);
        _dragMarker = Math.Abs(point.X - startX) <= Math.Abs(point.X - endX)
            ? DragMarker.Start
            : DragMarker.End;

        ApplyPointerEdit(point, samples.Length);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMarker == DragMarker.None)
            return;

        byte[] samples = DecodePcm(DataHex);
        if (samples.Length == 0)
            return;

        ApplyPointerEdit(e.GetPosition(this), samples.Length);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragMarker == DragMarker.None)
            return;

        _dragMarker = DragMarker.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ApplyPointerEdit(Point point, int sampleCount)
    {
        int sample = XToSample(GetPlotRect(), point.X, sampleCount);
        int start = Math.Clamp(LoopStart, 0, Math.Max(0, sampleCount - 1));
        int end = Math.Clamp(LoopEnd <= 0 ? sampleCount : LoopEnd, 1, sampleCount);

        if (_dragMarker == DragMarker.Start)
            start = Math.Clamp(sample, 0, Math.Max(0, end - 1));
        else if (_dragMarker == DragMarker.End)
            end = Math.Clamp(sample, Math.Min(sampleCount, start + 1), sampleCount);

        SetCurrentValue(LoopStartProperty, start);
        SetCurrentValue(LoopEndProperty, end);
        SetCurrentValue(IsLoopingProperty, true);
        LoopPointsEdited?.Invoke(this, new WaveDataLoopPointsEditedEventArgs(start, end, true));
    }

    private Rect GetPlotRect()
    {
        var rect = new Rect(Bounds.Size);
        return new Rect(rect.X + 5, rect.Y + 4, Math.Max(1, rect.Width - 10), Math.Max(1, rect.Height - 8));
    }

    private static void DrawWaveform(DrawingContext context, byte[] samples, Rect plot)
    {
        int points = Math.Min(samples.Length, Math.Max(32, (int)plot.Width));
        if (points <= 1)
            return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (int i = 0; i < points; i++)
            {
                int sampleIndex = Math.Clamp((int)Math.Round(i / (double)(points - 1) * (samples.Length - 1)), 0, samples.Length - 1);
                double x = plot.Left + plot.Width * i / (points - 1);
                double normalized = unchecked((sbyte)samples[sampleIndex]) / 128.0;
                double y = plot.Center.Y - normalized * plot.Height * 0.46;
                if (i == 0)
                    g.BeginFigure(new Point(x, y), false);
                else
                    g.LineTo(new Point(x, y));
            }
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#8FB7FF")), 1.4), geometry);
    }

    private static void DrawMarker(DrawingContext context, double x, Rect plot, string color)
    {
        var brush = new SolidColorBrush(Color.Parse(color));
        context.DrawLine(new Pen(brush, 1.6), new Point(x, plot.Top), new Point(x, plot.Bottom));
        context.FillRectangle(brush, new Rect(x - 2, plot.Top, 4, 4));
    }

    private static double SampleToX(Rect plot, int sample, int sampleCount)
    {
        if (sampleCount <= 1)
            return plot.Left;
        return plot.Left + Math.Clamp(sample, 0, sampleCount) / (double)sampleCount * plot.Width;
    }

    private static int XToSample(Rect plot, double x, int sampleCount)
    {
        if (sampleCount <= 0)
            return 0;
        return Math.Clamp((int)Math.Round((x - plot.Left) / plot.Width * sampleCount), 0, sampleCount);
    }

    private static byte[] DecodePcm(string hex)
    {
        string normalized = new string((hex ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length % 2 != 0)
            normalized = normalized[..^1];
        if (normalized.Length == 0)
            return [];

        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private enum DragMarker
    {
        None,
        Start,
        End
    }
}

public sealed class WaveDataLoopPointsEditedEventArgs : EventArgs
{
    public WaveDataLoopPointsEditedEventArgs(int loopStart, int loopEnd, bool loops)
    {
        LoopStart = loopStart;
        LoopEnd = loopEnd;
        Loops = loops;
    }

    public int LoopStart { get; }
    public int LoopEnd { get; }
    public bool Loops { get; }
}
