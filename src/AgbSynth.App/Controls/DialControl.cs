using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AgbSynth.App.Controls;

public sealed class DialControl : Control
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<DialControl, int>(
            nameof(Value),
            defaultValue: 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<DialControl, int>(nameof(Minimum), 0);

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<DialControl, int>(nameof(Maximum), 255);

    public static readonly StyledProperty<double> StartAngleDegreesProperty =
        AvaloniaProperty.Register<DialControl, double>(nameof(StartAngleDegrees), -135);

    public static readonly StyledProperty<double> EndAngleDegreesProperty =
        AvaloniaProperty.Register<DialControl, double>(nameof(EndAngleDegrees), 135);

    private bool _dragging;
    private double _startX;
    private int _startValue;

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    public int Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StartAngleDegrees
    {
        get => GetValue(StartAngleDegreesProperty);
        set => SetValue(StartAngleDegreesProperty, value);
    }

    public double EndAngleDegrees
    {
        get => GetValue(EndAngleDegreesProperty);
        set => SetValue(EndAngleDegreesProperty, value);
    }

    public DialControl()
    {
        Width = 32;
        Height = 32;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == ValueProperty ||
                e.Property == MinimumProperty ||
                e.Property == MaximumProperty ||
                e.Property == StartAngleDegreesProperty ||
                e.Property == EndAngleDegreesProperty ||
                e.Property.Name is "ActualThemeVariant" or "RequestedThemeVariant")
            {
                InvalidateVisual();
            }
        };
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => _dragging = false, handledEventsToo: true);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        double radius = size * 0.43;
        IBrush fill = ThemeDrawingPalette.Control(this);
        var stroke = new Pen(ThemeDrawingPalette.Border(this), 1.2);
        context.DrawEllipse(fill, stroke, center, radius, radius);

        double normalized = Maximum <= Minimum ? 0 : (Value - Minimum) / (double)(Maximum - Minimum);
        double angle = (StartAngleDegrees + normalized * (EndAngleDegrees - StartAngleDegrees)) * Math.PI / 180.0;
        var end = new Point(
            center.X + Math.Cos(angle) * radius * 0.72,
            center.Y + Math.Sin(angle) * radius * 0.72);
        context.DrawLine(new Pen(ThemeDrawingPalette.Text(this), 2.0), center, end);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        _startX = e.GetPosition(this).X;
        _startValue = Value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        double delta = e.GetPosition(this).X - _startX;
        Value = _startValue + (int)Math.Round(delta);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
