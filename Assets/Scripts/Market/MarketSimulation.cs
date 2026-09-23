using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Market
{
    /// <summary>
    /// Authoritative, headless market. Advances in fixed ticks aligned to a time grid, so the same seed
    /// produces identical results no matter how AdvanceTo calls are chunked (framerate, time scale, skips).
    /// Closed periods are skipped without ticking.
    /// </summary>
    public sealed partial class MarketSimulation : IMarketData
    {
        private readonly List<SecurityRuntimeState> _securities = new List<SecurityRuntimeState>();
        private readonly Dictionary<string, SecurityRuntimeState> _byTicker = new Dictionary<string, SecurityRuntimeState>(StringComparer.Ordinal);
        private readonly PriceEngine _engine;
        private readonly TimeSpan _tick;

        public MarketConfig Config { get; }
        public MarketSchedule Schedule { get; }
        public ulong Seed { get; }

        /// <summary>End time of the last processed tick.</summary>
        public DateTime Now { get; private set; }

        /// <summary>Session of the most recent tick (or of Now while closed).</summary>
        public MarketSession Session { get; private set; }

        public DateTime TradingDate { get; private set; }
        public long TickCount { get; private set; }
        public IReadOnlyList<SecurityRuntimeState> Securities => _securities;
        public MarketIndex Index { get; }

        /// <summary>Published headlines, oldest first. Empty when the simulation was built without news templates.</summary>
        public IReadOnlyList<NewsItem> News => _news != null ? _news.Feed : (IReadOnlyList<NewsItem>)Array.Empty<NewsItem>();

        public event Action Ticked;
        public event Action<MarketSession, MarketSession> SessionChanged;
        public event Action<NewsItem> NewsPublished;

        private readonly NewsEngine _news;
        private readonly Action<NewsItem> _raiseNews;

        /// <param name="newsTemplates">Optional. Without templates there is no news at all (pure factor model).</param>
        /// <param name="scheduledNews">Optional scripted headlines (scenario/tutorial).</param>
        public MarketSimulation(MarketConfig config, IReadOnlyList<SecuritySpec> specs, IndexSpec indexSpec,
            SeededRandomService random, DateTime start,
            IReadOnlyList<NewsTemplate> newsTemplates = null, IReadOnlyList<ScheduledNews> scheduledNews = null)
        {
            // Private copy: editing a settings asset mid-run must not change a running (deterministic) simulation.
            config = config.Clone();
            config.Validate();
            Config = config;
            Schedule = new MarketSchedule(config);
            Seed = random.Seed;
            _tick = TimeSpan.FromSeconds(config.TickSeconds);
            _engine = new PriceEngine(config, Schedule, random.CreateStream("market"));

            long tickTicks = _tick.Ticks;
            Now = start.Date + TimeSpan.FromTicks(start.TimeOfDay.Ticks / tickTicks * tickTicks);
            Session = Schedule.GetSession(Now);
            TradingDate = Now.Date;

            foreach (SecuritySpec spec in specs)
            {
                spec.Validate();
                if (_byTicker.ContainsKey(spec.Ticker))
                    throw new ArgumentException($"Duplicate ticker {spec.Ticker}.");

                var state = new SecurityRuntimeState(spec.Clone(), random.CreateStream("security:" + spec.Ticker), config.MaxCandlesPerSeries);
                _engine.Initialize(state, Session, Now);
                _securities.Add(state);
                _byTicker.Add(spec.Ticker, state);
            }

            Index = new MarketIndex(indexSpec.Clone(), config.MaxCandlesPerSeries);
            _engine.Initialize(Index);

            if (newsTemplates != null && newsTemplates.Count > 0)
            {
                _news = new NewsEngine(config, Schedule, random.CreateStream("news"), newsTemplates, scheduledNews, _securities, Now);
                _raiseNews = item => NewsPublished?.Invoke(item);
            }
        }

        public bool TryGetSecurity(string ticker, out SecurityRuntimeState security) =>
            _byTicker.TryGetValue(ticker, out security);

        public void ReportAggressiveFlow(string ticker, long signedShares)
        {
            if (_byTicker.TryGetValue(ticker, out SecurityRuntimeState sec)) _engine.ApplyExternalFlow(sec, signedShares);
        }

        public bool TryGetQuote(string ticker, out Quote quote)
        {
            if (_byTicker.TryGetValue(ticker, out var sec))
            {
                quote = sec.Quote;
                return true;
            }
            quote = default;
            return false;
        }

        public bool TryGetContract(string ticker, out ContractSpec contract)
        {
            if (_byTicker.TryGetValue(ticker, out var sec))
            {
                contract = ContractSpec.For(sec.Spec);
                return true;
            }
            contract = default;
            return false;
        }

        public void AdvanceTo(DateTime target)
        {
            while (true)
            {
                SyncSession();

                if (Session == MarketSession.Closed)
                {
                    if (Now >= target) return;
                    DateTime next = Schedule.NextSessionStart(Now);
                    Now = next < target ? next : target;
                    continue;
                }

                DateTime end = Now + _tick;
                if (end > target) return;

                _news?.PublishDue(Now, _engine, Index, _raiseNews);
                _engine.Tick(Now, Session, _securities, Index);
                TickCount++;
                Now = end;
                Ticked?.Invoke();
            }
        }

        private void SyncSession()
        {
            MarketSession next = Schedule.GetSession(Now);
            if (next == Session) return;

            MarketSession previous = Session;
            if (previous == MarketSession.Regular)
                PriceEngine.CaptureRegularClose(_securities, Index);
            if (next != MarketSession.Closed && Now.Date != TradingDate)
                BeginTradingDay();

            Session = next;
            SessionChanged?.Invoke(previous, next);
        }

        private void BeginTradingDay()
        {
            TradingDate = Now.Date;
            foreach (var sec in _securities)
            {
                sec.PreviousClose = sec.RegularClose;
                sec.DayVolume = 0;
                sec.DayNotional = 0;
                sec.DayHigh = 0;
                sec.DayLow = 0;
            }
            Index.PreviousClose = Index.RegularClose;
            _engine.ApplyOvernightGap(_securities, Index, Now);
            _news?.PlanRandomDay(Now.Date, Now);
        }
    }
}
