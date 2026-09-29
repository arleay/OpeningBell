using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Fund;
using OpeningBell.Home;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>A market, a bank, an office with furniture and a fund, wired like the game does.</summary>
    internal sealed class FundRig
    {
        public readonly MarketSimulation Market;
        public readonly Belongings Belongings = new Belongings();
        public readonly HedgeFund Fund;
        public decimal Bank;
        public readonly BrokerRules Rules = new BrokerRules();

        public FundRig(decimal bank = 400_000m, ulong seed = 11, FundConfig config = null, SecuritySpec[] specs = null, NewsTemplate[] news = null)
        {
            Market = new MarketSimulation(TestMarkets.FastConfig(), specs ?? TestMarkets.Basic(), new IndexSpec(), new OpeningBell.Core.SeededRandomService(seed),
                TestMarkets.Monday.AddHours(5), news);
            Bank = bank;
            Fund = new HedgeFund(Market, Rules, Belongings, seed, config ?? new FundConfig());
            Fund.ChargeOwner = (amount, what) =>
            {
                if (amount > Bank) return "Insufficient funds.";
                Bank -= amount;
                return null;
            };
            Fund.PayOwner = (amount, what) => Bank += amount;
        }

        public void Register(OfficeTenure tenure = OfficeTenure.Leased, decimal extra = 0m)
        {
            string error = Fund.Register("Kestrel Ridge Capital", 1, 2, tenure, extra, 20, 50_000m, Bank);
            Assert.IsNull(error, error);
        }

        /// <summary>Runs market and fund together to <paramref name="t"/> (the game's frame loop in one go).</summary>
        public void RunTo(DateTime t)
        {
            Market.AdvanceTo(t);
            Fund.AdvanceTo(t);
        }

        /// <summary>A complete workstation: desk facing south (seat on its north side at yaw 180), chair, tower, monitor, keyboard, mouse.</summary>
        public OwnedItem Station(float x, float z, bool chair = true, bool tower = true, bool monitor = true, bool peripherals = true, string chairId = "chair_office")
        {
            OwnedItem Put(string id, float px, float pz, float yaw = 0f, int on = 0)
            {
                OwnedItem i = Belongings.Add(id, 0, ItemState.Placed);
                i.Boxed = false;
                i.Property = HedgeFund.OfficeId;
                i.X = px; i.Z = pz; i.Yaw = yaw;
                i.MountedOn = on;
                return i;
            }
            OwnedItem desk = Put("desk_standard", x, z);
            // Yaw 0: the seat side is -z.
            if (chair) Put(chairId, x, z - 0.9f);
            if (tower) Put("pc_tower", x + 0.9f, z);
            if (monitor) Put("mon_27", x, z + 0.2f, 0, desk.Uid);
            if (peripherals)
            {
                Put("keyboard", x, z - 0.1f, 0, desk.Uid);
                Put("mouse", x + 0.3f, z - 0.1f, 0, desk.Uid);
            }
            Belongings.Touch();
            return desk;
        }

        public Employee Hire(double level = 0.5, Contract contract = null, Strategy? strategy = null)
        {
            Person p = Fund.MakePerson(level);
            if (strategy.HasValue) p.Strategy = strategy.Value;
            return Fund.HireDirect(p, contract ?? Contract.Of(PayStructure.HourlyPlusCommission, 40m, 0.15m), Market.Now.Date);
        }

        /// <summary>Equity − owner's net money in == trading result − expenses + other income (nothing created or lost).</summary>
        public void AssertBooksTie()
        {
            decimal lhs = Fund.Equity - Fund.Ledger.NetContributions;
            decimal rhs = Fund.TradingNet - Fund.Ledger.TotalExpenses + Fund.Ledger.OtherIncome;
            Assert.AreEqual((double)rhs, (double)lhs, 0.011, $"equity {Fund.Equity} contributions {Fund.Ledger.NetContributions} trading {Fund.TradingNet} expenses {Fund.Ledger.TotalExpenses}");
        }
    }

    public class FundTests
    {
        private static readonly DateTime Monday = TestMarkets.Monday;

        [Test]
        public void Register_SpendsTheFee_TransfersTheCapital_AndLeasesTheOffice()
        {
            var rig = new FundRig(bank: 200_000m);
            rig.Register();
            var cfg = rig.Fund.Config;
            Assert.AreEqual(200_000m - cfg.FormationFee - cfg.StartingCapital, rig.Bank, "fee and capital both leave the bank");
            Assert.AreEqual(cfg.StartingCapital, rig.Fund.Ledger.NetContributions, "only the capital is the owner's money in the company");
            Assert.AreEqual(cfg.StartingCapital - cfg.OfficeMonthlyRent - cfg.OfficeDeposit, rig.Fund.Ledger.Cash);
            Assert.AreEqual(cfg.OfficeDeposit, rig.Fund.DepositHeld, "the deposit is still the company's asset");
            Assert.AreEqual(cfg.StartingCapital - cfg.OfficeMonthlyRent, rig.Fund.Equity, "rent is the only expense so far");
            Assert.AreEqual(OfficeTenure.Leased, rig.Fund.Tenure);
            rig.AssertBooksTie();
        }

        [Test]
        public void Register_IsRefused_WithoutTheMilestone_OrTheMoney()
        {
            var rig = new FundRig(bank: 120_000m);
            StringAssert.Contains("You need $150,000", rig.Fund.Register("Some Fund", 0, 0, OfficeTenure.Leased, 0m, 20, 50_000m, rig.Bank));
            rig.Bank = 500_000m;
            StringAssert.Contains("more day", rig.Fund.Register("Some Fund", 0, 0, OfficeTenure.Leased, 0m, 2, 50_000m, rig.Bank));
            StringAssert.Contains("proven trading profit", rig.Fund.Register("Some Fund", 0, 0, OfficeTenure.Leased, 0m, 20, 100m, rig.Bank));
            StringAssert.Contains("3 characters", rig.Fund.Register("X", 0, 0, OfficeTenure.Leased, 0m, 20, 50_000m, rig.Bank));
            Assert.AreEqual(500_000m, rig.Bank, "nothing charged by a refused registration");
            Assert.IsFalse(rig.Fund.Exists);
        }

        [Test]
        public void Collect_MovesRealizedProfit_WithoutChangingEquity()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire();
            Assert.IsNull(rig.Fund.Allocate(e, 20_000m));
            Assert.AreEqual(0m, e.Collectible);
            StringAssert.Contains("No realized profit", e.CollectBlocker);

            e.Account.Ledger.Post(rig.Market.Now, LedgerEntryType.TradePnL, 3_420m, "AAA");
            Assert.AreEqual(3_420m, e.Collectible);
            decimal equity = rig.Fund.Equity, cash = rig.Fund.Ledger.Cash;
            decimal got = rig.Fund.Collect(e, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(3_420m, got);
            Assert.AreEqual(cash + 3_420m, rig.Fund.Ledger.Cash, "the company receives it once");
            Assert.AreEqual(equity, rig.Fund.Equity, "a transfer isn't profit");
            Assert.AreEqual(0m, e.Collectible);
            Assert.AreEqual(20_000m, e.Account.Cash, "the base capital stays on the desk");
            Assert.AreEqual(CashKind.Collection, rig.Fund.Ledger.Entries.Last().Kind);
            Assert.AreEqual(LedgerEntryType.Withdrawal, e.Account.Ledger.Entries.Last().Type, "both sides of the transfer are in a ledger");
        }

        [Test]
        public void Collect_WaitsUntilLossesAreWonBack()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire();
            rig.Fund.Allocate(e, 20_000m);
            e.Account.Ledger.Post(rig.Market.Now, LedgerEntryType.TradePnL, -5_000m, "AAA");
            e.Account.Ledger.Post(rig.Market.Now, LedgerEntryType.TradePnL, 5_000m, "AAA");
            Assert.AreEqual(0m, e.Collectible, "recovering a loss isn't distributable profit");
            e.Account.Ledger.Post(rig.Market.Now, LedgerEntryType.TradePnL, -1_000m, "AAA");
            StringAssert.Contains("Recover $1,000.00", e.CollectBlocker);
        }

        [Test]
        public void Commission_IsPaidOnlyOnNewHighs()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire(contract: Contract.Of(PayStructure.Commission, 0m, 0.2m));
            DateTime t = rig.Market.Now;
            int id = 1;
            Fill F(decimal realized) => new Fill(id++, 1, "AAA", OrderSide.Sell, 1, 25m, 0m, t) { RealizedPnL = realized };
            rig.Fund.OnFill(e, F(-5_000m));
            rig.Fund.OnFill(e, F(5_000m));
            Assert.AreEqual(0m, e.CommissionAccrued, "earning back a $5,000 loss pays no commission");
            rig.Fund.OnFill(e, F(1_000m));
            Assert.AreEqual(200m, e.CommissionAccrued, "20% of the $1,000 above the high-water mark");
            rig.Fund.OnFill(e, F(-500m));
            rig.Fund.OnFill(e, F(700m));
            Assert.AreEqual(240m, e.CommissionAccrued, "only the $200 past the previous high counts");
        }

        [Test]
        public void Negotiation_IsConsistent_AndLowballsEndTheTalks()
        {
            var rig = new FundRig();
            rig.Register();
            rig.Fund.Post(Channel.LocalBoard, null, null, out _);
            rig.RunTo(Monday.AddDays(3).AddHours(19));
            Applicant a = rig.Fund.Applicants.First(x => x.Status == ApplicantStatus.Open);
            Person p = a.Person;
            Contract low = Contract.Of(PayStructure.Hourly, 8m, 0m);
            Response first = Negotiation.Respond(p, low, a.Asking, a.Interest, 0, 3);
            Assert.AreEqual(first.Answer, Negotiation.Respond(p, low, a.Asking, a.Interest, 0, 3).Answer, "same offer, same answer");
            Assert.AreEqual(Answer.Reject, rig.Fund.Offer(a, low).Answer);
            Assert.AreEqual(Answer.Reject, rig.Fund.Offer(a, low).Answer);
            Assert.AreEqual(Answer.Withdraw, rig.Fund.Offer(a, low).Answer, "three unreasonable offers and they walk");
            Assert.AreEqual(ApplicantStatus.Withdrawn, a.Status);

            Applicant b = rig.Fund.Applicants.First(x => x.Status == ApplicantStatus.Open);
            Assert.AreEqual(Answer.Accept, rig.Fund.Offer(b, b.Asking.Copy()).Answer, "their own ask is always accepted");
            Employee hired = rig.Fund.Employees.Last();
            Assert.AreEqual(b.Person.Id, hired.Id);
            Assert.AreEqual(b.Asking.Describe(), hired.Contract.Describe());
        }

        [Test]
        public void Applicants_ArriveOverTime_AndDontReshuffle()
        {
            var rig = new FundRig();
            rig.Register();
            rig.Fund.Post(Channel.IndustryBoard, null, null, out Listing l);
            rig.RunTo(Monday.AddDays(4).AddHours(19));
            int n = rig.Fund.Applicants.Count;
            Assert.Greater(n, 2, "a few applications over four days");
            string names = string.Join(",", rig.Fund.Applicants.Select(a => a.Person.Name + a.Person.Years));
            // Another fund built on the same world gets the same people.
            var twin = new FundRig();
            twin.Register();
            twin.Fund.Post(Channel.IndustryBoard, null, null, out _);
            twin.RunTo(Monday.AddDays(4).AddHours(19));
            Assert.AreEqual(names, string.Join(",", twin.Fund.Applicants.Select(a => a.Person.Name + a.Person.Years)));
            var skills = rig.Fund.Applicants.Select(a => a.Person[Skill.SelfControl]).ToList();
            Assert.Greater(skills.Max() - skills.Min(), 8, "applicants differ");
        }

        [Test]
        public void Workstation_ExplainsWhatIsMissing_AndChecksTheChair()
        {
            var rig = new FundRig();
            rig.Register();
            OwnedItem bare = rig.Station(0, 0, chair: false, tower: false, monitor: false, peripherals: false);
            Workstation w = rig.Fund.Stations.First(s => s.Desk == bare.Uid);
            CollectionAssert.IsSupersetOf(w.Problems, new[] { "Computer missing.", "Monitor missing.", "No chair assigned.", "Keyboard missing." });

            OwnedItem full = rig.Station(6, 0);
            Assert.IsTrue(rig.Fund.Stations.First(s => s.Desk == full.Uid).Valid, rig.Fund.Stations.First(s => s.Desk == full.Uid).Summary);

            // A chair behind the desk doesn't serve it.
            OwnedItem behind = rig.Station(12, 0, chair: false);
            OwnedItem chair = rig.Belongings.Add("chair_ergo", 0, ItemState.Placed);
            chair.Property = HedgeFund.OfficeId; chair.X = 12; chair.Z = 1.0f; chair.Boxed = false;
            rig.Belongings.Touch();
            CollectionAssert.Contains(rig.Fund.Stations.First(s => s.Desk == behind.Uid).Problems, "No chair assigned.");

            // The world says the seat can't be reached.
            rig.Fund.Access = s => s.Desk == full.Uid ? "Chair access blocked." : null;
            rig.Fund.AccessVersion++;
            CollectionAssert.Contains(rig.Fund.Stations.First(s => s.Desk == full.Uid).Problems, "Chair access blocked.");

            // A monitor switched off isn't connected.
            rig.Fund.Access = null;
            rig.Fund.AccessVersion++;
            foreach (OwnedItem m in rig.Belongings.MountedOn(full.Uid)) if (m.Item.IsMonitor) m.Power = false;
            rig.Belongings.Touch();
            CollectionAssert.Contains(rig.Fund.Stations.First(s => s.Desk == full.Uid).Problems, "Monitor not connected.");
        }

        [Test]
        public void Assign_RefusesADeskSomeoneElseHas()
        {
            var rig = new FundRig();
            rig.Register();
            OwnedItem desk = rig.Station(0, 0);
            Employee a = rig.Hire(), b = rig.Hire();
            Assert.IsNull(rig.Fund.Assign(a, desk.Uid));
            Assert.AreEqual($"Already assigned to {a.Name}.", rig.Fund.Assign(b, desk.Uid));
            StringAssert.Contains("No free desk", rig.Fund.AutoAssign(b));
        }

        [Test]
        public void Schedule_ArriveWaitWorkAndLeave_WithWagesForTimeOnSite()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire(contract: Contract.Of(PayStructure.Hourly, 30m, 0m));
            rig.RunTo(Monday.AddHours(7));
            Assert.AreEqual(Activity.OffDuty, e.Activity);
            rig.RunTo(Monday.AddHours(8).AddMinutes(10));
            Assert.That(e.Activity, Is.EqualTo(Activity.Commuting).Or.EqualTo(Activity.Arriving));
            rig.RunTo(Monday.AddHours(9));
            Assert.AreEqual(Activity.WaitingForWorkstation, e.Activity, "no desk yet: they wait, and it's recorded why");
            Assert.AreEqual("No workstation assigned.", rig.Fund.StationProblem(e));
            Assert.Greater(e.WagesAccrued, 0m, "waiting on the clock is still paid");

            OwnedItem desk = rig.Station(0, 0);
            rig.Fund.Assign(e, desk.Uid);
            rig.RunTo(Monday.AddHours(9).AddMinutes(2));
            Assert.AreEqual(Activity.SettlingIn, e.Activity, "they walk over first");
            rig.RunTo(Monday.AddHours(9).AddMinutes(10));
            Assert.AreEqual(Activity.Preparing, e.Activity);
            rig.RunTo(Monday.AddHours(10));
            Assert.That(e.Activity, Is.EqualTo(Activity.Trading).Or.EqualTo(Activity.OnBreak));
            rig.RunTo(Monday.AddHours(19));
            Assert.AreEqual(Activity.OffDuty, e.Activity);
            // About eight hours on site at $30.
            Assert.AreEqual(240.0, (double)e.WagesAccrued, 20.0);
            Assert.Greater(e.Today.MinutesWaiting, 20);
            rig.AssertBooksTie();
        }

        [Test]
        public void Payroll_PaysOnFriday_AndExpensesAreCountedOnce()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire(contract: Contract.Of(PayStructure.Hourly, 45m, 0m));
            rig.Fund.Assign(e, rig.Station(0, 0).Uid);
            rig.RunTo(Monday.AddDays(4).AddHours(16).AddMinutes(59));
            decimal accrued = e.WagesAccrued;
            Assert.Greater(accrued, 1_500m);
            decimal cash = rig.Fund.Ledger.Cash, expenses = rig.Fund.Ledger.TotalExpenses;
            rig.RunTo(Monday.AddDays(4).AddHours(17).AddMinutes(1));
            Assert.Less(e.WagesAccrued, 0.01m, "paid off (a sub-cent remainder may carry)");
            Assert.AreEqual((double)(cash - accrued), (double)rig.Fund.Ledger.Cash, 0.011, "cash falls by what was accrued");
            Assert.AreEqual(expenses, rig.Fund.Ledger.TotalExpenses, "paying isn't another expense");
            Assert.AreEqual(CashKind.Wages, rig.Fund.Ledger.Entries.Last(x => x.Employee == e.Id).Kind);
            rig.AssertBooksTie();
        }

        [Test]
        public void Payroll_WithoutCash_BecomesOverdue_NotForgiven()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire(contract: Contract.Of(PayStructure.Hourly, 60m, 0m));
            rig.Fund.Withdraw(rig.Fund.FreeCash - 100m);
            rig.RunTo(Monday.AddDays(4).AddHours(17).AddMinutes(1));
            Assert.Greater(e.WagesOverdue, 1_000m);
            Assert.AreEqual(0.0, (double)rig.Fund.Ledger.Cash, 0.01);
            rig.RunTo(Monday.AddDays(7).AddHours(7));
            Assert.GreaterOrEqual(rig.Fund.DelinquentDays, 1);
            decimal owed = e.WagesOverdue;
            Assert.IsNull(rig.Fund.Contribute(owed + 500m));
            Assert.AreEqual(0m, e.WagesOverdue, "a contribution settles back pay at once");
            rig.AssertBooksTie();
        }

        [Test]
        public void LossLimit_LocksTheDesk_AndBlocksNewPositions()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire();
            rig.Fund.Allocate(e, 20_000m);
            e.Policy.MaxDailyLoss = 50m;
            rig.RunTo(Monday.AddHours(10));
            e.Account.Ledger.Post(rig.Market.Now, LedgerEntryType.TradePnL, -80m, "AAA");
            rig.RunTo(Monday.AddHours(10).AddMinutes(1));
            Assert.IsTrue(e.LockedToday);
            Order o = e.Orders.SubmitMarket("AAA", OrderSide.Buy, 1);
            Assert.AreEqual(OrderStatus.Rejected, o.Status);
            StringAssert.Contains("Daily loss limit", o.StatusReason);
            rig.RunTo(Monday.AddDays(1).AddHours(10));
            Assert.IsFalse(e.LockedToday, "a new day, a clean slate");
        }

        [Test]
        public void Gate_EnforcesInstrumentsAndSize_EvenForTheImpulsive()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire();
            rig.Fund.Allocate(e, 30_000m);
            e.Policy.Instruments.Add("AAA");
            e.Policy.MaxContracts = 2;
            rig.RunTo(Monday.AddHours(10));
            StringAssert.Contains("approved list", e.Orders.SubmitMarket("BBB", OrderSide.Buy, 1).StatusReason);
            StringAssert.Contains("Position limit", e.Orders.SubmitMarket("AAA", OrderSide.Buy, 3).StatusReason);
            Order ok = e.Orders.SubmitMarket("AAA", OrderSide.Buy, 2);
            Assert.AreEqual(OrderStatus.Filled, ok.Status);
            e.Policy.Authorized = false;
            Assert.AreEqual(OrderStatus.Filled, e.Orders.SubmitMarket("AAA", OrderSide.Sell, 2).Status, "closing is always allowed");
        }

        [Test]
        public void Training_ChargesOnce_TakesItsTime_AndSurvivesASave()
        {
            var cfg = new FundConfig();
            var rig = new FundRig(bank: 600_000m, config: cfg);
            rig.Register(extra: 200_000m);
            Employee e = rig.Hire(level: 0.3);
            rig.Fund.Assign(e, rig.Station(0, 0).Uid);
            e.Person.Skills[(int)Skill.Analysis] = 45;
            e.Person.Caps[(int)Skill.Analysis] = 95;
            double before = e.Person[Skill.Analysis];
            double estBefore = PeopleFactory.EstimatedWinRate(e.Person);
            CourseOffer offer = rig.Fund.OfferCourse(e, Skill.Analysis);
            Assert.AreEqual(50_000m, offer.Price);
            Assert.AreEqual(1, offer.Level);
            decimal cash = rig.Fund.Ledger.Cash;
            Assert.IsNull(rig.Fund.BuyTraining(e, Skill.Analysis));
            Assert.AreEqual(cash - 50_000m, rig.Fund.Ledger.Cash);
            StringAssert.Contains("already on", rig.Fund.BuyTraining(e, Skill.Analysis), "a second click can't stack it");
            Assert.AreEqual(cash - 50_000m, rig.Fund.Ledger.Cash);

            rig.RunTo(Monday.AddHours(7));
            StringAssert.Contains("Queued", rig.Fund.TrainingQueueReason(e));
            // Step until they sit down to study (they arrive around 8:30), then two minutes in.
            DateTime t = Monday.AddHours(8);
            while (e.Activity != Activity.Training && t < Monday.AddHours(9)) rig.RunTo(t = t.AddMinutes(1));
            Assert.AreEqual(Activity.Training, e.Activity, "they study at their desk before the open");
            rig.RunTo(t = t.AddMinutes(2));
            TrainingJob job = e.CurrentTraining;
            Assert.That(job.MinutesDone, Is.InRange(1, job.Minutes - 1));

            // Save mid-course and carry on in a new fund: nothing is charged again, progress continues.
            FundSaveData saved = JsonUtility.FromJson<FundSaveData>(JsonUtility.ToJson(rig.Fund.CaptureState()));
            var copy = new HedgeFund(rig.Market, rig.Rules, rig.Belongings, 11, cfg);
            copy.RestoreState(saved);
            Employee e2 = copy.Find(e.Id);
            Assert.AreEqual(job.MinutesDone, e2.CurrentTraining.MinutesDone);
            Assert.AreEqual(rig.Fund.Ledger.Cash, copy.Ledger.Cash);
            rig.RunTo(Monday.AddHours(9).AddMinutes(30));
            copy.AdvanceTo(Monday.AddHours(9).AddMinutes(30));
            Assert.IsNull(e.CurrentTraining, "ten minutes and it's done");
            Assert.IsNull(e2.CurrentTraining);
            Assert.AreEqual(e.Person[Skill.Analysis], e2.Person[Skill.Analysis]);
            Assert.Greater(e.Person[Skill.Analysis], before + 10);
            double gainPp = (PeopleFactory.EstimatedWinRate(e.Person) - estBefore) * 100;
            Assert.That(gainPp, Is.InRange(3.5, 7.0), "the first course is worth about five points of estimated win rate");
            Assert.AreEqual(1, e.Training.Count(j => j.Done));
        }

        [Test]
        public void Cars_FollowSavings_OnceAMonth()
        {
            var rig = new FundRig();
            rig.Register();
            Employee rich = rig.Hire(), poor = rig.Hire();
            rig.Fund.Assign(rich, rig.Station(0, 0).Uid);
            rig.Fund.Assign(poor, rig.Station(3, 0).Uid);
            rich.Person.Savings = 600_000m;
            rich.Person.Traits[(int)Trait.Flash] = 90;
            rich.Person.Car = "Kestrel Sedan";
            poor.Person.Savings = 20_000m;
            poor.Person.Car = "Kestrel Sedan";
            // Mid-month: nothing yet. The 1st of February: the review.
            rig.RunTo(new DateTime(2030, 1, 20, 9, 0, 0));
            Assert.AreEqual("Kestrel Sedan", rich.Person.Car);
            rig.RunTo(new DateTime(2030, 2, 1, 9, 0, 0));
            Assert.AreNotEqual("Kestrel Sedan", rich.Person.Car, $"a better car once savings allow (exists {rig.Fund.Exists}, savings {rich.Person.Savings}, former {rich.Former}, last: {rich.Person.History[rich.Person.History.Count - 1].Text})");
            decimal price = VehicleLadder.PriceOf(rich.Person.Car);
            Assert.Greater(price, 24_000m);
            Assert.Less(rich.Person.Savings, 600_000m + 50_000m - (price - 24_000m * 0.6m) + 1m, "the trade-in difference comes out of savings");
            Assert.AreEqual("Kestrel Sedan", poor.Person.Car, "no upgrade they can't comfortably afford");
            StringAssert.Contains("Traded the Kestrel Sedan", rich.Person.History[rich.Person.History.Count - 1].Text);
        }

        [Test]
        public void Employees_TradeTheSharedMarket_AndTheBooksTie()
        {
            var rig = new FundRig(bank: 600_000m);
            rig.Register(extra: 100_000m);
            var staff = new List<Employee>();
            foreach (Strategy s in new[] { Strategy.Scalping, Strategy.TrendPullback, Strategy.Breakout, Strategy.Reversion })
            {
                Employee e = rig.Hire(level: 0.5, strategy: s);
                rig.Fund.Assign(e, rig.Station(staff.Count * 3, 0).Uid);
                rig.Fund.Allocate(e, 30_000m);
                e.Policy.MaxRiskPerTrade = 600m;
                e.Policy.MaxDailyLoss = 3_000m;
                staff.Add(e);
            }
            rig.RunTo(Monday.AddDays(5).AddHours(18));
            int fills = staff.Sum(e => e.Orders.Fills.Count);
            TestContext.WriteLine("trades " + string.Join(", ", staff.Select(e => $"{e.Person.Strategy}:{e.Closed().Count} net {e.NetRealized:N0}")));
            Assert.Greater(fills, 10, "they traded");
            foreach (Employee e in staff)
                foreach (Fill f in e.Orders.Fills)
                {
                    Assert.IsTrue(rig.Market.TryGetSecurity(f.Ticker, out _), "a real symbol");
                    Assert.Greater(f.Commission, 0m, "the broker's commission applies");
                }
            var closed = staff.SelectMany(e => e.Closed()).ToList();
            Assert.IsTrue(closed.Any(t => t.Net > 0) && closed.Any(t => t.Net < 0), "wins and losses both happen");
            foreach (Employee e in staff) Assert.AreEqual(0, e.OpenPositionCount, "flat after the close");
            rig.AssertBooksTie();
        }

        [Test]
        public void Save_RoundTrip_ContinuesExactly()
        {
            FundRig Make() => new FundRig(bank: 600_000m, seed: 21);
            var a = Make();
            a.Register(extra: 50_000m);
            for (int i = 0; i < 2; i++)
            {
                Employee e = a.Hire(level: 0.5);
                a.Fund.Assign(e, a.Station(i * 3, 0).Uid);
                a.Fund.Allocate(e, 25_000m);
            }
            a.Fund.Post(Channel.LocalBoard, null, null, out _);
            a.RunTo(Monday.AddDays(1).AddHours(11).AddMinutes(17));

            string marketJson = JsonUtility.ToJson(a.Market.CaptureState());
            string fundJson = JsonUtility.ToJson(a.Fund.CaptureState());
            var b = Make();
            b.Market.RestoreState(JsonUtility.FromJson<MarketSaveData>(marketJson));
            b.Belongings.RestoreState(a.Belongings.CaptureState());
            b.Fund.RestoreState(JsonUtility.FromJson<FundSaveData>(fundJson));

            DateTime end = Monday.AddDays(3).AddHours(18);
            a.RunTo(end);
            b.RunTo(end);
            Assert.AreEqual(a.Fund.Ledger.Cash, b.Fund.Ledger.Cash);
            Assert.AreEqual(a.Fund.Equity, b.Fund.Equity);
            Assert.AreEqual(a.Fund.Applicants.Count, b.Fund.Applicants.Count);
            for (int i = 0; i < a.Fund.Employees.Count; i++)
            {
                Assert.AreEqual(a.Fund.Employees[i].Orders.Fills.Count, b.Fund.Employees[i].Orders.Fills.Count);
                Assert.AreEqual(a.Fund.Employees[i].NetRealized, b.Fund.Employees[i].NetRealized);
                Assert.AreEqual(a.Fund.Employees[i].Satisfaction, b.Fund.Employees[i].Satisfaction);
            }
            b.AssertBooksTie();
        }

        [Test]
        public void LetGo_ClosesPositionsThroughTheMarket_PaysOut_AndReturnsCapital()
        {
            var rig = new FundRig();
            rig.Register();
            Employee e = rig.Hire(contract: Contract.Of(PayStructure.Hourly, 40m, 0m));
            rig.Fund.Assign(e, rig.Station(0, 0).Uid);
            rig.Fund.Allocate(e, 20_000m);
            rig.RunTo(Monday.AddHours(11));
            e.Policy.MaxContracts = 10;
            Assert.AreEqual(OrderStatus.Filled, e.Orders.SubmitMarket("AAA", OrderSide.Buy, 2).Status);
            StringAssert.Contains("will be closed at market", rig.Fund.ExitSummary(e));
            decimal cashBefore = rig.Fund.Ledger.Cash;
            rig.Fund.LetGo(e);
            Assert.IsTrue(e.Former);
            Assert.AreEqual(0, e.OpenPositionCount, "closed with a market order");
            Assert.AreEqual(OrderType.Market, e.Orders.Orders.Last().Type);
            Assert.AreEqual(0m, e.WagesAccrued + e.WagesOverdue, "final pay settled");
            Assert.AreEqual(0m, e.Account.Cash, "desk emptied back to the company");
            Assert.Greater(rig.Fund.Ledger.Cash, cashBefore);
            Assert.AreEqual(Activity.Leaving, e.Activity, "they walk out, not vanish");
            rig.RunTo(Monday.AddHours(11).AddMinutes(20));
            Assert.AreEqual(Activity.Former, e.Activity);
            rig.AssertBooksTie();
        }

        [Test]
        public void LiquidityShare_SecondDeskWalksDeeperIntoTheBook()
        {
            var market = new FakeMarketData();
            market.SetQuote("XYZ", 9.99m, 10.01m, size: 10);
            var rules = new BrokerRules { BookLevelsPerTick = 5 };
            var share = new LiquidityShare();
            OrderManager Desk()
            {
                var acct = new Account(market);
                acct.Deposit(1_000_000m);
                return new OrderManager(market, acct, rules) { SharedLiquidity = share };
            }
            OrderManager a = Desk(), b = Desk();
            decimal pa = a.SubmitMarket("XYZ", OrderSide.Buy, 10).AverageFillPrice;
            decimal pb = b.SubmitMarket("XYZ", OrderSide.Buy, 10).AverageFillPrice;
            Assert.AreEqual(10.01m, pa);
            Assert.Greater(pb, pa, "the inside was already taken this tick");
            market.Now = market.Now.AddSeconds(2);
            Assert.AreEqual(10.01m, b.SubmitMarket("XYZ", OrderSide.Buy, 10).AverageFillPrice, "a new tick, a fresh book");
        }
    }
}
