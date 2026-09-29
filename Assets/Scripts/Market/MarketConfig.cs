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
        public double OpenBurstVolume = 1.0;
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

        // News (only active when the simulation is given templates).
        public double SecurityNewsPerDay = 5;
        public double SectorNewsPerDay = 0.5;
        /// <summary>Macro and geopolitical headlines: roughly one every other trading day.</summary>
        public double MarketNewsPerDay = 0.6;

        /// <summary>Move at severity 1, in multiples of the target's daily volatility.</summary>
        public double NewsImpactDailyVols = 4;

        /// <summary>Share of the permanent move that lands instantly; the rest arrives over NewsDeliveryMinutes.</summary>
        public double NewsImmediateFraction = 0.35;
        public double NewsDeliveryMinutes = 15;

        /// <summary>Extra volume at severity 1 (volume × (1 + boost·severity), volatility × its square root).</summary>
        public double NewsActivityBoost = 3;
        public double NewsActivityHalfLifeMinutes = 120;

        /// <summary>
        /// Time-of-day windows layered on the U-shape (see <see cref="TimeMacro"/>). They change participation, never
        /// direction. Minutes of the day, Eastern; the first window containing a time applies.
        /// </summary>
        public TimeMacro[] TimeMacros = DefaultTimeMacros();

        /// <summary>
        /// The brief's windows. Premarket builds toward the bell (catalysts from 8:00, institutional prep and tighter
        /// spreads from 9:00, queued orders from 9:25); the opening 15 minutes have a thin book and busy institutions;
        /// midday is slow with fewer institutions; 15:00 on brings rebalancing and 15:50 the closing urgency.
        /// Volume and volatility weights are chosen so each session's average stays near 1 (the engine also
        /// renormalises volatility to the spec).
        /// </summary>
        public static TimeMacro[] DefaultTimeMacros() => new[]
        {
            new TimeMacro(4 * 60, 8 * 60, volume: 0.75, volatility: 0.85, spread: 1.2),
            new TimeMacro(8 * 60, 9 * 60, volume: 1.3, volatility: 1.15),
            new TimeMacro(9 * 60, 9 * 60 + 25, volume: 1.7, volatility: 1.1, liquidity: 1.3, institutional: 1.5, spread: 0.8),
            new TimeMacro(9 * 60 + 25, 9 * 60 + 30, volume: 2.0, volatility: 0.9, liquidity: 1.4, institutional: 1.5, spread: 0.75),
            new TimeMacro(9 * 60 + 30, 9 * 60 + 45, liquidity: 0.8, institutional: 1.3),
            new TimeMacro(9 * 60 + 45, 10 * 60 + 30, institutional: 1.2),
            new TimeMacro(11 * 60 + 30, 13 * 60 + 30, volume: 0.9, volatility: 0.9, liquidity: 1.1, institutional: 0.7),
            new TimeMacro(15 * 60, 15 * 60 + 50, volume: 1.05, institutional: 1.3),
            new TimeMacro(15 * 60 + 50, 16 * 60, volume: 1.25, liquidity: 0.9, institutional: 1.8),
            new TimeMacro(16 * 60, 16 * 60 + 30, volume: 1.8, volatility: 1.2),
            new TimeMacro(16 * 60 + 30, 20 * 60, volume: 0.85, volatility: 0.95),
        };

        public MarketConfig Clone()
        {
            var c = (MarketConfig)MemberwiseClone();
            c.TimeMacros = (TimeMacro[])TimeMacros?.Clone();
            return c;
        }

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
            if (TimeMacros != null)
                foreach (TimeMacro m in TimeMacros)
                    if (m.EndMinute <= m.StartMinute || m.Volume <= 0 || m.Volatility <= 0 || m.Liquidity <= 0 || m.Institutional < 0 || m.Spread <= 0)
                        throw new ArgumentException("Time macros need a positive window and positive multipliers.");
            if (NewsDeliveryMinutes <= 0 || NewsActivityHalfLifeMinutes <= 0)
                throw new ArgumentException("News delivery time and attention half-life must be positive.");
        }
    }

    /// <summary>
    /// A time-of-day window: multipliers on volume, volatility, book depth, institutional arrivals and spread.
    /// Never directional ("10 AM changes participation", not "10 AM reverses").
    /// </summary>
    [Serializable]
    public struct TimeMacro
    {
        public int StartMinute, EndMinute;
        public double Volume, Volatility, Liquidity, Institutional, Spread;

        public TimeMacro(int startMinute, int endMinute, double volume = 1, double volatility = 1, double liquidity = 1,
            double institutional = 1, double spread = 1)
        {
            StartMinute = startMinute;
            EndMinute = endMinute;
            Volume = volume;
            Volatility = volatility;
            Liquidity = liquidity;
            Institutional = institutional;
            Spread = spread;
        }
    }
}
