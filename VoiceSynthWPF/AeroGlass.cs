using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoiceSynthWPF;

/// <summary>
/// Включает эффект Aero Glass (blur-behind) через DWM.
/// Совместимо с Windows 7, 8, 10 и 11.
/// На Windows 10 1903+ даёт полупрозрачное матовое размытие.
/// </summary>
public static class AeroGlass
{
    // ─── P/Invoke ────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    // AccentState values
    private const int ACCENT_DISABLED                = 0;
    private const int ACCENT_ENABLE_BLURBEHIND       = 3; // Windows 10 Aero blur
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4; // Windows 10 1803+ Acrylic

    private const int WCA_ACCENT_POLICY = 19;

    // ─── Публичное API ───────────────────────────────────────────────────────

    /// <summary>
    /// Применяет Aero Glass к окну.
    /// Вызывать после того как окно показано (SourceInitialized или Loaded).
    /// </summary>
    /// <param name="window">Целевое окно.</param>
    /// <param name="useAcrylic">
    /// true  — Acrylic (матовое стекло, Windows 10 1803+, рекомендуется).
    /// false — классический Aero blur (работает на Windows 7/8/10).
    /// </param>
    /// <param name="tintColor">
    /// Цвет тонировки для Acrylic в формате ARGB (0xAARRGGBB).
    /// По умолчанию — тёмный полупрозрачный.
    /// </param>
    public static void Enable(Window window, bool useAcrylic = true, uint tintColor = 0xCC1E1E1E)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        DwmIsCompositionEnabled(out var dwmEnabled);
        if (!dwmEnabled) return;

        // Только включаем blur — DWM frame уже расширен через WindowChrome GlassFrameThickness="-1"
        EnableBlur(hwnd, useAcrylic, tintColor);
    }

    /// <summary>Отключает blur, возвращает обычный фон.</summary>
    public static void Disable(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var accent = new AccentPolicy { AccentState = ACCENT_DISABLED };
        var accentSize = Marshal.SizeOf(accent);
        var accentPtr  = Marshal.AllocHGlobal(accentSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttribData
        {
            Attribute  = WCA_ACCENT_POLICY,
            Data       = accentPtr,
            SizeOfData = accentSize
        };
        SetWindowCompositionAttribute(hwnd, ref data);
        Marshal.FreeHGlobal(accentPtr);
    }

    private static void EnableBlur(IntPtr hwnd, bool useAcrylic, uint tintColor)
    {
        var accent = new AccentPolicy
        {
            AccentState   = useAcrylic ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_ENABLE_BLURBEHIND,
            AccentFlags   = 2,                        // draw all borders
            GradientColor = (int)tintColor,           // ARGB tint
            AnimationId   = 0
        };

        var accentSize = Marshal.SizeOf(accent);
        var accentPtr  = Marshal.AllocHGlobal(accentSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttribData
        {
            Attribute  = WCA_ACCENT_POLICY,
            Data       = accentPtr,
            SizeOfData = accentSize
        };

        SetWindowCompositionAttribute(hwnd, ref data);
        Marshal.FreeHGlobal(accentPtr);
    }

    /// <summary>
    /// Хук на WM_DWMCOMPOSITIONCHANGED — переподключает glass если DWM
    /// включили/выключили (например при смене темы или RDP сессии).
    /// </summary>
    public static void HookCompositionChange(Window window, bool useAcrylic = true, uint tintColor = 0xCC1E1E1E)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
            if (msg == WM_DWMCOMPOSITIONCHANGED)
                Enable(window, useAcrylic, tintColor);
            return IntPtr.Zero;
        });
    }
}
