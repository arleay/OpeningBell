using System;

namespace OpeningBell.Market
{
    /// <summary>
    /// Market tuning values. "Daily" volatility means close-to-close (the engine normalizes the intraday
    /// profile and overnight gap to it); other "PerDay" rates are per regular session (6.5h by default).
    /// Serializable so it can live in a settings asset for balancing.
    /// </summary>
    [Serializable]
    public sealed class MarketConfig
    {
        public int TickSeconds = 2;

        public int PremarketOpenMinute = 4 * 60;
        public int RegularOpenMinute = 9 * 60 + 30;
        public int RegularCloseMinute = 16 * 60;
        public int AfterHoursCloseMinute = 20 * 60;

        public double MarketDailyDrift = 0.0003;
        public double MarketDailyVolatility = 0.009;
        public double SectorDailyVolatility = 0.007;

        /// <summary>Size of mean-reverting intraday noise relative to a stock's DailyVolatility.</summary>
        public double TransientNoiseRatio = 0.7;
        public double MomentumHalfLifeMinutes = 5;

        // Volatility clustering: log-activity follows an OU process shared by vol and volume.
        public double ActivityReversionPerDay = 3;
        public double ActivityVolOfVol = 1.2;

        public double OvernightVolatilityRatio = 0.5;
        public double OvernightDeviationCarry = 0.5;

        // Regular session U-shape: multiplier is Floor at midday, Edge at open/close (mean ≈ 1).
        public double RegularVolFloor = 0.6;
        public double RegularVolEdge = 1.8;
        public double RegularVolumeFloor = 0.45;
        public double RegularVolumeEdge = 2.1;
        public double OpenBurstVol = 0.8;
        public double OpenBurstVolume = 1.5;
        public double OpenBurstMinutes = 8;

        public double PremarketVol = 0.35;
        public double PremarketVolume = 0.04;
        public double AfterHoursVol = 0.3;
        public double AfterHoursVolume = 0.03;
        public double ExtendedSpreadMultiplier = 3;

        /// <summary>Displayed size at the inside quote as a fraction of average daily volume.</summary>
        public double DepthFractionOfAdv = 0.0004;
        public double DepthNoise = 0.5;
        public double VolumeNoise = 0.6;

        public int MaxCandlesPerSeries = 5000;

        public MarketConfig Clone() => (MarketConfig)MemberwiseClone();

        public void Validate()
        {
            if (TickSeconds <= 0 || 60 % TickSeconds != 0)
                throw new ArgumentException("TickSeconds must divide 60 so ticks stay aligned to minute boundaries.");
            if (!(0 <= PremarketOpenMinute && PremarketOpenMinute < RegularOpenMinute &&
                  RegularOpenMinute < RegularCloseMinute && RegularCloseMinute < AfterHoursCloseMinute &&
                  AfterHoursCloseMinute <= 24 * 60))
                throw new ArgumentException("Session minutes must be ordered premarket < open < close < after-hours close.");
            if (ActivityReversionPerDay <= 0) throw new ArgumentException("ActivityReversionPerDay must be positive.");
            if (MomentumHalfLifeMinutes <= 0) throw new ArgumentException("MomentumHalfLifeMinutes must be positive.");
            if (MaxCandlesPerSeries < 10) throw new ArgumentException("MaxCandlesPerSeries is too small.");
        }
    }
}
