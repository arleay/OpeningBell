using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Fund
{
    /// <summary>Where someone is in their day (FUND_SPEC §4). The simulation's word; bodies in the world follow it.</summary>
    public enum Activity
    {
        OffDuty,
        AwaitingStart,
        Commuting,
        Arriving,
        WaitingForWorkstation,
        Preparing,
        Trading,
        OnBreak,
        Training,
        RiskLocked,
        WrappingUp,
        Leaving,
        Former,
        /// <summary>Walking from wherever they were to a newly assigned desk.</summary>
        SettlingIn,
    }

    public enum BreakKind { None, Coffee, Restroom, Lunch, Chat, Rest }

    /// <summary>Limits the company sets per trader. Hard ones are enforced by the order gate; soft ones are wishes.</summary>
    [Serializable]
    public sealed class RiskPolicy
    {
        public int MaxContracts = 4;
        public int MaxPositions = 2;
        public decimal MaxRiskPerTrade = 400m;
        public decimal MaxDailyLoss = 1_500m;
        /// <summary>Tickers they may trade (empty = all).</summary>
        public List<string> Instruments = new List<string>();
        /// <summary>Strategies they may use (empty = their own only).</summary>
        public List<Strategy> Strategies = new List<Strategy>();
        public bool Authorized = true;
        // Soft preferences: followed as well as their self-control allows.
        /// <summary>Most trades a day management would like (0 = no preference).</summary>
        public int PreferredMaxTrades;
        /// <summary>Only take clearly good setups (raises their bar).</summary>
        public bool PreferQuality;

        public RiskPolicy Copy()
        {
            var c = (RiskPolicy)MemberwiseClone();
            c.Instruments = new List<string>(Instruments);
            c.Strategies = new List<Strategy>(Strategies);
            return c;
        }

        public bool Allows(string ticker) => Instruments.Count == 0 || Instruments.Contains(ticker);
    }

    /// <summary>One round trip (entry to flat) on an employee's desk, for reports.</summary>
    [Serializable]
    public sealed class TradeRecord
    {
        public long Id;
        public string Ticker = "";
        public Strategy Strategy;
        public int Side; // +1 long, -1 short
        public long Contracts;
        public long Opened, Closed;
        public decimal Entry, Exit, PlannedStop, PlannedTarget;
        public decimal Gross, Fees, Net;
        /// <summary>Risk taken at entry in dollars (contracts × stop distance × point value).</summary>
        public decimal Risk;
        /// <summary>Result in units of the planned risk.</summary>
        public double R => Risk > 0m ? (double)(Net / Risk) : 0;
        /// <summary>Setup quality as judged (0–1) and whether it met their own bar (strategy adherence).</summary>
        public double Quality;
        public bool FollowedPlan = true;
        /// <summary>Why it was taken or how it was handled, e.g. "chased", "revenge", "cut early".</summary>
        public string Notes = "";
        // Running totals while it's open.
        public decimal EntryNotional, ExitNotional;
        public long ExitQuantity;
        public bool MovedToBreakeven, Trailed, Widened;
        public long LastManagedMinute;
        public bool Open => Closed == 0;
    }

    /// <summary>Something an employee did that explains their results ("Trade frequency rose after three losses").</summary>
    [Serializable]
    public sealed class BehaviorNote
    {
        public long Time;
        public string Text = "";
        /// <summary>A hard limit fired (not a choice).</summary>
        public bool Limit;
    }

    /// <summary>A reason satisfaction moved, shown to the player.</summary>
    [Serializable]
    public sealed class MoodReason
    {
        public string Factor = "";
        public double Delta;
        public long Time;
    }

    /// <summary>One of a person's working days.</summary>
    [Serializable]
    public sealed class WorkDay
    {
        public long Date;
        public decimal Realized, Fees, Wages, Commission;
        public int Trades, Wins, Losses;
        public int MinutesWaiting, MinutesTrading, MinutesTraining;
        public bool LossLimitHit;
    }

    /// <summary>A training course bought for one employee: queued, running or done.</summary>
    [Serializable]
    public sealed class TrainingJob
    {
        public long Id;
        public Skill Skill;
        /// <summary>The level it teaches (1 = the first course).</summary>
        public int Level;
        public decimal Price;
        public int Minutes;
        public double MinutesDone;
        public long Bought, Started, Finished;
        /// <summary>What the course adds to the skill (fixed at purchase, so a reload can't change it).</summary>
        public double Gain;
        public bool Done => Finished != 0;
    }

    /// <summary>
    /// Someone the company employs (or did): the person, their contract, desk account and limits, today's state, the
    /// record of their trading, training and mood. Money on their desk is a real <see cref="Account"/> trading the
    /// shared market through its own <see cref="OrderManager"/>.
    /// </summary>
    public sealed class Employee
    {
        public Person Person;
        public Contract Contract;
        public RiskPolicy Policy = new RiskPolicy();
        public long HiredOn, StartsOn, LeftOn;
        public string LeftReason = "";
        public bool Former => LeftOn != 0;

        // The desk.
        public Account Account;
        public OrderManager Orders;
        /// <summary>Capital the company has put on the desk and not taken back: the floor for profit collection.</summary>
        public decimal Base;
        /// <summary>Cumulative realized trading P&L after execution costs since hiring.</summary>
        public decimal NetRealized;
        /// <summary>Highest <see cref="NetRealized"/> commission has been paid on (starts at 0).</summary>
        public decimal HighWater;
        public decimal CommissionAccrued, WagesAccrued;
        /// <summary>Wages from past paydays the company couldn't pay (owed, not forgiven).</summary>
        public decimal WagesOverdue, CommissionOverdue;
        public decimal TotalCollected, TotalWagesPaid, TotalCommissionPaid;
        /// <summary>The desk (a placed desk's item uid) they work at, or 0.</summary>
        public int Desk;

        // Today.
        public Activity Activity = Activity.OffDuty;
        public BreakKind Break;
        public long ActivitySince;
        public bool LockedToday;
        public string LockReason = "";
        public double Fatigue, Stress;
        /// <summary>0–100.</summary>
        public double Satisfaction = 70;
        public int ConsecutiveLosses, ConsecutiveWins;
        public WorkDay Today;
        public DayPlan Plan;
        /// <summary>Minutes spent waiting for a desk since they were hired (satisfaction reads the recent part).</summary>
        public double WaitingMinutes;
        public int ResignationNotice; // business days left; 0 = none
        public bool RaiseRequested;
        public long MentorOf; // the senior mentoring this one (0 = none)
        /// <summary>Walking over to a newly assigned desk until then (ticks); not saved (a reload seats them).</summary>
        public long SettleUntil;

        public readonly List<TradeRecord> Trades = new List<TradeRecord>();
        public readonly List<BehaviorNote> Behavior = new List<BehaviorNote>();
        public readonly List<MoodReason> Mood = new List<MoodReason>();
        public readonly List<WorkDay> Days = new List<WorkDay>();
        public readonly List<TrainingJob> Training = new List<TrainingJob>();

        /// <summary>The round trip in progress per ticker.</summary>
        internal readonly Dictionary<string, TradeRecord> Open = new Dictionary<string, TradeRecord>();
        /// <summary>The brain's working memory (not saved: rebuilt from the market within a few minutes).</summary>
        internal readonly BrainMemory Memory = new BrainMemory();

        public long Id => Person.Id;
        public string Name => Person.Name;

        public decimal DeskEquity => Account?.Equity ?? 0m;
        public decimal DeskCash => Account?.Cash ?? 0m;
        public decimal Unrealized => Account?.UnrealizedPnL ?? 0m;

        /// <summary>
        /// Realized profit that can move to operating cash: desk cash above the allocated base (losses must be won back
        /// first), and never cash that's posting margin or reserved for working orders.
        /// </summary>
        public decimal Collectible
        {
            get
            {
                if (Account == null) return 0m;
                decimal above = Account.Cash - Base;
                decimal free = Math.Min(Account.Cash, Account.BuyingPower);
                decimal c = Math.Min(above, free);
                return c <= 0m ? 0m : Math.Floor(c * 100m) / 100m;
            }
        }

        /// <summary>Why nothing can be collected (for the disabled button), or null.</summary>
        public string CollectBlocker
        {
            get
            {
                if (Account == null) return "No desk account.";
                if (Collectible > 0m) return null;
                if (Account.Cash <= Base) return Account.Cash < Base
                    ? $"Recover {Money(Base - Account.Cash)} of losses first."
                    : "No realized profit above the allocated capital yet.";
                return "Profit is posting margin for open positions.";
            }
        }

        public TrainingJob CurrentTraining => Training.Find(t => !t.Done);

        public long OpenContracts
        {
            get
            {
                long n = 0;
                if (Account == null) return 0;
                foreach (Position p in Account.Portfolio.Positions) n += Math.Abs(p.Quantity);
                return n;
            }
        }

        public int OpenPositionCount
        {
            get
            {
                int n = 0;
                if (Account == null) return 0;
                foreach (Position p in Account.Portfolio.Positions) if (p.IsOpen) n++;
                return n;
            }
        }

        public bool IsFlat => OpenPositionCount == 0 && (Orders == null || Orders.OpenOrders.Count == 0);

        public void Note(DateTime time, string text, bool limit = false)
        {
            Behavior.Add(new BehaviorNote { Time = time.Ticks, Text = text, Limit = limit });
            if (Behavior.Count > 200) Behavior.RemoveAt(0);
        }

        public void Feel(DateTime time, string factor, double delta)
        {
            if (Math.Abs(delta) < 0.01) return;
            Satisfaction = Math.Max(0, Math.Min(100, Satisfaction + delta));
            // Fold repeats of the same factor on the same day.
            if (Mood.Count > 0)
            {
                MoodReason last = Mood[Mood.Count - 1];
                if (last.Factor == factor && new DateTime(last.Time).Date == time.Date)
                {
                    last.Delta += delta;
                    return;
                }
            }
            Mood.Add(new MoodReason { Factor = factor, Delta = delta, Time = time.Ticks });
            if (Mood.Count > 120) Mood.RemoveAt(0);
        }

        /// <summary>Completed round trips in the last <paramref name="count"/> (0 = all).</summary>
        public List<TradeRecord> Closed(int count = 0)
        {
            var list = Trades.FindAll(t => !t.Open);
            if (count > 0 && list.Count > count) list.RemoveRange(0, list.Count - count);
            return list;
        }

        private static string Money(decimal d) => "$" + d.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Today's timetable for one person (market-local minutes after midnight), drawn at the start of the day.</summary>
    [Serializable]
    public sealed class DayPlan
    {
        public long Date;
        public bool Workday;
        public int LeaveHome, ReachBuilding, Arrive, Leave, ReachHome;
        public int MorningBreak, MorningBreakLength, Lunch, LunchLength, AfternoonBreak, AfternoonBreakLength;
        public int Restroom, Restroom2;
        public BreakKind MorningKind, AfternoonKind;

        public bool OnSite(int minute) => Workday && minute >= Arrive && minute < Leave;

        public BreakKind BreakAt(int minute)
        {
            if (!Workday) return BreakKind.None;
            if (minute >= MorningBreak && minute < MorningBreak + MorningBreakLength) return MorningKind;
            if (minute >= Lunch && minute < Lunch + LunchLength) return BreakKind.Lunch;
            if (minute >= AfternoonBreak && minute < AfternoonBreak + AfternoonBreakLength) return AfternoonKind;
            if (Restroom > 0 && minute >= Restroom && minute < Restroom + 5) return BreakKind.Restroom;
            if (Restroom2 > 0 && minute >= Restroom2 && minute < Restroom2 + 5) return BreakKind.Restroom;
            return BreakKind.None;
        }
    }

    /// <summary>What the brain remembers between minutes: nothing money depends on that isn't in the market or the desk.</summary>
    internal sealed class BrainMemory
    {
        public long LastScanMinute = -1;
        public long LastEntryTicks;
        public int TradesToday;
        public decimal DayStartEquity;
        public bool FlattenedForClose;
        /// <summary>Pending entry waiting for its bracket: order id → plan.</summary>
        public readonly Dictionary<long, EntryPlan> Pending = new Dictionary<long, EntryPlan>();
        /// <summary>Entries delayed by execution (slow hands): submit when the time comes.</summary>
        public readonly List<(long DueTicks, EntryPlan Plan)> Delayed = new List<(long, EntryPlan)>();
        public string LastNote = "";
        public long LastNoteTicks;
        public bool FrequencyNoted, GreedNoted, ImpulseNoted;
    }

    /// <summary>A decided trade before its order fills.</summary>
    internal sealed class EntryPlan
    {
        public string Ticker;
        public Strategy Strategy;
        public int Side;
        public long Contracts;
        public decimal Entry, Stop, Target;
        public double Quality;
        public bool FollowedPlan;
        public string Notes = "";
    }
}
