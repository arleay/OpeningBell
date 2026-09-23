using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Core;
using OpeningBell.Vehicles;
using UnityEditor;

namespace OpeningBell.Tests
{
    public class DealershipTests
    {
        private static VehicleCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset").CreateCatalog();

        private static readonly DateTime Monday = new DateTime(2026, 3, 2, 10, 0, 0);

        [Test]
        public void Lineups_AreRealCars()
        {
            VehicleCatalog catalog = Catalog();
            foreach (string id in OpeningBell.City.Dealerships.NewLineup.Concat(OpeningBell.City.Dealerships.UsedLineup))
            {
                Assert.IsTrue(catalog.TryGetModel(id, out VehicleModel m), id);
                Assert.AreEqual(VehicleKind.Car, m.Kind, id);
            }
        }

        [Test]
        public void Stock_TurnsOverOnMondays_AndIsTheSameAllWeek()
        {
            int week = Dealership.Week(Monday);
            Assert.AreEqual(week, Dealership.Week(Monday.AddDays(6).AddHours(13)), "Sunday night is the same week");
            Assert.AreEqual(week + 1, Dealership.Week(Monday.AddDays(7)), "next Monday is a new week");
            Assert.AreEqual(week, Dealership.Week(Monday.Date), "Monday midnight starts the week");

            var random = new SeededRandomService(42);
            string[] lineup = OpeningBell.City.Dealerships.NewLineup;
            var a = Dealership.NewStock(lineup, 6, week, random, "fsm");
            CollectionAssert.AreEqual(a, Dealership.NewStock(lineup, 6, week, new SeededRandomService(42), "fsm"), "deterministic");
            Assert.AreEqual(6, a.Count);
            Assert.AreEqual(6, a.Distinct().Count(), "no model twice in the showroom");
            bool rotates = Enumerable.Range(1, 8).Any(w => !Dealership.NewStock(lineup, 6, week + w, random, "fsm").SequenceEqual(a));
            Assert.IsTrue(rotates, "the showroom changes over the weeks");
            Assert.AreEqual(3, Dealership.NewStock(new[] { "a", "b", "c" }, 6, week, random, "x").Count, "small lineups show everything");
        }

        [Test]
        public void UsedStock_IsCheaperThanNew_AndOwnsUpToEveryFlaw()
        {
            VehicleCatalog catalog = Catalog();
            var random = new SeededRandomService(7);
            int week = Dealership.Week(Monday);
            var all = Enumerable.Range(0, 20).SelectMany(w => Dealership.UsedStock(catalog, OpeningBell.City.Dealerships.UsedLineup, 6, week + w, random, "has", "Harbor Auto Sales")).ToList();
            Assert.AreEqual(120, all.Count);
            Assert.AreEqual(all.Count, all.Select(l => l.Id).Distinct().Count(), "listing ids are unique across weeks");
            foreach (UsedListing l in all)
            {
                catalog.TryGetModel(l.ModelId, out VehicleModel m);
                Assert.Less(l.Price, m.Price, l.Id);
                Assert.GreaterOrEqual(l.Price, 1000, l.Id);
                Assert.That(l.Condition, Is.InRange(0.3, 0.95), l.Id);
                Assert.Greater(l.OdometerKm, 0, l.Id);
                // Honest stickers: every flaw that matters is spelled out.
                if (l.Condition < 0.5) StringAssert.Contains("tune-up", l.Description, l.Id);
                if (l.TireCondition < 0.4) StringAssert.Contains("tyres", l.Description, l.Id);
                if (l.OdometerKm > 150_000) StringAssert.Contains("high mileage", l.Description, l.Id);
                Assert.AreEqual("Harbor Auto Sales", l.Seller);
            }
            Assert.IsTrue(all.Any(l => l.Condition < 0.5) && all.Any(l => l.Condition > 0.75), "a spread of conditions");
            var again = Dealership.UsedStock(catalog, OpeningBell.City.Dealerships.UsedLineup, 6, week, new SeededRandomService(7), "has", "Harbor Auto Sales");
            Assert.AreEqual(all[0].Price, again[0].Price, "deterministic");
            Assert.AreEqual("12 years old · high mileage · engine runs rough, needs a tune-up soon · tyres near the wear bars.",
                Dealership.Warnings(12, 200_000, 0.4, 0.3));
            Assert.AreEqual("3 years old · clean, no known issues.", Dealership.Warnings(3, 30_000, 0.9, 0.8));
        }

        [Test]
        public void TestCars_AreLentNotSaved_AndSoldCarsLeave()
        {
            var fleet = new Fleet(Catalog());
            OwnedVehicle mine = fleet.Add("car_sedan", 24000m, Monday, 0, 0, 0, 0);
            var listing = new UsedListing { Id = "has-1-0", ModelId = "car_hatch", Price = 9000, OdometerKm = 120000, Condition = 0.55, TireCondition = 0.3, FuelFraction = 0.2 };
            OwnedVehicle loaner = fleet.Lend("car_hatch", listing, Monday, 1, 0, 2, 90);
            Assert.IsTrue(loaner.TestDrive);
            Assert.AreEqual(0.55, loaner.Condition, 1e-9, "a used test car drives like the sticker says");
            Assert.AreEqual(120_000_000, loaner.Odometer, 1e-6);
            Assert.IsFalse(fleet.IsSold(listing.Id), "a test drive isn't a sale");
            fleet.SetState(loaner, VehicleState.Riding);

            FleetSaveData save = fleet.CaptureState();
            Assert.AreEqual(1, save.Vehicles.Count, "test cars aren't saved");
            Assert.AreEqual(mine.Id, save.Vehicles[0].Id);

            fleet.Remove(loaner);
            Assert.IsNull(fleet.LastRidden, "handing it back forgets it");
            Assert.AreEqual(1, fleet.Vehicles.Count);

            decimal offer = Dealership.TradeInOffer(mine, Monday.AddMonths(6));
            Assert.AreEqual(OpeningBell.Trading.Money.RoundCents(mine.ResaleValue(Monday.AddMonths(6)) * 0.85m), offer);
            Assert.Less(offer, 24000m * 0.8m);
            fleet.Remove(mine);
            Assert.AreEqual(0, fleet.Vehicles.Count);
        }

        [Test]
        public void TestDriveDamage_IsBilledByConditionLost()
        {
            Assert.AreEqual(0m, Dealership.DamageBill(0.9, 0.9, 30000m));
            Assert.AreEqual(0m, Dealership.DamageBill(0.9, 0.897, 30000m), "a scuff isn't billed");
            Assert.AreEqual(900m, Dealership.DamageBill(0.9, 0.8, 30000m)); // 0.1 × 30% × 30,000
            Assert.AreEqual(0m, Dealership.DamageBill(0.8, 0.9, 30000m));
        }
    }
}
