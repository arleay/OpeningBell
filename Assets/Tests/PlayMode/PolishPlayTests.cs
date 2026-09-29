using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>Phase 8: onboarding mail, pause menu (save / new game), audio cues, frame-time check.</summary>
    public class PolishPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator OnboardingMail_ArrivesAndReads_InTheTerminal()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            var audio = Find<GameAudio>();
            yield return null;
            Assert.AreEqual(2, game.Inbox.Emails.Count, "welcome + landlord on day 1");

            game.SkipTo(game.Clock.Now.Date.AddHours(9).AddMinutes(31));
            yield return null;
            Assert.IsTrue(game.Inbox.Emails.Any(e => e.Subject.StartsWith("Premarket movers")), "premarket lesson");
            Assert.GreaterOrEqual(audio.Played("bell"), 1, "opening bell");
            Assert.GreaterOrEqual(audio.Played("mail"), 1);

            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            terminal.RefreshAll();
            yield return null;
            VisualElement root = terminal.Root;
            StringAssert.StartsWith("MAIL (", root.Q<Button>("app-mail").text, "unread badge");

            Press(root.Q<Button>("app-mail"));
            yield return null;
            yield return null;
            Label premarket = root.Q("mail-list").Query<Label>(className: "news-headline").ToList()
                .FirstOrDefault(l => l.text.StartsWith("Premarket movers"));
            Assert.NotNull(premarket, "listed in the inbox");
            Click(premarket.parent);
            StringAssert.Contains("Market orders prioritize execution", root.Q<Label>("mail-body").text);
            terminal.RefreshAll();
            yield return null;
            SaveTerminalScreenshot("mail-app.png");

            game.Orders.SubmitMarket("APEX", OrderSide.Buy, 1);
            yield return null;
            Assert.GreaterOrEqual(audio.Played("fill"), 1);
            Assert.IsTrue(game.Inbox.Emails.Any(e => e.Subject == "Your first fill"));
        }

        [UnityTest]
        public IEnumerator PauseMenu_PausesSaves_AndStartsANewGame()
        {
            UseSimulatedInput();
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var menu = Find<PauseMenu>();
            var player = Find<FirstPersonController>();
            yield return null;

            game.SkipTo(game.Clock.Now.Date.AddHours(10));
            game.Orders.SubmitMarket("APEX", OrderSide.Buy, 1);

            yield return TapKey(Key.Escape);
            Assert.IsTrue(menu.IsOpen, "Esc opens the menu while standing");
            Assert.IsTrue(game.IsPaused);
            Assert.IsFalse(player.ControlEnabled);
            DateTime frozen = game.Clock.Now;
            yield return null;
            yield return null;
            Assert.AreEqual(frozen, game.Clock.Now, "time stops while paused");

            Assert.IsFalse(SaveSystem.Exists("slot1"));
            Press(menu.Root.Q<Button>("menu-save"));
            Assert.IsTrue(SaveSystem.Exists("slot1"), "manual save");

            yield return TapKey(Key.Escape);
            Assert.IsFalse(menu.IsOpen, "Esc closes it");
            Assert.IsFalse(game.IsPaused);
            Assert.IsTrue(player.ControlEnabled);

            // New game: confirm, the save is deleted and a fresh game loads.
            menu.Open();
            Press(menu.Root.Q<Button>("menu-new"));
            Press(menu.Root.Q<Button>("menu-confirm-new"));
            yield return null;
            yield return null;
            var fresh = Find<GameBootstrap>();
            Assert.AreNotSame(game, fresh);
            Assert.AreEqual(0, fresh.Account.Portfolio.QuantityOf("APEX"), "fresh portfolio");
            Assert.AreEqual(10_000m, fresh.Account.Cash);
            Assert.AreEqual(6, fresh.Clock.Now.Hour, "back to the first morning");
        }

        [UnityTest]
        public IEnumerator FrameTime_WhileTrading_IsReasonable()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            OpenApp(terminal, "broker");
            game.SkipTo(game.Clock.Now.Date.AddHours(9).AddMinutes(45));
            game.SpeedMultiplier = 30f; // 300 game-seconds per real second: ~150 market ticks/s
            for (int i = 0; i < 30; i++) yield return null; // warm-up

            const int frames = 240;
            var watch = new Stopwatch();
            double total = 0, worst = 0;
            int gcBefore = GC.CollectionCount(0);
            long ticksBefore = game.Market.TickCount;
            for (int i = 0; i < frames; i++)
            {
                watch.Restart();
                yield return null;
                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                worst = Math.Max(worst, ms);
            }
            double average = total / frames;
            UnityEngine.Debug.Log($"PERF frame avg {average:F2} ms, worst {worst:F2} ms, {game.Market.TickCount - ticksBefore} market ticks, " +
                                  $"{GC.CollectionCount(0) - gcBefore} gen0 GCs over {frames} frames");
            TestContext.WriteLine($"frame avg {average:F2} ms, worst {worst:F2} ms, gen0 GCs {GC.CollectionCount(0) - gcBefore}");
            Assert.Less(average, 33.3, "under 30 fps on average would be a regression (batchmode, 30× speed)");
        }
    }
}
