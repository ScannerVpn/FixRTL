using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RtlFix.App;

/// <summary>
/// A sleek, borderless in-place overlay that appears directly on top of or next to the user's
/// selected text. It displays Persian/Arabic text right-to-left with correct punctuation,
/// proper reading order, and one-click copy, without feeling like an external window.
/// </summary>
sealed class RtlReader : Form
{
    readonly TextBox canvas = new();
    readonly Panel topBar = new();
    readonly Label lblTitle = new();
    readonly Button btnCopy = new();
    readonly Button btnClose = new();
    readonly string rawText;
    Point dragOffset;
    bool dragging;

    public RtlReader(string text)
    {
        rawText = text;
        var displayText = CleanText(text).ReplaceLineEndings();

        // 1. Frameless, top-most, borderless in-place overlay
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(20, 24, 33);
        ForeColor = Color.FromArgb(240, 244, 250);

        // 2. Measure and calculate snug ideal size based on text
        var size = CalculateIdealSize(displayText);
        ClientSize = size;

        // 3. Position in-place near the cursor / active text area
        Location = CalculateBestPosition(size);

        // 4. Setup Controls
        SetupTopBar();
        SetupCanvas(displayText);

        // 5. Keyboard handling
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        canvas.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // 6. Dismiss when clicking outside back to the main app
        Deactivate += (_, _) => Close();
    }

    void SetupTopBar()
    {
        topBar.Height = 32;
        topBar.Dock = DockStyle.Top;
        topBar.BackColor = Color.FromArgb(28, 33, 44);
        topBar.Padding = new Padding(8, 4, 8, 4);

        lblTitle.Text = "RtlFix · راست‌چین درجا";
        lblTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(148, 163, 184);
        lblTitle.AutoSize = true;
        lblTitle.Dock = DockStyle.Right;

        btnClose.Text = "✕";
        btnClose.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnClose.BackColor = Color.Transparent;
        btnClose.ForeColor = Color.FromArgb(156, 163, 175);
        btnClose.FlatStyle = FlatStyle.Flat;
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Cursor = Cursors.Hand;
        btnClose.Size = new Size(26, 24);
        btnClose.Dock = DockStyle.Left;
        btnClose.Click += (_, _) => Close();

        btnCopy.Text = "کپی";
        btnCopy.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnCopy.BackColor = Color.FromArgb(37, 99, 235);
        btnCopy.ForeColor = Color.White;
        btnCopy.FlatStyle = FlatStyle.Flat;
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Cursor = Cursors.Hand;
        btnCopy.Size = new Size(54, 24);
        btnCopy.Dock = DockStyle.Left;
        btnCopy.Click += (_, _) =>
        {
            ClipboardText.Write(rawText);
            btnCopy.Text = "کپی شد! ✓";
            btnCopy.BackColor = Color.FromArgb(34, 197, 94);
            var timer = new System.Windows.Forms.Timer { Interval = 1400 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!IsDisposed)
                {
                    btnCopy.Text = "کپی";
                    btnCopy.BackColor = Color.FromArgb(37, 99, 235);
                }
            };
            timer.Start();
        };

        topBar.Controls.Add(lblTitle);
        topBar.Controls.Add(btnCopy);
        topBar.Controls.Add(btnClose);

        // Allow dragging the overlay by the header bar
        topBar.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragOffset = e.Location;
            }
        };
        topBar.MouseMove += (_, e) =>
        {
            if (dragging)
                Location = new Point(Location.X + e.X - dragOffset.X, Location.Y + e.Y - dragOffset.Y);
        };
        topBar.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) dragging = false;
        };

        Controls.Add(topBar);
    }

    void SetupCanvas(string text)
    {
        canvas.Multiline = true;
        canvas.ReadOnly = true;
        canvas.BorderStyle = BorderStyle.None;
        canvas.Dock = DockStyle.Fill;
        canvas.ScrollBars = ScrollBars.Vertical;
        canvas.RightToLeft = RightToLeft.Yes;
        canvas.TextAlign = HorizontalAlignment.Right;
        canvas.Font = GetPersianFont(12.5f);
        canvas.BackColor = Color.FromArgb(17, 20, 28);
        canvas.ForeColor = Color.FromArgb(240, 244, 250);
        canvas.Text = text;
        canvas.Select(0, 0);

        Controls.Add(canvas);
        canvas.BringToFront();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Draw modern 1px accent border around the frameless form
        using var pen = new Pen(Color.FromArgb(59, 130, 246), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    static Size CalculateIdealSize(string text)
    {
        var lines = text.Split('\n');
        var maxLineLength = lines.Length > 0 ? lines.Max(l => l.Length) : 0;
        var lineCount = Math.Max(lines.Length, text.Length / 55 + 1);

        int width = Math.Clamp(maxLineLength * 9 + 80, 420, 780);
        int height = Math.Clamp(lineCount * 26 + 70, 130, 480);

        return new Size(width, height);
    }

    static Point CalculateBestPosition(Size size)
    {
        var mouse = Cursor.Position;
        var screen = Screen.FromPoint(mouse);
        var area = screen.WorkingArea;

        int x = mouse.X - (size.Width / 2);
        int y = mouse.Y - 20;

        if (x + size.Width > area.Right - 16) x = area.Right - size.Width - 16;
        if (x < area.Left + 16) x = area.Left + 16;

        if (y + size.Height > area.Bottom - 16)
            y = mouse.Y - size.Height - 16;
        if (y < area.Top + 16)
            y = area.Top + 16;

        return new Point(x, y);
    }

    static Font GetPersianFont(float size)
    {
        string[] candidates = ["Vazirmatn", "IRANSans", "Vazir", "Segoe UI", "Tahoma"];
        foreach (var name in candidates)
        {
            try
            {
                using var family = new FontFamily(name);
                if (family.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return new Font(name, size, FontStyle.Regular);
            }
            catch { }
        }
        return new Font("Segoe UI", size, FontStyle.Regular);
    }

    static string CleanText(string text) =>
        text.Replace("\u2066", "")
            .Replace("\u2067", "")
            .Replace("\u2068", "")
            .Replace("\u2069", "")
            .Replace("\u200F", "")
            .Replace("\u200E", "");
}
