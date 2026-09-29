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
        private const long Version = 4; // 2: legs appended at the end; 3: leg pulse in place of v2's jitter word; 4: structure, rotation, level breaks, zones

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

            o.Add((long)f.Leg);
            D(o, f.LegRate);
            D(o, f.LegMinutes);
            D(o, f.LegPulse);

            o.Add(f.Structure);
            D(o, f.SectorMom);
            D(o, f.SectorVar);
            D(o, f.Absorbed);
            o.Add(f.LastHourSwingTicks);
            foreach (Level l in f.Levels.All) D(o, l.SinceBreak);
            o.Add(f.Zones.All.Count);
            foreach (Zone z in f.Zones.All)
            {
                o.Add((long)z.Kind);
                o.Add(z.Side);
                o.Add(z.Retests);
                o.Add((long)z.State);
                o.Add(z.CreatedTicks);
                o.Add((z.Inside ? 1 : 0) | (z.Flipped ? 2 : 0));
                foreach (double v in new[] { z.Low, z.High, z.Strength, z.Interest, z.Initial, z.CreationVolume, z.Age }) D(o, v);
            }
            return o;
        }

        public static void Restore(FlowState f, List<long> data)
        {
            if (data == null || data.Count == 0 || data[0] < 1 || data[0] > Version) return;
            long version = data[0];
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

            if (version >= 2)
            {
                f.Leg = (LegKind)L();
                f.LegRate = R();
                f.LegMinutes = R();
                double pulse = R();
                if (version >= 3) f.LegPulse = pulse; // v2 held a (now removed) jitter value here
            }

            f.Zones.Clear();
            if (version >= 4)
            {
                f.Structure = (int)L();
                f.SectorMom = R();
                f.SectorVar = R();
                f.Absorbed = R();
                f.LastHourSwingTicks = L();
                foreach (Level l in f.Levels.All) l.SinceBreak = R();
                int zones = (int)L();
                for (int k = 0; k < zones; k++)
                {
                    var z = new Zone { Kind = (ZoneKind)L(), Side = (int)L(), Retests = (int)L(), State = (ZoneState)L(), CreatedTicks = L() };
                    long flags = L();
                    z.Inside = (flags & 1) != 0;
                    z.Flipped = (flags & 2) != 0;
                    z.Low = R(); z.High = R(); z.Strength = R(); z.Interest = R(); z.Initial = R(); z.CreationVolume = R(); z.Age = R();
                    f.Zones.Mutable.Add(z);
                }
            }
        }

        private static void D(List<long> o, double v) => o.Add(BitConverter.DoubleToInt64Bits(v));
    }
}
