using Penumbra.Config;

namespace Penumbra.Game;

public enum GameModeState
{
    Normal,
    Detecting,
    Game,
    Leaving
}

// TargetPercent, quando existe, é o padrão salvo e tem prioridade sobre o valor capturado.
public sealed record MonitorSnapshot(string MonitorId, int Brightness, bool WasPoweredOff, int? TargetPercent = null);

public interface IDisplayBackend
{
    IReadOnlyList<MonitorSnapshot> Capture(Preset preset, string? mainId);
    void Apply(Preset preset, string? mainId, IReadOnlyCollection<string> capturedIds);

    // Devolve os snapshots que não puderam ser restaurados.
    IReadOnlyList<MonitorSnapshot> Restore(IReadOnlyList<MonitorSnapshot> snapshots);
}

public interface ISnapshotStore
{
    IReadOnlyList<MonitorSnapshot> Load();
    void Save(IReadOnlyList<MonitorSnapshot> snapshots);
    void Clear();
}

public sealed class GameModeController
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    private readonly IDisplayBackend _backend;
    private readonly ISnapshotStore _store;
    private readonly Func<AppConfig> _config;
    private List<MonitorSnapshot> _pending = new();
    private DateTime _since;
    private DateTime _nextRetry = DateTime.MinValue;

    public GameModeController(IDisplayBackend backend, ISnapshotStore store, Func<AppConfig> config)
    {
        _backend = backend;
        _store = store;
        _config = config;
    }

    public GameModeState State { get; private set; } = GameModeState.Normal;

    public bool ManualOn { get; set; }

    // Um snapshot que sobrou no disco significa que o app caiu com o preset aplicado.
    public void Recover() => _pending = _store.Load().ToList();

    public void Tick(bool fullscreen, DateTime now)
    {
        var config = _config();
        var active = fullscreen || ManualOn;

        switch (State)
        {
            case GameModeState.Normal:
                if (ManualOn)
                    Enter(config);
                else if (fullscreen)
                    (State, _since) = (GameModeState.Detecting, now);
                break;

            case GameModeState.Detecting:
                if (ManualOn || (fullscreen && now - _since >= TimeSpan.FromSeconds(config.ActivationSeconds)))
                    Enter(config);
                else if (!fullscreen)
                    State = GameModeState.Normal;
                break;

            case GameModeState.Game:
                if (!active)
                    (State, _since) = (GameModeState.Leaving, now);
                break;

            case GameModeState.Leaving:
                if (active)
                    State = GameModeState.Game;
                else if (now - _since >= TimeSpan.FromSeconds(config.RestoreSeconds))
                {
                    State = GameModeState.Normal;
                    RestorePending(now);
                }
                break;
        }

        if (State is GameModeState.Normal or GameModeState.Detecting && _pending.Count > 0 && now >= _nextRetry)
            RestorePending(now);
    }

    public void RestoreNow()
    {
        State = GameModeState.Normal;
        RestorePending(DateTime.Now);
    }

    private void Enter(AppConfig config)
    {
        State = GameModeState.Game;

        var preset = config.ActivePreset;
        if (preset is null)
            return;

        // O que ainda não foi restaurado guarda o valor original; capturar de novo pegaria o valor do preset.
        var pendingIds = _pending.Select(s => s.MonitorId).ToHashSet();
        _pending.AddRange(_backend.Capture(preset, config.MainMonitorId).Where(s => !pendingIds.Contains(s.MonitorId)));

        _store.Save(_pending);
        _backend.Apply(preset, config.MainMonitorId, _pending.Select(s => s.MonitorId).ToList());
        Log.Write($"Modo jogo ativado com o preset '{preset.Name}'.");
    }

    private void RestorePending(DateTime now)
    {
        if (_pending.Count == 0)
            return;

        // Lido a cada tentativa para valer o padrão mais recente, mesmo se mudou durante o jogo.
        var defaults = _config().DefaultBrightness;
        var targets = _pending
            .Select(s => s with { TargetPercent = defaults.TryGetValue(s.MonitorId, out var percent) ? percent : null })
            .ToList();

        _pending = _backend.Restore(targets).ToList();
        _nextRetry = now + RetryInterval;

        if (_pending.Count == 0)
        {
            _store.Clear();
            Log.Write("Monitores restaurados.");
        }
        else
        {
            _store.Save(_pending);
            Log.Write($"{_pending.Count} monitor(es) ainda sem restaurar; nova tentativa em breve.");
        }
    }
}
