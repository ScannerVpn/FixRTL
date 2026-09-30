using System;
using Xunit;
using RtlFix.Core.Shaping;

namespace RtlFix.Core.Tests.Transforms;

public class FullBufferShapeTest
{
    [Fact]
    public void ShapeEntireScreen()
    {
        DoShape(43196);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint dwProcessId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool ReadConsoleOutputCharacter(IntPtr hConsoleOutput, [System.Runtime.InteropServices.Out] char[] lpCharacter, uint nLength, COORD dwReadCoord, out uint lpNumberOfCharsRead);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool WriteConsoleOutputCharacter(IntPtr hConsoleOutput, string lpCharacter, uint nLength, COORD dwWriteCoord, out uint lpNumberOfCharsWritten);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct COORD { public short X; public short Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct SMALL_RECT { public short Left; public short Top; public short Right; public short Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct CONSOLE_SCREEN_BUFFER_INFO { public COORD dwSize; public COORD dwCursorPosition; public ushort wAttributes; public SMALL_RECT srWindow; public COORD dwMaximumWindowSize; }

    static void DoShape(uint pid)
    {
        FreeConsole();
        if (!AttachConsole(pid)) return;
        var hOut = CreateFile("CONOUT$", 0xC0000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (hOut == IntPtr.Zero || hOut == new IntPtr(-1)) return;

        try
        {
            if (!GetConsoleScreenBufferInfo(hOut, out var info)) return;
            var width = info.dwSize.X;
            var buf = new char[width];
            uint read, written;
            var cursorY = (int)info.dwCursorPosition.Y;

            for (short y = 0; y < info.dwSize.Y; y++)
            {
                if (y >= cursorY - 1 && y <= cursorY + 1) continue;

                COORD c = new COORD { X = 0, Y = y };
                if (ReadConsoleOutputCharacter(hOut, buf, (uint)width, c, out read) && read > 0)
                {
                    var line = new string(buf, 0, (int)read);
                    if (line.Contains('\u2500') || line.Contains('\u276F')) continue;

                    var shaped = PersianShaper.Shape(line);
                    if (shaped != line)
                    {
                        var transformed = shaped;
                        if (transformed.Length < width) transformed = transformed.PadRight(width);
                        else if (transformed.Length > width) transformed = transformed.Substring(0, width);
                        WriteConsoleOutputCharacter(hOut, transformed, (uint)width, c, out written);
                    }
                }
            }
        }
        finally
        {
            CloseHandle(hOut);
            FreeConsole();
        }
    }
}
