using System;
using System.Collections.Generic;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    public readonly struct Execution
    {
        public decimal Price { get; }
        public long Quantity { get; }

        public Execution(decimal price, long quantity)
        {
            Price = price;
            Quantity = quantity;
        }
    }

    /// <summary>
    /// Stateless fill model against a Level 1 quote. Hidden depth beyond the displayed size is synthesized
    /// as levels spaced about half a spread apart, each a bit larger than the last, so size relative to
    /// liquidity costs slippage. Player orders never move the simulated price.
    /// </summary>
    public sealed class ExecutionEngine
    {
        private readonly BrokerRules _rules;

        public ExecutionEngine(BrokerRules rules)
        {
            _rules = rules;
        }

        /// <param name="stopsActive">Stops only trigger when true (the regular session): a thin premarket print
        /// shouldn't set off a stop-loss.</param>
        public void Evaluate(Order order, in Quote quote, List<Execution> results, bool stopsActive = true)
        {
            results.Clear();
            long remaining = order.RemainingQuantity;
            if (remaining <= 0) return;
            bool buy = order.Side == OrderSide.Buy;
            bool justTriggered = false;

            if (order.IsStop && !order.Triggered)
            {
                // Triggered by any trade at or through the stop this tick (including a sweep's wick), or by the quote
                // having already moved past it (a gap). It then goes to the book like any market or limit order, so a
                // gap through the stop fills at the market, not at the stop.
                decimal stop = order.StopPrice;
                bool hit = buy ? quote.TickHigh >= stop || quote.Ask >= stop : quote.TickLow <= stop || quote.Bid <= stop;
                if (!stopsActive || !hit) return;
                order.Triggered = true;
                justTriggered = true;
            }

            if (order.ActiveType == OrderType.Market)
            {
                WalkBook(quote, buy, remaining, null, null, results);
                return;
            }

            decimal limit = order.LimitPrice;
            bool crossed = buy ? quote.Ask <= limit : quote.Bid >= limit;
            if (crossed)
            {
                // A new marketable order takes the book, including any price improvement. A resting order was
                // passed through during continuous trading, so it fills at its own limit.
                bool resting = order.IsResting && !justTriggered;
                WalkBook(quote, buy, remaining, limit, resting ? limit : (decimal?)null, results);
                return;
            }

            // A resting order inside the spread is the best price, so this tick's aggressive flow from the other
            // side trades with it first. Orders that only join the existing bid/ask wait for the market to come
            // to them: without a queue model, that is the conservative choice.
            if (!order.IsResting || quote.LastVolume <= 0) return;
            bool improvesQuote = buy ? limit > quote.Bid : limit < quote.Ask;
            bool flowTowardUs = buy ? quote.LastDirection < 0 : quote.LastDirection > 0;
            if (improvesQuote && flowTowardUs)
                results.Add(new Execution(limit, Math.Min(remaining, quote.LastVolume)));
        }

        private void WalkBook(in Quote quote, bool buy, long quantity, decimal? limit, decimal? fixedPrice, List<Execution> results)
        {
            decimal price = buy ? quote.Ask : quote.Bid;
            long insideSize = Math.Max(1L, buy ? quote.AskSize : quote.BidSize);
            decimal tick = PriceTick.For(price);
            decimal step = Math.Max(tick, PriceTick.RoundUp(quote.Spread / 2m, tick));

            long remaining = quantity;
            long fixedTotal = 0;
            for (int level = 0; level < _rules.BookLevelsPerTick && remaining > 0; level++)
            {
                if (price <= 0m) break;
                if (limit.HasValue && (buy ? price > limit.Value : price < limit.Value)) break;

                long size = (long)Math.Round(insideSize * (1 + _rules.BookLevelSizeGrowth * level));
                long take = Math.Min(size, remaining);
                if (fixedPrice.HasValue) fixedTotal += take;
                else results.Add(new Execution(price, take));

                remaining -= take;
                price = buy ? price + step : price - step;
            }

            if (fixedTotal > 0) results.Add(new Execution(fixedPrice.Value, fixedTotal));
        }
    }
}
