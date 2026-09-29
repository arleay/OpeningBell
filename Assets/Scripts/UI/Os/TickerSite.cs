using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Tickerpage, a stock-data website. tickerpage.com lists the index and every stock by today's move;
    /// tickerpage.com/TICKER is a quote page with today's line, key numbers and the stock's headlines.
    /// Numbers stay live while the page is open.
    /// </summary>
    internal sealed class TickerSite : BrowserPage
    {
        public const string Host = "tickerpage.com";
        private const int StockNews = 8;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly VisualElement _content;
        private readonly TextField _lookup;
        private readonly List<Action> _live = new List<Action>();

        public TickerSite(BrowserApp browser) : base(browser, "ticker-site")
        {
            VisualElement header = Header(Root, "site-tickerpage", GlyphKind.Chart, "tickerpage", "Quotes · Movers · Company data");
            Ui.Box("spacer", header);
            Ui.Button("MARKETS", () => Browser.Navigate(Host), "site-link", header, "tp-markets");
            _lookup = new TextField { name = "tp-lookup" };
            _lookup.textEdition.placeholder = "Look up a symbol";
            _lookup.AddToClassList("search-field");
            _lookup.AddToClassList("tp-lookup");
            _lookup.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                string t = _lookup.value.Trim();
                if (t.Length > 0) Browser.Navigate(Host + "/" + t);
            });
            header.Add(_lookup);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("site-body");
            scroll.AddToClassList("app-grow");
            Root.Add(scroll);
            _content = scroll.contentContainer;
        }

        public override string Show(string path)
        {
            _live.Clear();
            _content.Clear();
            _lookup.SetValueWithoutNotify("");
            if (path.Length == 0)
            {
                BuildMarkets();
                return "Markets today · Tickerpage";
            }
            string ticker = path.ToUpperInvariant();
            if (Context.Market.TryGetSecurity(ticker, out SecurityRuntimeState s))
            {
                BuildStock(s);
                return $"{s.Ticker} {s.Spec.CompanyName} · Tickerpage";
            }
            Ui.Label("tp-title", _content, "Symbol not found");
            Ui.Label("muted", _content, $"No listed stock trades as \"{ticker}\". Check the symbol, or browse today's markets.");
            Ui.Button("ALL STOCKS", () => Browser.Navigate(Host), "tp-cta", _content);
            return "Symbol not found · Tickerpage";
        }

        public override void Refresh()
        {
            foreach (Action update in _live) update();
        }

        private void BuildMarkets()
        {
            MarketSimulation market = Context.Market;
            MarketIndex index = market.Index;
            Ui.Label("tp-title", _content, "Markets today");

            var card = Ui.Box("app-card tp-index", _content);
            var text = Ui.Box("tp-index-text", card);
            Ui.Label("muted", text, index.Spec.Name.ToUpperInvariant());
            var level = Ui.Label("tp-price", text);
            var change = Ui.Label("tp-change", text);
            var spark = new Sparkline("tp-index-spark", 2f);
            card.Add(spark);
            _live.Add(() =>
            {
                Ui.SetText(level, $"{index.Ticker}  {Fmt.Price(index.Level)}");
                Ui.SetText(change, $"{Fmt.PriceDelta(index.Change, index.Level)}  ({Fmt.Percent(index.ChangePercent)})");
                Ui.SetSign(change, index.Change);
                spark.Set(index.Candles.Get(Timeframe.Minute1), index.PreviousClose, market.Now);
            });

            Ui.Label("panel-title tp-section", _content, "ALL STOCKS · BY TODAY'S MOVE");
            var table = Ui.Box("app-card tp-table", _content);
            var head = Ui.Box("tp-row tp-head", table);
            foreach (var (label, cls) in Columns) Ui.Label(cls, head, label);

            var sorted = new List<SecurityRuntimeState>(market.Securities);
            sorted.Sort((a, b) => b.ChangePercent.CompareTo(a.ChangePercent));
            foreach (SecurityRuntimeState s in sorted)
            {
                var row = Ui.Box("tp-row data-row", table);
                row.name = "tp-row-" + s.Ticker;
                Ui.Label("tp-c-sym symbol", row, s.Ticker);
                Ui.Label("tp-c-name", row, s.Spec.CompanyName);
                Ui.Label("tp-c-sector muted", row, s.Spec.Sector.ToString());
                var last = Ui.Label("tp-c-num", row);
                var chg = Ui.Label("tp-c-num", row);
                var pct = Ui.Label("tp-c-num", row);
                var vol = Ui.Label("tp-c-num muted", row);
                SecurityRuntimeState stock = s;
                _live.Add(() =>
                {
                    Ui.SetText(last, Fmt.Price(stock.Last));
                    Ui.SetText(chg, Fmt.PriceDelta(stock.Change, stock.Last));
                    Ui.SetText(pct, Fmt.Percent(stock.ChangePercent));
                    Ui.SetSign(chg, stock.Change);
                    Ui.SetSign(pct, stock.Change);
                    Ui.SetText(vol, Fmt.Volume(stock.DayVolume));
                });
                string ticker = s.Ticker;
                row.RegisterCallback<ClickEvent>(_ => Browser.Navigate(Host + "/" + ticker));
            }
        }

        private static readonly (string label, string cls)[] Columns =
        {
            ("SYMBOL", "tp-c-sym"), ("COMPANY", "tp-c-name"), ("SECTOR", "tp-c-sector"), ("LAST", "tp-c-num"),
            ("CHANGE", "tp-c-num"), ("% CHANGE", "tp-c-num"), ("VOLUME", "tp-c-num"),
        };

        private void BuildStock(SecurityRuntimeState s)
        {
            SecuritySpec spec = s.Spec;
            MarketSimulation market = Context.Market;

            var titleRow = Ui.Box("tp-stock-head", _content);
            var titles = Ui.Box("", titleRow);
            var name = Ui.Label("tp-title", titles, spec.CompanyName);
            name.name = "tp-company";
            Ui.Label("muted", titles, $"{s.Ticker}  ·  {spec.Sector}  ·  Kell Valley Exchange");
            Ui.Box("spacer", titleRow);
            string ticker = s.Ticker;
            Ui.Button($"TRADE {ticker} IN TERMINAL", () => Context.Trade(ticker), "tp-cta", titleRow, "tp-trade");

            var priceRow = Ui.Box("tp-price-row", _content);
            var price = Ui.Label("tp-price big", priceRow);
            var change = Ui.Label("tp-change", priceRow);
            var asOf = Ui.Label("muted tp-asof", priceRow);

            var chart = Ui.Box("app-card tp-chart", _content);
            Ui.Label("panel-title", chart, "TODAY");
            var spark = new Sparkline("tp-stock-spark", 2.2f);
            chart.Add(spark);

            var grid = Ui.Box("app-card tp-stats", _content);
            Label Stat(string caption) => Ui.Stat(caption, grid, "stat tp-stat");
            Label prev = Stat("PREVIOUS CLOSE"), range = Stat("DAY RANGE"), vwap = Stat("VWAP"), volume = Stat("VOLUME");
            Label avg = Stat("AVG VOLUME"), cap = Stat("MARKET CAP"), shares = Stat("SHARES OUT"), flt = Stat("FLOAT");
            Label beta = Stat("BETA"), move = Stat("TYPICAL DAILY MOVE");
            bool equity = spec.PointValue <= 0 && spec.SharesOutstanding > 0;
            Ui.SetText(avg, Fmt.Volume(spec.AverageDailyVolume));
            Ui.SetText(shares, equity ? Compact(spec.SharesOutstanding) : "—");
            Ui.SetText(flt, equity ? Compact(spec.FloatShares) : "—");
            Ui.SetText(beta, spec.MarketBeta.ToString("0.00", C));
            Ui.SetText(move, (spec.DailyVolatility * 100).ToString("0.0", C) + "%");

            _live.Add(() =>
            {
                Ui.SetText(price, Fmt.Price(s.Last));
                Ui.SetText(change, $"{Fmt.PriceDelta(s.Change, s.Last)}  ({Fmt.Percent(s.ChangePercent)})");
                Ui.SetSign(change, s.Change);
                Ui.SetText(asOf, market.Session == MarketSession.Closed
                    ? "At close"
                    : $"{Taskbar.SessionName(market.Session, market.Now.TimeOfDay).ToLowerInvariant()} · as of {market.Now.ToString("h:mm tt", C)}");
                spark.Set(s.Candles.Get(Timeframe.Minute1), s.PreviousClose, market.Now);
                Ui.SetText(prev, Fmt.Price(s.PreviousClose));
                Ui.SetText(range, s.DayVolume == 0 ? "—" : $"{Fmt.Price(s.DayLow)} – {Fmt.Price(s.DayHigh)}");
                Ui.SetText(vwap, s.DayVolume == 0 ? "—" : Fmt.Price(s.Vwap));
                Ui.SetText(volume, Fmt.Volume(s.DayVolume));
                Ui.SetText(cap, equity ? "$" + Compact(s.Last * spec.SharesOutstanding) : "—");
            });

            Ui.Label("panel-title tp-section", _content, $"LATEST {s.Ticker} NEWS");
            var list = Ui.Box("app-card", _content);
            IReadOnlyList<NewsItem> news = market.News;
            int shown = 0;
            for (int i = news.Count - 1; i >= 0 && shown < StockNews; i--)
            {
                NewsItem item = news[i];
                if (!Contains(item.Tickers, s.Ticker)) continue;
                var row = Ui.Box("news-row", list);
                var meta = Ui.Box("news-meta", row);
                Ui.Label("news-time muted", meta, $"{Fmt.Date(item.Time)} {Fmt.Minutes(item.Time)}");
                Ui.Label("news-tickers", meta, NewsText.Tag(item));
                Ui.Label("news-headline", row, item.Headline);
                NewsItem story = item;
                row.RegisterCallback<ClickEvent>(_ => Context.ReadStory(story));
                shown++;
            }
            if (shown == 0) Ui.Label("muted", list, $"No {s.Ticker} headlines yet.");
        }

        private static bool Contains(IReadOnlyList<string> tickers, string ticker)
        {
            foreach (string t in tickers)
                if (t == ticker) return true;
            return false;
        }

        /// <summary>4.2B, 380.5M, 12.0K.</summary>
        private static string Compact(decimal value)
        {
            decimal a = Math.Abs(value);
            if (a >= 1_000_000_000_000m) return (value / 1_000_000_000_000m).ToString("0.00", C) + "T";
            if (a >= 1_000_000_000m) return (value / 1_000_000_000m).ToString("0.00", C) + "B";
            if (a >= 1_000_000m) return (value / 1_000_000m).ToString("0.0", C) + "M";
            if (a >= 1_000m) return (value / 1_000m).ToString("0.0", C) + "K";
            return value.ToString("0", C);
        }
    }
}
