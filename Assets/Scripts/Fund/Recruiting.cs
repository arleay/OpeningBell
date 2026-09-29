using System;
using System.Collections.Generic;
using OpeningBell.Core;
using OpeningBell.Market;

namespace OpeningBell.Fund
{
    /// <summary>Where a job ad runs: how many and how experienced the people who answer it are, and what it costs.</summary>
    public enum Channel { LocalBoard, IndustryBoard, Headhunter }

    public enum ApplicantStatus { Open, Hired, Declined, Withdrawn, Expired }

    /// <summary>A job ad. Applications arrive while it's up.</summary>
    [Serializable]
    public sealed class Listing
    {
        public long Id;
        public Channel Channel;
        /// <summary>Wanted specialism (null = any), wanted strategy (null = any).</summary>
        public bool AnySector = true, AnyStrategy = true;
        public Sector Sector;
        public Strategy Strategy;
        public long Posted, Expires;
        public int Received;
        public bool Closed;

        public bool Live(DateTime now) => !Closed && now.Ticks < Expires;
    }

    /// <summary>Someone who applied: the person plus what they want and how the talks are going.</summary>
    public sealed class Applicant
    {
        public Person Person;
        public long ListingId;
        public long Received, AvailableFrom, Expires;
        public Contract Asking;
        /// <summary>0–1: how much they want this job (the firm's standing, the fit, how the talks went).</summary>
        public double Interest;
        public int Lowballs, Rounds;
        public ApplicantStatus Status;
        public Contract LastCounter;
        public string LastLine = "";

        public long Id => Person.Id;
    }

    /// <summary>
    /// Makes people. Each applicant is drawn once from the world seed and the application's number, so the same game
    /// always gets the same people, and they never reshuffle when a page opens. Archetypes give real trade-offs: the
    /// sharp analyst with a temper, the careful grinder who trades little, the fast gunslinger who sizes badly.
    /// </summary>
    public static class PeopleFactory
    {
        private static readonly string[] MaleFirst =
        {
            "John", "Marcus", "Daniel", "Ethan", "Rafael", "Owen", "Victor", "Samuel", "Julian", "Theo", "Adrian", "Nikhil", "Kenji",
            "Mateo", "Connor", "Isaac", "Liam", "Dmitri", "Andre", "Tobias", "Hugo", "Felix", "Omar", "Caleb", "Graham", "Wesley",
        };

        private static readonly string[] FemaleFirst =
        {
            "Claire", "Priya", "Elena", "Hannah", "Maya", "Sofia", "Naomi", "Grace", "Leah", "Isabel", "Mei", "Rachel", "Tessa",
            "Amara", "Julia", "Nora", "Vivian", "Lucia", "Harper", "Ingrid", "Camille", "Diane", "Yara", "Simone", "Fiona", "Beatrice",
        };

        private static readonly string[] LastNames =
        {
            "Carter", "Whitfield", "Okafor", "Nakamura", "Delgado", "Brennan", "Castellano", "Lindqvist", "Hargrove", "Patel", "Moreau",
            "Sutherland", "Kowalski", "Reyes", "Achebe", "Fairbanks", "Ashworth", "Vance", "Oduya", "Holloway", "Kerrigan", "Petrov",
            "Laurent", "Mercer", "Quinlan", "Stroud", "Takahashi", "Valdez", "Winslow", "Abernathy", "Blackwood", "Calloway", "Draper",
            "Ellison", "Fontaine", "Gallagher", "Hawthorne", "Iverson", "Jarrett", "Kimura", "Lockhart", "Monroe", "Nash", "Ortega",
        };

        private enum Archetype { Balanced, Analyst, Grinder, Gunslinger, Prodigy, Veteran }

        /// <summary>
        /// A new person. <paramref name="level"/> (0–1) shifts experience and quality up: better channels and a better
        /// reputation reach better people.
        /// </summary>
        public static Person Make(CounterRandom rng, long id, double level, Sector? sector, Strategy? strategy)
        {
            CounterRandom r = rng.Sub(id);
            int k = 0;
            double U() => r.Double(k++);
            double G() => r.Gaussian(k++);

            var p = new Person { Id = id };
            p.Look.Feminine = U() < 0.45;
            string[] firsts = p.Look.Feminine ? FemaleFirst : MaleFirst;
            p.First = firsts[r.Int(firsts.Length, k++)];
            p.Last = LastNames[r.Int(LastNames.Length, k++)];
            // Three in four wear the classic dark suit (FUND_SPEC §19); the rest navy, charcoal or grey.
            double suit = U();
            p.Look.Suit = suit < 0.75 ? SuitColour.Black : suit < 0.85 ? SuitColour.Navy : suit < 0.94 ? SuitColour.Charcoal : SuitColour.Grey;
            p.Look.Skin = 1 + r.Int(5, k++);
            p.Look.Hair = U() < 0.2 ? 6 : 1 + r.Int(5, k++);
            p.Look.Height = (float)Math.Max(1.55, Math.Min(1.88, (p.Look.Feminine ? 1.66 : 1.77) + 0.06 * G()));
            p.Look.Seed = (int)(r.Bits(k++) & 0x7fffffff);

            // Experience: a skewed draw, pushed up by the channel and the firm's name.
            double y = Math.Pow(U(), 1.6 - level) * (4 + 12 * level);
            var arche = (Archetype)r.Int(6, k++);
            if (arche == Archetype.Prodigy) y = Math.Min(y, 1.5);
            if (arche == Archetype.Veteran) y = Math.Max(y, 8 + 4 * U());
            p.Years = (int)Math.Round(y);
            p.Age = 22 + p.Years + r.Int(6, k++);
            p.Specialty = sector ?? (Sector)PickSector(r, k++);
            p.Strategy = strategy ?? (Strategy)r.Int(5, k++);

            // Skills: experience lifts everything; the archetype tilts it; everyone varies.
            double baseline = 30 + 4.2 * Math.Min(p.Years, 10) + 14 * level;
            for (int s = 0; s < Person.SkillCount; s++) p.Skills[s] = baseline + 9 * G();
            void Tilt(Skill s, double by) => p.Skills[(int)s] += by;
            switch (arche)
            {
                case Archetype.Analyst:
                    Tilt(Skill.Analysis, 16); Tilt(Skill.RewardRisk, 8); Tilt(Skill.SelfControl, -18); Tilt(Skill.Stamina, -6);
                    break;
                case Archetype.Grinder:
                    Tilt(Skill.RiskManagement, 15); Tilt(Skill.SelfControl, 14); Tilt(Skill.Timing, 8); Tilt(Skill.Analysis, -6); Tilt(Skill.Execution, -4);
                    break;
                case Archetype.Gunslinger:
                    Tilt(Skill.Execution, 16); Tilt(Skill.Timing, -10); Tilt(Skill.RiskManagement, -16); Tilt(Skill.Adaptability, 6);
                    break;
                case Archetype.Prodigy:
                    Tilt(Skill.Learning, 22); Tilt(Skill.Analysis, 8); Tilt(Skill.SelfControl, -8);
                    break;
                case Archetype.Veteran:
                    Tilt(Skill.Leadership, 18); Tilt(Skill.SelfControl, 8); Tilt(Skill.Learning, -14); Tilt(Skill.Adaptability, -8);
                    break;
            }
            Tilt(Skill.Leadership, -10 + p.Years * 1.5);
            for (int s = 0; s < Person.SkillCount; s++)
            {
                p.Skills[s] = Clamp(p.Skills[s], 8, 88);
                // Headroom: the young and quick learners have the most.
                double room = 12 + 25 * p.Skills[(int)Skill.Learning] / 100.0 - 1.2 * Math.Min(p.Years, 12) + 6 * U();
                p.Caps[s] = Clamp(p.Skills[s] + Math.Max(4, room), 20, 96);
            }

            for (int t = 0; t < Person.TraitCount; t++) p.Traits[t] = Clamp(50 + 20 * G(), 3, 97);
            if (arche == Archetype.Gunslinger) { p.Traits[(int)Trait.Greed] = Clamp(p.Traits[(int)Trait.Greed] + 20, 3, 97); p.Traits[(int)Trait.Flash] += 15; }
            if (arche == Archetype.Grinder) { p.Traits[(int)Trait.Composure] = Clamp(p.Traits[(int)Trait.Composure] + 18, 3, 97); p.Traits[(int)Trait.Caution] += 10; }
            if (arche == Archetype.Analyst) p.Traits[(int)Trait.Composure] = Clamp(p.Traits[(int)Trait.Composure] - 15, 3, 97);
            for (int t = 0; t < Person.TraitCount; t++) p.Traits[t] = Clamp(p.Traits[t], 3, 97);

            // Style and the record they bring (estimates, shown as such).
            double freq = p.Strategy switch
            {
                Strategy.Scalping => 9,
                Strategy.Breakout => 3,
                Strategy.Reversion => 4,
                Strategy.TrendPullback => 3,
                _ => 2.5,
            };
            p.TradesPerDay = Math.Round(freq * (0.7 + 0.6 * (1 - p.Skills[(int)Skill.SelfControl] / 100.0)) * (arche == Archetype.Grinder ? 0.6 : 1.0), 1);
            p.PastWinRate = Clamp(EstimatedWinRate(p) + 0.05 * G(), 0.25, 0.72);
            p.PastRewardRisk = Math.Round(Clamp(0.8 + 1.4 * p.Skills[(int)Skill.RewardRisk] / 100.0 + 0.2 * G(), 0.6, 3.2), 2);

            // Savings and a car to match a trading career so far.
            p.Savings = Math.Round((decimal)(8_000 + 22_000 * p.Years * (0.5 + U())), 0);
            p.Car = VehicleLadder.Affordable(p.Savings, p.Traits[(int)Trait.Flash]);
            return p;
        }

        private static int PickSector(CounterRandom r, long k)
        {
            // The market's sectors with listed stocks, weighted by how many there are.
            Sector[] listed = { Sector.Technology, Sector.Technology, Sector.Technology, Sector.Energy, Sector.Energy, Sector.Industrials,
                Sector.Industrials, Sector.Consumer, Sector.Healthcare, Sector.Communication };
            return (int)listed[r.Int(listed.Length, k)];
        }

        /// <summary>
        /// Estimated win probability under reference conditions (FUND_SPEC §11): a model of the skills, not a promise. Market
        /// analysis carries most of it; timing and specialisation add a little; fatigue and stress (live) aren't in it.
        /// Calibrated so a mid-level trader sits near 45% and the first market-analysis course adds about five points.
        /// </summary>
        public static double EstimatedWinRate(Person p) =>
            Clamp(0.43 + 0.0033 * (p[Skill.Analysis] - 50) + 0.0008 * (p[Skill.Timing] - 50) + 0.0005 * (p[Skill.Specialization] - 50), 0.25, 0.7);

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// The game's cars as rungs of a ladder (the vehicle library's models and prices). People buy the best car their
    /// savings comfortably cover, if they care to spend (Flash).
    /// </summary>
    public static class VehicleLadder
    {
        public static readonly (string Model, decimal Price)[] Cars =
        {
            ("Kestrel Sedan", 24_000m), ("Brisk GTi", 31_000m), ("Ridgeline SUV", 36_000m), ("Vantage GT", 64_000m),
            ("Havoc 707", 72_000m), ("Strata Luxe", 88_000m), ("Ringmark V10", 165_000m), ("Brava Toro", 230_000m),
            ("Aurel Vector", 240_000m), ("Stallion GT3", 320_000m), ("Fulmine SVJ", 420_000m),
        };

        /// <summary>The car they'd own with these savings: "" (transit) if not even the cheapest is comfortable.</summary>
        public static string Affordable(decimal savings, double flash)
        {
            // Frugal people spend a fifth of their savings on a car; show-offs up to 60%.
            decimal budget = savings * (decimal)(0.2 + 0.4 * flash / 100.0);
            string best = "";
            foreach (var (model, price) in Cars)
                if (price <= budget) best = model;
            return best;
        }

        public static decimal PriceOf(string model)
        {
            foreach (var (m, price) in Cars) if (m == model) return price;
            return 0m;
        }
    }
}
