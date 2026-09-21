using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Market;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// Phase 3 acceptance: trade the simulated market entirely through the terminal UI. Renders the panel
    /// into a 1920×1080 texture so layout matches a real screen and a screenshot can be reviewed.
    /// </summary>
    public class TradingTerminalTests
    {
        private PanelSettings _panelSettings;
        private RenderTexture _target;

        [TearDown]
        public void TearDown()
        {
            // PanelSettings is an asset: undo the in-memory change so it never leaks into the project.
            if (_panelSettings != null) _panelSettings.targetTexture = null;
            if (_target != null) _target.Release();
        }

        [UnityTest]
        public IEnumerator Developer_CanTradeThroughTheTerminal()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            var game = UnityEngine.Object.FindAnyObjectByType<GameBootstrap>();
            var terminal = UnityEngine.Object.FindAnyObjectByType<TradingTerminal>();
            Assert.NotNull(game, "GameBootstrap in scene");
            Assert.NotNull(terminal, "TradingTerminal in scene");

            _panelSettings = terminal.GetComponent<UIDocument>().panelSettings;
            _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _panelSettings.targetTexture = _target;
            yield return null;
            yield return null;

            VisualElement root = terminal.Root;
            Assert.NotNull(root, "terminal built");
            var submit = root.Q<Button>("submit-order");
            var status = root.Q<Label>("order-status");

            // Pre-market: a market order is refused with a clear reason.
            Assert.AreEqual(MarketSession.Premarket, game.Market.Session);
            Press(submit);
            StringAssert.Contains("regular session", status.text);

            // Fast-forward into the regular session, then freeze time so quotes are stable.
            game.SpeedMultiplier = 2000f;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (game.Clock.Now.TimeOfDay < new TimeSpan(9, 45, 0) && Time.realtimeSinceStartup < deadline)
                yield return null;
            game.SpeedMultiplier = 1f;
            game.IsPaused = true;
            Assert.AreEqual(MarketSession.Regular, game.Market.Session, "reached the regular session");

            // Select APEX from the watchlist.
            Click(root.Q("watch-APEX"));
            Assert.AreEqual("APEX", terminal.Context.SelectedTicker);
            Assert.AreEqual("APEX", root.Q<Label>("quote-ticker").text);

            // Market buy 100.
            game.Market.TryGetQuote("APEX", out Quote entry);
            Press(root.Q<Button>("side-buy"));
            Press(root.Q<Button>("type-market"));
            root.Q<TextField>("qty").value = "100";
            Press(submit);
            Assert.AreEqual(100, game.Account.Portfolio.QuantityOf("APEX"));
            StringAssert.StartsWith("Filled 100 APEX @ " + Fmt.Price(entry.Ask), status.text);

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
            SaveScreenshot("terminal-trading.png");

            // Cancel the resting order from the Orders tab.
            Press(root.Q<Button>("tab-orders"));
            terminal.RefreshAll();
            yield return null;
            yield return null;
            var cancel = root.Q<Button>("cancel-" + resting.Id);
            Assert.NotNull(cancel, "cancel button for the working order");
            Press(cancel);
            Assert.AreEqual(OrderStatus.Cancelled, resting.Status);
            Assert.AreEqual(game.Account.Cash, game.Account.BuyingPower, "reservation released");

            // Close the position from the Positions tab.
            Press(root.Q<Button>("tab-positions"));
            game.Market.TryGetQuote("APEX", out Quote exit);
            Press(root.Q<Button>("close-APEX"));
            Assert.AreEqual(0, game.Account.Portfolio.QuantityOf("APEX"));
            Assert.AreEqual(100 * (exit.Bid - entry.Ask), game.Account.RealizedPnL);
            StringAssert.StartsWith("Filled 100 APEX", status.text, "ticket shows the Close result");

            terminal.RefreshAll();
            yield return null;
            Assert.IsNull(root.Q("pos-APEX"), "position row removed");
            Assert.AreEqual(game.Account.Equity - game.Account.NetDeposits,
                game.Account.RealizedPnL + game.Account.UnrealizedPnL - game.Account.TotalCommissions);
        }

        /// <summary>Activates a Button the way keyboard/gamepad submit does.</summary>
        private static void Press(Button button)
        {
            Assert.NotNull(button, "button exists");
            Assert.IsTrue(button.enabledInHierarchy, $"{button.name} is enabled");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        private static void Click(VisualElement element)
        {
            Assert.NotNull(element, "element exists");
            using (var e = ClickEvent.GetPooled())
            {
                e.target = element;
                element.SendEvent(e);
            }
        }

        private void SaveScreenshot(string fileName)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, fileName), texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
    }
}
