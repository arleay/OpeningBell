using System;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Core;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §41–46, §48–51, §58–61, §88–90, §98: rewards, hotel, drinks, budget, spending, saves.</summary>
    public class ResortTests
    {
        private static readonly DateTime Wednesday = new DateTime(2030, 1, 9, 20, 0, 0);

        [Test]
        public void Rewards_EarnFromPlayAndSpend_TiersUp_NeverDoubleCount()
        {
            var floor = new CasinoFloor();
            floor.Account.BuyChips(10_000m, Wednesday, (a, w) => null);
            var bj = new BlackjackRound(new BlackjackRules { MinBet = 100m, MaxBet = 1_000m }, new Shoe(6, 0.75, new SeededRandom(1)), floor.Account, () => Wednesday);
            for (int i = 0; i < 20; i++)
            {
                bj.Deal(100m);
                while (bj.Phase == BlackjackPhase.PlayerTurn) bj.Stand();
            }
            int fromTables = floor.Rewards.Lifetime;
            Assert.AreEqual((int)(floor.Account.TotalWagered / 10m), fromTables, "1 point per $10 at the tables, once per round");
            Assert.AreEqual(RewardTier.Silver, new Func<RewardTier>(() =>
            {
                var r = new Rewards();
                r.EarnFromSpend(600m);
                return r.Tier;
            })());
            Assert.IsNull(floor.Spend(SpendCategory.Food, 120m, "Dinner", Wednesday, (a, w) => null));
            Assert.AreEqual(fromTables + 120, floor.Rewards.Lifetime);
            Assert.AreEqual(120m, floor.SpentOn(SpendCategory.Food));
        }

        [Test]
        public void Points_PayForThings_OnlyWhenTheBankPaysTheRest()
        {
            var floor = new CasinoFloor();
            floor.Rewards.Restore(1_000, 1_000, "0"); // $10 of points
            decimal bank = 5m;
            Func<decimal, string, string> charge = (a, w) => a > bank ? "declined" : (bank -= a) >= 0 ? null : null;
            Assert.IsNotNull(floor.Spend(SpendCategory.Drinks, 40m, "Round", Wednesday, charge, usePoints: true), "$30 left to pay, $5 in the bank");
            Assert.AreEqual(1_000, floor.Rewards.Points, "declined: no points taken");
            Assert.IsNull(floor.Spend(SpendCategory.Drinks, 14m, "Old Fashioned", Wednesday, charge, usePoints: true));
            Assert.AreEqual(1m, bank, "points covered $10, the bank $4");
            Assert.AreEqual(0, floor.Rewards.Points - 4, "4 new points on the $4 paid");
        }

        [Test]
        public void Hotel_Prices_WeekendsDiscounts_CheckOutAndKeys()
        {
            // Wednesday night, 2 nights: Wed + Thu at the weekday rate.
            Assert.AreEqual(378m, Hotel.Quote(RoomClass.Standard, Wednesday, 2, 0m));
            // Thursday booking of 3 nights: Thu, Fri (+20%), Sat (+20%).
            Assert.AreEqual(189m + 226.8m * 2, Hotel.Quote(RoomClass.Standard, Wednesday.AddDays(1), 3, 0m));
            Assert.AreEqual(Math.Round(4_500m * 0.75m, 2), Hotel.Quote(RoomClass.Penthouse, Wednesday, 1, 0.25m), "Diamond discount");
            Assert.AreEqual(Wednesday.Date, Hotel.NightOf(Wednesday.Date.AddDays(1).AddHours(2)), "2 AM is still last night");

            var hotel = new Hotel();
            decimal bank = 1_000m;
            Func<decimal, string, string> charge = (a, w) => a > bank ? "declined" : (bank -= a) >= 0 ? null : null;
            Assert.IsNotNull(hotel.Book(RoomClass.Penthouse, 1, Wednesday, 0m, charge));
            Assert.IsNull(hotel.Book(RoomClass.Suite, 2, Wednesday, 0m, charge));
            Assert.AreEqual(160m, bank);
            Assert.AreEqual(new DateTime(2030, 1, 11, 11, 0, 0), hotel.Stay.CheckOut, "11 AM after the last night");
            Assert.IsTrue(hotel.HasKey("502", Wednesday.AddHours(3)));
            Assert.IsFalse(hotel.HasKey("501", Wednesday.AddHours(3)), "your key opens your room only");
            Assert.IsNotNull(hotel.Book(RoomClass.Standard, 1, Wednesday, 0m, charge), "one stay at a time");
            Assert.IsFalse(hotel.Active(hotel.Stay.CheckOut), "expired at check-out");
            Assert.IsNull(hotel.Extend(1, Wednesday.AddDays(1), 0m, (a, w) => null));
            Assert.AreEqual(new DateTime(2030, 1, 12, 11, 0, 0), hotel.Stay.CheckOut);
        }

        [Test]
        public void Drinks_WearOff_StopTheCar_AndTheBar()
        {
            var d = new Intoxication();
            d.Drink(1, Wednesday);
            Assert.AreEqual(Sobriety.Tipsy, d.Level);
            Assert.IsTrue(d.CanDrive);
            d.Drink(2, Wednesday);
            Assert.IsFalse(d.CanDrive, "three drinks: no driving");
            d.Drink(2, Wednesday);
            Assert.IsTrue(d.Refused, "five: the bar stops serving");
            d.Update(Wednesday.AddHours(2));
            Assert.AreEqual(3.0, d.Units, 1e-9, "one an hour");
            d.Update(Wednesday.AddHours(10));
            Assert.AreEqual(Sobriety.Sober, d.Level);
            Assert.IsTrue(d.CanDrive);
        }

        [Test]
        public void Budget_Warns_AndOnlyStopsYouIfAskedTo()
        {
            var floor = new CasinoFloor();
            floor.Account.BuyChips(1_000m, Wednesday, (a, w) => null);
            floor.Budget.Start(100m, hard: false, Wednesday, floor.Net);
            var bj = new BlackjackRound(new BlackjackRules { MinBet = 50m, MaxBet = 500m }, new Shoe(6, 0.75, new SeededRandom(3)), floor.Account, () => Wednesday);
            bj.Deal(50m);
            while (bj.Phase == BlackjackPhase.PlayerTurn) bj.Hit(); // play badly
            Assert.IsTrue(floor.Budget.ActiveOn(Wednesday.AddHours(5)));
            Assert.IsFalse(floor.Budget.ActiveOn(Wednesday.AddHours(11)), "resets at 6 AM");

            floor.Budget.Start(60m, hard: true, Wednesday, floor.Net);
            Assert.IsNull(bj.Deal(50m));
            while (bj.Phase == BlackjackPhase.PlayerTurn) bj.Hit();
            decimal lost = floor.Budget.Lost(floor.Net);
            if (lost > 10m)
            {
                string refused = bj.Deal(50m);
                StringAssert.Contains("limit", refused, "a hard limit refuses the bet that would pass it");
            }
            floor.Budget.Clear();
            Assert.IsNull(bj.Deal(50m));
        }

        [Test]
        public void EverythingSaves_ThroughJson()
        {
            var floor = new CasinoFloor { Member = true, ValetCar = "car-7" };
            floor.Account.BuyChips(2_000m, Wednesday, (a, w) => null);
            floor.Hotel.Book(RoomClass.LuxurySuite, 2, Wednesday, 0.1m, (a, w) => null);
            floor.Drinks.Drink(2.5, Wednesday);
            floor.Budget.Start(500m, true, Wednesday, floor.Net);
            floor.Spend(SpendCategory.Hotel, 850m, "Suite", Wednesday, (a, w) => null);
            floor.FirstTime("cage");
            floor.Jackpot.Contribute(12.345m);
            floor.PokerSeat = () => ("nlh-1-2", 187m, 200m);

            var loaded = new CasinoFloor();
            loaded.RestoreState(JsonUtility.FromJson<CasinoSaveData>(JsonUtility.ToJson(floor.CaptureState())));
            Assert.IsTrue(loaded.Member);
            Assert.AreEqual("car-7", loaded.ValetCar);
            Assert.AreEqual(floor.Hotel.Stay.CheckOut, loaded.Hotel.Stay.CheckOut);
            Assert.AreEqual("503", loaded.Hotel.Stay.Room);
            Assert.AreEqual(floor.Hotel.Stay.Paid, loaded.Hotel.Stay.Paid);
            Assert.AreEqual(2.5, loaded.Drinks.Units, 1e-9);
            Assert.AreEqual(500m, loaded.Budget.Limit);
            Assert.IsTrue(loaded.Budget.Hard);
            Assert.AreEqual(850m, loaded.SpentOn(SpendCategory.Hotel));
            Assert.IsFalse(loaded.FirstTime("cage"), "hints aren't shown twice");
            Assert.AreEqual(floor.Jackpot.Pool, loaded.Jackpot.Pool);
            Assert.AreEqual(floor.Rewards.Lifetime, loaded.Rewards.Lifetime);

            // A poker seat at save time comes back as chips, once.
            decimal chips = loaded.Account.Chips;
            Assert.AreEqual(187m, loaded.SettleRestoredPoker(Wednesday));
            Assert.AreEqual(chips + 187m, loaded.Account.Chips);
            Assert.AreEqual(-13m, loaded.Account.PokerNet);
            Assert.AreEqual(0m, loaded.SettleRestoredPoker(Wednesday), "not twice");
        }

        [Test]
        public void Economy_CannotBeGamed()
        {
            var a = new CasinoAccount();
            Assert.IsFalse(a.TryWager(-50m, Wednesday, "x"), "negative wager");
            Assert.IsNotNull(a.BuyChips(decimal.MaxValue, Wednesday, (x, w) => null), "absurd amounts refused");
            Assert.IsNotNull(a.BuyChips(-1m, Wednesday, (x, w) => null));
            a.BuyChips(100m, Wednesday, (x, w) => null);
            Assert.IsFalse(a.ToTable(200m, Wednesday, "t"), "can't sit with chips you don't have");
            Assert.IsNull(a.CashOut(100m, Wednesday, (x, w) => { }));
            Assert.IsNotNull(a.CashOut(100m, Wednesday, (x, w) => { }), "no second cash-out");
        }
    }
}
