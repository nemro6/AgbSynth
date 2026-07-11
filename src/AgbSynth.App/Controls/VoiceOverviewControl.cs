using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgbSynth.App.Controls;

public sealed class VoiceOverviewControl : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<VoiceOverviewControl, string>(nameof(Kind), string.Empty);

    public static readonly StyledProperty<string> DataFilePathProperty =
        AvaloniaProperty.Register<VoiceOverviewControl, string>(nameof(DataFilePath), string.Empty);

    public static readonly StyledProperty<string> ProjectDirectoryProperty =
        AvaloniaProperty.Register<VoiceOverviewControl, string>(nameof(ProjectDirectory), string.Empty);

    public static readonly StyledProperty<int> SquareDutyIndexProperty =
        AvaloniaProperty.Register<VoiceOverviewControl, int>(nameof(SquareDutyIndex), 2);

    private string _cachedPath = string.Empty;
    private double[]? _cachedSamples;

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value ?? string.Empty);
    }

    public string DataFilePath
    {
        get => GetValue(DataFilePathProperty);
        set => SetValue(DataFilePathProperty, value ?? string.Empty);
    }

    public string ProjectDirectory
    {
        get => GetValue(ProjectDirectoryProperty);
        set => SetValue(ProjectDirectoryProperty, value ?? string.Empty);
    }

    public int SquareDutyIndex
    {
        get => GetValue(SquareDutyIndexProperty);
        set => SetValue(SquareDutyIndexProperty, Math.Clamp(value, 0, 3));
    }

    public VoiceOverviewControl()
    {
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == KindProperty ||
                e.Property == DataFilePathProperty ||
                e.Property == ProjectDirectoryProperty ||
                e.Property == SquareDutyIndexProperty ||
                e.Property.Name is "ActualThemeVariant" or "RequestedThemeVariant")
            {
                if (e.Property == DataFilePathProperty || e.Property == ProjectDirectoryProperty)
                    _cachedPath = string.Empty;
                InvalidateVisual();
            }
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(ThemeDrawingPalette.Surface(this), rect);
        context.DrawRectangle(new Pen(ThemeDrawingPalette.Border(this), 1), rect.Deflate(0.5));

        if (string.IsNullOrWhiteSpace(Kind))
        {
            DrawCenteredText(context, "No overview", ThemeDrawingPalette.Muted(this));
            return;
        }

        double left = rect.X + 8;
        double right = rect.Right - 8;
        double width = Math.Max(1, right - left);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#8FB7FF")), 1.5);

        if (Kind == "Square")
        {
            double dutyRatio = Math.Clamp(SquareDutyIndex, 0, 3) switch
            {
                0 => 0.125,
                1 => 0.25,
                2 => 0.5,
                3 => 0.75,
                _ => 0.5
            };
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                double high = rect.Y + 16;
                double low = rect.Bottom - 16;
                g.BeginFigure(new Point(left, high), false);
                for (int i = 0; i < 8; i++)
                {
                    double x0 = left + width * i / 8.0;
                    double x1 = left + width * (i + dutyRatio) / 8.0;
                    double x2 = left + width * (i + 1) / 8.0;
                    g.LineTo(new Point(x1, high));
                    g.LineTo(new Point(x1, low));
                    g.LineTo(new Point(x2, low));
                    g.LineTo(new Point(x2, high));
                }
            }
            context.DrawGeometry(null, pen, geometry);
            return;
        }

        double[]? samples = LoadSamples();
        if (samples is { Length: > 1 })
        {
            DrawSamples(context, samples, left, right, rect);
            return;
        }

        double mid = rect.Center.Y;
        var wave = new StreamGeometry();
        using (var g = wave.Open())
        {
            g.BeginFigure(new Point(left, mid), false);
            for (int i = 1; i <= 96; i++)
            {
                double x = left + width * i / 96.0;
                double phase = i / 96.0 * Math.PI * 6;
                double amp = Kind == "WaveMemory" ? 18 : 24;
                double y = mid + Math.Sin(phase) * amp * (0.55 + 0.45 * Math.Sin(phase * 0.33));
                g.LineTo(new Point(x, y));
            }
        }
        context.DrawGeometry(null, pen, wave);
    }

    private double[]? LoadSamples()
    {
        string resolvedPath = ResolvePath(DataFilePath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            return null;

        if (string.Equals(resolvedPath, _cachedPath, StringComparison.OrdinalIgnoreCase))
            return _cachedSamples;

        _cachedPath = resolvedPath;
        _cachedSamples = Kind switch
        {
            "WaveData" => LoadWaveDataJson(resolvedPath),
            "WaveMemory" => LoadWaveMemoryBinary(resolvedPath),
            _ => null
        };
        return _cachedSamples;
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        if (Path.IsPathRooted(path))
            return path;
        return string.IsNullOrWhiteSpace(ProjectDirectory)
            ? path
            : Path.Combine(ProjectDirectory, path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static double[]? LoadWaveDataJson(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("DataHex", out var dataHexElement))
                return null;

            string hex = dataHexElement.GetString() ?? string.Empty;
            int length = hex.Length / 2;
            if (length == 0)
                return null;

            var samples = new double[length];
            for (int i = 0; i < length; i++)
            {
                byte value = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                samples[i] = unchecked((sbyte)value) / 128.0;
            }
            return samples;
        }
        catch
        {
            return null;
        }
    }

    private static double[]? LoadWaveMemoryBinary(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return null;

            var values = new List<double>(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                values.Add(((value >> 4) - 7.5) / 7.5);
                values.Add(((value & 0x0F) - 7.5) / 7.5);
            }
            return values.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSamples(DrawingContext context, double[] samples, double left, double right, Rect rect)
    {
        var geometry = new StreamGeometry();
        double width = Math.Max(1, right - left);
        double mid = rect.Center.Y;
        double amp = Math.Max(1, rect.Height * 0.38);
        int points = Math.Min(256, Math.Max(32, (int)width));
        using (var g = geometry.Open())
        {
            for (int i = 0; i < points; i++)
            {
                int sampleIndex = Math.Clamp((int)Math.Round(i / (double)(points - 1) * (samples.Length - 1)), 0, samples.Length - 1);
                double x = left + width * i / (points - 1);
                double y = mid - Math.Clamp(samples[sampleIndex], -1, 1) * amp;
                if (i == 0)
                    g.BeginFigure(new Point(x, y), false);
                else
                    g.LineTo(new Point(x, y));
            }
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#8FB7FF")), 1.5), geometry);
    }

    private void DrawCenteredText(DrawingContext context, string text, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11,
            brush);
        context.DrawText(formatted, new Point((Bounds.Width - formatted.Width) / 2, (Bounds.Height - formatted.Height) / 2));
    }
}
