using System;

namespace OpeningBell.Market
{
    internal readonly struct ActivityProfile
    {
        public readonly double Volatility;
        public readonly double Volume;

        public ActivityProfile(double volatility, double volume)
        {
            Volatility = volatility;
            Volume = volume;
        }
    }

    /// <summary>Time-of-day multipliers for volatility and volume.</summary>
    internal static class IntradayProfile
    {
        public static ActivityProfile Evaluate(MarketConfig c, MarketSchedule schedule, MarketSession session, DateTime time)
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
