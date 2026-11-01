using System.Drawing;
using System.Runtime.InteropServices;
using static Penumbra.Monitors.NativeMethods;

namespace Penumbra.Monitors;

public sealed record MonitorInfo(string Id, string Name, string GdiName, Rectangle Bounds, bool IsPrimary, IntPtr Handle, string? PanelKey = null, string? Input = null)
{
    public string DisplayLabel => Input is null ? Name : $"{Name} - {Input}";

    // Um monitor ligado por duas entradas aparece duas vezes, mas tem um só brilho e uma só energia.
    public bool SharesPanelWith(MonitorInfo? other) =>
        other is not null && other.Id != Id && PanelKey is not null && PanelKey == other.PanelKey;
}

public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var targets = QueryTargets();
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(handle, ref info))
                return true;

            var rect = info.rcMonitor;
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;

            // DISPLAYn muda entre reconexões, então o caminho do dispositivo é o ID salvo.
            targets.TryGetValue(info.szDevice, out var target);
            var id = string.IsNullOrEmpty(target.Path) ? info.szDevice : target.Path;
            var name = string.IsNullOrEmpty(target.Name) ? "Monitor" : target.Name;

            monitors.Add(new MonitorInfo(id, name, info.szDevice, bounds, primary, handle,
                EdidReader.PanelKeyFor(id), EdidReader.InputFor(id)));
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public static MonitorInfo? ResolveMain(IReadOnlyList<MonitorInfo> monitors, string? mainId) =>
        monitors.FirstOrDefault(m => m.Id == mainId)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();

    public static IEnumerable<MonitorInfo> SamePanel(IEnumerable<MonitorInfo> monitors, MonitorInfo monitor) =>
        monitors.Where(m => m.Id == monitor.Id || m.SharesPanelWith(monitor));

    // Um item por monitor físico; se o principal for uma das entradas, é ele quem representa o monitor.
    public static IReadOnlyList<MonitorInfo> PhysicalMonitors(IReadOnlyList<MonitorInfo> monitors, string? mainId)
    {
        var ordered = monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
        var main = ResolveMain(monitors, mainId);

        return ordered
            .GroupBy(m => m.PanelKey ?? m.Id)
            .Select(panel => panel.FirstOrDefault(m => m.Id == main?.Id) ?? panel.First())
            .ToList();
    }

    private static Dictionary<string, (string Path, string Name)> QueryTargets()
    {
        var result = new Dictionary<string, (string Path, string Name)>(StringComparer.OrdinalIgnoreCase);

        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
            return result;

        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            return result;

        for (var i = 0; i < pathCount; i++)
        {
            var source = new SourceDeviceName();
            source.header.type = DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = Marshal.SizeOf<SourceDeviceName>();
            source.header.adapterId = paths[i].sourceInfo.adapterId;
            source.header.id = paths[i].sourceInfo.id;

            var target = new TargetDeviceName();
            target.header.type = DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = Marshal.SizeOf<TargetDeviceName>();
            target.header.adapterId = paths[i].targetInfo.adapterId;
            target.header.id = paths[i].targetInfo.id;

            if (DisplayConfigGetDeviceInfo(ref source) == 0 && DisplayConfigGetDeviceInfo(ref target) == 0)
                result.TryAdd(source.viewGdiDeviceName, (target.monitorDevicePath, target.monitorFriendlyDeviceName));
        }

        return result;
    }
}
