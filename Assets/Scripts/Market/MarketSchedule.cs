using System;

namespace OpeningBell.Market
{
    public enum MarketSession
    {
        Closed,
        Premarket,
        Regular,
        AfterHours,
    }

    public sealed class MarketSchedule
    {
        public TimeSpan PremarketOpen { get; }
        public TimeSpan RegularOpen { get; }
        public TimeSpan RegularClose { get; }
        public TimeSpan AfterHoursClose { get; }

        public double RegularSessionSeconds => (RegularClose - RegularOpen).TotalSeconds;

        public MarketSchedule(MarketConfig config)
        {
            PremarketOpen = TimeSpan.FromMinutes(config.PremarketOpenMinute);
            RegularOpen = TimeSpan.FromMinutes(config.RegularOpenMinute);
            RegularClose = TimeSpan.FromMinutes(config.RegularCloseMinute);
            AfterHoursClose = TimeSpan.FromMinutes(config.AfterHoursCloseMinute);
        }

        // TODO: market holidays.
        public bool IsTradingDay(DateTime date) =>
            date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

        public MarketSession GetSession(DateTime time)
        {
            if (!IsTradingDay(time.Date)) return MarketSession.Closed;
            TimeSpan t = time.TimeOfDay;
            if (t < PremarketOpen) return MarketSession.Closed;
            if (t < RegularOpen) return MarketSession.Premarket;
            if (t < RegularClose) return MarketSession.Regular;
            if (t < AfterHoursClose) return MarketSession.AfterHours;
            return MarketSession.Closed;
        }

        /// <summary>First premarket open at or after <paramref name="time"/>.</summary>
        public DateTime NextSessionStart(DateTime time)
        {
            DateTime sameDay = time.Date + PremarketOpen;
            if (IsTradingDay(time.Date) && sameDay >= time) return sameDay;

            DateTime day = time.Date.AddDays(1);
            while (!IsTradingDay(day)) day = day.AddDays(1);
            return day + PremarketOpen;
        }
    }
}
