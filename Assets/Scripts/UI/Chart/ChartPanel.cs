using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.UI
{
    /// <summary>
    /// The chart with its toolbar: timeframes, drawing tools, and the Indicators, Overlays and Theme menus. A bar
    /// appears over the chart while a drawing is selected (colour, width, extend, delete). Also feeds the chart the
    /// position, working orders (draggable TP/SL) and the stock's market memory.
    /// </summary>
    public sealed class ChartPanel : TerminalPanel
    {
        private static readonly (Timeframe tf, string label)[] Timeframes =
        {
            (Timeframe.Minute1, "1m"), (Timeframe.Minute5, "5m"), (Timeframe.Minute15, "15m"), (Timeframe.Hour1, "1h"), (Timeframe.Day1, "1D"),
        };

        private static readonly (DrawingKind? Kind, string Label, string Name)[] Tools =
        {
            (null, "↖", "tool-cursor"), (DrawingKind.HorizontalLine, "—", "tool-hline"), (DrawingKind.Trendline, "╱", "tool-trend"),
            (DrawingKind.Ray, "↗", "tool-ray"), (DrawingKind.Rectangle, "▭", "tool-rect"), (DrawingKind.Fibonacci, "Fib", "tool-fib"),
            (DrawingKind.VerticalLine, "│", "tool-vline"),
        };

        private readonly ChartView _view;
        private readonly Label _title;
        private readonly Button[] _timeframeButtons = new Button[Timeframes.Length];
        private readonly Button[] _toolButtons = new Button[Tools.Length];
        private readonly VisualElement _indicatorsMenu, _overlaysMenu, _themeMenu, _selectBar;
        private readonly ChartPreferences _prefs;
        private readonly ChartTrading _trading = new ChartTrading();
        private Timeframe _timeframe = Timeframe.Minute1;
        private string _ticker;
        private long _knownTick = -1, _knownTrading;

        private static decimal LinePrice(Order o) => o.IsStop && !o.Triggered ? o.StopPrice : o.LimitPrice;
        private string _themeTarget = nameof(ChartTheme.Up);

        public ChartView View => _view;
        public ChartPreferences Preferences => _prefs;

        public ChartPanel(TerminalContext context) : base(context, "chart")
        {
            _prefs = ChartPreferences.Load();

            var toolbar = Ui.Box("chart-toolbar", Root);
            _title = Ui.Label("panel-title chart-title", toolbar);
            for (int i = 0; i < Timeframes.Length; i++)
            {
                Timeframe tf = Timeframes[i].tf;
                _timeframeButtons[i] = Ui.Button(Timeframes[i].label, () => SetTimeframe(tf), "tf-btn", toolbar, "tf-" + Timeframes[i].label);
            }
            Ui.Box("chart-sep", toolbar);
            for (int i = 0; i < Tools.Length; i++)
            {
                DrawingKind? kind = Tools[i].Kind;
                _toolButtons[i] = Ui.Button(Tools[i].Label, () => _view.Tool = kind, "tool-btn", toolbar, Tools[i].Name);
            }
            Ui.Box("chart-sep", toolbar);
            Ui.Button("Indicators", () => Toggle(_indicatorsMenu), "tf-btn", toolbar, "menu-indicators");
            Ui.Button("Overlays", () => Toggle(_overlaysMenu), "tf-btn", toolbar, "menu-overlays");
            Ui.Button("Theme", () => Toggle(_themeMenu), "tf-btn", toolbar, "menu-theme");
            Ui.Box("spacer", toolbar);
            Ui.Label("legend muted", toolbar, "wheel: zoom · drag: pan · dbl-click: live · Del: remove drawing");

            _view = new ChartView();
            Root.Add(_view);
            _view.SetPreferences(_prefs);
            _view.SetDrawings(context.Game.Drawings);
            _view.SetTrading(_trading);
            _view.ToolChanged += _ => RefreshToolButtons();
            _view.SelectionChanged += _ => BuildSelectBar();

            _trading.Modify = (id, price) => Context.Orders.ModifyPrice(id, price);
            _trading.Cancel = id => Context.Orders.Cancel(id);
            _trading.CreateBracket = (tp, sl) =>
            {
                long qty = Context.Orders.AvailableToClose(_ticker);
                if (qty <= 0) return "All your contracts are already covered by orders.";
                foreach (Order o in Context.Orders.SubmitBracket(_ticker, qty, tp, sl))
                    if (o.Status == OrderStatus.Rejected) return o.StatusReason;
                return null;
            };

            _indicatorsMenu = Menu("menu-indicators-panel", 360);
            _overlaysMenu = Menu("menu-overlays-panel", 440);
            _themeMenu = Menu("menu-theme-panel", 520);
            _selectBar = Ui.Box("chart-select-bar", Root);
            _selectBar.name = "chart-select-bar";
            Ui.Show(_selectBar, false);

            _prefs.Changed += () =>
            {
                _view.SetPreferences(_prefs);
                BuildMenus();
            };
            BuildMenus();
            RefreshToolButtons();
            context.SelectionChanged += Refresh;
        }

        public override void Refresh()
        {
            SecurityRuntimeState s = Context.Selected;
            if (s.Ticker != _ticker)
            {
                _ticker = s.Ticker;
                LoadSeries();
            }

            Position position = Context.Account.Portfolio.Find(s.Ticker);
            _trading.Quantity = position != null && position.IsOpen ? position.Quantity : 0;
            _trading.AveragePrice = position != null && position.IsOpen ? position.AveragePrice : 0m;
            _trading.Last = s.Last;
            _trading.PointValue = Context.Account.Contract(s.Ticker).PointValue;
            _trading.Orders.Clear();
            foreach (Order o in Context.Orders.OpenOrders)
                if (o.Ticker == s.Ticker && o.Type != OrderType.Market) _trading.Orders.Add(o);
            _view.SetSecurity(s);
            _view.SetOverlays(Context.Orders.Fills, Context.Market.News);

            // Redraw on new market data, and when the position or working orders change (even while paused).
            long tradingState = _trading.Quantity * 31 + (long)(_trading.AveragePrice * 100) + Context.Orders.Fills.Count * 7919;
            foreach (Order o in _trading.Orders) tradingState = tradingState * 17 + o.Id * 131 + (long)(LinePrice(o) * 100) + o.RemainingQuantity;
            if (Context.Market.TickCount != _knownTick || tradingState != _knownTrading)
            {
                _knownTick = Context.Market.TickCount;
                _knownTrading = tradingState;
                _view.Refresh();
            }
        }

        private void SetTimeframe(Timeframe timeframe)
        {
            _timeframe = timeframe;
            LoadSeries();
        }

        private void LoadSeries()
        {
            SecurityRuntimeState s = Context.Selected;
            _view.SetSeries(s.Candles.Get(_timeframe), _timeframe, s.Ticker);
            for (int i = 0; i < Timeframes.Length; i++)
                _timeframeButtons[i].EnableInClassList("active", Timeframes[i].tf == _timeframe);
            Ui.SetText(_title, $"{s.Ticker} · {Timeframes[(int)_timeframe].label}");
        }

        private void RefreshToolButtons()
        {
            for (int i = 0; i < Tools.Length; i++)
                _toolButtons[i].EnableInClassList("active", Tools[i].Kind == _view.Tool);
        }

        // ------------------------------------------------------------------ menus

        private VisualElement Menu(string name, float left)
        {
            var menu = Ui.Box("chart-menu", Root);
            menu.name = name;
            menu.style.left = left;
            Ui.Show(menu, false);
            return menu;
        }

        private void Toggle(VisualElement menu)
        {
            bool open = menu.resolvedStyle.display == DisplayStyle.None;
            foreach (VisualElement m in new[] { _indicatorsMenu, _overlaysMenu, _themeMenu }) Ui.Show(m, false);
            Ui.Show(menu, open);
            if (open) menu.BringToFront();
        }

        private void BuildMenus()
        {
            BuildIndicatorsMenu();
            BuildOverlaysMenu();
            BuildThemeMenu();
        }

        private void BuildIndicatorsMenu()
        {
            VisualElement m = _indicatorsMenu;
            m.Clear();
            Ui.Label("chart-menu-title", m, "ON THE CHART");
            for (int i = 0; i < _prefs.Indicators.Count; i++)
            {
                IndicatorConfig c = _prefs.Indicators[i];
                var row = Ui.Box("chart-menu-row", m);
                row.name = $"indicator-{i}";
                Ui.Button(c.Visible ? "●" : "○", () => { c.Visible = !c.Visible; _prefs.Notify(); }, "tool-btn", row, "visible");
                Ui.Label("chart-menu-name", row, c.Title);
                if (c.Kind != IndicatorKind.Vwap && c.Kind != IndicatorKind.Volume)
                {
                    NumberField(row, c.Period, v => c.Period = Math.Clamp(v, 1, 400), "period");
                    if (c.Kind == IndicatorKind.Macd)
                    {
                        NumberField(row, c.Period2, v => c.Period2 = Math.Clamp(v, 2, 400), "period2");
                        NumberField(row, c.Period3, v => c.Period3 = Math.Clamp(v, 1, 400), "period3");
                    }
                    if (c.Kind == IndicatorKind.Bollinger)
                        NumberField(row, c.Period2, v => c.Period2 = Math.Clamp(v, 5, 50), "width10");
                }
                if (c.Kind != IndicatorKind.Volume)
                {
                    var swatch = Ui.Box("chart-swatch", row);
                    swatch.style.backgroundColor = c.Color;
                    swatch.RegisterCallback<ClickEvent>(_ =>
                    {
                        c.Color = Next(c.Color);
                        _prefs.Notify();
                    });
                    Ui.Button($"{c.Width:0.#}px", () => { c.Width = c.Width >= 3 ? 1 : c.Width + 1; _prefs.Notify(); }, "tool-btn", row);
                }
                int index = i;
                Ui.Button("✕", () => { _prefs.Indicators.RemoveAt(index); _prefs.Notify(); }, "tool-btn", row, "remove");
            }
            Ui.Label("chart-menu-title", m, "ADD");
            var add = Ui.Box("chart-menu-row", m);
            add.style.flexWrap = Wrap.Wrap;
            foreach (IndicatorKind kind in Enum.GetValues(typeof(IndicatorKind)))
            {
                IndicatorKind k = kind;
                Ui.Button(IndicatorConfig.Default(k).Title.Split(' ')[0], () =>
                {
                    _prefs.Indicators.Add(IndicatorConfig.Default(k));
                    _prefs.Notify();
                }, "tool-btn", add, "add-" + k);
            }
            Ui.Label("muted", m, "BB width is ×10 (20 = 2.0 σ). Click a colour to change it.");
        }

        private void NumberField(VisualElement row, int value, Action<int> set, string name)
        {
            var field = new TextField { value = value.ToString(CultureInfo.InvariantCulture), name = name };
            field.AddToClassList("ticket-field");
            field.AddToClassList("chart-field");
            TicketInput.Restrict(field);
            field.RegisterCallback<FocusOutEvent>(_ => Commit());
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Commit();
            }, TrickleDown.TrickleDown);
            row.Add(field);

            void Commit()
            {
                if (int.TryParse(field.value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v != value)
                {
                    set(v);
                    _prefs.Notify();
                }
            }
        }

        private void BuildOverlaysMenu()
        {
            VisualElement m = _overlaysMenu;
            m.Clear();
            Ui.Label("chart-menu-title", m, "AUTO MARKINGS (describe the past, don't predict)");
            Toggle(m, "Fair value gaps", () => _prefs.ShowFvg, v => _prefs.ShowFvg = v, "overlay-fvg");
            Toggle(m, "BOS / CHoCH", () => _prefs.ShowStructure, v => _prefs.ShowStructure = v, "overlay-structure");
            Toggle(m, "Swept highs / lows", () => _prefs.ShowSweeps, v => _prefs.ShowSweeps = v, "overlay-sweeps");
            Toggle(m, "Equal highs / lows", () => _prefs.ShowEqualHighsLows, v => _prefs.ShowEqualHighsLows = v, "overlay-equal");
            Toggle(m, "Liquidity (PMH/PML, PDH/PDL, ORH/ORL)", () => _prefs.ShowLiquidity, v => _prefs.ShowLiquidity = v, "overlay-liquidity");
            if (Debug.isDebugBuild)
                Toggle(m, "Developer: hidden market (F10)", () => _prefs.ShowDebug, v => _prefs.ShowDebug = v, "overlay-debug");
        }

        private void Toggle(VisualElement m, string label, Func<bool> get, Action<bool> set, string name)
        {
            var row = Ui.Box("chart-menu-row", m);
            Ui.Button(get() ? "●" : "○", () => { set(!get()); _prefs.Notify(); }, "tool-btn", row, name);
            Ui.Label("chart-menu-name", row, label);
        }

        private static readonly (string Field, string Label)[] ThemeParts =
        {
            (nameof(ChartTheme.Up), "Bull candle"), (nameof(ChartTheme.Down), "Bear candle"), (nameof(ChartTheme.UpWick), "Bull wick"),
            (nameof(ChartTheme.DownWick), "Bear wick"), (nameof(ChartTheme.Background), "Background"), (nameof(ChartTheme.Grid), "Grid"),
            (nameof(ChartTheme.UpVolume), "Volume up"), (nameof(ChartTheme.DownVolume), "Volume down"), (nameof(ChartTheme.Crosshair), "Crosshair"),
            (nameof(ChartTheme.Vwap), "VWAP"),
        };

        private void BuildThemeMenu()
        {
            VisualElement m = _themeMenu;
            m.Clear();
            Ui.Label("chart-menu-title", m, "THEMES");
            var presets = Ui.Box("chart-menu-row", m);
            presets.style.flexWrap = Wrap.Wrap;
            foreach (ChartTheme t in ChartTheme.Presets)
            {
                ChartTheme theme = t;
                Button b = Ui.Button(t.Name, () =>
                {
                    _prefs.ThemeName = theme.Name;
                    _prefs.UseCustom = false;
                    _prefs.Notify();
                }, "tool-btn", presets, "theme-" + t.Name);
                b.EnableInClassList("active", !_prefs.UseCustom && _prefs.ThemeName == t.Name);
            }

            Ui.Label("chart-menu-title", m, "CUSTOM COLOURS (pick a part, then a colour)");
            ChartTheme current = _prefs.Theme;
            foreach (var (field, label) in ThemeParts)
            {
                var row = Ui.Box("chart-menu-row", m);
                string f = field;
                Button pick = Ui.Button(label, () => { _themeTarget = f; BuildThemeMenu(); }, "tool-btn", row, "part-" + f);
                pick.EnableInClassList("active", _themeTarget == f);
                var swatch = Ui.Box("chart-swatch", row);
                swatch.style.backgroundColor = (Color)typeof(ChartTheme).GetField(f).GetValue(current);
            }
            var grid = Ui.Box("chart-swatches", m);
            for (int i = 0; i < ChartTheme.Swatches.Length; i++)
            {
                Color color = ChartTheme.Swatches[i];
                var sw = Ui.Box("chart-swatch", grid);
                sw.name = "swatch-" + i;
                sw.style.backgroundColor = color;
                sw.RegisterCallback<ClickEvent>(_ => SetThemeColor(color));
            }
        }

        /// <summary>Custom colours start from whatever theme is showing, then the picked part changes.</summary>
        public void SetThemeColor(Color color)
        {
            if (!_prefs.UseCustom)
            {
                _prefs.Custom = _prefs.Theme.Copy();
                _prefs.Custom.Name = "Custom";
                _prefs.UseCustom = true;
            }
            System.Reflection.FieldInfo field = typeof(ChartTheme).GetField(_themeTarget);
            // Volume keeps its transparency so bars stay behind candles.
            if (_themeTarget == nameof(ChartTheme.UpVolume) || _themeTarget == nameof(ChartTheme.DownVolume)) color.a = 0.32f;
            if (_themeTarget == nameof(ChartTheme.Grid)) color.a = 0.12f;
            field.SetValue(_prefs.Custom, color);
            _prefs.Notify();
        }

        /// <summary>Picks which theme part the next swatch click recolours (Up, Down, Background...).</summary>
        public void SetThemeTarget(string field) => _themeTarget = field;

        private static Color Next(Color c)
        {
            Color[] s = ChartTheme.Swatches;
            int best = 0;
            for (int i = 0; i < s.Length; i++)
                if (Distance(s[i], c) < Distance(s[best], c)) best = i;
            return s[(best + 1) % s.Length];
        }

        private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        // ------------------------------------------------------------------ selected drawing

        private void BuildSelectBar()
        {
            ChartDrawing d = _view.SelectedDrawing;
            _selectBar.Clear();
            Ui.Show(_selectBar, d != null);
            if (d == null) return;
            _selectBar.BringToFront();
            Ui.Label("chart-menu-name", _selectBar, d.Kind.ToString());
            for (int i = 0; i < ChartTheme.Swatches.Length; i += 2)
            {
                Color color = ChartTheme.Swatches[i];
                var sw = Ui.Box("chart-swatch", _selectBar);
                sw.style.backgroundColor = color;
                sw.RegisterCallback<ClickEvent>(_ =>
                {
                    d.Color = "#" + ColorUtility.ToHtmlStringRGB(color);
                    Context.Game.Drawings.Touch();
                });
            }
            Ui.Button($"{d.Width:0.#}px", () =>
            {
                d.Width = d.Width >= 3 ? 1 : d.Width + 0.5f;
                Context.Game.Drawings.Touch();
                BuildSelectBar();
            }, "tool-btn", _selectBar, "draw-width");
            if (d.Kind == DrawingKind.Rectangle || d.Kind == DrawingKind.Trendline || d.Kind == DrawingKind.Fibonacci)
            {
                Button extend = Ui.Button("Extend →", () =>
                {
                    d.ExtendRight = !d.ExtendRight;
                    Context.Game.Drawings.Touch();
                    BuildSelectBar();
                }, "tool-btn", _selectBar, "draw-extend");
                extend.EnableInClassList("active", d.ExtendRight);
            }
            Ui.Button("Delete", _view.DeleteSelected, "tool-btn", _selectBar, "draw-delete");
        }
    }
}
