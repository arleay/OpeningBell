using System;

namespace OpeningBell.Market
{
    /// <summary>
    /// Static definition of a security. Serialized inside SecurityDefinition assets; the simulation works on a
    /// private copy, so live values never write back into a definition.
    /// </summary>
    [Serializable]
    public sealed class SecuritySpec
    {
        public string Ticker = "";
        public string CompanyName = "";
        public Sector Sector;
        public double BasePrice = 10;

        /// <summary>Typical close-to-close idiosyncratic move (0.03 = 3%), including extended hours and overnight gap.</summary>
        public double DailyVolatility = 0.03;

        public long AverageDailyVolume = 1_000_000;
        public long SharesOutstanding = 100_000_000;
        public long FloatShares = 80_000_000;

        /// <summary>Typical regular-hours quoted spread, in basis points of price.</summary>
        public double BaseSpreadBps = 5;

        public double MarketBeta = 1;
        public double SectorBeta = 1;

        /// <summary>Feedback of recent returns into price. 0 = none; must stay below 1.</summary>
        public double MomentumCoefficient = 0.1;

        /// <summary>Decay rate of transient mispricing, per session. 15 ≈ 18-minute half-life.</summary>
        public double MeanReversionPerDay = 15;

        public SecuritySpec Clone() => (SecuritySpec)MemberwiseClone();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Ticker)) throw new ArgumentException("Security ticker is empty.");
            string p = $"Security {Ticker}: ";
            if (BasePrice <= 0) throw new ArgumentException(p + "BasePrice must be positive.");
            if (DailyVolatility < 0) throw new ArgumentException(p + "DailyVolatility cannot be negative.");
            if (AverageDailyVolume <= 0) throw new ArgumentException(p + "AverageDailyVolume must be positive.");
            if (FloatShares <= 0 || FloatShares > SharesOutstanding) throw new ArgumentException(p + "FloatShares must be in (0, SharesOutstanding].");
            if (BaseSpreadBps < 0) throw new ArgumentException(p + "BaseSpreadBps cannot be negative.");
            if (MomentumCoefficient < 0 || MomentumCoefficient >= 1) throw new ArgumentException(p + "MomentumCoefficient must be in [0, 1).");
            if (MeanReversionPerDay < 0) throw new ArgumentException(p + "MeanReversionPerDay cannot be negative.");
        }
    }

    [Serializable]
    public sealed class IndexSpec
    {
        public string Ticker = "CMPX";
        public string Name = "Composite Index";
        public double BaseLevel = 4800;

        public IndexSpec Clone() => (IndexSpec)MemberwiseClone();
    }
}
