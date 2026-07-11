using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgbSynth.App.Controls;

public sealed class AdsrEnvelopeControl : Control
{
    public static readonly StyledProperty<int> AttackProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(
            nameof(Attack),
            defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> DecayProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(
            nameof(Decay),
            defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> SustainProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(
            nameof(Sustain),
            defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> ReleaseProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, int>(
            nameof(Release),
            defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsPsgProperty =
        AvaloniaProperty.Register<AdsrEnvelopeControl, bool>(nameof(IsPsg));

    public int Attack
    {
        get => GetValue(AttackProperty);
        set => SetValue(AttackProperty, Math.Clamp(value, 0, 255));
    }

    public int Decay
    {
        get => GetValue(DecayProperty);
        set => SetValue(DecayProperty, Math.Clamp(value, 0, 255));
    }

    public int Sustain
    {
        get => GetValue(SustainProperty);
        set => SetValue(SustainProperty, Math.Clamp(value, 0, 255));
    }

    public int Release
    {
        get => GetValue(ReleaseProperty);
        set => SetValue(ReleaseProperty, Math.Clamp(value, 0, 255));
    }

    public bool IsPsg
    {
        get => GetValue(IsPsgProperty);
        set => SetValue(IsPsgProperty, value);
    }

    public AdsrEnvelopeControl()
    {
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == AttackProperty ||
                e.Property == DecayProperty ||
                e.Property == SustainProperty ||
                e.Property == ReleaseProperty ||
                e.Property == IsPsgProperty ||
                e.Property.Name is "ActualThemeVariant" or "RequestedThemeVariant")
            {
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

        var plot = rect.Deflate(new Thickness(10, 8, 10, 8));
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        double bottom = plot.Bottom;
        double top = plot.Top;
        double sustainMax = IsPsg ? 15.0 : 255.0;
        double sustainY = bottom - plot.Height * Math.Clamp(Sustain, 0, (int)sustainMax) / sustainMax;

        // DirectSound attack is speed-like. PSG attack/decay/release are time-like.
        double attackFactor = IsPsg
            ? Math.Clamp(Attack, 0, 255) / 255.0
            : 1.0 - Math.Clamp(Attack, 0, 255) / 255.0;
        double attackWidth = 8 + attackFactor * plot.Width * 0.22;
        double decayWidth = 12 + Math.Clamp(Decay, 0, 255) / 255.0 * plot.Width * 0.18;
        double sustainWidth = plot.Width * 0.24;
        double releaseWidth = 10 + Math.Clamp(Release, 0, 255) / 255.0 * plot.Width * 0.24;

        double x0 = plot.Left;
        double x1 = Math.Min(plot.Right, x0 + attackWidth);
        double x2 = Math.Min(plot.Right, x1 + decayWidth);
        double x3 = Math.Min(plot.Right, x2 + sustainWidth);
        double x4 = Math.Min(plot.Right, x3 + releaseWidth);

        var fillGeometry = new StreamGeometry();
        using (var g = fillGeometry.Open())
        {
            g.BeginFigure(new Point(x0, bottom), true);
            g.LineTo(new Point(x1, top));
            g.LineTo(new Point(x2, sustainY));
            g.LineTo(new Point(x3, sustainY));
            g.LineTo(new Point(x4, bottom));
        }

        var lineGeometry = new StreamGeometry();
        using (var g = lineGeometry.Open())
        {
            g.BeginFigure(new Point(x0, bottom), false);
            g.LineTo(new Point(x1, top));
            g.LineTo(new Point(x2, sustainY));
            g.LineTo(new Point(x3, sustainY));
            g.LineTo(new Point(x4, bottom));
        }

        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(82, 120, 185, 255)), null, fillGeometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#A8CCFF")), 1.6), lineGeometry);
    }
}
