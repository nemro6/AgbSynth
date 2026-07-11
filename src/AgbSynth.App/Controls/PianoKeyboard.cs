using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AgbSynth.App.Controls
{
    public sealed class PianoKeyboard : Control
    {
        private static readonly IBrush LightThemeWhiteKeyBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));

        public static readonly StyledProperty<int> MinNoteProperty =
            AvaloniaProperty.Register<PianoKeyboard, int>(nameof(MinNote), 0);
        public static readonly StyledProperty<int> MaxNoteProperty =
            AvaloniaProperty.Register<PianoKeyboard, int>(nameof(MaxNote), 127);
        public static readonly StyledProperty<IReadOnlyList<ushort>?> ActiveNoteChannelMasksProperty =
            AvaloniaProperty.Register<PianoKeyboard, IReadOnlyList<ushort>?>(nameof(ActiveNoteChannelMasks));
        public static readonly StyledProperty<IReadOnlyList<IBrush>?> ChannelBrushesProperty =
            AvaloniaProperty.Register<PianoKeyboard, IReadOnlyList<IBrush>?>(nameof(ChannelBrushes));
        public static readonly StyledProperty<IReadOnlyList<int>?> HighlightedNotesProperty =
            AvaloniaProperty.Register<PianoKeyboard, IReadOnlyList<int>?>(nameof(HighlightedNotes));
        public static readonly StyledProperty<IReadOnlyList<int>?> MappedNotesProperty =
            AvaloniaProperty.Register<PianoKeyboard, IReadOnlyList<int>?>(nameof(MappedNotes));
        public static readonly StyledProperty<bool> ShowMappedNotesProperty =
            AvaloniaProperty.Register<PianoKeyboard, bool>(nameof(ShowMappedNotes));
        public static readonly StyledProperty<bool> ShowKeySplitRangesProperty =
            AvaloniaProperty.Register<PianoKeyboard, bool>(nameof(ShowKeySplitRanges));
        public static readonly StyledProperty<string> KeySplitKeyMapHexProperty =
            AvaloniaProperty.Register<PianoKeyboard, string>(nameof(KeySplitKeyMapHex), string.Empty);
        public static readonly StyledProperty<IReadOnlyList<string>?> KeySplitRegionLabelsProperty =
            AvaloniaProperty.Register<PianoKeyboard, IReadOnlyList<string>?>(nameof(KeySplitRegionLabels));
        public static readonly StyledProperty<int> SelectedKeySplitRegionIndexProperty =
            AvaloniaProperty.Register<PianoKeyboard, int>(nameof(SelectedKeySplitRegionIndex), -1);

        public int MinNote { get => GetValue(MinNoteProperty); set => SetValue(MinNoteProperty, value); }
        public int MaxNote { get => GetValue(MaxNoteProperty); set => SetValue(MaxNoteProperty, value); }
        public IReadOnlyList<ushort>? ActiveNoteChannelMasks { get => GetValue(ActiveNoteChannelMasksProperty); set => SetValue(ActiveNoteChannelMasksProperty, value); }
        public IReadOnlyList<IBrush>? ChannelBrushes { get => GetValue(ChannelBrushesProperty); set => SetValue(ChannelBrushesProperty, value); }
        public IReadOnlyList<int>? HighlightedNotes { get => GetValue(HighlightedNotesProperty); set => SetValue(HighlightedNotesProperty, value); }
        public IReadOnlyList<int>? MappedNotes { get => GetValue(MappedNotesProperty); set => SetValue(MappedNotesProperty, value); }
        public bool ShowMappedNotes { get => GetValue(ShowMappedNotesProperty); set => SetValue(ShowMappedNotesProperty, value); }
        public bool ShowKeySplitRanges { get => GetValue(ShowKeySplitRangesProperty); set => SetValue(ShowKeySplitRangesProperty, value); }
        public string KeySplitKeyMapHex { get => GetValue(KeySplitKeyMapHexProperty); set => SetValue(KeySplitKeyMapHexProperty, value); }
        public IReadOnlyList<string>? KeySplitRegionLabels { get => GetValue(KeySplitRegionLabelsProperty); set => SetValue(KeySplitRegionLabelsProperty, value); }
        public int SelectedKeySplitRegionIndex { get => GetValue(SelectedKeySplitRegionIndexProperty); set => SetValue(SelectedKeySplitRegionIndexProperty, value); }

        private const int KeySplitNoteCount = 128;
        private const double KeySplitRangeHeight = 32.0;
        private const double KeySplitBoundaryHitWidth = 5.0;
        private const int KeySplitValidRunLength = 16;
        private readonly IBrush[] _keySplitRegionBrushes =
        [
            new SolidColorBrush(Color.Parse("#2D3438")),
            new SolidColorBrush(Color.Parse("#3A332C")),
            new SolidColorBrush(Color.Parse("#2F3B31")),
            new SolidColorBrush(Color.Parse("#342F3D")),
            new SolidColorBrush(Color.Parse("#3D3030")),
            new SolidColorBrush(Color.Parse("#2F3A42")),
            new SolidColorBrush(Color.Parse("#3B3A2F")),
            new SolidColorBrush(Color.Parse("#30363E"))
        ];
        private readonly IBrush[] _lightKeySplitRegionBrushes =
        [
            new SolidColorBrush(Color.Parse("#DCE8EE")),
            new SolidColorBrush(Color.Parse("#EADFCC")),
            new SolidColorBrush(Color.Parse("#DCEBDD")),
            new SolidColorBrush(Color.Parse("#E6DDEE")),
            new SolidColorBrush(Color.Parse("#EEDADA")),
            new SolidColorBrush(Color.Parse("#D9E7F0")),
            new SolidColorBrush(Color.Parse("#E9E7D5")),
            new SolidColorBrush(Color.Parse("#DDE4EE"))
        ];
        private readonly HashSet<int> _pressed = new();

        private readonly List<(Rect rect, int note, bool isBlack)> _white = new();
        private readonly List<(Rect rect, int note, bool isBlack)> _black = new();
        private int _keySplitDragBoundary = -1;
        private int _keySplitDragLeftRegion;
        private int _keySplitDragRightRegion;

        public event EventHandler<int>? NoteOn;
        public event EventHandler<int>? NoteOff;

        public PianoKeyboard()
        {
            MinHeight = 64;
            Cursor = new Cursor(StandardCursorType.Arrow);

            this.PropertyChanged += (_, e) =>
            {
                if (e.Property == BoundsProperty ||
                    e.Property == MinNoteProperty ||
                    e.Property == MaxNoteProperty ||
                    e.Property == ActiveNoteChannelMasksProperty ||
                    e.Property == ChannelBrushesProperty ||
                    e.Property == HighlightedNotesProperty ||
                    e.Property == MappedNotesProperty ||
                    e.Property == ShowMappedNotesProperty ||
                    e.Property == ShowKeySplitRangesProperty ||
                    e.Property == KeySplitKeyMapHexProperty ||
                    e.Property == KeySplitRegionLabelsProperty ||
                    e.Property == SelectedKeySplitRegionIndexProperty ||
                    e.Property.Name == "ActualThemeVariant" ||
                    e.Property.Name == "RequestedThemeVariant")
                {
                    UpdateGeometry();
                    InvalidateVisual();
                }
            };

            AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
            AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
            AddHandler(PointerCaptureLostEvent, (_, __) =>
            {
                _keySplitDragBoundary = -1;
                ReleaseAll();
            }, handledEventsToo: true);

            AddHandler(PointerExitedEvent, (_, __) =>
            {
                if (_keySplitDragBoundary < 0)
                    ReleaseAll();
            }, handledEventsToo: true);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            if (_white.Count == 0 && _black.Count == 0) UpdateGeometry();

            var bounds = Bounds;

            IBrush bg = ThemeDrawingPalette.Canvas(this);

            ctx.FillRectangle(bg, bounds);
            byte[] keySplitMap = DecodeKeySplitKeyMap();
            if (ShouldDrawKeySplitRanges(keySplitMap))
                DrawKeySplitRanges(ctx, bounds, keySplitMap);

            IBrush darkKeyBrush =
                this.FindResource("App.WindowBgBrush") as IBrush
                ?? this.FindResource("PianoRoll.Background") as IBrush
                ?? new SolidColorBrush(Color.Parse("#222222"));

            bool invertKeys = IsDarkThemeVariant();

            IBrush whiteBrush = invertKeys ? darkKeyBrush : LightThemeWhiteKeyBrush;
            var whitePen = new Pen(Brushes.Gray, 1);
            IBrush pressedWhite = new SolidColorBrush(Color.FromRgb(204, 232, 255));

            foreach (var k in _white)
            {
                IBrush brush = _pressed.Contains(k.note) ? pressedWhite : whiteBrush;
                ctx.FillRectangle(brush, k.rect);
                ctx.DrawRectangle(whitePen, k.rect);
            }

            IBrush blackBrush = invertKeys ? LightThemeWhiteKeyBrush : darkKeyBrush;
            IBrush pressedBlack = new SolidColorBrush(Color.FromRgb(64, 128, 192));

            foreach (var k in _black)
            {
                IBrush brush = _pressed.Contains(k.note) ? pressedBlack : blackBrush;
                ctx.FillRectangle(brush, k.rect);
                ctx.DrawRectangle(whitePen, k.rect);
            }

            if (ShowMappedNotes)
            {
                foreach (var k in _white)
                    DrawMappedNoteHighlight(ctx, k.note, k.rect);

                foreach (var k in _black)
                    DrawMappedNoteHighlight(ctx, k.note, k.rect);
            }

            foreach (var k in _white)
                DrawActiveNoteLine(ctx, k.note, k.rect);

            foreach (var k in _black)
                DrawActiveNoteLine(ctx, k.note, k.rect);

            if (ShouldDrawKeySplitRanges(keySplitMap))
            {
                foreach (var k in _white)
                    DrawKeySplitRegionMarker(ctx, k.note, k.rect, keySplitMap);

                foreach (var k in _black)
                    DrawKeySplitRegionMarker(ctx, k.note, k.rect, keySplitMap);
            }

            foreach (var k in _white)
                DrawHighlightedNote(ctx, k.note, k.rect);

            foreach (var k in _black)
                DrawHighlightedNote(ctx, k.note, k.rect);
        }

        private void DrawActiveNoteLine(DrawingContext ctx, int note, Rect keyRect)
        {
            int ch = GetActiveChannel(note);
            if (ch < 0) return;

            IBrush line = GetChannelBrush(ch, Brushes.Transparent);
            const double lineThickness = 4.0;
            double h = Math.Min(lineThickness, keyRect.Height);
            var rect = new Rect(keyRect.X, keyRect.Bottom - h, keyRect.Width, h);
            ctx.FillRectangle(line, rect);
        }

        private void DrawKeySplitRegionMarker(DrawingContext ctx, int note, Rect keyRect, byte[] keySplitMap)
        {
            int selected = SelectedKeySplitRegionIndex;
            if (selected < 0 || GetKeySplitRegionAtNote(keySplitMap, note) != selected)
                return;

            const double lineThickness = 4.0;
            double h = Math.Min(lineThickness, keyRect.Height);
            var rect = new Rect(keyRect.X, keyRect.Bottom - h, keyRect.Width, h);
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#4C84FF")), rect);
        }

        private void DrawMappedNoteHighlight(DrawingContext ctx, int note, Rect keyRect)
        {
            var notes = MappedNotes;
            if (notes is null || !notes.Contains(note))
                return;

            var fill = new SolidColorBrush(Color.FromArgb(96, 80, 176, 138));
            var pen = new Pen(new SolidColorBrush(Color.Parse("#8EE6C1")), 1.4);
            Rect rect = keyRect.Deflate(1);
            ctx.FillRectangle(fill, rect);
            ctx.DrawRectangle(null, pen, rect, 2, 2);
        }

        private IBrush GetChannelBrush(int channel, IBrush fallback)
        {
            var brushes = ChannelBrushes;
            if (brushes is null || (uint)channel >= (uint)brushes.Count) return fallback;
            return brushes[channel] ?? fallback;
        }

        private void DrawHighlightedNote(DrawingContext ctx, int note, Rect keyRect)
        {
            var notes = HighlightedNotes;
            if (notes is null || !notes.Contains(note))
                return;

            double radius = Math.Min(5.0, Math.Min(keyRect.Width, keyRect.Height) * 0.35);
            var center = new Point(keyRect.Center.X, keyRect.Bottom - radius - 8);
            var fill = new SolidColorBrush(Color.Parse("#3C7DFF"));
            var pen = new Pen(new SolidColorBrush(Color.Parse("#B9D0FF")), 1.2);
            ctx.DrawEllipse(fill, pen, center, radius, radius);
        }

        private bool IsDarkThemeVariant()
        {
            return ActualThemeVariant != ThemeVariant.Light;
        }

        private int GetActiveChannel(int note)
        {
            var masks = ActiveNoteChannelMasks;
            if (masks is null || (uint)note >= (uint)masks.Count) return -1;

            ushort mask = masks[note];
            if (mask == 0) return -1;

            for (int ch = 0; ch < 16; ch++)
            {
                if ((mask & (1 << ch)) != 0)
                    return ch;
            }

            return -1;
        }

        private void UpdateGeometry()
        {
            _white.Clear();
            _black.Clear();

            int lo = Math.Clamp(MinNote, 0, 127);
            int hi = Math.Clamp(MaxNote, 0, 127);
            if (hi < lo) (lo, hi) = (hi, lo);

            int WhiteCount(int a, int b) => Enumerable.Range(a, b - a + 1).Count(n => !IsBlack(n));
            int whiteN = WhiteCount(lo, hi);
            if (whiteN == 0) return;

            var keyboardArea = GetKeyboardArea(DecodeKeySplitKeyMap());
            var w = keyboardArea.Width;
            var h = keyboardArea.Height;
            double whiteW = w / whiteN;
            double whiteH = h;
            double blackW = whiteW * 0.6;
            double blackH = h * 0.6;

            int first = lo;
            while (first <= hi && IsBlack(first)) first++;
            if (first > hi) return;

            double x = 0;
            for (int n = first; n <= hi; n++)
            {
                if (IsBlack(n)) continue;
                var rect = new Rect(keyboardArea.X + x, keyboardArea.Y, whiteW, whiteH);
                _white.Add((rect, n, false));
                x += whiteW;
            }

            var wx = _white.ToDictionary(k => k.note, k => k.rect.X);

            foreach (var n in Enumerable.Range(lo, hi - lo + 1))
            {
                if (!IsBlack(n)) continue;

                int leftWhite = PrevWhite(n);
                int rightWhite = NextWhite(n);

                if (!wx.ContainsKey(leftWhite) || !wx.ContainsKey(rightWhite)) continue;

                double lx = wx[leftWhite];
                double rx = wx[rightWhite];
                double cx = (lx + rx + whiteW) * 0.5;
                var rect = new Rect(cx - blackW / 2, keyboardArea.Y, blackW, blackH);
                _black.Add((rect, n, true));
            }
        }

        private Rect GetKeyboardArea(byte[] keySplitMap)
        {
            double top = ShouldDrawKeySplitRanges(keySplitMap)
                ? Math.Min(KeySplitRangeHeight + 4, Bounds.Height * 0.42)
                : 0;
            double height = Math.Max(1, Bounds.Height - top);
            return new Rect(0, top, Bounds.Width, height);
        }

        private bool ShouldDrawKeySplitRanges(byte[] keySplitMap)
        {
            return ShowKeySplitRanges && keySplitMap.Length > 0;
        }

        private void DrawKeySplitRanges(DrawingContext context, Rect bounds, byte[] map)
        {
            double height = Math.Min(KeySplitRangeHeight, Math.Max(18, bounds.Height * 0.38));
            bool isLight = ThemeDrawingPalette.IsLight(this);
            var borderPen = new Pen(ThemeDrawingPalette.Border(this), 1);
            var selectedPen = new Pen(new SolidColorBrush(Color.Parse("#7AA7FF")), 2);
            IBrush labelBrush = ThemeDrawingPalette.Text(this);
            IBrush mutedLabelBrush = ThemeDrawingPalette.Muted(this);

            context.FillRectangle(ThemeDrawingPalette.Canvas(this), new Rect(0, 0, bounds.Width, height));

            foreach (var segment in GetKeySplitSegments(map))
            {
                var (visibleLo, visibleHi) = GetVisibleKeySplitNoteRange();
                int start = Math.Max(segment.Start, visibleLo);
                int end = Math.Min(segment.End, visibleHi);
                if (start > end)
                    continue;

                double x = KeySplitNoteToX(start, bounds.Width);
                double right = KeySplitNoteToX(end + 1, bounds.Width);
                var rect = new Rect(x, 0, Math.Max(1, right - x), height);
                IBrush[] regionBrushes = isLight ? _lightKeySplitRegionBrushes : _keySplitRegionBrushes;
                IBrush fill = regionBrushes[Math.Abs(segment.Region) % regionBrushes.Length];
                context.FillRectangle(fill, rect);
                context.DrawRectangle(segment.Region == SelectedKeySplitRegionIndex ? selectedPen : borderPen, rect);

                if (rect.Width < 28)
                    continue;

                string label = GetKeySplitRegionLabel(segment.Region);
                var text = new FormattedText(
                    label,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    9.5,
                    segment.Region == SelectedKeySplitRegionIndex ? labelBrush : mutedLabelBrush);

                double textX = rect.X + Math.Max(4, (rect.Width - text.Width) * 0.5);
                double textY = rect.Y + (rect.Height - text.Height) * 0.5;
                context.DrawText(text, new Point(Math.Min(textX, rect.Right - 8), textY));
            }

            if (map.Length < KeySplitNoteCount)
            {
                var (visibleLo, visibleHi) = GetVisibleKeySplitNoteRange();
                int start = Math.Max(map.Length, visibleLo);
                if (start <= visibleHi)
                {
                    double x = KeySplitNoteToX(start, bounds.Width);
                    var rect = new Rect(x, 0, Math.Max(1, bounds.Width - x), height);
                    context.FillRectangle(ThemeDrawingPalette.Surface(this), rect);
                    context.DrawRectangle(new Pen(ThemeDrawingPalette.Grid(this), 1), rect);
                }
            }

            for (int note = 1; note < map.Length; note++)
            {
                int previousRegion = GetKeySplitRegionAtNote(map, note - 1);
                int currentRegion = GetKeySplitRegionAtNote(map, note);
                if (previousRegion < 0 || currentRegion < 0 || previousRegion == currentRegion)
                    continue;

                var (visibleLo, visibleHi) = GetVisibleKeySplitNoteRange();
                if (note < visibleLo || note > visibleHi + 1)
                    continue;

                double x = KeySplitNoteToX(note, bounds.Width);
                context.DrawLine(new Pen(ThemeDrawingPalette.Text(this), 1.1), new Point(x, 0), new Point(x, height));
            }
        }

        private IEnumerable<(int Start, int End, int Region)> GetKeySplitSegments(byte[] map)
        {
            if (map.Length == 0)
                yield break;

            int start = -1;
            int region = -1;
            for (int note = 0; note < KeySplitNoteCount; note++)
            {
                int currentRegion = GetKeySplitRegionAtNote(map, note);
                if (currentRegion == region)
                    continue;

                if (start >= 0 && region >= 0)
                    yield return (start, note - 1, region);

                start = currentRegion >= 0 ? note : -1;
                region = currentRegion;
            }

            if (start >= 0 && region >= 0)
                yield return (start, KeySplitNoteCount - 1, region);
        }

        private string GetKeySplitRegionLabel(int region)
        {
            var labels = KeySplitRegionLabels;
            if (labels is not null && (uint)region < (uint)labels.Count)
                return labels[region];

            return $"{region:D3}";
        }

        private byte[] DecodeKeySplitKeyMap()
        {
            string text = KeySplitKeyMapHex?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return Array.Empty<byte>();

            try
            {
                string normalized = new(text.Where(Uri.IsHexDigit).ToArray());
                if (normalized.Length % 2 != 0)
                    normalized = normalized[..^1];
                if (normalized.Length == 0)
                    return Array.Empty<byte>();

                byte[] map = Convert.FromHexString(normalized);
                return map.Length > KeySplitNoteCount
                    ? map.Take(KeySplitNoteCount).ToArray()
                    : map;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private int GetKeySplitRegionAtNote(byte[] map, int note)
        {
            if (note < 0 || note >= map.Length)
                return -1;

            int regionCount = GetKeySplitRegionCount(map);
            if (regionCount <= 0)
                return -1;

            if (note < GetKeySplitFirstMappedNote(map, regionCount))
                return -1;

            int region = map[note];
            return region >= 0 && region < regionCount ? region : -1;
        }

        private int GetKeySplitRegionCount(byte[] map)
        {
            var labels = KeySplitRegionLabels;
            if (labels is { Count: > 0 })
                return labels.Count;

            return map.Length == 0 ? 0 : map.Max() + 1;
        }

        private static int GetKeySplitFirstMappedNote(byte[] map, int regionCount)
        {
            if (map.Length == 0 || regionCount <= 0)
                return 0;

            int requiredRun = Math.Min(KeySplitValidRunLength, map.Length);
            for (int start = 0; start < map.Length; start++)
            {
                int run = 0;
                for (int i = start; i < map.Length && map[i] < regionCount; i++)
                    run++;

                if (run >= requiredRun)
                    return start;
            }

            return 0;
        }

        private int HitTestKeySplitBoundary(Point point, byte[] map)
        {
            if (!ShouldDrawKeySplitRanges(map) || point.Y > KeySplitRangeHeight + 4 || map.Length <= 1)
                return -1;

            for (int note = 1; note < map.Length; note++)
            {
                int previousRegion = GetKeySplitRegionAtNote(map, note - 1);
                int currentRegion = GetKeySplitRegionAtNote(map, note);
                if (previousRegion < 0 || currentRegion < 0 || previousRegion == currentRegion)
                    continue;

                double x = KeySplitNoteToX(note, Bounds.Width);
                if (Math.Abs(point.X - x) <= KeySplitBoundaryHitWidth)
                    return note;
            }

            return -1;
        }

        private (int Lo, int Hi) GetVisibleKeySplitNoteRange()
        {
            int lo = Math.Clamp(MinNote, 0, KeySplitNoteCount - 1);
            int hi = Math.Clamp(MaxNote, 0, KeySplitNoteCount - 1);
            return hi < lo ? (hi, lo) : (lo, hi);
        }

        private double KeySplitNoteToX(int note, double width)
        {
            var (lo, hi) = GetVisibleKeySplitNoteRange();
            if (note <= lo)
                return 0;
            if (note > hi)
                return Math.Max(1, width);

            double? left = GetNoteCenterX(note - 1);
            double? right = GetNoteCenterX(note);
            if (left is double leftX && right is double rightX)
                return (leftX + rightX) * 0.5;

            return KeySplitNoteToLinearX(note, width);
        }

        private int KeySplitXToNote(double x, double width)
        {
            var (lo, hi) = GetVisibleKeySplitNoteRange();
            for (int note = lo; note <= hi; note++)
            {
                if (x < KeySplitNoteToX(note + 1, width))
                    return note;
            }

            return hi;
        }

        private int KeySplitXToBoundary(double x, double width)
        {
            var (lo, hi) = GetVisibleKeySplitNoteRange();
            int min = Math.Max(1, lo + 1);
            int max = Math.Max(min, hi);
            int best = min;
            double bestDistance = double.MaxValue;

            for (int note = min; note <= max; note++)
            {
                double distance = Math.Abs(x - KeySplitNoteToX(note, width));
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = note;
            }

            return best;
        }

        private double KeySplitNoteToLinearX(int note, double width)
        {
            var (lo, hi) = GetVisibleKeySplitNoteRange();
            int count = Math.Max(1, hi - lo + 1);
            int clamped = Math.Clamp(note, lo, hi + 1);
            return (clamped - lo) / (double)count * Math.Max(1, width);
        }

        private double? GetNoteCenterX(int note)
        {
            foreach (var black in _black)
            {
                if (black.note == note)
                    return black.rect.Center.X;
            }

            foreach (var white in _white)
            {
                if (white.note == note)
                    return white.rect.Center.X;
            }

            return null;
        }

        private static bool IsBlack(int note)
        {
            int pc = ((note % 12) + 12) % 12;
            return pc is 1 or 3 or 6 or 8 or 10;
        }
        private static int PrevWhite(int note) { int n = note - 1; while (IsBlack(n)) n--; return n; }
        private static int NextWhite(int note) { int n = note + 1; while (IsBlack(n)) n++; return n; }

        private void OnPointerPressed(object? s, PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this);
            byte[] keySplitMap = DecodeKeySplitKeyMap();
            if (ShouldDrawKeySplitRanges(keySplitMap) && p.Y <= KeySplitRangeHeight + 4)
            {
                int boundary = HitTestKeySplitBoundary(p, keySplitMap);
                if (boundary >= 1)
                {
                    _keySplitDragBoundary = boundary;
                    _keySplitDragLeftRegion = keySplitMap[boundary - 1];
                    _keySplitDragRightRegion = keySplitMap[boundary];
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }

                int keySplitNote = KeySplitXToNote(p.X, Bounds.Width);
                int region = GetKeySplitRegionAtNote(keySplitMap, keySplitNote);
                if (region >= 0)
                {
                    SetCurrentValue(SelectedKeySplitRegionIndexProperty, region);
                    e.Handled = true;
                    return;
                }
            }

            var note = HitTestNote(p);
            if (note is null) return;
            e.Pointer.Capture(this);
            if (_pressed.Add(note.Value))
            {
                InvalidateVisual();
                NoteOn?.Invoke(this, note.Value);
            }
        }

        private void OnPointerReleased(object? s, PointerReleasedEventArgs e)
        {
            if (_keySplitDragBoundary >= 1)
            {
                _keySplitDragBoundary = -1;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            var p = e.GetPosition(this);
            var note = HitTestNote(p);
            if (note is not null && _pressed.Contains(note.Value))
            {
                _pressed.Remove(note.Value);
                InvalidateVisual();
                NoteOff?.Invoke(this, note.Value);
            }
            e.Pointer.Capture(null);
        }

        private void OnPointerMoved(object? s, PointerEventArgs e)
        {
            byte[] keySplitMap = DecodeKeySplitKeyMap();
            var p = e.GetPosition(this);
            if (_keySplitDragBoundary >= 1)
            {
                int newBoundary = Math.Clamp(
                    KeySplitXToBoundary(p.X, Bounds.Width),
                    1,
                    Math.Max(1, keySplitMap.Length - 1));

                if (newBoundary != _keySplitDragBoundary)
                {
                    if (newBoundary > _keySplitDragBoundary)
                    {
                        for (int i = _keySplitDragBoundary; i < newBoundary && i < keySplitMap.Length; i++)
                            keySplitMap[i] = (byte)_keySplitDragLeftRegion;
                    }
                    else
                    {
                        for (int i = newBoundary; i < _keySplitDragBoundary && i < keySplitMap.Length; i++)
                            keySplitMap[i] = (byte)_keySplitDragRightRegion;
                    }

                    _keySplitDragBoundary = newBoundary;
                    SetCurrentValue(KeySplitKeyMapHexProperty, Convert.ToHexString(keySplitMap));
                }

                e.Handled = true;
                return;
            }

            if (ShouldDrawKeySplitRanges(keySplitMap) && p.Y <= KeySplitRangeHeight + 4 && _pressed.Count == 0)
            {
                Cursor = HitTestKeySplitBoundary(p, keySplitMap) >= 1
                    ? new Cursor(StandardCursorType.SizeWestEast)
                    : new Cursor(StandardCursorType.Arrow);
                return;
            }

            Cursor = new Cursor(StandardCursorType.Arrow);
            if (!_pressed.Any()) return;
            var hit = HitTestNote(p);
            var current = _pressed.First();
            if (hit is null || hit.Value == current) return;

            _pressed.Clear();
            NoteOff?.Invoke(this, current);

            _pressed.Add(hit.Value);
            NoteOn?.Invoke(this, hit.Value);
            InvalidateVisual();
        }

        private void ReleaseAll()
        {
            if (_pressed.Count == 0) return;
            foreach (var n in _pressed.ToArray())
                NoteOff?.Invoke(this, n);
            _pressed.Clear();
            InvalidateVisual();
        }

        private int? HitTestNote(Point p)
        {
            var k = _black.FirstOrDefault(b => b.rect.Contains(p));
            if (k.rect != default) return k.note;
            var w = _white.FirstOrDefault(b => b.rect.Contains(p));
            if (w.rect != default) return w.note;
            return null;
        }
    }
}
