using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Penumbra.Config;
using Penumbra.Game;
using Penumbra.Monitors;
using Penumbra.UI;
using static Penumbra.Monitors.NativeMethods;
using Timer = System.Windows.Forms.Timer;

namespace Penumbra;

public sealed class TrayApp : ApplicationContext
{
    private readonly GameModeController _controller;
    private readonly NotifyIcon _tray;
    private readonly Timer _timer = new() { Interval = 2000 };
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _manual = new("Ativar modo jogo agora") { CheckOnClick = true };
    private readonly ToolStripMenuItem _pause = new("Pausar detecção") { CheckOnClick = true };
    private readonly Icon _normalIcon = TrayIcons.Create();
    private readonly Icon _gameIcon = TrayIcons.Create(Color.LimeGreen);
    private readonly Icon _pausedIcon = TrayIcons.Create(Color.Gray);
    private readonly WinEventProc _foregroundChanged;
    private readonly IntPtr _hook;
    private AppConfig _config;
    private WidgetWindow? _widget;
    private Task? _tick;

    public TrayApp(bool showWidget)
    {
        var firstRun = !File.Exists(AppPaths.Config);
        _config = JsonFile.Read<AppConfig>(AppPaths.Config) ?? new AppConfig();

        _controller = new GameModeController(new DdcController(), new FileSnapshotStore(), () => _config);
        _controller.Recover();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_manual);
        menu.Items.Add(_pause);
        menu.Items.Add("Abrir widget", null, (_, _) => Widget().Open());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Quit());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Icon = _normalIcon, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Widget().Toggle();
        };

        _manual.CheckedChanged += (_, _) =>
        {
            _controller.ManualOn = _manual.Checked;
            Tick();
        };
        _pause.CheckedChanged += (_, _) => Tick();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // O hook fica em campo para o delegate não ser coletado enquanto estiver registrado.
        _foregroundChanged = (_, _, _, _, _, _, _) => Tick();
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT);

        UpdateTray();
        Tick();

        if (firstRun || showWidget)
            Widget().Open();
    }

    private void Tick()
    {
        // DDC/CI pode levar segundos, então uma leitura em andamento não acumula outra.
        if (_tick is { IsCompleted: false })
            return;

        var monitors = MonitorService.Enumerate();
        var main = MonitorService.ResolveMain(monitors, _config.MainMonitorId);
        var fullscreen = !_pause.Checked && main is not null
            && MonitorService.SamePanel(monitors, main).Any(m => FullscreenDetector.IsFullscreenOn(m.Bounds));

        var now = DateTime.Now;
        _tick = Task.Run(() => _controller.Tick(fullscreen, now));
        _tick.ContinueWith(task =>
        {
            if (task.Exception is not null)
                Log.Write($"Erro no ciclo de detecção: {task.Exception.GetBaseException()}");

            UpdateTray();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void UpdateTray()
    {
        var state = _controller.State;
        var label = state switch
        {
            GameModeState.Game => "Modo jogo ativo",
            GameModeState.Leaving => "Saindo do modo jogo",
            GameModeState.Detecting => "Jogo detectado",
            _ => _pause.Checked ? "Detecção pausada" : "Aguardando jogo"
        };

        _status.Text = label;
        _tray.Text = $"Penumbra: {label}";
        _widget?.SetStatus(label, state is GameModeState.Game or GameModeState.Leaving);
        _tray.Icon = state is GameModeState.Game or GameModeState.Leaving ? _gameIcon
            : _pause.Checked ? _pausedIcon
            : _normalIcon;
    }

    private WidgetWindow Widget()
    {
        if (_widget is not null)
            return _widget;

        _widget = new WidgetWindow(_config.Clone());
        _widget.ConfigChanged += config =>
        {
            JsonFile.Write(AppPaths.Config, config);
            _config = config;
        };

        // O laço de mensagens é do WinForms; sem isso a janela WPF não recebe digitação.
        ElementHost.EnableModelessKeyboardInterop(_widget);
        UpdateTray();
        return _widget;
    }

    private void Quit()
    {
        _timer.Stop();
        _widget?.Quit();
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);

        _tick?.Wait();
        _controller.RestoreNow();

        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
