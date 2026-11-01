using Penumbra.Config;
using Penumbra.Game;
using Xunit;

namespace Penumbra.Tests;

public class GameModeControllerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    private sealed class FakeBackend : IDisplayBackend
    {
        public int Applied;
        public int Captured;
        public int Restored;
        public bool FailRestore;

        public IReadOnlyList<MonitorSnapshot> Capture(Preset preset, string? mainId)
        {
            Captured++;
            return new[] { new MonitorSnapshot("sec", Captured == 1 ? 80 : 10, false) };
        }

        public void Apply(Preset preset, string? mainId, IReadOnlyCollection<string> capturedIds) => Applied++;

        public IReadOnlyList<MonitorSnapshot> Restore(IReadOnlyList<MonitorSnapshot> snapshots)
        {
            Restored++;
            LastRestored = snapshots;
            return FailRestore ? snapshots : Array.Empty<MonitorSnapshot>();
        }

        public IReadOnlyList<MonitorSnapshot> LastRestored = Array.Empty<MonitorSnapshot>();
    }

    private sealed class FakeStore : ISnapshotStore
    {
        public List<MonitorSnapshot> Saved = new();

        public IReadOnlyList<MonitorSnapshot> Load() => Saved;
        public void Save(IReadOnlyList<MonitorSnapshot> snapshots) => Saved = snapshots.ToList();
        public void Clear() => Saved = new();
    }

    private readonly FakeBackend _backend = new();
    private readonly FakeStore _store = new();
    private readonly AppConfig _config = new() { ActivationSeconds = 5, RestoreSeconds = 10 };
    private readonly GameModeController _controller;

    public GameModeControllerTests()
    {
        _controller = new GameModeController(_backend, _store, () => _config);
    }

    private void EnterGame()
    {
        _controller.Tick(true, T0);
        _controller.Tick(true, T0.AddSeconds(5));
    }

    [Fact]
    public void Activates_only_after_activation_delay()
    {
        _controller.Tick(true, T0);
        _controller.Tick(true, T0.AddSeconds(4));
        Assert.Equal(0, _backend.Applied);

        _controller.Tick(true, T0.AddSeconds(5));
        Assert.Equal(GameModeState.Game, _controller.State);
        Assert.Equal(1, _backend.Applied);
    }

    [Fact]
    public void Brief_fullscreen_does_not_activate()
    {
        _controller.Tick(true, T0);
        _controller.Tick(false, T0.AddSeconds(2));
        _controller.Tick(true, T0.AddSeconds(4));
        _controller.Tick(true, T0.AddSeconds(6));

        Assert.Equal(GameModeState.Detecting, _controller.State);
        Assert.Equal(0, _backend.Applied);
    }

    [Fact]
    public void Short_alt_tab_keeps_preset()
    {
        EnterGame();

        _controller.Tick(false, T0.AddSeconds(10));
        _controller.Tick(true, T0.AddSeconds(15));
        _controller.Tick(false, T0.AddSeconds(30));

        Assert.Equal(GameModeState.Leaving, _controller.State);
        Assert.Equal(0, _backend.Restored);
    }

    [Fact]
    public void Restores_after_restore_delay()
    {
        EnterGame();

        _controller.Tick(false, T0.AddSeconds(10));
        _controller.Tick(false, T0.AddSeconds(20));

        Assert.Equal(GameModeState.Normal, _controller.State);
        Assert.Equal(1, _backend.Restored);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Restore_targets_saved_default_when_monitor_has_one()
    {
        _config.DefaultBrightness["sec"] = 55;
        EnterGame();

        _controller.RestoreNow();

        Assert.Equal(55, Assert.Single(_backend.LastRestored).TargetPercent);
    }

    [Fact]
    public void Restore_without_default_keeps_captured_value()
    {
        EnterGame();

        _controller.RestoreNow();

        var restored = Assert.Single(_backend.LastRestored);
        Assert.Null(restored.TargetPercent);
        Assert.Equal(80, restored.Brightness);
    }

    [Fact]
    public void Default_changed_during_game_is_used_on_restore()
    {
        _config.DefaultBrightness["sec"] = 55;
        EnterGame();

        _config.DefaultBrightness["sec"] = 30;
        _controller.RestoreNow();

        Assert.Equal(30, Assert.Single(_backend.LastRestored).TargetPercent);
    }

    [Fact]
    public void Recover_after_crash_uses_saved_default()
    {
        _config.DefaultBrightness["sec"] = 55;
        _store.Saved = new List<MonitorSnapshot> { new("sec", 80, false) };

        _controller.Recover();
        _controller.Tick(false, T0);

        Assert.Equal(55, Assert.Single(_backend.LastRestored).TargetPercent);
    }

    [Fact]
    public void Snapshot_is_persisted_before_preset_is_applied()
    {
        EnterGame();
        Assert.Single(_store.Saved);
    }

    [Fact]
    public void Recover_restores_snapshot_left_by_crash()
    {
        _store.Saved = new List<MonitorSnapshot> { new("sec", 80, false) };

        _controller.Recover();
        _controller.Tick(false, T0);

        Assert.Equal(1, _backend.Restored);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Failed_restore_is_retried_with_interval()
    {
        _backend.FailRestore = true;
        EnterGame();
        _controller.Tick(false, T0.AddSeconds(10));
        _controller.Tick(false, T0.AddSeconds(20));
        Assert.Equal(1, _backend.Restored);

        _controller.Tick(false, T0.AddSeconds(22));
        Assert.Equal(1, _backend.Restored);

        _backend.FailRestore = false;
        _controller.Tick(false, T0.AddSeconds(31));
        Assert.Equal(2, _backend.Restored);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Reentering_game_keeps_original_values_of_unrestored_monitors()
    {
        _backend.FailRestore = true;
        EnterGame();
        _controller.Tick(false, T0.AddSeconds(10));
        _controller.Tick(false, T0.AddSeconds(20));

        _controller.Tick(true, T0.AddSeconds(21));
        _controller.Tick(true, T0.AddSeconds(26));
        Assert.Equal(80, Assert.Single(_store.Saved).Brightness);
    }

    [Fact]
    public void Manual_mode_activates_immediately_and_holds_without_fullscreen()
    {
        _controller.ManualOn = true;
        _controller.Tick(false, T0);
        Assert.Equal(GameModeState.Game, _controller.State);

        _controller.Tick(false, T0.AddMinutes(5));
        Assert.Equal(GameModeState.Game, _controller.State);
        Assert.Equal(0, _backend.Restored);
    }

    [Fact]
    public void RestoreNow_restores_while_in_game()
    {
        EnterGame();
        _controller.RestoreNow();

        Assert.Equal(GameModeState.Normal, _controller.State);
        Assert.Equal(1, _backend.Restored);
    }
}
