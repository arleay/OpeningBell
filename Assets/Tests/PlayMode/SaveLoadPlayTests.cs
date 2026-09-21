using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>Quit-and-continue: a saved game comes back with the same money, positions, orders, time and place.</summary>
    public class SaveLoadPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Game_ContinuesFromSave_AfterRestart()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            DateTime date = game.Clock.Now.Date;

            game.SkipTo(date.AddHours(10));
            game.Orders.SubmitMarket("APEX", OrderSide.Buy, 100);
            game.Market.TryGetQuote("NVRA", out var nvra);
            Order resting = game.Orders.SubmitLimit("NVRA", OrderSide.Buy, 5, Market.PriceTick.RoundDown(nvra.Bid * 0.95m, 0.01m));
            player.PlaceAt(new Vector3(-1.2f, 0f, -1.5f), 135f);
            yield return null;

            var before = new
            {
                game.Clock.Now, game.Account.Cash, game.Account.Equity, game.Account.BuyingPower,
                Apex = game.Account.Portfolio.QuantityOf("APEX"), News = game.Market.News.Count, Day = game.Days.DayNumber,
                Position = player.transform.position,
            };
            game.Save();

            // "Restart": reload the scene; the new bootstrap continues from the slot in Awake.
            // Assert before its first Update so the clock hasn't moved on yet.
            yield return SceneManager.LoadSceneAsync("Main");
            var loaded = Find<GameBootstrap>();
            Assert.AreNotSame(game, loaded);
            var loadedPlayer = Find<FirstPersonController>();

            Assert.AreEqual(before.Now, loaded.Clock.Now);
            Assert.AreEqual(before.Cash, loaded.Account.Cash);
            Assert.AreEqual(before.Equity, loaded.Account.Equity);
            Assert.AreEqual(before.BuyingPower, loaded.Account.BuyingPower, "open order still reserves cash");
            Assert.AreEqual(before.Apex, loaded.Account.Portfolio.QuantityOf("APEX"));
            Assert.AreEqual(before.News, loaded.Market.News.Count);
            Assert.AreEqual(before.Day, loaded.Days.DayNumber);
            Assert.Less(Vector3.Distance(before.Position, loadedPlayer.transform.position), 0.01f, "player where they left");

            Order restored = loaded.Orders.OpenOrders.Single();
            Assert.AreEqual(resting.Id, restored.Id);
            Assert.AreEqual(resting.LimitPrice, restored.LimitPrice);

            yield return null; // terminal builds its UI in Start
            var terminal = Find<TradingTerminal>();
            terminal.RefreshAll();
            Assert.NotNull(terminal.Root.Q("pos-APEX"), "terminal shows the loaded position");
        }
    }
}
