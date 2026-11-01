using System.Drawing;
using System.Text;
using Penumbra.Monitors;
using static Penumbra.Monitors.NativeMethods;

namespace Penumbra.Game;

public static class FullscreenDetector
{
    private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public static bool IsFullscreenOn(Rectangle monitor)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == GetShellWindow() || window == GetDesktopWindow())
            return false;

        if (!IsWindowVisible(window) || IsIconic(window) || IsShellClass(window))
            return false;

        if (!GetWindowRect(window, out var rect))
            return false;

        var maximizedWithCaption = IsZoomed(window) && ((uint)GetWindowLong(window, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        var exclusive = SHQueryUserNotificationState(out var state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;

        return Evaluate(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), monitor, maximizedWithCaption, exclusive);
    }

    public static bool Evaluate(Rectangle window, Rectangle monitor, bool maximizedWithCaption, bool exclusive)
    {
        var center = new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);
        if (exclusive && monitor.Contains(center))
            return true;

        // Janela maximizada comum pode cobrir o monitor quando a barra de tarefas está oculta.
        return !maximizedWithCaption && window.Contains(monitor);
    }

    private static bool IsShellClass(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return ShellClasses.Contains(name.ToString());
    }
}
