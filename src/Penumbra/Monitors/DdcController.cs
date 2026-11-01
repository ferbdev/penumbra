using System.Collections.Concurrent;
using Penumbra.Config;
using Penumbra.Game;
using static Penumbra.Monitors.NativeMethods;

namespace Penumbra.Monitors;

public sealed class DdcController : IDisplayBackend
{
    private const byte VcpBrightness = 0x10;
    private const byte VcpPower = 0xD6;
    private const uint PowerOn = 1;
    private const uint PowerOff = 5;
    private const uint PowerStandby = 4;
    private const int Attempts = 3;
    private const int DefaultMax = 100;
    private static readonly object BusGate = new();
    private static readonly ConcurrentDictionary<string, int> MaxBrightness = new();
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WakeDelay = TimeSpan.FromSeconds(2);

    public static (int Current, int Max)? ReadBrightness(MonitorInfo monitor)
    {
        var brightness = Read(monitor, VcpBrightness);
        if (brightness is not { } value || value.Max <= 0 || value.Current > value.Max)
            return null;

        MaxBrightness.TryAdd(monitor.Id, value.Max);
        return value;
    }

    public static (int Current, int Max)? ReadPower(MonitorInfo monitor) => Read(monitor, VcpPower);

    public static bool SetBrightnessRaw(MonitorInfo monitor, int value) => Write(monitor, VcpBrightness, (uint)value);

    public static bool SetPower(MonitorInfo monitor, bool on)
    {
        if (on)
            return Write(monitor, VcpPower, PowerOn);

        return Write(monitor, VcpPower, PowerOff) || Write(monitor, VcpPower, PowerStandby);
    }

    public static int? ReadPercent(string monitorId)
    {
        var monitor = MonitorService.Enumerate().FirstOrDefault(m => m.Id == monitorId);
        var brightness = monitor is null ? null : ReadBrightness(monitor);
        return brightness?.Max > 0 ? brightness.Value.Current * 100 / brightness.Value.Max : null;
    }

    public static bool SetPercent(string monitorId, int percent)
    {
        var monitor = MonitorService.Enumerate().FirstOrDefault(m => m.Id == monitorId);
        return monitor is not null && SetBrightnessPercent(monitor, percent);
    }

    public IReadOnlyList<MonitorSnapshot> Capture(Preset preset, string? mainId)
    {
        var snapshots = new List<MonitorSnapshot>();

        foreach (var (monitor, action) in Targets(preset, mainId))
        {
            var brightness = ReadBrightness(monitor);
            if (brightness is null)
            {
                Log.Write($"Sem leitura de brilho em {monitor.DisplayLabel}; monitor ignorado.");
                continue;
            }

            snapshots.Add(new MonitorSnapshot(monitor.Id, brightness.Value.Current, action.PowerOff));
        }

        return snapshots;
    }

    public void Apply(Preset preset, string? mainId, IReadOnlyCollection<string> capturedIds)
    {
        foreach (var (monitor, action) in Targets(preset, mainId))
        {
            // Sem snapshot não há como voltar ao estado anterior, então o monitor fica intacto.
            if (!capturedIds.Contains(monitor.Id))
                continue;

            var ok = action.PowerOff ? SetPower(monitor, false) : SetBrightnessPercent(monitor, action.Brightness);
            if (!ok)
                Log.Write($"Falha ao aplicar preset em {monitor.DisplayLabel}.");
        }
    }

    public IReadOnlyList<MonitorSnapshot> Restore(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        var failed = new List<MonitorSnapshot>();
        var monitors = MonitorService.Enumerate();

        foreach (var snapshot in snapshots)
        {
            var monitor = monitors.FirstOrDefault(m => m.Id == snapshot.MonitorId);
            if (monitor is null || !RestoreOne(monitor, snapshot))
                failed.Add(snapshot);
        }

        return failed;
    }

    private static bool RestoreOne(MonitorInfo monitor, MonitorSnapshot snapshot)
    {
        if (snapshot.WasPoweredOff)
        {
            if (!SetPower(monitor, true))
                return false;

            Thread.Sleep(WakeDelay);
        }

        return snapshot.TargetPercent is { } percent
            ? SetBrightnessPercent(monitor, percent)
            : SetBrightnessRaw(monitor, snapshot.Brightness);
    }

    private static bool SetBrightnessPercent(MonitorInfo monitor, int percent)
    {
        // Depois de uma queda ainda não há máximo guardado; escritas anteriores já respeitaram a pausa.
        if (!MaxBrightness.ContainsKey(monitor.Id))
            ReadBrightness(monitor);

        var max = MaxBrightness.TryGetValue(monitor.Id, out var cached) ? cached : DefaultMax;
        return SetBrightnessRaw(monitor, Math.Clamp(percent, 0, 100) * max / 100);
    }

    private static IEnumerable<(MonitorInfo Monitor, MonitorAction Action)> Targets(Preset preset, string? mainId)
    {
        var monitors = MonitorService.Enumerate();
        var main = MonitorService.ResolveMain(monitors, mainId);

        foreach (var monitor in monitors)
        {
            if (monitor.Id != main?.Id && !monitor.SharesPanelWith(main) && preset.Actions.TryGetValue(monitor.Id, out var action))
                yield return (monitor, action);
        }
    }

    private static (int Current, int Max)? Read(MonitorInfo monitor, byte code) =>
        WithPhysicalMonitor(monitor, handle =>
        {
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                if (GetVCPFeatureAndVCPFeatureReply(handle, code, IntPtr.Zero, out var current, out var max))
                    return ((int)current, (int)max);

                Thread.Sleep(RetryDelay);
            }

            return ((int, int)?)null;
        });

    private static bool Write(MonitorInfo monitor, byte code, uint value) =>
        WithPhysicalMonitor(monitor, handle =>
        {
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                var ok = SetVCPFeature(handle, code, value);

                // Dentro do lock, para nenhuma leitura pegar o monitor ainda processando a gravação.
                Thread.Sleep(ok ? SettleDelay : RetryDelay);
                if (ok)
                    return true;
            }

            return false;
        });

    // Handles são reabertos a cada operação porque a dock pode ter sido reconectada.
    private static T? WithPhysicalMonitor<T>(MonitorInfo monitor, Func<IntPtr, T> action)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out var count) || count == 0)
            return default;

        var physical = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, physical))
            return default;

        try
        {
            // O modo jogo e os botões manuais rodam em threads diferentes no mesmo barramento.
            lock (BusGate)
                return action(physical[0].hPhysicalMonitor);
        }
        finally
        {
            DestroyPhysicalMonitors(count, physical);
        }
    }
}
