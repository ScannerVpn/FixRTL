using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RtlFix.App;

public sealed record DesktopAppInfo(
    string Id,
    string DisplayName,
    string ProcessName,
    string TargetFilePath,
    bool IsInstalled,
    bool IsEnabled,
    string Category = "General");

public sealed record ManagedAppInfo(
    string Name,
    string ProcessName,
    string TargetPath,
    string AppType,
    int Port,
    bool IsRunning,
    bool IsConnected,
    bool IsCustom = false);

/// <summary>
/// Integrates in-place right-to-left rendering directly into popular desktop coding environments,
/// AI coding assistants (Qoder, Google Antigravity, Freebuff, VS Code, LM Studio), and terminal environments
/// so that text is right-aligned in-place on the screen without any separate popup window or manual copy-pasting.
/// </summary>
public static class DesktopAppIntegrator
{
    public const string MarkerStart = "/* rtl-fix-injected-start */";
    public const string MarkerEnd = "/* rtl-fix-injected-end */";
    public const string HtmlMarkerStart = "<!-- rtl-fix-injected-start -->";
    public const string HtmlMarkerEnd = "<!-- rtl-fix-injected-end -->";

    public const int QoderCdpPort = 54075;
    public const int AntigravityCdpPort = 17744;

    public static readonly string UniversalCss = """
        [dir="rtl"], .rtl-fix-injected {
          direction: rtl !important;
          text-align: right !important;
        }
        [dir="rtl"] p, [dir="rtl"] li, [dir="rtl"] h1, [dir="rtl"] h2, [dir="rtl"] h3,
        [dir="rtl"] h4, [dir="rtl"] h5, [dir="rtl"] h6, [dir="rtl"] blockquote {
          direction: rtl !important;
          text-align: right !important;
        }
        [dir="rtl"] ul, [dir="rtl"] ol {
          direction: rtl !important;
          text-align: right !important;
          padding-right: 1.5rem !important;
          padding-left: 0 !important;
        }
        [dir="rtl"] pre, [dir="rtl"] pre *, [dir="rtl"] code:not(.md-inline),
        [dir="rtl"] .cm-editor, [dir="rtl"] .cm-content, [dir="rtl"] .monaco-editor,
        [dir="rtl"] .xterm, [dir="rtl"] .xterm-screen, [dir="rtl"] .terminal-group,
        [dir="rtl"] [data-language], [dir="rtl"] [class*="code-block"] {
          direction: ltr !important;
          text-align: left !important;
          unicode-bidi: isolate !important;
        }
        [dir="rtl"] code.md-inline, [dir="rtl"] :not(pre) > code {
          direction: ltr !important;
          unicode-bidi: isolate !important;
          display: inline-block !important;
          vertical-align: baseline !important;
        }
        textarea[dir="rtl"], input[dir="rtl"], [contenteditable][dir="rtl"] {
          direction: rtl !important;
          text-align: right !important;
        }
        """;

    public static readonly string LiveJsPayload = """
        (() => {
          let style = document.getElementById('rtl-fix-live-styles');
          if (!style) {
            style = document.createElement('style');
            style.id = 'rtl-fix-live-styles';
            (document.head || document.documentElement).appendChild(style);
          }
          style.textContent = `
            [dir="rtl"], .rtl-live-text, .tiptap [dir="rtl"], .ProseMirror [dir="rtl"] {
              direction: rtl !important;
              text-align: right !important;
            }
            :is(p, li, h1, h2, h3, h4, blockquote, .markdown-body, .loop-rich-editor, div, span):dir(rtl) {
              direction: rtl !important;
              text-align: right !important;
            }
            ol:dir(rtl), ul:dir(rtl), [dir="rtl"] ol, [dir="rtl"] ul {
              direction: rtl !important;
              text-align: right !important;
              padding-right: 1.5rem !important;
              padding-left: 0 !important;
            }
            pre, pre *, code, .cm-editor, .cm-content, .monaco-editor, .xterm, [class*="code-block"] {
              direction: ltr !important;
              text-align: left !important;
              unicode-bidi: isolate !important;
            }
            code.md-inline, :not(pre) > code {
              direction: ltr !important;
              unicode-bidi: isolate !important;
              display: inline-block !important;
            }
          `;

          const rtlRegex = /[\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF]/;

          function applyRtl() {
            const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            let node;
            while (node = walker.nextNode()) {
              if (rtlRegex.test(node.textContent)) {
                let p = node.parentElement;
                while (p && p !== document.body) {
                  if (p.matches('pre, code, .monaco-editor, .cm-editor, .xterm')) break;
                  if (p.matches('p, li, h1, h2, h3, h4, h5, h6, blockquote, div, [role="textbox"], [contenteditable]')) {
                    if (p.getAttribute('dir') !== 'rtl') p.setAttribute('dir', 'rtl');
                    break;
                  }
                  p = p.parentElement;
                }
              }
            }
          }
          applyRtl();

          if (!window.__rtl_fix_observer) {
            window.__rtl_fix_observer = new MutationObserver((mutations) => {
              let hasAdded = false;
              for (const m of mutations) {
                if (m.addedNodes.length > 0) {
                  hasAdded = true;
                  break;
                }
              }
              if (hasAdded) applyRtl();
            });
            window.__rtl_fix_observer.observe(document.body, { childList: true, subtree: true });

            document.addEventListener('input', (e) => {
              let t = e.target;
              if (!t) return;
              if (t.nodeType === 3) t = t.parentElement;
              const el = t.closest ? t.closest('textarea, input, [contenteditable="true"], [role="textbox"], .loop-rich-editor') : t;
              if (el) {
                const val = el.value || el.textContent || '';
                if (!val.trim()) {
                  el.removeAttribute('dir');
                } else {
                  el.setAttribute('dir', rtlRegex.test(val) ? 'rtl' : 'ltr');
                }
              }
            }, { passive: true, capture: true });
          }
        })();
        """;

    public static List<DesktopAppInfo> GetSupportedApps()
    {
        var list = new List<DesktopAppInfo>();
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // 1. Qoder AI Workbench
        var qoderExe = @"G:\app installed\Qoder\Qoder.exe";
        if (!File.Exists(qoderExe))
        {
            var p = Path.Combine(localApp, "Programs", "Qoder", "Qoder.exe");
            if (File.Exists(p)) qoderExe = p;
        }
        var qoderInstalled = File.Exists(qoderExe);
        var qoderShortcutsConfigured = AreQoderShortcutsConfigured();
        var qoderRunning = Process.GetProcessesByName("Qoder").Length > 0;
        list.Add(new DesktopAppInfo(
            "qoder",
            "Qoder AI Workbench",
            "Qoder",
            qoderExe,
            qoderInstalled,
            qoderShortcutsConfigured || qoderRunning,
            "AI Assistant"));

        // 2. Google Antigravity
        var antigravityExe = Path.Combine(localApp, "Programs", "antigravity", "Antigravity.exe");
        var antigravityInstalled = File.Exists(antigravityExe);
        var antigravityRunning = Process.GetProcessesByName("Antigravity").Length > 0;
        list.Add(new DesktopAppInfo(
            "antigravity",
            "Google Antigravity",
            "Antigravity",
            antigravityExe,
            antigravityInstalled || antigravityRunning,
            antigravityRunning,
            "AI Assistant"));

        // 3. Freebuff Desktop
        var freebuffUi = Path.Combine(localApp, "Programs", "@codebufffreebuff-desktop", "resources", "orchestrator", "ui", "index.html");
        var freebuffInstalled = File.Exists(freebuffUi);
        var freebuffEnabled = freebuffInstalled && File.ReadAllText(freebuffUi).Contains(HtmlMarkerStart);
        list.Add(new DesktopAppInfo(
            "freebuff",
            "Freebuff Desktop",
            "Freebuff",
            freebuffUi,
            freebuffInstalled,
            freebuffEnabled,
            "AI Assistant"));

        // 4. Microsoft Visual Studio Code
        var vsCodeCss = FindVsCodeWorkbenchCss();
        var vsCodeInstalled = !string.IsNullOrEmpty(vsCodeCss) && File.Exists(vsCodeCss);
        var vsCodeEnabled = vsCodeInstalled && vsCodeCss is not null && File.ReadAllText(vsCodeCss).Contains(MarkerStart);
        list.Add(new DesktopAppInfo(
            "vscode",
            "Microsoft Visual Studio Code",
            "Code",
            vsCodeCss ?? "",
            vsCodeInstalled,
            vsCodeEnabled,
            "IDE"));

        // 5. LM Studio
        var lmStudioUi = @"G:\app installed\LM Studio\resources\app\.webpack\renderer\index.html";
        if (!File.Exists(lmStudioUi))
        {
            var p = Path.Combine(localApp, "Programs", "LM Studio", "resources", "app", ".webpack", "renderer", "index.html");
            if (File.Exists(p)) lmStudioUi = p;
        }
        var lmStudioInstalled = File.Exists(lmStudioUi);
        var lmStudioEnabled = lmStudioInstalled && File.ReadAllText(lmStudioUi).Contains(HtmlMarkerStart);
        list.Add(new DesktopAppInfo(
            "lmstudio",
            "LM Studio",
            "LM Studio",
            lmStudioUi,
            lmStudioInstalled,
            lmStudioEnabled,
            "AI Assistant"));

        // 6. Windows Terminal (Native Bidi)
        var winTermSettings = FindWindowsTerminalSettings();
        var winTermInstalled = !string.IsNullOrEmpty(winTermSettings) && File.Exists(winTermSettings);
        var winTermEnabled = winTermInstalled && winTermSettings is not null && IsWindowsTerminalBidiEnabled(winTermSettings);
        list.Add(new DesktopAppInfo(
            "winterminal",
            "Windows Terminal (محیط ترمینال ویندوز)",
            "WindowsTerminal",
            winTermSettings ?? "",
            winTermInstalled,
            winTermEnabled,
            "Terminal"));

        return list;
    }

    public static bool EnableRtl(string appId, out string message)
    {
        switch (appId.ToLowerInvariant())
        {
            case "qoder":
                return EnableQoder(out message);

            case "antigravity":
                return EnableAntigravity(out message);

            case "winterminal":
                return EnableWindowsTerminal(out message);

            case "vscode":
                return EnableVsCode(out message);

            case "freebuff":
            case "lmstudio":
                return EnableHtmlApp(appId, out message);

            default:
                message = "برنامه ناشناخته است.";
                return false;
        }
    }

    public static bool DisableRtl(string appId, out string message)
    {
        switch (appId.ToLowerInvariant())
        {
            case "winterminal":
                return DisableWindowsTerminal(out message);

            case "vscode":
                return DisableVsCode(out message);

            case "freebuff":
            case "lmstudio":
                return DisableHtmlApp(appId, out message);

            case "qoder":
            case "antigravity":
                message = "برای قطع راست‌چین در این برنامه، کافیست برنامه را مجدداً راه‌اندازی کنید.";
                return true;

            default:
                message = "برنامه ناشناخته است.";
                return false;
        }
    }

    public static bool EnableAll(out List<string> results)
    {
        results = new List<string>();
        var allOk = true;

        foreach (var app in GetSupportedApps())
        {
            if (app.IsInstalled)
            {
                var ok = EnableRtl(app.Id, out var msg);
                results.Add($"{app.DisplayName}: {msg}");
                if (!ok) allOk = false;
            }
        }

        return allOk;
    }

    #region App Implementations

    static bool EnableQoder(out string message)
    {
        try
        {
            // 1. Configure shortcuts with --remote-debugging-port
            ConfigureQoderShortcuts();

            // 2. Check if running
            var procs = Process.GetProcessesByName("Qoder");
            if (procs.Length == 0)
            {
                // Launch Qoder with debug port
                var exe = @"G:\app installed\Qoder\Qoder.exe";
                if (!File.Exists(exe)) exe = @"C:\Users\Sajad\AppData\Local\Qoder\Qoder Launcher\Qoder Launcher.exe";
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe, $"--remote-debugging-port={QoderCdpPort}") { UseShellExecute = true });
                    Thread.Sleep(2500);
                }
            }

            // 3. Inject live via CDP
            var injected = InjectCdpAsync(QoderCdpPort, "Qoder").GetAwaiter().GetResult();
            if (injected)
            {
                message = "راست‌چین درجا روی Qoder با موفقیت فعال شد!";
                return true;
            }
            else
            {
                message = "میانبرهای Qoder تنظیم شدند. برای اتصال CDP کافیست یک‌بار Qoder را ببندید و دوباره باز کنید.";
                return true;
            }
        }
        catch (Exception ex)
        {
            message = $"خطا در فعال‌سازی Qoder: {ex.Message}";
            return false;
        }
    }

    public static Task<bool> InjectAntigravityLiveAsync() => InjectCdpAsync(AntigravityCdpPort, "Antigravity");

    static bool EnableAntigravity(out string message)
    {
        var ok = InjectAntigravityLiveAsync().GetAwaiter().GetResult();
        message = ok ? "راست‌چین درجا روی Antigravity اعمال شد!" : "خطا در اتصال به Antigravity (آیا برنامه باز است؟)";
        return ok;
    }

    static bool EnableWindowsTerminal(out string message)
    {
        var settingsPath = FindWindowsTerminalSettings();
        if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath))
        {
            message = "فایل تنظیمات Windows Terminal یافت نشد.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject root)
            {
                var profiles = root["profiles"] as JsonObject ?? new JsonObject();
                root["profiles"] = profiles;

                var defaults = profiles["defaults"] as JsonObject ?? new JsonObject();
                profiles["defaults"] = defaults;

                defaults.Remove("experimental.enableBidi");

                var font = defaults["font"] as JsonObject ?? new JsonObject();
                font["face"] = "Cascadia Code, Segoe UI";
                defaults["font"] = font;

                File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                message = "فونت ترمینال ویندوز به Cascadia Code, Segoe UI (با فال‌بک و اتصال درست حروف فارسی) تنظیم شد!";
                return true;
            }

            message = "فرمت تنظیمات نامعتبر است.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"خطا: {ex.Message}";
            return false;
        }
    }

    static bool DisableWindowsTerminal(out string message)
    {
        var settingsPath = FindWindowsTerminalSettings();
        if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath))
        {
            message = "فایل تنظیمات یافت نشد.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject root && root["profiles"]?["defaults"] is JsonObject defaults)
            {
                defaults.Remove("experimental.enableBidi");
                File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                message = "راست‌چین ترمینال ویندوز غیرفعال شد.";
                return true;
            }
            message = "انجام شد.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"خطا: {ex.Message}";
            return false;
        }
    }

    static bool EnableVsCode(out string message)
    {
        var cssPath = FindVsCodeWorkbenchCss();
        if (string.IsNullOrEmpty(cssPath) || !File.Exists(cssPath))
        {
            message = "فایل استایل VS Code یافت نشد.";
            return false;
        }

        try
        {
            var content = File.ReadAllText(cssPath);
            if (content.Contains(MarkerStart))
            {
                message = "راست‌چین از قبل در VS Code فعال است.";
                return true;
            }

            var payload = $"\n{MarkerStart}\n{UniversalCss}\n{MarkerEnd}\n";

            try
            {
                File.AppendAllText(cssPath, payload);
            }
            catch (UnauthorizedAccessException)
            {
                // Elevate via PowerShell if in Program Files
                var tmp = Path.GetTempFileName();
                File.WriteAllText(tmp, payload);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[System.IO.File]::AppendAllText('{cssPath}', [System.IO.File]::ReadAllText('{tmp}'))\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = Process.Start(psi);
                p?.WaitForExit(5000);
                try { File.Delete(tmp); } catch { }
            }

            message = "راست‌چین درجا روی محیط VS Code و چت‌های هوش مصنوعی فعال شد!";
            return true;
        }
        catch (Exception ex)
        {
            message = $"خطا در فعال‌سازی VS Code: {ex.Message}";
            return false;
        }
    }

    static bool DisableVsCode(out string message)
    {
        var cssPath = FindVsCodeWorkbenchCss();
        if (string.IsNullOrEmpty(cssPath) || !File.Exists(cssPath))
        {
            message = "فایل یافت نشد.";
            return false;
        }

        try
        {
            var content = File.ReadAllText(cssPath);
            var start = content.IndexOf(MarkerStart, StringComparison.Ordinal);
            var end = content.IndexOf(MarkerEnd, StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                var updated = content.Remove(start, (end + MarkerEnd.Length) - start);
                try
                {
                    File.WriteAllText(cssPath, updated);
                }
                catch (UnauthorizedAccessException)
                {
                    var tmp = Path.GetTempFileName();
                    File.WriteAllText(tmp, updated);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[System.IO.File]::WriteAllText('{cssPath}', [System.IO.File]::ReadAllText('{tmp}'))\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    try { File.Delete(tmp); } catch { }
                }
            }

            message = "راست‌چین VS Code غیرفعال شد.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"خطا: {ex.Message}";
            return false;
        }
    }

    static bool EnableHtmlApp(string appId, out string message)
    {
        var app = GetSupportedApps().FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
        if (app is null || !app.IsInstalled)
        {
            message = "برنامه یافت نشد.";
            return false;
        }

        try
        {
            var content = File.ReadAllText(app.TargetFilePath);
            if (content.Contains(HtmlMarkerStart))
            {
                message = "راست‌چین از قبل فعال است.";
                return true;
            }

            var htmlPayload = $"\n{HtmlMarkerStart}\n<style id=\"rtl-fix-injected-styles\">\n{UniversalCss}\n</style>\n<script id=\"rtl-fix-injected-script\">\n{LiveJsPayload}\n</script>\n{HtmlMarkerEnd}\n";

            var backup = app.TargetFilePath + ".rtlfix-backup";
            if (!File.Exists(backup)) File.Copy(app.TargetFilePath, backup, true);

            var headIdx = content.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            var updated = headIdx >= 0 ? content.Insert(headIdx, htmlPayload) : content + "\n" + htmlPayload;
            File.WriteAllText(app.TargetFilePath, updated);

            RestartIfRunning(app.ProcessName);

            message = $"راست‌چین درجا برای {app.DisplayName} با موفقیت فعال شد!";
            return true;
        }
        catch (Exception ex)
        {
            message = $"خطا: {ex.Message}";
            return false;
        }
    }

    static bool DisableHtmlApp(string appId, out string message)
    {
        var app = GetSupportedApps().FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
        if (app is null || !app.IsInstalled)
        {
            message = "برنامه یافت نشد.";
            return false;
        }

        try
        {
            var backup = app.TargetFilePath + ".rtlfix-backup";
            if (File.Exists(backup))
            {
                File.Copy(backup, app.TargetFilePath, true);
                File.Delete(backup);
            }
            else
            {
                var content = File.ReadAllText(app.TargetFilePath);
                var start = content.IndexOf(HtmlMarkerStart, StringComparison.Ordinal);
                var end = content.IndexOf(HtmlMarkerEnd, StringComparison.Ordinal);
                if (start >= 0 && end > start)
                {
                    var updated = content.Remove(start, (end + HtmlMarkerEnd.Length) - start);
                    File.WriteAllText(app.TargetFilePath, updated);
                }
            }

            RestartIfRunning(app.ProcessName);

            message = $"راست‌چین برای {app.DisplayName} غیرفعال شد.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"خطا: {ex.Message}";
            return false;
        }
    }

    #endregion

    #region CDP WebSocket Injection & Background Watcher

    static readonly HashSet<string> injectedPages = new();

    public static async Task<bool> InjectCdpAsync(int port, string appName, bool silent = false)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var json = await client.GetStringAsync($"http://127.0.0.1:{port}/json");
            using var doc = JsonDocument.Parse(json);

            var anySuccess = false;
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (elem.TryGetProperty("webSocketDebuggerUrl", out var wsUrlProp))
                {
                    var wsUrl = wsUrlProp.GetString();
                    if (!string.IsNullOrEmpty(wsUrl))
                    {
                        var pageId = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? wsUrl : wsUrl;
                        var isNew = false;
                        lock (injectedPages)
                        {
                            isNew = injectedPages.Add(pageId);
                        }

                        try
                        {
                            using var ws = new ClientWebSocket();
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                            var payload = JsonSerializer.Serialize(new
                            {
                                id = 1,
                                method = "Runtime.evaluate",
                                @params = new { expression = LiveJsPayload }
                            });
                            var bytes = Encoding.UTF8.GetBytes(payload);
                            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
                            var buf = new byte[4096];
                            await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
                            anySuccess = true;

                            if (isNew && !silent)
                            {
                                Log.Write($"integrator: successfully injected live in-place RTL into {appName} (port {port})");
                            }
                        }
                        catch { }
                    }
                }
            }

            return anySuccess;
        }
        catch
        {
            return false;
        }
    }

    public static readonly int[] CandidateCdpPorts = Enumerable.Range(54070, 55).Concat(new[] { 17744, 9222, 9229 }).ToArray();

    /// <summary>
    /// Finds a live CDP port for a running app no matter how it was started: the debug port lives
    /// on the process command line, not on the shortcut that launched it.
    /// </summary>
    public static Task<int> DiscoverCdpPortAsync(string processName) =>
        DiscoverPortsAsync(async ports =>
        {
            foreach (var port in ports)
            {
                if (await CdpEndpointAliveAsync(port)) return port;
            }
            return 0;
        }, processName);

    /// <summary>Every CDP port currently open on the machine, read off the process command lines.</summary>
    public static Task<List<int>> DiscoverAllCdpPortsAsync() =>
        DiscoverPortsAsync(async ports =>
        {
            var alive = new List<int>();
            foreach (var port in ports)
            {
                if (await CdpEndpointAliveAsync(port)) alive.Add(port);
            }
            return alive;
        }, null);

    static async Task<T> DiscoverPortsAsync<T>(Func<List<int>, Task<T>> pick, string? processName)
    {
        var ports = await Task.Run(() =>
        {
            try
            {
                var filterArg = processName is null ? "" : $" -Filter \"Name='{processName}.exe'\"";
                var script = $$"""
                    $out = @()
                    Get-CimInstance Win32_Process{{filterArg}} -ErrorAction SilentlyContinue | ForEach-Object {
                        if ($_.CommandLine -match '--remote-debugging-port=(\d+)') { $out += [int]$Matches[1] }
                    }
                    $out | Select-Object -Unique | ConvertTo-Json -Compress
                    """;

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return new List<int>();
                var json = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (string.IsNullOrWhiteSpace(json)) return new List<int>();

                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().Select(e => e.GetInt32()).ToList()
                    : [doc.RootElement.GetInt32()];
            }
            catch
            {
                return new List<int>();
            }
        });

        return await pick(ports);
    }

    static readonly HttpClient aliveClient = new() { Timeout = TimeSpan.FromMilliseconds(400) };

    static async Task<bool> CdpEndpointAliveAsync(int port)
    {
        try
        {
            var json = await aliveClient.GetStringAsync($"http://127.0.0.1:{port}/json");
            return !string.IsNullOrEmpty(json) && json.Contains("webSocketDebuggerUrl");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes a running app and starts it again with an open CDP port, waiting until the debug
    /// endpoint answers, so injection no longer depends on which shortcut the user clicked.
    /// </summary>
    public static async Task<bool> RestartAppWithDebugPortAsync(string processName, string targetPath, int port)
    {
        try
        {
            var exePath = targetPath;
            var procs = Process.GetProcessesByName(processName);
            foreach (var p in procs)
            {
                try
                {
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                        exePath = p.MainModule?.FileName ?? exePath;
                    p.Kill();
                }
                catch { }
            }
            await Task.Delay(1200);

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
            Process.Start(new ProcessStartInfo(exePath, $"--remote-debugging-port={port}") { UseShellExecute = true });

            for (var i = 0; i < 24; i++)
            {
                await Task.Delay(500);
                if (await CdpEndpointAliveAsync(port)) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> InjectAllActiveCdpAppsAsync(bool silent = true)
    {
        var tasks = CandidateCdpPorts.Select(async port =>
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
                var json = await client.GetStringAsync($"http://127.0.0.1:{port}/json");
                if (!string.IsNullOrEmpty(json) && json.Contains("webSocketDebuggerUrl"))
                {
                    return await InjectCdpAsync(port, $"Port:{port}", silent);
                }
            }
            catch { }
            return false;
        });

        var results = await Task.WhenAll(tasks);
        return results.Any(x => x);
    }

    public static Task<bool> InjectActiveForegroundAsync() => InjectAllActiveCdpAppsAsync(silent: false);

    public static void StartLiveWatcher(CancellationToken ct)
    {
        Task.Run(async () =>
        {
            var tick = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await InjectAllActiveCdpAppsAsync(silent: true);

                    // The fixed range only knows ports our own shortcuts assigned; sweeping the
                    // process table now and then also catches an app started with a debug flag
                    // from anywhere else — a terminal, another launcher, its own config.
                    if (++tick % 5 == 0)
                    {
                        foreach (var port in await DiscoverAllCdpPortsAsync())
                            await InjectCdpAsync(port, $"Port:{port}", silent: false);
                    }
                }
                catch { }

                await Task.Delay(3000, ct);
            }
        }, ct);
    }

    #endregion

    #region Helper Methods

    public static void ConfigureAllElectronShortcuts()
    {
        try
        {
            var script = """
                $commonStart = "C:\ProgramData\Microsoft\Windows\Start Menu\Programs"
                $userStart = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
                $userDesktop = "$env:USERPROFILE\Desktop"
                $publicDesktop = "C:\Users\Public\Desktop"
                $sh = New-Object -ComObject WScript.Shell

                $knownApps = @{
                    "qoder" = 54075
                    "zcode" = 54076
                    "antigravity" = 17744
                    "freebuff" = 54077
                    "minimax" = 54078
                    "verdent" = 54079
                    "opencode" = 54080
                    "workbuddy" = 54081
                    "chatbox" = 54082
                    "cursor" = 54083
                    "windsurf" = 54084
                }
                $nextPort = 54085

                function Configure-Shortcut($lnkPath, $destPath) {
                    try {
                        $s = $sh.CreateShortcut($lnkPath)
                        $target = $s.TargetPath
                        if ($target -and (Test-Path $target)) {
                            $dir = Split-Path -Path $target -Parent
                            $isElectron = (Test-Path (Join-Path $dir "resources\app.asar")) -or (Test-Path (Join-Path $dir "..\resources\app.asar"))
                            if ($isElectron) {
                                $baseName = [System.IO.Path]::GetFileNameWithoutExtension($target).ToLower()
                                $assignedPort = 0
                                foreach ($k in $knownApps.Keys) {
                                    if ($baseName -match $k) {
                                        $assignedPort = $knownApps[$k]
                                        break
                                    }
                                }
                                if ($assignedPort -eq 0) {
                                    $assignedPort = $nextPort
                                    $script:nextPort++
                                }
                                
                                if ($destPath -ne $lnkPath) {
                                    $parent = Split-Path -Path $destPath -Parent
                                    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                                    Copy-Item -Path $lnkPath -Destination $destPath -Force
                                    $s = $sh.CreateShortcut($destPath)
                                }
                                
                                if ($s.Arguments -notmatch "remote-debugging-port=") {
                                    $s.Arguments = ($s.Arguments + " --remote-debugging-port=$assignedPort").Trim()
                                    $s.Save()
                                }
                            }
                        }
                    } catch {}
                }

                # 1. Process User Start Menu
                if (Test-Path $userStart) {
                    Get-ChildItem -Path $userStart -Recurse -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
                        Configure-Shortcut $_.FullName $_.FullName
                    }
                }

                # 2. Process All Users Start Menu -> copy to User Start Menu
                if (Test-Path $commonStart) {
                    Get-ChildItem -Path $commonStart -Recurse -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
                        $rel = $_.FullName.Substring($commonStart.Length)
                        $dest = Join-Path $userStart $rel
                        Configure-Shortcut $_.FullName $dest
                    }
                }

                # 3. Process User Desktop
                if (Test-Path $userDesktop) {
                    Get-ChildItem -Path $userDesktop -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
                        Configure-Shortcut $_.FullName $_.FullName
                    }
                }

                # 4. Process Public Desktop -> copy to User Desktop
                if (Test-Path $publicDesktop) {
                    Get-ChildItem -Path $publicDesktop -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
                        $dest = Join-Path $userDesktop $_.Name
                        Configure-Shortcut $_.FullName $dest
                    }
                }
                """;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            p?.WaitForExit(4000);
        }
        catch { }
    }

    public static void ConfigureQoderShortcuts() => ConfigureAllElectronShortcuts();

    public static bool AreQoderShortcutsConfigured()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var lnk = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs", "Qoder.lnk");
            if (!File.Exists(lnk)) return false;

            var bytes = File.ReadAllBytes(lnk);
            var content = Encoding.Unicode.GetString(bytes) + Encoding.ASCII.GetString(bytes);
            return content.Contains("54075");
        }
        catch
        {
            return false;
        }
    }

    public static void RestartQoderWithDebugPort()
    {
        try
        {
            var procs = Process.GetProcessesByName("Qoder");
            if (procs.Length == 0)
            {
                var launcher = @"C:\Users\Sajad\AppData\Local\Qoder\Qoder Launcher\Qoder Launcher.exe";
                if (!File.Exists(launcher)) launcher = @"G:\app installed\Qoder\Qoder.exe";
                if (File.Exists(launcher))
                {
                    Process.Start(new ProcessStartInfo(launcher, $"--remote-debugging-port={QoderCdpPort}") { UseShellExecute = true });
                }
            }
        }
        catch { }
    }

    static string? FindVsCodeWorkbenchCss()
    {
        var roots = new[]
        {
            @"C:\Program Files\Microsoft VS Code",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code")
        };

        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                try
                {
                    var matches = Directory.GetFiles(root, "workbench.desktop.main.css", SearchOption.AllDirectories);
                    if (matches.Length > 0) return matches[0];
                }
                catch { }
            }
        }

        return null;
    }

    public static string? FindWindowsTerminalSettings()
    {
        try
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pkgDir = Path.Combine(localApp, "Packages");
            if (!Directory.Exists(pkgDir)) return null;

            var termDirs = Directory.GetDirectories(pkgDir, "*Microsoft.WindowsTerminal_*");
            if (termDirs.Length > 0)
            {
                var settings = Path.Combine(termDirs[0], "LocalState", "settings.json");
                if (File.Exists(settings)) return settings;
            }
        }
        catch { }

        return null;
    }

    public static bool IsWindowsTerminalBidiEnabled(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("\"experimental.enableBidi\": true") || text.Contains("\"experimental.enableBidi\":true");
        }
        catch
        {
            return false;
        }
    }

    static void RestartIfRunning(string processName)
    {
        try
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0) return;

            var exePath = procs[0].MainModule?.FileName;
            foreach (var p in procs)
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                Thread.Sleep(500);
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
        }
        catch { }
    }


    public static List<(string ProcessName, string Title, string FilePath)> GetRunningWindowApps()
    {
        var list = new List<(string, string, string)>();
        var currentPid = Process.GetCurrentProcess().Id;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == currentPid || proc.MainWindowHandle == IntPtr.Zero) continue;
                var title = proc.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var path = "";
                try { path = proc.MainModule?.FileName ?? ""; } catch { }
                list.Add((proc.ProcessName, title, path));
            }
            catch { }
        }
        return list.OrderBy(x => x.Item2).ToList();
    }

    public static List<ManagedAppInfo> GetManagedApps(RtlSettings settings)
    {
        var result = new Dictionary<string, ManagedAppInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan shortcuts from Desktop and Start Menu
        try
        {
            var script = """
                $sh = New-Object -ComObject WScript.Shell
                $list = @()
                Get-ChildItem -Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs", "$env:USERPROFILE\Desktop" -Recurse -Filter "*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
                    try {
                        $s = $sh.CreateShortcut($_.FullName)
                        if ($s.Arguments -match "remote-debugging-port=(\d+)") {
                            $list += [PSCustomObject]@{
                                Name = $_.BaseName
                                TargetPath = $s.TargetPath
                                Port = [int]$matches[1]
                                ProcessName = [System.IO.Path]::GetFileNameWithoutExtension($s.TargetPath)
                            }
                        }
                    } catch {}
                }
                $list | ConvertTo-Json -Compress
                """;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var json = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                        ? doc.RootElement.EnumerateArray().ToList()
                        : new List<JsonElement> { doc.RootElement };
                    foreach (var el in elements)
                    {
                        var name = el.GetProperty("Name").GetString() ?? "";
                        var target = el.GetProperty("TargetPath").GetString() ?? "";
                        var proc = el.GetProperty("ProcessName").GetString() ?? "";
                        var port = el.GetProperty("Port").GetInt32();
                        if (!string.IsNullOrEmpty(proc) && !result.ContainsKey(proc))
                        {
                            result[proc] = new ManagedAppInfo(name, proc, target, "وب / الکترون (زنده)", port, false, false);
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Custom Apps from settings
        foreach (var c in settings.CustomApps)
        {
            if (!result.ContainsKey(c.ProcessName))
            {
                result[c.ProcessName] = new ManagedAppInfo(
                    c.Name,
                    c.ProcessName,
                    c.TargetPath,
                    c.IsElectron ? "وب / الکترون (شخصی)" : "نیتیو / کنسول (ویژوال)",
                    c.Port,
                    false,
                    false,
                    IsCustom: true);
            }
        }

        // 3. Known integrated apps (VS Code, Windows Terminal)
        if (!result.ContainsKey("Code"))
        {
            var vsCodeCss = FindVsCodeWorkbenchCss();
            result["Code"] = new ManagedAppInfo("Visual Studio Code", "Code", vsCodeCss ?? "", "کدنویسی (CSS مستقیم)", 0, false, false);
        }

        if (!result.ContainsKey("WindowsTerminal"))
        {
            var wt = FindWindowsTerminalSettings();
            result["WindowsTerminal"] = new ManagedAppInfo("Windows Terminal", "WindowsTerminal", wt ?? "", "ترمینال ویندوز (Bidi نیتیو)", 0, false, false);
        }

        // 4. Bidi-blind terminals & editors
        foreach (var b in settings.BidiBlindApps)
        {
            if (!result.ContainsKey(b))
            {
                result[b] = new ManagedAppInfo(b, b, "", "نیتیو / کنسول (ویژوال)", 0, false, false);
            }
        }

        // 5. Update live running and connected status
        var list = result.Values.ToList();
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(150) };

        for (int i = 0; i < list.Count; i++)
        {
            var app = list[i];
            var isRunning = false;
            try { isRunning = Process.GetProcessesByName(app.ProcessName).Length > 0; } catch { }

            var isConnected = false;
            if (app.Port > 0 && isRunning)
            {
                try
                {
                    var res = http.GetStringAsync($"http://127.0.0.1:{app.Port}/json").GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(res) && res.Contains("webSocketDebuggerUrl"))
                        isConnected = true;
                }
                catch { }
            }

            list[i] = app with { IsRunning = isRunning, IsConnected = isConnected };
        }

        return list.OrderByDescending(x => x.IsConnected)
                   .ThenByDescending(x => x.IsRunning)
                   .ThenBy(x => x.Name)
                   .ToList();
    }

    public static bool RegisterApp(string pathOrProcess, RtlSettings settings, SettingsStore store, out string message)
    {
        if (string.IsNullOrWhiteSpace(pathOrProcess))
        {
            message = "مسیر یا نام برنامه نمی‌تواند خالی باشد.";
            return false;
        }

        pathOrProcess = pathOrProcess.Trim().Trim('\"');
        string targetPath = "";
        string procName = "";
        string displayName = "";

        if (File.Exists(pathOrProcess))
        {
            targetPath = pathOrProcess;
            displayName = Path.GetFileNameWithoutExtension(targetPath);
            procName = displayName;
        }
        else
        {
            procName = pathOrProcess.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            displayName = procName;
            var running = Process.GetProcessesByName(procName);
            if (running.Length > 0)
            {
                try { targetPath = running[0].MainModule?.FileName ?? ""; } catch { }
            }
        }

        bool isElectron = false;
        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                isElectron = File.Exists(Path.Combine(dir, "resources", "app.asar")) ||
                             File.Exists(Path.Combine(dir, "..", "resources", "app.asar"));
            }
        }

        if (isElectron)
        {
            var usedPorts = CandidateCdpPorts.ToHashSet();
            int port = 54085;
            while (usedPorts.Contains(port)) port++;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var lnkPath = Path.Combine(desktop, $"{displayName}.lnk");

            var script = $"""
                $sh = New-Object -ComObject WScript.Shell
                $s = $sh.CreateShortcut('{lnkPath}')
                $s.TargetPath = '{targetPath}'
                $s.Arguments = '--remote-debugging-port={port}'
                $s.Save()
                """;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);

            settings.CustomApps.RemoveAll(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
            settings.CustomApps.Add(new CustomManagedApp
            {
                Name = displayName,
                ProcessName = procName,
                TargetPath = targetPath,
                Port = port,
                IsElectron = true
            });
            store.Save(settings);

            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await InjectCdpAsync(port, displayName, silent: true);
            });

            message = $"برنامه {displayName} به عنوان نرم‌افزار تحت وب/الکترون با پورت {port} ثبت و شورت‌کات دسکتاپ آن تنظیم شد.";
            return true;
        }
        else
        {
            var lower = procName.ToLowerInvariant();
            if (!settings.BidiBlindApps.Contains(lower))
            {
                settings.BidiBlindApps.Add(lower);
            }
            settings.CustomApps.RemoveAll(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
            settings.CustomApps.Add(new CustomManagedApp
            {
                Name = displayName,
                ProcessName = procName,
                TargetPath = targetPath,
                Port = 0,
                IsElectron = false
            });
            store.Save(settings);

            message = $"برنامه {displayName} به عنوان برنامه نیتیو/کنسول ثبت شد و راست‌نویسی دیداری مستقیم روی آن فعال گردید.";
            return true;
        }
    }

    public static bool UnregisterApp(string processName, RtlSettings settings, SettingsStore store, out string message)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            message = "نام پروسه نامعتبر است.";
            return false;
        }

        settings.CustomApps.RemoveAll(x => x.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        settings.BidiBlindApps.RemoveAll(x => x.Equals(processName, StringComparison.OrdinalIgnoreCase));
        store.Save(settings);

        message = $"برنامه {processName} از لیست حذف شد.";
        return true;
    }

    #endregion
}
