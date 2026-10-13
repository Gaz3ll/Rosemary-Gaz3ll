using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SOR.Presentation.Services;

public static class WindowTitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_FRAMECHANGED = 0x0020;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    public static void Apply(Window window, AppTheme theme)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                // Okno jeszcze nie zainicjalizowane; spróbujemy ponownie po załadowaniu.
                window.Loaded += (_, _) => Apply(window, theme);
                return;
            }

            var isDark = theme == AppTheme.Dark;
            int attribute = Environment.OSVersion.Version.Build >= 17763 ? DWMWA_USE_IMMERSIVE_DARK_MODE : DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            int value = isDark ? 1 : 0;
            var result = DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());

            if (result != 0)
            {
                // Spróbuj starszego atrybutu dla kompatybilności.
                attribute = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
                result = DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());
            }

            // Wymuszenie przerysowania ramki okna, by zmiana tytułowego paska była widoczna.
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch (Exception)
        {
            // Brak obsługi DWM — aplikacja powinna działać normalnie bez tego efektu.
        }
    }
}