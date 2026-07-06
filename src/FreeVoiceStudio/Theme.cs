using System.Drawing.Drawing2D;

namespace FreeVoiceStudio;

/// <summary>FreeVoice palette — same family as FreeFlow, tuned for the studio.</summary>
public static class Theme
{
    public static readonly Color Back = Color.FromArgb(14, 14, 19);
    public static readonly Color Panel = Color.FromArgb(21, 21, 28);
    public static readonly Color Card = Color.FromArgb(26, 26, 35);
    public static readonly Color Card2 = Color.FromArgb(31, 31, 41);
    public static readonly Color Border = Color.FromArgb(40, 40, 51);
    public static readonly Color Border2 = Color.FromArgb(58, 58, 76);
    public static readonly Color Text = Color.FromArgb(234, 234, 242);
    public static readonly Color Sub = Color.FromArgb(154, 154, 171);
    public static readonly Color Dim = Color.FromArgb(107, 107, 124);
    // FreeVoice accent: light green → light blue (Baron's pick); everything else matches FreeFlow
    public static readonly Color A1 = Color.FromArgb(110, 231, 167);
    public static readonly Color A2 = Color.FromArgb(90, 200, 250);
    public static readonly Color Ok = Color.FromArgb(79, 200, 128);
    public static readonly Color Err = Color.FromArgb(224, 112, 80);

    public static LinearGradientBrush Grad(Rectangle r)
        => new(r, A1, A2, 60f);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Apply(Control root)
    {
        Style(root);
        foreach (Control c in root.Controls)
            Apply(c);
    }

    private static void Style(Control c)
    {
        switch (c)
        {
            case Form f:
                f.BackColor = Back;
                f.ForeColor = Text;
                break;
            case Button b when b.Tag as string != "custom":
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.BackColor = Card2;
                b.ForeColor = Text;
                b.Cursor = Cursors.Hand;
                break;
            case TextBox tb:
                tb.BackColor = Card2;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                PadTextBox(tb);
                break;
            case ComboBox cb:
                cb.BackColor = Card2;
                cb.ForeColor = Text;
                cb.FlatStyle = FlatStyle.Flat;
                break;
        }
    }

    /// <summary>Text/placeholder inset so text never touches the border.</summary>
    public static void PadTextBox(TextBox tb)
    {
        const int EM_SETMARGINS = 0xD3;
        void Apply() => SendMessage(tb.Handle, EM_SETMARGINS, (IntPtr)3 /*left|right*/, (IntPtr)((8 << 16) | 8));
        if (tb.IsHandleCreated) Apply();
        tb.HandleCreated += (_, _) => Apply();
    }

    /// <summary>Dark title bar via DWM.</summary>
    public static void DarkTitleBar(Form f)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
