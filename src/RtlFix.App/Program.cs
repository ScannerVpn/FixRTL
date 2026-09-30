using System.Windows.Forms;

namespace RtlFix.App;

static class Program
{
    static Mutex? singleInstance;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        singleInstance = new Mutex(initiallyOwned: true, "RtlFix.TrayApp", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("RtlFix is already running in the notification area.",
                "RtlFix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var store = new SettingsStore();

        // Opening the settings window on its own makes the tray app scriptable, and makes it possible
        // to see the preview without hunting for an icon in the notification area.
        if (args.Contains("--settings"))
        {
            using var form = new SettingsForm(store.Load());
            if (form.ShowDialog() == DialogResult.OK)
                store.Save(form.Settings);
            return;
        }

        Application.Run(new TrayApp(store));

        // The mutex only has to outlive the message loop, which it does; this keeps the intent explicit.
        GC.KeepAlive(singleInstance);
    }
}
