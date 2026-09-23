using NUnit.Framework;
using OpeningBell.Trading;

namespace OpeningBell.Tests
{
    public class PositionTests
    {
        [Test]
        public void ScalingIn_AveragesCost()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(100, 10m);
            p.ApplyFill(100, 12m);

            Assert.AreEqual(200, p.Quantity);
            Assert.AreEqual(11m, p.AveragePrice);
            Assert.AreEqual(2200m, p.CostBasis);
            Assert.AreEqual(PositionDirection.Long, p.Direction);
        }

        [Test]
        public void PartialExit_RealizesProfit_AndKeepsAverage()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(200, 11m);

            decimal realized = p.ApplyFill(-100, 13m);

            Assert.AreEqual(200m, realized);
            Assert.AreEqual(100, p.Quantity);
            Assert.AreEqual(11m, p.AveragePrice);
            Assert.AreEqual(200m, p.RealizedPnL);
        }

        [Test]
        public void CompleteExit_ClosesPosition()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(200, 11m);
            p.ApplyFill(-100, 13m);
            decimal realized = p.ApplyFill(-100, 9m);

            Assert.AreEqual(-200m, realized);
            Assert.AreEqual(0, p.Quantity);
            Assert.IsFalse(p.IsOpen);
            Assert.AreEqual(PositionDirection.Flat, p.Direction);
            Assert.AreEqual(0m, p.CostBasis);
            Assert.AreEqual(0m, p.RealizedPnL);
        }

        [Test]
        public void RepeatingDecimalCost_StaysExactAcrossExits()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(1, 10m);
            p.ApplyFill(1, 10m);
            p.ApplyFill(1, 10.01m); // cost 30.01 over 3 shares

            Assert.AreEqual(1.00m, p.ApplyFill(-1, 11m)); // removes 10.00 of cost (rounded)
            Assert.AreEqual(1.99m, p.ApplyFill(-2, 11m)); // removes the remaining 20.01
            Assert.AreEqual(2.99m, p.RealizedPnL);         // == 33.00 − 30.01 exactly
            Assert.AreEqual(0m, p.CostBasis);
        }

        [Test]
        public void ShortSide_CoverAndFlip()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(-100, 10m);
            Assert.AreEqual(PositionDirection.Short, p.Direction);
            Assert.AreEqual(10m, p.AveragePrice);

            Assert.AreEqual(80m, p.ApplyFill(40, 8m));
            Assert.AreEqual(-60, p.Quantity);
            Assert.AreEqual(10m, p.AveragePrice);

            Assert.AreEqual(60m, p.ApplyFill(100, 9m)); // covers 60, opens 40 long
            Assert.AreEqual(40, p.Quantity);
            Assert.AreEqual(9m, p.AveragePrice);
            Assert.AreEqual(140m, p.RealizedPnL);
        }

        [Test]
        public void UnrealizedPnL_UsesMark()
        {
            var p = new Position("TST", 1m);
            p.ApplyFill(100, 10m);
            Assert.AreEqual(1050m, p.MarketValue(10.50m));
            Assert.AreEqual(50m, p.UnrealizedPnL(10.50m));
        }

        [Test]
        public void PointValue_ScalesMoney_LikeFutures()
        {
            var p = new Position("NQX", 20m); // $20 a point, like an NQ mini
            p.ApplyFill(2, 100m);
            Assert.AreEqual(100m, p.AveragePrice);
            Assert.AreEqual(30m, p.UnrealizedPnL(100.75m));   // 2 × 0.75 × 20
            Assert.AreEqual(4030m, p.MarketValue(100.75m));   // notional exposure, not what it costs to hold
            Assert.AreEqual(50m, p.ApplyFill(-2, 101.25m));   // 2 × 1.25 × 20
            Assert.IsFalse(p.IsOpen);
        }
    }
}
