using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;

namespace OpeningBell.Trading
{
    /// <summary>
    /// Order lifecycle: validate → Working → (Partially)Filled / Cancelled / Rejected.
    /// Orders are evaluated on submission and again after every market tick. All orders are day orders:
    /// unfilled market orders are cancelled at the regular close, everything else when the market closes.
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

        public Order SubmitMarket(string ticker, OrderSide side, long quantity) =>
            Submit(ticker, side, OrderType.Market, quantity, 0m);

        public Order SubmitLimit(string ticker, OrderSide side, long quantity, decimal limitPrice) =>
            Submit(ticker, side, OrderType.Limit, quantity, limitPrice);

        public Order Submit(string ticker, OrderSide side, OrderType type, long quantity, decimal limitPrice)
        {
            var order = new Order(_nextOrderId++, ticker, side, type, quantity,
                type == OrderType.Limit ? limitPrice : 0m, _market.Now);
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

            if (order.Type == OrderType.Limit)
            {
                if (order.LimitPrice <= 0m) return "Limit price must be positive.";
                if (!PriceTick.IsOnGrid(order.LimitPrice))
                    return string.Format(CultureInfo.InvariantCulture, "Limit price must be in increments of {0}.", PriceTick.For(order.LimitPrice));
            }

            if (order.Side == OrderSide.Buy)
            {
                order.ReservePrice = order.Type == OrderType.Limit
                    ? order.LimitPrice
                    : PriceTick.RoundUp(quote.Ask * (1m + (decimal)_rules.MarketBuyReservePercent / 100m), PriceTick.For(quote.Ask));
                decimal notional = order.Quantity * order.ReservePrice;
                decimal required = notional + _rules.CommissionFor(order.Quantity, notional);
                if (required > _account.BuyingPower)
                    return string.Format(CultureInfo.InvariantCulture,
                        "Insufficient buying power: needs ${0:N2}, available ${1:N2}.", required, _account.BuyingPower);
            }
            else
            {
                long available = _account.Portfolio.QuantityOf(order.Ticker) - OpenSellQuantity(order.Ticker);
                if (order.Quantity > available)
                    return available <= 0
                        ? "No shares available to sell. Short selling is not available."
                        : $"Only {available} shares available to sell. Short selling is not available.";
            }

            return null;
        }

        private void Evaluate(Order order, in Quote quote)
        {
            _engine.Evaluate(order, quote, _executions);
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

            OrderFilled?.Invoke(fill);
            OrderUpdated?.Invoke(order);
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

        private long OpenSellQuantity(string ticker)
        {
            long total = 0;
            foreach (var o in _open)
                if (o.Side == OrderSide.Sell && o.Ticker == ticker)
                    total += o.RemainingQuantity;
            return total;
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
                if (current == MarketSession.Closed)
                    Close(order, OrderStatus.Cancelled, "Day order expired at the close.");
                else if (order.Type == OrderType.Market && previous == MarketSession.Regular && !_rules.AllowMarketOrdersOutsideRegularHours)
                    Close(order, OrderStatus.Cancelled, "Market order was not fully filled before the close.");
            }
        }
    }
}
