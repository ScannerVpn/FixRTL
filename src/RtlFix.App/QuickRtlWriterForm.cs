using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RtlFix.Core.Transforms;

namespace RtlFix.App;

/// <summary>
/// A sleek, lightning-fast in-place Persian writer. Type naturally in Persian, hit Enter,
/// and it formats and writes the text directly into whatever terminal or application was active.
/// </summary>
sealed class QuickRtlWriterForm : Form
{
    readonly RtlSettings settings;
    readonly TextService service;
    readonly IntPtr targetWindow;
    readonly string targetProcess;
    readonly bool isTerminalTarget;

    readonly Panel topBar = new();
    readonly Label lblTarget = new();
    readonly Button btnMode = new();
    readonly Button btnClose = new();
    readonly TextBox input = new();
    readonly Label lblHint = new();

    bool forceVisual;
    Point dragOffset;
    bool dragging;

    public QuickRtlWriterForm(
        RtlSettings settings,
        IntPtr targetWindow,
        string? targetProcess,
        bool isTerminalTarget,
        TextService service)
    {
        this.settings = settings;
        this.service = service;
        this.targetWindow = targetWindow;
        this.targetProcess = string.IsNullOrEmpty(targetProcess) ? "برنامه فعال" : targetProcess;
        this.isTerminalTarget = isTerminalTarget;
        this.forceVisual = true;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(20, 24, 33);
        ForeColor = Color.FromArgb(240, 244, 250);
        ClientSize = new Size(540, 136);

        Location = CalculatePosition(ClientSize);

        SetupTopBar();
        SetupControls();

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };

        Shown += (_, _) =>
        {
            input.Focus();
        };

        Deactivate += (_, _) =>
        {
            // Allow user to click outside to dismiss
            Close();
        };
    }

    void SetupTopBar()
    {
        topBar.Height = 30;
        topBar.Dock = DockStyle.Top;
        topBar.BackColor = Color.FromArgb(26, 31, 43);
        topBar.Padding = new Padding(6, 3, 6, 3);

        btnClose.Text = "✕";
        btnClose.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnClose.BackColor = Color.Transparent;
        btnClose.ForeColor = Color.FromArgb(156, 163, 175);
        btnClose.FlatStyle = FlatStyle.Flat;
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Cursor = Cursors.Hand;
        btnClose.Size = new Size(24, 24);
        btnClose.Dock = DockStyle.Left;
        btnClose.Click += (_, _) => Close();

        btnMode.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        btnMode.FlatStyle = FlatStyle.Flat;
        btnMode.FlatAppearance.BorderSize = 0;
        btnMode.Cursor = Cursors.Hand;
        btnMode.AutoSize = true;
        btnMode.Dock = DockStyle.Left;
        btnMode.Margin = new Padding(4, 0, 0, 0);
        btnMode.Click += (_, _) =>
        {
            forceVisual = !forceVisual;
            UpdateModeButton();
        };
        UpdateModeButton();

        lblTarget.Text = $"نوشتن در: {targetProcess}";
        lblTarget.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        lblTarget.ForeColor = Color.FromArgb(148, 163, 184);
        lblTarget.AutoSize = true;
        lblTarget.Dock = DockStyle.Right;

        topBar.Controls.Add(lblTarget);
        topBar.Controls.Add(btnMode);
        topBar.Controls.Add(btnClose);

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

    void UpdateModeButton()
    {
        if (forceVisual)
        {
            btnMode.Text = "  حالت ویژوال (ترمینال)  ";
            btnMode.BackColor = Color.FromArgb(37, 99, 235);
            btnMode.ForeColor = Color.White;
        }
        else
        {
            btnMode.Text = "  حالت مستقیم (علامت‌دار)  ";
            btnMode.BackColor = Color.FromArgb(38, 118, 92);
            btnMode.ForeColor = Color.White;
        }
    }

    void SetupControls()
    {
        lblHint.Text = "Enter: ارسال به برنامه  •  Shift+Enter: خط بعد  •  Esc: لغو";
        lblHint.Font = new Font("Segoe UI", 8f);
        lblHint.ForeColor = Color.FromArgb(100, 116, 139);
        lblHint.Dock = DockStyle.Bottom;
        lblHint.Height = 22;
        lblHint.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(lblHint);

        var editorContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 4),
            BackColor = Color.FromArgb(20, 24, 33),
        };

        input.Multiline = true;
        input.Dock = DockStyle.Fill;
        input.Font = GetPersianFont(12.5f);
        input.RightToLeft = RightToLeft.Yes;
        input.TextAlign = HorizontalAlignment.Right;
        input.BackColor = Color.FromArgb(27, 33, 46);
        input.ForeColor = Color.FromArgb(240, 245, 255);
        input.BorderStyle = BorderStyle.None;
        input.ScrollBars = ScrollBars.Vertical;

        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Close();
            }
        };

        editorContainer.Controls.Add(input);
        Controls.Add(editorContainer);
        editorContainer.BringToFront();
    }

    void Submit()
    {
        var raw = input.Text;
        if (string.IsNullOrEmpty(raw))
        {
            Close();
            return;
        }

        Close();

        var options = settings.Options(targetIsBidiBlind: forceVisual);
        var transformed = RtlTransform.Rtlize(raw, options);

        if (targetWindow != IntPtr.Zero)
        {
            Interop.ForceSetForegroundWindow(targetWindow);
            Thread.Sleep(40);
        }

        service.PasteTextDirect(transformed, isTerminalTarget);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(59, 130, 246), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    static Point CalculatePosition(Size size)
    {
        var mouse = Cursor.Position;
        var screen = Screen.FromPoint(mouse);
        var area = screen.WorkingArea;

        int x = mouse.X - (size.Width / 2);
        int y = mouse.Y - size.Height - 12;

        if (x + size.Width > area.Right - 16) x = area.Right - size.Width - 16;
        if (x < area.Left + 16) x = area.Left + 16;

        if (y < area.Top + 16) y = mouse.Y + 24;
        if (y + size.Height > area.Bottom - 16) y = area.Bottom - size.Height - 16;

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
}
