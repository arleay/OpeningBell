using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    /// <summary>
    /// The hidden book several order managers take from within one tick. Each <see cref="OrderManager"/> walks the book
    /// from the inside quote; without this, ten desks of one firm buying the same symbol in the same tick would each get
    /// the best price. With it, the second desk starts where the first stopped. Reset whenever the market time moves on.
    /// </summary>
    public sealed class LiquidityShare
    {
        private readonly Dictionary<(string, bool), long> _taken = new Dictionary<(string, bool), long>();
        private DateTime _at;

        /// <summary>Contracts already taken on this side of <paramref name="ticker"/>'s book at <paramref name="now"/>.</summary>
        public long Taken(string ticker, bool buy, DateTime now)
        {
            Roll(now);
            return _taken.TryGetValue((ticker, buy), out long n) ? n : 0;
        }

        public void Take(string ticker, bool buy, long contracts, DateTime now)
        {
            if (contracts <= 0) return;
            Roll(now);
            _taken[(ticker, buy)] = (_taken.TryGetValue((ticker, buy), out long n) ? n : 0) + contracts;
        }

        private void Roll(DateTime now)
        {
            if (now == _at) return;
            _at = now;
            _taken.Clear();
        }
    }
}
