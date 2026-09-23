using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>
    /// Packs a stock's order-flow state into a flat list of 64-bit words (doubles as raw IEEE bits), so a load resumes
    /// bit-for-bit. The layout is versioned: an older or missing block leaves the stock's freshly initialised state.
    /// </summary>
    internal static class FlowCodec
    {
        private const long Version = 1;

        public static List<long> Capture(FlowState f)
        {
            var o = new List<long> { Version };
            DayProfile d = f.Day;
            o.Add((long)d.Type);
            foreach (double v in new[]
                     {
                         d.Drift, d.FlipMinute, d.DriftAfterFlip, d.Gap, d.RelativeVolume, d.Volatility, d.Liquidity,
                         d.Institutional, d.Momentum, d.MeanReversion, d.Breakout, d.LevelRespect, d.Retail,
                     })
                D(o, v);

            o.Add((long)f.Regime);
            foreach (double v in new[]
                     {
                         f.RegimeMinutes, f.Mom5, f.Mom30, f.Var, f.VarSlow, f.GrossEma, f.Withdraw, f.Burst, f.NewsFlow,
                         f.PremarketHigh, f.PremarketLow, f.SessionHigh, f.SessionLow, f.SessionOpen, f.MinutesSinceOpen,
                         f.PathHigh, f.PathLow,
                     })
                D(o, v);
            o.Add(f.StepsInMinute);
            o.Add(f.OpenAuctionDone ? 1 : 0);
            o.Add(f.LastSwingTicks);

            o.Add(f.Metas.Count);
            foreach (MetaOrder m in f.Metas)
            {
                o.Add(m.Side);
                D(o, m.Remaining);
                D(o, m.Rate);
                D(o, m.Urgency);
                D(o, m.LimitLog);
                D(o, m.Passive);
            }

            o.Add(f.Levels.All.Count);
            foreach (Level l in f.Levels.All)
            {
                o.Add((long)l.Kind);
                D(o, l.Log);
                D(o, l.Strength);
                o.Add(l.Touches);
                o.Add(l.Side);
                D(o, l.Wall);
                D(o, l.Stops);
                D(o, l.Age);
            }
            return o;
        }

        public static void Restore(FlowState f, List<long> data)
        {
            if (data == null || data.Count == 0 || data[0] != Version) return;
            int i = 1;
            long L() => data[i++];
            double R() => BitConverter.Int64BitsToDouble(data[i++]);

            f.Day = new DayProfile
            {
                Type = (DayType)L(), Drift = R(), FlipMinute = R(), DriftAfterFlip = R(), Gap = R(), RelativeVolume = R(),
                Volatility = R(), Liquidity = R(), Institutional = R(), Momentum = R(), MeanReversion = R(), Breakout = R(),
                LevelRespect = R(), Retail = R(),
            };
            f.Regime = (Regime)L();
            f.RegimeMinutes = R(); f.Mom5 = R(); f.Mom30 = R(); f.Var = R(); f.VarSlow = R(); f.GrossEma = R();
            f.Withdraw = R(); f.Burst = R(); f.NewsFlow = R();
            f.PremarketHigh = R(); f.PremarketLow = R(); f.SessionHigh = R(); f.SessionLow = R(); f.SessionOpen = R();
            f.MinutesSinceOpen = R(); f.PathHigh = R(); f.PathLow = R();
            f.StepsInMinute = (int)L();
            f.OpenAuctionDone = L() != 0;
            f.LastSwingTicks = L();

            f.Metas.Clear();
            int metas = (int)L();
            for (int k = 0; k < metas; k++)
                f.Metas.Add(new MetaOrder { Side = (int)L(), Remaining = R(), Rate = R(), Urgency = R(), LimitLog = R(), Passive = R() });

            f.Levels.Clear();
            int levels = (int)L();
            for (int k = 0; k < levels; k++)
                f.Levels.Mutable.Add(new Level
                {
                    Kind = (LevelKind)L(), Log = R(), Strength = R(), Touches = (int)L(), Side = (int)L(), Wall = R(), Stops = R(), Age = R(),
                });
        }

        private static void D(List<long> o, double v) => o.Add(BitConverter.DoubleToInt64Bits(v));
    }
}
