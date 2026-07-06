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

/// <summary>iOS-style toggle.</summary>
public sealed class FvToggle : Control
{
    public bool Checked { get; set; }
    public event Action? Changed;
    private readonly string _label;

    public FvToggle(string label, bool value)
    {
        _label = label; Checked = value;
        Size = new Size(220, 26);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Click += (_, _) => { Checked = !Checked; Invalidate(); Changed?.Invoke(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);

        var sw = new RectangleF(0, 3, 40, 21);
        using (var bg = Checked
                   ? (Brush)new LinearGradientBrush(sw, Theme.A1, Theme.A2, 0f)
                   : new SolidBrush(Color.FromArgb(49, 49, 63)))
        using (var path = Theme.Rounded(sw, 10.5f))
            g.FillPath(bg, path);
        using var knob = new SolidBrush(Checked ? Color.White : Color.FromArgb(140, 140, 156));
        g.FillEllipse(knob, Checked ? 22 : 3, 5.5f, 16, 16);

        using var font = new Font("Segoe UI", 9f);
        using var brush = new SolidBrush(Theme.Sub);
        g.DrawString(_label, font, brush, 48, 4);
    }
}

/// <summary>Two-option segmented control (MP3 / WAV).</summary>
public sealed class Segmented : Control
{
    private readonly string[] _options;
    public int Index { get; private set; }
    public string Value => _options[Index];
    public event Action? Changed;

    public Segmented(string[] options, int index = 0)
    {
        _options = options; Index = index;
        Size = new Size(130, 32);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Click += (_, e2) =>
        {
            var me = (MouseEventArgs)e2;
            Index = Math.Min(_options.Length - 1, me.X * _options.Length / Math.Max(1, Width));
            Invalidate();
            Changed?.Invoke();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
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

        float w = (float)Width / _options.Length;
        for (int i = 0; i < _options.Length; i++)
        {
            var seg = new RectangleF(i * w + 2, 2, w - 4, Height - 5);
            if (i == Index)
            {
                using var sel = new LinearGradientBrush(seg, Theme.A1, Theme.A2, 0f);
                using var path = Theme.Rounded(seg, 7);
                g.FillPath(sel, path);
            }
            using var font = new Font("Segoe UI", 9f, i == Index ? FontStyle.Bold : FontStyle.Regular);
            using var brush = new SolidBrush(i == Index ? Color.White : Theme.Sub);
            var size = g.MeasureString(_options[i], font);
            g.DrawString(_options[i], font, brush, seg.X + (seg.Width - size.Width) / 2, seg.Y + (seg.Height - size.Height) / 2);
        }
    }
}

/// <summary>Panel that is transparent to mouse hit-testing — clicks and drags fall
/// through to the form, giving native window dragging over the custom title bar.</summary>
public class HitTransparentPanel : Panel
{
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTTRANSPARENT = -1;
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>Panel that lets hit-tests through only near the form's edges, so the
/// native resize grips keep working under docked content.</summary>
public class EdgeAwarePanel : Panel
{
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTTRANSPARENT = -1;
        if (m.Msg == WM_NCHITTEST)
        {
            var form = FindForm();
            if (form is { WindowState: FormWindowState.Normal })
            {
                var screenPt = new Point((short)(m.LParam.ToInt64() & 0xFFFF),
                                         (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                var formPt = form.PointToClient(screenPt);
                const int margin = 7;
                if (formPt.X < margin || formPt.X >= form.Width - margin ||
                    formPt.Y < margin || formPt.Y >= form.Height - margin)
                {
                    m.Result = HTTRANSPARENT;
                    return;
                }
            }
        }
        base.WndProc(ref m);
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
