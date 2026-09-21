using System;
using NUnit.Framework;
using OpeningBell.Core;

namespace OpeningBell.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new SeededRandom(18492);
            var b = new SeededRandom(18492);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void NamedStreams_AreIndependent_AndReproducible()
        {
            var service = new SeededRandomService(7);
            double market = service.CreateStream("market").NextDouble();
            Assert.AreEqual(market, new SeededRandomService(7).CreateStream("market").NextDouble());
            Assert.AreNotEqual(market, service.CreateStream("news").NextDouble());
            Assert.AreNotEqual(market, new SeededRandomService(8).CreateStream("market").NextDouble());
        }

        [Test]
        public void Distributions_HaveExpectedMoments()
        {
            var rng = new SeededRandom(1);
            const int n = 200_000;
            double sum = 0, sumSq = 0, min = 1, max = 0;
            for (int i = 0; i < n; i++)
            {
                double u = rng.NextDouble();
                min = Math.Min(min, u);
                max = Math.Max(max, u);
                double z = rng.NextGaussian();
                sum += z;
                sumSq += z * z;
            }

            Assert.GreaterOrEqual(min, 0.0);
            Assert.Less(max, 1.0);
            Assert.AreEqual(0.0, sum / n, 0.01);
            Assert.AreEqual(1.0, Math.Sqrt(sumSq / n), 0.01);
        }
    }

    public class GameClockTests
    {
        [Test]
        public void Advance_ScalesRealTime()
        {
            var start = new DateTime(2030, 1, 7, 9, 0, 0);
            var clock = new GameClock(start, timeScale: 10);
            clock.Advance(2.5);
            Assert.AreEqual(start.AddSeconds(25), clock.Now);
        }

        [Test]
        public void Paused_DoesNotAdvance()
        {
            var start = new DateTime(2030, 1, 7, 9, 0, 0);
            var clock = new GameClock(start, 10) { IsPaused = true };
            clock.Advance(5);
            Assert.AreEqual(start, clock.Now);
        }
    }
}
