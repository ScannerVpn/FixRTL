using System.Windows.Forms;
using RtlFix.Core.Transforms;

namespace RtlFix.App;

/// <summary>
/// The tray icon, its menu, and the hotkeys that drive the two text operations.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    readonly SettingsStore store;
    readonly RtlSettings settings;
    readonly NotifyIcon icon;
    readonly HotkeyManager hotkeys = new();
    readonly TextService service;
    readonly ToolStripMenuItem modeMenu = new("Output mode");
    readonly CancellationTokenSource watcherCts = new();
    AppManagerForm? managerForm;
    bool paused;

    public TrayApp(SettingsStore store)
    {
        Log.Write("started");
        this.store = store;
        settings = store.Load();

        icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        service = new TextService(settings, icon);
        icon.ContextMenuStrip = BuildMenu();
        icon.DoubleClick += (_, _) => OpenAppManager();
        RefreshTitle();

        hotkeys.Triggered = Run;
        foreach (var failure in ApplyHotkeys())
        {
            Log.Write($"hotkey rejected: {failure}");
            icon.ShowBalloonTip(3000, "RtlFix", failure, ToolTipIcon.Warning);
        }
        Log.Write($"started, mode {settings.Mode}");

        // Auto-integrate in-place RTL into installed desktop applications (background, non-blocking)
        Task.Run(() =>
        {
            try
            {
                foreach (var app in DesktopAppIntegrator.GetSupportedApps())
                {
                    if (app.Id == "qoder") continue;
                    if (app.IsInstalled && !app.IsEnabled)
                    {
                        if (DesktopAppIntegrator.EnableRtl(app.Id, out var msg))
                        {
                            Log.Write($"auto-integrated {app.DisplayName}: {msg}");
                        }
                    }
                }
            }
            catch { }
        });

        // Real-time live watcher for active coding assistants (Qoder, Antigravity, etc.)
        DesktopAppIntegrator.StartLiveWatcher(watcherCts.Token);

        // Real-time console buffer shaping engine for terminal output
        ConsoleBufferEngine.Start();
    }

    public IReadOnlyList<string> ApplyHotkeys() => hotkeys.Apply(
        settings.Keys.Select(pair => new Hotkey(pair.Value, pair.Key)).ToArray());

    void Run(RtlAction action)
    {
        Log.Write($"run: {action}{(paused ? " (paused)" : "")}");
        if (paused) return;
        switch (action)
        {
            case RtlAction.FixClipboard:
                service.FixClipboard();
                break;
            case RtlAction.CopyFixPaste:
                service.CopyFixPaste();
                break;
            case RtlAction.PasteVisual:
                service.CopyFixPaste(forceVisual: true);
                break;
            case RtlAction.ToggleMode:
                service.ToggleMode();
                RefreshTitle();
                break;
            case RtlAction.ReadSelection:
                service.ReadSelection();
                break;
            case RtlAction.QuickWriter:
                service.OpenQuickWriter();
                break;
        }
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(Item("🎯 مدیریت و افزودن برنامه‌ها... (App Manager)", () => OpenAppManager()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("⚡ راست‌نویس سریع (درجا)... [Ctrl+Alt+Space]", () => service.OpenQuickWriter()));
        menu.Items.Add(new ToolStripSeparator());

        foreach (var (mode, label) in new[]
        {
            (RtlMode.Auto, "Automatic"),
            (RtlMode.Repair, "Directional marks"),
            (RtlMode.Visual, "Visual order"),
        })
        {
            var item = new ToolStripMenuItem(label)
            {
                Tag = mode,
                Checked = settings.Mode == mode,
            };
            item.Click += (_, _) =>
            {
                settings.Mode = mode;
                store.Save(settings);
                foreach (ToolStripMenuItem sibling in modeMenu.DropDownItems)
                    sibling.Checked = Equals(sibling.Tag, mode);
                RefreshTitle();
            };
            modeMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(modeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Fix the clipboard", () => Run(RtlAction.FixClipboard)));
        menu.Items.Add(Item("Copy, fix, paste back", () => Run(RtlAction.CopyFixPaste)));
        menu.Items.Add(Item("Paste in visual order", () => Run(RtlAction.PasteVisual)));
        menu.Items.Add(Item("Read the selection right-to-left", () => service.ReadSelection()));

        var desktopMenu = new ToolStripMenuItem("راست‌چین‌سازی برنامه‌های کدنویسی و ترمینال (درجا)");

        var enableAllItem = new ToolStripMenuItem("⚡ فعال‌سازی همه برنامه‌ها با یک کلیک");
        enableAllItem.Click += (_, _) =>
        {
            var ok = DesktopAppIntegrator.EnableAll(out var results);
            var summary = string.Join("\n", results);
            icon.ShowBalloonTip(4000, "RtlFix", ok ? "تمام برنامه‌ها با موفقیت راست‌چین شدند!" : "برخی برنامه‌ها فعال شدند:\n" + summary, ToolTipIcon.Info);
        };
        desktopMenu.DropDownItems.Add(enableAllItem);
        desktopMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var app in DesktopAppIntegrator.GetSupportedApps())
        {
            var appItem = new ToolStripMenuItem($"{app.DisplayName} [{app.Category}]")
            {
                CheckOnClick = true,
                Checked = app.IsEnabled,
                Enabled = app.IsInstalled,
            };
            appItem.Click += (_, _) =>
            {
                if (appItem.Checked)
                {
                    DesktopAppIntegrator.EnableRtl(app.Id, out var msg);
                    icon.ShowBalloonTip(3000, "RtlFix", msg, ToolTipIcon.Info);
                }
                else
                {
                    DesktopAppIntegrator.DisableRtl(app.Id, out var msg);
                    icon.ShowBalloonTip(3000, "RtlFix", msg, ToolTipIcon.Info);
                }
            };
            desktopMenu.DropDownItems.Add(appItem);
        }
        menu.Items.Add(desktopMenu);



        paused = false;
        var pause = new ToolStripMenuItem("Pause hotkeys") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) =>
        {
            paused = pause.Checked;
            if (paused) hotkeys.Apply([]);
            else ApplyHotkeys();
        };
        menu.Items.Add(pause);

        menu.Items.Add(Item("Settings…", () =>
        {
            using var form = new SettingsForm(settings);
            if (form.ShowDialog() == DialogResult.OK)
            {
                store.Save(settings);
                RefreshTitle();
                foreach (var failure in ApplyHotkeys())
                    icon.ShowBalloonTip(3000, "RtlFix", failure, ToolTipIcon.Warning);
            }
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", () =>
        {
            managerForm?.Close();
            hotkeys.Dispose();
            icon.Visible = false;
            ExitThread();
        }));
        return menu;
    }

    static ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    void OpenAppManager()
    {
        if (managerForm != null && !managerForm.IsDisposed)
        {
            managerForm.Activate();
            return;
        }
        managerForm = new AppManagerForm(settings, store, () => RefreshTitle());
        managerForm.Show();
    }

    void RefreshTitle()
    {
        var mode = settings.Mode switch
        {
            RtlMode.Visual => "visual order",
            RtlMode.Repair => "directional marks",
            _ => "automatic",
        };
        icon.Text = $"RtlFix — {mode}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ConsoleBufferEngine.Stop();
            watcherCts.Cancel();
            watcherCts.Dispose();
            hotkeys.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
