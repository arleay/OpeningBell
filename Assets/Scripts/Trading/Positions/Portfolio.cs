using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    public sealed class Portfolio
    {
        private readonly Dictionary<string, Position> _byTicker = new Dictionary<string, Position>(StringComparer.Ordinal);
        private readonly List<Position> _positions = new List<Position>();

        /// <summary>Every position ever traded, including flat ones (they keep their realized P&L).</summary>
        public IReadOnlyList<Position> Positions => _positions;

        public Position Find(string ticker) => _byTicker.TryGetValue(ticker, out var p) ? p : null;

        public long QuantityOf(string ticker) => Find(ticker)?.Quantity ?? 0;

        public decimal RealizedPnL
        {
            get
            {
                decimal total = 0m;
                foreach (var p in _positions) total += p.RealizedPnL;
                return total;
            }
        }

        internal Position GetOrCreate(string ticker)
        {
            if (!_byTicker.TryGetValue(ticker, out var position))
            {
                position = new Position(ticker);
                _byTicker.Add(ticker, position);
                _positions.Add(position);
            }
            return position;
        }
    }
}
