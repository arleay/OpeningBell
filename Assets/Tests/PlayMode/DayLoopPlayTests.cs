using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>Phase 6 acceptance: complete several consecutive trading days (trade → close → summary → sleep → wake).</summary>
    public class DayLoopPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Player_CompletesThreeConsecutiveTradingDays()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            var sleep = Find<SleepController>();
            var bed = Find<BedInteractable>();
            var player = Find<FirstPersonController>();
            var hud = Find<InteractionHud>();

            Assert.AreEqual(1, game.Days.DayNumber);
            Assert.IsFalse(sleep.CanSleep, "no sleeping at 6 AM");
            StringAssert.Contains("after 4 PM", bed.Prompt);

            for (int day = 1; day <= 3; day++)
            {
                DateTime date = game.Clock.Now.Date;
                Assert.AreEqual(day, game.Days.DayNumber);

                // Trade during the session.
                game.SkipTo(date.AddHours(10));
                Assert.AreEqual(OrderStatus.Filled, game.Orders.SubmitMarket("APEX", OrderSide.Buy, 1).Status);
                game.SkipTo(date.AddHours(11));
                Assert.AreEqual(OrderStatus.Filled, game.Orders.SubmitMarket("APEX", OrderSide.Sell, 1).Status);
                // Regular close → session summary appears in the terminal.
                game.SkipTo(date.AddHours(16).AddMinutes(30));
                terminal.RefreshAll();
                yield return null;
                VisualElement summary = terminal.Root.Q("day-summary");
                Assert.AreEqual(DisplayStyle.Flex, summary.resolvedStyle.display, "summary shown at the close");
                if (day == 1)
                {
                    RenderTerminalOffscreen(terminal);
                    yield return null;
                    yield return null;
                    SaveTerminalScreenshot("day-summary.png");
                    terminal.ShowOnScreen(false); // back to the monitor texture
                }
                Press(terminal.Root.Q<Button>("summary-continue"));
                Assert.AreEqual(DisplayStyle.None, summary.style.display.value);

                // Sleep through the night.
                Assert.IsTrue(sleep.CanSleep);
                bed.Interact();
                yield return WaitUntil(() => sleep.State == SleepState.Asleep, 5f, "asleep");
                StringAssert.Contains($"Day {day} complete", sleep.RecapText);
                yield return WaitUntil(() => sleep.State == SleepState.Awake, 10f, "awake");

                Assert.AreEqual(6, game.Clock.Now.Hour, "wakes at 6 AM");
                Assert.AreEqual(day, game.Days.Completed.Count);
                Assert.IsTrue(player.ControlEnabled, "player can move after waking");
            }

            yield return null;
            StringAssert.StartsWith("DAY 4", hud.ClockText);
            var reports = game.Days.Completed;
            Assert.IsTrue(reports.All(r => r.Fills == 2 && r.IsComplete));
            Assert.AreEqual(game.Account.Equity - game.Account.NetDeposits, reports.Sum(r => r.NetPnL));
        }
    }
}
