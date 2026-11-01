using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Penumbra.UI;

public static class GlassBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    // Formato AABBGGRR: azul-escuro com transparência suficiente para o blur aparecer.
    private const uint TintColor = 0x66201812;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int Size;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttributeData data);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        // O blur é desenhado pelo sistema, então o WPF precisa limpar a janela com transparência.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;

        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        EnableBlur(hwnd);
    }

    // O backdrop documentado (DWMWA_SYSTEMBACKDROP_TYPE) fica opaco sem foco, o que não serve para um widget.
    private static void EnableBlur(IntPtr hwnd)
    {
        var accent = new AccentPolicy { AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND, GradientColor = TintColor };
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new CompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = pointer, Size = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
}
