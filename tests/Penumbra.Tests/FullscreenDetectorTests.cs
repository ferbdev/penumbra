using System.Drawing;
using Penumbra.Game;
using Xunit;

namespace Penumbra.Tests;

public class FullscreenDetectorTests
{
    private static readonly Rectangle Main = new(0, 0, 1920, 1080);
    private static readonly Rectangle Side = new(1920, 0, 1080, 1920);

    [Fact]
    public void Borderless_window_covering_monitor_is_fullscreen() =>
        Assert.True(FullscreenDetector.Evaluate(Main, Main, false, false));

    [Fact]
    public void Smaller_window_is_not_fullscreen() =>
        Assert.False(FullscreenDetector.Evaluate(new Rectangle(0, 0, 1920, 1040), Main, false, false));

    [Fact]
    public void Maximized_window_with_caption_is_not_fullscreen() =>
        Assert.False(FullscreenDetector.Evaluate(new Rectangle(-8, -8, 1936, 1096), Main, true, false));

    [Fact]
    public void Fullscreen_on_other_monitor_is_ignored() =>
        Assert.False(FullscreenDetector.Evaluate(Side, Main, false, false));

    [Fact]
    public void Exclusive_fullscreen_counts_when_centered_on_main() =>
        Assert.True(FullscreenDetector.Evaluate(new Rectangle(0, 0, 1280, 720), Main, false, true));

    [Fact]
    public void Exclusive_fullscreen_on_other_monitor_is_ignored() =>
        Assert.False(FullscreenDetector.Evaluate(Side, Main, false, true));
}
