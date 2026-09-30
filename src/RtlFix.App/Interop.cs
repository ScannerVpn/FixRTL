using System.Runtime.InteropServices;

namespace RtlFix.App;

/// <summary>Win32 entry points the tray app needs for hotkeys and synthesized keystrokes.</summary>
static class Interop
{
    public const int WM_HOTKEY = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    const uint InputKeyboard = 1;
    const uint KeyEventKeyUp = 0x0002;
    const ushort VkControl = 0x11;
    const ushort VkAlt = 0x12;
    const ushort VkShift = 0x10;
    const ushort VkInsert = 0x2D;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public static void ForceSetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        var fg = GetForegroundWindow();
        if (fg == hWnd) return;

        var fgThread = GetWindowThreadProcessId(fg, out _);
        var curThread = GetCurrentThreadId();

        if (fgThread != 0 && fgThread != curThread)
        {
            AttachThreadInput(curThread, fgThread, true);
            SetForegroundWindow(hWnd);
            AttachThreadInput(curThread, fgThread, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ReadConsoleOutputCharacter(
        IntPtr hConsoleOutput,
        [Out] char[] lpCharacter,
        uint nLength,
        COORD dwReadCoord,
        out uint lpNumberOfCharsRead);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WriteConsoleOutputCharacter(
        IntPtr hConsoleOutput,
        string lpCharacter,
        uint nLength,
        COORD dwWriteCoord,
        out uint lpNumberOfCharsWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadConsoleOutputAttribute(
        IntPtr hConsoleOutput,
        [Out] ushort[] lpAttribute,
        uint nLength,
        COORD dwReadCoord,
        out uint lpNumberOfAttrsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteConsoleOutputAttribute(
        IntPtr hConsoleOutput,
        ushort[] lpAttribute,
        uint nLength,
        COORD dwWriteCoord,
        out uint lpNumberOfAttrsWritten);

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputs, INPUT[] data, int size);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// Win32 checks cbSize against the full INPUT, whose union is sized by MOUSEINPUT; a smaller
    /// struct silently injects nothing.
    static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public static void ReleaseModifiers(bool releaseCtrl = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 350 &&
               ((GetAsyncKeyState(VkShift) & 0x8000) != 0 ||
                (GetAsyncKeyState(VkAlt) & 0x8000) != 0 ||
                (releaseCtrl && (GetAsyncKeyState(VkControl) & 0x8000) != 0)))
        {
            Thread.Sleep(15);
        }

        INPUT[] release = releaseCtrl
            ? [Key(VkAlt, KeyEventKeyUp), Key(VkShift, KeyEventKeyUp), Key(VkControl, KeyEventKeyUp)]
            : [Key(VkAlt, KeyEventKeyUp), Key(VkShift, KeyEventKeyUp)];

        SendInput((uint)release.Length, release, InputSize);
        Thread.Sleep(15);
    }

    /// <summary>Sends Ctrl plus the given letter to whatever window has the focus.</summary>
    public static void PressControl(char key)
    {
        var virtualKey = (ushort)char.ToUpperInvariant(key);
        ReleaseModifiers(releaseCtrl: false);

        INPUT[] down = [Key(VkControl, 0), Key(virtualKey, 0)];
        SendInput((uint)down.Length, down, InputSize);
        Thread.Sleep(25);

        INPUT[] up = [Key(virtualKey, KeyEventKeyUp), Key(VkControl, KeyEventKeyUp)];
        SendInput((uint)up.Length, up, InputSize);
        Thread.Sleep(15);

        Log.Write($"Ctrl+{(int)key}: sent strokes, error {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Sends Ctrl+Shift plus the given letter (standard for modern terminals).</summary>
    public static void PressControlShift(char key)
    {
        var virtualKey = (ushort)char.ToUpperInvariant(key);
        ReleaseModifiers(releaseCtrl: false);

        INPUT[] down = [Key(VkControl, 0), Key(VkShift, 0), Key(virtualKey, 0)];
        SendInput((uint)down.Length, down, InputSize);
        Thread.Sleep(25);

        INPUT[] up = [Key(virtualKey, KeyEventKeyUp), Key(VkShift, KeyEventKeyUp), Key(VkControl, KeyEventKeyUp)];
        SendInput((uint)up.Length, up, InputSize);
        Thread.Sleep(15);

        Log.Write($"Ctrl+Shift+{(int)key}: sent strokes, error {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Sends Shift+Insert: the universal paste shortcut for all Windows terminals.</summary>
    public static void PressShiftInsert()
    {
        ReleaseModifiers(releaseCtrl: true);

        INPUT[] down = [Key(VkShift, 0), Key(VkInsert, 0)];
        SendInput((uint)down.Length, down, InputSize);
        Thread.Sleep(25);

        INPUT[] up = [Key(VkInsert, KeyEventKeyUp), Key(VkShift, KeyEventKeyUp)];
        SendInput((uint)up.Length, up, InputSize);
        Thread.Sleep(15);

        Log.Write("Shift+Insert: sent paste strokes");
    }

    /// <summary>Pastes into foreground window using the appropriate keystroke.</summary>
    public static void PressPaste(bool isTerminal)
    {
        if (isTerminal)
            PressShiftInsert();
        else
            PressControl('V');
    }

    static INPUT Key(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new INPUTUnion
        {
            Keyboard = new KeyBoardInput
            {
                VirtualKey = virtualKey,
                ScanCode = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyBoardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Horizontal;
        public int Vertical;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public uint Message;
        public ushort LowParameter;
        public ushort HighParameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyBoardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
