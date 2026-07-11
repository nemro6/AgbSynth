using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace AgbSynth.App.Controls;

public sealed class WaveMemoryEditorControl : Control
{
    public static readonly StyledProperty<string> DataHexProperty =
        AvaloniaProperty.Register<WaveMemoryEditorControl, string>(
            nameof(DataHex),
            "00000000000000000000000000000000",
            defaultBindingMode: BindingMode.TwoWay);

    private bool _isDragging;

    public event EventHandler<WaveMemoryDataHexEditedEventArgs>? DataHexEdited;

    public string DataHex
    {
        get => GetValue(DataHexProperty);
        set => SetValue(DataHexProperty, NormalizeHex(value));
    }

    public WaveMemoryEditorControl()
    {
        Focusable = true;
        MinHeight = 12;
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataHexProperty ||
                e.Property.Name is "ActualThemeVariant" or "RequestedThemeVariant")
                InvalidateVisual();
        };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _isDragging = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        context.FillRectangle(ThemeDrawingPalette.Canvas(this), rect);
        context.DrawRectangle(new Pen(ThemeDrawingPalette.Border(this), 1), rect.Deflate(0.5));

        if (rect.Width < 4 || rect.Height < 4)
            return;

        double paddingX = rect.Height <= 24 ? 3 : 5;
        double paddingY = rect.Height <= 24 ? 2 : 4;
        var plot = new Rect(
            rect.X + paddingX,
            rect.Y + paddingY,
            Math.Max(1, rect.Width - paddingX * 2),
            Math.Max(1, rect.Height - paddingY * 2));

        double centerY = ValueToY(plot, 7.5);
        context.DrawLine(new Pen(ThemeDrawingPalette.Grid(this), 1), new Point(plot.Left, centerY), new Point(plot.Right, centerY));

        int[] samples = DecodeSamples(DataHex);
        double step = plot.Width / samples.Length;
        var lineGeometry = new StreamGeometry();
        using (var g = lineGeometry.Open())
        {
            for (int i = 0; i < samples.Length; i++)
            {
                double x0 = plot.Left + i * step;
                double x1 = i == samples.Length - 1 ? plot.Right : plot.Left + (i + 1) * step;
                double y = ValueToY(plot, samples[i]);
                if (i == 0)
                    g.BeginFigure(new Point(x0, y), false);
                else
                    g.LineTo(new Point(x0, y));
                g.LineTo(new Point(x1, y));
            }
        }

        double stroke = rect.Height <= 24 ? 1.1 : 1.4;
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#8FB7FF")), stroke), lineGeometry);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        _isDragging = true;
        e.Pointer.Capture(this);
        ApplyPointerEdit(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
            return;

        ApplyPointerEdit(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ApplyPointerEdit(Point point)
    {
        var bounds = new Rect(Bounds.Size);
        var rect = new Rect(bounds.X + 8, bounds.Y + 7, Math.Max(1, bounds.Width - 16), Math.Max(1, bounds.Height - 14));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        int index = Math.Clamp((int)((point.X - rect.Left) / rect.Width * 32), 0, 31);
        int value = Math.Clamp((int)Math.Round((rect.Bottom - point.Y) / rect.Height * 15), 0, 15);
        int[] samples = DecodeSamples(DataHex);
        if (samples[index] == value)
            return;

        samples[index] = value;
        string dataHex = EncodeSamples(samples);
        SetCurrentValue(DataHexProperty, dataHex);
        DataHexEdited?.Invoke(this, new WaveMemoryDataHexEditedEventArgs(dataHex));
    }

    private static double ValueToY(Rect rect, double value)
    {
        return rect.Bottom - Math.Clamp(value, 0, 15) / 15.0 * rect.Height;
    }

    private static int[] DecodeSamples(string hex)
    {
        string normalized = NormalizeHex(hex).PadRight(32, '0');
        if (normalized.Length > 32)
            normalized = normalized[..32];

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            bytes = new byte[16];
        }

        var samples = new int[32];
        for (int i = 0; i < 16; i++)
        {
            byte value = i < bytes.Length ? bytes[i] : (byte)0;
            samples[i * 2] = value >> 4;
            samples[i * 2 + 1] = value & 0x0F;
        }
        return samples;
    }

    private static string EncodeSamples(int[] samples)
    {
        var bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i++)
        {
            int high = Math.Clamp(samples[i * 2], 0, 15);
            int low = Math.Clamp(samples[i * 2 + 1], 0, 15);
            bytes[i] = (byte)((high << 4) | low);
        }
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeHex(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}

public sealed class WaveMemoryDataHexEditedEventArgs : EventArgs
{
    public WaveMemoryDataHexEditedEventArgs(string dataHex)
    {
        DataHex = dataHex;
    }

    public string DataHex { get; }
}
