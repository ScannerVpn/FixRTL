using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RtlFix.Core.Transforms;

namespace RtlFix.App;

/// <summary>
/// Direct console screen buffer injector.
/// Attaches to native console processes (cmd, powershell, Windows Terminal client)
/// and rewrites raw Persian rows straight inside the active console buffer with 1:1 cell
/// alignment. Terminals are bidi-blind, so rows get the full visual treatment: shaped
/// presentation forms reordered the way they must be drawn (plus mirrored brackets).
/// </summary>
static class ConsoleBufferEngine
{
    static readonly RtlOptions visualOptions = new()
    {
        TargetIsBidiBlind = true,
        Shape = true,
        Mirror = true,
    };

    static readonly Dictionary<(uint Pid, int Row), string> rowCache = new();
    static CancellationTokenSource? cts;
    static Task? workerTask;

    public static bool IsRunning => cts != null && !cts.IsCancellationRequested;

    public static void Start()
    {
        if (IsRunning) return;
        cts = new CancellationTokenSource();
        workerTask = Task.Run(() => WorkerLoop(cts.Token));
        Log.Write("ConsoleBufferEngine started.");
    }

    public static void Stop()
    {
        try
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        catch { }
        finally
        {
            cts = null;
            workerTask = null;
            Log.Write("ConsoleBufferEngine stopped.");
        }
    }

    static async Task WorkerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Write($"ConsoleBufferEngine tick error: {ex.Message}");
            }

            try
            {
                await Task.Delay(350, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static void Tick()
    {
        Span<string> shells = ["cmd", "powershell", "pwsh"];
        foreach (var shell in shells)
        {
            try
            {
                var procs = Process.GetProcessesByName(shell);
                foreach (var p in procs)
                {
                    using (p)
                    {
                        if (!p.HasExited)
                        {
                            FixConsoleBuffer((uint)p.Id);
                        }
                    }
                }
            }
            catch { }
        }
    }

    public static int FixConsoleBuffer(uint targetPid)
    {
        Interop.FreeConsole();
        if (!Interop.AttachConsole(targetPid))
        {
            return 0;
        }

        int fixedRows = 0;
        try
        {
            var access = 0xC0000000u;
            var hOut = Interop.CreateFile("CONOUT$", access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (hOut == IntPtr.Zero || hOut == new IntPtr(-1))
            {
                return 0;
            }

            try
            {
                if (!Interop.GetConsoleScreenBufferInfo(hOut, out var info))
                {
                    return 0;
                }

                var width = info.dwSize.X;
                if (width <= 0 || width > 1000)
                {
                    return 0;
                }

                var cursorY = (int)info.dwCursorPosition.Y;
                var top = Math.Max(0, (int)info.srWindow.Top);
                var bottom = Math.Min((int)info.dwSize.Y - 1, (int)info.srWindow.Bottom);
                var buffer = new char[width];

                for (var y = top; y <= bottom; y++)
                {
                    // Never touch rows near the cursor where typing or streaming is in flight
                    if (y >= cursorY - 1 && y <= cursorY + 1)
                    {
                        continue;
                    }

                    var key = (targetPid, y);
                    var coord = new Interop.COORD { X = 0, Y = (short)y };
                    if (Interop.ReadConsoleOutputCharacter(hOut, buffer, (uint)width, coord, out var read) && read > 0)
                    {
                        var line = new string(buffer, 0, (int)read).PadRight(width);
                        if (rowCache.TryGetValue(key, out var cached) && cached == line)
                        {
                            continue;
                        }

                        // Skip UI borders and prompts. A leading slash only marks a path line when
                        // the row has no Arabic in it — wrapped prose rows often start with one.
                        if (line.Contains('─') || line.Contains('❯') ||
                            (line.TrimStart().StartsWith('/') && !HasRawPersianLetters(line)))
                        {
                            rowCache[key] = line;
                            continue;
                        }

                        // Fresh terminal content is raw logical text. Presentation forms only exist
                        // in cells a previous fix wrote, so how they sit against raw letters tells
                        // the row's history: forms after all the raw letters means the app rewrote
                        // the row's head — transform that head and let the padded write wipe the
                        // stale tail. Forms woven between raw letters are leftovers of an older
                        // buggy pass that reordered without shaping: that row is visual-order text,
                        // so normalize it back to letters, flip it to logical, and fix it normally.
                        // Rows of forms alone are ours (already correct) or legacy the app redraws;
                        // transforming either would reverse correct text.
                        var forms = IndexOfPresentationForm(line);

                        // A reflow can leave rows holding nothing but two or three orphan form
                        // cells (sometimes with a stray letter of a word that moved elsewhere).
                        // They read as scattered noise, carry no recoverable content, and the TUI
                        // never writes forms itself — blank them, but keep genuinely short lines
                        // like "OK" or "y" intact.
                        var nonspace = line.Count(c => !char.IsWhiteSpace(c));
                        var asciiRunes = line.Count(char.IsAsciiLetterOrDigit);
                        if (forms >= 0 && nonspace <= 5 && !HasRawPersianLetters(line) && asciiRunes <= 1)
                        {
                            var blank = new string(' ', width);
                            if (Interop.WriteConsoleOutputCharacter(hOut, blank, (uint)width, coord, out _))
                            {
                                fixedRows++;
                                rowCache[key] = blank;
                            }
                            else
                            {
                                rowCache[key] = line;
                            }
                            continue;
                        }

                        string candidate;
                        if (forms < 0)
                        {
                            candidate = line;
                        }
                        else if (!HasRawPersianLetters(line[(forms + 1)..]))
                        {
                            candidate = line[..forms];
                        }
                        else
                        {
                            var normalized = line.Normalize(NormalizationForm.FormKC);
                            candidate = RtlTransform.LogicalFromVisual(normalized);
                        }

                        if (!HasRawPersianLetters(candidate))
                        {
                            rowCache[key] = line;
                            continue;
                        }

                        var transformed = RtlTransform.Rtlize(candidate, visualOptions);

                        if (transformed.Length < width)
                        {
                            transformed = transformed.PadRight(width);
                        }
                        else if (transformed.Length > width)
                        {
                            transformed = transformed[..width];
                        }

                        if (Interop.WriteConsoleOutputCharacter(hOut, transformed, (uint)width, coord, out _))
                        {
                            fixedRows++;
                            rowCache[key] = transformed;
                        }
                        else
                        {
                            rowCache[key] = line;
                        }
                    }
                }

                if (fixedRows > 0)
                {
                    Log.Write($"Fixed {fixedRows} console buffer rows in PID {targetPid}");
                }
            }
            finally
            {
                Interop.CloseHandle(hOut);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"FixConsoleBuffer exception: {ex.Message}");
        }
        finally
        {
            Interop.FreeConsole();
        }

        return fixedRows;
    }

    /// <summary>Raw (unshaped) Arabic/Persian letters, i.e. fresh logical text to fix.</summary>
    public static bool HasRawPersianLetters(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c >= 0x0621 && c <= 0x064A) return true;
            if (c >= 0x0671 && c <= 0x06D3) return true;
        }
        return false;
    }

    /// <summary>Index of the first presentation-form glyph (cells a previous fix wrote), or -1.</summary>
    public static int IndexOfPresentationForm(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c is >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF') return i;
        }
        return -1;
    }
}
