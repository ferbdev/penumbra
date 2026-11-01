using System.Drawing;
using System.Text;
using Penumbra.Monitors;
using Xunit;

namespace Penumbra.Tests;

public class PanelIdentityTests
{
    private static byte[] Edid(ushort product, string? textSerial, uint numericSerial = 0)
    {
        var edid = new byte[128];
        edid[8] = 0x1E;
        edid[9] = 0x6D;
        BitConverter.GetBytes(product).CopyTo(edid, 10);
        BitConverter.GetBytes(numericSerial).CopyTo(edid, 12);

        if (textSerial is not null)
        {
            edid[75] = 0xFF;
            Encoding.ASCII.GetBytes(textSerial + "\n").CopyTo(edid, 77);
        }

        return edid;
    }

    private static MonitorInfo Monitor(string id, string? panel) =>
        new(id, "M", @"\\.\DISPLAY1", new Rectangle(0, 0, 1, 1), false, IntPtr.Zero, panel);

    private static byte[] EdidWithHdmiBlock()
    {
        var edid = new byte[256];
        edid[20] = 0x80;
        edid[128] = 2;
        edid[130] = 12;
        new byte[] { 0x65, 0x03, 0x0C, 0x00, 0x10, 0x00 }.CopyTo(edid, 132);
        return edid;
    }

    [Fact]
    public void Hdmi_vendor_block_identifies_hdmi() =>
        Assert.Equal("HDMI", EdidReader.ParseInput(EdidWithHdmiBlock()));

    [Theory]
    [InlineData(0xB5, "DisplayPort")]
    [InlineData(0xA2, "HDMI")]
    [InlineData(0xA1, "DVI")]
    [InlineData(0x80, null)]
    [InlineData(0x25, null)]
    public void Input_comes_from_digital_interface_byte(int value, string? expected)
    {
        var edid = new byte[128];
        edid[20] = (byte)value;
        Assert.Equal(expected, EdidReader.ParseInput(edid));
    }

    [Fact]
    public void Label_shows_input_when_known()
    {
        var monitor = new MonitorInfo("a", "LG", @"\\.\DISPLAY6", default, false, IntPtr.Zero, Input: "HDMI");
        Assert.Equal("LG - HDMI (DISPLAY6)", monitor.DisplayLabel);
        Assert.Equal("LG (DISPLAY6)", (monitor with { Input = null }).DisplayLabel);
    }

    [Fact]
    public void Two_inputs_of_same_monitor_have_same_key() =>
        Assert.Equal(EdidReader.ParsePanelKey(Edid(0x5CDD, "100001")), EdidReader.ParsePanelKey(Edid(0x5CDE, "100001")));

    [Fact]
    public void Different_serials_have_different_keys() =>
        Assert.NotEqual(EdidReader.ParsePanelKey(Edid(0x5CDD, "100001")), EdidReader.ParsePanelKey(Edid(0x5CDD, "100002")));

    [Fact]
    public void Falls_back_to_numeric_serial() =>
        Assert.EndsWith(":1234", EdidReader.ParsePanelKey(Edid(1, null, 1234)));

    [Fact]
    public void Without_serial_key_is_null() =>
        Assert.Null(EdidReader.ParsePanelKey(Edid(1, null)));

    [Fact]
    public void Unknown_panels_are_not_treated_as_shared() =>
        Assert.False(Monitor("a", null).SharesPanelWith(Monitor("b", null)));

    [Fact]
    public void Same_panel_on_different_ids_is_shared() =>
        Assert.True(Monitor("a", "x").SharesPanelWith(Monitor("b", "x")));
}
