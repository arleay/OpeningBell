using System;
using System.Collections.Generic;
using OpeningBell.Market;

namespace OpeningBell.Fund
{
    /// <summary>What a trader can get better at (0–100 each). Order is saved: append only.</summary>
    public enum Skill
    {
        /// <summary>Reading the information in front of them: how accurately they judge a setup.</summary>
        Analysis,
        /// <summary>Patience: waiting for the entry instead of taking the first thing that moves.</summary>
        Timing,
        /// <summary>Stops at structure, moving to breakeven, taking profit at the plan.</summary>
        TradeManagement,
        /// <summary>Only taking trades whose target is worth the risk.</summary>
        RewardRisk,
        /// <summary>Sizing to the risk budget instead of to a feeling.</summary>
        RiskManagement,
        /// <summary>Resisting revenge trades, greed and fear.</summary>
        SelfControl,
        /// <summary>Fast, correct order entry.</summary>
        Execution,
        /// <summary>Recognising when the market's character changes.</summary>
        Adaptability,
        /// <summary>Depth in their own sector and strategy.</summary>
        Specialization,
        /// <summary>Holding focus through a whole day.</summary>
        Stamina,
        /// <summary>How fast training sinks in (capped).</summary>
        Learning,
        /// <summary>Mentoring juniors.</summary>
        Leadership,
    }

    /// <summary>Personality (0–100 each). Order is saved: append only.</summary>
    public enum Trait
    {
        /// <summary>Wants more money; weighs pay heavily.</summary>
        Ambition,
        /// <summary>Stays through rough patches.</summary>
        Loyalty,
        /// <summary>Chats, takes coffee with others.</summary>
        Sociability,
        /// <summary>Stress builds slowly.</summary>
        Composure,
        /// <summary>Keeps pushing after a good day.</summary>
        Greed,
        /// <summary>Snatches profits early.</summary>
        Caution,
        /// <summary>Spends on cars and watches.</summary>
        Flash,
    }

    public enum Strategy { Breakout, Reversion, TrendPullback, NewsMomentum, Scalping }

    public enum SuitColour { Black, Charcoal, Navy, Grey }

    /// <summary>How an employee looks: which of the art library's suits, tints. Kept for life.</summary>
    [Serializable]
    public sealed class PersonLook
    {
        public bool Feminine;
        public SuitColour Suit;
        public int Skin, Hair;
        /// <summary>Height in metres (1.55–1.85).</summary>
        public float Height = 1.72f;
        /// <summary>Stable seed for anything else about the body (idle phase, small variation).</summary>
        public int Seed;
    }

    /// <summary>One line of a person's history with the company (hired, raise, trained, resigned...).</summary>
    [Serializable]
    public sealed class HistoryEntry
    {
        public long Time;
        public string Text;
    }

    /// <summary>
    /// A persistent person in the world: an applicant, then maybe an employee, then maybe a former employee. Identity
    /// (name, look), background, skills and personality never reshuffle.
    /// </summary>
    [Serializable]
    public sealed class Person
    {
        public long Id;
        public string First = "", Last = "";
        public int Age;
        public PersonLook Look = new PersonLook();
        /// <summary>Years of trading experience before joining.</summary>
        public int Years;
        public Sector Specialty;
        public Strategy Strategy;
        public double[] Skills = new double[SkillCount];
        /// <summary>How far each skill can go with training (their ceiling).</summary>
        public double[] Caps = new double[SkillCount];
        public double[] Traits = new double[TraitCount];
        /// <summary>Their own money (from pay and commissions, less living), and the car they drive ("" = none: transit).</summary>
        public decimal Savings;
        public string Car = "";
        /// <summary>Estimated record before joining (labelled as an estimate everywhere it's shown).</summary>
        public double PastWinRate, PastRewardRisk;
        /// <summary>Typical trades a day (their style).</summary>
        public double TradesPerDay;
        public List<HistoryEntry> History = new List<HistoryEntry>();

        public const int SkillCount = 12, TraitCount = 7;

        public string Name => First + " " + Last;
        public string Initials => (First.Length > 0 ? First.Substring(0, 1) : "") + (Last.Length > 0 ? Last.Substring(0, 1) : "");
        public double this[Skill s] => Skills[(int)s];
        public double Trait(Trait t) => Traits[(int)t];

        public void Note(DateTime time, string text) => History.Add(new HistoryEntry { Time = time.Ticks, Text = text });

        /// <summary>Title from experience.</summary>
        public string Seniority => Years switch
        {
            < 1 => "Graduate trader",
            < 3 => "Junior trader",
            < 6 => "Trader",
            < 10 => "Senior trader",
            _ => "Veteran trader",
        };

        public static string SkillName(Skill s) => s switch
        {
            Skill.Analysis => "Market analysis",
            Skill.Timing => "Entry timing",
            Skill.TradeManagement => "Trade management",
            Skill.RewardRisk => "Reward-to-risk planning",
            Skill.RiskManagement => "Risk management",
            Skill.SelfControl => "Self-control",
            Skill.Execution => "Execution",
            Skill.Adaptability => "Adaptability",
            Skill.Specialization => "Specialization",
            Skill.Stamina => "Focus and stamina",
            Skill.Learning => "Learning efficiency",
            Skill.Leadership => "Leadership",
            _ => s.ToString(),
        };

        public static string StrategyName(Strategy s) => s switch
        {
            Strategy.Breakout => "Opening-range breakouts",
            Strategy.Reversion => "VWAP mean reversion",
            Strategy.TrendPullback => "Trend pullbacks",
            Strategy.NewsMomentum => "News momentum",
            Strategy.Scalping => "Scalping",
            _ => s.ToString(),
        };

        public static string SectorName(Sector s) => s switch
        {
            Sector.Technology => "Technology",
            Sector.Healthcare => "Healthcare",
            Sector.Energy => "Energy",
            Sector.Consumer => "Consumer",
            Sector.Industrials => "Industrials",
            Sector.Financials => "Financials",
            Sector.Communication => "Communication",
            _ => s.ToString(),
        };

        public static string SuitName(SuitColour c) => c switch
        {
            SuitColour.Black => "black suit",
            SuitColour.Charcoal => "charcoal suit",
            SuitColour.Navy => "navy suit",
            _ => "grey suit",
        };
    }
}
