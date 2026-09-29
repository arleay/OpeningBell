using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>PROP_SPEC §5: a new game starts broke; Sal's Pizza hires you and pays wages and tips into the bank.</summary>
    public class PizzaJobPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator StartBroke_WorkAShift_GetPaid()
        {
            GameBootstrap.StartingMoneyOverride = null; // the real new-game start
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            Assert.AreEqual(0m, game.Account.Cash, "no trading money");
            Assert.LessOrEqual(game.Economy.Bank.Balance, 0m, "nothing in the bank");

            game.SkipTo(game.Clock.Now.Date.AddHours(12)); // Sal's opens at 11
            var shop = Find<PizzaShop>();
            var player = Find<FirstPersonController>();
            player.PlaceAt(shop.WorkSpot, shop.WorkYaw, 5f);
            yield return null;

            var clock = Object.FindAnyObjectByType<PizzaClock>();
            StringAssert.Contains("job", clock.Prompt);
            clock.Interact(); // hired
            Assert.IsTrue(game.Job.Hired);
            clock.Interact(); // clock in
            Assert.IsTrue(game.Job.OnShift);
            Assert.IsTrue(game.IsWorking);

            yield return WaitUntil(() => shop.OrderWaiting, 30f, "a customer orders at the counter");
            Assert.Less(Vector3.Distance(player.transform.position, shop.WorkSpot), 0.3f, "nobody shoves you off the counter");
            yield return CaptureCamera(Camera.main, "pizza-counter.png");
            // A spectator's view over the dining room toward the counter: customer in front, you behind.
            var spectator = new GameObject("Spectator").AddComponent<Camera>();
            Vector3 eye = shop.transform.TransformPoint(new Vector3(-1.5f, 2.9f, 2.5f));
            spectator.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(shop.WorkSpot + Vector3.up * 0.5f - eye));
            spectator.fieldOfView = 70f;
            yield return CaptureCamera(spectator, "pizza-overview.png");
            Vector3 side = shop.WorkSpot + shop.transform.right * 3.2f + Vector3.up * 1.2f;
            spectator.transform.SetPositionAndRotation(side, Quaternion.LookRotation(shop.WorkSpot + Vector3.up * 0.9f - side));
            yield return CaptureCamera(spectator, "pizza-side.png");
            Object.Destroy(spectator.gameObject);

            // Wrong item first: sent back. Then the right order from the warmer.
            PizzaItem wrong = shop.OrderItem == PizzaItem.WholePie ? PizzaItem.CheeseSlice : PizzaItem.WholePie;
            PizzaCustomerTap tap = Object.FindObjectsByType<PizzaCustomerTap>(FindObjectsSortMode.None).Single(t => t.CanInteract);
            shop.TakeFromWarmer(wrong);
            tap.Interact();
            Assert.IsTrue(shop.OrderWaiting, "wrong item: still waiting");
            Object.FindAnyObjectByType<PizzaBin>().Interact();
            for (int i = 0; i < shop.OrderCount; i++) shop.TakeFromWarmer(shop.OrderItem);
            yield return CaptureCamera(Camera.main, "pizza-holding.png");
            tap.Interact();

            var register = Object.FindAnyObjectByType<PizzaRegister>();
            Assert.IsTrue(register.CanInteract, "ready to ring up");
            StringAssert.StartsWith("Ring up $", register.Prompt);
            register.Interact();
            Assert.AreEqual(1, game.Job.ShiftOrders);
            Assert.Greater(game.Job.ShiftTips, 0m, "a quick order tips");

            decimal before = game.Economy.Bank.Balance;
            yield return new WaitForSeconds(2f); // some paid time on the clock (10x: ~20 game seconds)
            game.SkipTo(game.Clock.Now.AddHours(1)); // an hour more on shift
            clock.Interact(); // clock out
            Assert.IsFalse(game.Job.OnShift);
            Assert.GreaterOrEqual(game.Economy.Bank.Balance - before, 15m, "an hour's wage plus the tip, in the bank");
        }
    }
}
