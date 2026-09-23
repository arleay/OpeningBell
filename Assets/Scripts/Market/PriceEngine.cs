using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>
    /// Two layers per security (see MARKET_SPEC.md):
    ///   information  fair += βm·market + βs·sector + σ·z + day drift + news     what informed traders think it's worth
    ///   price        moved only by order flow (<see cref="OrderFlow"/>): participants trade, the book absorbs
    /// Price = fair + deviation, where the deviation is whatever the flow has left between price and fair value.
    /// σ scales with the intraday profile and a stochastic "activity" level (log-OU) that also drives volume and
    /// spread (volatility clustering). The market has a hidden day too (its drift gives the index trend days).
    /// </summary>
    internal sealed class PriceEngine
    {
        private const double MeanAbsNormal = 0.7978845608; // E|z| for z ~ N(0,1)
        private static readonly double MinLogPrice = Math.Log(0.0001);

        private readonly MarketConfig _config;
        private readonly MarketSchedule _schedule;
        private readonly SeededRandom _marketRng;
        private readonly double[] _sectorReturns;
        private readonly double _dt;
        private readonly double _dtDays;
        private readonly double _sqrtDtDays;
        private readonly double _momentumAlpha;
        private readonly double _activityDecay;
        private readonly double _activityShock;
        private readonly double _activityVariance;
        private readonly double _volumeNormalizer;
        private readonly double _volNormalizer;
        private readonly double _newsDelivery;
        private readonly double _newsActivityDecay;
        private double _marketActivityLog;
        private readonly OrderFlow _flow;
        private readonly double _stepsPerSession;

        /// <summary>
        /// Share of each layer's variance left to random diffusion once day drifts and gaps take their part, so
        /// close-to-close volatility still matches the specs on average.
        /// </summary>
        private const double FairDiffusion = 0.5, MarketDiffusion = 0.78;
        /// <summary>Spread of the market's daily drift, in market-volatility units.</summary>
        private const double MarketDayDriftSpread = 0.65;

        /// <summary>The market's hidden day: drift of the index factor over the regular session, in market-vol units.</summary>
        internal double MarketDayDrift { get; set; }

        internal OrderFlow Flow => _flow;

        internal double MarketActivityLog
        {
            get => _marketActivityLog;
            set => _marketActivityLog = value;
        }

        internal SeededRandom MarketRng => _marketRng;

        public PriceEngine(MarketConfig config, MarketSchedule schedule, SeededRandom marketRng)
        {
            _config = config;
            _schedule = schedule;
            _marketRng = marketRng;
            _sectorReturns = new double[Enum.GetValues(typeof(Sector)).Length];

            _dt = config.TickSeconds;
            _dtDays = _dt / schedule.RegularSessionSeconds;
            _sqrtDtDays = Math.Sqrt(_dtDays);
            _momentumAlpha = 1 - Math.Exp(-_dt * Math.Log(2) / (config.MomentumHalfLifeMinutes * 60));
            _activityDecay = Math.Exp(-config.ActivityReversionPerDay * _dtDays);
            _activityShock = config.ActivityVolOfVol * _sqrtDtDays;
            // Stationary variance V of the log-OU; exp(a − V) has E[mult²] = 1, so clustering leaves average variance unchanged.
            _activityVariance = config.ActivityVolOfVol * config.ActivityVolOfVol / (2 * config.ActivityReversionPerDay);
            // Volume uses the product of two such multipliers, whose mean is exp(−V); undo that so ADV holds on average.
            _volumeNormalizer = Math.Exp(_activityVariance);
            // Scale so "daily" volatility means close-to-close, including extended hours and the overnight gap.
            _volNormalizer = 1 / Math.Sqrt(DailyVarianceMultiplier(config, schedule));
            _newsDelivery = 1 - Math.Exp(-_dt / (config.NewsDeliveryMinutes * 60));
            _newsActivityDecay = Math.Exp(-_dt * Math.Log(2) / (config.NewsActivityHalfLifeMinutes * 60));
            _stepsPerSession = schedule.RegularSessionSeconds / _dt;
            _flow = new OrderFlow(config, schedule);
            MarketDayDrift = MarketDayDriftSpread * _marketRng.NextGaussian();
        }

        private static bool SmallCap(SecuritySpec spec) => spec.DailyVolatility >= 0.05 || spec.FloatShares < 30_000_000;

        /// <summary>Queues a news catalyst for the next tick (see NewsEngine for how the moves are drawn).</summary>
        public void ApplyNews(SecurityRuntimeState sec, double fairShift, double overreaction, double severity, double attentionScale)
        {
            sec.NewsImpulseFair += fairShift * _config.NewsImmediateFraction;
            sec.PendingNewsLog += fairShift * (1 - _config.NewsImmediateFraction);
            sec.NewsImpulseDeviation += overreaction;
            double boost = Math.Log(1 + severity * attentionScale * _config.NewsActivityBoost);
            sec.NewsActivityLog = Math.Max(sec.NewsActivityLog, boost);
        }

        public void ApplyIndexNews(MarketIndex index, double move)
        {
            index.NewsImpulse += move * _config.NewsImmediateFraction;
            index.PendingNewsLog += move * (1 - _config.NewsImmediateFraction);
        }

        /// <summary>Variance of one full trading day relative to a flat regular session: ∫ profile² dt + overnight².</summary>
        private static double DailyVarianceMultiplier(MarketConfig config, MarketSchedule schedule)
        {
            var day = new DateTime(2000, 1, 3); // any weekday
            double sum = 0;
            for (DateTime t = day + schedule.PremarketOpen; t < day + schedule.AfterHoursClose; t = t.AddSeconds(config.TickSeconds))
            {
                double v = IntradayProfile.Evaluate(config, schedule, schedule.GetSession(t), t).Volatility;
                sum += v * v * config.TickSeconds;
            }
            return sum / schedule.RegularSessionSeconds + config.OvernightVolatilityRatio * config.OvernightVolatilityRatio;
        }

        public void Initialize(SecurityRuntimeState sec, MarketSession session, DateTime time)
        {
            sec.FairLog = Math.Log(sec.Spec.BasePrice);
            decimal last = PriceTick.RoundNearest((decimal)sec.Spec.BasePrice);
            sec.PreviousClose = last;
            sec.RegularClose = last;
            var profile = IntradayProfile.Evaluate(_config, _schedule, session, time);
            SetQuoteWithoutTrade(sec, profile, session == MarketSession.Regular, time);
            sec.Quote = new Quote(sec.Quote.Bid, sec.Quote.Ask, sec.Quote.BidSize, sec.Quote.AskSize, last, 0, 0, time);
            _flow.StartDay(sec, sec.FairLog, DayProfile.Draw(sec.Rng, MarketDayDrift, SmallCap(sec.Spec)));
        }

        public void Initialize(MarketIndex index)
        {
            index.LogLevel = Math.Log(index.Spec.BaseLevel);
            index.Level = Math.Round((decimal)index.Spec.BaseLevel, 2);
            index.PreviousClose = index.Level;
            index.RegularClose = index.Level;
        }

        public void Tick(DateTime time, MarketSession session, IReadOnlyList<SecurityRuntimeState> securities, MarketIndex index)
        {
            var profile = IntradayProfile.Evaluate(_config, _schedule, session, time);
            bool regular = session == MarketSession.Regular;

            _marketActivityLog = _marketActivityLog * _activityDecay + _activityShock * _marketRng.NextGaussian();
            double marketActivity = Math.Exp(_marketActivityLog - _activityVariance);
            double factorScale = _volNormalizer * profile.Volatility * marketActivity * _sqrtDtDays;

            double marketDrift = regular ? MarketDayDrift * _config.MarketDailyVolatility / _stepsPerSession : 0;
            double marketReturn = _config.MarketDailyDrift * _dtDays + marketDrift +
                                  _config.MarketDailyVolatility * MarketDiffusion * factorScale * _marketRng.NextGaussian();
            for (int i = 0; i < _sectorReturns.Length; i++)
                _sectorReturns[i] = _config.SectorDailyVolatility * factorScale * _marketRng.NextGaussian();

            for (int i = 0; i < securities.Count; i++)
                TickSecurity(securities[i], time, session, profile, regular, marketReturn, marketActivity);

            double indexNews = index.NewsImpulse + index.PendingNewsLog * _newsDelivery;
            index.PendingNewsLog -= index.PendingNewsLog * _newsDelivery;
            index.NewsImpulse = 0;
            index.LogLevel += marketReturn + indexNews;
            index.Level = Math.Round((decimal)Math.Exp(index.LogLevel), 2);
            index.Candles.Record(time, index.Level, 0, regular);
        }

        private void TickSecurity(SecurityRuntimeState sec, DateTime time, MarketSession session, ActivityProfile profile, bool regular,
            double marketReturn, double marketActivity)
        {
            SecuritySpec spec = sec.Spec;
            SeededRandom rng = sec.Rng;
            FlowState flow = sec.Flow;

            // News: consume impulses, deliver part of the pending move, decay attention. All exactly zero/one without news.
            double delivered = sec.PendingNewsLog * _newsDelivery;
            sec.PendingNewsLog -= delivered;
            double newsFair = sec.NewsImpulseFair + delivered;
            // What news traders act on right away: the headline's immediate value plus the crowd's over/under-reaction.
            double newsNow = sec.NewsImpulseFair + sec.NewsImpulseDeviation;
            sec.NewsImpulseFair = 0;
            sec.NewsImpulseDeviation = 0;
            sec.NewsActivityLog *= _newsActivityDecay;
            double attention = Math.Exp(sec.NewsActivityLog);

            sec.ActivityLog = sec.ActivityLog * _activityDecay + _activityShock * rng.NextGaussian();
            double activity = Math.Exp(sec.ActivityLog - _activityVariance) * Math.Sqrt(attention);
            double sigma = spec.DailyVolatility * _volNormalizer * profile.Volatility * activity * _sqrtDtDays;
            double minutesSinceOpen = (time.TimeOfDay - _schedule.RegularOpen).TotalMinutes;

            // ---- information: fair value moves with the market, the sector, the company's own news and the day's drift.
            double price = sec.FairLog + sec.DeviationLog;
            double drift = regular ? flow.Day.DriftAt(minutesSinceOpen) * spec.DailyVolatility / _stepsPerSession : 0;
            double systematic = spec.MarketBeta * marketReturn + spec.SectorBeta * _sectorReturns[sec.SectorIndex];
            sec.FairLog += systematic + sigma * FairDiffusion * rng.NextGaussian() + newsFair + drift;

            // ---- price: only order flow moves it.
            if (newsNow != 0) _flow.OnNews(sec, newsNow, price);
            bool openingCross = regular && !flow.OpenAuctionDone;
            if (openingCross) _flow.OnOpen(sec, price);
            var step = new FlowStep(regular, minutesSinceOpen, profile.Volume, sigma, systematic);
            double newLog = Math.Max(MinLogPrice, _flow.Step(sec, price, step, out double gross));
            sec.DeviationLog = newLog - sec.FairLog;
            if ((time.TimeOfDay.TotalSeconds + _dt) % 60 < 1e-6) _flow.Minute(sec, newLog, step);

            double logReturn = newLog - price;
            sec.Momentum += _momentumAlpha * (logReturn / _dt - sec.Momentum);

            // Volume: the time-of-day profile and activity, the day's relative volume, and how much actually traded
            // this step (absorption and stop runs print heavy volume even when price barely moves).
            double flowRatio = flow.GrossEma > 0 ? Math.Min(4, gross / flow.GrossEma) : 1;
            double expectedShares = spec.AverageDailyVolume * _dtDays * profile.Volume * activity * Math.Sqrt(attention)
                                    * marketActivity * _volumeNormalizer * flow.Day.RelativeVolume / RelativeVolumeMean
                                    * (0.6 + 0.4 * flowRatio) * LogNormal(rng, _config.VolumeNoise);
            // Opening and closing crosses: queued orders meet in one big print.
            if (openingCross) expectedShares += spec.AverageDailyVolume * 0.006 * flow.Day.RelativeVolume * LogNormal(rng, 0.4);
            bool closingCross = session == MarketSession.Regular && (time + TimeSpan.FromSeconds(_dt)).TimeOfDay >= _schedule.RegularClose;
            if (closingCross) expectedShares += spec.AverageDailyVolume * 0.035 * flow.Day.RelativeVolume * LogNormal(rng, 0.4);
            long shares = (long)Math.Round(expectedShares);

            double depth = flow.Day.Liquidity / (1 + flow.Withdraw);
            ComputeQuote(sec, Math.Exp(newLog), SpreadScale(regular, profile, activity) / Math.Sqrt(depth), profile,
                out decimal bid, out decimal ask, out long bidSize, out long askSize);

            decimal last = sec.Last;
            int direction = 0;
            if (shares > 0)
            {
                // Price rose → buyers lifted the offer; fell → sellers hit the bid. Produces natural bid/ask bounce.
                direction = logReturn > 0 ? 1 : logReturn < 0 ? -1 : (rng.NextDouble() < 0.5 ? 1 : -1);
                last = direction > 0 ? ask : bid;

                // A step that ran through stops and came back leaves a print at its extreme (the wick of a sweep).
                decimal tick = PriceTick.For(last);
                double extreme = direction >= 0 ? flow.PathLow : flow.PathHigh;
                decimal extremePrice = PriceTick.RoundNearest((decimal)Math.Exp(Math.Max(MinLogPrice, extreme)));
                long wickShares = 0;
                if (Math.Abs(extremePrice - last) >= 2 * tick && extremePrice > 0m)
                {
                    wickShares = Math.Max(1, shares / 4);
                    RecordPrint(sec, time, extremePrice, wickShares, regular);
                }
                RecordPrint(sec, time, last, shares - wickShares, regular);
            }
            else
            {
                shares = 0;
            }

            sec.Quote = new Quote(bid, ask, bidSize, askSize, last, shares, direction, time);
        }

        /// <summary>Mean volume multiplier from the day mix, busy flow and the opening/closing crosses; dividing keeps ADV calibrated.</summary>
        private const double RelativeVolumeMean = 1.55;

        private static void RecordPrint(SecurityRuntimeState sec, DateTime time, decimal price, long shares, bool regular)
        {
            if (shares <= 0) return;
            if (sec.DayVolume == 0)
            {
                sec.DayHigh = price;
                sec.DayLow = price;
            }
            else
            {
                if (price > sec.DayHigh) sec.DayHigh = price;
                if (price < sec.DayLow) sec.DayLow = price;
            }
            sec.DayVolume += shares;
            sec.DayNotional += price * shares;
            sec.Candles.Record(time, price, shares, regular);
        }

        /// <summary>Close-to-open jump applied when a new trading day's premarket begins.</summary>
        public void ApplyOvernightGap(IReadOnlyList<SecurityRuntimeState> securities, MarketIndex index, DateTime time)
        {
            double scale = _volNormalizer * _config.OvernightVolatilityRatio;
            double marketGap = _config.MarketDailyVolatility * scale * _marketRng.NextGaussian();
            MarketDayDrift = MarketDayDriftSpread * _marketRng.NextGaussian();
            for (int i = 0; i < _sectorReturns.Length; i++)
                _sectorReturns[i] = _config.SectorDailyVolatility * scale * _marketRng.NextGaussian();

            var profile = IntradayProfile.Evaluate(_config, _schedule, MarketSession.Premarket, time);
            foreach (var sec in securities)
            {
                SecuritySpec spec = sec.Spec;
                // Undelivered news is fully priced in by the next morning; attention fades overnight.
                sec.FairLog += sec.NewsImpulseFair + sec.PendingNewsLog;
                sec.DeviationLog += sec.NewsImpulseDeviation;
                sec.NewsImpulseFair = sec.PendingNewsLog = sec.NewsImpulseDeviation = 0;
                sec.NewsActivityLog *= 0.4;

                sec.FairLog += spec.MarketBeta * marketGap + spec.SectorBeta * _sectorReturns[sec.SectorIndex]
                               + spec.DailyVolatility * scale * sec.Rng.NextGaussian();
                sec.DeviationLog *= _config.OvernightDeviationCarry;
                sec.Momentum = 0;

                // Today's hidden character; gap days open away from yesterday's close.
                DayProfile day = DayProfile.Draw(sec.Rng, MarketDayDrift, SmallCap(spec));
                sec.FairLog += day.Gap * spec.DailyVolatility * _config.OvernightVolatilityRatio;
                SetQuoteWithoutTrade(sec, profile, false, time);
                _flow.StartDay(sec, sec.FairLog + sec.DeviationLog, day);
            }

            index.LogLevel += marketGap + index.NewsImpulse + index.PendingNewsLog;
            index.NewsImpulse = index.PendingNewsLog = 0;
            index.Level = Math.Round((decimal)Math.Exp(index.LogLevel), 2);
        }

        public static void CaptureRegularClose(IReadOnlyList<SecurityRuntimeState> securities, MarketIndex index)
        {
            foreach (var sec in securities) sec.RegularClose = sec.Last;
            index.RegularClose = index.Level;
        }

        private void SetQuoteWithoutTrade(SecurityRuntimeState sec, ActivityProfile profile, bool regular, DateTime time)
        {
            double activity = Math.Exp(sec.ActivityLog - _activityVariance);
            ComputeQuote(sec, Math.Exp(sec.FairLog + sec.DeviationLog), SpreadScale(regular, profile, activity), profile,
                out decimal bid, out decimal ask, out long bidSize, out long askSize);
            sec.Quote = new Quote(bid, ask, bidSize, askSize, sec.Last, 0, 0, time);
        }

        /// <summary>
        /// Regular hours: spreads track volatility (wide at the open, tight midday). Extended hours: spreads are
        /// wide because liquidity is thin, so the low time-of-day volatility must not narrow them.
        /// </summary>
        private double SpreadScale(bool regular, ActivityProfile profile, double activity) =>
            regular ? Math.Sqrt(profile.Volatility * activity) : _config.ExtendedSpreadMultiplier * Math.Sqrt(activity);

        private void ComputeQuote(SecurityRuntimeState sec, double mid, double spreadScale, ActivityProfile profile,
            out decimal bid, out decimal ask, out long bidSize, out long askSize)
        {
            SecuritySpec spec = sec.Spec;
            decimal midPrice = (decimal)mid;
            decimal tick = PriceTick.For(midPrice);

            double spreadFraction = spec.BaseSpreadBps / 10000.0 * spreadScale;
            decimal half = Math.Max(tick / 2m, midPrice * (decimal)(spreadFraction / 2));
            bid = PriceTick.RoundDown(midPrice - half, tick);
            ask = PriceTick.RoundUp(midPrice + half, tick);
            if (bid < tick) bid = tick;
            if (ask <= bid) ask = bid + tick;

            double depth = spec.AverageDailyVolume * _config.DepthFractionOfAdv * Math.Sqrt(profile.Volume);
            bidSize = RoundLot(depth * LogNormal(sec.Rng, _config.DepthNoise));
            askSize = RoundLot(depth * LogNormal(sec.Rng, _config.DepthNoise));
        }

        /// <summary>Mean-one lognormal multiplier.</summary>
        private static double LogNormal(SeededRandom rng, double sigma) =>
            Math.Exp(sigma * rng.NextGaussian() - sigma * sigma / 2);

        private static long RoundLot(double shares) => Math.Max(100L, (long)Math.Round(shares / 100) * 100);
    }
}
