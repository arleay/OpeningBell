using System;

namespace OpeningBell.Fund
{
    /// <summary>One training category: what it changes and how it's priced (FUND_SPEC §6, brief §15–16).</summary>
    public sealed class Course
    {
        public Skill Skill;
        public string Description, Effect;
        public int MaxLevel = 5;
        /// <summary>Price and duration relative to market analysis.</summary>
        public double PriceFactor = 1, TimeFactor = 1;
    }

    /// <summary>
    /// The courses. Each changes a different part of how a trader decides (see <see cref="TraderBrain"/>), none is a
    /// straight win-rate bonus. Levels get dearer and longer; gains shrink near the person's ceiling.
    /// </summary>
    public static class TrainingCatalog
    {
        public static readonly Course[] Courses =
        {
            new Course { Skill = Skill.Analysis, Description = "Reading the tape, levels, volume and news: judging how good a setup really is.",
                Effect = "Sharper setup judgement: fewer poor trades mistaken for good ones. Raises the estimated baseline win rate." },
            new Course { Skill = Skill.Timing, Description = "Waiting for the entry: patience at levels, not chasing the first move.",
                Effect = "Higher bar before entering and less chasing; better entry prices.", PriceFactor = 0.8 },
            new Course { Skill = Skill.TradeManagement, Description = "Stops at structure, moving to breakeven, scaling out at the plan.",
                Effect = "Better stop placement and profit-taking once in a trade.", PriceFactor = 0.85 },
            new Course { Skill = Skill.RewardRisk, Description = "Only trades whose target is worth the risk.",
                Effect = "Skips trades with thin reward:risk; plans bigger targets.", PriceFactor = 0.7 },
            new Course { Skill = Skill.RiskManagement, Description = "Sizing to the risk budget and the account, never to a feeling.",
                Effect = "Consistent size within company limits; less oversizing after wins.", PriceFactor = 0.7 },
            new Course { Skill = Skill.SelfControl, Description = "Coaching on revenge trading, greed and fear.",
                Effect = "Less overtrading after losses, fewer greed trades, fewer early exits.", PriceFactor = 0.9, TimeFactor = 1.2 },
            new Course { Skill = Skill.Execution, Description = "Order-entry drills and platform shortcuts.",
                Effect = "Faster, cleaner order entry: fewer delays and fewer market orders by mistake.", PriceFactor = 0.5, TimeFactor = 0.7 },
            new Course { Skill = Skill.Adaptability, Description = "Regime changes: trending, choppy and news-driven days.",
                Effect = "Trades less when the day doesn't suit the strategy.", PriceFactor = 0.8 },
            new Course { Skill = Skill.Specialization, Description = "Deep study of their own sector and playbook.",
                Effect = "Clearer judgement in their specialty sector and strategy.", PriceFactor = 0.75 },
            new Course { Skill = Skill.Stamina, Description = "Routines for a full day of focus.",
                Effect = "Slower fatigue through the day, so afternoon judgement holds up.", PriceFactor = 0.5, TimeFactor = 0.8 },
            new Course { Skill = Skill.Learning, Description = "How to study: note-taking, review, spaced practice.",
                Effect = "Future courses take less time (at most a quarter less).", MaxLevel = 3, PriceFactor = 0.6 },
            new Course { Skill = Skill.Leadership, Description = "Mentoring and running a small desk.",
                Effect = "Can mentor more juniors (one per level): mentees slowly improve analysis and self-control.", PriceFactor = 0.9 },
        };

        public static Course For(Skill s) => Array.Find(Courses, c => c.Skill == s);

        /// <summary>Levels already taken in a skill (the next course is this + 1).</summary>
        public static int LevelsTaken(Employee e, Skill s)
        {
            int n = 0;
            foreach (TrainingJob j in e.Training) if (j.Skill == s) n++;
            return n;
        }

        public static decimal Price(FundConfig cfg, Skill s, int level)
        {
            Course c = For(s);
            double p = (double)cfg.FirstTrainingPrice * c.PriceFactor * Math.Pow(cfg.TrainingPriceGrowth, level - 1);
            return Math.Round((decimal)p / 500m) * 500m;
        }

        /// <summary>In-game minutes: longer each level, up to a quarter shorter for quick learners.</summary>
        public static int Minutes(FundConfig cfg, Skill s, int level, Person p)
        {
            Course c = For(s);
            double speed = 1.0 - 0.25 * Math.Min(1.0, p[Skill.Learning] / 100.0);
            return Math.Max(1, (int)Math.Round(cfg.FirstTrainingMinutes * c.TimeFactor * Math.Pow(cfg.TrainingTimeGrowth, level - 1) * speed));
        }

        /// <summary>
        /// Points the course adds: a base per level, shrinking as the skill nears 100, never past their ceiling. The first
        /// market-analysis course at a typical 45 lands about 15 points (≈ +5 points of estimated win rate).
        /// </summary>
        public static double Gain(Person p, Skill s, int level)
        {
            double[] baseGain = { 24, 20, 16, 12, 9 };
            double b = baseGain[Math.Max(0, Math.Min(baseGain.Length - 1, level - 1))];
            double current = p[s];
            double gain = b * Math.Max(0.1, 1.0 - current / 120.0);
            return Math.Max(0, Math.Min(gain, p.Caps[(int)s] - current));
        }
    }

    /// <summary>The career milestone for business ownership (a foundation the game didn't have).</summary>
    public static class CareerProgress
    {
        /// <summary>Why a fund can't be formed yet, or null.</summary>
        public static string Blocker(FundConfig cfg, int tradingDays, decimal provenProfit)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (tradingDays < cfg.RequiredTradingDays)
                return string.Format(c, "Trade {0} more day{1} first ({2} of {3} completed).", cfg.RequiredTradingDays - tradingDays,
                    cfg.RequiredTradingDays - tradingDays == 1 ? "" : "s", tradingDays, cfg.RequiredTradingDays);
            if (provenProfit < cfg.RequiredProvenProfit)
                return string.Format(c, "Show ${0:N0} of proven trading profit first (you have ${1:N0}).", cfg.RequiredProvenProfit, Math.Max(0m, provenProfit));
            return null;
        }
    }
}
