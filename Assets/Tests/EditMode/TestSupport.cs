using System;
using System.Collections.Generic;
using OpeningBell.Core;
using OpeningBell.Market;

namespace OpeningBell.Tests
{
    internal static class TestMarkets
    {
        public static readonly DateTime Monday = new DateTime(2030, 1, 7);

        public static SecuritySpec Spec(string ticker, Sector sector = Sector.Technology, double price = 20,
            double vol = 0.03, double beta = 1, double sectorBeta = 1, long adv = 2_000_000) =>
            new SecuritySpec
            {
                Ticker = ticker,
                CompanyName = ticker + " Corp",
                Sector = sector,
                BasePrice = price,
                DailyVolatility = vol,
                AverageDailyVolume = adv,
                SharesOutstanding = 100_000_000,
                FloatShares = 80_000_000,
                BaseSpreadBps = 5,
                MarketBeta = beta,
                SectorBeta = sectorBeta,
                MomentumCoefficient = 0.1,
                MeanReversionPerDay = 15,
            };

        public static SecuritySpec[] Basic() => new[]
        {
            Spec("AAA", Sector.Technology, 25),
            Spec("BBB", Sector.Energy, 60, vol: 0.02),
            Spec("CCC", Sector.Healthcare, 3.5, vol: 0.06, adv: 800_000),
        };

        public static MarketSimulation Create(ulong seed, DateTime start, SecuritySpec[] specs, MarketConfig config = null) =>
            new MarketSimulation(config ?? new MarketConfig(), specs, new IndexSpec(), new SeededRandomService(seed), start);

        /// <summary>Coarser ticks for long statistical runs; model is calibrated per unit time, so statistics are unchanged.</summary>
        public static MarketConfig FastConfig() => new MarketConfig { TickSeconds = 10 };
    }

    /// <summary>Scriptable market for execution tests.</summary>
    internal sealed class FakeMarketData : IMarketData
    {
        private readonly Dictionary<string, Quote> _quotes = new Dictionary<string, Quote>();

        public DateTime Now { get; set; } = TestMarkets.Monday.AddHours(10);
        public MarketSession Session { get; private set; } = MarketSession.Regular;

        public event Action Ticked;
        public event Action<MarketSession, MarketSession> SessionChanged;

        public bool TryGetQuote(string ticker, out Quote quote) => _quotes.TryGetValue(ticker, out quote);

        /// <summary>Contract terms per symbol; unset symbols trade $1 a point with $10 margin per contract.</summary>
        public readonly Dictionary<string, ContractSpec> Contracts = new Dictionary<string, ContractSpec>();

        public bool TryGetContract(string ticker, out ContractSpec contract)
        {
            if (!_quotes.ContainsKey(ticker)) { contract = default; return false; }
            if (!Contracts.TryGetValue(ticker, out contract)) contract = new ContractSpec(1m, 10m);
            return true;
        }

        /// <summary>Aggressive flow reported by the order manager (signed shares), for impact tests.</summary>
        public readonly List<(string Ticker, long Shares)> ReportedFlow = new List<(string, long)>();

        public void ReportAggressiveFlow(string ticker, long signedShares) => ReportedFlow.Add((ticker, signedShares));

        public void SetQuote(string ticker, decimal bid, decimal ask, long size = 1000, decimal? last = null,
            long lastVolume = 0, int direction = 0, decimal tickHigh = 0m, decimal tickLow = 0m)
        {
            _quotes[ticker] = new Quote(bid, ask, size, size, last ?? (bid + ask) / 2m, lastVolume, direction, Now, tickHigh, tickLow);
        }

        /// <summary>Optionally sets a new quote, then raises a tick.</summary>
        public void Tick()
        {
            Now = Now.AddSeconds(2);
            Ticked?.Invoke();
        }

        public void SetSession(MarketSession session)
        {
            var previous = Session;
            Session = session;
            SessionChanged?.Invoke(previous, session);
        }
    }
}
