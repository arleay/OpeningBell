using System;

namespace OpeningBell.Market
{
    internal readonly struct ActivityProfile
    {
        public readonly double Volatility;
        public readonly double Volume;
        /// <summary>Book depth, institutional arrivals and spread relative to normal (time macros).</summary>
        public readonly double Liquidity, Institutional, Spread;

        public ActivityProfile(double volatility, double volume, double liquidity = 1, double institutional = 1, double spread = 1)
        {
            Volatility = volatility;
            Volume = volume;
            Liquidity = liquidity;
            Institutional = institutional;
            Spread = spread;
        }
    }

    /// <summary>Time-of-day multipliers: the session's U-shape, then the configured time macros on top.</summary>
    internal static class IntradayProfile
    {
        public static ActivityProfile Evaluate(MarketConfig c, MarketSchedule schedule, MarketSession session, DateTime time)
        {
            ActivityProfile shape = Shape(c, schedule, session, time);
            if (c.TimeMacros == null || session == MarketSession.Closed) return shape;
            double minute = time.TimeOfDay.TotalMinutes;
            foreach (TimeMacro m in c.TimeMacros)
                if (minute >= m.StartMinute && minute < m.EndMinute)
                    return new ActivityProfile(shape.Volatility * m.Volatility, shape.Volume * m.Volume, m.Liquidity, m.Institutional, m.Spread);
            return shape;
        }

        private static ActivityProfile Shape(MarketConfig c, MarketSchedule schedule, MarketSession session, DateTime time)
        {
            switch (session)
            {
                case MarketSession.Regular:
                {
                    double elapsed = (time.TimeOfDay - schedule.RegularOpen).TotalSeconds;
                    double u = elapsed / schedule.RegularSessionSeconds;
                    // (2u−1)² is 1 at open/close and 0 at midday, averaging 1/3 → the classic U-shape.
                    double edge = (2 * u - 1) * (2 * u - 1);
                    double burst = Math.Exp(-elapsed / (c.OpenBurstMinutes * 60));
                    return new ActivityProfile(
                        c.RegularVolFloor + (c.RegularVolEdge - c.RegularVolFloor) * edge + c.OpenBurstVol * burst,
                        c.RegularVolumeFloor + (c.RegularVolumeEdge - c.RegularVolumeFloor) * edge + c.OpenBurstVolume * burst);
                }
                case MarketSession.AfterHours:
                    return new ActivityProfile(c.AfterHoursVol, c.AfterHoursVolume);
                default:
                    // Premarket; also used to shape quotes displayed while closed.
                    return new ActivityProfile(c.PremarketVol, c.PremarketVolume);
            }
        }
    }
}
