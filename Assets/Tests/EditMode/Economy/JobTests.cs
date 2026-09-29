using System;
using NUnit.Framework;
using OpeningBell.Economy;

namespace OpeningBell.Tests
{
    public class JobTests
    {
        private static readonly DateTime Noon = new DateTime(2030, 1, 7, 12, 0, 0);

        [Test]
        public void MustBeHiredToClockIn()
        {
            var job = new Job("Sal's Pizza", 15m);
            Assert.IsFalse(job.ClockIn(Noon));
            job.Hire();
            Assert.IsTrue(job.ClockIn(Noon));
            Assert.IsFalse(job.ClockIn(Noon), "already on shift");
        }

        [Test]
        public void Wage_ByTheMinute_TipsByTheWait()
        {
            var job = new Job("Sal's Pizza", 15m);
            job.Hire();
            job.ClockIn(Noon);
            Assert.AreEqual(4.00m, job.Served(20m, 10, 90), "fast: 20% of $20");
            Assert.AreEqual(0.50m, job.Served(4m, 50, 90), "12% of $4 = $0.48 → $0.50");
            Assert.AreEqual(0.25m, job.Served(4m, 80, 90), "slow: 5% → $0.20 → $0.25");
            job.Missed();

            ShiftSummary s = job.ClockOut(Noon.AddHours(2).AddMinutes(30));
            Assert.AreEqual(37.50m, s.Wage, "2.5 h × $15");
            Assert.AreEqual(4.75m, s.Tips);
            Assert.AreEqual(3, s.Orders);
            Assert.AreEqual(1, s.Missed);
            Assert.IsFalse(job.OnShift);
            Assert.AreEqual(42.25m, job.TotalEarned);
        }
    }
}
