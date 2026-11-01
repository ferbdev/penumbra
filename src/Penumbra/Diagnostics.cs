using Penumbra.Monitors;

namespace Penumbra;

// Uso: Penumbra.exe --list | --set DISPLAY5 <0-100|on|off>
public static class Diagnostics
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || !args[0].StartsWith("--") || args[0] == Program.ShowArgument)
            return false;

        NativeMethods.AttachConsole(-1);

        switch (args[0])
        {
            case "--list":
                List();
                break;
            case "--set" when args.Length == 3:
                Set(args[1], args[2]);
                break;
            default:
                Console.WriteLine("Uso: Penumbra.exe --list | --set DISPLAY5 <0-100|on|off>");
                break;
        }

        return true;
    }

    private static void List()
    {
        foreach (var monitor in MonitorService.Enumerate())
        {
            var brightness = DdcController.ReadBrightness(monitor);
            var power = DdcController.ReadPower(monitor);
            Console.WriteLine($"{monitor.DisplayLabel} ({monitor.GdiName.Replace(@"\\.\", "")}) {monitor.Bounds.Width}x{monitor.Bounds.Height} principal={monitor.IsPrimary}");
            Console.WriteLine($"  id: {monitor.Id}");
            Console.WriteLine($"  painel: {monitor.PanelKey ?? "?"}");
            Console.WriteLine($"  brilho: {brightness?.Current.ToString() ?? "?"}/{brightness?.Max.ToString() ?? "?"} energia: {power?.Current.ToString() ?? "?"}");
        }
    }

    private static void Set(string display, string value)
    {
        var monitor = MonitorService.Enumerate()
            .FirstOrDefault(m => m.GdiName.EndsWith(display, StringComparison.OrdinalIgnoreCase));

        if (monitor is null)
        {
            Console.WriteLine($"Monitor {display} não encontrado.");
            return;
        }

        var ok = value switch
        {
            "on" => DdcController.SetPower(monitor, true),
            "off" => DdcController.SetPower(monitor, false),
            _ => DdcController.SetBrightnessRaw(monitor, int.Parse(value))
        };

        Console.WriteLine(ok ? "OK" : "FALHOU");
    }
}
