using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Economy;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>Phase 11: both dealers stock their lots; test drive, return, buy and sell, end to end.</summary>
    public class DealerPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Dealers_TestDrive_Buy_AndSell()
        {
            yield return LoadMain();
            var player = Find<FirstPersonController>();
            var city = Find<CityBuilder>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(10.5));
            DealerLot fresh = city.Dealers.Single(d => d.DealerName == "First Street Motors");
            DealerLot used = city.Dealers.Single(d => d.DealerName == "Harbor Auto Sales");
            yield return WaitUntil(() => fresh.StaffHere && used.StaffHere, 30f, "salespeople at work");

            foreach (DealerLot lot in city.Dealers)
            {
                Assert.AreEqual(6, lot.Displays.Count, lot.DealerName);
                Assert.IsTrue(lot.Displays.All(d => d.Car != null && d.Sign != null), lot.DealerName + " has every car out");
                Assert.IsTrue(lot.Displays.All(d => lot.Lot.Contains(new Vector2(d.Car.transform.position.x, d.Car.transform.position.z))), "on the lot");
            }
            Assert.IsTrue(used.Displays.All(d => d.Used != null && !string.IsNullOrEmpty(d.Used.Description)), "used cars carry their sticker");

            Camera cam = player.GetComponentInChildren<Camera>();
            player.PlaceAt(new Vector3(101f, 0f, 33f), 235f, 8f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(cam, "dealer-new.png");
            Vector3 board = fresh.Displays[0].Sign.transform.position;
            player.PlaceAt(board + new Vector3(0f, 0f, -2.2f), 0f, 18f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(cam, "dealer-board.png");
            player.PlaceAt(new Vector3(205f, 0f, -69.5f), 300f, 8f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(cam, "dealer-used.png");

            // Test drive, returned to the lot: free.
            decimal bank = game.Economy.Bank.Balance;
            DealerLot.Display shown = fresh.Displays[0];
            fresh.TestDrive(shown);
            Assert.IsNotNull(fresh.Loaner, "keys handed over");
            Assert.IsFalse(shown.Car.activeSelf, "the display car is the one out on the drive");
            yield return WaitUntil(() => city.Driver.IsDriving && city.Driver.Car != null && player.transform.parent != null, 5f, "in the driver's seat");
            yield return new WaitForSeconds(0.5f);
            city.Driver.Exit();
            yield return null;
            Assert.IsNull(fresh.Loaner, "handed back");
            Assert.IsTrue(shown.Car.activeSelf, "back on display");
            Assert.AreEqual(bank, game.Economy.Bank.Balance, "free when it comes back in one piece");
            Assert.IsFalse(game.Vehicles.Vehicles.Any(v => v.TestDrive), "the test car left the fleet");

            // Test drive abandoned on Maple St: the dealer fetches it for a fee.
            fresh.TestDrive(shown);
            yield return WaitUntil(() => city.Driver.IsDriving && player.transform.parent != null, 5f, "in the seat again");
            yield return new WaitForSeconds(0.5f);
            Rigidbody body = city.Driver.Car.Body;
            body.position = new Vector3(30f, 0.1f, -11.5f);
            body.linearVelocity = Vector3.zero;
            city.Driver.Car.transform.position = body.position;
            yield return new WaitForSeconds(1f);
            city.Driver.Exit();
            yield return null;
            Assert.IsNull(fresh.Loaner);
            Assert.IsTrue(game.Economy.Bank.Transactions.Any(t => t.Kind == TransactionKind.Fee && t.Description.Contains("recovery")), "recovery fee billed");
            Assert.LessOrEqual(game.Economy.Bank.Balance, bank - Dealership.RecoveryFee);

            // Buy the new car (two presses: ask, then sign). It waits in a delivery bay.
            game.Economy.DevDeposit(200_000m, game.Clock.Now);
            bank = game.Economy.Bank.Balance;
            decimal price = fresh.PriceOf(shown);
            fresh.Buy(shown);
            Assert.AreEqual(bank, game.Economy.Bank.Balance, "the first press only asks");
            fresh.Buy(shown);
            Assert.AreEqual(bank - price, game.Economy.Bank.Balance, "paid");
            OwnedVehicle bought = game.Vehicles.Vehicles.Single(v => v.ModelId == shown.ModelId && !v.TestDrive);
            Assert.IsTrue(fresh.Lot.Contains(new Vector2((float)bought.X, (float)bought.Z)), "parked in a bay on the lot");
            Assert.IsTrue(shown.Car.activeSelf, "new cars are ordered in: the display stays");
            yield return null;
            Assert.IsNotNull(city.Fleet.Shown(bought), "your car appears");

            // Sell it straight back: the dealer pays 85% of its private value.
            Assert.AreEqual(bought, fresh.TradeIn());
            decimal offer = Dealership.TradeInOffer(bought, game.Clock.Now);
            bank = game.Economy.Bank.Balance;
            fresh.Sell();
            fresh.Sell();
            Assert.IsFalse(game.Vehicles.Vehicles.Contains(bought), "sold");
            Assert.AreEqual(bank + offer, game.Economy.Bank.Balance);

            // A used car leaves the lot for good.
            DealerLot.Display banger = used.Displays[0];
            bank = game.Economy.Bank.Balance;
            used.Buy(banger);
            used.Buy(banger);
            Assert.AreEqual(bank - (decimal)banger.Used.Price, game.Economy.Bank.Balance);
            Assert.IsTrue(game.Vehicles.IsSold(banger.Used.Id));
            Assert.IsNull(banger.Car, "off the lot");
            OwnedVehicle banged = game.Vehicles.Vehicles.Single(v => v.ModelId == banger.ModelId && !v.TestDrive);
            Assert.AreEqual(banger.Used.Condition, banged.Condition, 1e-9, "as the sticker said");
        }
    }
}
