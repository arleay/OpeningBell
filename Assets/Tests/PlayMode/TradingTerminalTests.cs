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
    /// <summary>Phase 3 acceptance: trade the simulated market entirely through the terminal UI.</summary>
    public class TradingTerminalTests : SceneTestBase
    {
        /// <summary>Standing up, the desk monitors still show the market moving (the world texture keeps repainting).</summary>
        [UnityTest]
        public IEnumerator WorldTexture_KeepsMoving_WhileStanding()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            Assert.IsFalse(terminal.IsOnScreen, "standing: terminal renders into the monitor texture");
            game.SpeedMultiplier = 2000f;
            yield return WaitUntil(() => game.Market.Session == MarketSession.Regular, 60f, "regular session");
            game.SpeedMultiplier = 1f;
            for (int i = 0; i < 5; i++) yield return null;
            int before = Hash(terminal.WorldTexture);
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.AreNotEqual(before, Hash(terminal.WorldTexture), "the monitor image changed as the market moved");
        }

        private static int Hash(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var t = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            t.Apply();
            RenderTexture.active = previous;
            int h = 17;
            foreach (Color32 c in t.GetPixels32()) h = h * 31 + (c.r | c.g << 8 | c.b << 16);
            UnityEngine.Object.Destroy(t);
            return h;
        }

        [UnityTest]
        public IEnumerator Developer_CanTradeThroughTheTerminal()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            yield return null;
            yield return null;

            VisualElement root = terminal.Root;
            var submit = root.Q<Button>("submit-order");
            var status = root.Q<Label>("order-status");

            // The account bar keeps its height (it was squashed, captions clipped, when the panels wanted more room).
            VisualElement bar = root.Q(className: "account-bar");
            VisualElement stat = bar.Q(className: "stat");
            Assert.GreaterOrEqual(bar.layout.height, stat.layout.height + 10f, "account bar at full height");

            // Pre-market: a market order is refused with a clear reason.
            Assert.AreEqual(MarketSession.Premarket, game.Market.Session);
            Press(submit);
            StringAssert.Contains("regular session", status.text);

            // Fast-forward into the regular session, then freeze time so quotes are stable.
            game.SpeedMultiplier = 2000f;
            yield return WaitUntil(() => game.Clock.Now.TimeOfDay >= new TimeSpan(9, 45, 0), 60f, "9:45");
            game.SpeedMultiplier = 1f;
            game.IsPaused = true;
            Assert.AreEqual(MarketSession.Regular, game.Market.Session, "reached the regular session");

            // The 8:15 onboarding headline is in the feed; clicking it selects APEX.
            terminal.RefreshAll();
            yield return null;
            Label headline = root.Q("news-list").Query<Label>(className: "news-headline").ToList()
                .FirstOrDefault(l => l.text.Contains("distribution agreement"));
            Assert.NotNull(headline, "APEX headline shown in the news feed");
            Click(headline.parent);
            Assert.AreEqual("APEX", terminal.Context.SelectedTicker);
            Assert.AreEqual("APEX", root.Q<Label>("quote-ticker").text);
            StringAssert.Contains("distribution agreement", root.Q<Label>("quote-news").text);

            // Market buy 2 contracts.
            game.Market.TryGetQuote("APEX", out Quote entry);
            Press(root.Q<Button>("side-buy"));
            Press(root.Q<Button>("type-market"));
            root.Q<TextField>("qty").value = "2";
            Press(submit);
            Assert.AreEqual(2, game.Account.Portfolio.QuantityOf("APEX"));
            StringAssert.StartsWith("Filled 2 APEX @ " + Fmt.Price(entry.Ask), status.text);

            // Resting limit buy well below the market.
            Press(root.Q<Button>("type-limit"));
            root.Q<TextField>("limit-price").value = Fmt.Price(PriceTick.RoundDown(entry.Bid * 0.9m, 0.01m));
            Press(submit);
            Order resting = game.Orders.OpenOrders.Single();
            StringAssert.StartsWith("Working", status.text);

            terminal.RefreshAll();
            yield return null;
            Assert.NotNull(root.Q("pos-APEX"), "position row shown");

            // Let a little market time pass so the chart moves, then capture the screen for review.
            game.IsPaused = false;
            game.SpeedMultiplier = 30f;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            game.IsPaused = true;
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("terminal-trading.png");

            // Cancel the resting order from the Orders tab.
            Press(root.Q<Button>("tab-orders"));
            terminal.RefreshAll();
            yield return null;
            yield return null;
            var cancel = root.Q<Button>("cancel-" + resting.Id);
            Assert.NotNull(cancel, "cancel button for the working order");
            Press(cancel);
            Assert.AreEqual(OrderStatus.Cancelled, resting.Status);
            Assert.AreEqual(0m, game.Account.ReservedCash, "reservation released");

            // Close the position from the Positions tab.
            Press(root.Q<Button>("tab-positions"));
            game.Market.TryGetQuote("APEX", out Quote exit);
            Press(root.Q<Button>("close-APEX"));
            Assert.AreEqual(0, game.Account.Portfolio.QuantityOf("APEX"));
            Assert.AreEqual(2 * (exit.Bid - entry.Ask) * game.Account.Contract("APEX").PointValue, game.Account.RealizedPnL);
            StringAssert.StartsWith("Filled 2 APEX", status.text, "ticket shows the Close result");

            terminal.RefreshAll();
            yield return null;
            Assert.IsNull(root.Q("pos-APEX"), "position row removed");
            Assert.AreEqual(game.Account.Equity - game.Account.NetDeposits,
                game.Account.RealizedPnL + game.Account.UnrealizedPnL - game.Account.TotalCommissions);
        }
    }
}
