using System;

namespace OpeningBell.Market
{
    /// <summary>What trading code needs from a market. Implemented by MarketSimulation; faked in tests.</summary>
    public interface IMarketData
    {
        DateTime Now { get; }
        MarketSession Session { get; }
        bool TryGetQuote(string ticker, out Quote quote);

        /// <summary>Contract terms (point value, day margin) the symbol trades with.</summary>
        bool TryGetContract(string ticker, out ContractSpec contract);

        /// <summary>Raised after every simulation tick, with quotes already updated.</summary>
        event Action Ticked;

        /// <summary>(previous, current)</summary>
        event Action<MarketSession, MarketSession> SessionChanged;

        /// <summary>Tells the market someone traded aggressively (positive = bought), so large orders have impact.</summary>
        void ReportAggressiveFlow(string ticker, long signedShares); // share-equivalent: contracts × point value
    }
}
