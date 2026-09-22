using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    // Serialized by value in assets: append only, never reorder.
    public enum CatalystType
    {
        General,
        Earnings,
        Guidance,
        Management,
        Regulatory,
        Product,
        Lawsuit,
        Acquisition,
        Contract,
        Investigation,
        Analyst,
        Offering,
        Buyback,
        Bankruptcy,
        Sector,
        Economic,
        Geopolitical,
    }

    /// <summary>
    /// A sector that moves differently from the rest of the market on a market-wide headline: threats of war sink
    /// the index but lift energy and defence; tariffs on electronics hit tech hardest.
    /// </summary>
    [Serializable]
    public sealed class SectorTilt
    {
        public Sector Sector;

        /// <summary>Expected direction of the sector's own extra move, −1..+1 (on top of the market move).</summary>
        public double Bias;
    }

    public enum NewsScope
    {
        Security,
        Sector,
        Market,
    }

    /// <summary>
    /// A kind of headline and how markets tend to react to it. Reactions are drawn per event: the bias sets the
    /// expected direction, but how much is priced in varies and uncertainty adds noise, so a good headline can
    /// still sell off. Severity 0 = flavour text with no market effect.
    /// </summary>
    [Serializable]
    public sealed class NewsTemplate
    {
        public string Id = "";
        public CatalystType Type;
        public NewsScope Scope;

        /// <summary>Tokens: {company}, {ticker}, {sector}.</summary>
        public string Headline = "";

        /// <summary>Expected direction, −1..+1.</summary>
        public double Bias;

        public double MinSeverity;
        public double MaxSeverity;

        /// <summary>Spread of the realized reaction around the expectation, in units of the event's magnitude.</summary>
        public double Uncertainty = 0.5;

        /// <summary>Relative frequency when picking random news.</summary>
        public double Weight = 1;

        /// <summary>Restricts random security news to these sectors (empty = any).</summary>
        public List<Sector> OnlySectors = new List<Sector>();

        /// <summary>Market-scope only: sectors that get an extra move of their own.</summary>
        public List<SectorTilt> Tilts = new List<SectorTilt>();

        public bool AppliesTo(Sector sector) => OnlySectors == null || OnlySectors.Count == 0 || OnlySectors.Contains(sector);

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("News template id is empty.");
            if (string.IsNullOrWhiteSpace(Headline)) throw new ArgumentException($"News template {Id}: headline is empty.");
            if (Bias < -1 || Bias > 1) throw new ArgumentException($"News template {Id}: Bias must be in [-1, 1].");
            if (MinSeverity < 0 || MaxSeverity > 1 || MinSeverity > MaxSeverity)
                throw new ArgumentException($"News template {Id}: severity must satisfy 0 ≤ Min ≤ Max ≤ 1.");
            if (Uncertainty < 0) throw new ArgumentException($"News template {Id}: Uncertainty cannot be negative.");
            if (Weight < 0) throw new ArgumentException($"News template {Id}: Weight cannot be negative.");
            if (Tilts != null)
                foreach (SectorTilt tilt in Tilts)
                {
                    if (Scope != NewsScope.Market) throw new ArgumentException($"News template {Id}: only market news can tilt sectors.");
                    if (tilt.Bias < -1 || tilt.Bias > 1) throw new ArgumentException($"News template {Id}: tilt bias must be in [-1, 1].");
                }
        }
    }

    /// <summary>A headline pinned to a time (scenario/tutorial content). Random news is planned separately.</summary>
    [Serializable]
    public sealed class ScheduledNews
    {
        /// <summary>Calendar days after the simulation's start date.</summary>
        public int DayOffset;

        public int MinuteOfDay;
        public string TemplateId = "";

        /// <summary>Target for Security-scope templates.</summary>
        public string Ticker = "";

        /// <summary>Target for Sector-scope templates.</summary>
        public Sector Sector;

        /// <summary>0 = draw from the template's range.</summary>
        public double Severity;
    }

    /// <summary>A published headline. What the player sees; the reaction itself stays hidden.</summary>
    public sealed class NewsItem
    {
        public long Id { get; }
        public DateTime Time { get; }
        public string Headline { get; }
        public CatalystType Type { get; }
        public NewsScope Scope { get; }

        /// <summary>Directly affected securities (all members for sector news; empty for market news).</summary>
        public IReadOnlyList<string> Tickers { get; }

        internal double Severity { get; }

        /// <summary>Drawn permanent log-move (per unit beta for sector/market news). Hidden from the player.</summary>
        internal double RealizedMove { get; }

        internal NewsItem(long id, DateTime time, string headline, CatalystType type, NewsScope scope,
            IReadOnlyList<string> tickers, double severity, double realizedMove)
        {
            Id = id;
            Time = time;
            Headline = headline;
            Type = type;
            Scope = scope;
            Tickers = tickers;
            Severity = severity;
            RealizedMove = realizedMove;
        }

        public bool Mentions(string ticker)
        {
            foreach (string t in Tickers)
                if (t == ticker) return true;
            return false;
        }
    }
}
