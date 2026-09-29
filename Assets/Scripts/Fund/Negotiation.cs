using System;
using System.Globalization;

namespace OpeningBell.Fund
{
    public enum PayStructure { Hourly, Commission, HourlyPlusCommission }

    /// <summary>
    /// Agreed pay. Locked once signed: changing it takes a new negotiation. Commission is a share of new positive
    /// cumulative net realized trading profit above the employee's high-water mark (FUND_SPEC §3), paid with payroll.
    /// </summary>
    [Serializable]
    public sealed class Contract
    {
        public PayStructure Structure;
        public decimal Hourly;
        /// <summary>Fraction of qualifying profit (0.15 = 15%).</summary>
        public decimal CommissionRate;
        public long Signed;
        public int Version = 1;

        public bool PaysHourly => Structure != PayStructure.Commission && Hourly > 0m;
        public bool PaysCommission => Structure != PayStructure.Hourly && CommissionRate > 0m;

        public Contract Copy() => (Contract)MemberwiseClone();

        public string Describe()
        {
            var c = CultureInfo.InvariantCulture;
            return Structure switch
            {
                PayStructure.Hourly => string.Format(c, "${0:0.00}/h", Hourly),
                PayStructure.Commission => string.Format(c, "{0:0.#}% commission", CommissionRate * 100m),
                _ => string.Format(c, "${0:0.00}/h + {1:0.#}% commission", Hourly, CommissionRate * 100m),
            };
        }

        public static Contract Of(PayStructure s, decimal hourly, decimal rate) => new Contract
        {
            Structure = s,
            Hourly = s == PayStructure.Commission ? 0m : Math.Round(Math.Max(0m, hourly), 2),
            CommissionRate = s == PayStructure.Hourly ? 0m : Math.Round(Math.Max(0m, Math.Min(0.6m, rate)), 3),
        };
    }

    public enum Answer { Accept, Counter, Reject, Withdraw }

    public readonly struct Response
    {
        public readonly Answer Answer;
        public readonly Contract Counter;
        public readonly string Line;

        public Response(Answer answer, Contract counter, string line)
        {
            Answer = answer;
            Counter = counter;
            Line = line;
        }
    }

    /// <summary>
    /// How someone weighs a pay offer. Deterministic: the same person, offer and company always get the same answer.
    /// An offer is valued per year: hourly pay over a 2,000-hour year plus commission on the profit they believe they'd
    /// make, discounted for its uncertainty (more for cautious people). They accept anything at or above their reserve,
    /// counter when it's close, reject lowballs, and walk away after too many.
    /// </summary>
    public static class Negotiation
    {
        public const decimal HoursPerYear = 2000m;

        /// <summary>What the market pays someone with this record per hour (before their own ambition).</summary>
        public static decimal MarketHourly(Person p)
        {
            decimal byYears = p.Years switch
            {
                < 1 => 27m,
                < 3 => 36m,
                < 6 => 52m,
                < 10 => 76m,
                _ => 108m,
            };
            double quality = Math.Max(0.0, Math.Min(1.0, (CoreSkill(p) - 30.0) / 50.0));
            return Math.Round(byYears * (decimal)(0.8 + 0.45 * quality), 2);
        }

        /// <summary>Average of the skills that make money (analysis, timing, management, reward:risk, risk, self-control).</summary>
        public static double CoreSkill(Person p) =>
            (p[Skill.Analysis] + p[Skill.Timing] + p[Skill.TradeManagement] + p[Skill.RewardRisk] + p[Skill.RiskManagement] + p[Skill.SelfControl]) / 6.0;

        /// <summary>What they believe they'd make a year for a desk (drives how they value commission).</summary>
        public static decimal SelfEstimatedProfit(Person p)
        {
            double confidence = 0.8 + 0.4 * p.Trait(Trait.Greed) / 100.0;
            double estimate = 20_000 + 3_600 * Math.Max(0.0, CoreSkill(p) - 30.0) * confidence;
            return Math.Round((decimal)estimate, 0);
        }

        /// <summary>How much of the believed commission they count on (the rest is uncertainty).</summary>
        public static decimal CommissionCredibility(Person p) => (decimal)(0.85 - 0.3 * p.Trait(Trait.Caution) / 100.0);

        /// <summary>A year of this contract to this person.</summary>
        public static decimal Value(Person p, Contract c)
        {
            decimal v = 0m;
            if (c.Structure != PayStructure.Commission) v += c.Hourly * HoursPerYear;
            if (c.Structure != PayStructure.Hourly) v += c.CommissionRate * SelfEstimatedProfit(p) * CommissionCredibility(p);
            return v;
        }

        /// <summary>
        /// The pay they ask for: their preferred structure at the market rate plus their ambition, plus a premium for a
        /// well-known firm's standards (better firms attract better people who expect more).
        /// </summary>
        public static Contract Asking(Person p, double reputation)
        {
            decimal ambition = 1m + (decimal)(p.Trait(Trait.Ambition) / 100.0 * 0.25);
            decimal hourly = MarketHourly(p) * ambition * (decimal)(1.0 + 0.1 * reputation / 100.0);
            decimal rate = (decimal)(0.08 + 0.12 * Math.Min(1.0, p.Years / 10.0) + 0.06 * p.Trait(Trait.Greed) / 100.0);
            PayStructure s = Preferred(p);
            // Commission-only people want the salary's worth back in a bigger share.
            if (s == PayStructure.Commission) rate += 0.1m;
            if (s == PayStructure.HourlyPlusCommission) hourly *= 0.8m;
            return Contract.Of(s, Math.Round(hourly, 2), Math.Round(rate, 2));
        }

        /// <summary>Juniors want security; confident seniors want a share.</summary>
        public static PayStructure Preferred(Person p)
        {
            double greed = p.Trait(Trait.Greed), caution = p.Trait(Trait.Caution);
            if (p.Years < 3 || caution > 70) return PayStructure.Hourly;
            if (greed > 70 && CoreSkill(p) > 60) return PayStructure.Commission;
            return PayStructure.HourlyPlusCommission;
        }

        /// <summary>
        /// Below this yearly value they won't sign. Keen people (interest near 1) give more ground; ambitious ones less.
        /// </summary>
        public static decimal Reserve(Person p, Contract asking, double interest)
        {
            double flexibility = 0.06 + 0.14 * Math.Max(0.0, Math.Min(1.0, interest)) - 0.06 * p.Trait(Trait.Ambition) / 100.0;
            return Value(p, asking) * (decimal)(1.0 - Math.Max(0.02, flexibility));
        }

        /// <summary>
        /// Their answer to <paramref name="offer"/>. <paramref name="lowballs"/> counts earlier offers they found
        /// unreasonable; <paramref name="limit"/> of them and they withdraw.
        /// </summary>
        public static Response Respond(Person p, Contract offer, Contract asking, double interest, int lowballs, int limit)
        {
            decimal value = Value(p, offer), reserve = Reserve(p, asking, interest), ask = Value(p, asking);
            var c = CultureInfo.InvariantCulture;
            if (value >= reserve)
                return new Response(Answer.Accept, null, value >= ask ? "That works for me. When do I start?" : "It's a little under what I hoped, but I'm in.");
            if (value >= reserve * 0.85m)
            {
                // Meet in the middle, in the structure they offered where it makes sense, else their own.
                Contract counter = Midpoint(offer, asking, p);
                return new Response(Answer.Counter, counter, string.Format(c, "I can't do that, but I'd sign for {0}.", counter.Describe()));
            }
            if (lowballs + 1 >= limit)
                return new Response(Answer.Withdraw, null, "I don't think we're going to get there. I'll look elsewhere.");
            return new Response(Answer.Reject, null, value < reserve * 0.6m ? "That's well below what I'm worth. No." : "That's too low for me.");
        }

        /// <summary>Halfway between an offer and their ask, in the offered structure (with its commission floor honoured).</summary>
        public static Contract Midpoint(Contract offer, Contract asking, Person p)
        {
            decimal target = (Value(p, offer) + Value(p, asking)) / 2m;
            PayStructure s = offer.Structure;
            decimal profit = SelfEstimatedProfit(p) * CommissionCredibility(p);
            switch (s)
            {
                case PayStructure.Hourly:
                    return Contract.Of(s, RoundUp(target / HoursPerYear, 0.25m), 0m);
                case PayStructure.Commission:
                    return Contract.Of(s, 0m, profit <= 0m ? 0.3m : RoundUp(target / profit, 0.005m));
                default:
                {
                    // Keep their offered commission (at least), make up the rest in hourly.
                    decimal rate = Math.Max(offer.CommissionRate, asking.Structure != PayStructure.Hourly ? asking.CommissionRate * 0.75m : 0.08m);
                    decimal hourly = Math.Max(0m, (target - rate * profit) / HoursPerYear);
                    return Contract.Of(s, RoundUp(hourly, 0.25m), rate);
                }
            }
        }

        /// <summary>
        /// A change to a signed contract. A raise or a structure change of equal value is accepted; a cut only by a
        /// content employee and only a small one, and it costs satisfaction either way (the caller applies that).
        /// </summary>
        public static Response RespondToChange(Person p, Contract current, Contract offer, double satisfaction)
        {
            decimal now = Value(p, current), then = Value(p, offer);
            if (then >= now * 0.995m) return new Response(Answer.Accept, null, then > now ? "Thank you, I appreciate it." : "Fine by me.");
            decimal cut = 1m - then / Math.Max(1m, now);
            if (cut <= 0.05m && satisfaction >= 70) return new Response(Answer.Accept, null, "I don't love it, but I understand. Okay.");
            return new Response(Answer.Reject, null, cut > 0.2m ? "That's a serious pay cut. I won't sign that." : "I'd rather keep my current terms.");
        }

        private static decimal RoundUp(decimal v, decimal step) => Math.Ceiling(v / step) * step;
    }
}
