using System;
using RtlFix.Core.Transforms;

public class TestRtlizeLine
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint dwProcessId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool WriteConsoleOutputCharacter(IntPtr hConsoleOutput, string lpCharacter, uint nLength, COORD dwWriteCoord, out uint lpNumberOfCharsWritten);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct COORD { public short X; public short Y; }

    public static void Apply(uint pid, short y)
    {
        FreeConsole();
        if (!AttachConsole(pid)) return;
        var hOut = CreateFile("CONOUT$", 0xC0000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        
        var original = "   4. کارهای استارتاپ را اختیاری کنید: EnableAll فقط با دکمه کاربر (App Manager) اجرا شود؛ UAC خودکار در بوت حذف شود؛";
        var opt = new RtlOptions { Mode = RtlMode.Visual, Shape = true, Mirror = true };
        var transformed = RtlTransform.Rtlize(original, opt);
        
        uint written;
        COORD c = new COORD { X = 0, Y = y };
        WriteConsoleOutputCharacter(hOut, transformed.PadRight(171), 171, c, out written);
        
        CloseHandle(hOut);
        FreeConsole();
    }
}