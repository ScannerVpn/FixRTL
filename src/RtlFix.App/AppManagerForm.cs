using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RtlFix.App;

/// <summary>
/// A dedicated App Manager interface that allows users (and anyone cloning from GitHub)
/// to easily view, auto-scan, and add any application for automatic, persistent RTL rendering.
/// </summary>
sealed class AppManagerForm : Form
{
    readonly RtlSettings settings;
    readonly SettingsStore store;
    readonly Action onSettingsChanged;

    readonly ListView appList = new();
    readonly Button btnAutoScan = new();
    readonly Button btnAddRunning = new();
    readonly Button btnAddFile = new();
    readonly Button btnRefresh = new();
    readonly Button btnQuickWriter = new();
    readonly Button btnInject = new();
    readonly Button btnRemove = new();
    readonly Button btnClose = new();
    readonly Label lblStatus = new();

    public AppManagerForm(RtlSettings settings, SettingsStore store, Action onSettingsChanged)
    {
        this.settings = settings;
        this.store = store;
        this.onSettingsChanged = onSettingsChanged;

        Text = "مدیریت برنامه‌ها و راست‌چین‌سازی خودکار (RtlFix App Manager)";
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(680, 440);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.FromArgb(248, 249, 250);

        BuildUi();
        LoadApps();
    }

    void BuildUi()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Toolbar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // List
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Bottom actions

        // 1. Header
        var headerPanel = new Panel { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        var lblTitle = new Label
        {
            Text = "🎯 مدیریت برنامه‌ها و راست‌چین‌سازی خودکار",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
            AutoSize = true,
            Dock = DockStyle.Top,
        };
        var lblSub = new Label
        {
            Text = "تمام برنامه‌های شناسایی‌شده روی ویندوز. برای افزودن هر نرم‌افزار جدید کافیست از دکمه‌های زیر استفاده کنید تا راست‌چینی خودکار روی آن فعال شود.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Margin = new Padding(0, 4, 0, 0),
        };
        headerPanel.Controls.Add(lblSub);
        headerPanel.Controls.Add(lblTitle);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // 2. Toolbar
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 0, 0, 8),
        };

        StyleButton(btnAutoScan, "⚡ اسکن خودکار همه برنامه‌ها", Color.FromArgb(14, 116, 144), Color.White);
        btnAutoScan.Click += async (_, _) => await RunAutoScanAsync();

        StyleButton(btnAddRunning, "➕ افزودن از برنامه‌های باز", Color.FromArgb(37, 99, 235), Color.White);
        btnAddRunning.Click += (_, _) => ShowRunningAppsDialog();

        StyleButton(btnAddFile, "📂 انتخاب فایل (.exe / .lnk)", Color.FromArgb(51, 65, 85), Color.White);
        btnAddFile.Click += (_, _) => BrowseAndAddFile();

        StyleButton(btnRefresh, "🔄 بروزرسانی", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59));
        btnRefresh.Click += (_, _) => LoadApps();

        StyleButton(btnQuickWriter, "⌨️ تایپ در ترمینال (Ctrl+Alt+Space)", Color.FromArgb(16, 185, 129), Color.White);
        btnQuickWriter.Click += (_, _) =>
        {
            var service = new TextService(settings, new NotifyIcon());
            service.OpenQuickWriter();
        };

        toolbar.Controls.AddRange([btnAutoScan, btnAddRunning, btnAddFile, btnRefresh, btnQuickWriter]);
        mainLayout.Controls.Add(toolbar, 0, 1);

        // 3. ListView
        appList.Dock = DockStyle.Fill;
        appList.View = View.Details;
        appList.FullRowSelect = true;
        appList.GridLines = true;
        appList.MultiSelect = false;
        appList.Font = new Font("Segoe UI", 9.5f);
        appList.Columns.Add("نام برنامه", 180, HorizontalAlignment.Right);
        appList.Columns.Add("پروسه", 120, HorizontalAlignment.Left);
        appList.Columns.Add("موتور / نوع", 130, HorizontalAlignment.Right);
        appList.Columns.Add("پورت دیباگ", 90, HorizontalAlignment.Center);
        appList.Columns.Add("وضعیت اتصال", 170, HorizontalAlignment.Right);
        appList.SelectedIndexChanged += (_, _) => UpdateButtonStates();
        mainLayout.Controls.Add(appList, 0, 2);

        // 4. Bottom Panel
        var bottomLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        lblStatus.Text = "در حال بارگذاری لیست برنامه‌ها...";
        lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
        lblStatus.AutoSize = true;
        lblStatus.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        lblStatus.Margin = new Padding(0, 8, 0, 0);

        var bottomButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };

        StyleButton(btnClose, "بستن", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59));
        btnClose.Click += (_, _) => Close();

        StyleButton(btnRemove, "🗑️ حذف از لیست", Color.FromArgb(239, 68, 68), Color.White);
        btnRemove.Click += (_, _) => RemoveSelectedApp();

        StyleButton(btnInject, "⚡ تست و تزریق زنده", Color.FromArgb(16, 185, 129), Color.White);
        btnInject.Click += async (_, _) => await InjectSelectedAppAsync();

        bottomButtons.Controls.AddRange([btnClose, btnRemove, btnInject]);

        bottomLayout.Controls.Add(lblStatus, 0, 0);
        bottomLayout.Controls.Add(bottomButtons, 1, 0);
        mainLayout.Controls.Add(bottomLayout, 0, 3);

        Controls.Add(mainLayout);
        UpdateButtonStates();
    }

    static void StyleButton(Button btn, string text, Color bg, Color fg)
    {
        btn.Text = text;
        btn.BackColor = bg;
        btn.ForeColor = fg;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.Cursor = Cursors.Hand;
        btn.AutoSize = true;
        btn.Padding = new Padding(10, 6, 10, 6);
        btn.Margin = new Padding(0, 0, 8, 0);
        btn.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
    }

    void UpdateButtonStates()
    {
        var hasSelection = appList.SelectedItems.Count > 0;
        btnInject.Enabled = hasSelection;
        btnRemove.Enabled = hasSelection;
    }

    void LoadApps()
    {
        appList.Items.Clear();
        var apps = DesktopAppIntegrator.GetManagedApps(settings);
        var connectedCount = 0;

        foreach (var app in apps)
        {
            var item = new ListViewItem(app.Name) { Tag = app };
            item.SubItems.Add(app.ProcessName);
            item.SubItems.Add(app.AppType);
            item.SubItems.Add(app.Port > 0 ? app.Port.ToString() : "-");

            string statusText;
            Color statusColor;

            if (app.IsConnected)
            {
                statusText = "🟢 متصل و راست‌چین زنده";
                statusColor = Color.DarkGreen;
                connectedCount++;
            }
            else if (app.IsRunning)
            {
                statusText = app.Port > 0 ? "🟡 در حال اجرا (برای زنده، از شورت‌کات باز شود)" : "🟢 فعال (با Ctrl+Alt+Space یا Ctrl+Alt+V)";
                statusColor = app.Port > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            }
            else
            {
                statusText = "⚪ آماده (شورت‌کات تنظیم شده)";
                statusColor = Color.Gray;
            }

            var subItem = item.SubItems.Add(statusText);
            subItem.ForeColor = statusColor;
            appList.Items.Add(item);
        }

        lblStatus.Text = $"تعداد برنامه‌ها: {apps.Count} | برنامه‌های متصل لحظه‌ای: {connectedCount}";
        UpdateButtonStates();
    }

    async Task RunAutoScanAsync()
    {
        btnAutoScan.Enabled = false;
        btnAutoScan.Text = "در حال اسکن و پیکربندی...";
        try
        {
            await Task.Run(() =>
            {
                DesktopAppIntegrator.ConfigureAllElectronShortcuts();
                DesktopAppIntegrator.EnableAll(out _);
            });
            await DesktopAppIntegrator.InjectAllActiveCdpAppsAsync(silent: true);
            LoadApps();
            MessageBox.Show(this, "اسکن تمام برنامه‌های سیستم انجام شد و شورت‌کات‌ها با موفقیت پیکربندی شدند.", "موفقیت", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"خطا در اسکن خودکار: {ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnAutoScan.Enabled = true;
            btnAutoScan.Text = "⚡ اسکن خودکار همه برنامه‌ها";
        }
    }

    void ShowRunningAppsDialog()
    {
        var running = DesktopAppIntegrator.GetRunningWindowApps();
        if (running.Count == 0)
        {
            MessageBox.Show(this, "برنامه باز معتبری در دسکتاپ یافت نشد.", "توجه", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new Form
        {
            Text = "انتخاب برنامه در حال اجرا جهت راست‌چین‌سازی",
            Font = new Font("Segoe UI", 9.5f),
            ClientSize = new Size(540, 420),
            StartPosition = FormStartPosition.CenterParent,
            RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = true,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text = "برنامه مورد نظر خود را از لیست پنجره‌های باز انتخاب کنید:",
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8),
        };

        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f) };
        foreach (var (proc, title, path) in running)
        {
            list.Items.Add($"{proc} — {title}");
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var ok = new Button { Text = "افزودن و اعمال راست‌چین", DialogResult = DialogResult.OK, AutoSize = true, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "انصراف", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat };
        btnPanel.Controls.AddRange([cancel, ok]);

        layout.Controls.Add(lbl, 0, 0);
        layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(btnPanel, 0, 2);
        dlg.Controls.Add(layout);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        if (dlg.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
        {
            var selected = running[list.SelectedIndex];
            var target = !string.IsNullOrEmpty(selected.FilePath) ? selected.FilePath : selected.ProcessName;
            if (DesktopAppIntegrator.RegisterApp(target, settings, store, out var msg))
            {
                onSettingsChanged();
                LoadApps();
                MessageBox.Show(this, msg, "افزودن برنامه", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, msg, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    void BrowseAndAddFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "انتخاب فایل اجرایی برنامه یا میانبر آن",
            Filter = "برنامه‌ها و میانبرها (*.exe;*.lnk)|*.exe;*.lnk|تمام فایل‌ها (*.*)|*.*",
            Multiselect = false,
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            if (DesktopAppIntegrator.RegisterApp(ofd.FileName, settings, store, out var msg))
            {
                onSettingsChanged();
                LoadApps();
                MessageBox.Show(this, msg, "افزودن برنامه", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, msg, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    async Task InjectSelectedAppAsync()
    {
        if (appList.SelectedItems.Count == 0) return;
        var app = (ManagedAppInfo)appList.SelectedItems[0].Tag!;

        if (app.Port > 0)
        {
            btnInject.Enabled = false;
            btnInject.Text = "در حال تزریق...";
            try
            {
                var port = app.Port;
                var ok = await DesktopAppIntegrator.InjectCdpAsync(port, app.Name, silent: false);

                // The stored port belongs to the shortcut's launch. If it is silent, ask the
                // running process itself how it was started — the flag travels on the command
                // line wherever the app was launched from.
                if (!ok)
                {
                    var discovered = await DesktopAppIntegrator.DiscoverCdpPortAsync(app.ProcessName);
                    if (discovered > 0 && discovered != port)
                    {
                        port = discovered;
                        ok = await DesktopAppIntegrator.InjectCdpAsync(port, app.Name, silent: false);
                    }
                }

                if (!ok && IsProcessRunning(app.ProcessName))
                {
                    // The app is up but no listener anywhere: only a restart with the flag opens
                    // one. Offer to do it here instead of telling the user how to launch things.
                    var confirm = MessageBox.Show(this,
                        $"برنامه {app.Name} اجرا است ولی پورت دیباگ باز نیست. خودم یکبار ببندم و با پورت باز کنم؟",
                        "بازکردن با پورت", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    if (confirm == DialogResult.Yes)
                    {
                        btnInject.Text = "در حال بازکردن مجدد...";
                        ok = await DesktopAppIntegrator.RestartAppWithDebugPortAsync(app.ProcessName, app.TargetPath, port);
                        if (ok) ok = await DesktopAppIntegrator.InjectCdpAsync(port, app.Name, silent: false);
                    }
                }

                if (ok)
                {
                    MessageBox.Show(this, $"تزریق زنده استایل‌های راست‌چین به {app.Name} با موفقیت انجام شد!", "تزریق موفق", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
                else if (!IsProcessRunning(app.ProcessName))
                {
                    MessageBox.Show(this, $"برنامه {app.Name} اجرا نیست. وقتی بازش کنی، تزریق زنده خودکار وصل می‌شود.", "عدم اتصال", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
                else
                {
                    MessageBox.Show(this, $"اتصال به {app.Name} برقرار نشد. watcher پس‌زمینه هر ۳ ثانیه دوباره تلاش می‌کند.", "عدم اتصال", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
            }
            finally
            {
                btnInject.Enabled = true;
                btnInject.Text = "⚡ تست و تزریق زنده";
                LoadApps();
            }
        }
        else
        {
            MessageBox.Show(this, $"برنامه {app.Name} از نوع نیتیو/کنسول است و راست‌نویسی آن از طریق کلیدهای Ctrl+Alt+Space و Ctrl+Alt+V به صورت مستقیم درجا اعمال می‌شود.", "اطلاعیه", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }

    static bool IsProcessRunning(string processName)
    {
        try { return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    void RemoveSelectedApp()
    {
        if (appList.SelectedItems.Count == 0) return;
        var app = (ManagedAppInfo)appList.SelectedItems[0].Tag!;

        var confirm = MessageBox.Show(this, $"آیا از حذف {app.Name} از لیست اطمینان دارید؟", "تایید حذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        DesktopAppIntegrator.UnregisterApp(app.ProcessName, settings, store, out var msg);
        onSettingsChanged();
        LoadApps();
        MessageBox.Show(this, msg, "حذف شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
