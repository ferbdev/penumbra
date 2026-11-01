using System.Collections.Concurrent;
using System.Text;
using Microsoft.Win32;

namespace Penumbra.Monitors;

public static class EdidReader
{
    private const int DescriptorSize = 18;
    private static readonly int[] DescriptorOffsets = { 54, 72, 90, 108 };
    private const int BlockSize = 128;
    private const byte CtaExtensionTag = 2;
    private const int VendorDataBlock = 3;
    private const int InterfaceDvi = 1;
    private const int InterfaceHdmiA = 2;
    private const int InterfaceHdmiB = 3;
    private const int InterfaceDisplayPort = 5;
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new();

    public static string? PanelKeyFor(string deviceId) => ParsePanelKey(EdidFor(deviceId));

    public static string? InputFor(string deviceId) => ParseInput(EdidFor(deviceId));

    // O Windows informa DisplayPort até para HDMI ligado em dock DisplayLink, então vale o que o monitor declara.
    public static string? ParseInput(byte[]? edid)
    {
        if (edid is null || edid.Length < BlockSize)
            return null;

        if (HasHdmiVendorBlock(edid))
            return "HDMI";

        var digital = (edid[20] & 0x80) != 0;
        return (digital ? edid[20] & 0x0F : 0) switch
        {
            InterfaceDisplayPort => "DisplayPort",
            InterfaceHdmiA or InterfaceHdmiB => "HDMI",
            InterfaceDvi => "DVI",
            _ => null
        };
    }

    private static bool HasHdmiVendorBlock(byte[] edid)
    {
        for (var block = BlockSize; block + BlockSize <= edid.Length; block += BlockSize)
        {
            if (edid[block] != CtaExtensionTag)
                continue;

            var end = block + edid[block + 2];
            for (var pos = block + 4; pos < end && pos + 3 < edid.Length; pos += (edid[pos] & 0x1F) + 1)
            {
                // Identificador IEEE do HDMI Licensing, gravado de trás para frente.
                if (edid[pos] >> 5 == VendorDataBlock && edid[pos + 1] == 0x03 && edid[pos + 2] == 0x0C && edid[pos + 3] == 0x00)
                    return true;
            }
        }

        return false;
    }

    private static byte[]? EdidFor(string deviceId) => Cache.GetOrAdd(deviceId, ReadEdid);

    // O código do produto muda conforme a entrada, por isso a chave usa fabricante e serial.
    public static string? ParsePanelKey(byte[]? edid)
    {
        if (edid is null || edid.Length < 128)
            return null;

        var manufacturer = (edid[8] << 8) | edid[9];
        var serial = TextSerial(edid);
        if (serial is null)
        {
            var numeric = BitConverter.ToUInt32(edid, 12);
            serial = numeric == 0 ? null : numeric.ToString();
        }

        return serial is null ? null : $"{manufacturer:X4}:{serial}";
    }

    private static string? TextSerial(byte[] edid)
    {
        foreach (var offset in DescriptorOffsets)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 3] != 0xFF)
                continue;

            var text = Encoding.ASCII.GetString(edid, offset + 5, DescriptorSize - 5).Trim('\n', ' ', '\0');
            return text.Length == 0 ? null : text;
        }

        return null;
    }

    private static byte[]? ReadEdid(string deviceId)
    {
        // Formato: \\?\DISPLAY#GSM5CDD#9&14a8583e&0&UID257#{guid}
        var parts = deviceId.Split('#');
        if (parts.Length < 3)
            return null;

        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");

        return key?.GetValue("EDID") as byte[];
    }
}
