using System.Drawing.Drawing2D;

namespace FreeVoiceStudio;

/// <summary>Selectable engine card: name, tier badge, description, speed line.</summary>
public sealed class EngineCard : Panel
{
    public string EngineId { get; }
    private readonly string _name, _desc, _speed;
    private readonly int _tier;
    private bool _selected;
    public event Action<string>? Selected;

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public EngineCard(string id, string name, int tier, string desc, string speed)
    {
        EngineId = id; _name = name; _tier = tier; _desc = desc; _speed = speed;
        Size = new Size(242, 104);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        BackColor = Theme.Card2;
        Click += (_, _) => Selected?.Invoke(EngineId);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Back); // corners must match the page, not the card

        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var bg = new SolidBrush(Theme.Card2))
        using (var path = Theme.Rounded(rect, 11))
            g.FillPath(bg, path);
        using (var pen = new Pen(_selected ? Theme.A1 : Theme.Border, _selected ? 2f : 1.2f))
        using (var path = Theme.Rounded(rect, 11))
            g.DrawPath(pen, path);

        using var nameFont = new Font("Segoe UI Semibold", 11f);
        using var textBrush = new SolidBrush(Theme.Text);
        g.DrawString(_name, nameFont, textBrush, 14, 12);

        // tier badge
        var nameSize = g.MeasureString(_name, nameFont);
        var badgeRect = new RectangleF(16 + nameSize.Width, 15, 52, 17);
        using (var badgeBg = _tier == 3
                   ? (Brush)new LinearGradientBrush(badgeRect, Theme.A1, Theme.A2, 0f)
                   : new SolidBrush(Color.FromArgb(49, 49, 63)))
        using (var badgePath = Theme.Rounded(badgeRect, 8))
            g.FillPath(badgeBg, badgePath);
        using var badgeFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var badgeText = new SolidBrush(_tier == 3 ? Color.White : Theme.Sub);
        g.DrawString($"TIER {_tier}", badgeFont, badgeText, badgeRect.X + 7, badgeRect.Y + 3);

        using var descFont = new Font("Segoe UI", 8.25f);
        using var subBrush = new SolidBrush(Theme.Sub);
        g.DrawString(_desc, descFont, subBrush, new RectangleF(14, 36, Width - 26, 42));

        using var dimBrush = new SolidBrush(Theme.Dim);
        using var speedFont = new Font("Segoe UI", 7.75f);
        g.DrawString(_speed, speedFont, dimBrush, 14, Height - 21);
    }
}

/// <summary>Minimal violet slider with a label and live value readout.</summary>
public sealed class FvSlider : Control
{
    private double _min, _max, _value, _step;
    private readonly string _label;
    private readonly Func<double, string> _fmt;
    private bool _drag;
    public event Action? Changed;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, _min, _max); Invalidate(); }
    }

    public FvSlider(string label, double min, double max, double value, double step,
                    Func<double, string>? fmt = null)
    {
        _label = label; _min = min; _max = max; _value = value; _step = step;
        _fmt = fmt ?? (v => v.ToString("0.00"));
        Size = new Size(300, 44);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    private RectangleF Track => new(2, Height - 16, Width - 4, 5);

    private void SetFromMouse(int x)
    {
        double t = Math.Clamp((x - Track.X) / Track.Width, 0, 1);
        double raw = _min + t * (_max - _min);
        _value = Math.Clamp(Math.Round(raw / _step) * _step, _min, _max);
        Invalidate();
        Changed?.Invoke();
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; SetFromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);

        using var labelFont = new Font("Segoe UI", 8.75f);
        using var sub = new SolidBrush(Theme.Sub);
        using var txt = new SolidBrush(Theme.Text);
        g.DrawString(_label, labelFont, sub, 0, 0);
        var valStr = _fmt(_value);
        var valSize = g.MeasureString(valStr, labelFont);
        g.DrawString(valStr, labelFont, txt, Width - valSize.Width - 2, 0);

        var track = Track;
        using (var bg = new SolidBrush(Color.FromArgb(44, 44, 58)))
        using (var path = Theme.Rounded(track, track.Height / 2))
            g.FillPath(bg, path);

        float t = (float)((_value - _min) / (_max - _min));
        var fill = new RectangleF(track.X, track.Y, Math.Max(6, track.Width * t), track.Height);
        using (var fillBrush = new LinearGradientBrush(fill, Theme.A1, Theme.A2, 0f))
        using (var path = Theme.Rounded(fill, track.Height / 2))
            g.FillPath(fillBrush, path);

        float cx = track.X + track.Width * t;
        using var thumb = new SolidBrush(Color.White);
        g.FillEllipse(thumb, cx - 7, track.Y + track.Height / 2 - 7, 14, 14);
        using var thumbRing = new Pen(Theme.A1, 2f);
        g.DrawEllipse(thumbRing, cx - 7, track.Y + track.Height / 2 - 7, 14, 14);
    }
}

/// <summary>iOS-style toggle with a 60fps knob slide.</summary>
public sealed class FvToggle : Control
{
    public bool Checked { get; set; }
    public event Action? Changed;
    private readonly string _label;
    private float _knobX;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    public FvToggle(string label, bool value)
    {
        _label = label; Checked = value;
        _knobX = value ? 22 : 3;
        Size = new Size(220, 26);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        _anim.Tick += (_, _) =>
        {
            float target = Checked ? 22 : 3;
            _knobX += (target - _knobX) * 0.35f;
            if (Math.Abs(target - _knobX) < 0.4f) { _knobX = target; _anim.Stop(); }
            Invalidate();
        };
        Click += (_, _) => { Checked = !Checked; _anim.Start(); Changed?.Invoke(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);

        var sw = new RectangleF(0, 3, 40, 21);
        float blend = (_knobX - 3) / 19f; // 0 off → 1 on
        var trackColor = Blend(Color.FromArgb(49, 49, 63), Theme.A1, blend);
        using (var bg = new SolidBrush(trackColor))
        using (var path = Theme.Rounded(sw, 10.5f))
            g.FillPath(bg, path);
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, _knobX, 5.5f, 16, 16);

        using var font = new Font("Segoe UI", 9f);
        using var brush = new SolidBrush(Theme.Sub);
        g.DrawString(_label, font, brush, 48, 4);
    }

    private static Color Blend(Color a, Color b, float t)
        => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
}

/// <summary>Two-option segmented control with a sliding thumb (MP3 / WAV).</summary>
public sealed class Segmented : Control
{
    private readonly string[] _options;
    public int Index { get; private set; }
    public string Value => _options[Index];
    public event Action? Changed;
    private float _thumbX = -1;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    public Segmented(string[] options, int index = 0)
    {
        _options = options; Index = index;
        Size = new Size(130, 32);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        _anim.Tick += (_, _) =>
        {
            float target = TargetX();
            _thumbX += (target - _thumbX) * 0.35f;
            if (Math.Abs(target - _thumbX) < 0.5f) { _thumbX = target; _anim.Stop(); }
            Invalidate();
        };
        Click += (_, e2) =>
        {
            var me = (MouseEventArgs)e2;
            int next = Math.Min(_options.Length - 1, me.X * _options.Length / Math.Max(1, Width));
            if (next == Index) return;
            Index = next;
            _anim.Start();
            Changed?.Invoke();
        };
    }

    private float SegWidth() => (float)Width / _options.Length;
    private float TargetX() => Index * SegWidth() + 2;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_thumbX < 0) _thumbX = TargetX();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var bg = new SolidBrush(Theme.Card2))
        using (var path = Theme.Rounded(rect, 9))
            g.FillPath(bg, path);
        using (var pen = new Pen(Theme.Border))
        using (var path = Theme.Rounded(rect, 9))
            g.DrawPath(pen, path);

        float w = SegWidth();
        var thumb = new RectangleF(_thumbX, 2, w - 4, Height - 5);
        using (var sel = new LinearGradientBrush(thumb, Theme.A1, Theme.A2, 0f))
        using (var path = Theme.Rounded(thumb, 7))
            g.FillPath(sel, path);

        for (int i = 0; i < _options.Length; i++)
        {
            var seg = new RectangleF(i * w + 2, 2, w - 4, Height - 5);
            using var font = new Font("Segoe UI", 9f, i == Index ? FontStyle.Bold : FontStyle.Regular);
            using var brush = new SolidBrush(i == Index ? Color.White : Theme.Sub);
            var size = g.MeasureString(_options[i], font);
            g.DrawString(_options[i], font, brush, seg.X + (seg.Width - size.Width) / 2, seg.Y + (seg.Height - size.Height) / 2);
        }
    }
}

/// <summary>Thin draggable seek bar for the player.</summary>
public sealed class SeekBar : Control
{
    private double _frac;
    private bool _drag;
    /// <summary>0..1 position. Setting from code while the user drags is ignored.</summary>
    public double Fraction
    {
        get => _frac;
        set { if (!_drag) { _frac = Math.Clamp(value, 0, 1); Invalidate(); } }
    }
    public event Action<double>? Seeked;

    public SeekBar()
    {
        Size = new Size(300, 18);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    private void FromMouse(int x)
    {
        _frac = Math.Clamp((double)x / Math.Max(1, Width), 0, 1);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; FromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) FromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag) { _drag = false; Seeked?.Invoke(_frac); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        var track = new RectangleF(0, Height / 2f - 2.5f, Width, 5);
        using (var bg = new SolidBrush(Color.FromArgb(44, 44, 58)))
        using (var path = Theme.Rounded(track, 2.5f))
            g.FillPath(bg, path);
        var fill = new RectangleF(track.X, track.Y, Math.Max(5, (float)(track.Width * _frac)), track.Height);
        using (var fb = new LinearGradientBrush(fill, Theme.A1, Theme.A2, 0f))
        using (var path = Theme.Rounded(fill, 2.5f))
            g.FillPath(fb, path);
        float cx = (float)(track.Width * _frac);
        using var thumb = new SolidBrush(Color.White);
        g.FillEllipse(thumb, cx - 5, Height / 2f - 5, 10, 10);
    }
}

/// <summary>Gradient primary button.</summary>
public sealed class GradientButton : Button
{
    public GradientButton()
    {
        Tag = "custom";
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Font = new Font("Segoe UI Semibold", 11.5f);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Back);
        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var bg = Enabled
                   ? (Brush)new LinearGradientBrush(rect, Theme.A1, Theme.A2, 60f)
                   : new SolidBrush(Color.FromArgb(52, 52, 68)))
        using (var path = Theme.Rounded(rect, 11))
            g.FillPath(bg, path);
        using var brush = new SolidBrush(Enabled ? Color.White : Theme.Sub);
        var size = g.MeasureString(Text, Font);
        g.DrawString(Text, Font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
    }
}
