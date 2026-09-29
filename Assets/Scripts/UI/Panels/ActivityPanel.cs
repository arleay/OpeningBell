using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.UI
{
    /// <summary>Bottom tabs: open positions, today's orders (with cancel), and fills.</summary>
    public sealed class ActivityPanel : TerminalPanel
    {
        private enum Tab { Positions, Orders, Fills }

        private sealed class PositionRow
        {
            public VisualElement Root;
            public Label Qty, Avg, Last, Value, Unrealized, Realized;
        }

        private const int RowHeight = 24;

        private readonly Action<Order> _orderPlaced;
        private readonly Button _positionsTab, _ordersTab, _fillsTab;
        private readonly VisualElement _positionsView, _positionsBody;
        private readonly Label _noPositions;
        private readonly ListView _ordersView, _fillsView;
        private readonly Dictionary<string, PositionRow> _positionRows = new Dictionary<string, PositionRow>();
        private readonly List<Order> _orderItems = new List<Order>();
        private readonly List<Fill> _fillItems = new List<Fill>();
        private Tab _tab = Tab.Positions;
        private bool _ordersDirty = true;
        private int _knownFills = -1;

        /// <param name="orderPlaced">Called with orders this panel submits (Close buttons), so the ticket can show the result.</param>
        public ActivityPanel(TerminalContext context, Action<Order> orderPlaced) : base(context, "activity")
        {
            _orderPlaced = orderPlaced;

            var tabs = Ui.Box("tabs", Root);
            _positionsTab = Ui.Button("POSITIONS", () => SetTab(Tab.Positions), "", tabs, "tab-positions");
            _ordersTab = Ui.Button("ORDERS", () => SetTab(Tab.Orders), "", tabs, "tab-orders");
            _fillsTab = Ui.Button("FILLS", () => SetTab(Tab.Fills), "", tabs, "tab-fills");

            _positionsView = Ui.Box("activity-view", Root);
            Header(_positionsView, "SYMBOL", "CONTRACTS", "AVG", "LAST", "MARGIN", "UNREALIZED", "REALIZED", "");
            _positionsBody = Ui.Box("", _positionsView);
            _noPositions = Ui.Label("muted empty-note", _positionsView, "No open positions.");

            var ordersSection = Ui.Box("activity-view", Root);
            Header(ordersSection, "TIME", "SYMBOL", "SIDE", "TYPE", "QTY", "FILLED", "PRICE", "AVG FILL", "STATUS", "");
            _ordersView = List(_orderItems, MakeOrderRow, BindOrderRow, ordersSection);

            var fillsSection = Ui.Box("activity-view", Root);
            Header(fillsSection, "TIME", "SYMBOL", "SIDE", "QTY", "PRICE", "COMMISSION", "REALIZED");
            _fillsView = List(_fillItems, MakeFillRow, BindFillRow, fillsSection);

            // Follow whichever account is active (the terminal switches between personal and prop accounts).
            OrderManager watched = context.Orders;
            Action<Order> dirty = _ => _ordersDirty = true;
            watched.OrderUpdated += dirty;
            context.ActiveChanged += () =>
            {
                watched.OrderUpdated -= dirty;
                watched = context.Orders;
                watched.OrderUpdated += dirty;
                _ordersDirty = true;
                _knownFills = -1;
            };
            SetTab(Tab.Positions);
        }

        public override void Refresh()
        {
            int open = Context.Orders.OpenOrders.Count;
            Ui.SetText(_positionsTab, $"POSITIONS ({CountOpenPositions()})");
            Ui.SetText(_ordersTab, open > 0 ? $"ORDERS ({open} OPEN)" : "ORDERS");
            Ui.SetText(_fillsTab, $"FILLS ({Context.Orders.Fills.Count})");

            RefreshPositions();

            if (_ordersDirty)
            {
                _ordersDirty = false;
                Reversed(Context.Orders.Orders, _orderItems);
                _ordersView.RefreshItems();
            }

            if (_knownFills != Context.Orders.Fills.Count)
            {
                _knownFills = Context.Orders.Fills.Count;
                Reversed(Context.Orders.Fills, _fillItems);
                _fillsView.RefreshItems();
            }
        }

        private void SetTab(Tab tab)
        {
            _tab = tab;
            Ui.Show(_positionsView, tab == Tab.Positions);
            Ui.Show(_ordersView.parent, tab == Tab.Orders);
            Ui.Show(_fillsView.parent, tab == Tab.Fills);
            _positionsTab.EnableInClassList("active", tab == Tab.Positions);
            _ordersTab.EnableInClassList("active", tab == Tab.Orders);
            _fillsTab.EnableInClassList("active", tab == Tab.Fills);
        }

        // ---- Positions ----

        private void RefreshPositions()
        {
            var portfolio = Context.Account.Portfolio;
            foreach (Position p in portfolio.Positions)
            {
                bool hasRow = _positionRows.TryGetValue(p.Ticker, out PositionRow row);
                if (p.IsOpen && !hasRow) row = AddPositionRow(p.Ticker);
                else if (!p.IsOpen && hasRow)
                {
                    row.Root.RemoveFromHierarchy();
                    _positionRows.Remove(p.Ticker);
                    continue;
                }
                if (!p.IsOpen) continue;

                decimal mark = Context.Account.MarkPrice(p.Ticker);
                decimal unrealized = p.UnrealizedPnL(mark);
                Ui.SetText(row.Qty, Fmt.Shares(p.Quantity));
                Ui.SetText(row.Avg, Fmt.Price(p.AveragePrice));
                Ui.SetText(row.Last, Fmt.Price(mark));
                Ui.SetText(row.Value, Fmt.Money(System.Math.Abs(p.Quantity) * Context.Account.MarginPerContract(p.Ticker)));
                Ui.SetText(row.Unrealized, Fmt.SignedMoney(unrealized));
                Ui.SetSign(row.Unrealized, unrealized);
                Ui.SetText(row.Realized, Fmt.SignedMoney(p.RealizedPnL));
                Ui.SetSign(row.Realized, p.RealizedPnL);
            }
            Ui.Show(_noPositions, _positionRows.Count == 0);
        }

        private PositionRow AddPositionRow(string ticker)
        {
            var row = new PositionRow { Root = Ui.Box("table-row data-row", _positionsBody) };
            row.Root.name = "pos-" + ticker;
            Ui.Label("c c-left symbol", row.Root, ticker);
            row.Qty = Ui.Label("c", row.Root);
            row.Avg = Ui.Label("c", row.Root);
            row.Last = Ui.Label("c", row.Root);
            row.Value = Ui.Label("c", row.Root);
            row.Unrealized = Ui.Label("c", row.Root);
            row.Realized = Ui.Label("c", row.Root);
            var cell = Ui.Box("c c-btn", row.Root);
            Ui.Button("CLOSE", () => ClosePosition(ticker), "row-btn", cell, "close-" + ticker);
            row.Root.RegisterCallback<ClickEvent>(_ => Context.Select(ticker));
            _positionRows.Add(ticker, row);
            return row;
        }

        /// <summary>
        /// Market order in the regular session; outside it, a limit at the bid (market orders are not accepted then).
        /// Followers close their own position in the symbol, whatever its size.
        /// </summary>
        private void ClosePosition(string ticker)
        {
            if (Context.Orders.AvailableToClose(ticker) <= 0 || !Context.Market.TryGetQuote(ticker, out Quote quote)) return;
            bool regular = Context.Market.Session == MarketSession.Regular;
            List<Order> placed = Context.PlaceMany(om =>
            {
                long qty = om.AvailableToClose(ticker);
                if (qty <= 0) return new List<Order>(); // a follower with nothing to close does nothing
                // A long closes by selling (at the bid outside the session), a short by buying back (at the ask).
                bool shortPosition = om.PositionQuantity(ticker) < 0;
                OrderSide side = shortPosition ? OrderSide.Buy : OrderSide.Sell;
                return new List<Order>
                {
                    regular ? om.SubmitMarket(ticker, side, qty) : om.SubmitLimit(ticker, side, qty, shortPosition ? quote.Ask : quote.Bid),
                };
            });
            if (placed.Count > 0) _orderPlaced?.Invoke(placed[0]);
        }

        private int CountOpenPositions()
        {
            int n = 0;
            foreach (Position p in Context.Account.Portfolio.Positions)
                if (p.IsOpen) n++;
            return n;
        }

        // ---- Orders ----

        private VisualElement MakeOrderRow()
        {
            var row = Ui.Box("table-row data-row");
            for (int i = 0; i < 9; i++) Ui.Label(i == 1 ? "c c-left symbol" : "c", row);
            var cell = Ui.Box("c c-btn", row);
            var cancel = Ui.Button("CANCEL", null, "row-btn", cell);
            cancel.clicked += () => Context.Cancel((long)cancel.userData);
            return row;
        }

        private void BindOrderRow(VisualElement row, int index)
        {
            Order o = _orderItems[index];
            SetCell(row, 0, Fmt.Clock(o.SubmittedAt));
            SetCell(row, 1, o.Ticker);
            SetCell(row, 2, o.Side.ToString().ToUpperInvariant());
            SetCell(row, 3, o.Type.ToString().ToUpperInvariant());
            SetCell(row, 4, Fmt.Shares(o.Quantity));
            SetCell(row, 5, Fmt.Shares(o.FilledQuantity));
            SetCell(row, 6, Fmt.OrderPrice(o));
            SetCell(row, 7, o.FilledQuantity > 0 ? Fmt.Price(o.AverageFillPrice) : "—");
            SetCell(row, 8, o.Status == OrderStatus.PartiallyFilled ? "PARTIAL" : o.Status.ToString().ToUpperInvariant());

            var status = (Label)row[8];
            status.EnableInClassList("status-ok", o.Status == OrderStatus.Filled);
            status.EnableInClassList("status-bad", o.Status == OrderStatus.Rejected);
            status.EnableInClassList("status-live", o.IsOpen);
            status.EnableInClassList("muted", o.Status == OrderStatus.Cancelled);
            ((Label)row[2]).EnableInClassList("up", o.Side == OrderSide.Buy);
            ((Label)row[2]).EnableInClassList("down", o.Side == OrderSide.Sell);

            var cancel = (Button)row[9][0];
            cancel.userData = o.Id;
            cancel.name = "cancel-" + o.Id;
            Ui.Show(cancel, o.IsOpen);
        }

        // ---- Fills ----

        private VisualElement MakeFillRow()
        {
            var row = Ui.Box("table-row data-row");
            for (int i = 0; i < 7; i++) Ui.Label(i == 1 ? "c c-left symbol" : "c", row);
            return row;
        }

        private void BindFillRow(VisualElement row, int index)
        {
            Fill f = _fillItems[index];
            SetCell(row, 0, Fmt.Clock(f.Time));
            SetCell(row, 1, f.Ticker);
            SetCell(row, 2, f.Side.ToString().ToUpperInvariant());
            SetCell(row, 3, Fmt.Shares(f.Quantity));
            SetCell(row, 4, Fmt.Price(f.Price));
            SetCell(row, 5, Fmt.Money(f.Commission));
            SetCell(row, 6, f.RealizedPnL == 0m ? "—" : Fmt.SignedMoney(f.RealizedPnL));
            ((Label)row[2]).EnableInClassList("up", f.Side == OrderSide.Buy);
            ((Label)row[2]).EnableInClassList("down", f.Side == OrderSide.Sell);
            Ui.SetSign(row[6], f.RealizedPnL);
        }

        // ---- helpers ----

        private static void SetCell(VisualElement row, int column, string text) => Ui.SetText((Label)row[column], text);

        private static void Header(VisualElement parent, params string[] columns)
        {
            var header = Ui.Box("table-row table-header", parent);
            for (int i = 0; i < columns.Length; i++)
            {
                bool button = columns[i].Length == 0;
                Ui.Label(button ? "c c-btn" : i == (columns[0] == "TIME" ? 1 : 0) ? "c c-left" : "c", header, columns[i]);
            }
        }

        private static ListView List<T>(List<T> items, Func<VisualElement> make, Action<VisualElement, int> bind, VisualElement parent)
        {
            var list = new ListView(items, RowHeight, make, bind) { selectionType = SelectionType.None };
            list.AddToClassList("activity-list");
            parent.Add(list);
            return list;
        }

        private static void Reversed<T>(IReadOnlyList<T> source, List<T> target)
        {
            target.Clear();
            for (int i = source.Count - 1; i >= 0; i--) target.Add(source[i]);
        }
    }
}
