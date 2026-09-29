using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>What the chart needs from the account to draw the position and working orders, and to change them.</summary>
    public sealed class ChartTrading
    {
        /// <summary>Signed contracts: negative for a short.</summary>
        public long Quantity;
        public decimal AveragePrice;
        public decimal Last;
        /// <summary>Dollars per 1.00 move per contract, for the live P&L.</summary>
        public decimal PointValue = 1m;
        public readonly List<Order> Orders = new List<Order>();
        /// <summary>Moves a working order to a new price; returns an error or null.</summary>
        public Func<long, decimal, string> Modify;
        /// <summary>Adds a take-profit (false) or stop-loss (true) leg at a price; returns an error or null.</summary>
        public Func<bool, decimal, string> AddProtection;
        /// <summary>Called after the chart changed an order or the position, so the owner can redraw at once.</summary>
        public Action Changed;
        /// <summary>Cancels a working order (the "×" on its line); false if it was no longer working.</summary>
        public Func<long, bool> Cancel;
        /// <summary>Closes the whole position (the "×" on the position bar); returns an error or null.</summary>
        public Func<string> Close;
    }

    /// <summary>
    /// The position line (average entry with live P&L) and a line per working order. Take-profit, stop-loss and limit
    /// lines can be dragged: releasing moves the real order (the order manager validates it, so the line and the order
    /// never disagree). The position bar sits at the right end of its line: TP and SL tabs (drag one out and
    /// drop it at a price to place that leg; each hides once its leg exists), then the contracts, live P&L and a "×" that
    /// closes the position. It is drawn light on light chart themes and dark on dark ones. The "×" on an order's label cancels it.
    /// </summary>
    public sealed partial class ChartView
    {
        private static readonly Color StopLossColor = new Color32(232, 84, 76, 255);

        private ChartTrading _trading;
        private Rect _closeButton;
        /// <summary>A leg being dragged out of the position bar: 0 none, 1 take-profit, 2 stop-loss; and where it is.</summary>
        private int _newLeg;
        private decimal _newLegPrice;
        // The position bar's boxes (chart-local), filled in layout and painted behind their labels.
        private Rect _barTp, _barSl, _barMain, _barQty;

        /// <summary>Accent of the position bar (blue outline, quantity box and "×").</summary>
        private static readonly Color BarBlue = new Color32(41, 98, 255, 255);
        private static readonly Color BarTp = new Color32(8, 153, 129, 255);
        private static readonly Color BarSl = new Color32(247, 166, 0, 255);

        /// <summary>Light chart backgrounds (white, pale greys) get the light bar; everything darker gets the dark one.</summary>
        private bool LightChart
        {
            get
            {
                Color b = Theme.Background;
                return 0.2126f * b.r + 0.7152f * b.g + 0.0722f * b.b > 0.5f;
            }
        }

        private Color BarFill => LightChart ? Color.white : (Color)new Color32(19, 23, 34, 255);

        /// <summary>Where the position bar's "×" is (chart-local), or null with no position; lets tests click it like a player.</summary>
        public Vector2? ClosePoint => _closeButton.width > 0 ? _closeButton.center : (Vector2?)null;

        /// <summary>Where the position bar's TP or SL tab is (chart-local), or null if that leg already exists.</summary>
        public Vector2? ProtectionTabPoint(bool stopLoss)
        {
            Rect r = stopLoss ? _barSl : _barTp;
            return r.width > 0 ? r.center : (Vector2?)null;
        }
        private readonly List<(Order Order, Rect Rect)> _cancelButtons = new List<(Order, Rect)>();
        private Order _dragOrder;
        private decimal _dragPrice;

        public void SetTrading(ChartTrading trading) => _trading = trading;

        /// <summary>Where an order's "×" is drawn (chart-local), or null if it has none; lets tests click it like a player.</summary>
        public Vector2? CancelPointOf(long orderId)
        {
            foreach (var (order, rect) in _cancelButtons)
                if (order.Id == orderId) return rect.center;
            return null;
        }

        private static decimal LinePrice(Order o) => o.IsStop && !o.Triggered ? o.StopPrice : o.LimitPrice;

        /// <summary>
        /// What an order's pill says and its colour. A bracket leg shows the P&L the position would have if it filled
        /// there (teal take-profit, orange stop-loss); an entry order shows its side and type (blue buy, red sell).
        /// </summary>
        private (string Label, Color Color, bool Bracket) Describe(Order o, decimal price)
        {
            bool bracket = o.OcoGroup != 0;
            bool stop = o.IsStop && !o.Triggered;
            if (bracket)
            {
                Color c = stop ? BarSl : BarTp;
                return (LegPnl(price, o.RemainingQuantity), c, true);
            }
            string side = o.Side == OrderSide.Buy ? "Buy" : "Sell";
            return ($"{side} {(stop ? "stop" : "limit")}", o.Side == OrderSide.Buy ? BarBlue : StopLossColor, false);
        }

        /// <summary>What the position would make or lose on <paramref name="quantity"/> contracts filled at a price.</summary>
        private string LegPnl(decimal price, long quantity)
        {
            if (_trading.Quantity == 0) return Fmt.Price(price);
            decimal pnl = (price - _trading.AveragePrice) * Math.Sign(_trading.Quantity) * quantity * _trading.PointValue;
            return $"{(pnl >= 0 ? "+" : "\u2212")} {Math.Abs(pnl):N2} USD";
        }

        // The order pills laid out this rebuild (chart-local), painted behind their labels.
        private readonly List<(Order Order, decimal Price, Rect Main, Rect Close, float Sep, Color Color, bool Dashed)> _orderPills =
            new List<(Order, decimal, Rect, Rect, float, Color, bool)>();

        private void LayoutTrading()
        {
            _cancelButtons.Clear();
            _orderPills.Clear();
            if (_trading == null) return;

            _closeButton = _barTp = _barSl = _barMain = _barQty = Rect.zero;
            if (_trading.Quantity != 0)
            {
                // Right to left from the price axis: [TP] [SL] [qty | P&L | ×], centred on the line. Compact:
                // it sits on the entry line all the time, so it shouldn't cover the candles.
                const float h = 16, gap = 3, cw = 5.6f;
                decimal avg = _trading.AveragePrice;
                decimal pnl = (_trading.Last - avg) * _trading.Quantity * _trading.PointValue;
                float top = ClampY(Y((double)avg)) - h / 2;
                string qty = Fmt.Shares(Math.Abs(_trading.Quantity));
                string money = $"{(pnl >= 0 ? "+" : "\u2212")}{Math.Abs(pnl):N2}";

                float qtyW = cw * qty.Length + 8, moneyW = cw * money.Length + 10, closeW = 16;
                float right = _plot.xMax - 8;
                _barMain = new Rect(right - (qtyW + moneyW + closeW), top, qtyW + moneyW + closeW, h);
                _barQty = new Rect(_barMain.x, top, qtyW, h);
                _closeButton = new Rect(_barMain.xMax - closeW, top, closeW, h);
                float x = _barMain.x;

                bool hasTp = _trading.Orders.Exists(o => o.OcoGroup != 0 && !(o.IsStop && !o.Triggered));
                bool hasSl = _trading.Orders.Exists(o => o.OcoGroup != 0 && o.IsStop && !o.Triggered);
                if (_trading.AddProtection != null)
                {
                    if (!hasSl) { _barSl = new Rect(x - gap - 20, top, 20, h); x = _barSl.x; }
                    if (!hasTp) { _barTp = new Rect(x - gap - 20, top, 20, h); x = _barTp.x; }
                }

                float ty = top + 1;
                if (_barTp.width > 0) SmallText(_barTp.x + 3, ty, "TP", BarTp);
                if (_barSl.width > 0) SmallText(_barSl.x + 4, ty, "SL", BarSl);
                SmallText(_barQty.x + 4, ty, qty, Color.white);
                SmallText(_barQty.xMax + 5, ty, money, pnl >= 0 ? BarTp : StopLossColor);
                SmallText(_closeButton.x + 4, ty, "\u00D7", LightChart ? BarBlue : new Color(0.75f, 0.78f, 0.85f));
            }

            // One pill per order at the right end of its line: [qty | P&L or "Buy limit" | ×].
            foreach (Order o in _trading.Orders)
            {
                decimal price = o == _dragOrder ? _dragPrice : LinePrice(o);
                if (price <= 0m) continue;
                var (label, color, bracket) = Describe(o, price);
                Rect close = Pill(o, price, Fmt.Shares(o.RemainingQuantity), label, color, bracket);
                if (_trading.Cancel != null) _cancelButtons.Add((o, close));
            }

            // The leg being dragged out of the position bar follows the pointer until it is dropped.
            if (_newLeg != 0 && _trading.Quantity != 0)
            {
                long qty = Math.Abs(_trading.Quantity);
                Pill(null, _newLegPrice, Fmt.Shares(qty), LegPnl(_newLegPrice, qty), _newLeg == 2 ? BarSl : BarTp, true);
            }
        }

        /// <summary>Lays out one order pill [qty | label | ×] at the right end of its line; returns the "×" area.</summary>
        private Rect Pill(Order o, decimal price, string qty, string label, Color color, bool dashed)
        {
            const float h = 26, cw = 8.4f, closeW = 26;
            float qtyW = cw * qty.Length + 14, labelW = cw * label.Length + 16;
            float top = ClampY(Y((double)price)) - h / 2;
            var main = new Rect(_plot.xMax - 8 - (qtyW + labelW + closeW), top, qtyW + labelW + closeW, h);
            var close = new Rect(main.xMax - closeW, top, closeW, h);
            _orderPills.Add((o, price, main, close, main.x + qtyW, color, dashed));
            float ty = top + 4;
            BarText(main.x + 7, ty, qty, color);
            BarText(main.x + qtyW + 8, ty, label, color);
            BarText(close.x + 8, ty, "\u00D7", color);
            return close;
        }

        /// <summary>Order pill text: larger and bold, like a trading platform's order pills.</summary>
        private void BarText(float x, float y, string text, Color color)
        {
            Label l = Text(x, y, text, color);
            l.style.fontSize = 13;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        /// <summary>Position bar text: small and bold.</summary>
        private void SmallText(float x, float y, string text, Color color)
        {
            Label l = Text(x, y, text, color);
            l.style.fontSize = 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        private void PaintTrading(Painter2D p)
        {
            if (_trading == null) return;
            if (_trading.Quantity != 0) HorizontalLine(p, (double)_trading.AveragePrice, BarBlue, 1.5f);
            // Bracket legs on dotted lines, entry orders on solid ones; each ends in its pill.
            foreach (var (o, price, main, close, sep, color, bracket) in _orderPills)
            {
                bool dragging = o == null || o == _dragOrder;
                HorizontalLine(p, (double)price, color, dragging ? 2f : 1.3f, dashed: bracket);
                BarBox(p, main, BarFill, color, bracket);
                foreach (float sx in new[] { sep, close.x })
                {
                    p.strokeColor = new Color(color.r, color.g, color.b, 0.45f);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(sx, main.y + 5));
                    p.LineTo(new Vector2(sx, main.yMax - 5));
                    p.Stroke();
                }
            }
            PaintPositionBar(p);
        }

        private void PaintPositionBar(Painter2D p)
        {
            if (_barMain.width <= 0) return;

            Color fill = BarFill;

            if (_barTp.width > 0) BarBox(p, _barTp, fill, BarTp, true);
            if (_barSl.width > 0) BarBox(p, _barSl, fill, BarSl, true);
            BarBox(p, _barMain, fill, BarBlue, false);
            // The contracts sit in a filled blue square at the start of the main box.
            p.fillColor = BarBlue;
            p.BeginPath();
            RoundRect(p, _barQty.x, _barQty.y, _barQty.width, _barQty.height, 3);
            p.Fill();
            // Hairline between the P&L and the close button.
            p.strokeColor = new Color(BarBlue.r, BarBlue.g, BarBlue.b, 0.35f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(_closeButton.x, _closeButton.y + 3));
            p.LineTo(new Vector2(_closeButton.x, _closeButton.yMax - 3));
            p.Stroke();
        }

        /// <summary>A rounded box that hides the line behind it, with a solid or dashed outline.</summary>
        private static void BarBox(Painter2D p, Rect r, Color fill, Color outline, bool dashed)
        {
            p.fillColor = fill;
            p.BeginPath();
            RoundRect(p, r.x, r.y, r.width, r.height, 3);
            p.Fill();
            p.strokeColor = outline;
            p.lineWidth = 1f;
            p.BeginPath();
            if (!dashed)
            {
                RoundRect(p, r.x + 0.5f, r.y + 0.5f, r.width - 1, r.height - 1, 3);
                p.Stroke();
                return;
            }
            // Dashes along each edge (3 on, 2 off).
            void Dash(Vector2 a, Vector2 b)
            {
                float len = Vector2.Distance(a, b);
                Vector2 d = (b - a) / len;
                for (float t = 0; t < len; t += 5)
                {
                    p.MoveTo(a + d * t);
                    p.LineTo(a + d * Math.Min(len, t + 3));
                }
            }
            float x0 = r.x + 0.5f, y0 = r.y + 0.5f, x1 = r.xMax - 0.5f, y1 = r.yMax - 0.5f;
            Dash(new Vector2(x0, y0), new Vector2(x1, y0));
            Dash(new Vector2(x1, y0), new Vector2(x1, y1));
            Dash(new Vector2(x1, y1), new Vector2(x0, y1));
            Dash(new Vector2(x0, y1), new Vector2(x0, y0));
            p.Stroke();
        }

        private static void RoundRect(Painter2D p, float x, float y, float w, float h, float r)
        {
            p.MoveTo(new Vector2(x + r, y));
            p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + h), r);
            p.ArcTo(new Vector2(x + w, y + h), new Vector2(x, y + h), r);
            p.ArcTo(new Vector2(x, y + h), new Vector2(x, y), r);
            p.ArcTo(new Vector2(x, y), new Vector2(x + w, y), r);
            p.ClosePath();
        }

        /// <summary>
        /// Starts dragging an order line or a new TP/SL out of the position bar, or presses an "×". True if the click
        /// was for trading.
        /// </summary>
        private bool TryBeginTradingDrag(Vector2 pos)
        {
            _dragOrder = null;
            _newLeg = 0;
            if (_trading == null || !_plot.Contains(pos)) return false;
            foreach (var (order, rect) in _cancelButtons)
            {
                if (!rect.Contains(pos)) continue;
                if (!_trading.Cancel(order.Id)) ShowStatus("That order is no longer working.");
                _trading.Changed?.Invoke();
                return true;
            }
            if (_closeButton.Contains(pos) && _trading.Close != null)
            {
                string error = _trading.Close();
                ShowStatus(error ?? "Position closed.");
                _trading.Changed?.Invoke();
                return true;
            }
            if (_barTp.Contains(pos) || _barSl.Contains(pos))
            {
                _newLeg = _barSl.Contains(pos) ? 2 : 1;
                _newLegPrice = _trading.AveragePrice;
                return true;
            }
            if (_barMain.Contains(pos)) return true; // the bar itself is not a drag target
            foreach (var (o, _, main, _, _, _, _) in _orderPills)
            {
                if (o == null || !main.Contains(pos)) continue;
                _dragOrder = o;
                _dragPrice = LinePrice(o);
                return true;
            }
            foreach (Order o in _trading.Orders)
            {
                decimal price = LinePrice(o);
                if (price <= 0m || Math.Abs(Y((double)price) - pos.y) > 5) continue;
                _dragOrder = o;
                _dragPrice = price;
                return true;
            }
            return false;
        }

        private void DragOrder(Vector2 pos)
        {
            decimal price = PriceTick.RoundNearest((decimal)Math.Max(0.0001, PriceAt(pos.y)));
            if (_newLeg != 0) _newLegPrice = price;
            else if (_dragOrder != null) _dragPrice = price;
        }

        private void EndOrderDrag()
        {
            if (_newLeg != 0)
            {
                // Dropped back on the entry price (a click without a drag): nothing to place.
                bool stopLoss = _newLeg == 2;
                decimal price = _newLegPrice;
                _newLeg = 0;
                if (price == _trading.AveragePrice || _trading.AddProtection == null) return;
                string legError = _trading.AddProtection(stopLoss, price);
                if (legError != null) ShowStatus(legError);
                _trading.Changed?.Invoke();
                return;
            }
            Order o = _dragOrder;
            _dragOrder = null;
            if (o == null || _trading?.Modify == null || _dragPrice == LinePrice(o)) return;
            string error = _trading.Modify(o.Id, _dragPrice);
            if (error != null) ShowStatus(error);
            _trading.Changed?.Invoke();
        }
    }
}
