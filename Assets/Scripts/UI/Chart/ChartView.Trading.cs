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
        public long Quantity;
        public decimal AveragePrice;
        public decimal Last;
        public readonly List<Order> Orders = new List<Order>();
        /// <summary>Moves a working order to a new price; returns an error or null.</summary>
        public Func<long, decimal, string> Modify;
        /// <summary>Creates a take-profit + stop-loss bracket for the position; returns an error or null.</summary>
        public Func<decimal, decimal, string> CreateBracket;
    }

    /// <summary>
    /// The position line (average entry with live P&L) and a line per working order. Take-profit, stop-loss and limit
    /// lines can be dragged: releasing moves the real order (the order manager validates it, so the line and the order
    /// never disagree). "+ TP/SL" on the position line creates a bracket to drag into place.
    /// </summary>
    public sealed partial class ChartView
    {
        private static readonly Color PositionColor = new Color32(90, 156, 245, 255);
        private static readonly Color TakeProfitColor = new Color32(52, 199, 123, 255);
        private static readonly Color StopLossColor = new Color32(232, 84, 76, 255);
        private static readonly Color OrderColor = new Color32(255, 152, 0, 255);

        private ChartTrading _trading;
        private Rect _bracketButton;
        private Order _dragOrder;
        private decimal _dragPrice;

        public void SetTrading(ChartTrading trading) => _trading = trading;

        private static decimal LinePrice(Order o) => o.IsStop && !o.Triggered ? o.StopPrice : o.LimitPrice;

        private (string Label, Color Color) Describe(Order o)
        {
            bool bracket = o.OcoGroup != 0;
            string qty = Fmt.Shares(o.RemainingQuantity);
            if (o.IsStop && !o.Triggered)
                return bracket && o.Side == OrderSide.Sell ? ($"SL {qty}", StopLossColor) : ($"{o.Side.ToString().ToUpperInvariant()} STOP {qty}", OrderColor);
            if (bracket && o.Side == OrderSide.Sell) return ($"TP {qty}", TakeProfitColor);
            return ($"{o.Side.ToString().ToUpperInvariant()} LMT {qty}", o.Side == OrderSide.Buy ? PositionColor : OrderColor);
        }

        private void LayoutTrading()
        {
            _bracketButton = Rect.zero;
            if (_trading == null) return;

            if (_trading.Quantity > 0)
            {
                decimal avg = _trading.AveragePrice;
                decimal pnl = (_trading.Last - avg) * _trading.Quantity;
                decimal pct = avg > 0 ? (_trading.Last - avg) / avg * 100m : 0m;
                float y = ClampY(Y((double)avg)) - 9;
                Label a = Text(_plot.x + 6, y, $"AVG {Fmt.Price(avg)} · {Fmt.Shares(_trading.Quantity)} sh", Color.white, PositionColor);
                float x = _plot.x + 12 + 7.2f * a.text.Length + 8;
                Label b = Text(x, y, $"{Fmt.SignedMoney(pnl)}  {Fmt.Percent(pct)}", Color.white, pnl >= 0 ? TakeProfitColor : StopLossColor);
                x += 7.2f * b.text.Length + 16;

                bool hasBracket = _trading.Orders.Exists(o => o.OcoGroup != 0);
                if (!hasBracket && _trading.CreateBracket != null)
                {
                    Text(x, y, "+ TP/SL", Color.white, new Color(0.25f, 0.28f, 0.34f));
                    _bracketButton = new Rect(x - 2, y - 2, 64, 22);
                }
            }

            foreach (Order o in _trading.Orders)
            {
                decimal price = o == _dragOrder ? _dragPrice : LinePrice(o);
                if (price <= 0m) continue;
                var (label, color) = Describe(o);
                float y = ClampY(Y((double)price)) - 9;
                Text(_plot.xMax - 7.2f * (label.Length + 10) - 8, y, $"{label}  {Fmt.Price(price)}", Color.white, color);
            }
        }

        private void PaintTrading(Painter2D p)
        {
            if (_trading == null) return;
            if (_trading.Quantity > 0) HorizontalLine(p, (double)_trading.AveragePrice, PositionColor, 1.5f);
            foreach (Order o in _trading.Orders)
            {
                decimal price = o == _dragOrder ? _dragPrice : LinePrice(o);
                if (price > 0m) HorizontalLine(p, (double)price, Describe(o).Color, o == _dragOrder ? 2f : 1.3f, dashed: true);
            }
        }

        /// <summary>Starts dragging an order line, or presses "+ TP/SL". True if the click was for trading.</summary>
        private bool TryBeginTradingDrag(Vector2 pos)
        {
            _dragOrder = null;
            if (_trading == null || !_plot.Contains(pos)) return false;
            if (_bracketButton.Contains(pos))
            {
                CreateDefaultBracket();
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
            if (_dragOrder == null) return;
            _dragPrice = PriceTick.RoundNearest((decimal)Math.Max(0.0001, PriceAt(pos.y)));
        }

        private void EndOrderDrag()
        {
            Order o = _dragOrder;
            _dragOrder = null;
            if (o == null || _trading?.Modify == null || _dragPrice == LinePrice(o)) return;
            string error = _trading.Modify(o.Id, _dragPrice);
            if (error != null) ShowStatus(error);
        }

        /// <summary>A bracket a typical move away: take-profit two average ranges up, stop-loss one down (then drag them).</summary>
        private void CreateDefaultBracket()
        {
            var atr = new double[1];
            Indicators.Atr(_series, 14, _series.Count - 1, 1, atr);
            decimal last = _trading.Last;
            decimal range = double.IsNaN(atr[0]) || atr[0] <= 0 ? last * 0.01m : (decimal)atr[0];
            decimal tick = PriceTick.For(last);
            decimal tp = PriceTick.RoundNearest(last + Math.Max(2 * range, 3 * tick));
            decimal sl = PriceTick.RoundNearest(last - Math.Max(range, 3 * tick));
            string error = _trading.CreateBracket(tp, sl);
            if (error != null) ShowStatus(error);
        }
    }
}
