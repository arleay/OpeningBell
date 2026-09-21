using System;
using OpeningBell.Market;

namespace OpeningBell.Gameplay
{
    /// <summary>When the player may sleep and when they wake. Pure so it can be unit-tested.</summary>
    public static class SleepRules
    {
        /// <summary>Bedtime window: from <paramref name="earliestBedtime"/> until the next morning's wake time.</summary>
        public static bool CanSleep(DateTime now, TimeSpan earliestBedtime, TimeSpan wakeTime) =>
            now.TimeOfDay >= earliestBedtime || now.TimeOfDay < wakeTime;

        /// <summary>Next wake-up on a trading day. Weekends are slept through, so every morning has a market.</summary>
        public static DateTime NextWake(DateTime now, TimeSpan wakeTime, MarketSchedule schedule)
        {
            DateTime day = now.TimeOfDay < wakeTime ? now.Date : now.Date.AddDays(1);
            while (!schedule.IsTradingDay(day)) day = day.AddDays(1);
            return day + wakeTime;
        }
    }
}
