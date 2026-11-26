using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Making the island your own, by hand: pull the folded pill's edge to make it wider or bigger, drag the
/// tabs and the Home buttons into your order (a click hides or returns one), choose what the idle pill shows.
/// </summary>
public partial class MainWindow
{
    // ================================================================ size: pull the pill's edges

    private bool _editMode;
    private Canvas? _editLayer;
    private Rectangle? _editOutline;
    private Thumb? _gripRight, _gripBottom;
    private Border? _editBar;
    private TextBlock? _editInfo;
    private readonly DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Point _gripStart;
    private int _gripStartExtra, _gripStartScale;

    private void EditSize_Click(object sender, RoutedEventArgs e) => EnterEditMode();

    private void EnterEditMode()
    {
        if (_editMode) return;
        if (_hidden) ShowIsland();
        _pinnedOpen = false;
        if (_mode != Mode.Compact) SetMode(Mode.Compact);
        _editMode = true;
        FollowEdge(animate: true);   // on the taskbar it rises a little: room for the grips under it

        if (_editLayer == null)
        {
            _editLayer = new Canvas();
            Root.Children.Add(_editLayer);

            _editOutline = new Rectangle { StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false, Opacity = 0.9 };
            _editOutline.SetResourceReference(Shape.StrokeProperty, "Accent");
            _editLayer.Children.Add(_editOutline);

            Thumb Grip(Cursor cursor)
            {
                var t = new Thumb { Cursor = cursor, Template = GripTemplate() };
                t.DragStarted += (_, _) =>
                {
                    _gripStart = CursorDip();
                    _gripStartExtra = _settings.PillExtra;
                    _gripStartScale = _settings.IslandScale;
                };
                t.DragCompleted += (_, _) => _settings.Save();
                _editLayer!.Children.Add(t);
                return t;
            }
            _gripRight = Grip(Cursors.SizeWE);
            _gripRight.DragDelta += (_, _) =>
            {
                // the pill stays centred, so it grows on both sides: twice the cursor's way keeps the edge under it
                double dx = Vertical ? CursorDip().Y - _gripStart.Y : CursorDip().X - _gripStart.X;   // upright on a side: along its length
                int extra = (int)Math.Round(Math.Clamp(_gripStartExtra + 2 * dx / UiScale, PillExtraMin, PillExtraMax));
                if (extra == _settings.PillExtra) return;
                _settings.PillExtra = extra;
                MorphTo(PillSize(CompactWidth));
                PlaceEditGrips();
            };
            _gripBottom = Grip(Cursors.SizeNS);
            _gripBottom.DragDelta += (_, _) =>
            {
                double dy = Vertical ? (CursorDip().X - _gripStart.X) * (_edge == Edge.Left ? 1 : -1) : CursorDip().Y - _gripStart.Y;
                int scale = (int)Math.Round(Math.Clamp(_gripStartScale + dy / CompactHeight * 100, 80, 130));
                if (scale == _settings.IslandScale) return;
                _settings.IslandScale = scale;
                ApplyIslandScale();
                ScaleSlider.Value = scale;
                PlaceEditGrips();
            };

            _editInfo = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
            _editInfo.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var reset = new Button { Style = (Style)FindResource("GhostPill"), Content = L.T("Сбросить"), Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 6, 0) };
            reset.Click += (_, _) => ResetPillSize();
            var done = new Button { Style = (Style)FindResource("AccentPill"), Content = L.T("Готово"), Padding = new Thickness(13, 5, 13, 5) };
            done.Click += (_, _) => ExitEditMode();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_editInfo); row.Children.Add(reset); row.Children.Add(done);
            _editBar = new Border { CornerRadius = new CornerRadius(19), Padding = new Thickness(12, 6, 6, 6), Child = row, BorderThickness = new Thickness(1) };
            _editBar.SetResourceReference(Border.BackgroundProperty, "IslandBg");
            _editBar.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
            _editLayer.Children.Add(_editBar);

            _editTimer.Tick += (_, _) => Measure("edit grips", PlaceEditGrips);
        }
        _editLayer.Visibility = Visibility.Visible;
        PreviewKeyDown += EditMode_KeyDown;
        _editTimer.Start();
        PlaceEditGrips();
        Activate();
    }

    private const int PillExtraMin = -50, PillExtraMax = 320;

    private static ControlTemplate GripTemplate()
    {
        // a small rounded bar on a wider, invisible hit area
        var hit = new FrameworkElementFactory(typeof(Grid));
        hit.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var bar = new FrameworkElementFactory(typeof(Border));
        bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        bar.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        hit.AppendChild(bar);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = hit };
    }

    private void EditMode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter) { ExitEditMode(); e.Handled = true; }
    }

    private void ExitEditMode()
    {
        if (!_editMode) return;
        _editMode = false;
        FollowEdge(animate: true);
        _editTimer.Stop();
        PreviewKeyDown -= EditMode_KeyDown;
        if (_editLayer != null) _editLayer.Visibility = Visibility.Collapsed;
        _settings.Save();
        ShrinkWindowToPill();
    }

    private void ResetPillSize()
    {
        _settings.PillExtra = 0;
        _settings.IslandScale = 100;
        _settings.Save();
        ApplyIslandScale();
        ScaleSlider.Value = 100;
        if (_mode == Mode.Compact) MorphTo(PillSize(CompactWidth));
        if (_editMode) PlaceEditGrips();
    }

    /// <summary>Outline, the two grips and the bar follow the pill while it changes.</summary>
    private void PlaceEditGrips()
    {
        if (!_editMode || _editLayer == null) return;
        if (_mode != Mode.Compact || _hidden) { ExitEditMode(); return; }
        _shrinkTimer.Stop();
        var tl = Island.TranslatePoint(new Point(0, 0), Root);
        double w = Island.ActualWidth * UiScale, h = Island.ActualHeight * UiScale;

        _editOutline!.Width = w + 8; _editOutline.Height = h + 8;
        _editOutline.RadiusX = _editOutline.RadiusY = (Math.Min(w, h) + 8) / 2;
        Canvas.SetLeft(_editOutline, tl.X - 4); Canvas.SetTop(_editOutline, tl.Y - 4);

        if (Vertical)
        {
            // upright on a side: the length grip below it, the size grip on its free side
            _gripRight!.Width = Math.Max(22, w - 6); _gripRight.Height = 14; _gripRight.Cursor = Cursors.SizeNS;
            Canvas.SetLeft(_gripRight, tl.X + (w - _gripRight.Width) / 2); Canvas.SetTop(_gripRight, tl.Y + h + 5);
            _gripBottom!.Width = 14; _gripBottom.Height = Math.Clamp(h * 0.36, 44, 120); _gripBottom.Cursor = Cursors.SizeWE;
            Canvas.SetLeft(_gripBottom, _edge == Edge.Left ? tl.X + w + 5 : tl.X - 19); Canvas.SetTop(_gripBottom, tl.Y + (h - _gripBottom.Height) / 2);
        }
        else
        {
            _gripRight!.Width = 14; _gripRight.Height = Math.Max(22, h - 6); _gripRight.Cursor = Cursors.SizeWE;
            Canvas.SetLeft(_gripRight, tl.X + w + 5); Canvas.SetTop(_gripRight, tl.Y + (h - _gripRight.Height) / 2);
            _gripBottom!.Height = 14; _gripBottom.Width = Math.Clamp(w * 0.36, 44, 120); _gripBottom.Cursor = Cursors.SizeNS;
            Canvas.SetLeft(_gripBottom, tl.X + (w - _gripBottom.Width) / 2); Canvas.SetTop(_gripBottom, tl.Y + h + 5);
        }

        _editInfo!.Text = L.F("Тяните за край · размер {0}%, ширина {1}", _settings.IslandScale, (_settings.PillExtra >= 0 ? "+" : "") + _settings.PillExtra);
        _editBar!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double by = tl.Y + h + 28;
        double bx = tl.X + (w - _editBar.DesiredSize.Width) / 2;
        if (_edge == Edge.Left) bx = Math.Max(bx, tl.X);   // nothing is drawn past the edge it is attached to
        if (_edge == Edge.Right) bx = Math.Min(bx, tl.X + w - _editBar.DesiredSize.Width);
        Canvas.SetLeft(_editBar, Math.Max(0, bx)); Canvas.SetTop(_editBar, by);
        if (by + _editBar.DesiredSize.Height + 6 > Root.ActualHeight) GrowWindow(by + _editBar.DesiredSize.Height + 10);
    }

    // ================================================================ tabs: order and which ones are shown

    private static readonly string[] DefaultTabOrder =
        ["TabHome", "TabMusic", "TabWeather", "TabChat", "TabTimer", "TabClip", "TabNotes", "TabCalendar", "TabSystem", "TabWidgets"];

    private RadioButton[] AllPageTabs => [TabHome, TabMusic, TabWeather, TabChat, TabTimer, TabClip, TabNotes, TabCalendar, TabSystem, TabWidgets];

    private RadioButton[]? _pageTabs;

    /// <summary>The page tabs the way the user arranged them (hidden ones left out).</summary>
    private RadioButton[] PageTabs => _pageTabs ??= OrderedTabs().Where(t => !_settings.HiddenTabs.Contains(t.Name) || t == TabHome).ToArray();

    private List<RadioButton> OrderedTabs()
    {
        var all = AllPageTabs;
        var order = _settings.TabOrder.Where(n => all.Any(t => t.Name == n)).Distinct().ToList();
        order.AddRange(DefaultTabOrder.Where(n => !order.Contains(n)));
        return order.Select(n => all.First(t => t.Name == n)).ToList();
    }

    private void ApplyTabLayout()
    {
        _pageTabs = null;
        var ordered = OrderedTabs();
        foreach (var t in ordered) TabStrip.Children.Remove(t);
        foreach (var t in ordered)
        {
            TabStrip.Children.Add(t);
            t.Visibility = PageTabs.Contains(t) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (PageTabs.All(t => t.IsChecked != true) && TabSettings.IsChecked != true) TabHome.IsChecked = true;
        DotsRow.Children.Clear();
        UpdatePageDots();
        // new dots come up visible: put them (and the tabs shown again) in the state the strip is in
        ShowTabIcons(_iconsShown || _settings.TabIcons == "always", animate: false);
    }


    // ================================================================ a strip of chips you drag into order

    private sealed record StripItem(string Id, string Label, bool On, bool Locked = false);

    /// <summary>Chips in a row: drag one to move it, click it to switch it off or back on.</summary>
    private WrapPanel ReorderStrip(IEnumerable<StripItem> items, Action<List<(string Id, bool On)>> changed)
    {
        var panel = new WrapPanel();
        Border? held = null;
        Point pressAt = default;
        bool moved = false;

        void Paint(Border chip)
        {
            var (_, on, _) = ((string, bool, bool))chip.Tag;
            chip.Opacity = on ? 1 : 0.42;
            if (on) chip.SetResourceReference(Border.BackgroundProperty, "ChipBg"); else chip.Background = Brushes.Transparent;
        }

        void Report() => changed(panel.Children.OfType<Border>().Select(c => { var (id, on, _) = ((string, bool, bool))c.Tag; return (id, on); }).ToList());

        foreach (var item in items)
        {
            var text = new TextBlock { Text = item.Label, FontSize = 12.5 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), Child = text,
                                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Tag = (item.Id, item.On, item.Locked) };
            chip.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
            Paint(chip);
            chip.MouseLeftButtonDown += (_, e) =>
            {
                held = chip; moved = false; pressAt = e.GetPosition(panel);
                chip.CaptureMouse();
                e.Handled = true;
            };
            chip.MouseMove += (_, e) =>
            {
                if (held != chip) return;
                var p = e.GetPosition(panel);
                if (!moved && Math.Abs(p.X - pressAt.X) < 5 && Math.Abs(p.Y - pressAt.Y) < 5) return;
                moved = true;
                chip.Cursor = Cursors.SizeAll;
                // the chip under the cursor gives up its place
                foreach (Border other in panel.Children)
                {
                    if (other == chip) continue;
                    var o = other.TranslatePoint(new Point(0, 0), panel);
                    if (p.X < o.X || p.X > o.X + other.ActualWidth || p.Y < o.Y - 3 || p.Y > o.Y + other.ActualHeight + 3) continue;
                    int to = panel.Children.IndexOf(other);
                    panel.Children.Remove(chip);
                    panel.Children.Insert(to, chip);
                    break;
                }
            };
            chip.MouseLeftButtonUp += (_, e) =>
            {
                if (held != chip) return;
                held = null;
                chip.ReleaseMouseCapture();
                chip.Cursor = Cursors.Hand;
                var (id, on, locked) = ((string, bool, bool))chip.Tag;
                if (!moved && !locked) { chip.Tag = (id, !on, locked); Paint(chip); }
                Report();
                e.Handled = true;
            };
            panel.Children.Add(chip);
        }
        return panel;
    }

    // ================================================================ Settings → Layout

    private bool _layoutBuilt;

    private void BuildLayoutPage()
    {
        if (_layoutBuilt) return;
        _layoutBuilt = true;
        var host = LayoutHost;

        Border Tile(string caption, params UIElement[] rows)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Text = caption, Margin = new Thickness(0, 0, 0, 10) });
            foreach (var r in rows) stack.Children.Add(r);
            return new Border { Style = (Style)FindResource("Tile"), Margin = new Thickness(0, 0, 0, 10), Child = stack };
        }
        TextBlock Hint(string text) => new() { Style = (Style)FindResource("Muted"), Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };

        UIElement Choice(string label, string group, (string Id, string Name)[] options, Func<string> get, Action<string> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var name = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Width = 92 };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            var chips = new WrapPanel();
            foreach (var (id, text) in options)
            {
                var chip = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, Content = text, IsChecked = get() == id, Margin = new Thickness(0, 0, 4, 4) };
                chip.Checked += (_, _) => { set(id); _settings.Save(); OnTick(); UpdatePillRight(); };
                chips.Children.Add(chip);
            }
            row.Children.Add(chips);
            return row;
        }

        // the pill
        var size = new Button { Style = (Style)FindResource("AccentPill"), Content = L.T("Изменить размер прямо на острове"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        size.Click += (_, _) => EnterEditMode();
        var reset = new Button { Style = (Style)FindResource("GhostPill"), Content = L.T("Сбросить размер"), Padding = new Thickness(12, 7, 12, 7) };
        reset.Click += (_, _) => ResetPillSize();
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(size); buttons.Children.Add(reset);
        var right = new List<(string, string)> { ("weather", L.T("Погода")) };
        if (BatteryPercent() >= 0) right.Add(("battery", L.T("Батарея")));
        right.Add(("none", L.T("Ничего")));
        host.Children.Add(Tile(L.T("ПИЛЮЛЯ"),
            Hint(L.T("Свёрнутый остров можно тянуть за край: вправо — шире, вниз — крупнее. То же — правым кликом по острову.")),
            buttons,
            Choice(L.T("В центре"), "PillClock", [("time", L.T("Время")), ("timedate", L.T("Время и дата")), ("date", L.T("Дата"))], () => _settings.PillClock, v => _settings.PillClock = v),
            Choice(L.T("Справа"), "PillRight", right.ToArray(), () => _settings.PillRight, v => _settings.PillRight = v)));

        // look
        UIElement SliderRow(string label, int min, int max, int step, Func<int> get, Action<int> set, Func<int, string> show)
        {
            var slider = new Views.Scrubber { Label = label, Format = v => show((int)Math.Round(v)), Minimum = min, Maximum = max, Step = step, Value = Math.Clamp(get(), min, max), Margin = new Thickness(0, 0, 0, 8) };
            slider.ValueChanged += (_, e) => { set((int)Math.Round(e.NewValue)); _settings.SaveSoon(); ApplyLook(); };
            return slider;
        }
        CheckBox SwitchRow(string text, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Style = (Style)FindResource("Switch"), Content = text, IsChecked = get() };
            c.Click += (_, _) => { set(c.IsChecked == true); _settings.Save(); ApplyLook(); OnTick(); };
            return c;
        }
        var fonts = new (string, string)[] { ("", L.T("Системный")), ("Bahnschrift", "Bahnschrift"), ("Georgia", "Georgia"), ("Cascadia Code", "Cascadia"), ("Comic Sans MS", "Comic Sans") };
        host.Children.Add(Tile(L.T("ФОРМА И ВИД"),
            SliderRow(L.T("Скругление углов"), 0, 100, 5, () => _settings.Roundness, v => _settings.Roundness = v, v => v + "%"),
            SliderRow(L.T("Прозрачность в покое"), 30, 100, 5, () => _settings.IdleOpacity, v => _settings.IdleOpacity = v, v => v >= 100 ? L.T("нет") : v + "%"),
            SwitchRow(L.T("Тень под островом"), () => _settings.ShowShadow, v => _settings.ShowShadow = v),
            SwitchRow(L.T("Кнопка меню ☰ в заголовке острова"), () => _settings.MenuButton, v => _settings.MenuButton = v),
            Choice(L.T("Шрифт"), "IslandFont", fonts, () => _settings.Font, v => { _settings.Font = v; ApplyLook(); })));

        host.Children.Add(Tile(L.T("ЧАСЫ, РАСКРЫТИЕ, ЗВУК"),
            SwitchRow(L.T("12-часовой формат времени"), () => _settings.Clock12, v => _settings.Clock12 = v),
            SwitchRow(L.T("Показывать секунды"), () => _settings.ClockSeconds, v => _settings.ClockSeconds = v),
            SliderRow(L.T("Задержка раскрытия при наведении"), 0, 800, 50, () => _settings.HoverDelay, v => _settings.HoverDelay = v, v => L.F("{0} мс", v)),
            SwitchRow(L.T("Звук таймера и напоминаний"), () => _settings.Sounds, v => _settings.Sounds = v)));

        // desktop widgets
        var widgets = new Button { Style = (Style)FindResource("AccentPill"), Content = L.T("Изменить виджеты"), Padding = new Thickness(14, 7, 14, 7), HorizontalAlignment = HorizontalAlignment.Left };
        widgets.Click += (_, _) => EditWidgets(true);
        host.Children.Add(Tile(L.T("ВИДЖЕТЫ НА РАБОЧЕМ СТОЛЕ"),
            Hint(L.T("Стеклянные карточки на рабочем столе: музыка, погода, часы, календарь, таймер, заметки, загрузка процессора, видеокарты, памяти, диска и сети. Добавляйте их и двигайте в режиме изменения.")),
            widgets));

        // tabs
        var tabs = OrderedTabs().Select(t => new StripItem(t.Name, (string)t.Content, !_settings.HiddenTabs.Contains(t.Name) || t == TabHome, t == TabHome));
        host.Children.Add(Tile(L.T("ВКЛАДКИ"),
            Hint(L.T("Перетащите, чтобы поменять порядок. Нажмите, чтобы скрыть вкладку или вернуть её.")),
            ReorderStrip(tabs, state =>
            {
                _settings.TabOrder = state.Select(s => s.Id).ToList();
                _settings.HiddenTabs = state.Where(s => !s.On).Select(s => s.Id).ToList();
                _settings.Save();
                ApplyTabLayout();
            })));

        // the buttons on Home
        var chosen = _settings.Actions.Where(id => AllActions.Any(a => a.Id == id)).ToList();
        var actions = chosen.Concat(AllActions.Select(a => a.Id).Where(id => !chosen.Contains(id)))
            .Select(id => new StripItem(id, AllActions.First(a => a.Id == id).Label, chosen.Contains(id)));
        host.Children.Add(Tile(L.T("КНОПКИ НА ГЛАВНОЙ"),
            Hint(L.T("Перетащите, чтобы поменять порядок. Нажмите, чтобы убрать кнопку или вернуть её.")),
            ReorderStrip(actions, state =>
            {
                _settings.Actions = state.Where(s => s.On).Select(s => s.Id).ToList();
                _settings.Save();
                BuildHome();
            })));
    }

    // ================================================================ shape, shadow, font, clock, hover

    private string ClockFormat => (_settings.Clock12 ? "h:mm" : "HH:mm") + (_settings.ClockSeconds ? ":ss" : "");

    /// <summary>Applies the look choices of Settings → Layout (safe to call any time).</summary>
    private void ApplyLook()
    {
        IslandShadowPath.Visibility = _settings.ShowShadow ? Visibility.Visible : Visibility.Hidden;
        MenuButton.Visibility = _settings.MenuButton ? Visibility.Visible : Visibility.Collapsed;
        _expandDelay.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.HoverDelay, 0, 800) + 1);
        var font = new FontFamily((_settings.Font.Length > 0 ? _settings.Font + ", " : "") + "Segoe UI Variable Text, Segoe UI");
        if (!Equals(FontFamily.Source, font.Source))
        {
            FontFamily = font;
            if (_mode == Mode.Compact) Dispatcher.BeginInvoke(() => { if (_mode == Mode.Compact) MorphTo(PillSize(CompactWidth)); }, DispatcherPriority.Loaded);
            else Remeasure();
        }
        UpdateShape(false);     // the corners
        CaptureFrost();         // …and the glass cut to them
        _idleDim = null;        // the idle transparency is worked out again on the next tick
    }

    private bool? _idleDim;

    /// <summary>
    /// Every 250 ms: the folded island turns a little see-through while the cursor is away from it (if the
    /// user chose so), and comes back solid as the cursor comes near, or when anything happens on it.
    /// </summary>
    private void UpdateIdleDim()
    {
        if (_hidden || _fullscreenHidden) { _idleDim = null; return; }
        bool dim = _settings.IdleOpacity < 100 && _mode == Mode.Compact && !Island.IsMouseOver && !_editMode && !_dragging
                   && _reach.Target < 0.02 && !_recording.IsRecording && _demoMedia == null;
        if (dim == _idleDim) return;
        _idleDim = dim;
        Fade(Island, dim ? Math.Clamp(_settings.IdleOpacity, 30, 100) / 100.0 : 1, dim ? 600 : 160, dim ? 900 : 0);
    }

    // ================================================================ what the idle pill shows

    private TextBlock? _pillBattery;

    /// <summary>The clock in the middle of the idle pill: the time, the time and the date, or the date.</summary>
    private string PillClockText(string time)
    {
        var now = DateTime.Now;
        var culture = Loc.Culture;
        return _settings.PillClock switch
        {
            "timedate" => $"{time} · {now.ToString("ddd d", culture)}",
            "date" => now.ToString("ddd, d MMM", culture),
            _ => time,
        };
    }

    /// <summary>Battery charge, 0…100, or -1 on a computer without one.</summary>
    private static int BatteryPercent()
    {
        try
        {
            var p = System.Windows.Forms.SystemInformation.PowerStatus;
            if (p.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || p.BatteryLifePercent > 1) return -1;
            return (int)Math.Round(p.BatteryLifePercent * 100);
        }
        catch { return -1; }
    }

    /// <summary>The right side of the idle pill: weather, battery or nothing (music's bars take the place while it plays).</summary>
    private void UpdatePillRight()
    {
        bool playing = _lastMedia?.IsPlaying == true;
        int battery = _settings.PillRight == "battery" ? (_demoMedia != null ? 87 : BatteryPercent()) : -1;
        CompactWeather.Visibility = !playing && _settings.PillRight == "weather" ? Visibility.Visible : Visibility.Collapsed;
        if (battery < 0) { if (_pillBattery != null) _pillBattery.Visibility = Visibility.Collapsed; return; }
        if (_pillBattery == null)
        {
            _pillBattery = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _pillBattery.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            ((Panel)CompactWeather.Parent).Children.Add(_pillBattery);
        }
        _pillBattery.Text = battery + "%";
        _pillBattery.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The ☰ button: the same menu as a right click on the island, under the button; its lines cross while it is open.</summary>
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (IslandMenu.IsOpen) { IslandMenu.IsOpen = false; return; }
        IslandMenu.PlacementTarget = MenuButton;
        IslandMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MenuToggle.Open = true;
        RoutedEventHandler? closed = null;
        closed = (_, _) =>
        {
            IslandMenu.Closed -= closed;
            MenuToggle.Open = false;
            IslandMenu.ClearValue(System.Windows.Controls.ContextMenu.PlacementTargetProperty);   // the right click places it again
            IslandMenu.ClearValue(System.Windows.Controls.ContextMenu.PlacementProperty);
        };
        IslandMenu.Closed += closed;
        IslandMenu.IsOpen = true;
    }
}
