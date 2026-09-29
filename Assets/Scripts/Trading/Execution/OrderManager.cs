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
    /// Symbols trade as futures-style contracts: buys post day margin (see Account), and no position is held
    /// overnight: at the 4:00 PM close every position is closed at the closing price (after-hours ones at 8:00 PM).
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

        /// <summary>
        /// Extra checks an account's owner adds on top of the broker's (a prop firm's contract limit, a failed
        /// account). Returns a rejection reason, or null to accept.
        /// </summary>
        public Func<Order, string> Gate { get; set; }

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
        /// Protects an open position with a take-profit (limit) and/or a stop-loss (stop) on the closing side, linked
        /// so that when one fills the other shrinks by the same amount (OCO). A long's legs sell (TP above, SL below);
        /// a short's legs buy (TP below, SL above). Both are good until cancelled. Returns the orders created (either
        /// may be rejected; check their status).
        /// </summary>
        public List<Order> SubmitBracket(string ticker, long quantity, decimal takeProfit, decimal stopLoss)
        {
            long group = _nextOcoGroup++;
            OrderSide side = _account.Portfolio.QuantityOf(ticker) < 0 ? OrderSide.Buy : OrderSide.Sell;
            var created = new List<Order>();
            if (takeProfit > 0m) created.Add(Submit(ticker, side, OrderType.Limit, quantity, takeProfit, 0m, true, group));
            if (stopLoss > 0m) created.Add(Submit(ticker, side, OrderType.Stop, quantity, 0m, stopLoss, true, group));
            return created;
        }

        /// <summary>
        /// Adds one protective leg (a take-profit limit or a stop-loss stop) to the position, e.g. dragged out of the
        /// chart's position bar. It joins the symbol's existing bracket, so it stays one-cancels-other with the leg
        /// already there, or starts a new bracket covering everything still open to close.
        /// </summary>
        public Order SubmitProtection(string ticker, bool stopLoss, decimal price)
        {
            Order existing = _open.Find(o => o.Ticker == ticker && o.OcoGroup != 0);
            long group = existing?.OcoGroup ?? _nextOcoGroup++;
            long quantity = existing?.RemainingQuantity ?? AvailableToClose(ticker);
            OrderSide side = _account.Portfolio.QuantityOf(ticker) < 0 ? OrderSide.Buy : OrderSide.Sell;
            return stopLoss
                ? Submit(ticker, side, OrderType.Stop, quantity, 0m, price, true, group)
                : Submit(ticker, side, OrderType.Limit, quantity, price, 0m, true, group);
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

            if (order.Side == OrderSide.Buy) order.ReservePrice = ReservePrice(order, quote);

            string gated = Gate?.Invoke(order);
            if (gated != null) return gated;

            // Protective orders (brackets) only ever close: they may not open a position the other way.
            if (order.OcoGroup != 0)
            {
                long closable = AvailableToClose(order.Ticker, order.OcoGroup);
                if (!Closes(order.Ticker, order.Side) || order.Quantity > closable)
                    return closable <= 0 || !Closes(order.Ticker, order.Side)
                        ? "No open position to protect."
                        : $"Only {closable} contract{(closable == 1 ? "" : "s")} to protect.";
            }

            // Selling more than you hold opens a short, buying more than you're short opens a long: only the contracts
            // that open new exposure post margin.
            long opening = OpeningQuantity(order.Ticker, order.Side, order.Quantity);
            if (opening > 0)
            {
                decimal required = RequiredMargin(order.Ticker, opening);
                if (required > _account.BuyingPower)
                    return string.Format(CultureInfo.InvariantCulture,
                        "Insufficient buying power: {0} new contract{1} need ${2:N2} margin, available ${3:N2}.",
                        opening, opening == 1 ? "" : "s", required, _account.BuyingPower);
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

        /// <summary>
        /// Book depth shared with other order managers (a firm's desks): taking liquidity here leaves less for them this
        /// tick. Null (the default) = this manager sees the whole book every tick.
        /// </summary>
        public LiquidityShare SharedLiquidity { get; set; }

        private void Evaluate(Order order, in Quote quote)
        {
            bool buySide = order.Side == OrderSide.Buy;
            long depthTaken = SharedLiquidity?.Taken(order.Ticker, buySide, _market.Now) ?? 0;
            _engine.Evaluate(order, quote, _executions, _market.Session == MarketSession.Regular, depthTaken);
            // Taking liquidity moves the market (negligible for retail size, real for a huge order), reported once per
            // evaluation since impact grows with the whole order, not each book level; a resting limit that got filled
            // provided liquidity instead.
            bool aggressive = order.ActiveType == OrderType.Market || !order.IsResting;
            long taken = 0;
            foreach (Execution execution in _executions)
            {
                if (!order.IsOpen) break;

                long quantity = AffordableQuantity(order, execution.Price, execution.Quantity);

                if (quantity <= 0)
                {
                    Close(order, OrderStatus.Cancelled, "Insufficient buying power to complete the order.");
                    break;
                }

                ApplyExecution(order, execution.Price, quantity);
                taken += quantity;
            }
            // Impact is sized by exposure: a contract moves the market like point-value shares.
            if (aggressive && taken > 0)
            {
                SharedLiquidity?.Take(order.Ticker, buySide, taken, _market.Now);
                long exposure = (long)(taken * _account.Contract(order.Ticker).PointValue);
                _market.ReportAggressiveFlow(order.Ticker, order.Side == OrderSide.Buy ? exposure : -exposure);
            }
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
            FitProtectiveOrders(order.Ticker);
            // The position changed, so how much of each other working order would open new exposure changed too.
            foreach (Order o in _open)
                if (o.Ticker == order.Ticker) UpdateReservation(o);

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
        /// After the position shrinks or flips, protective (good-until-cancelled) orders can't be for more contracts than
        /// are left, and never on the side that would open a new position: they are trimmed, or cancelled when nothing
        /// is left to protect.
        /// </summary>
        private void FitProtectiveOrders(string ticker)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            var protective = _open.FindAll(o => o.Gtc && o.Ticker == ticker);
            foreach (Order o in protective)
            {
                long cap = Closes(ticker, o.Side) ? Math.Abs(held) : 0;
                if (o.RemainingQuantity <= cap) continue;
                o.ReduceTo(o.FilledQuantity + cap);
                if (o.RemainingQuantity <= 0) Close(o, OrderStatus.Cancelled, "No position left to protect.");
                else OrderUpdated?.Invoke(o);
            }
        }

        /// <summary>True if an order on this side reduces the current position (sell a long, buy back a short).</summary>
        private bool Closes(string ticker, OrderSide side)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            return side == OrderSide.Sell ? held > 0 : held < 0;
        }

        /// <summary>Net contracts held in a symbol (negative = short).</summary>
        public long PositionQuantity(string ticker) => _account.Portfolio.QuantityOf(ticker);

        /// <summary>Contracts of an order that would open new exposure (beyond closing what's held on the other side).</summary>
        public long OpeningQuantity(string ticker, OrderSide side, long quantity)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            long closable = side == OrderSide.Sell ? Math.Max(0, held) : Math.Max(0, -held);
            return Math.Max(0, quantity - closable);
        }

        /// <summary>Commission on the order's cumulative fills, so partial fills are charged exactly once per contract.</summary>
        private decimal CommissionDelta(Order order, long quantity, decimal price) =>
            _rules.CommissionFor(order.FilledQuantity + quantity) - order.Commission;

        /// <summary>
        /// Guards margin when a buy fills after the account moved (open P&L fell). The order may use its own
        /// reservation, never margin reserved for other orders.
        /// </summary>
        private long AffordableQuantity(Order order, decimal price, long quantity)
        {
            decimal available = _account.BuyingPower + _account.ReservationFor(order.Id);
            decimal margin = _account.MarginPerContract(order.Ticker);
            long q = quantity;
            while (q > 0 && OpeningQuantity(order.Ticker, order.Side, q) * margin + CommissionDelta(order, q, price) > available) q--;
            return q;
        }

        /// <summary>Holds margin for the part of a working order that would open new exposure, plus its commission.</summary>
        private void UpdateReservation(Order order)
        {
            decimal amount = 0m;
            if (order.IsOpen)
            {
                long opening = OpeningQuantity(order.Ticker, order.Side, order.RemainingQuantity);
                decimal commission = opening > 0 ? Math.Max(0m, _rules.CommissionFor(order.Quantity) - order.Commission) : 0m;
                amount = opening * _account.MarginPerContract(order.Ticker) + commission;
            }
            _account.SetReservation(order.Id, amount);
        }

        /// <summary>
        /// Contracts of the open position (long or short) not already committed to working orders that close it. A
        /// bracket's two legs protect the same contracts, so a group counts once (its largest leg).
        /// <paramref name="exceptGroup"/> leaves one group out (its own legs).
        /// </summary>
        public long AvailableToClose(string ticker, long exceptGroup = 0)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            if (held == 0) return 0;
            OrderSide closing = held > 0 ? OrderSide.Sell : OrderSide.Buy;
            long committed = 0;
            var groups = new Dictionary<long, long>();
            foreach (var o in _open)
            {
                if (o.Side != closing || o.Ticker != ticker) continue;
                if (o.OcoGroup == 0) committed += o.RemainingQuantity;
                else if (o.OcoGroup != exceptGroup)
                    groups[o.OcoGroup] = Math.Max(groups.TryGetValue(o.OcoGroup, out long q) ? q : 0, o.RemainingQuantity);
            }
            foreach (long q in groups.Values) committed += q;
            return Math.Max(0, Math.Abs(held) - committed);
        }

        /// <summary>Most contracts an order on this side could be for right now: close what's held, then open with margin.</summary>
        public long MaxQuantity(string ticker, OrderSide side)
        {
            long held = _account.Portfolio.QuantityOf(ticker);
            long closable = side == OrderSide.Sell ? Math.Max(0, held) : Math.Max(0, -held);
            return closable + MaxBuyQuantity(ticker, OrderType.Market, 0m);
        }

        /// <summary>Most new contracts (either side) margin allows right now (same rule as validation).</summary>
        public long MaxBuyQuantity(string ticker, OrderType type, decimal limitPrice)
        {
            if (!_market.TryGetQuote(ticker, out _)) return 0;
            decimal perContract = _account.MarginPerContract(ticker) + _rules.CommissionFor(1);
            decimal buyingPower = _account.BuyingPower;
            return perContract <= 0m || buyingPower <= 0m ? 0 : (long)Math.Floor(buyingPower / perContract);
        }

        /// <summary>Margin plus commission to open this many contracts.</summary>
        public decimal RequiredMargin(string ticker, long contracts) =>
            contracts * _account.MarginPerContract(ticker) + _rules.CommissionFor(contracts);

        private decimal ReservePrice(Order order, in Quote quote) => ReservePrice(order.Type, order.LimitPrice, order.StopPrice, quote);

        /// <summary>Cash held per share for a buy: its limit, or (market, stop) the price it could fill at plus a cushion.</summary>
        private decimal ReservePrice(OrderType type, decimal limitPrice, decimal stopPrice, in Quote quote)
        {
            if (type == OrderType.Limit || type == OrderType.StopLimit) return limitPrice;
            decimal basis = type == OrderType.Stop ? Math.Max(quote.Ask, stopPrice) : quote.Ask;
            return PriceTick.RoundUp(basis * (1m + (decimal)_rules.MarketBuyReservePercent / 100m), PriceTick.For(basis));
        }


        /// <summary>Cancels every working order and closes every position at the last price (a prop firm liquidation).</summary>
        public void LiquidateAll(string reason)
        {
            _scratch.Clear();
            _scratch.AddRange(_open);
            foreach (Order o in _scratch) Close(o, OrderStatus.Cancelled, reason);
            FlattenAll(reason);
        }

        /// <summary>
        /// End of day: cancels the orders of every held symbol and closes each position in full at the closing
        /// price (the closing print, so the whole size fills there like a market-on-close order).
        /// </summary>
        private void FlattenAll(string reason)
        {
            var held = new List<Position>();
            foreach (Position p in _account.Portfolio.Positions)
                if (p.IsOpen) held.Add(p);
            foreach (Position p in held)
            {
                if (!_market.TryGetQuote(p.Ticker, out Quote quote)) continue;
                foreach (Order o in _open.FindAll(o => o.Ticker == p.Ticker))
                    Close(o, OrderStatus.Cancelled, "Cancelled: the position was closed at the end of the day.");

                long quantity = Math.Abs(p.Quantity);
                OrderSide side = p.Quantity > 0 ? OrderSide.Sell : OrderSide.Buy;
                var order = new Order(_nextOrderId++, p.Ticker, side, OrderType.Market, quantity, 0m, _market.Now)
                {
                    Status = OrderStatus.Working,
                    StatusReason = reason,
                };
                _orders.Add(order);
                _open.Add(order);
                decimal price = quote.Last > 0m ? quote.Last : quote.Mid;
                ApplyExecution(order, price, quantity);
            }
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
            if (previous == MarketSession.Regular && current != MarketSession.Regular) FlattenAll("Closed at the 4:00 PM close.");
            else if (current == MarketSession.Closed) FlattenAll("Closed when the market closed.");
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
