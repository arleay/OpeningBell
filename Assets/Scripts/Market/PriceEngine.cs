using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>
    /// Per-tick multi-factor model. Each security's log price is fair + deviation:
    ///   fair      += βm·market + βs·sector + σ·z                   permanent; systematic moves persist
    ///   deviation  = deviation·e^(−κ·Δ) + φ·momentum·Δt + σ·k·z'    transient overreaction that mean-reverts
    /// σ scales with the intraday profile and a stochastic "activity" level (log-OU) that also drives volume
    /// and spread. That gives volatility clustering and fat tails without a heavy model.
    /// The engine never sees orders: it cannot react to what the player does.
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
        private double _marketActivityLog;

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

            double marketReturn = _config.MarketDailyDrift * _dtDays +
                                  _config.MarketDailyVolatility * factorScale * _marketRng.NextGaussian();
            for (int i = 0; i < _sectorReturns.Length; i++)
                _sectorReturns[i] = _config.SectorDailyVolatility * factorScale * _marketRng.NextGaussian();

            for (int i = 0; i < securities.Count; i++)
                TickSecurity(securities[i], time, profile, regular, marketReturn, marketActivity);

            index.LogLevel += marketReturn;
            index.Level = Math.Round((decimal)Math.Exp(index.LogLevel), 2);
            index.Candles.Record(time, index.Level, 0, regular);
        }

        private void TickSecurity(SecurityRuntimeState sec, DateTime time, ActivityProfile profile, bool regular,
            double marketReturn, double marketActivity)
        {
            SecuritySpec spec = sec.Spec;
            SeededRandom rng = sec.Rng;

            sec.ActivityLog = sec.ActivityLog * _activityDecay + _activityShock * rng.NextGaussian();
            double activity = Math.Exp(sec.ActivityLog - _activityVariance);
            double volMultiplier = profile.Volatility * activity;
            double sigma = spec.DailyVolatility * _volNormalizer * volMultiplier * _sqrtDtDays;

            double zFair = rng.NextGaussian();
            double zNoise = rng.NextGaussian();
            double oldLog = sec.FairLog + sec.DeviationLog;

            sec.FairLog += spec.MarketBeta * marketReturn + spec.SectorBeta * _sectorReturns[sec.SectorIndex] + sigma * zFair;
            sec.DeviationLog = sec.DeviationLog * Math.Exp(-spec.MeanReversionPerDay * _dtDays)
                               + spec.MomentumCoefficient * sec.Momentum * _dt
                               + sigma * _config.TransientNoiseRatio * zNoise;

            double newLog = sec.FairLog + sec.DeviationLog;
            if (newLog < MinLogPrice)
            {
                sec.DeviationLog = MinLogPrice - sec.FairLog;
                newLog = MinLogPrice;
            }

            double logReturn = newLog - oldLog;
            sec.Momentum += _momentumAlpha * (logReturn / _dt - sec.Momentum);

            // Volume follows the time-of-day profile, both activity levels, and how surprising this move was.
            double k = _config.TransientNoiseRatio;
            double surprise = Math.Abs(zFair + k * zNoise) / Math.Sqrt(1 + k * k) / MeanAbsNormal;
            double expectedShares = spec.AverageDailyVolume * _dtDays * profile.Volume * activity * marketActivity * _volumeNormalizer
                                    * (0.5 + 0.5 * surprise) * LogNormal(rng, _config.VolumeNoise);
            long shares = (long)Math.Round(expectedShares);

            ComputeQuote(sec, Math.Exp(newLog), SpreadScale(regular, profile, activity), profile,
                out decimal bid, out decimal ask, out long bidSize, out long askSize);

            decimal last = sec.Last;
            int direction = 0;
            if (shares > 0)
            {
                // Price rose → buyers lifted the offer; fell → sellers hit the bid. Produces natural bid/ask bounce.
                direction = logReturn > 0 ? 1 : logReturn < 0 ? -1 : (rng.NextDouble() < 0.5 ? 1 : -1);
                last = direction > 0 ? ask : bid;

                if (sec.DayVolume == 0)
                {
                    sec.DayHigh = last;
                    sec.DayLow = last;
                }
                else
                {
                    if (last > sec.DayHigh) sec.DayHigh = last;
                    if (last < sec.DayLow) sec.DayLow = last;
                }

                sec.DayVolume += shares;
                sec.DayNotional += last * shares;
                sec.Candles.Record(time, last, shares, regular);
            }
            else
            {
                shares = 0;
            }

            sec.Quote = new Quote(bid, ask, bidSize, askSize, last, shares, direction, time);
        }

        /// <summary>Close-to-open jump applied when a new trading day's premarket begins.</summary>
        public void ApplyOvernightGap(IReadOnlyList<SecurityRuntimeState> securities, MarketIndex index, DateTime time)
        {
            double scale = _volNormalizer * _config.OvernightVolatilityRatio;
            double marketGap = _config.MarketDailyVolatility * scale * _marketRng.NextGaussian();
            for (int i = 0; i < _sectorReturns.Length; i++)
                _sectorReturns[i] = _config.SectorDailyVolatility * scale * _marketRng.NextGaussian();

            var profile = IntradayProfile.Evaluate(_config, _schedule, MarketSession.Premarket, time);
            foreach (var sec in securities)
            {
                SecuritySpec spec = sec.Spec;
                sec.FairLog += spec.MarketBeta * marketGap + spec.SectorBeta * _sectorReturns[sec.SectorIndex]
                               + spec.DailyVolatility * scale * sec.Rng.NextGaussian();
                sec.DeviationLog *= _config.OvernightDeviationCarry;
                sec.Momentum = 0;
                SetQuoteWithoutTrade(sec, profile, false, time);
            }

            index.LogLevel += marketGap;
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
