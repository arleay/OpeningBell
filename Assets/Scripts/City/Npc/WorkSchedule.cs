using System;

namespace OpeningBell.City
{
    /// <summary>Opening hours or a work shift, in minutes after midnight. Close past midnight wraps (e.g. 6:00–1:00).</summary>
    [Serializable]
    public struct Hours
    {
        public int Open, Close;

        public Hours(int openMinute, int closeMinute)
        {
            Open = openMinute;
            Close = closeMinute;
        }

        public static Hours Of(double openHour, double closeHour) => new Hours((int)(openHour * 60), (int)(closeHour * 60));

        public bool Contains(DateTime t)
        {
            int m = t.Hour * 60 + t.Minute;
            return Close > Open ? m >= Open && m < Close : m >= Open || m < Close;
        }

        public string Describe()
        {
            return $"{Clock(Open)}–{Clock(Close)}";
        }

        public static string Clock(int minute) =>
            DateTime.Today.AddMinutes(minute % (24 * 60)).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A staff member's day: on duty during the shift except for a break (receptionist lunch, etc.).</summary>
    [Serializable]
    public struct WorkSchedule
    {
        public Hours Shift;
        public Hours Break;
        public bool HasBreak;

        public bool OnDuty(DateTime t) => Shift.Contains(t) && !(HasBreak && Break.Contains(t));
    }
}
