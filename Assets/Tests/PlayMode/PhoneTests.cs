using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The phone: Tab takes it out and puts it away, PennyBridge trades on the real account (whole contracts only),
    /// fills and headlines notify, calls connect, and every app is photographed (TestResults/phone-*.png).
    /// </summary>
    public class PhoneTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Phone_OpensWithTab_TradesAndNotifies()
        {
            UseSimulatedInput();
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            var hud = Find<InteractionHud>();
            Phone phone = city.Phone;
            Assert.IsNotNull(phone);
            game.SkipTo(game.Clock.Now.Date.AddHours(10.5)); // regular session: market orders fill
            for (int i = 0; i < 5; i++) yield return null;

            // Tab takes it out: the mouse is freed, but the player is still in the world.
            yield return TapKey(Key.Tab);
            yield return WaitUntil(() => phone.IsOpen, 2f, "the phone to open");
            Assert.IsFalse(player.ControlEnabled, "control is off while the phone is up");
            Assert.IsTrue(player.Browsing);
            Assert.IsTrue(city.Minimap.Visible, "the minimap stays up");
            for (int i = 0; i < 20; i++) yield return null; // let it slide up
            phone.Show(PhoneAppId.Home); // (Tab opens a showing banner's story; the morning's headlines may be up)
            yield return null;
            yield return CaptureWithHud(player, hud, "phone-home.png");

            // PennyBridge: a stock page, then buy through the sheet. Letters never get into the quantity.
            phone.Show(PhoneAppId.PennyBridge);
            yield return null;
            yield return CaptureWithHud(player, hud, "phone-pennybridge.png");
            var broker = (PennyBridgeApp)phone.App(PhoneAppId.PennyBridge);
            string ticker = game.Market.Securities[0].Ticker;
            broker.ShowStock(ticker);
            broker.OpenSheet(OrderSide.Buy);
            broker.QuantityField.value = "2abc";
            Assert.AreEqual("2", broker.QuantityField.value, "letters are filtered out");
            yield return null;
            broker.Submit();
            yield return null;
            StringAssert.StartsWith("Filled 2", broker.StatusText);
            Assert.AreEqual(2, game.Account.Portfolio.Find(ticker).Quantity, "the fill lands on the real account");
            yield return CaptureWithHud(player, hud, "phone-trade.png");
            Assert.IsTrue(phone.BannerTexts.Any(t => t.Contains("Bought 2 " + ticker)), "the fill pops up as a notification");

            // Messages has the fill.
            phone.Show(PhoneAppId.Messages);
            yield return null;
            yield return CaptureWithHud(player, hud, "phone-messages.png");

            // Maps.
            phone.Show(PhoneAppId.Maps);
            yield return null;
            yield return CaptureWithHud(player, hud, "phone-maps.png");

            // A call to PennyBridge support connects and speaks.
            phone.Show(PhoneAppId.Calls);
            var calls = (CallsApp)phone.App(PhoneAppId.Calls);
            calls.Dial("5550100200");
            Assert.IsTrue(calls.InCall);
            yield return WaitUntil(() => hud.SubtitleText.Contains("PennyBridge"), 5f, "support to answer");
            StringAssert.Contains("market is open", hud.SubtitleText);
            yield return CaptureWithHud(player, hud, "phone-call.png");

            // Tab puts it away; control comes back.
            yield return TapKey(Key.Tab);
            Assert.IsFalse(phone.IsOpen);
            Assert.IsTrue(player.ControlEnabled);
            Assert.IsFalse(player.Browsing);

            // Headlines notify with the phone away: run the clock until something publishes.
            int before = game.Market.News.Count;
            for (int step = 0; step < 24 && game.Market.News.Count == before; step++)
            {
                game.SkipTo(game.Clock.Now.AddMinutes(20));
                yield return null;
            }
            Assert.Greater(game.Market.News.Count, before, "news was published");
            NewsItem latest = game.Market.News[game.Market.News.Count - 1];
            Assert.IsTrue(phone.BannerTexts.Contains(latest.Headline), "the headline pops up bottom right");
            Assert.Greater(phone.App(PhoneAppId.News).Unread, 0, "and badges News");
            for (int i = 0; i < 20; i++) yield return null;
            yield return CaptureWithHud(player, hud, "phone-notification.png");

            // Tab while a banner is up opens that story.
            yield return TapKey(Key.Tab);
            Assert.IsTrue(phone.IsOpen);
            Assert.AreEqual(PhoneAppId.News, phone.CurrentApp);
            Assert.AreSame(latest, ((NewsApp)phone.App(PhoneAppId.News)).Reading);
            for (int i = 0; i < 20; i++) yield return null;
            yield return CaptureWithHud(player, hud, "phone-news.png");
            phone.Close();
        }
    }
}
