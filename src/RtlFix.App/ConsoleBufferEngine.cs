using System.Text;
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
    static readonly Dictionary<(uint Pid, int Row), string> rawSeen = new();
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
                await Task.Delay(120, token).ConfigureAwait(false);
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

        // Two console kinds need opposite treatment:
        //  • ConPTY (class PseudoConsoleWindow — Windows Terminal): with experimental.enableBidi it
        //    orders and shapes Persian itself, so rewriting cells would double-reverse it. The
        //    engine only right-aligns stable lines (padding + attribute shift); lines still being
        //    streamed stay put and remain readable through the terminal's own bidi.
        //  • Classic conhost (ConsoleWindowClass): bidi-blind, needs the full visual transform.
        var hwnd = Interop.GetConsoleWindow();
        var className = new StringBuilder(64);
        var isConPty = hwnd != IntPtr.Zero &&
            Interop.GetClassName(hwnd, className, 64) > 0 &&
            className.ToString() == "PseudoConsoleWindow";


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
                var attrs = new ushort[width];

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
                        Interop.ReadConsoleOutputAttribute(hOut, attrs, (uint)width, coord, out _);
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
                            var blankAttrs = new ushort[width];
                            Array.Fill(blankAttrs, attrs[width - 1]);
                            if (Interop.WriteConsoleOutputCharacter(hOut, blank, (uint)width, coord, out _) &&
                                Interop.WriteConsoleOutputAttribute(hOut, blankAttrs, (uint)width, coord, out _))
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

                        // Orphan color: an all-space row whose cells still carry a few colored
                        // attribute runs left behind when the words they painted moved on. The TUI
                        // has no element this small, so reset those cells to the row's default
                        // attribute instead of leaving floating color blocks on the margin.
                        if (nonspace == 0)
                        {
                            var stray = 0;
                            for (var k = 0; k < width && stray <= 6; k++)
                                if (attrs[k] != attrs[width - 1]) stray++;
                            if (stray > 0 && stray <= 6)
                            {
                                var blankAttrs = new ushort[width];
                                Array.Fill(blankAttrs, attrs[width - 1]);
                                Interop.WriteConsoleOutputAttribute(hOut, blankAttrs, (uint)width, coord, out _);
                                rowCache[key] = line;
                                continue;
                            }
                        }

                        if (isConPty)
                        {
                            // Right-align raw text for the terminal's own bidi renderer. A line
                            // must be seen unchanged on two consecutive ticks before it is moved:
                            // lines Ink is still streaming keep changing and stay untouched (they
                            // are readable through bidi anyway), so there is no repaint race —
                            // each finished line settles right once and stays.
                            var rawSeenKey = key;
                            rawSeen.TryGetValue(rawSeenKey, out var prevRaw);
                            if (prevRaw != line)
                            {
                                rawSeen[rawSeenKey] = line;
                                continue;
                            }

                            var content = line.TrimEnd();
                            if (!HasRawPersianLetters(content) || content.Length >= width)
                            {
                                rowCache[key] = line;
                                continue;
                            }

                            var offset = width - content.Length;
                            var shifted = content.PadLeft(width);
                            var shiftAttrs = new ushort[width];
                            Array.Fill(shiftAttrs, attrs[width - 1]);
                            for (var k = 0; k < content.Length; k++)
                                shiftAttrs[offset + k] = attrs[k];

                            if (Interop.WriteConsoleOutputCharacter(hOut, shifted, (uint)width, coord, out _) &&
                                Interop.WriteConsoleOutputAttribute(hOut, shiftAttrs, (uint)width, coord, out _))
                            {
                                fixedRows++;
                                rowCache[key] = shifted;
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

                        // Colors live in per-cell attributes and do not move by themselves: the
                        // mapped transform reports which source cell each output char came from,
                        // so the TUI's per-word coloring travels with the reordered text instead
                        // of staying behind under the wrong letters.
                        var mapped = RtlTransform.RtlizeMapped(candidate, visualOptions);
                        var transformed = mapped.Text;
                        if (transformed.Length > width) transformed = transformed[..width];

                        var outAttrs = new ushort[width];
                        for (var k = 0; k < transformed.Length; k++)
                        {
                            var src = mapped.SourceCharIndex[k];
                            outAttrs[k] = src < width ? attrs[src] : attrs[width - 1];
                        }
                        for (var k = transformed.Length; k < width; k++) outAttrs[k] = attrs[width - 1];
                        if (transformed.Length < width) transformed = transformed.PadRight(width);

                        if (Interop.WriteConsoleOutputCharacter(hOut, transformed, (uint)width, coord, out _) &&
                            Interop.WriteConsoleOutputAttribute(hOut, outAttrs, (uint)width, coord, out _))
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
