using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>
    /// Plans (scheduled + random) and publishes news, turning each headline into catalysts for the price engine.
    /// Uses its own random stream, so news never changes the price paths of securities it does not touch.
    /// </summary>
    internal sealed class NewsEngine
    {
        private sealed class Planned
        {
            public DateTime Time;
            public long Sequence;
            public NewsTemplate Template;
            public SecurityRuntimeState Target;
            public Sector Sector;
            public double Severity = double.NaN;
        }

        private readonly MarketConfig _config;
        private readonly MarketSchedule _schedule;
        private readonly SeededRandom _rng;
        private readonly IReadOnlyList<SecurityRuntimeState> _securities;
        private readonly Dictionary<string, NewsTemplate> _templates = new Dictionary<string, NewsTemplate>(StringComparer.Ordinal);
        private readonly List<NewsTemplate> _templateList;
        private readonly List<Planned> _queue = new List<Planned>();
        private readonly List<NewsItem> _feed = new List<NewsItem>();
        private readonly List<string> _tickers = new List<string>();
        private long _nextId = 1;
        private long _sequence;

        public IReadOnlyList<NewsItem> Feed => _feed;

        public NewsEngine(MarketConfig config, MarketSchedule schedule, SeededRandom rng, IReadOnlyList<NewsTemplate> templates,
            IReadOnlyList<ScheduledNews> scheduled, IReadOnlyList<SecurityRuntimeState> securities, DateTime start)
        {
            _config = config;
            _schedule = schedule;
            _rng = rng;
            _securities = securities;
            _templateList = new List<NewsTemplate>(templates);
            foreach (NewsTemplate t in _templateList)
            {
                t.Validate();
                if (_templates.ContainsKey(t.Id)) throw new ArgumentException($"Duplicate news template id {t.Id}.");
                _templates.Add(t.Id, t);
            }

            if (scheduled != null)
                foreach (ScheduledNews s in scheduled)
                    AddScheduled(s, start);

            if (schedule.IsTradingDay(start.Date)) PlanRandomDay(start.Date, start);
        }

        private void AddScheduled(ScheduledNews s, DateTime start)
        {
            if (!_templates.TryGetValue(s.TemplateId, out NewsTemplate template))
                throw new ArgumentException($"Scheduled news references unknown template '{s.TemplateId}'.");

            var planned = new Planned
            {
                Time = start.Date.AddDays(s.DayOffset).AddMinutes(s.MinuteOfDay),
                Template = template,
                Sector = s.Sector,
                Severity = s.Severity > 0 ? s.Severity : double.NaN,
            };
            if (planned.Time < start) return;

            if (template.Scope == NewsScope.Security)
            {
                planned.Target = Find(s.Ticker) ?? throw new ArgumentException($"Scheduled news targets unknown ticker '{s.Ticker}'.");
            }
            Enqueue(planned);
        }

        /// <summary>Draws the day's random headlines. Called when a trading day begins.</summary>
        public void PlanRandomDay(DateTime date, DateTime notBefore)
        {
            PlanScope(date, notBefore, NewsScope.Security, _config.SecurityNewsPerDay);
            PlanScope(date, notBefore, NewsScope.Sector, _config.SectorNewsPerDay);
            PlanScope(date, notBefore, NewsScope.Market, _config.MarketNewsPerDay);
        }

        private void PlanScope(DateTime date, DateTime notBefore, NewsScope scope, double perDay)
        {
            int count = Poisson(perDay);
            for (int i = 0; i < count; i++)
            {
                var planned = new Planned { Time = RandomNewsTime(date) };
                if (scope == NewsScope.Security)
                {
                    planned.Target = _securities[_rng.NextInt(_securities.Count)];
                    planned.Template = PickTemplate(scope, planned.Target.Spec.Sector);
                }
                else
                {
                    planned.Sector = _securities[_rng.NextInt(_securities.Count)].Spec.Sector;
                    planned.Template = PickTemplate(scope, planned.Sector);
                }
                if (planned.Template != null && planned.Time >= notBefore) Enqueue(planned);
            }
        }

        /// <summary>Publishes everything due at or before <paramref name="now"/> and applies its catalysts.</summary>
        public void PublishDue(DateTime now, PriceEngine engine, MarketIndex index, Action<NewsItem> published)
        {
            while (_queue.Count > 0 && _queue[0].Time <= now)
            {
                Planned p = _queue[0];
                _queue.RemoveAt(0);
                NewsItem item = Publish(p, now, engine, index);
                _feed.Add(item);
                published?.Invoke(item);
            }
        }

        private NewsItem Publish(Planned p, DateTime now, PriceEngine engine, MarketIndex index)
        {
            NewsTemplate t = p.Template;
            double severity = double.IsNaN(p.Severity)
                ? t.MinSeverity + (t.MaxSeverity - t.MinSeverity) * _rng.NextDouble()
                : p.Severity;

            _tickers.Clear();
            string headline;
            double move;
            switch (t.Scope)
            {
                case NewsScope.Security:
                {
                    SecuritySpec spec = p.Target.Spec;
                    move = DrawReaction(t, severity, spec.DailyVolatility);
                    engine.ApplyNews(p.Target, move, DrawOverreaction(move), severity, 1.0);
                    _tickers.Add(spec.Ticker);
                    headline = Format(t.Headline, spec.CompanyName, spec.Ticker, spec.Sector);
                    break;
                }
                case NewsScope.Sector:
                {
                    move = DrawReaction(t, severity, _config.SectorDailyVolatility);
                    foreach (SecurityRuntimeState s in _securities)
                    {
                        if (s.Spec.Sector != p.Sector) continue;
                        double shift = move * s.Spec.SectorBeta;
                        engine.ApplyNews(s, shift, DrawOverreaction(shift), severity, 0.5);
                        _tickers.Add(s.Ticker);
                    }
                    headline = Format(t.Headline, "", "", p.Sector);
                    break;
                }
                default:
                {
                    move = DrawReaction(t, severity, _config.MarketDailyVolatility);
                    engine.ApplyIndexNews(index, move);
                    foreach (SecurityRuntimeState s in _securities)
                    {
                        double shift = move * s.Spec.MarketBeta;
                        engine.ApplyNews(s, shift, DrawOverreaction(shift), severity, 0.3);
                    }
                    // Sector tilts: an extra move of the sector's own on top of the market's. Drawn only for
                    // templates that have them, so older headlines keep their random streams.
                    if (t.Tilts != null)
                        foreach (SectorTilt tilt in t.Tilts)
                        {
                            double sectorMove = DrawReaction(tilt.Bias, t.Uncertainty, severity, _config.SectorDailyVolatility);
                            foreach (SecurityRuntimeState s in _securities)
                            {
                                if (s.Spec.Sector != tilt.Sector) continue;
                                double shift = sectorMove * s.Spec.SectorBeta;
                                engine.ApplyNews(s, shift, DrawOverreaction(shift), severity, 0.5);
                                if (!_tickers.Contains(s.Ticker)) _tickers.Add(s.Ticker);
                            }
                        }
                    headline = Format(t.Headline, "", "", default);
                    break;
                }
            }

            return new NewsItem(_nextId++, p.Time, headline, t.Type, t.Scope, _tickers.ToArray(), severity, move);
        }

        /// <summary>
        /// Permanent log-move for one event. Magnitude scales with the target's own volatility (a big-cap moves
        /// less than a biotech on equally big news). The share of the expected move that shows up varies (some is
        /// already priced in), and uncertainty adds symmetric noise, so direction is likely but never guaranteed.
        /// </summary>
        internal double DrawReaction(NewsTemplate t, double severity, double dailyVolatility) =>
            DrawReaction(t.Bias, t.Uncertainty, severity, dailyVolatility);

        private double DrawReaction(double bias, double uncertainty, double severity, double dailyVolatility)
        {
            if (severity <= 0) return 0;
            double magnitude = severity * _config.NewsImpactDailyVols * dailyVolatility;
            double expected = bias * magnitude * (0.4 + 0.9 * _rng.NextDouble());
            return expected + uncertainty * magnitude * _rng.NextGaussian();
        }

        /// <summary>
        /// Transient overshoot added to the mean-reverting deviation: positive = pop then fade (reversal),
        /// negative = under-reaction then grind (continuation).
        /// </summary>
        private double DrawOverreaction(double move) => move * (-0.3 + 1.1 * _rng.NextDouble());

        /// <summary>Premarket-heavy timing: most company news lands before the open or after the close.</summary>
        private DateTime RandomNewsTime(DateTime date)
        {
            double u = _rng.NextDouble();
            TimeSpan from, to;
            if (u < 0.4) { from = _schedule.RegularOpen - TimeSpan.FromMinutes(150); to = _schedule.RegularOpen; }
            else if (u < 0.8) { from = _schedule.RegularOpen; to = _schedule.RegularClose; }
            else { from = _schedule.RegularClose; to = _schedule.RegularClose + TimeSpan.FromMinutes(120); }

            double minutes = from.TotalMinutes + (to - from).TotalMinutes * _rng.NextDouble();
            return date.AddMinutes(Math.Floor(minutes));
        }

        private NewsTemplate PickTemplate(NewsScope scope, Sector sector)
        {
            double total = 0;
            foreach (NewsTemplate t in _templateList)
                if (t.Scope == scope && t.AppliesTo(sector)) total += t.Weight;
            if (total <= 0) return null;

            double pick = _rng.NextDouble() * total;
            foreach (NewsTemplate t in _templateList)
            {
                if (t.Scope != scope || !t.AppliesTo(sector)) continue;
                pick -= t.Weight;
                if (pick < 0) return t;
            }
            return null;
        }

        private int Poisson(double lambda)
        {
            if (lambda <= 0) return 0;
            double limit = Math.Exp(-lambda), p = 1;
            int k = 0;
            do
            {
                k++;
                p *= _rng.NextDouble();
            } while (p > limit);
            return k - 1;
        }

        private void Enqueue(Planned planned)
        {
            planned.Sequence = _sequence++;
            int i = _queue.Count;
            while (i > 0 && Later(_queue[i - 1], planned)) i--;
            _queue.Insert(i, planned);
        }

        private static bool Later(Planned a, Planned b) => a.Time > b.Time || (a.Time == b.Time && a.Sequence > b.Sequence);

        private SecurityRuntimeState Find(string ticker)
        {
            foreach (var s in _securities)
                if (s.Ticker == ticker) return s;
            return null;
        }

        internal NewsSaveData Capture(int maxFeed)
        {
            var data = new NewsSaveData { Rng = _rng.CaptureState(), NextId = _nextId, Sequence = _sequence };
            foreach (Planned p in _queue)
            {
                data.Queue.Add(new PlannedNewsSaveData
                {
                    Time = p.Time.Ticks, Sequence = p.Sequence, TemplateId = p.Template.Id,
                    Ticker = p.Target?.Ticker ?? "", Sector = (int)p.Sector, SeverityBits = SaveCodec.Bits(p.Severity),
                });
            }
            for (int i = Math.Max(0, _feed.Count - maxFeed); i < _feed.Count; i++)
            {
                NewsItem n = _feed[i];
                var tickers = new string[n.Tickers.Count];
                for (int t = 0; t < tickers.Length; t++) tickers[t] = n.Tickers[t];
                data.Feed.Add(new NewsItemSaveData
                {
                    Id = n.Id, Time = n.Time.Ticks, Headline = n.Headline, Type = (int)n.Type, Scope = (int)n.Scope,
                    Tickers = tickers, SeverityBits = SaveCodec.Bits(n.Severity), MoveBits = SaveCodec.Bits(n.RealizedMove),
                });
            }
            return data;
        }

        /// <summary>Planned items whose template or target no longer exists are dropped.</summary>
        internal void Restore(NewsSaveData data)
        {
            _rng.RestoreState(data.Rng);
            _nextId = data.NextId;
            _sequence = data.Sequence;

            _queue.Clear();
            foreach (PlannedNewsSaveData q in data.Queue)
            {
                if (!_templates.TryGetValue(q.TemplateId, out NewsTemplate template)) continue;
                SecurityRuntimeState target = string.IsNullOrEmpty(q.Ticker) ? null : Find(q.Ticker);
                if (template.Scope == NewsScope.Security && target == null) continue;
                _queue.Add(new Planned
                {
                    Time = new DateTime(q.Time), Sequence = q.Sequence, Template = template, Target = target,
                    Sector = (Sector)q.Sector, Severity = SaveCodec.Double(q.SeverityBits),
                });
            }

            _feed.Clear();
            foreach (NewsItemSaveData f in data.Feed)
                _feed.Add(new NewsItem(f.Id, new DateTime(f.Time), f.Headline, (CatalystType)f.Type, (NewsScope)f.Scope,
                    f.Tickers ?? Array.Empty<string>(), SaveCodec.Double(f.SeverityBits), SaveCodec.Double(f.MoveBits)));
        }

        private static string Format(string headline, string company, string ticker, Sector sector) =>
            headline.Replace("{company}", company).Replace("{ticker}", ticker).Replace("{sector}", sector.ToString());
    }
}
