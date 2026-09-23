using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;

namespace OpeningBell.Tests
{
    public class EconomyTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday; // Jan 7 2030

        private sealed class World
        {
            public MarketSimulation Market;
            public Account Brokerage;
            public OrderManager Orders;
            public TradingDayRecorder Days;
            public EconomySystem Economy;
        }

        private static EconomyConfig Config(double bank = 1000) => new EconomyConfig
        {
            StartingBankBalance = bank,
            DailyLivingCost = 10,
            OverdraftFee = 35,
            Bills = new List<RecurringBill>
            {
                new RecurringBill { Id = "rent", Name = "Rent", Category = BillCategory.Housing, Amount = 500, DayOfMonth = 10 },
                new RecurringBill { Id = "net", Name = "Internet", Category = BillCategory.Subscription, Amount = 20, DayOfMonth = 8 },
            },
        };

        private static readonly StoreItem[] Catalog =
        {
            new StoreItem { Id = "chair", Name = "Chair", Category = StoreCategory.Equipment, Slot = "Chair", Price = 100 },
            new StoreItem { Id = "fiber", Name = "Fiber", Category = StoreCategory.Service, Price = 50, MonthlyCost = 30, BillDayOfMonth = 12, ReplacesBillId = "net" },
            new StoreItem { Id = "office", Name = "Office", Category = StoreCategory.Lease, Price = 200, MonthlyCost = 200, BillDayOfMonth = 9 },
        };

        [Test]
        public void Lease_IsChargedAsRent_ButIsNotTheApartmentRent()
        {
            World w = Build();
            Assert.IsNull(w.Economy.Buy("office", Monday.AddHours(7)));
            Assert.AreEqual(BillCategory.Lease, w.Economy.ActiveBills.Single(b => b.Id == "office").Category);

            w.Economy.AdvanceTo(Monday.AddDays(2).AddHours(12)); // through Wed Jan 9
            BankTransaction lease = w.Economy.Bank.Transactions.Single(t => t.Description == "Office" && t.Kind == TransactionKind.Rent);
            Assert.AreEqual(-200m, lease.Amount);
            Assert.IsTrue(lease.IsNotable);
        }

        [Test]
        public void Spend_DeclinesInsteadOfOverdrawing()
        {
            World w = Build(Config(bank: 5));
            Assert.IsNull(w.Economy.Spend(4.5m, "Coffee", Monday.AddHours(7)));
            Assert.AreEqual(0.5m, w.Economy.Bank.Balance);
            StringAssert.StartsWith("Card declined", w.Economy.Spend(4.5m, "Coffee", Monday.AddHours(8)));
            Assert.AreEqual(0.5m, w.Economy.Bank.Balance);
            Assert.AreEqual(TransactionKind.Purchase, w.Economy.Bank.Transactions.Last().Kind);
        }

        private static World Build(EconomyConfig config = null, bool fundBank = true)
        {
            var w = new World { Market = TestMarkets.Create(3, Monday.AddHours(6), TestMarkets.Basic()) };
            w.Brokerage = new Account(w.Market);
            w.Brokerage.Deposit(10_000m);
            w.Orders = new OrderManager(w.Market, w.Brokerage, new BrokerRules());
            w.Days = new TradingDayRecorder(w.Market, w.Brokerage, w.Orders);
            w.Economy = new EconomySystem(config ?? Config(), Catalog, w.Brokerage, Monday.AddHours(6), fundBank);
            return w;
        }

        [Test]
        public void Bills_AndLivingCosts_AreChargedPerCalendarDay()
        {
            World w = Build();
            w.Economy.AdvanceTo(Monday.AddDays(4).AddHours(12)); // through Fri Jan 11

            // Living Jan 8–11 (4 × 10), internet on the 8th (20), rent on the 10th (500).
            Assert.AreEqual(1000m - 40m - 20m - 500m, w.Economy.Bank.Balance);
            var kinds = w.Economy.Bank.Transactions.Select(t => t.Kind).ToList();
            Assert.AreEqual(1, kinds.Count(k => k == TransactionKind.Rent));
            Assert.AreEqual(1, kinds.Count(k => k == TransactionKind.Subscription));
            Assert.AreEqual(4, kinds.Count(k => k == TransactionKind.Living));

            // The same days charged hour by hour give identical results.
            World hourly = Build();
            for (DateTime t = Monday.AddHours(6); t <= Monday.AddDays(4).AddHours(12); t = t.AddHours(1)) hourly.Economy.AdvanceTo(t);
            CollectionAssert.AreEqual(w.Economy.Bank.Transactions.Select(t => $"{t.Time:O} {t.Kind} {t.Amount}"),
                hourly.Economy.Bank.Transactions.Select(t => $"{t.Time:O} {t.Kind} {t.Amount}"));
        }

        [Test]
        public void DevDeposit_IsALedgerDeposit_ThatCoversAnOverdraft()
        {
            World w = Build(Config(bank: 300));
            w.Economy.AdvanceTo(Monday.AddDays(3)); // overdrawn by rent, as above
            decimal before = w.Economy.Bank.Balance;
            w.Economy.DevDeposit(100_000m, Monday.AddDays(3).AddHours(9));
            Assert.AreEqual(before + 100_000m, w.Economy.Bank.Balance);
            BankTransaction t = w.Economy.Bank.Transactions.Last();
            Assert.AreEqual(TransactionKind.Deposit, t.Kind);
            Assert.AreEqual(w.Economy.Bank.Balance, t.BalanceAfter);
            Assert.AreEqual(10_000m, w.Brokerage.Cash, "the brokerage isn't touched");
            w.Economy.DevDeposit(-5m, Monday.AddDays(3).AddHours(9));
            Assert.AreEqual(before + 100_000m, w.Economy.Bank.Balance, "nothing taken out");
        }

        [Test]
        public void BouncedBill_OverdrawsAndChargesAFee()
        {
            World w = Build(Config(bank: 300));
            w.Economy.AdvanceTo(Monday.AddDays(3)); // Jan 10: rent 500 on ~260 left

            Assert.IsTrue(w.Economy.Bank.IsOverdrawn);
            Assert.AreEqual(300m - 30m - 20m - 500m - 35m, w.Economy.Bank.Balance);
            Assert.AreEqual(TransactionKind.Fee, w.Economy.Bank.Transactions.Last().Kind);
        }

        [Test]
        public void Transfers_MoveMoney_WithoutCountingAsTradingPnL()
        {
            World w = Build();
            w.Market.AdvanceTo(Monday.AddHours(10));
            Order resting = w.Orders.SubmitLimit("AAA", OrderSide.Buy, 100, PriceTick.RoundDown(w.Market.Securities[0].Bid * 0.9m, 0.01m));
            decimal free = w.Brokerage.BuyingPower;

            StringAssert.Contains("free", w.Economy.TransferFromBrokerage(free + 1m, w.Market.Now), "reserved cash can't be moved");
            Assert.IsNull(w.Economy.TransferFromBrokerage(2000m, w.Market.Now));
            Assert.AreEqual(3000m, w.Economy.Bank.Balance);
            Assert.AreEqual(8000m, w.Brokerage.Cash);
            Assert.AreEqual(0m, w.Brokerage.DailyPnL, "a withdrawal is not a trading loss");

            StringAssert.Contains("in the bank", w.Economy.TransferToBrokerage(5000m, w.Market.Now));
            Assert.IsNull(w.Economy.TransferToBrokerage(500m, w.Market.Now));
            Assert.AreEqual(2500m, w.Economy.Bank.Balance);
            Assert.AreEqual(8500m, w.Brokerage.NetDeposits);

            w.Orders.Cancel(resting.Id);
            w.Market.AdvanceTo(Monday.AddHours(21));
            TradingDayReport day = w.Days.Completed.Single();
            Assert.AreEqual(-1500m, day.Transfers);
            Assert.AreEqual(0m, day.NetPnL, "no trades, so no trading result despite the transfers");
        }

        [Test]
        public void Purchases_NeedBankFunds_AndServicesReplaceBills()
        {
            World w = Build(Config(bank: 90));
            var bought = new List<StoreItem>();
            w.Economy.ItemPurchased += bought.Add;

            StringAssert.Contains("Not enough", w.Economy.Buy("chair", Monday.AddHours(7)));
            w.Economy.TransferFromBrokerage(500m, Monday.AddHours(7));
            Assert.IsNull(w.Economy.Buy("chair", Monday.AddHours(7)));
            Assert.IsTrue(w.Economy.HasUpgrade("Chair"));
            StringAssert.Contains("already own", w.Economy.Buy("chair", Monday.AddHours(7)));

            Assert.IsNull(w.Economy.Buy("fiber", Monday.AddHours(7)));
            CollectionAssert.AreEquivalent(new[] { "rent", "fiber" }, w.Economy.ActiveBills.Select(b => b.Id), "fiber replaces basic internet");
            CollectionAssert.AreEqual(new[] { "chair", "fiber" }, bought.Select(i => i.Id));
            Assert.AreEqual(90m + 500m - 100m - 50m, w.Economy.Bank.Balance);

            var upcoming = w.Economy.Upcoming(Monday, 10);
            CollectionAssert.AreEqual(new[] { "Rent", "Fiber" }, upcoming.Select(u => u.Bill.Name), "Jan 10 rent, Jan 12 fiber");
        }

        [Test]
        public void EconomyState_RoundTripsThroughJson()
        {
            World a = Build();
            a.Economy.TransferFromBrokerage(400m, Monday.AddHours(7));
            a.Economy.Buy("fiber", Monday.AddHours(7));
            a.Economy.AdvanceTo(Monday.AddDays(3));

            string json = JsonUtility.ToJson(a.Economy.CaptureState());
            World b = Build(fundBank: false);
            b.Economy.RestoreState(JsonUtility.FromJson<EconomySaveData>(json));

            foreach (World w in new[] { a, b }) w.Economy.AdvanceTo(Monday.AddDays(40));
            Assert.AreEqual(a.Economy.Bank.Balance, b.Economy.Bank.Balance);
            Assert.AreEqual(a.Economy.LastProcessedDate, b.Economy.LastProcessedDate);
            CollectionAssert.AreEquivalent(a.Economy.OwnedItems, b.Economy.OwnedItems);
            CollectionAssert.AreEqual(a.Economy.Bank.Transactions.Select(t => $"{t.Id} {t.Time:O} {t.Amount}"),
                b.Economy.Bank.Transactions.Select(t => $"{t.Id} {t.Time:O} {t.Amount}"));
        }

        [Test]
        public void ShippedEconomySettings_AreValid()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<EconomySettings>("Assets/ScriptableObjects/Economy/EconomySettings.asset");
            Assert.NotNull(settings);
            World w = Build();
            var economy = new EconomySystem(settings.Config, settings.StoreItems, w.Brokerage, Monday); // validates ids and references
            Assert.GreaterOrEqual(settings.StoreItems.Count, 5);
            Assert.IsTrue(economy.ActiveBills.Any(b => b.Category == BillCategory.Housing), "rent exists");
            TestContext.WriteLine($"monthly bills {economy.MonthlyBills:N2} + living {economy.DailyLivingCost * 30:N2}");
        }
    }
}
