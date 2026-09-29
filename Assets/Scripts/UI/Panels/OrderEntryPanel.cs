using System;
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

        private readonly Label _symbol, _quote, _estimate, _hint, _status, _position, _priceCaption;
        private string _copyNote = ""; // what happened to the copies of the last order
        private readonly Button _buy, _sell, _market, _limit, _stop, _submit;
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
            _stop = Ui.Button("STOP", () => SetType(OrderType.Stop), "", types, "type-stop");

            Ui.Label("field-caption", Root, "QUANTITY");
            _quantity = Field("qty", "1", Root);
            TicketInput.Restrict(_quantity, ","); // whole contracts; commas from the quick buttons
            var quick = Ui.Box("quick-row", Root);
            foreach (long q in new long[] { 1, 2, 5 })
                Ui.Button(Fmt.Shares(q), () => SetQuantity(q), "", quick);
            Ui.Button("MAX", SetMaxQuantity, "", quick, "qty-max");

            _limitSection = Ui.Box("", Root);
            _priceCaption = Ui.Label("field-caption", _limitSection, "LIMIT PRICE");
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
            _stop.EnableInClassList("active", _type == OrderType.Stop);
            Ui.Show(_limitSection, _type != OrderType.Market);
            Ui.SetText(_priceCaption, _type == OrderType.Stop ? "STOP PRICE (becomes a market order when traded)" : "LIMIT PRICE");

            bool hasQty = TicketInput.TryParseQuantity(_quantity.value, out long qty);
            bool hasLimit = TicketInput.TryParsePrice(_limitPrice.value, out decimal limit);
            bool ready = hasQty && (_type == OrderType.Market || hasLimit);

            string priceText = _type == OrderType.Market ? "MKT" : (hasLimit ? Fmt.Price(limit) : "?") + (_type == OrderType.Stop ? " STOP" : " LMT");
            Ui.SetText(_submit, $"{(buy ? "BUY" : "SELL")} {(hasQty ? Fmt.Shares(qty) : "?")} {s.Ticker} @ {priceText}");
            _submit.EnableInClassList("buy", buy);
            _submit.EnableInClassList("sell", !buy);
            _submit.SetEnabled(ready);

            if (ready)
            {
                decimal price = _type != OrderType.Market ? limit : buy ? s.Ask : s.Bid;
                ContractSpec contract = Context.Account.Contract(s.Ticker);
                decimal commission = orders.Rules.CommissionFor(qty);
                string perMove = $"{Fmt.Money(qty * contract.PointValue)}/pt · {Fmt.Money(qty * contract.TickValue(price))}/tick";
                long opening = orders.OpeningQuantity(s.Ticker, _side, qty);
                Ui.SetText(_estimate, opening > 0
                    ? $"Margin {Fmt.Money(opening * contract.Margin)} + {Fmt.Money(commission)} commission · {perMove}"
                    : $"{Fmt.Money(commission)} commission · {perMove}");
            }
            else
            {
                Ui.SetText(_estimate, hasQty ? "Enter a limit price." : "Enter a whole number of contracts.");
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

            long max = Context.Orders.MaxQuantity(s.Ticker, _side);
            if (qty > max) return $"Exceeds buying power. Max {Fmt.Contracts(max)}.";
            long held = Context.Account.Portfolio.QuantityOf(s.Ticker);
            if (_side == OrderSide.Sell && qty > Math.Max(0, held))
                return held > 0 ? $"Sells your {Fmt.Contracts(held)} and goes short {Fmt.Contracts(qty - held)}." : $"Opens a short: {Fmt.Contracts(qty)}.";
            if (_side == OrderSide.Buy && held < 0 && qty > -held)
                return $"Covers your short and goes long {Fmt.Contracts(qty + held)}.";
            return "";
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
                OrderStatus.Working => $"Working: {o.Side} {Fmt.Shares(o.Quantity)} {o.Ticker} @ {Fmt.OrderPrice(o)}",
                OrderStatus.Cancelled => $"Cancelled{(o.FilledQuantity > 0 ? $" after {Fmt.Shares(o.FilledQuantity)} filled" : "")}: {o.StatusReason}",
                _ => o.Status.ToString(),
            };
            Ui.SetText(_status, _copyNote.Length > 0 ? text + "\n" + _copyNote : text);
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
            Ui.SetText(_position, $"{(p.Quantity > 0 ? "Long" : "Short")} {Fmt.Contracts(Math.Abs(p.Quantity))} @ {Fmt.Price(p.AveragePrice)}   P&L {Fmt.SignedMoney(pnl)}");
            Ui.SetSign(_position, pnl);
        }

        private void Submit()
        {
            if (!TicketInput.TryParseQuantity(_quantity.value, out long qty)) return;
            decimal limit = 0m;
            if (_type != OrderType.Market && !TicketInput.TryParsePrice(_limitPrice.value, out limit)) return;

            // Placed on the active account and copied to any checked followers.
            string ticker = Context.SelectedTicker;
            OrderSide side = _side;
            OrderType type = _type;
            _lastOrder = Context.Place(om => type == OrderType.Stop
                ? om.SubmitStop(ticker, side, qty, limit)
                : om.Submit(ticker, side, type, qty, limit));
            _copyNote = Context.CopyNote;
            Refresh();
        }

        /// <summary>Shows the result of an order placed elsewhere in the terminal (e.g. a Close button).</summary>
        public void Track(Order order)
        {
            _lastOrder = order;
            _copyNote = Context.CopyNote;
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
            if (type != OrderType.Market && !TicketInput.TryParsePrice(_limitPrice.value, out _))
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
                ? Context.Orders.MaxQuantity(ticker, OrderSide.Buy)
                : Context.Orders.MaxQuantity(ticker, OrderSide.Sell);
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
