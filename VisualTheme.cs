using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

// Tokens compartilhados: manter cores, foco e espaçamento consistentes em novas telas.
internal static class VisualTheme
{
    internal static readonly Color Background = ColorTranslator.FromHtml("#1B1B2B");
    internal static readonly Color Surface = ColorTranslator.FromHtml("#232436");
    internal static readonly Color Secondary = ColorTranslator.FromHtml("#2B2C40");
    internal static readonly Color Border = ColorTranslator.FromHtml("#414359");
    internal static readonly Color Text = ColorTranslator.FromHtml("#F1F2F8");
    internal static readonly Color Muted = ColorTranslator.FromHtml("#B7B9CD");
    internal static readonly Color Focus = ColorTranslator.FromHtml("#8E96B7");
    internal static readonly Color Primary = ColorTranslator.FromHtml("#2B7C53");
    internal static readonly Color PrimaryBorder = ColorTranslator.FromHtml("#3A9B69");
    internal static readonly Color Selected = ColorTranslator.FromHtml("#34394E");
    internal static readonly Color Hover = ColorTranslator.FromHtml("#3A3D55");
    internal static readonly Color Success = ColorTranslator.FromHtml("#38D57B");
    internal static readonly Color Warning = ColorTranslator.FromHtml("#DAA520");
    internal static Color Foreground => SystemInformation.HighContrast ? SystemColors.WindowText : Text;
    internal static Color Canvas => SystemInformation.HighContrast ? SystemColors.Window : Background;
    internal static Color Support => SystemInformation.HighContrast ? SystemColors.WindowText : Muted;
    internal static Font Font(float pixels = 13, FontStyle style = FontStyle.Regular) => new("Segoe UI", pixels, style, GraphicsUnit.Pixel);

    internal static void Window(Form form)
    {
        form.BackColor = Canvas;
        form.ForeColor = Foreground;
        form.Font = Font();
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.HandleCreated += (_, _) =>
        {
            if (SystemInformation.HighContrast) return;
            int enabled = 1;
            try { DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    internal static GraphicsPath Rounded(Rectangle rectangle, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Grid(DataGridView grid)
    {
        if (SystemInformation.HighContrast) return;
        grid.BackgroundColor = Background;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Muted, SelectionBackColor = Surface, SelectionForeColor = Muted,
            Font = grid.Font, Padding = new Padding(8, 0, 8, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Background, ForeColor = Text, SelectionBackColor = Selected, SelectionForeColor = Text,
            Padding = new Padding(8, 4, 8, 4)
        };
        grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 36;
    }

    internal static void Menu(ContextMenuStrip menu)
    {
        if (SystemInformation.HighContrast) return;
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Focus;
        public override Color MenuBorder => Border;
    }

    internal static void Alert(IWin32Window owner, string title, string message, Icon icon)
    {
        using var dialog = CreateAlert(title, message, icon);
        dialog.ShowDialog(owner);
    }

    internal static Form CreateAlert(string title, string message, Icon icon)
    {
        var dialog = new Form
        {
            Text = title, Icon = icon, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(540, 320),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        Window(dialog);
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = title, AutoSize = true, MaximumSize = new Size(490, 0), Font = Font(16), Margin = new Padding(0, 0, 0, 16) });
        layout.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 0, 0, 24) });
        var close = new ThemeButton { Text = "Entendi", IsPrimary = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        layout.Controls.Add(close);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = close;
        dialog.CancelButton = close;
        return dialog;
    }
}

internal sealed class ThemeButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsPrimary { get; set; }
    private bool hovered;
    internal ThemeButton()
    {
        AutoSize = true;
        MinimumSize = new Size(80, 38);
        Padding = new Padding(19, 9, 19, 9);
        Margin = new Padding(0, 0, 8, 0);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Font = VisualTheme.Font(12);
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? VisualTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var path = VisualTheme.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 6 * scale);
        Color fill = IsPrimary ? VisualTheme.Primary : VisualTheme.Secondary;
        if (hovered) fill = ControlPaint.Light(fill, .12f);
        if (!Enabled) fill = Blend(fill, VisualTheme.Background, .4f);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Focused ? VisualTheme.Focus : IsPrimary ? VisualTheme.PrimaryBorder : VisualTheme.Border, Focused ? 2 * scale : scale);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? VisualTheme.Text : Blend(VisualTheme.Text, VisualTheme.Background, .4f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
    }
    private static Color Blend(Color foreground, Color background, float opacity) => Color.FromArgb(
        (int)(foreground.R * opacity + background.R * (1 - opacity)),
        (int)(foreground.G * opacity + background.G * (1 - opacity)),
        (int)(foreground.B * opacity + background.B * (1 - opacity)));
}

internal sealed class ThemeField : Panel
{
    internal ThemeField(TextBox box)
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        Height = 38;
        MinimumSize = new Size(100, 38);
        Margin = new Padding(0, 0, 8, 0);
        Padding = new Padding(12, 8, 12, 8);
        box.BorderStyle = BorderStyle.None;
        box.BackColor = SystemInformation.HighContrast ? SystemColors.Window : VisualTheme.Surface;
        box.ForeColor = VisualTheme.Foreground;
        box.Dock = DockStyle.Fill;
        Controls.Add(box);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = VisualTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6 * DeviceDpi / 96f);
        using var brush = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window : VisualTheme.Surface);
        using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : ContainsFocus ? VisualTheme.Focus : VisualTheme.Border);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ThemeCheckBox : CheckBox
{
    internal ThemeCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = true;
        Padding = new Padding(0, 3, 0, 3);
        Cursor = Cursors.Hand;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? VisualTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int side = (int)(16 * DeviceDpi / 96f);
        var box = new Rectangle(1, (Height - side) / 2, side, side);
        using var path = VisualTheme.Rounded(box, 4 * DeviceDpi / 96f);
        using var fill = new SolidBrush(VisualTheme.Surface);
        using var border = new Pen(Focused ? VisualTheme.Focus : VisualTheme.Border, Focused ? 2 : 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        Color text = Enabled ? VisualTheme.Text : VisualTheme.Muted;
        if (Checked)
        {
            using var check = new Pen(text, 2 * DeviceDpi / 96f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(check, [new PointF(box.Left + side * .23f, box.Top + side * .5f), new PointF(box.Left + side * .43f, box.Top + side * .72f), new PointF(box.Left + side * .8f, box.Top + side * .28f)]);
        }
        var label = new Rectangle(side + (int)(8 * DeviceDpi / 96f), 0, Width - side - (int)(8 * DeviceDpi / 96f), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, label, text, TextFormatFlags.VerticalCenter | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + (int)(28 * DeviceDpi / 96f), Math.Max(text.Height + Padding.Vertical, (int)(24 * DeviceDpi / 96f)));
    }
}

internal sealed class ThemeProgress : ProgressBar
{
    internal ThemeProgress() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var background = SystemInformation.HighContrast ? SystemColors.Control : VisualTheme.Surface;
        e.Graphics.Clear(background);
        int width = Maximum > Minimum ? (int)(Width * (double)(Value - Minimum) / (Maximum - Minimum)) : 0;
        using var brush = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : VisualTheme.PrimaryBorder);
        e.Graphics.FillRectangle(brush, 0, 0, width, Height);
    }
}
