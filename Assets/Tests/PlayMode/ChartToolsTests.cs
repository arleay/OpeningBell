using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The chart as a trading tool: indicators and panes, drawings (saved per symbol), the position line with live
    /// P&L, a bracket's TP/SL lines on real orders, auto markings, and themes. Screenshots: terminal-chart*.png.
    /// </summary>
    public class ChartToolsTests : SceneTestBase
    {
        private const string PrefsKey = "OpeningBell.ChartPreferences.v1";
        private string _savedPrefs;

        [SetUp]
        public void KeepPlayerPreferences() => _savedPrefs = PlayerPrefs.GetString(PrefsKey, null);

        [TearDown]
        public void RestorePlayerPreferences()
        {
            if (_savedPrefs == null) PlayerPrefs.DeleteKey(PrefsKey);
            else PlayerPrefs.SetString(PrefsKey, _savedPrefs);
        }

        [UnityTest]
        public IEnumerator Chart_ShowsIndicatorsDrawingsPositionAndBrackets()
        {
            PlayerPrefs.DeleteKey(PrefsKey); // start from the default layout
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            game.SkipTo(game.Clock.Now.Date.AddHours(11.2)); // a morning of candles to chart
            game.IsPaused = true;
            terminal.Context.Select("APEX");
            terminal.RefreshAll();
            yield return null;

            ChartPanel panel = terminal.Chart;
            ChartPreferences prefs = panel.Preferences;
            prefs.Indicators.Add(IndicatorConfig.Default(IndicatorKind.Ema));
            var ema20 = IndicatorConfig.Default(IndicatorKind.Ema);
            ema20.Period = 20;
            ema20.Color = new Color(1f, 0.6f, 0f);
            prefs.Indicators.Add(ema20);
            prefs.Indicators.Add(IndicatorConfig.Default(IndicatorKind.Bollinger));
            prefs.Indicators.Add(IndicatorConfig.Default(IndicatorKind.Rsi));
            prefs.Indicators.Add(IndicatorConfig.Default(IndicatorKind.Macd));
            prefs.ShowFvg = prefs.ShowStructure = prefs.ShowLiquidity = prefs.ShowEqualHighsLows = prefs.ShowSweeps = true;
            prefs.Notify();
            Assert.IsTrue(PlayerPrefs.GetString(PrefsKey, "").Contains("\"Period\":20"), "the layout persists between sessions");

            // A position with a bracket on it.
            Order buy = game.Orders.SubmitMarket("APEX", OrderSide.Buy, 2);
            Assert.AreEqual(OrderStatus.Filled, buy.Status, buy.StatusReason);
            decimal last = game.Market.Securities.First(s => s.Ticker == "APEX").Last;
            var legs = game.Orders.SubmitBracket("APEX", 2, PriceTick.RoundNearest(last * 1.02m), PriceTick.RoundNearest(last * 0.985m));
            Assert.IsTrue(legs.All(o => o.Status == OrderStatus.Working), string.Join("; ", legs.Select(o => o.StatusReason)));

            // Drawings, anchored in time and price.
            CandleSeries m1 = game.Market.Securities.First(s => s.Ticker == "APEX").Candles.Get(Timeframe.Minute1);
            Candle a = m1[m1.Count - 80], b = m1[m1.Count - 20];
            game.Drawings.Add(new ChartDrawing { Ticker = "APEX", Kind = DrawingKind.Rectangle, T1 = a.Start.Ticks, T2 = b.Start.Ticks,
                P1 = (double)a.High, P2 = (double)a.Low, Color = "#5A9CF5", ExtendRight = true });
            game.Drawings.Add(new ChartDrawing { Ticker = "APEX", Kind = DrawingKind.HorizontalLine, T1 = a.Start.Ticks, P1 = (double)b.High, Color = "#E8B63C" });
            game.Drawings.Add(new ChartDrawing { Ticker = "APEX", Kind = DrawingKind.Trendline, T1 = a.Start.Ticks, T2 = b.Start.Ticks,
                P1 = (double)a.Low, P2 = (double)b.Low, Color = "#4FC3F7" });
            game.Drawings.Add(new ChartDrawing { Ticker = "NVRA", Kind = DrawingKind.HorizontalLine, T1 = a.Start.Ticks, P1 = 100, Color = "#FFFFFF" });

            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("terminal-chart.png");

            VisualElement chart = terminal.Root.Q(className: "chart-view");
            string texts = string.Join(" | ", chart.Query<Label>().ToList().Where(l => l.resolvedStyle.display != DisplayStyle.None).Select(l => l.text));
            TestContext.WriteLine(texts);
            StringAssert.Contains("AVG ", texts, "the position line is labelled");
            StringAssert.Contains("TP 2", texts, "the take-profit line");
            StringAssert.Contains("SL 2", texts, "the stop-loss line");
            StringAssert.Contains("RSI 14", texts, "the RSI pane");
            StringAssert.Contains("MACD 12 26 9", texts, "the MACD pane");
            StringAssert.Contains("EMA 20", texts, "the EMA legend");

            // With the mouse: draw a zone with the rectangle tool, then drag the stop-loss line up.
            ChartView view = panel.View;
            view.Tool = DrawingKind.Rectangle;
            Vector2 from = view.PointOf(m1[m1.Count - 50].Start, (double)last * 1.004);
            Vector2 to = view.PointOf(m1[m1.Count - 30].Start, (double)last * 0.998);
            Pointer(view, EventType.MouseDown, from);
            Pointer(view, EventType.MouseDrag, to);
            Pointer(view, EventType.MouseUp, to);
            Assert.AreEqual(4, game.Drawings.For("APEX").Count(), "the rectangle tool drew a zone");
            Assert.AreEqual(DrawingKind.Rectangle, view.SelectedDrawing.Kind, "and selected it");
            Assert.IsNull(view.Tool, "back to the cursor");

            Order stop = legs.Single(o => o.Type == OrderType.Stop);
            decimal oldStop = stop.StopPrice;
            decimal newStop = PriceTick.RoundNearest(oldStop + (last - oldStop) / 2);
            Vector2 grab = view.PointOf(m1[m1.Count - 10].Start, (double)oldStop);
            Vector2 drop = view.PointOf(m1[m1.Count - 10].Start, (double)newStop);
            Pointer(view, EventType.MouseDown, grab);
            Pointer(view, EventType.MouseDrag, drop);
            Pointer(view, EventType.MouseUp, drop);
            Assert.AreEqual((double)newStop, (double)stop.StopPrice, 0.02, "dragging the SL line moved the real stop order");
            Assert.AreEqual(OrderStatus.Working, stop.Status);

            // The "×" at the end of the take-profit's label cancels it (its stop-loss stays).
            Order tpLeg = legs.Single(o => o.Type == OrderType.Limit);
            terminal.RefreshAll();
            yield return null;
            Vector2? cancel = view.CancelPointOf(tpLeg.Id);
            Assert.IsTrue(cancel.HasValue, "the TP line has an ×");
            Pointer(view, EventType.MouseDown, cancel.Value);
            Pointer(view, EventType.MouseUp, cancel.Value);
            Assert.AreEqual(OrderStatus.Cancelled, tpLeg.Status, "clicking × cancelled the take-profit");
            Assert.AreEqual(OrderStatus.Working, stop.Status, "the stop-loss is still working");

            // Drawings are saved with the game, per symbol.
            game.Save();
            Assert.IsTrue(SaveSystem.TryRead("slot1", out SaveGame save, out _));
            Assert.IsTrue(save.HasDrawings);
            Assert.AreEqual(4, save.Drawings.Items.Count(d => d.Ticker == "APEX"));
            Assert.AreEqual(1, save.Drawings.Items.Count(d => d.Ticker == "NVRA"));

            // Themes and custom colours.
            prefs.ThemeName = "Light";
            prefs.UseCustom = false;
            prefs.Notify();
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("terminal-chart-light.png");
            Assert.AreEqual(ChartTheme.Presets.First(t => t.Name == "Light").Background, chart.resolvedStyle.backgroundColor);

            panel.SetThemeTarget(nameof(ChartTheme.Up));
            panel.SetThemeColor(new Color(0.3f, 0.76f, 0.97f));
            Assert.IsTrue(prefs.UseCustom, "a custom colour makes a custom theme from the current one");
            Assert.AreEqual(new Color(0.3f, 0.76f, 0.97f), prefs.Theme.Up);
            Assert.AreEqual(ChartTheme.Presets.First(t => t.Name == "Light").Background, prefs.Theme.Background, "the rest is kept");
        }

        /// <summary>Sends a mouse event to the chart at a point in its local coordinates.</summary>
        private static void Pointer(VisualElement target, EventType type, Vector2 local)
        {
            var e = new Event { type = type, mousePosition = target.LocalToWorld(local), button = 0, clickCount = 1 };
            EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(e),
                EventType.MouseUp => PointerUpEvent.GetPooled(e),
                _ => PointerMoveEvent.GetPooled(e),
            };
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }
    }
}
