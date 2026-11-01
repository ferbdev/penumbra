using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Penumbra.UI;

public static class TrayIcons
{
    private const string ResourceName = "Penumbra.ico";

    // Mesmo ícone do executável; o ponto opcional indica o estado na bandeja.
    public static Icon Create(Color? badge = null)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var icon = new Icon(stream, SystemInformation.SmallIconSize);
        using var bitmap = icon.ToBitmap();

        if (badge is { } color)
            DrawBadge(bitmap, color);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DrawBadge(Bitmap bitmap, Color color)
    {
        var size = Math.Max(6, bitmap.Width * 9 / 20);
        var x = bitmap.Width - size;
        var y = bitmap.Height - size;

        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(color);
        using var outline = new Pen(Color.FromArgb(20, 18, 40));

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillEllipse(brush, x, y, size - 1, size - 1);
        graphics.DrawEllipse(outline, x, y, size - 1, size - 1);
    }
}
