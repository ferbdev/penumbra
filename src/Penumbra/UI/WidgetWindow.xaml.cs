using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Penumbra.Config;
using Penumbra.Monitors;

namespace Penumbra.UI;

public partial class WidgetWindow : Window
{
    private const int Step = 5;
    private const int MaxActivationSeconds = 120;
    private const int MaxRestoreSeconds = 300;
    private const double GlyphLongSide = 46;

    private readonly AppConfig _config;
    private readonly DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<string, Panel> _panels = new();
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private Action<string>? _nameConfirmed;
    private bool _quitting;

    public event Action<AppConfig>? ConfigChanged;

    public WidgetWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        SourceInitialized += (_, _) => GlassBackdrop.Apply(this);
        LocationChanged += (_, _) => RememberPosition();
        _commitTimer.Tick += (_, _) => Flush();
    }

    public void Open()
    {
        Rebuild();
        PlaceOnScreen();
        Show();
        Activate();
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Open();
    }

    public void Quit()
    {
        _quitting = true;
        Flush();
        Close();
    }

    public void SetStatus(string text, bool active)
    {
        StatusText.Text = text;
        StatusDot.Fill = active ? new SolidColorBrush(Color.FromRgb(0x5D, 0xE0, 0x8C)) : new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_quitting)
            return;

        e.Cancel = true;
        Hide();
    }

    private Preset? CurrentPreset => _config.ActivePreset;

    private MonitorInfo? Main => MonitorService.ResolveMain(_monitors, _config.MainMonitorId);

    private void Commit()
    {
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private void Flush()
    {
        _commitTimer.Stop();
        ConfigChanged?.Invoke(_config.Clone());
    }

    private void Rebuild()
    {
        _monitors = MonitorService.Enumerate();

        ActivationValue.Text = $"{_config.ActivationSeconds}s";
        RestoreValue.Text = $"{_config.RestoreSeconds}s";
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;

        BuildCards();
        BuildPresetChips();
        BuildRows();
    }

    private void BuildCards()
    {
        Cards.Children.Clear();
        _panels.Clear();

        foreach (var monitor in MonitorService.PhysicalMonitors(_monitors, Main?.Id))
        {
            var panel = new Panel(monitor.Id);
            panel.MonitorIds.AddRange(MonitorService.SamePanel(_monitors, monitor).Select(m => m.Id));

            var card = new MonitorCard(this, monitor, panel, monitor.Id == Main?.Id);
            panel.Cards.Add(card);
            _panels[monitor.Id] = panel;
            Cards.Children.Add(card.Root);
        }

        foreach (var panel in _panels.Values.ToList())
            _ = LoadBrightness(panel);
    }

    private async Task LoadBrightness(Panel panel)
    {
        var percent = await Task.Run(() => DdcController.ReadPercent(panel.MonitorId));
        panel.Percent = percent;
        panel.Sent = percent;
        panel.Refresh();
    }

    // Cliques rápidos só atualizam o alvo; DDC/CI recebe apenas o valor mais recente.
    private async void ChangeBrightness(Panel panel, int delta)
    {
        if (panel.Percent is not { } percent)
            return;

        panel.Percent = Math.Clamp(percent + delta * Step, 0, 100);
        panel.Failed = false;
        panel.Refresh();

        if (panel.Sending)
            return;

        panel.Sending = true;
        while (panel.Sent != panel.Percent)
        {
            var value = panel.Percent!.Value;
            var ok = await Task.Run(() => DdcController.SetPercent(panel.MonitorId, value));
            panel.Sent = value;

            if (ok)
            {
                foreach (var id in panel.MonitorIds)
                    _config.DefaultBrightness[id] = value;
                Commit();
            }
            else
            {
                Log.Write($"Falha ao ajustar o brilho de {panel.MonitorId}.");
                panel.Failed = true;
            }
        }
        panel.Sending = false;
        panel.Refresh();
    }

    private void SetMain(string id)
    {
        _config.MainMonitorId = id;
        Commit();
        Rebuild();
    }

    private void BuildPresetChips()
    {
        PresetChips.Children.Clear();

        foreach (var preset in _config.Presets)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Pill"),
                Content = preset.Name,
                GroupName = "Presets",
                IsChecked = preset == CurrentPreset
            };
            chip.Checked += (_, _) =>
            {
                _config.ActivePresetName = preset.Name;
                Commit();
                BuildRows();
            };
            PresetChips.Children.Add(chip);
        }

        PresetChips.Children.Add(ChipButton("\uE710", "Novo preset", OnAddPreset));
        PresetChips.Children.Add(ChipButton("\uE70F", "Renomear", OnRenamePreset));

        var remove = ChipButton("\uE74D", "Excluir", OnRemovePreset);
        remove.IsEnabled = _config.Presets.Count > 1;
        PresetChips.Children.Add(remove);
    }

    private Button ChipButton(string glyph, string tip, Action action)
    {
        var button = new Button { Style = (Style)FindResource("IconButton"), Content = glyph, ToolTip = tip };
        button.Click += (_, _) => action();
        return button;
    }

    private void OnAddPreset() => AskName("", name =>
    {
        _config.Presets.Add(new Preset { Name = name });
        _config.ActivePresetName = name;
    });

    private void OnRenamePreset()
    {
        if (CurrentPreset is not { } preset)
            return;

        AskName(preset.Name, name =>
        {
            preset.Name = name;
            _config.ActivePresetName = name;
        });
    }

    private void OnRemovePreset()
    {
        if (CurrentPreset is not { } preset || _config.Presets.Count == 1)
            return;

        _config.Presets.Remove(preset);
        _config.ActivePresetName = _config.Presets[0].Name;
        Commit();
        BuildPresetChips();
        BuildRows();
    }

    private void AskName(string initial, Action<string> confirmed)
    {
        _nameConfirmed = name =>
        {
            if (name != initial && _config.Presets.Any(p => p.Name == name))
                return;

            confirmed(name);
            Commit();
            BuildPresetChips();
            BuildRows();
        };

        PresetName.Text = initial;
        PresetName.Visibility = Visibility.Visible;
        PresetName.Focus();
        PresetName.SelectAll();
        Keyboard.Focus(PresetName);
    }

    private void CloseNameBox(bool confirm)
    {
        var action = _nameConfirmed;
        _nameConfirmed = null;
        PresetName.Visibility = Visibility.Collapsed;

        var name = PresetName.Text.Trim();
        if (confirm && name.Length > 0)
            action?.Invoke(name);
    }

    private void OnPresetNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            CloseNameBox(e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void OnPresetNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CloseNameBox(false);

    private void BuildRows()
    {
        Rows.Children.Clear();

        var preset = CurrentPreset;
        var main = Main;
        if (preset is null)
            return;

        foreach (var monitor in MonitorService.PhysicalMonitors(_monitors, main?.Id).Where(m => m.Id != main?.Id))
            Rows.Children.Add(PresetRow(preset, monitor));
    }

    private UIElement PresetRow(Preset preset, MonitorInfo monitor)
    {
        preset.Actions.TryGetValue(monitor.Id, out var action);

        var slider = new Slider
        {
            Style = (Style)FindResource("GlassSlider"),
            Value = action?.Brightness ?? 10,
            Margin = new Thickness(12, 0, 8, 0),
            IsEnabled = action is { PowerOff: false }
        };
        var value = new TextBlock
        {
            Text = $"{(int)slider.Value}%",
            Width = 34,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var keep = Segment("Manter", monitor, action is null);
        var dim = Segment("Brilho", monitor, action is { PowerOff: false });
        var off = Segment("Desligar", monitor, action is { PowerOff: true });

        void Store()
        {
            if (keep.IsChecked == true)
                preset.Actions.Remove(monitor.Id);
            else
                preset.Actions[monitor.Id] = new MonitorAction { PowerOff = off.IsChecked == true, Brightness = (int)slider.Value };

            var visible = dim.IsChecked == true;
            slider.IsEnabled = visible;
            slider.Opacity = visible ? 1 : 0.3;
            value.Opacity = visible ? 1 : 0.3;
            value.Text = $"{(int)slider.Value}%";
            Commit();
        }

        slider.Opacity = value.Opacity = dim.IsChecked == true ? 1 : 0.3;
        keep.Checked += (_, _) => Store();
        dim.Checked += (_, _) => Store();
        off.Checked += (_, _) => Store();
        slider.ValueChanged += (_, _) => Store();

        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        segments.Children.Add(keep);
        segments.Children.Add(dim);
        segments.Children.Add(off);

        var segmentFrame = new Border
        {
            Background = (Brush)FindResource("CardFill"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            Child = segments
        };

        var grid = RowGrid();
        grid.Children.Add(new TextBlock { Text = ShortName(monitor), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(segmentFrame, 1);
        grid.Children.Add(segmentFrame);
        Grid.SetColumn(slider, 2);
        grid.Children.Add(slider);
        Grid.SetColumn(value, 3);
        grid.Children.Add(value);
        return grid;
    }

    private RadioButton Segment(string text, MonitorInfo monitor, bool isChecked) => new()
    {
        Style = (Style)FindResource("Segment"),
        Content = text,
        GroupName = monitor.Id,
        IsChecked = isChecked
    };

    private static Grid RowGrid()
    {
        var grid = new Grid { Margin = new Thickness(2, 4, 0, 4), MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static string ShortName(MonitorInfo monitor) =>
        monitor.Input is null ? monitor.Name : $"{monitor.Name.Split(' ')[0]} {monitor.Input}";

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void RememberPosition()
    {
        if (!IsVisible || WindowState != WindowState.Normal)
            return;

        _config.WidgetLeft = Left;
        _config.WidgetTop = Top;
        Commit();
    }

    private void PlaceOnScreen()
    {
        var left = _config.WidgetLeft;
        var top = _config.WidgetTop;

        // Um monitor desligado ou desconectado não pode esconder o widget.
        var onScreen = left is not null && top is not null
            && left >= SystemParameters.VirtualScreenLeft
            && top >= SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80;

        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left!.Value;
            Top = top!.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private void OnIdentify(object sender, RoutedEventArgs e) => IdentifyOverlay.Show(_monitors);

    private void OnStartupToggled(object sender, RoutedEventArgs e) =>
        StartupRegistration.Set(StartupSwitch.IsChecked == true);

    private void OnActivationDown(object sender, RoutedEventArgs e) => SetActivation(_config.ActivationSeconds - 1);

    private void OnActivationUp(object sender, RoutedEventArgs e) => SetActivation(_config.ActivationSeconds + 1);

    private void OnRestoreDown(object sender, RoutedEventArgs e) => SetRestore(_config.RestoreSeconds - 1);

    private void OnRestoreUp(object sender, RoutedEventArgs e) => SetRestore(_config.RestoreSeconds + 1);

    private void SetActivation(int seconds)
    {
        _config.ActivationSeconds = Math.Clamp(seconds, 0, MaxActivationSeconds);
        ActivationValue.Text = $"{_config.ActivationSeconds}s";
        Commit();
    }

    private void SetRestore(int seconds)
    {
        _config.RestoreSeconds = Math.Clamp(seconds, 0, MaxRestoreSeconds);
        RestoreValue.Text = $"{_config.RestoreSeconds}s";
        Commit();
    }

    // Representa a tela física: duas entradas do mesmo monitor compartilham o mesmo brilho.
    private sealed class Panel
    {
        public Panel(string monitorId) => MonitorId = monitorId;

        public string MonitorId { get; }
        public List<string> MonitorIds { get; } = new();
        public List<MonitorCard> Cards { get; } = new();
        public int? Percent { get; set; }
        public int? Sent { get; set; }
        public bool Sending { get; set; }
        public bool Failed { get; set; }

        public void Refresh()
        {
            foreach (var card in Cards)
                card.Show(Percent, Failed);
        }
    }

    private sealed class MonitorCard
    {
        private readonly TextBlock _value = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "..." };
        private readonly Button _minus;
        private readonly Button _plus;
        private readonly Border _screen;

        public MonitorCard(WidgetWindow owner, MonitorInfo monitor, Panel panel, bool isMain)
        {
            _minus = StepButton(owner, "\uE738", $"Diminuir brilho {monitor.DisplayLabel}", () => owner.ChangeBrightness(panel, -1));
            _plus = StepButton(owner, "\uE710", $"Aumentar brilho {monitor.DisplayLabel}", () => owner.ChangeBrightness(panel, 1));

            var portrait = monitor.Bounds.Height > monitor.Bounds.Width;
            _screen = new Border
            {
                Width = portrait ? GlyphLongSide * 9 / 16 : GlyphLongSide,
                Height = portrait ? GlyphLongSide : GlyphLongSide * 9 / 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1.5),
                BorderBrush = (Brush)owner.FindResource(isMain ? "Accent" : "Muted"),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var star = new Button
            {
                Style = (Style)owner.FindResource("IconButton"),
                Content = isMain ? "\uE735" : "\uE734",
                ToolTip = isMain ? "Monitor principal" : "Definir como principal",
                Foreground = (Brush)owner.FindResource(isMain ? "Accent" : "Muted"),
                Width = 26,
                Height = 26,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -6, 0)
            };
            star.Click += (_, _) => owner.SetMain(monitor.Id);

            var glyph = new Grid { Height = GlyphLongSide + 4 };
            glyph.Children.Add(_screen);
            glyph.Children.Add(star);

            var name = new TextBlock
            {
                Text = monitor.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 1)
            };
            var detail = new TextBlock
            {
                Text = monitor.Input ?? "",
                FontSize = 11,
                Foreground = (Brush)owner.FindResource("Muted"),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var controls = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.Children.Add(_minus);
            Grid.SetColumn(_value, 1);
            controls.Children.Add(_value);
            Grid.SetColumn(_plus, 2);
            controls.Children.Add(_plus);

            var stack = new StackPanel();
            stack.Children.Add(glyph);
            stack.Children.Add(name);
            stack.Children.Add(detail);
            stack.Children.Add(controls);

            Root = new Border
            {
                Background = (Brush)owner.FindResource("CardFill"),
                BorderBrush = (Brush)owner.FindResource(isMain ? "AccentFill" : "Hairline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10),
                Margin = new Thickness(4),
                Child = stack
            };
        }

        public Border Root { get; }

        public void Show(int? percent, bool failed)
        {
            _minus.IsEnabled = _plus.IsEnabled = percent is not null;
            _value.Text = failed ? "Falhou" : percent is null ? "N/D" : $"{percent}%";
            _value.FontSize = failed || percent is null ? 13 : 20;
            _screen.Background = new SolidColorBrush(Color.FromArgb((byte)(40 + (percent ?? 0) * 2), 255, 255, 255));
        }

        private static Button StepButton(WidgetWindow owner, string glyph, string name, Action click)
        {
            var button = new Button { Style = (Style)owner.FindResource("FilledIconButton"), Content = glyph, IsEnabled = false };
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => click();
            return button;
        }
    }
}
