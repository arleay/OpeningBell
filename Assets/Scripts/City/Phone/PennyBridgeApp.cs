using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.City
{
    /// <summary>
    /// PennyBridge on the phone: the same account as the desk terminal. Watchlist, positions and orders tabs; a
    /// stock page with today's chart, stats and your position; and an order sheet (market or limit, whole contracts
    /// only) that goes through the same order manager and rules as the desk.
    /// </summary>
    internal sealed class PennyBridgeApp : PhoneScreen
    {
        private enum Page { Watchlist, Positions, Orders, Stock }

        public override PhoneAppId Id => PhoneAppId.PennyBridge;

        private readonly VisualElement _home, _stock;
        private readonly Label _equity, _dayPnl, _session;
        private readonly ScrollView _list, _stockBody;
        private readonly List<Label> _tabs = new List<Label>();
        private Page _page = Page.Watchlist;
        private string _ticker;

        // The order sheet: built once, its fields keep what was typed while the page behind it refreshes.
        private readonly VisualElement _sheet;
        private readonly Label _sheetTitle, _estimate, _status;
        private readonly TextField _quantity, _limit;
        private readonly Label _marketType, _limitType, _submit;
        private readonly VisualElement _limitRow;
        private OrderSide _side;
        private OrderType _type = OrderType.Market;
        private Order _last;

        public string Ticker => _ticker;
        public bool SheetOpen => _sheet.style.display == DisplayStyle.Flex;
        public string StatusText => _status.text;
        public TextField QuantityField => _quantity;

        public PennyBridgeApp(Phone phone) : base(phone)
        {
            Root.style.backgroundColor = new Color(0.03f, 0.05f, 0.09f);

            // ---- home: account header, tabs, list
            _home = PhoneKit.Box(Root, "pb-home");
            _home.style.flexGrow = 1;
            var header = PhoneKit.Box(_home);
            header.style.paddingTop = 50f;
            header.style.paddingLeft = header.style.paddingRight = 18f;
            var brand = PhoneKit.Row(header);
            PhoneKit.Label(brand, "PennyBridge", 15f, new Color(0.45f, 0.9f, 0.6f), true);
            _session = PhoneKit.Label(brand, "", 11f, PhoneKit.Muted, true);
            _equity = PhoneKit.Label(header, "", 32f, PhoneKit.Text, true);
            _equity.name = "pb-equity";
            _equity.style.marginTop = 6f;
            _dayPnl = PhoneKit.Label(header, "", 13f, PhoneKit.Muted, true);

            var tabs = PhoneKit.Row(_home, Justify.FlexStart);
            tabs.style.marginLeft = tabs.style.marginRight = 14f;
            tabs.style.marginTop = 12f;
            tabs.style.marginBottom = 8f;
            tabs.style.backgroundColor = new Color(1f, 1f, 1f, 0.07f);
            PhoneKit.Radius(tabs, 10f);
            PhoneKit.Pad(tabs, 3f, 3f);
            foreach (Page p in new[] { Page.Watchlist, Page.Positions, Page.Orders })
            {
                var tab = PhoneKit.Label(tabs, p.ToString(), 13f, PhoneKit.Text, true);
                tab.name = "pb-tab-" + p;
                tab.style.flexGrow = 1;
                tab.style.unityTextAlign = TextAnchor.MiddleCenter;
                tab.style.height = 28f;
                PhoneKit.Radius(tab, 8f);
                Page page = p;
                PhoneKit.Tap(tab, () => { _page = page; Refresh(); });
                _tabs.Add(tab);
            }
            _list = List(_home);

            // ---- stock page
            _stock = PhoneKit.Box(Root, "pb-stock");
            _stock.style.flexGrow = 1;
            _stock.style.display = DisplayStyle.None;
            var back = PhoneKit.Label(_stock, "‹ PennyBridge", 16f, PhoneKit.Blue);
            back.name = "back";
            back.style.marginTop = 50f;
            back.style.marginLeft = 18f;
            PhoneKit.Tap(back, () => ShowHome(Page.Watchlist));
            _stockBody = List(_stock);
            var trade = PhoneKit.Row(_stock, Justify.SpaceBetween);
            trade.style.paddingLeft = trade.style.paddingRight = 16f;
            trade.style.paddingBottom = 30f;
            trade.style.paddingTop = 8f;
            var buy = PhoneKit.Pill(trade, "Buy", PhoneKit.Green, Color.white, () => OpenSheet(OrderSide.Buy), 16f);
            buy.name = "pb-buy";
            buy.style.width = 118f;
            var sell = PhoneKit.Pill(trade, "Sell", PhoneKit.Red, Color.white, () => OpenSheet(OrderSide.Sell), 16f);
            sell.name = "pb-sell";
            sell.style.width = 118f;

            // ---- order sheet (slides over the stock page)
            _sheet = PhoneKit.Box(Root, "pb-sheet");
            PhoneKit.Absolute(_sheet, 0f, bottom: 0f);
            _sheet.style.width = Phone.ScreenWidth;
            _sheet.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            _sheet.style.borderTopLeftRadius = _sheet.style.borderTopRightRadius = 22f;
            PhoneKit.Pad(_sheet, 18f, 14f);
            _sheet.style.paddingBottom = 34f;
            _sheet.style.display = DisplayStyle.None;
            var sheetTop = PhoneKit.Row(_sheet);
            _sheetTitle = PhoneKit.Label(sheetTop, "", 20f, PhoneKit.Text, true);
            var cancel = PhoneKit.Label(sheetTop, "Close", 15f, PhoneKit.Blue);
            cancel.name = "pb-sheet-close";
            PhoneKit.Tap(cancel, CloseSheet);

            var types = PhoneKit.Row(_sheet, Justify.FlexStart);
            types.style.marginTop = 10f;
            types.style.backgroundColor = new Color(1f, 1f, 1f, 0.07f);
            PhoneKit.Radius(types, 10f);
            PhoneKit.Pad(types, 3f, 3f);
            _marketType = TypeTab(types, "Market", OrderType.Market);
            _limitType = TypeTab(types, "Limit", OrderType.Limit);

            PhoneKit.Label(_sheet, "CONTRACTS", 11f, PhoneKit.Muted, true).style.marginTop = 12f;
            var qtyRow = PhoneKit.Row(_sheet);
            qtyRow.style.marginTop = 4f;
            _quantity = PhoneKit.Field(qtyRow, "1", "pb-qty");
            TicketInput.Restrict(_quantity, ","); // whole contracts only: letters never get in
            _quantity.RegisterValueChangedCallback(_ => RefreshSheet());
            var quick = PhoneKit.Row(_sheet, Justify.SpaceBetween);
            quick.style.marginTop = 6f;
            foreach (long q in new long[] { 1, 2, 5 })
            {
                long n = q;
                var p = PhoneKit.Pill(quick, Fmt.Shares(q), new Color(1f, 1f, 1f, 0.08f), PhoneKit.Text, () => _quantity.value = Fmt.Shares(n), 13f);
                p.style.width = 56f;
            }
            var max = PhoneKit.Pill(quick, "Max", new Color(1f, 1f, 1f, 0.08f), PhoneKit.Text, SetMax, 13f);
            max.name = "pb-max";
            max.style.width = 56f;

            _limitRow = PhoneKit.Box(_sheet);
            PhoneKit.Label(_limitRow, "LIMIT PRICE", 11f, PhoneKit.Muted, true).style.marginTop = 12f;
            var limitFieldRow = PhoneKit.Row(_limitRow);
            limitFieldRow.style.marginTop = 4f;
            _limit = PhoneKit.Field(limitFieldRow, "", "pb-limit");
            TicketInput.Restrict(_limit, ".,$");
            _limit.RegisterValueChangedCallback(_ => RefreshSheet());

            _estimate = PhoneKit.Label(_sheet, "", 13f, PhoneKit.Muted);
            _estimate.style.marginTop = 12f;
            var submit = PhoneKit.Box(_sheet, "pb-submit");
            submit.style.marginTop = 12f;
            submit.style.height = 46f;
            submit.style.justifyContent = Justify.Center;
            PhoneKit.Radius(submit, 14f);
            _submit = PhoneKit.Label(submit, "", 16f, Color.white, true);
            _submit.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Tap(submit, Submit);
            _status = PhoneKit.Label(_sheet, "", 13f, PhoneKit.Muted);
            _status.name = "pb-status";
            _status.style.marginTop = 8f;
        }

        public static string SessionText(MarketSession session) => session switch
        {
            MarketSession.Regular => "MARKET OPEN",
            MarketSession.Premarket => "PRE-MARKET",
            MarketSession.AfterHours => "AFTER HOURS",
            _ => "CLOSED",
        };

        private Label TypeTab(VisualElement parent, string text, OrderType type)
        {
            var tab = PhoneKit.Label(parent, text, 13f, PhoneKit.Text, true);
            tab.name = "pb-type-" + text;
            tab.style.flexGrow = 1;
            tab.style.height = 28f;
            tab.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Radius(tab, 8f);
            PhoneKit.Tap(tab, () => SetType(type));
            return tab;
        }

        public override void Opened()
        {
            if (_page != Page.Stock) ShowHome(_page);
        }

        private void ShowHome(Page page)
        {
            _page = page;
            _ticker = null;
            CloseSheet();
            _stock.style.display = DisplayStyle.None;
            _home.style.display = DisplayStyle.Flex;
            Invalidate();
            Refresh();
        }

        /// <summary>Opens a stock's page (from the list, a position or a news story).</summary>
        public void ShowStock(string ticker)
        {
            if (!Phone.Game.Market.TryGetSecurity(ticker, out _)) return;
            _ticker = ticker;
            _page = Page.Stock;
            CloseSheet();
            _home.style.display = DisplayStyle.None;
            _stock.style.display = DisplayStyle.Flex;
            Invalidate();
            Refresh();
        }

        public override void Refresh()
        {
            Account account = Phone.Game.Account;
            MarketSimulation market = Phone.Game.Market;
            if (_page == Page.Stock)
            {
                BuildStock();
                if (SheetOpen) RefreshSheet();
                return;
            }

            _session.text = SessionText(market.Session);
            _equity.text = Fmt.Money(account.Equity);
            _dayPnl.text = $"{Fmt.SignedMoney(account.DailyPnL)} today  ·  BP {Fmt.Money(account.BuyingPower)}";
            _dayPnl.style.color = PhoneKit.SignColor(account.DailyPnL);
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].style.backgroundColor = (int)_page == i ? new Color(1f, 1f, 1f, 0.16f) : Color.clear;

            OrderManager orders = Phone.Game.Orders;
            var sig = new System.Text.StringBuilder(_page.ToString());
            if (_page == Page.Positions)
                foreach (SecurityRuntimeState s in market.Securities)
                {
                    Position p = account.Portfolio.Find(s.Ticker);
                    if (p != null && p.IsOpen) sig.Append('|').Append(s.Ticker).Append(p.Quantity).Append('@').Append(p.AveragePrice);
                }
            if (_page == Page.Orders)
            {
                foreach (Order o in orders.OpenOrders) sig.Append('|').Append(o.Id).Append(':').Append(o.FilledQuantity);
                sig.Append("|f").Append(orders.Fills.Count);
            }
            if (!Rebuild(sig.ToString())) return;

            _list.Clear();
            VisualElement c = _list.contentContainer;
            switch (_page)
            {
                case Page.Watchlist:
                {
                    var (_, price, pill) = Row(c, market.Index.Ticker, market.Index.Spec.Name, null);
                    Live(() => SetQuote(price, pill, Fmt.Price(market.Index.Level), market.Index.ChangePercent));
                    foreach (SecurityRuntimeState s in market.Securities)
                    {
                        SecurityRuntimeState sec = s;
                        var (_, p, q) = Row(c, s.Ticker, s.Spec.CompanyName, () => ShowStock(sec.Ticker));
                        Live(() => SetQuote(p, q, Fmt.Price(sec.Last), sec.ChangePercent));
                    }
                    break;
                }

                case Page.Positions:
                    bool any = false;
                    foreach (SecurityRuntimeState s in market.Securities)
                    {
                        Position p = account.Portfolio.Find(s.Ticker);
                        if (p == null || !p.IsOpen) continue;
                        any = true;
                        SecurityRuntimeState sec = s;
                        var (_, value, pnlPill) = Row(c, s.Ticker, $"{Fmt.Shares(p.Quantity)} ct @ {Fmt.Price(p.AveragePrice)}", () => ShowStock(sec.Ticker));
                        Live(() =>
                        {
                            decimal pnl = p.UnrealizedPnL(account.MarkPrice(sec.Ticker));
                            value.text = Fmt.Money(p.Quantity * sec.Last);
                            pnlPill.text = Fmt.SignedMoney(pnl);
                            pnlPill.style.backgroundColor = PhoneKit.SignColor(pnl);
                        });
                    }
                    if (!any) PhoneKit.Label(c, "No open positions. Pick a stock from the watchlist to buy.", 14f, PhoneKit.Muted).style.marginTop = 10f;
                    break;

                case Page.Orders:
                    PhoneKit.Label(c, "OPEN", 11f, PhoneKit.Muted, true).style.marginTop = 4f;
                    if (orders.OpenOrders.Count == 0) PhoneKit.Label(c, "No working orders.", 14f, PhoneKit.Muted).style.marginTop = 4f;
                    foreach (Order o in orders.OpenOrders)
                    {
                        var row = PhoneKit.Row(c);
                        row.style.paddingTop = row.style.paddingBottom = 9f;
                        row.style.borderBottomWidth = 1f;
                        row.style.borderBottomColor = PhoneKit.Separator;
                        PhoneKit.Label(row, $"{o.Side} {Fmt.Shares(o.Quantity - o.FilledQuantity)} {o.Ticker} @ {Fmt.OrderPrice(o)}", 14f, PhoneKit.Text, true);
                        long id = o.Id;
                        PhoneKit.Pill(row, "Cancel", new Color(1f, 0.27f, 0.23f, 0.2f), PhoneKit.Red, () => { orders.Cancel(id); Refresh(); }, 12f).name = "pb-cancel-" + id;
                    }
                    PhoneKit.Label(c, "RECENT FILLS", 11f, PhoneKit.Muted, true).style.marginTop = 16f;
                    IReadOnlyList<Fill> fills = orders.Fills;
                    if (fills.Count == 0) PhoneKit.Label(c, "Nothing filled yet.", 14f, PhoneKit.Muted).style.marginTop = 4f;
                    for (int i = fills.Count - 1; i >= Math.Max(0, fills.Count - 15); i--)
                    {
                        Fill f = fills[i];
                        var row = PhoneKit.Row(c);
                        row.style.paddingTop = row.style.paddingBottom = 8f;
                        row.style.borderBottomWidth = 1f;
                        row.style.borderBottomColor = PhoneKit.Separator;
                        PhoneKit.Label(row, $"{(f.Side == OrderSide.Buy ? "Bought" : "Sold")} {Fmt.Shares(f.Quantity)} {f.Ticker}", 14f, PhoneKit.Text);
                        PhoneKit.Label(row, "$" + Fmt.Price(f.Price), 14f, PhoneKit.Muted);
                    }
                    break;
            }
        }

        private static void SetQuote(Label price, Label pill, string text, decimal changePercent)
        {
            price.text = text;
            pill.text = Fmt.Percent(changePercent);
            pill.style.backgroundColor = changePercent > 0m ? PhoneKit.Green : changePercent < 0m ? PhoneKit.Red : new Color(0.3f, 0.3f, 0.33f);
        }

        private static (VisualElement Row, Label Price, Label Pill) Row(VisualElement parent, string ticker, string sub, Action open)
        {
            var row = PhoneKit.Row(parent);
            row.name = "pb-row-" + ticker;
            row.style.paddingTop = row.style.paddingBottom = 9f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = PhoneKit.Separator;
            var left = PhoneKit.Box(row);
            left.style.flexShrink = 1;
            PhoneKit.Label(left, ticker, 16f, PhoneKit.Text, true);
            PhoneKit.Label(left, sub, 11f, PhoneKit.Muted);
            var right = PhoneKit.Row(row, Justify.FlexEnd);
            var price = PhoneKit.Label(right, "", 15f, PhoneKit.Text, true);
            price.style.marginRight = 8f;
            var pill = PhoneKit.Label(right, "", 13f, Color.white, true);
            pill.name = "pill";
            pill.style.minWidth = 70f;
            pill.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Radius(pill, 7f);
            PhoneKit.Pad(pill, 6f, 4f);
            if (open != null) PhoneKit.Tap(row, open);
            return (row, price, pill);
        }

        // ------------------------------------------------------------------ stock page

        private void BuildStock()
        {
            MarketSimulation market = Phone.Game.Market;
            if (!market.TryGetSecurity(_ticker, out SecurityRuntimeState s)) return;
            Account account = Phone.Game.Account;
            Position p = account.Portfolio.Find(s.Ticker);
            bool holding = p != null && p.IsOpen;
            NewsItem latest = null;
            IReadOnlyList<NewsItem> news = market.News;
            for (int i = news.Count - 1; i >= 0 && latest == null; i--)
                if (news[i].Mentions(s.Ticker)) latest = news[i];
            if (!Rebuild($"stock|{s.Ticker}|{holding}|{latest?.Id}")) return;

            _stockBody.Clear();
            VisualElement c = _stockBody.contentContainer;
            PhoneKit.Label(c, s.Ticker, 13f, PhoneKit.Muted, true).style.marginTop = 6f;
            PhoneKit.Label(c, s.Spec.CompanyName, 22f, PhoneKit.Text, true);
            var price = PhoneKit.Label(c, "", 34f, PhoneKit.Text, true);
            price.name = "pb-price";
            price.style.marginTop = 4f;
            var change = PhoneKit.Label(c, "", 14f, PhoneKit.Muted, true);
            Live(() =>
            {
                price.text = "$" + Fmt.Price(s.Last);
                change.text = $"{Fmt.PriceDelta(s.Change, s.Last)} ({Fmt.Percent(s.ChangePercent)}) today";
                change.style.color = PhoneKit.SignColor(s.Change);
            });

            // Today's line: 1-minute closes of the latest day so far, against the previous close.
            var chart = PhoneKit.Box(c, "pb-chart");
            chart.style.height = 130f;
            chart.style.marginTop = 10f;
            var points = new List<float>();
            var empty = PhoneKit.Label(chart, "The chart fills in as the day trades.", 12f, PhoneKit.Muted);
            chart.generateVisualContent += ctx =>
                DrawLine(ctx, chart.contentRect, points, (float)s.PreviousClose, s.Change >= 0m ? PhoneKit.Green : PhoneKit.Red);
            Live(() =>
            {
                points.Clear();
                CandleSeries series = s.Candles.Get(Timeframe.Minute1);
                DateTime day = series.Count > 0 ? series[series.Count - 1].Start.Date : DateTime.MinValue;
                for (int i = Math.Max(0, series.Count - 400); i < series.Count; i++)
                    if (series[i].Start.Date == day) points.Add((float)series[i].Close);
                empty.style.display = points.Count < 2 ? DisplayStyle.Flex : DisplayStyle.None;
                chart.MarkDirtyRepaint();
            });

            var stats = Card(c);
            stats.style.marginTop = 12f;
            Label bidAsk = Stat(stats, "Bid / Ask"), range = Stat(stats, "Day range"), volume = Stat(stats, "Volume");
            Stat(stats, "Sector").text = s.Spec.Sector.ToString();
            Live(() =>
            {
                bidAsk.text = $"{Fmt.Price(s.Bid)} / {Fmt.Price(s.Ask)}";
                range.text = s.DayHigh > 0m ? $"{Fmt.Price(s.DayLow)} – {Fmt.Price(s.DayHigh)}" : "—";
                volume.text = Fmt.Volume(s.DayVolume);
            });

            var pos = Card(c);
            if (holding)
            {
                Label shares = Stat(pos, "Your contracts"), avg = Stat(pos, "Average price"), value = Stat(pos, "Margin"), unreal = Stat(pos, "Unrealized");
                Live(() =>
                {
                    decimal pnl = p.UnrealizedPnL(account.MarkPrice(s.Ticker));
                    shares.text = Fmt.Shares(p.Quantity);
                    avg.text = "$" + Fmt.Price(p.AveragePrice);
                    value.text = Fmt.Money(System.Math.Abs(p.Quantity) * account.MarginPerContract(s.Ticker));
                    unreal.text = Fmt.SignedMoney(pnl);
                    unreal.style.color = PhoneKit.SignColor(pnl);
                });
            }
            else
            {
                Stat(pos, "Your contracts").text = "None";
            }

            // The latest headline about it.
            if (latest != null)
            {
                NewsItem item = latest;
                var card = Card(c);
                PhoneKit.Pad(card, 12f, 10f);
                PhoneKit.Label(card, "LATEST NEWS · " + item.Time.ToString("MMM d h:mm tt", System.Globalization.CultureInfo.InvariantCulture), 10f, PhoneKit.Muted, true);
                PhoneKit.Label(card, item.Headline, 14f, PhoneKit.Text, true).style.marginTop = 3f;
                PhoneKit.Tap(card, () =>
                {
                    Phone.Show(PhoneAppId.News);
                    ((NewsApp)Phone.App(PhoneAppId.News)).Read(item);
                });
            }
        }

        /// <summary>A name/value row in a card; returns the value label.</summary>
        private static Label Stat(VisualElement card, string name)
        {
            var row = PhoneKit.Row(card);
            PhoneKit.Pad(row, 12f, 8f);
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = PhoneKit.Separator;
            PhoneKit.Label(row, name, 14f, PhoneKit.Muted);
            return PhoneKit.Label(row, "", 14f, PhoneKit.Text, true);
        }

        private static void DrawLine(MeshGenerationContext ctx, Rect r, List<float> points, float reference, Color color)
        {
            if (points.Count < 2 || r.width <= 0f) return;
            float lo = reference, hi = reference;
            foreach (float v in points)
            {
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
            }
            float pad = Mathf.Max((hi - lo) * 0.08f, hi * 0.001f);
            lo -= pad;
            hi += pad;
            float Y(float v) => r.height - (v - lo) / (hi - lo) * r.height;

            Painter2D p = ctx.painter2D;
            // The previous close, dotted.
            p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
            p.lineWidth = 1f;
            for (float x = 0f; x < r.width; x += 8f)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x, Y(reference)));
                p.LineTo(new Vector2(Mathf.Min(x + 4f, r.width), Y(reference)));
                p.Stroke();
            }
            p.strokeColor = color;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < points.Count; i++)
            {
                var pt = new Vector2(i * r.width / (points.Count - 1), Y(points[i]));
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.Stroke();
        }

        // ------------------------------------------------------------------ order sheet

        /// <summary>Opens the buy or sell sheet for the stock on screen.</summary>
        public void OpenSheet(OrderSide side)
        {
            if (_ticker == null) return;
            _side = side;
            _last = null;
            if (!TicketInput.TryParsePrice(_limit.value, out _) && Phone.Game.Market.TryGetSecurity(_ticker, out SecurityRuntimeState s))
                _limit.value = Fmt.Price(s.Last);
            _sheet.style.display = DisplayStyle.Flex;
            RefreshSheet();
        }

        private void CloseSheet()
        {
            _sheet.style.display = DisplayStyle.None;
            _limit.value = "";
            _last = null;
        }

        private void SetType(OrderType type)
        {
            _type = type;
            RefreshSheet();
        }

        private void SetMax()
        {
            TicketInput.TryParsePrice(_limit.value, out decimal limit);
            long max = _side == OrderSide.Buy
                ? Phone.Game.Orders.MaxBuyQuantity(_ticker, _type, limit)
                : Phone.Game.Orders.AvailableToSell(_ticker);
            _quantity.value = Fmt.Shares(max);
        }

        private void RefreshSheet()
        {
            if (_ticker == null || !Phone.Game.Market.TryGetSecurity(_ticker, out SecurityRuntimeState s)) return;
            bool buy = _side == OrderSide.Buy;
            _sheetTitle.text = $"{(buy ? "Buy" : "Sell")} {_ticker}";
            _marketType.style.backgroundColor = _type == OrderType.Market ? new Color(1f, 1f, 1f, 0.16f) : Color.clear;
            _limitType.style.backgroundColor = _type == OrderType.Limit ? new Color(1f, 1f, 1f, 0.16f) : Color.clear;
            _limitRow.style.display = _type == OrderType.Limit ? DisplayStyle.Flex : DisplayStyle.None;

            bool hasQty = TicketInput.TryParseQuantity(_quantity.value, out long qty);
            bool hasLimit = TicketInput.TryParsePrice(_limit.value, out decimal limit);
            bool ready = hasQty && (_type == OrderType.Market || hasLimit);
            _submit.text = ready ? $"{(buy ? "Buy" : "Sell")} {Fmt.Shares(qty)} {(_type == OrderType.Market ? "at market" : "at $" + Fmt.Price(limit))}" : "Enter contracts";
            _submit.parent.style.backgroundColor = !ready ? new Color(0.3f, 0.3f, 0.33f) : buy ? PhoneKit.Green : PhoneKit.Red;

            if (ready)
            {
                ContractSpec contract = Phone.Game.Account.Contract(_ticker);
                decimal commission = Phone.Game.Orders.Rules.CommissionFor(qty);
                _estimate.text = buy
                    ? $"Margin {Fmt.Money(qty * contract.Margin)} + {Fmt.Money(commission)} fees · {Fmt.Money(qty * contract.PointValue)} per point"
                    : $"{Fmt.Money(commission)} fees · {Fmt.Money(qty * contract.PointValue)} per point";
            }
            else
            {
                _estimate.text = hasQty ? "Enter a limit price." : "Whole contracts only.";
            }

            if (_last != null)
            {
                Order o = _last;
                _status.text = o.Status switch
                {
                    OrderStatus.Rejected => "Rejected: " + o.StatusReason,
                    OrderStatus.Filled => $"Filled {Fmt.Shares(o.FilledQuantity)} at ${Fmt.Price(o.AverageFillPrice)}",
                    OrderStatus.PartiallyFilled => $"Partly filled {Fmt.Shares(o.FilledQuantity)}/{Fmt.Shares(o.Quantity)}. Working.",
                    OrderStatus.Working => "Working. It fills when the price reaches your limit.",
                    OrderStatus.Cancelled => "Cancelled: " + o.StatusReason,
                    _ => o.Status.ToString(),
                };
                _status.style.color = o.Status == OrderStatus.Rejected ? PhoneKit.Red : o.Status == OrderStatus.Filled ? PhoneKit.Green : PhoneKit.Muted;
            }
            else
            {
                MarketSession session = Phone.Game.Market.Session;
                _status.text = session == MarketSession.Closed ? "The market is closed. Orders aren't accepted."
                    : _type == OrderType.Market && session != MarketSession.Regular && !Phone.Game.Orders.Rules.AllowMarketOrdersOutsideRegularHours
                        ? "Market orders only work in regular hours. Use a limit."
                        : "";
                _status.style.color = PhoneKit.Muted;
            }
        }

        /// <summary>Sends the sheet's order through the broker (the same rules as the desk).</summary>
        public void Submit()
        {
            if (_ticker == null || !TicketInput.TryParseQuantity(_quantity.value, out long qty)) return;
            decimal limit = 0m;
            if (_type == OrderType.Limit && !TicketInput.TryParsePrice(_limit.value, out limit)) return;
            _last = Phone.Game.Orders.Submit(_ticker, _side, _type, qty, limit);
            RefreshSheet();
        }
    }
}
