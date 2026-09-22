using System.Globalization;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.UI
{
    /// <summary>Lenient parsing of what players type into the ticket.</summary>
    public static class TicketInput
    {
        public static bool TryParseQuantity(string text, out long quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string clean = text.Replace(",", "").Replace(" ", "").Trim();
            return long.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) && quantity > 0;
        }

        /// <summary>
        /// Restricts a field to the characters in <paramref name="allowed"/> (digits always). Filtering the value
        /// rather than key presses also catches pasted text.
        /// </summary>
        public static void Restrict(TextField field, string allowed = "")
        {
            field.RegisterValueChangedCallback(e =>
            {
                string clean = Keep(e.newValue, allowed);
                if (clean == e.newValue) return;
                int caret = Mathf.Min(field.cursorIndex, clean.Length);
                field.SetValueWithoutNotify(clean);
                field.SelectRange(caret, caret);
            });
        }

        public static string Keep(string text, string allowed)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char ch in text)
                if ((ch >= '0' && ch <= '9') || allowed.IndexOf(ch) >= 0) sb.Append(ch);
            return sb.ToString();
        }

        public static bool TryParsePrice(string text, out decimal price)
        {
            price = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string clean = text.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price) && price > 0m;
        }
    }

    public sealed class OrderEntryPanel : TerminalPanel
    {
        private OrderSide _side = OrderSide.Buy;
        private OrderType _type = OrderType.Market;
        private Order _lastOrder;

        private readonly Label _symbol, _quote, _estimate, _hint, _status, _position;
        private readonly Button _buy, _sell, _market, _limit, _submit;
        private readonly TextField _quantity, _limitPrice;
        private readonly VisualElement _limitSection;

        public OrderEntryPanel(TerminalContext context) : base(context, "order-entry")
        {
            Ui.Label("panel-title", Root, "ORDER ENTRY");
            _symbol = Ui.Label("ticket-symbol", Root);
            _quote = Ui.Label("muted", Root);

            var sides = Ui.Box("segmented", Root);
            _buy = Ui.Button("BUY", () => SetSide(OrderSide.Buy), "side-buy", sides, "side-buy");
            _sell = Ui.Button("SELL", () => SetSide(OrderSide.Sell), "side-sell", sides, "side-sell");

            var types = Ui.Box("segmented", Root);
            _market = Ui.Button("MARKET", () => SetType(OrderType.Market), "", types, "type-market");
            _limit = Ui.Button("LIMIT", () => SetType(OrderType.Limit), "", types, "type-limit");

            Ui.Label("field-caption", Root, "QUANTITY");
            _quantity = Field("qty", "100", Root);
            TicketInput.Restrict(_quantity, ","); // whole shares; commas from the quick buttons ("1,000")
            var quick = Ui.Box("quick-row", Root);
            foreach (long q in new long[] { 100, 500, 1000 })
                Ui.Button(Fmt.Shares(q), () => SetQuantity(q), "", quick);
            Ui.Button("MAX", SetMaxQuantity, "", quick, "qty-max");

            _limitSection = Ui.Box("", Root);
            Ui.Label("field-caption", _limitSection, "LIMIT PRICE");
            var priceRow = Ui.Box("field-row", _limitSection);
            _limitPrice = Field("limit-price", "", priceRow);
            TicketInput.Restrict(_limitPrice, ".,$");
            Ui.Button("-", () => NudgeLimit(-1), "nudge", priceRow);
            Ui.Button("+", () => NudgeLimit(+1), "nudge", priceRow);
            var fill = Ui.Box("quick-row", _limitSection);
            Ui.Button("BID", () => SetLimit(Context.Selected.Bid), "", fill);
            Ui.Button("ASK", () => SetLimit(Context.Selected.Ask), "", fill);
            Ui.Button("LAST", () => SetLimit(Context.Selected.Last), "", fill);

            _estimate = Ui.Label("ticket-estimate", Root);
            _hint = Ui.Label("ticket-hint", Root);
            _submit = Ui.Button("", Submit, "submit", Root, "submit-order");
            _status = Ui.Label("ticket-status", Root);
            _status.name = "order-status";
            _position = Ui.Label("ticket-position", Root);

            context.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        public override void Refresh()
        {
            SecurityRuntimeState s = Context.Selected;
            OrderManager orders = Context.Orders;
            bool buy = _side == OrderSide.Buy;

            Ui.SetText(_symbol, s.Ticker);
            Ui.SetText(_quote, $"Bid {Fmt.Price(s.Bid)}   Ask {Fmt.Price(s.Ask)}   Last {Fmt.Price(s.Last)}");

            _buy.EnableInClassList("active", buy);
            _sell.EnableInClassList("active", !buy);
            _market.EnableInClassList("active", _type == OrderType.Market);
            _limit.EnableInClassList("active", _type == OrderType.Limit);
            Ui.Show(_limitSection, _type == OrderType.Limit);

            bool hasQty = TicketInput.TryParseQuantity(_quantity.value, out long qty);
            bool hasLimit = TicketInput.TryParsePrice(_limitPrice.value, out decimal limit);
            bool ready = hasQty && (_type == OrderType.Market || hasLimit);

            string priceText = _type == OrderType.Market ? "MKT" : (hasLimit ? Fmt.Price(limit) : "?") + " LMT";
            Ui.SetText(_submit, $"{(buy ? "BUY" : "SELL")} {(hasQty ? Fmt.Shares(qty) : "?")} {s.Ticker} @ {priceText}");
            _submit.EnableInClassList("buy", buy);
            _submit.EnableInClassList("sell", !buy);
            _submit.SetEnabled(ready);

            if (ready)
            {
                decimal price = _type == OrderType.Limit ? limit : buy ? s.Ask : s.Bid;
                decimal notional = qty * price;
                decimal commission = orders.Rules.CommissionFor(qty, notional);
                Ui.SetText(_estimate, buy
                    ? $"Est. cost {Fmt.Money(notional)} + {Fmt.Money(commission)} commission"
                    : $"Est. proceeds {Fmt.Money(notional)} − {Fmt.Money(commission)} commission");
            }
            else
            {
                Ui.SetText(_estimate, hasQty ? "Enter a limit price." : "Enter a whole number of shares.");
            }

            Ui.SetText(_hint, Hint(s, hasQty ? qty : 0, limit));
            RefreshStatus();
            RefreshPosition(s);
        }

        private string Hint(SecurityRuntimeState s, long qty, decimal limit)
        {
            MarketSession session = Context.Market.Session;
            if (session == MarketSession.Closed) return "The market is closed. Orders are not accepted.";
            if (_type == OrderType.Market && session != MarketSession.Regular && !Context.Orders.Rules.AllowMarketOrdersOutsideRegularHours)
            {
                var schedule = Context.Market.Schedule;
                return $"Market orders only work {schedule.RegularOpen:hh\\:mm}–{schedule.RegularClose:hh\\:mm}. Use a limit order.";
            }
            if (qty <= 0) return "";

            if (_side == OrderSide.Buy)
            {
                long max = Context.Orders.MaxBuyQuantity(s.Ticker, _type, limit);
                return qty > max ? $"Exceeds buying power. Max {Fmt.Shares(max)} shares." : "";
            }

            long available = Context.Orders.AvailableToSell(s.Ticker);
            if (qty <= available) return "";
            return available == 0 ? "No shares to sell. Short selling is not available." : $"You can sell at most {Fmt.Shares(available)}.";
        }

        private void RefreshStatus()
        {
            if (_lastOrder == null)
            {
                Ui.SetText(_status, "");
                return;
            }

            Order o = _lastOrder;
            string text = o.Status switch
            {
                OrderStatus.Rejected => "Rejected: " + o.StatusReason,
                OrderStatus.Filled => $"Filled {Fmt.Shares(o.FilledQuantity)} {o.Ticker} @ {Fmt.Price(o.AverageFillPrice)}",
                OrderStatus.PartiallyFilled => $"Partially filled {Fmt.Shares(o.FilledQuantity)}/{Fmt.Shares(o.Quantity)} @ {Fmt.Price(o.AverageFillPrice)}. Working.",
                OrderStatus.Working => $"Working: {o.Side} {Fmt.Shares(o.Quantity)} {o.Ticker} @ {(o.Type == OrderType.Limit ? Fmt.Price(o.LimitPrice) : "MKT")}",
                OrderStatus.Cancelled => $"Cancelled{(o.FilledQuantity > 0 ? $" after {Fmt.Shares(o.FilledQuantity)} filled" : "")}: {o.StatusReason}",
                _ => o.Status.ToString(),
            };
            Ui.SetText(_status, text);
            _status.EnableInClassList("error", o.Status == OrderStatus.Rejected);
            _status.EnableInClassList("ok", o.Status == OrderStatus.Filled);
        }

        private void RefreshPosition(SecurityRuntimeState s)
        {
            Position p = Context.Account.Portfolio.Find(s.Ticker);
            if (p == null || !p.IsOpen)
            {
                Ui.SetText(_position, "No position");
                Ui.SetSign(_position, 0m);
                return;
            }

            decimal pnl = p.UnrealizedPnL(Context.Account.MarkPrice(s.Ticker));
            Ui.SetText(_position, $"Position {Fmt.Shares(p.Quantity)} @ {Fmt.Price(p.AveragePrice)}   P&L {Fmt.SignedMoney(pnl)}");
            Ui.SetSign(_position, pnl);
        }

        private void Submit()
        {
            if (!TicketInput.TryParseQuantity(_quantity.value, out long qty)) return;
            decimal limit = 0m;
            if (_type == OrderType.Limit && !TicketInput.TryParsePrice(_limitPrice.value, out limit)) return;

            _lastOrder = Context.Orders.Submit(Context.SelectedTicker, _side, _type, qty, limit);
            Refresh();
        }

        /// <summary>Shows the result of an order placed elsewhere in the terminal (e.g. a Close button).</summary>
        public void Track(Order order)
        {
            _lastOrder = order;
            Refresh();
        }

        private void SetSide(OrderSide side)
        {
            _side = side;
            Refresh();
        }

        private void SetType(OrderType type)
        {
            _type = type;
            if (type == OrderType.Limit && !TicketInput.TryParsePrice(_limitPrice.value, out _))
                SetLimit(Context.Selected.Last);
            Refresh();
        }

        private void SetQuantity(long qty)
        {
            _quantity.value = Fmt.Shares(qty);
            Refresh();
        }

        private void SetMaxQuantity()
        {
            string ticker = Context.SelectedTicker;
            TicketInput.TryParsePrice(_limitPrice.value, out decimal limit);
            long max = _side == OrderSide.Buy
                ? Context.Orders.MaxBuyQuantity(ticker, _type, limit)
                : Context.Orders.AvailableToSell(ticker);
            SetQuantity(max);
        }

        private void SetLimit(decimal price)
        {
            _limitPrice.value = Fmt.Price(price);
            Refresh();
        }

        private void NudgeLimit(int ticks)
        {
            if (!TicketInput.TryParsePrice(_limitPrice.value, out decimal price)) price = Context.Selected.Last;
            decimal next = PriceTick.RoundNearest(price) + ticks * PriceTick.For(price);
            if (next > 0m) SetLimit(next);
        }

        private void OnSelectionChanged()
        {
            _limitPrice.value = Fmt.Price(Context.Selected.Last);
            Refresh();
        }

        private TextField Field(string name, string value, VisualElement parent)
        {
            var field = new TextField { name = name, value = value };
            field.AddToClassList("ticket-field");
            field.RegisterValueChangedCallback(_ => Refresh());
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit();
            }, TrickleDown.TrickleDown);
            parent.Add(field);
            return field;
        }
    }
}
