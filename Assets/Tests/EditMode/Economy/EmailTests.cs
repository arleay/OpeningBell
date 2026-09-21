using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    public class EmailTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday; // Jan 7 2030

        private sealed class World
        {
            public MarketSimulation Market;
            public Account Account;
            public OrderManager Orders;
            public EconomySystem Economy;
            public Inbox Inbox;
            public EmailDirector Director;
        }

        private static readonly EmailDefinition[] Definitions =
        {
            new EmailDefinition { Id = "welcome", Subject = "Welcome", Body = "You have {brokerage}.", Trigger = EmailTrigger.GameStart },
            new EmailDefinition { Id = "bell", Subject = "Bell", Trigger = EmailTrigger.AtTime, DayOffset = 0, MinuteOfDay = 9 * 60 + 25 },
            new EmailDefinition { Id = "fill", Subject = "First fill", Trigger = EmailTrigger.FirstFill },
            new EmailDefinition { Id = "rent", Subject = "Rent soon", Body = "{rent} due {due}; bank {bank}.", Trigger = EmailTrigger.RentDueSoon, DaysBefore = 3 },
            new EmailDefinition { Id = "overdrawn", Subject = "Overdrawn", Trigger = EmailTrigger.Overdrawn },
        };

        private static World Build(double bank = 5000, Inbox inbox = null)
        {
            var w = new World { Market = TestMarkets.Create(3, Monday.AddHours(6), TestMarkets.Basic()) };
            w.Account = new Account(w.Market);
            w.Account.Deposit(10_000m);
            w.Orders = new OrderManager(w.Market, w.Account, new BrokerRules());
            var config = new EconomyConfig
            {
                StartingBankBalance = bank,
                DailyLivingCost = 0,
                Bills = new List<RecurringBill> { new RecurringBill { Id = "rent", Name = "Rent", Category = BillCategory.Housing, Amount = 950, DayOfMonth = 15 } },
            };
            w.Economy = new EconomySystem(config, Array.Empty<StoreItem>(), w.Account, Monday.AddHours(6));
            w.Inbox = inbox ?? new Inbox();
            w.Director = new EmailDirector(Definitions, w.Inbox, w.Account, w.Orders, w.Economy, Monday);
            return w;
        }

        private static void Step(World w, DateTime to)
        {
            w.Market.AdvanceTo(to);
            w.Economy.AdvanceTo(to);
            w.Director.Update(to);
        }

        [Test]
        public void Onboarding_ArrivesOnItsTriggers_ExactlyOnce()
        {
            World w = Build();
            Step(w, Monday.AddHours(6));
            CollectionAssert.AreEqual(new[] { "Welcome" }, w.Inbox.Emails.Select(e => e.Subject));
            Assert.AreEqual("You have $10,000.00.", w.Inbox.Emails[0].Body, "tokens filled at delivery");

            Step(w, Monday.AddHours(9).AddMinutes(20));
            Assert.AreEqual(1, w.Inbox.Emails.Count, "not yet 9:25");
            Step(w, Monday.AddHours(9).AddMinutes(40));
            Assert.AreEqual("Bell", w.Inbox.Emails.Last().Subject);

            w.Orders.SubmitMarket("AAA", OrderSide.Buy, 10);
            Step(w, Monday.AddHours(9).AddMinutes(41));
            w.Orders.SubmitMarket("AAA", OrderSide.Buy, 10);
            Step(w, Monday.AddHours(9).AddMinutes(42));
            Assert.AreEqual(1, w.Inbox.Emails.Count(e => e.Subject == "First fill"), "first fill only once");
            Assert.AreEqual(3, w.Inbox.UnreadCount);
        }

        [Test]
        public void RentReminder_ComesEveryMonth_ThreeDaysAhead()
        {
            World w = Build();
            Step(w, Monday.AddDays(4)); // Jan 11: rent on the 15th is 4 days away
            Assert.IsFalse(w.Inbox.Emails.Any(e => e.Subject == "Rent soon"));
            Step(w, Monday.AddDays(5)); // Jan 12: 3 days away
            Email reminder = w.Inbox.Emails.Single(e => e.Subject == "Rent soon");
            StringAssert.StartsWith("$950.00 due Tuesday, January 15", reminder.Body);

            Step(w, Monday.AddDays(38)); // Feb 14
            Assert.AreEqual(2, w.Inbox.Emails.Count(e => e.Subject == "Rent soon"), "one per rent date");
        }

        [Test]
        public void Overdraft_TriggersANotice()
        {
            World w = Build(bank: 100);
            Step(w, Monday.AddDays(9)); // rent bounces on Jan 15
            Assert.IsTrue(w.Economy.Bank.IsOverdrawn);
            Assert.AreEqual(1, w.Inbox.Emails.Count(e => e.Subject == "Overdrawn"));
        }

        [Test]
        public void RestoredInbox_DoesNotResendAnything()
        {
            World a = Build();
            Step(a, Monday.AddHours(10));
            var saved = UnityEngine.JsonUtility.ToJson(a.Inbox.CaptureState());

            var inbox = new Inbox();
            inbox.RestoreState(UnityEngine.JsonUtility.FromJson<InboxSaveData>(saved));
            World b = Build(inbox: inbox);
            Step(b, Monday.AddHours(10));
            CollectionAssert.AreEqual(a.Inbox.Emails.Select(e => e.Key), b.Inbox.Emails.Select(e => e.Key));
        }
    }
}
