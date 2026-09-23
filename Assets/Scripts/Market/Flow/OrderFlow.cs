using System;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>What the price engine tells the order flow about the current step.</summary>
    internal readonly struct FlowStep
    {
        public readonly bool Regular;
        public readonly double MinutesSinceOpen;
        public readonly double ProfileVolume;
        /// <summary>This step's baseline volatility (time of day and activity), before the day and regime.</summary>
        public readonly double Sigma;
        /// <summary>This step's market + sector move of fair value: index arbitrage and hedgers carry it straight into price.</summary>
        public readonly double Systematic;

        public FlowStep(bool regular, double minutesSinceOpen, double profileVolume, double sigma, double systematic)
        {
            Systematic = systematic;
            Regular = regular;
            MinutesSinceOpen = minutesSinceOpen;
            ProfileVolume = profileVolume;
            Sigma = sigma;
        }
    }

    /// <summary>
    /// The order-flow layer: turns the stock's information (fair value) into price through competing participants
    /// and a lightweight book. Each step the groups send aggressive flow (in log-price "push" at normal liquidity):
    ///   noise/retail   fat-tailed random flow, plus chasing the day's move
    ///   value/informed trade toward fair value (how news and the market reach price)
    ///   institutions   meta-orders worked over time, part aggressive, part resting (absorption)
    ///   momentum       follow 5- and 30-minute returns
    ///   mean reversion lean against distance from VWAP
    ///   breakout       pile in after a remembered level breaks
    ///   news traders   hit the tape right after a headline
    /// The net push walks the book: depth (liquidity) turns push into distance; level traders' walls absorb push at
    /// remembered prices; stops just beyond them add push when crossed. So a test of a level can hold (wall absorbs),
    /// break (wall eaten, stops fire, breakout traders pile in), or sweep and fail (stops fire, then the trapped
    /// breakout traders' stops back across fire the other way), and none of it is scripted.
    /// All randomness comes from the stock's own stream, in a fixed order, so runs are reproducible.
    /// </summary>
    internal sealed class OrderFlow
    {
        // Participant weights (tuned against the statistics tests, not outcomes). Noise is per step; the others are
        // persistent pressures, which add up linearly over time, so they are in daily volatilities per session:
        // saturated momentum alone would carry a stock about 1.3 σ over a whole session.
        private const double NoiseWeight = 0.1;
        private const double MomentumWeight = 1.3;
        private const double MeanReversionWeight = 0.6;
        private const double FomoWeight = 0.3;
        /// <summary>Jitter size per step and how fast it fades (seconds).</summary>
        private const double JitterWeight = 0.25, JitterSeconds = 60;
        /// <summary>Informed traders close a gap to fair value with this time constant (minutes).</summary>
        private const double ValueMinutesRegular = 30, ValueMinutesExtended = 90;
        /// <summary>Book depth outside regular hours relative to the regular session.</summary>
        private const double ExtendedLiquidity = 0.6;
        /// <summary>Level traders, stops and breakout traders present outside regular hours, relative to the session.</summary>
        private const double ExtendedParticipation = 0.35;

        // Level interest, in daily-volatility units per unit of level strength.
        private const double WallScale = 0.06;
        private const double StopScale = 0.05;
        private const double BreakoutScale = 0.05;
        private const double StopOffset = 0.035;
        private const double CrossMargin = 0.02;
        /// <summary>Minutes a level must stand before its break brings in breakout traders.</summary>
        private const double EstablishedMinutes = 20;

        // Legs: share of the session spent pausing (chop), and the size of legs in daily volatilities.
        private const double PauseShare = 0.08;
        private const double LegScale = 0.62;

        // Institutions.
        private const double MetaArrivalPerMinute = 0.012;
        private const int MaxMetas = 4;

        private readonly MarketSchedule _schedule;
        private readonly int _stepsPerMinute;
        private readonly double _valueKappaRegular, _valueKappaExtended;
        /// <summary>Steps in a regular session: a persistent pressure of sd/_stepsPerSession moves price 1 σ a session.</summary>
        private readonly double _stepsPerSession;
        private readonly double _jitterDecay;

        public OrderFlow(MarketConfig config, MarketSchedule schedule)
        {
            _schedule = schedule;
            _stepsPerMinute = 60 / config.TickSeconds;
            _stepsPerSession = schedule.RegularSessionSeconds / config.TickSeconds;
            _jitterDecay = Math.Exp(-config.TickSeconds / JitterSeconds);
            _valueKappaRegular = 1 - Math.Exp(-config.TickSeconds / (ValueMinutesRegular * 60));
            _valueKappaExtended = 1 - Math.Exp(-config.TickSeconds / (ValueMinutesExtended * 60));
        }

        // ------------------------------------------------------------------ one step

        /// <summary>Runs one step of flow against the book and returns the new log price.</summary>
        public double Step(SecurityRuntimeState sec, double price, in FlowStep step, out double gross)
        {
            FlowState f = sec.Flow;
            SeededRandom rng = sec.Rng;
            double sd = sec.Spec.DailyVolatility;
            double s = step.Sigma * f.Day.Volatility * RegimeVolatility(f.Regime);
            double unit = sd / _stepsPerSession; // one daily volatility per session
            if (f.Var <= 0) f.Var = s * s;

            // ---- participants
            double noise = s * NoiseWeight * Math.Sqrt(f.Day.Retail) * StudentT4(rng);
            // Texture: fast-reverting jitter (quote flicker, small prints) that shapes candles and wicks but nets out
            // within a few minutes, so it doesn't pile up into chop the way persistent noise does.
            double jitterLevel = f.Jitter * _jitterDecay + s * JitterWeight * StudentT4(rng);
            double jitter = jitterLevel - f.Jitter;
            f.Jitter = jitterLevel;
            double gap = sec.FairLog - price;
            double value = gap * (step.Regular ? _valueKappaRegular : _valueKappaExtended);

            double vol = Math.Sqrt(f.Var) + 1e-12;
            double z5 = f.Mom5 * Math.Sqrt(5 * _stepsPerMinute) / vol;
            double z30 = f.Mom30 * Math.Sqrt(30 * _stepsPerMinute) / vol;
            // Momentum desks are mostly absent outside the session; without this, thin premarket drifts in smooth waves.
            double momentum = MomentumWeight * f.Day.Momentum * RegimeMomentum(f.Regime) * unit * Math.Tanh(0.6 * z5 + 0.4 * z30)
                              * (step.Regular ? 1 : 0.4 * ExtendedParticipation);

            double reversion = 0;
            if (sec.Vwap > 0m)
            {
                double zv = (price - Math.Log((double)sec.Vwap)) / (0.35 * sd);
                reversion = -MeanReversionWeight * f.Day.MeanReversion * RegimeReversion(f.Regime) * unit * Math.Tanh(zv / 2);
            }
            double fomo = sec.PreviousClose > 0m
                ? FomoWeight * f.Day.Retail * unit * Math.Tanh((price - Math.Log((double)sec.PreviousClose)) / sd)
                : 0;

            double burst = f.Burst * 0.06;
            f.Burst -= burst;
            double news = f.NewsFlow * 0.2;
            f.NewsFlow -= news;

            double institutional = 0;
            foreach (MetaOrder m in f.Metas)
            {
                bool pastLimit = m.Side > 0 ? price > m.LimitLog : price < m.LimitLog;
                if (!pastLimit && m.Remaining > 0)
                {
                    double take = Math.Min(m.Remaining, m.Rate * m.Urgency);
                    institutional += m.Side * take;
                    m.Remaining -= take;
                }
                // The resting part refills gradually up to a few minutes' worth.
                double cap = Math.Min(m.Remaining, m.Rate * (1 - m.Urgency) * 90);
                m.Passive = Math.Min(cap, m.Passive + m.Rate * (1 - m.Urgency) * 2);
            }

            // Index arbitrage / hedging: when the market or the sector moves, programs trade the stock with it at once.
            double program = step.Systematic;

            double push = noise + jitter + value + momentum + reversion + fomo + burst + news + institutional + program;
            gross = Math.Abs(noise) + Math.Abs(jitter) + Math.Abs(value) + Math.Abs(momentum) + Math.Abs(reversion) + Math.Abs(fomo)
                    + Math.Abs(burst) + Math.Abs(news) + Math.Abs(institutional) + Math.Abs(program);

            // ---- the book
            double liquidity = f.Day.Liquidity * (step.Regular ? 1 : ExtendedLiquidity) / (1 + f.Withdraw);
            double newPrice = Walk(f, price, push, liquidity, sd, out double absorbed, out double triggered);
            gross += absorbed + triggered;

            // ---- consequences
            CheckCrossings(f, newPrice, sd, step.Regular ? 1 : ExtendedParticipation);
            double r = newPrice - price;
            f.Mom5 += (r - f.Mom5) / (5 * _stepsPerMinute);
            f.Mom30 += (r - f.Mom30) / (30 * _stepsPerMinute);
            f.Var += (r * r - f.Var) / (10 * _stepsPerMinute);
            f.VarSlow += (r * r - f.VarSlow) / (60 * _stepsPerMinute);
            // Market makers step back after violent prints and return over a few minutes (volatility clusters).
            f.Withdraw = Math.Min(1.5, f.Withdraw * 0.97 + 0.04 * Math.Max(0, Math.Abs(r) / (s + 1e-12) - 3.5));
            if (f.GrossEma <= 0) f.GrossEma = gross;
            f.GrossEma += (gross - f.GrossEma) / (20 * _stepsPerMinute);
            return newPrice;
        }

        /// <summary>
        /// Moves price by push/liquidity through whatever rests on the way: walls absorb (price pins at the wall if
        /// they hold), stops add push (acceleration), institutions' resting orders absorb and fill.
        /// </summary>
        private static double Walk(FlowState f, double price, double push, double liquidity, double sd,
            out double absorbed, out double triggered)
        {
            absorbed = triggered = 0;
            f.PathHigh = f.PathLow = price;
            if (push == 0) return price;
            int dir = push > 0 ? 1 : -1;
            double remaining = Math.Abs(push);
            double pos = price;
            double offset = StopOffset * sd;

            for (int iter = 0; iter < 10; iter++)
            {
                // The nearest thing in the way.
                double best = double.MaxValue;
                Level wallLevel = null, stopLevel = null;
                MetaOrder meta = null;
                foreach (Level l in f.Levels.All)
                {
                    if (l.Side != dir) continue;
                    double wallDist = (l.Log - pos) * dir;
                    if (l.Wall > 1e-12 && wallDist >= 0 && wallDist < best) { best = wallDist; wallLevel = l; stopLevel = null; meta = null; }
                    double stopDist = (l.Log + dir * offset - pos) * dir;
                    if (l.Stops > 1e-12 && stopDist >= 0 && stopDist < best) { best = stopDist; stopLevel = l; wallLevel = null; meta = null; }
                }
                foreach (MetaOrder m in f.Metas)
                {
                    if (m.Side == dir || m.Passive <= 1e-12) continue; // only the other side's resting orders stand in the way
                    double peg = Peg(m, price, sd);
                    double d = (peg - pos) * dir;
                    if (d >= 0 && d < best) { best = d; meta = m; wallLevel = stopLevel = null; }
                }

                double reach = remaining / liquidity;
                if (best == double.MaxValue || best > reach)
                {
                    pos += dir * reach;
                    break;
                }

                pos += dir * best;
                remaining -= best * liquidity;
                if (wallLevel != null)
                {
                    double take = Math.Min(remaining, wallLevel.Wall);
                    wallLevel.Wall -= take;
                    remaining -= take;
                    absorbed += take;
                    if (remaining <= 1e-12) break; // held: price pins at the level
                }
                else if (stopLevel != null)
                {
                    remaining += stopLevel.Stops;
                    triggered += stopLevel.Stops;
                    stopLevel.Stops = 0;
                }
                else if (meta != null)
                {
                    double take = Math.Min(remaining, meta.Passive);
                    meta.Passive -= take;
                    meta.Remaining -= take;
                    remaining -= take;
                    absorbed += take;
                    if (remaining <= 1e-12) break; // absorbed by the institution
                }
                if (pos > f.PathHigh) f.PathHigh = pos;
                if (pos < f.PathLow) f.PathLow = pos;
            }

            if (pos > f.PathHigh) f.PathHigh = pos;
            if (pos < f.PathLow) f.PathLow = pos;
            return pos;
        }

        /// <summary>Where an institution's resting order sits: just off the price, or at its limit once price runs away.</summary>
        private static double Peg(MetaOrder m, double price, double sd)
        {
            double peg = price - m.Side * 0.03 * sd;
            return m.Side > 0 ? Math.Min(peg, m.LimitLog) : Math.Max(peg, m.LimitLog);
        }

        /// <summary>
        /// A level that price has cleanly crossed changes role: breakout traders pile in the new direction, the old
        /// wall is gone, and the breakout traders' own stops now rest back across it (fuel if the break fails).
        /// </summary>
        private static void CheckCrossings(FlowState f, double price, double sd, double participation)
        {
            foreach (Level l in f.Levels.All)
            {
                int side = price > l.Log ? -1 : 1;
                if (side == l.Side || Math.Abs(price - l.Log) < CrossMargin * sd) continue;
                int dir = -side; // the direction price broke
                if (l.Age < EstablishedMinutes)
                {
                    // A level still being made (today's high as it extends, the premarket high during premarket) is
                    // not a breakout when price moves past it: nobody has been watching it yet.
                    l.Side = side;
                    continue;
                }
                // Breakout traders act on a fresh break; each re-cross of the same level draws fewer of them, and only
                // the first break leaves trapped traders' stops behind.
                double freshness = 1.0 / (1 + 1.5 * l.Touches);
                f.Burst += dir * Math.Min(1, l.Strength) * BreakoutScale * sd * f.Day.Breakout * RegimeBreakout(f.Regime) * freshness * participation;
                l.Side = side;
                l.Wall = 0;
                l.Stops = l.Touches == 0 ? Math.Min(1, l.Strength) * StopScale * sd : 0;
                l.Touches++;
                if (l.Strength > 0.7 && l.Touches == 1 && (f.Regime == Regime.Compression || f.Regime == Regime.Range))
                {
                    f.Regime = Regime.Expansion;
                    f.RegimeMinutes = 0;
                }
            }
        }

        // ------------------------------------------------------------------ every minute

        /// <summary>
        /// Slow bookkeeping, once a simulated minute: levels regrow and fade, institutions arrive and finish, the
        /// regime may change, and session levels (premarket, opening range, swings, day extremes) are recorded.
        /// </summary>
        public void Minute(SecurityRuntimeState sec, double price, in FlowStep step)
        {
            FlowState f = sec.Flow;
            SeededRandom rng = sec.Rng;
            double sd = sec.Spec.DailyVolatility;
            f.RegimeMinutes++;

            // Levels: interest regrows toward what the level deserves (slowly near price, where it was just tested).
            // Outside regular hours few traders are around: less resting interest and fewer stops at levels.
            double participation = step.Regular ? 1 : ExtendedParticipation;
            double respect = f.Day.LevelRespect * RegimeRespect(f.Regime) * participation;
            foreach (Level l in f.Levels.Mutable)
            {
                l.Age++;
                l.Strength *= l.Kind == LevelKind.SwingHigh || l.Kind == LevelKind.SwingLow ? 0.9993 : 0.99985;
                bool near = Math.Abs(price - l.Log) < 0.15 * sd;
                double wallTarget = l.Strength * WallScale * sd * respect;
                l.Wall += (wallTarget - l.Wall) * (near ? 0.02 : 0.12);
                double stopTarget = l.Strength * StopScale * sd * (1 + 0.3 * Math.Min(l.Touches, 4)) * f.Day.Retail
                                    * (f.Regime == Regime.Compression ? 1.5 : 1) * participation;
                l.Stops += (stopTarget - l.Stops) * 0.05;
            }
            f.Levels.Mutable.RemoveAll(l => l.Strength < 0.08);

            // Institutions: finished orders leave; new ones arrive, mostly on the side fair value points to.
            f.Metas.RemoveAll(m => m.Remaining <= 1e-9 && m.Passive <= 1e-9);
            double arrival = MetaArrivalPerMinute * f.Day.Institutional * Math.Sqrt(step.ProfileVolume);
            if (f.Metas.Count < MaxMetas && rng.NextDouble() < arrival)
                f.Metas.Add(NewMeta(sec, price, rng, step));

            // Legs: the day's direction arrives in impulses and pullbacks, a range day's in rotations.
            if (!step.Regular) { f.Leg = LegKind.Pause; f.LegRate = 0; f.LegMinutes = 0; }
            else if (--f.LegMinutes <= 0) NewLeg(sec, price, rng, step);

            UpdateRegime(f, rng);
            TrackSession(sec, price, step);
            DetectSwings(sec, price);
        }

        /// <summary>
        /// Draws the next leg. Trend days send most legs with the trend (impulses on heavy volume) and some against it
        /// (shallower pullbacks on light volume); range days rotate, mostly back toward VWAP. About a fifth of the
        /// session is pauses, where only the other participants trade: that is the chop.
        /// A leg moves fair value and price together (like program flow), so value traders don't fade it back.
        /// </summary>
        private void NewLeg(SecurityRuntimeState sec, double price, SeededRandom rng, in FlowStep step)
        {
            FlowState f = sec.Flow;
            double sd = sec.Spec.DailyVolatility;
            double drift = f.Day.DriftAt(step.MinutesSinceOpen);
            double minutes, move;
            int dir;
            if (rng.NextDouble() < PauseShare)
            {
                f.Leg = LegKind.Pause;
                minutes = 5 + 12 * rng.NextDouble();
                move = 0;
                dir = 0;
            }
            else if (Math.Abs(drift) > 0.4)
            {
                double pWith = Math.Min(0.68, 0.5 + 0.08 * Math.Abs(drift));
                bool with = rng.NextDouble() < pWith;
                dir = with ? Math.Sign(drift) : -Math.Sign(drift);
                f.Leg = with ? LegKind.Impulse : LegKind.Pullback;
                minutes = with ? 10 + 25 * rng.NextDouble() : 6 + 14 * rng.NextDouble();
                move = with ? 0.16 + 0.16 * rng.NextDouble() : 0.12 + 0.14 * rng.NextDouble();
            }
            else
            {
                double zv = sec.Vwap > 0m ? (price - Math.Log((double)sec.Vwap)) / (0.3 * sd) : 0;
                dir = rng.NextDouble() < 1 / (1 + Math.Exp(1.5 * zv)) ? 1 : -1;
                f.Leg = LegKind.Rotation;
                minutes = 8 + 20 * rng.NextDouble();
                move = 0.08 + 0.12 * rng.NextDouble();
            }
            minutes = Math.Round(minutes);
            f.LegMinutes = minutes;
            f.LegRate = dir * move * LegScale * sd * f.Day.Volatility / (minutes * _stepsPerMinute);
        }

        /// <summary>Volume relative to normal during each kind of leg: impulses print heavy, pullbacks and pauses light.</summary>
        internal static double LegVolume(LegKind leg) => leg switch
        {
            LegKind.Impulse => 1.5,
            LegKind.Rotation => 1.05,
            LegKind.Pullback => 0.75,
            _ => 0.7,
        };

        private MetaOrder NewMeta(SecurityRuntimeState sec, double price, SeededRandom rng, in FlowStep step)
        {
            FlowState f = sec.Flow;
            double sd = sec.Spec.DailyVolatility;
            double gap = (sec.FairLog - price) / (0.25 * sd);
            double lean = step.Regular ? Math.Sign(f.Day.DriftAt(step.MinutesSinceOpen)) * 0.6 : 0;
            double pBuy = 1 / (1 + Math.Exp(-(gap + lean)));
            int side = rng.NextDouble() < pBuy ? 1 : -1;
            double total = sd * 0.25 * Math.Exp(0.7 * rng.NextGaussian() - 0.25);
            double minutes = 8 + 50 * rng.NextDouble();
            return new MetaOrder
            {
                Side = side,
                Remaining = total,
                Rate = total / (minutes * _stepsPerMinute),
                Urgency = 0.25 + 0.6 * rng.NextDouble(),
                LimitLog = price + side * sd * (0.25 + 0.5 * rng.NextDouble()),
            };
        }

        /// <summary>A news headline: news traders hit the tape now; informed institutions follow for longer.</summary>
        public void OnNews(SecurityRuntimeState sec, double immediate, double price)
        {
            FlowState f = sec.Flow;
            f.NewsFlow += immediate;
            double sd = sec.Spec.DailyVolatility;
            // A real headline puts the stock "in play": the rest of the day trades heavier (up to 3× relative volume).
            f.Day.RelativeVolume *= 1 + Math.Min(2, 2.5 * Math.Abs(immediate) / sd);
            if (Math.Abs(immediate) > 0.2 * sd && f.Metas.Count < MaxMetas)
            {
                int side = Math.Sign(immediate);
                double total = Math.Abs(immediate) * 0.5;
                f.Metas.Add(new MetaOrder
                {
                    Side = side, Remaining = total, Rate = total / (20 * _stepsPerMinute), Urgency = 0.8,
                    LimitLog = price + side * (Math.Abs(immediate) * 2 + 0.3 * sd),
                });
            }
            if (Math.Abs(immediate) > 0.3 * sd && f.Regime != Regime.Expansion)
            {
                f.Regime = Regime.Expansion;
                f.RegimeMinutes = 0;
            }
        }

        // ------------------------------------------------------------------ regimes

        private static double RegimeVolatility(Regime r) => r switch
        {
            Regime.Compression => 0.6,
            Regime.Expansion => 1.45,
            Regime.Trend => 1.05,
            _ => 0.9,
        };

        private static double RegimeMomentum(Regime r) => r switch
        {
            Regime.Trend => 1.5,
            Regime.Expansion => 1.3,
            Regime.Compression => 0.6,
            _ => 0.7,
        };

        private static double RegimeReversion(Regime r) => r switch
        {
            Regime.Range => 1.35,
            Regime.Compression => 1.2,
            Regime.Trend => 0.6,
            _ => 0.7,
        };

        private static double RegimeRespect(Regime r) => r switch
        {
            Regime.Range => 1.3,
            Regime.Compression => 1.1,
            Regime.Expansion => 0.7,
            _ => 0.85,
        };

        private static double RegimeBreakout(Regime r) => r switch
        {
            Regime.Range => 0.7,
            Regime.Expansion => 1.3,
            Regime.Trend => 1.2,
            _ => 1,
        };

        /// <summary>
        /// Regimes persist (at least a quarter of an hour) and change on conditions: compression builds pressure until
        /// it expands, expansion settles into a trend if the move keeps going or a range if it doesn't, trends tire.
        /// </summary>
        private void UpdateRegime(FlowState f, SeededRandom rng)
        {
            double u = rng.NextDouble();
            if (f.RegimeMinutes < 15) return;
            double z30 = Math.Abs(f.Mom30) * Math.Sqrt(30 * _stepsPerMinute) / (Math.Sqrt(f.Var) + 1e-12);
            Regime next = f.Regime;
            switch (f.Regime)
            {
                case Regime.Range:
                    if (z30 > 1.6 && u < 0.3) next = Regime.Trend;
                    else if (u < 0.012) next = Regime.Compression;
                    break;
                case Regime.Compression:
                    if (u < 0.004 + 0.0012 * (f.RegimeMinutes - 15)) next = Regime.Expansion;
                    break;
                case Regime.Expansion:
                    if (z30 > 1.2 && u < 0.25) next = Regime.Trend;
                    else if (u < 0.03) next = Regime.Range;
                    break;
                case Regime.Trend:
                    if (u < 0.006 + (z30 < 0.4 ? 0.03 : 0)) next = Regime.Range;
                    break;
            }
            if (next != f.Regime)
            {
                f.Regime = next;
                f.RegimeMinutes = 0;
            }
        }

        // ------------------------------------------------------------------ memory

        /// <summary>Premarket range, opening range and today's extremes, as levels the market remembers.</summary>
        private static void TrackSession(SecurityRuntimeState sec, double price, in FlowStep step)
        {
            FlowState f = sec.Flow;
            if (!step.Regular)
            {
                if (step.MinutesSinceOpen < 0 && double.IsNaN(f.SessionOpen))
                {
                    // Premarket: its high and low become levels, more watched the longer they stand.
                    if (double.IsNaN(f.PremarketHigh) || price > f.PremarketHigh) f.PremarketHigh = price;
                    if (double.IsNaN(f.PremarketLow) || price < f.PremarketLow) f.PremarketLow = price;
                    double strength = Math.Max(0.2, Math.Min(0.8, 0.2 + 0.6 * (330 + step.MinutesSinceOpen) / 330));
                    Level pmh = f.Levels.Track(LevelKind.PremarketHigh, f.PremarketHigh, strength, 1);
                    Level pml = f.Levels.Track(LevelKind.PremarketLow, f.PremarketLow, strength, -1);
                    if (pmh != null) pmh.Strength = strength;
                    if (pml != null) pml.Strength = strength;
                }
                return;
            }

            if (double.IsNaN(f.SessionHigh) || price > f.SessionHigh) f.SessionHigh = price;
            if (double.IsNaN(f.SessionLow) || price < f.SessionLow) f.SessionLow = price;
            int m = (int)Math.Round(step.MinutesSinceOpen);
            if (m == 5)
            {
                f.Levels.Add(LevelKind.OpeningRangeHigh, f.SessionHigh, 0.55, 1, 0.02 * sec.Spec.DailyVolatility);
                f.Levels.Add(LevelKind.OpeningRangeLow, f.SessionLow, 0.55, -1, 0.02 * sec.Spec.DailyVolatility);
            }
            else if (m == 30)
            {
                f.Levels.Add(LevelKind.OpeningRangeHigh, f.SessionHigh, 0.7, 1, 0.02 * sec.Spec.DailyVolatility);
                f.Levels.Add(LevelKind.OpeningRangeLow, f.SessionLow, 0.7, -1, 0.02 * sec.Spec.DailyVolatility);
            }
            if (m >= 15)
            {
                // Stops collect above the high of day and below the low of day.
                f.Levels.Track(LevelKind.DayHigh, f.SessionHigh, 0.5, 1);
                f.Levels.Track(LevelKind.DayLow, f.SessionLow, 0.5, -1);
            }
        }

        /// <summary>Swing highs/lows from 5-minute bars (a bar with two lower highs, or higher lows, each side).</summary>
        private static void DetectSwings(SecurityRuntimeState sec, double price)
        {
            FlowState f = sec.Flow;
            CandleSeries bars = sec.Candles.Get(Timeframe.Minute5);
            int n = bars.Completed.Count;
            if (n < 5 || bars.Completed[n - 1].Start.Ticks == f.LastSwingTicks) return;
            f.LastSwingTicks = bars.Completed[n - 1].Start.Ticks;
            Candle c = bars.Completed[n - 3];
            double sd = sec.Spec.DailyVolatility;
            bool high = true, low = true;
            for (int k = n - 5; k < n; k++)
            {
                if (k == n - 3) continue;
                Candle o = bars.Completed[k];
                if (o.High >= c.High) high = false;
                if (o.Low <= c.Low) low = false;
            }
            if (high)
            {
                double log = Math.Log((double)c.High);
                double size = (log - Math.Log((double)Math.Min(bars.Completed[n - 5].Low, bars.Completed[n - 1].Low))) / sd;
                f.Levels.Add(LevelKind.SwingHigh, log, Math.Min(0.8, 0.25 + size), price > log ? -1 : 1, 0.04 * sd);
            }
            if (low)
            {
                double log = Math.Log((double)c.Low);
                double size = (Math.Log((double)Math.Max(bars.Completed[n - 5].High, bars.Completed[n - 1].High)) - log) / sd;
                f.Levels.Add(LevelKind.SwingLow, log, Math.Min(0.8, 0.25 + size), price > log ? -1 : 1, 0.04 * sd);
            }
        }

        // ------------------------------------------------------------------ day roll

        /// <summary>
        /// A new trading day: yesterday's session becomes levels (PDH, PDL, close; the week's range), older levels
        /// fade, round numbers near the price are refreshed, institutions reassess, and a new hidden day is drawn.
        /// </summary>
        public void StartDay(SecurityRuntimeState sec, double price, DayProfile day)
        {
            FlowState f = sec.Flow;
            double sd = sec.Spec.DailyVolatility;
            LevelBook book = f.Levels;
            foreach (LevelKind k in new[]
                     {
                         LevelKind.PreviousDayHigh, LevelKind.PreviousDayLow, LevelKind.PreviousClose, LevelKind.PreviousWeekHigh,
                         LevelKind.PreviousWeekLow, LevelKind.PremarketHigh, LevelKind.PremarketLow, LevelKind.OpeningRangeHigh,
                         LevelKind.OpeningRangeLow, LevelKind.DayHigh, LevelKind.DayLow, LevelKind.RoundNumber,
                     })
                book.Remove(k);
            foreach (Level l in book.Mutable) l.Strength *= 0.8;

            if (!double.IsNaN(f.SessionHigh))
            {
                book.Add(LevelKind.PreviousDayHigh, f.SessionHigh, 0.85, 1, 0.02 * sd);
                book.Add(LevelKind.PreviousDayLow, f.SessionLow, 0.85, -1, 0.02 * sd);
            }
            if (sec.RegularClose > 0m) book.Add(LevelKind.PreviousClose, Math.Log((double)sec.RegularClose), 0.45, 1, 0.01 * sd);
            var days = sec.Candles.Get(Timeframe.Day1).Completed;
            if (days.Count >= 2)
            {
                decimal hi = 0m, lo = decimal.MaxValue;
                for (int i = Math.Max(0, days.Count - 5); i < days.Count; i++)
                {
                    if (days[i].High > hi) hi = days[i].High;
                    if (days[i].Low < lo) lo = days[i].Low;
                }
                book.Add(LevelKind.PreviousWeekHigh, Math.Log((double)hi), 0.7, 1, 0.03 * sd);
                book.Add(LevelKind.PreviousWeekLow, Math.Log((double)lo), 0.7, -1, 0.03 * sd);
            }

            // Round numbers: the two nearest on each side (whole numbers get more attention than halves).
            double p = Math.Exp(price);
            double step = RoundStep(p);
            double first = Math.Floor(p / step) * step;
            for (int i = -1; i <= 2; i++)
            {
                double level = first + i * step;
                if (level <= 0) continue;
                bool major = Math.Abs(level / (step * 2) - Math.Round(level / (step * 2))) < 1e-9;
                book.Add(LevelKind.RoundNumber, Math.Log(level), major ? 0.5 : 0.3, 1, 0.005 * sd);
            }

            foreach (Level l in book.Mutable)
            {
                l.Side = price > l.Log ? -1 : 1;
                l.Wall = l.Strength * WallScale * sd;
                l.Stops = l.Strength * StopScale * sd;
            }

            f.Day = day;
            f.Regime = Regime.Range;
            f.RegimeMinutes = 0;
            f.Metas.Clear();
            f.Burst = 0;
            f.PremarketHigh = f.PremarketLow = f.SessionHigh = f.SessionLow = f.SessionOpen = double.NaN;
            f.OpenAuctionDone = false;
            var fives = sec.Candles.Get(Timeframe.Minute5).Completed;
            f.LastSwingTicks = fives.Count > 0 ? fives[fives.Count - 1].Start.Ticks : 0;
        }

        /// <summary>
        /// The opening bell: orders queued overnight meet in the opening cross (part of the gap to fair value goes
        /// through at once), and the day's character sets the first regime.
        /// </summary>
        public void OnOpen(SecurityRuntimeState sec, double price)
        {
            FlowState f = sec.Flow;
            f.OpenAuctionDone = true;
            f.SessionOpen = price;
            f.NewsFlow += 0.5 * (sec.FairLog - price);
            f.Regime = f.Day.Type switch
            {
                DayType.TrendUp or DayType.TrendDown or DayType.Recovery or DayType.Panic => Regime.Trend,
                DayType.GapAndGo or DayType.GapAndFade or DayType.Squeeze or DayType.Chop => Regime.Expansion,
                DayType.Grind => Regime.Compression,
                _ => Regime.Range,
            };
            f.RegimeMinutes = 0;
        }

        private static double RoundStep(double p) =>
            p < 5 ? 0.5 : p < 20 ? 1 : p < 50 ? 2.5 : p < 200 ? 5 : p < 1000 ? 25 : 100;

        // ------------------------------------------------------------------ noise

        /// <summary>Unit-variance Student-t with 4 degrees of freedom: fat-tailed order sizes and arrivals.</summary>
        private static double StudentT4(SeededRandom rng)
        {
            double z = rng.NextGaussian();
            double chi = 0;
            for (int i = 0; i < 4; i++)
            {
                double g = rng.NextGaussian();
                chi += g * g;
            }
            return z / Math.Sqrt(chi / 4) / Math.Sqrt(2);
        }
    }
}
