using System.Drawing;
using Penumbra.UI;
using Xunit;

namespace Penumbra.Tests;

public class TrayIconsTests
{
    [Fact]
    public void Icon_comes_from_the_embedded_eclipse()
    {
        using var icon = TrayIcons.Create();
        using var bitmap = icon.ToBitmap();

        var crescent = bitmap.GetPixel(bitmap.Width * 22 / 64, bitmap.Height * 40 / 64);

        Assert.True(crescent.R > 200 && crescent.G > 140 && crescent.B < 120, $"Pixel inesperado: {crescent}");
    }

    [Fact]
    public void Badge_changes_the_bottom_right_corner()
    {
        using var icon = TrayIcons.Create(Color.Lime);
        using var bitmap = icon.ToBitmap();

        var pixel = bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height * 3 / 4);

        Assert.True(pixel.G > 200 && pixel.R < 100, $"Pixel inesperado: {pixel}");
    }
}
