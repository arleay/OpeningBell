using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    // Save models: plain fields only (JsonUtility-friendly). Times are DateTime ticks, prices fixed-point ×10⁴,
    // engine doubles are raw IEEE bits so a load resumes bit-for-bit.

    [Serializable]
    public sealed class MarketSaveData
    {
        public long Now;
        public int Session;
        public long TradingDate;
        public long TickCount;
        public long MarketActivityBits;
        public long MarketDayDriftBits;
        public long MarketLegRateBits, MarketLegMinutesBits;
        public RandomState MarketRng;
        public List<SecuritySaveData> Securities = new List<SecuritySaveData>();
        public IndexSaveData Index = new IndexSaveData();
        public bool HasNews;
        public NewsSaveData News = new NewsSaveData();
    }

    [Serializable]
    public sealed class SecuritySaveData
    {
        public string Ticker;
        public long FairLogBits, DeviationLogBits, MomentumBits, ActivityLogBits;
        public long NewsImpulseFairBits, NewsImpulseDeviationBits, PendingNewsLogBits, NewsActivityLogBits;
        public RandomState Rng;
        public long Bid, Ask, Last, BidSize, AskSize, LastVolume, QuoteTime;
        public int LastDirection;
        public long PreviousClose, RegularClose, DayHigh, DayLow, DayVolume, DayNotional;
        public CandleSetSaveData Candles = new CandleSetSaveData();
        /// <summary>Order-flow state (see FlowCodec); empty in saves from before the order-flow market.</summary>
        public List<long> Flow = new List<long>();
    }

    [Serializable]
    public sealed class IndexSaveData
    {
        public long LogLevelBits, NewsImpulseBits, PendingNewsLogBits;
        public long Level, PreviousClose, RegularClose;
        public CandleSetSaveData Candles = new CandleSetSaveData();
    }

    /// <summary>1-minute and daily history. 5m/15m/1h are rebuilt from 1-minute candles on load (lossless).</summary>
    [Serializable]
    public sealed class CandleSetSaveData
    {
        public CandleSeriesSaveData Minute1 = new CandleSeriesSaveData();
        public CandleSeriesSaveData Day1 = new CandleSeriesSaveData();
    }

    [Serializable]
    public sealed class CandleSeriesSaveData
    {
        public long[] Start = Array.Empty<long>();
        public long[] Open = Array.Empty<long>();
        public long[] High = Array.Empty<long>();
        public long[] Low = Array.Empty<long>();
        public long[] Close = Array.Empty<long>();
        public long[] Volume = Array.Empty<long>();
        public long[] Notional = Array.Empty<long>();
    }

    [Serializable]
    public sealed class NewsSaveData
    {
        public RandomState Rng;
        public long NextId;
        public long Sequence;
        public List<PlannedNewsSaveData> Queue = new List<PlannedNewsSaveData>();
        public List<NewsItemSaveData> Feed = new List<NewsItemSaveData>();
    }

    [Serializable]
    public sealed class PlannedNewsSaveData
    {
        public long Time, Sequence;
        public string TemplateId, Ticker;
        public int Sector;
        public long SeverityBits;
    }

    [Serializable]
    public sealed class NewsItemSaveData
    {
        public long Id, Time;
        public string Headline;
        public int Type, Scope;
        public string[] Tickers;
        public long SeverityBits, MoveBits;
    }

    internal static class SaveCodec
    {
        public static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        public static double Double(long bits) => BitConverter.Int64BitsToDouble(bits);

        /// <summary>Prices and notionals live on a 0.0001 grid, so ×10⁴ is exact. Anything else is a bug.</summary>
        public static long Fixed(decimal value)
        {
            decimal scaled = value * 10000m;
            if (scaled != decimal.Truncate(scaled)) throw new InvalidOperationException($"{value} is not on the 0.0001 grid.");
            return decimal.ToInt64(scaled);
        }

        public static decimal Decimal(long fixed4) => fixed4 / 10000m;

        public static CandleSeriesSaveData Candles(CandleSeries series, int maxCandles)
        {
            int count = Math.Min(series.Count, maxCandles);
            int first = series.Count - count;
            var data = new CandleSeriesSaveData
            {
                Start = new long[count], Open = new long[count], High = new long[count], Low = new long[count],
                Close = new long[count], Volume = new long[count], Notional = new long[count],
            };
            for (int i = 0; i < count; i++)
            {
                Candle c = series[first + i];
                data.Start[i] = c.Start.Ticks;
                data.Open[i] = Fixed(c.Open);
                data.High[i] = Fixed(c.High);
                data.Low[i] = Fixed(c.Low);
                data.Close[i] = Fixed(c.Close);
                data.Volume[i] = c.Volume;
                data.Notional[i] = Fixed(c.Notional);
            }
            return data;
        }

        public static List<Candle> Candles(CandleSeriesSaveData data)
        {
            var list = new List<Candle>(data.Start.Length);
            for (int i = 0; i < data.Start.Length; i++)
                list.Add(new Candle(new DateTime(data.Start[i]), Decimal(data.Open[i]), Decimal(data.High[i]),
                    Decimal(data.Low[i]), Decimal(data.Close[i]), data.Volume[i], Decimal(data.Notional[i])));
            return list;
        }
    }
}
