using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    /// <summary>
    /// Order lifecycle: validate → Working → (Partially)Filled / Cancelled / Rejected.
    /// Orders are evaluated on submission and again after every market tick. Market, limit, stop and stop-limit
    /// orders; brackets (take-profit + stop-loss, one-cancels-other). Orders are day orders unless good 'til
    /// cancelled (brackets): unfilled market orders are cancelled at the regular close, other day orders when the
    /// market closes. Stops trigger only in the regular session and then fill against the book like any market order.
    /// </summary>
    public sealed class OrderManager
    {
        private readonly IMarketData _market;
        private readonly Account _account;
        private readonly BrokerRules _rules;
        private readonly ExecutionEngine _engine;
        private readonly List<Order> _orders = new List<Order>();
        private readonly List<Order> _open = new List<Order>();
        private readonly List<Fill> _fills = new List<Fill>();
        private readonly List<Execution> _executions = new List<Execution>();
        private readonly List<Order> _scratch = new List<Order>();
        private long _nextOrderId = 1;
        private long _nextFillId = 1;
        private long _nextOcoGroup = 1;

        public IReadOnlyList<Order> Orders => _orders;
        public IReadOnlyList<Order> OpenOrders => _open;
        public IReadOnlyList<Fill> Fills => _fills;
        public BrokerRules Rules => _rules;

        public event Action<Order> OrderUpdated;
        public event Action<Fill> OrderFilled;

        public OrderManager(IMarketData market, Account account, BrokerRules rules)
        {
            _market = market;
            _account = account;
            _rules = rules;
            _engine = new ExecutionEngine(rules);
            _market.Ticked += OnTick;
            _market.SessionChanged += OnSessionChanged;
        }

        internal long NextOrderId => _nextOrderId;
        internal long NextFillId => _nextFillId;

        /// <summary>Loads saved orders/fills and re-reserves cash for open buys.</summary>
        internal void Restore(long nextOrderId, long nextFillId, List<Order> orders, List<Fill> fills)
        {
            _nextOrderId = nextOrderId;
            _nextFillId = nextFillId;
            _orders.Clear();
            _open.Clear();
            _fills.Clear();
            _orders.AddRange(orders);
            _fills.AddRange(fills);
            foreach (Order o in orders)
            {
                _nextOcoGroup = Math.Max(_nextOcoGroup, o.OcoGroup + 1);
                if (!o.IsOpen) continue;
                _open.Add(o);
                UpdateReservation(o);
            }
        }

        public Order SubmitMarket(string ticker, OrderSide side, long quantity) =>
            Submit(ticker, side, OrderType.Market, quantity, 0m);

        public Order SubmitLimit(string ticker, OrderSide side, long quantity, decimal limitPrice) =>
            Submit(ticker, side, OrderType.Limit, quantity, limitPrice);

        public Order Submit(string ticker, OrderSide side, OrderType type, long quantity, decimal limitPrice) =>
            Submit(ticker, side, type, quantity, limitPrice, 0m, false, 0);

        /// <summary>A stop (becomes a market order when price trades at or through the stop) or stop-limit.</summary>
        public Order SubmitStop(string ticker, OrderSide side, long quantity, decimal stopPrice, decimal limitPrice = 0m, bool gtc = false) =>
            Submit(ticker, side, limitPrice > 0m ? OrderType.StopLimit : OrderType.Stop, quantity, limitPrice, stopPrice, gtc, 0);

        /// <summary>
        /// Protects shares already held with a take-profit (sell limit) and/or a stop-loss (sell stop), linked so
        /// that when one fills the other shrinks by the same amount (OCO). Both are good until cancelled. Returns
        /// the orders created (either may be rejected; check their status).
        /// </summary>
        public List<Order> SubmitBracket(string ticker, long quantity, decimal takeProfit, decimal stopLoss)
        {
            long group = _nextOcoGroup++;
            var created = new List<Order>();
            if (takeProfit > 0m) created.Add(Submit(ticker, OrderSide.Sell, OrderType.Limit, quantity, takeProfit, 0m, true, group));
            if (stopLoss > 0m) created.Add(Submit(ticker, OrderSide.Sell, OrderType.Stop, quantity, 0m, stopLoss, true, group));
            return created;
        }

        private Order Submit(string ticker, OrderSide side, OrderType type, long quantity, decimal limitPrice, decimal stopPrice,
            bool gtc, long ocoGroup)
        {
            bool hasLimit = type == OrderType.Limit || type == OrderType.StopLimit;
            var order = new Order(_nextOrderId++, ticker, side, type, quantity, hasLimit ? limitPrice : 0m, _market.Now)
            {
                StopPrice = type == OrderType.Stop || type == OrderType.StopLimit ? stopPrice : 0m,
                Gtc = gtc,
                OcoGroup = ocoGroup,
            };
            _orders.Add(order);

            string rejection = Validate(order, out Quote quote);
            if (rejection != null)
            {
                order.Status = OrderStatus.Rejected;
                order.StatusReason = rejection;
                OrderUpdated?.Invoke(order);
                return order;
            }

            order.Status = OrderStatus.Working;
            _open.Add(order);
            UpdateReservation(order);
            OrderUpdated?.Invoke(order);

            Evaluate(order, quote);
            order.IsResting = true;
            return order;
        }

        /// <summary>
        /// Moves a working order's price (dragging a TP/SL line on the chart): the limit of a limit order, the trigger
        /// of a stop (a stop-limit's limit moves with it). Returns an error, or null. A limit dragged through the market
        /// becomes marketable and executes now.
        /// </summary>
        public string ModifyPrice(long orderId, decimal newPrice)
        {
            Order order = _open.Find(o => o.Id == orderId);
            if (order == null) return "That order is no longer working.";
            if (newPrice <= 0m) return "Price must be positive.";
            newPrice = PriceTick.RoundNearest(newPrice);
            if (!_market.TryGetQuote(order.Ticker, out Quote quote)) return $"Unknown symbol {order.Ticker}.";
            bool buy = order.Side == OrderSide.Buy;

            if (order.IsStop && !order.Triggered)
            {
                string stopProblem = StopProblem(buy, newPrice, quote);
                if (stopProblem != null) return stopProblem;
                if (order.Type == OrderType.StopLimit) order.LimitPrice = PriceTick.RoundNearest(order.LimitPrice + (newPrice - order.StopPrice));
                order.StopPrice = newPrice;
            }
            else if (order.ActiveType == OrderType.Limit)
            {
                order.LimitPrice = newPrice;
                order.IsResting = false; // re-evaluated as a fresh order at its new price
            }
            else
            {
                return "A market order has no price to move.";
            }

            if (buy) order.ReservePrice = ReservePrice(order, quote);
            order.UpdatedAt = _market.Now;
            UpdateReservation(order);
            OrderUpdated?.Invoke(order);
            Evaluate(order, quote);
            order.IsResting = true;
            return null;
        }

        public bool Cancel(long orderId)
        {
            Order order = _open.Find(o => o.Id == orderId);
            if (order == null) return false;
            Close(order, OrderStatus.Cancelled, "Cancelled by user.");
            return true;
        }

        private string Validate(Order order, out Quote quote)
        {
            quote = default;
            if (order.Quantity <= 0) return "Quantity must be positive.";
            if (!_market.TryGetQuote(order.Ticker, out quote)) return $"Unknown symbol {order.Ticker}.";

            MarketSession session = _market.Session;
            if (session == MarketSession.Closed) return "The market is closed.";
            if (order.Type == OrderType.Market && session != MarketSession.Regular && !_rules.AllowMarketOrdersOutsideRegularHours)
                return "Market orders are only accepted during the regular session. Use a limit order.";

            if (order.Type == OrderType.Limit || order.Type == OrderType.StopLimit)
            {
                if (order.LimitPrice <= 0m) return "Limit price must be positive.";
                if (!PriceTick.IsOnGrid(order.LimitPrice))
                    return string.Format(CultureInfo.InvariantCulture, "Limit price must be in increments of {0}.", PriceTick.For(order.LimitPrice));
            }
            if (order.IsStop)
            {
                if (order.StopPrice <= 0m) return "Stop price must be positive.";
                if (!PriceTick.IsOnGrid(order.StopPrice))
                    return string.Format(CultureInfo.InvariantCulture, "Stop price must be in increments of {0}.", PriceTick.For(order.StopPrice));
                string stopProblem = StopProblem(order.Side == OrderSide.Buy, order.StopPrice, quote);
                if (stopProblem != null) return stopProblem;
            }

            if (order.Side == OrderSide.Buy)
            {
                order.ReservePrice = ReservePrice(order, quote);
                decimal required = RequiredCash(order.Quantity, order.ReservePrice);
                if (required > _account.BuyingPower)
                    return string.Format(CultureInfo.InvariantCulture,
                        "Insufficient buying power: needs ${0:N2}, available ${1:N2}.", required, _account.BuyingPower);
            }
            else
            {
                long available = AvailableToSell(order.Ticker, order.OcoGroup);
                if (order.Quantity > available)
                    return available <= 0
                        ? "No shares available to sell. Short selling is not available."
                        : $"Only {available} shares available to sell. Short selling is not available.";
            }

            return null;
        }

        /// <summary>A sell stop must sit below the bid (a buy stop above the ask), or it would trigger at once.</summary>
        private static string StopProblem(bool buy, decimal stop, in Quote quote)
        {
            if (!buy && stop >= quote.Bid)
                return string.Format(CultureInfo.InvariantCulture, "A sell stop must be below the bid ({0:0.00}).", quote.Bid);
            if (buy && stop <= quote.Ask)
                return string.Format(CultureInfo.InvariantCulture, "A buy stop must be above the ask ({0:0.00}).", quote.Ask);
            return null;
        }

        private void Evaluate(Order order, in Quote quote)
        {
            _engine.Evaluate(order, quote, _executions, _market.Session == MarketSession.Regular);
            // Taking liquidity moves the market (negligible for retail size, real for a huge order), reported once per
            // evaluation since impact grows with the whole order, not each book level; a resting limit that got filled
            // provided liquidity instead.
            bool aggressive = order.ActiveType == OrderType.Market || !order.IsResting;
            long taken = 0;
            foreach (Execution execution in _executions)
            {
                if (!order.IsOpen) break;

                long quantity = execution.Quantity;
                if (order.Side == OrderSide.Buy)
                    quantity = AffordableQuantity(order, execution.Price, quantity);

                if (quantity <= 0)
                {
                    Close(order, OrderStatus.Cancelled, "Insufficient buying power to complete the order.");
                    break;
                }

                ApplyExecution(order, execution.Price, quantity);
                taken += quantity;
            }
            if (aggressive && taken > 0) _market.ReportAggressiveFlow(order.Ticker, order.Side == OrderSide.Buy ? taken : -taken);
        }

        private void ApplyExecution(Order order, decimal price, long quantity)
        {
            decimal commission = CommissionDelta(order, quantity, price);
            var fill = new Fill(_nextFillId++, order.Id, order.Ticker, order.Side, quantity, price, commission, _market.Now);
            fill.RealizedPnL = _account.ApplyFill(fill);
            order.AddFill(fill);
            _fills.Add(fill);

            if (!order.IsOpen) _open.Remove(order);
            UpdateReservation(order);

            if (order.OcoGroup != 0) ShrinkSiblings(order, quantity);
            if (order.Side == OrderSide.Sell) FitProtectiveOrders(order.Ticker);

            OrderFilled?.Invoke(fill);
            OrderUpdated?.Invoke(order);
        }

        /// <summary>One-cancels-other: a fill on one leg takes the same amount off the other; an emptied leg is cancelled.</summary>
        private void ShrinkSiblings(Order filled, long quantity)
        {
            var siblings = _open.FindAll(o => o != filled && o.OcoGroup == filled.OcoGroup);
            foreach (Order o in siblings)
            {
                o.ReduceTo(o.Quantity - quantity);
                if (o.RemainingQuantity <= 0) Close(o, OrderStatus.Cancelled, "The other side of the bracket filled.");
                else OrderUpdated?.Invoke(o);
            }
        }

        /// <summary>
        /// After the position shrinks (a manual sale), protective orders can't be for more shares than are left:
        /// good-until-cancelled sells are trimmed, and cancelled if nothing is left to protect.
        /// </summary>
        private void FitProtectiveOrders(string ticker)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            var protective = _open.FindAll(o => o.Gtc && o.Side == OrderSide.Sell && o.Ticker == ticker && o.RemainingQuantity > held);
            foreach (Order o in protective)
            {
                o.ReduceTo(o.FilledQuantity + held);
                if (o.RemainingQuantity <= 0) Close(o, OrderStatus.Cancelled, "No shares left to protect.");
                else OrderUpdated?.Invoke(o);
            }
        }

        /// <summary>Commission is computed on the order's cumulative fills, so partial fills never overpay the minimum.</summary>
        private decimal CommissionDelta(Order order, long quantity, decimal price) =>
            _rules.CommissionFor(order.FilledQuantity + quantity, order.FilledNotional + quantity * price) - order.Commission;

        /// <summary>
        /// Guards cash when a market buy slips past its reserve cushion. The order may use its own reservation,
        /// never cash reserved for other orders.
        /// </summary>
        private long AffordableQuantity(Order order, decimal price, long quantity)
        {
            decimal available = _account.BuyingPower + _account.ReservationFor(order.Id);
            if (Cost(quantity) <= available) return quantity;

            long q = Math.Min(quantity - 1, (long)Math.Floor(available / price));
            while (q > 0 && Cost(q) > available) q--;
            return q;

            decimal Cost(long n) => n * price + CommissionDelta(order, n, price);
        }

        private void UpdateReservation(Order order)
        {
            if (order.Side != OrderSide.Buy) return;

            decimal amount = 0m;
            if (order.IsOpen)
            {
                decimal remainingNotional = order.RemainingQuantity * order.ReservePrice;
                decimal commission = _rules.CommissionFor(order.Quantity, order.FilledNotional + remainingNotional) - order.Commission;
                amount = remainingNotional + Math.Max(0m, commission);
            }
            _account.SetReservation(order.Id, amount);
        }

        /// <summary>
        /// Shares held that are not already committed to open sell orders. A bracket's two legs protect the same shares,
        /// so a group counts once (its largest leg). <paramref name="exceptGroup"/> leaves one group out (its own legs).
        /// </summary>
        public long AvailableToSell(string ticker, long exceptGroup = 0)
        {
            long committed = 0;
            var groups = new Dictionary<long, long>();
            foreach (var o in _open)
            {
                if (o.Side != OrderSide.Sell || o.Ticker != ticker) continue;
                if (o.OcoGroup == 0) committed += o.RemainingQuantity;
                else if (o.OcoGroup != exceptGroup)
                    groups[o.OcoGroup] = Math.Max(groups.TryGetValue(o.OcoGroup, out long q) ? q : 0, o.RemainingQuantity);
            }
            foreach (long q in groups.Values) committed += q;
            return Math.Max(0, _account.Portfolio.QuantityOf(ticker) - committed);
        }

        /// <summary>Largest buy that would pass the buying-power check right now (same reserve rule as validation).</summary>
        public long MaxBuyQuantity(string ticker, OrderType type, decimal limitPrice)
        {
            if (!_market.TryGetQuote(ticker, out Quote quote)) return 0;
            decimal price = ReservePrice(type, limitPrice, 0m, quote);
            if (price <= 0m) return 0;

            // Required cash rises monotonically with quantity; binary search below the no-commission upper bound.
            decimal buyingPower = _account.BuyingPower;
            long lo = 0, hi = Math.Max(0L, (long)Math.Floor(buyingPower / price));
            while (lo < hi)
            {
                long mid = lo + (hi - lo + 1) / 2;
                if (RequiredCash(mid, price) <= buyingPower) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private decimal ReservePrice(Order order, in Quote quote) => ReservePrice(order.Type, order.LimitPrice, order.StopPrice, quote);

        /// <summary>Cash held per share for a buy: its limit, or (market, stop) the price it could fill at plus a cushion.</summary>
        private decimal ReservePrice(OrderType type, decimal limitPrice, decimal stopPrice, in Quote quote)
        {
            if (type == OrderType.Limit || type == OrderType.StopLimit) return limitPrice;
            decimal basis = type == OrderType.Stop ? Math.Max(quote.Ask, stopPrice) : quote.Ask;
            return PriceTick.RoundUp(basis * (1m + (decimal)_rules.MarketBuyReservePercent / 100m), PriceTick.For(basis));
        }

        private decimal RequiredCash(long quantity, decimal price)
        {
            decimal notional = quantity * price;
            return notional + _rules.CommissionFor(quantity, notional);
        }

        private void Close(Order order, OrderStatus status, string reason)
        {
            order.Status = status;
            order.StatusReason = reason;
            order.UpdatedAt = _market.Now;
            _open.Remove(order);
            UpdateReservation(order);
            OrderUpdated?.Invoke(order);
        }

        private void OnTick()
        {
            if (_open.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_open);
            foreach (Order order in _scratch)
            {
                if (!order.IsOpen || !_market.TryGetQuote(order.Ticker, out Quote quote)) continue;
                Evaluate(order, quote);
                order.IsResting = true;
            }
        }

        private void OnSessionChanged(MarketSession previous, MarketSession current)
        {
            if (_open.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_open);
            foreach (Order order in _scratch)
            {
                bool marketNow = order.ActiveType == OrderType.Market && (!order.IsStop || order.Triggered);
                if (current == MarketSession.Closed && !order.Gtc)
                    Close(order, OrderStatus.Cancelled, "Day order expired at the close.");
                else if (marketNow && previous == MarketSession.Regular && !_rules.AllowMarketOrdersOutsideRegularHours)
                    Close(order, OrderStatus.Cancelled, "Market order was not fully filled before the close.");
            }
        }
    }
}
