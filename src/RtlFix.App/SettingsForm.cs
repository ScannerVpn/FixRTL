using System.Windows.Forms;
using RtlFix.Core.Transforms;

namespace RtlFix.App;

/// <summary>
/// Every option, plus a live preview: the difference between the two output modes is invisible in a
/// list of check boxes, and obvious the moment the same line is rendered both ways.
/// </summary>
sealed class SettingsForm : Form
{
    readonly RtlSettings settings;
    readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox detectTarget = Check("Reorder for apps that cannot display bidi");
    readonly CheckBox paragraphMark = Check("Add a paragraph direction mark");
    readonly CheckBox isolate = Check("Wrap Latin runs in isolates");
    readonly CheckBox shape = Check("Join Persian letters (visual order only)");
    readonly CheckBox mirror = Check("Mirror brackets (visual order only)");
    readonly CheckBox restoreClipboard = Check("Put the clipboard back after pasting");
    readonly CheckBox runAtStartup = Check("Start with Windows");
    readonly TextBox blindApps = new();
    readonly TextBox previewInput = new();
    readonly TextBox previewRepair = new();
    readonly DumbRenderBox previewVisual = new() { Padding = new Padding(4) };

    readonly (RtlAction Action, string Label, TextBox Box)[] hotkeys =
    [
        (RtlAction.QuickWriter, "Quick RTL writer", new TextBox()),
        (RtlAction.FixClipboard, "Fix clipboard", new TextBox()),
        (RtlAction.CopyFixPaste, "Copy, fix, paste", new TextBox()),
        (RtlAction.PasteVisual, "Paste visual", new TextBox()),
        (RtlAction.ToggleMode, "Toggle mode", new TextBox()),
        (RtlAction.ReadSelection, "Read selection RTL", new TextBox()),
    ];

    /// <summary>What was edited: <see cref="Accept"/> is the only place that writes into it.</summary>
    public RtlSettings Settings => settings;

    public SettingsForm(RtlSettings settings)
    {
        this.settings = settings;

        Text = "RtlFix settings";
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(940, 600);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildOptions());
        split.Panel2.Controls.Add(BuildPreview());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        ok.Click += (_, _) => Accept();
        var btnManager = new Button { Text = "🎯 مدیریت برنامه‌ها...", AutoSize = true };
        btnManager.Click += (_, _) =>
        {
            using var mgr = new AppManagerForm(settings, new SettingsStore(), () => { });
            mgr.ShowDialog(this);
            LoadValues();
        };
        buttons.Controls.AddRange([ok, new Button { Text = "Cancel", DialogResult = DialogResult.Cancel }, btnManager]);

        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = ok;
        Shown += (_, _) => split.SplitterDistance = ClientSize.Width * 42 / 100;

        LoadValues();
        RefreshPreview();
    }

    GroupBox BuildOptions()
    {
        var box = new GroupBox { Text = "Options", Dock = DockStyle.Fill, Padding = new Padding(10) };

        mode.Items.AddRange([
            "Automatic (look at the target app)",
            "Directional marks (Word, browsers, chat)",
            "Visual order (Notepad, terminals, games)",
        ]);

        var checks = new[] { detectTarget, paragraphMark, isolate, shape, mirror, restoreClipboard, runAtStartup };
        var layout = Grid(2, 1 + checks.Length + hotkeys.Length + 2, 140);
        var row = 0;

        Field(layout, row++, "Output mode", mode);
        foreach (var check in checks)
            Span(layout, row++, check);
        foreach (var (label, editor) in hotkeys.Select(h => (h.Label, h.Box)))
            Field(layout, row++, label, editor);

        blindApps.Multiline = true;
        blindApps.ScrollBars = ScrollBars.Vertical;
        Field(layout, row++, "Reorder for", blindApps);
        Span(layout, row, new Label
        {
            Text = "Process names, one per line, without .exe — Notepad, terminals, game chat.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        });

        // The list of applications is the one row worth growing with the window.
        layout.RowStyles[checks.Length + hotkeys.Length + 1] = new RowStyle(SizeType.Percent, 100);
        box.Controls.Add(layout);
        return box;
    }

    GroupBox BuildPreview()
    {
        var box = new GroupBox
        {
            Text = "Preview — type or paste a line to compare the two outputs",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };

        var layout = Grid(1, 6, 0);
        Caption(layout, 0, "Original, as written (logical order)");
        Editor(layout, 1, previewInput, readOnly: false);
        Caption(layout, 2, "Directional marks — stays editable and searchable");
        Editor(layout, 3, previewRepair, readOnly: true);
        Caption(layout, 4, "Visual order — drawn here the way a renderer with no bidi support shows it");
        Editor(layout, 5, previewVisual);

        previewInput.TextChanged += (_, _) => RefreshPreview();
        box.Controls.Add(layout);
        return box;
    }

    static TableLayoutPanel Grid(int columns, int rows, int labelWidth)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows,
        };
        if (labelWidth > 0)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        for (var row = 0; row < rows; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    static void Field(TableLayoutPanel layout, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 9, 3, 3) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    static void Span(TableLayoutPanel layout, int row, Control control)
    {
        layout.SetColumnSpan(control, layout.ColumnCount);
        control.Margin = new Padding(3, 4, 3, 4);
        layout.Controls.Add(control, 0, row);
    }

    static void Caption(TableLayoutPanel layout, int row, string text) => Span(layout, row, new Label
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Margin = new Padding(3, 6, 3, 2),
    });

    static void Editor(TableLayoutPanel layout, int row, Control box, bool readOnly = false)
    {
        if (box is TextBox text)
        {
            text.Multiline = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.ReadOnly = readOnly;
        }
        box.Dock = DockStyle.Fill;
        box.BackColor = readOnly ? SystemColors.Control : SystemColors.Window;
        layout.RowStyles[row] = new RowStyle(SizeType.Percent, 33);
        Span(layout, row, box);
    }

    static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    void RefreshPreview()
    {
        previewRepair.Text = ShowMarks(RtlTransform.Rtlize(previewInput.Text,
            settings.Options() with { Mode = RtlMode.Repair }));
        previewVisual.Display = RtlTransform.Rtlize(previewInput.Text,
            settings.Options() with { Mode = RtlMode.Visual });
    }

    /// <summary>
    /// The repair marks are invisible by design, which makes a preview of them useless: name them so
    /// the reader can see what the transform actually inserted.
    /// </summary>
    static string ShowMarks(string text) => text
        .Replace("\u200F", "[RLM]")
        .Replace("\u2066", "[LRI]")
        .Replace("\u2067", "[RLI]")
        .Replace("\u2069", "[PDI]");

    void LoadValues()
    {
        mode.SelectedIndex = (int)settings.Mode;
        detectTarget.Checked = settings.DetectTargetApp;
        paragraphMark.Checked = settings.ParagraphMark;
        isolate.Checked = settings.IsolateLeftToRightRuns;
        shape.Checked = settings.Shape;
        mirror.Checked = settings.Mirror;
        restoreClipboard.Checked = settings.RestoreClipboard;
        runAtStartup.Checked = Startup.IsEnabled();
        blindApps.Text = string.Join(Environment.NewLine, settings.BidiBlindApps);

        foreach (var (action, _, editor) in hotkeys)
            editor.Text = settings.Keys.TryGetValue(action, out var chord) ? chord : string.Empty;

        previewInput.Text = Sample.MixedLine;
    }

    void Accept()
    {
        settings.Mode = (RtlMode)mode.SelectedIndex;
        settings.DetectTargetApp = detectTarget.Checked;
        settings.ParagraphMark = paragraphMark.Checked;
        settings.IsolateLeftToRightRuns = isolate.Checked;
        settings.Shape = shape.Checked;
        settings.Mirror = mirror.Checked;
        settings.RestoreClipboard = restoreClipboard.Checked;
        settings.BidiBlindApps = blindApps.Lines
            .Select(line => line.Trim().ToLowerInvariant().Replace(".exe", string.Empty))
            .Where(line => line.Length > 0)
            .Distinct()
            .ToList();

        foreach (var (action, _, editor) in hotkeys)
            settings.Keys[action] = editor.Text.Trim();

        // Writing the run key is the one change here that reaches outside the application, so it waits
        // for the box to disagree with what is actually registered.
        if (runAtStartup.Checked != Startup.IsEnabled())
            Startup.SetEnabled(runAtStartup.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }
}
