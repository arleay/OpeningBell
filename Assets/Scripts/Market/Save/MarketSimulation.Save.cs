using System;

namespace OpeningBell.Market
{
    public sealed partial class MarketSimulation
    {
        /// <summary>
        /// Everything needed to resume exactly: clock position, engine and RNG states, per-security model state,
        /// quotes, day stats, recent candles and news. Definitions (specs, config, templates) come from assets.
        /// </summary>
        public MarketSaveData CaptureState(int maxMinuteCandles = 2000, int maxNewsItems = 300)
        {
            var data = new MarketSaveData
            {
                Now = Now.Ticks,
                Session = (int)Session,
                TradingDate = TradingDate.Ticks,
                TickCount = TickCount,
                MarketActivityBits = SaveCodec.Bits(_engine.MarketActivityLog),
                MarketDayDriftBits = SaveCodec.Bits(_engine.MarketDayDrift),
                MarketRng = _engine.MarketRng.CaptureState(),
            };
            foreach (SecurityRuntimeState s in _securities) data.Securities.Add(Capture(s, maxMinuteCandles));
            data.Index = Capture(Index, maxMinuteCandles);
            if (_news != null)
            {
                data.HasNews = true;
                data.News = _news.Capture(maxNewsItems);
            }
            return data;
        }

        /// <summary>
        /// Applies a saved state to a simulation built from the same definitions. Saved securities that no longer
        /// exist are dropped; new listings keep their freshly initialised state.
        /// </summary>
        public void RestoreState(MarketSaveData data)
        {
            Now = new DateTime(data.Now);
            Session = (MarketSession)data.Session;
            TradingDate = new DateTime(data.TradingDate);
            TickCount = data.TickCount;
            _engine.MarketActivityLog = SaveCodec.Double(data.MarketActivityBits);
            _engine.MarketDayDrift = SaveCodec.Double(data.MarketDayDriftBits);
            _engine.MarketRng.RestoreState(data.MarketRng);

            foreach (SecuritySaveData saved in data.Securities)
                if (_byTicker.TryGetValue(saved.Ticker, out SecurityRuntimeState s))
                    Restore(s, saved);
            Restore(Index, data.Index);

            if (_news != null && data.HasNews) _news.Restore(data.News);
        }

        private static SecuritySaveData Capture(SecurityRuntimeState s, int maxMinuteCandles)
        {
            Quote q = s.Quote;
            return new SecuritySaveData
            {
                Ticker = s.Ticker,
                FairLogBits = SaveCodec.Bits(s.FairLog),
                DeviationLogBits = SaveCodec.Bits(s.DeviationLog),
                MomentumBits = SaveCodec.Bits(s.Momentum),
                ActivityLogBits = SaveCodec.Bits(s.ActivityLog),
                NewsImpulseFairBits = SaveCodec.Bits(s.NewsImpulseFair),
                NewsImpulseDeviationBits = SaveCodec.Bits(s.NewsImpulseDeviation),
                PendingNewsLogBits = SaveCodec.Bits(s.PendingNewsLog),
                NewsActivityLogBits = SaveCodec.Bits(s.NewsActivityLog),
                Rng = s.Rng.CaptureState(),
                Bid = SaveCodec.Fixed(q.Bid),
                Ask = SaveCodec.Fixed(q.Ask),
                Last = SaveCodec.Fixed(q.Last),
                BidSize = q.BidSize,
                AskSize = q.AskSize,
                LastVolume = q.LastVolume,
                LastDirection = q.LastDirection,
                QuoteTime = q.Time.Ticks,
                PreviousClose = SaveCodec.Fixed(s.PreviousClose),
                RegularClose = SaveCodec.Fixed(s.RegularClose),
                DayHigh = SaveCodec.Fixed(s.DayHigh),
                DayLow = SaveCodec.Fixed(s.DayLow),
                DayVolume = s.DayVolume,
                DayNotional = SaveCodec.Fixed(s.DayNotional),
                Candles = Capture(s.Candles, maxMinuteCandles),
                Flow = FlowCodec.Capture(s.Flow),
            };
        }

        private static void Restore(SecurityRuntimeState s, SecuritySaveData d)
        {
            s.FairLog = SaveCodec.Double(d.FairLogBits);
            s.DeviationLog = SaveCodec.Double(d.DeviationLogBits);
            s.Momentum = SaveCodec.Double(d.MomentumBits);
            s.ActivityLog = SaveCodec.Double(d.ActivityLogBits);
            s.NewsImpulseFair = SaveCodec.Double(d.NewsImpulseFairBits);
            s.NewsImpulseDeviation = SaveCodec.Double(d.NewsImpulseDeviationBits);
            s.PendingNewsLog = SaveCodec.Double(d.PendingNewsLogBits);
            s.NewsActivityLog = SaveCodec.Double(d.NewsActivityLogBits);
            s.Rng.RestoreState(d.Rng);
            s.Quote = new Quote(SaveCodec.Decimal(d.Bid), SaveCodec.Decimal(d.Ask), d.BidSize, d.AskSize,
                SaveCodec.Decimal(d.Last), d.LastVolume, d.LastDirection, new DateTime(d.QuoteTime));
            s.PreviousClose = SaveCodec.Decimal(d.PreviousClose);
            s.RegularClose = SaveCodec.Decimal(d.RegularClose);
            s.DayHigh = SaveCodec.Decimal(d.DayHigh);
            s.DayLow = SaveCodec.Decimal(d.DayLow);
            s.DayVolume = d.DayVolume;
            s.DayNotional = SaveCodec.Decimal(d.DayNotional);
            Restore(s.Candles, d.Candles);
            FlowCodec.Restore(s.Flow, d.Flow);
        }

        private static IndexSaveData Capture(MarketIndex index, int maxMinuteCandles) => new IndexSaveData
        {
            LogLevelBits = SaveCodec.Bits(index.LogLevel),
            NewsImpulseBits = SaveCodec.Bits(index.NewsImpulse),
            PendingNewsLogBits = SaveCodec.Bits(index.PendingNewsLog),
            Level = SaveCodec.Fixed(index.Level),
            PreviousClose = SaveCodec.Fixed(index.PreviousClose),
            RegularClose = SaveCodec.Fixed(index.RegularClose),
            Candles = Capture(index.Candles, maxMinuteCandles),
        };

        private static void Restore(MarketIndex index, IndexSaveData d)
        {
            index.LogLevel = SaveCodec.Double(d.LogLevelBits);
            index.NewsImpulse = SaveCodec.Double(d.NewsImpulseBits);
            index.PendingNewsLog = SaveCodec.Double(d.PendingNewsLogBits);
            index.Level = SaveCodec.Decimal(d.Level);
            index.PreviousClose = SaveCodec.Decimal(d.PreviousClose);
            index.RegularClose = SaveCodec.Decimal(d.RegularClose);
            Restore(index.Candles, d.Candles);
        }

        private static CandleSetSaveData Capture(CandleAggregator candles, int maxMinuteCandles) => new CandleSetSaveData
        {
            Minute1 = SaveCodec.Candles(candles.Get(Timeframe.Minute1), maxMinuteCandles),
            Day1 = SaveCodec.Candles(candles.Get(Timeframe.Day1), int.MaxValue),
        };

        private static void Restore(CandleAggregator candles, CandleSetSaveData d) =>
            candles.Restore(SaveCodec.Candles(d.Minute1), SaveCodec.Candles(d.Day1));
    }
}
