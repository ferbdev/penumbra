using System.Drawing;
using System.Drawing.Drawing2D;

namespace Penumbra.UI;

public static class TrayIcons
{
    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var screen = new SolidBrush(Color.FromArgb(235, 240, 245));
            graphics.FillRectangle(brush, 2, 4, 28, 19);
            graphics.FillRectangle(screen, 5, 7, 22, 13);
            graphics.FillRectangle(brush, 13, 23, 6, 3);
            graphics.FillRectangle(brush, 8, 26, 16, 3);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
