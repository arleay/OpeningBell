using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// The computer's news app: a filterable feed on the left, the story on the right with the live market reaction
    /// of everything it mentions, links to trade or look the stock up, and more stories on the same names.
    /// </summary>
    public sealed class NewsroomApp : TerminalPanel
    {
        private enum Filter { All, Markets, Companies, Breaking }

        private const int RowHeight = 62;
        private const int Related = 5;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly (Filter filter, string label)[] Filters =
            { (Filter.All, "ALL"), (Filter.Markets, "MARKETS"), (Filter.Companies, "COMPANIES"), (Filter.Breaking, "BREAKING") };

        private readonly List<NewsItem> _items = new List<NewsItem>();
        private readonly ListView _list;
        private readonly Label _count;
        private readonly Button[] _filterButtons = new Button[Filters.Length];
        private readonly ScrollView _article;
        private readonly List<Action> _live = new List<Action>();
        private Filter _filter = Filter.All;
        private NewsItem _reading;
        private int _knownCount = -1;
        private long _knownMinute = -1;

        public NewsroomApp(TerminalContext context) : base(context, "newsroom-app")
        {
            var left = Ui.Box("app-column app-card newsroom-feed", Root);
            var head = Ui.Box("newsroom-head", left);
            Ui.Label("newsroom-brand", head, "KELL STREET WIRE");
            Ui.Box("spacer", head);
            _count = Ui.Label("muted", head);
            var chips = Ui.Box("quick-row newsroom-filters", left);
            for (int i = 0; i < Filters.Length; i++)
            {
                Filter f = Filters[i].filter;
                _filterButtons[i] = Ui.Button(Filters[i].label, () => SetFilter(f), "chip", chips, "filter-" + Filters[i].label.ToLowerInvariant());
            }
            _list = new ListView(_items, RowHeight, MakeRow, BindRow) { selectionType = SelectionType.None, name = "newsroom-list" };
            _list.AddToClassList("news-list");
            left.Add(_list);

            var right = Ui.Box("app-column app-wide app-card newsroom-story", Root);
            _article = new ScrollView(ScrollViewMode.Vertical) { name = "newsroom-article" };
            _article.AddToClassList("app-grow");
            right.Add(_article);

            context.StoryRequested += Read;
        }

        public NewsItem Reading => _reading;

        private void SetFilter(Filter filter)
        {
            _filter = filter;
            _knownCount = -1;
            Refresh();
        }

        private bool Matches(NewsItem item) => _filter switch
        {
            Filter.Markets => item.Scope != NewsScope.Security,
            Filter.Companies => item.Scope == NewsScope.Security,
            Filter.Breaking => item.IsMajor,
            _ => true,
        };

        public override void Refresh()
        {
            IReadOnlyList<NewsItem> feed = Context.Market.News;
            long minute = Context.Market.Now.Ticks / TimeSpan.TicksPerMinute;
            if (feed.Count != _knownCount)
            {
                _knownCount = feed.Count;
                _items.Clear();
                for (int i = feed.Count - 1; i >= 0; i--)
                    if (Matches(feed[i])) _items.Add(feed[i]);
                Ui.SetText(_count, $"{_items.Count} {(_items.Count == 1 ? "story" : "stories")}");
                for (int i = 0; i < Filters.Length; i++) _filterButtons[i].EnableInClassList("active", Filters[i].filter == _filter);
                if (_reading == null && _items.Count > 0) Read(_items[0]);
                else if (_reading == null) ShowEmpty();
                _list.RefreshItems();
            }
            else if (minute != _knownMinute) _list.RefreshItems(); // "x minutes ago" only changes by the minute
            _knownMinute = minute;
            foreach (Action update in _live) update();
        }

        /// <summary>Opens a story (from the feed, a desktop headline, or a search result).</summary>
        public void Read(NewsItem item)
        {
            _reading = item;
            _live.Clear();
            _article.Clear();
            VisualElement body = _article.contentContainer;

            Ui.Label("article-tag", body, NewsText.Tag(item)).EnableInClassList("major", item.IsMajor);
            var headline = Ui.Label("article-headline", body, item.Headline);
            headline.name = "article-headline";
            Ui.Label("muted article-time", body, item.Time.ToString("dddd, MMMM d · h:mm tt", C) + "  ·  Kell Street Wire");
            string dek = NewsText.Dek(item.Type);
            if (dek.Length > 0) Ui.Label("article-dek", body, dek);

            Ui.Label("panel-title article-section", body, "MARKET REACTION");
            MarketSimulation market = Context.Market;
            if (item.Scope == NewsScope.Market)
            {
                MarketIndex index = market.Index;
                Reaction(body, index.Ticker, index.Spec.Name, () => (index.Level, index.ChangePercent), null);
            }
            foreach (string ticker in item.Tickers)
                if (market.TryGetSecurity(ticker, out SecurityRuntimeState s))
                    Reaction(body, s.Ticker, s.Spec.CompanyName, () => (s.Last, s.ChangePercent), s.Ticker);

            var related = new List<NewsItem>();
            IReadOnlyList<NewsItem> feed = market.News;
            for (int i = feed.Count - 1; i >= 0 && related.Count < Related; i--)
                if (feed[i] != item && Shares(feed[i], item)) related.Add(feed[i]);
            if (related.Count > 0)
            {
                Ui.Label("panel-title article-section", body, "MORE ON THIS");
                foreach (NewsItem r in related)
                {
                    var row = Ui.Box("news-row article-related", body);
                    var meta = Ui.Box("news-meta", row);
                    Ui.Label("news-time muted", meta, $"{Fmt.Date(r.Time)} {Fmt.Minutes(r.Time)}");
                    Ui.Label("news-tickers", meta, NewsText.Tag(r));
                    Ui.Label("news-headline", row, r.Headline);
                    NewsItem target = r;
                    row.RegisterCallback<ClickEvent>(_ => Read(target));
                }
            }
            _list.RefreshItems();
            foreach (Action update in _live) update();
        }

        private void ShowEmpty()
        {
            _article.Clear();
            Ui.Label("article-empty muted", _article.contentContainer, "No stories yet today. Headlines land here as they break.");
        }

        private static bool Shares(NewsItem a, NewsItem b)
        {
            if (a.Scope == NewsScope.Market && b.Scope == NewsScope.Market) return true;
            foreach (string t in a.Tickers)
                foreach (string u in b.Tickers)
                    if (t == u) return true;
            return false;
        }

        /// <summary>One live quote row; stocks also get TRADE and PROFILE links.</summary>
        private void Reaction(VisualElement parent, string ticker, string name, Func<(decimal price, decimal pct)> quote, string stock)
        {
            var row = Ui.Box("reaction-row", parent);
            row.name = "reaction-" + ticker;
            var col = Ui.Box("reaction-name", row);
            Ui.Label("symbol", col, ticker);
            Ui.Label("muted", col, name);
            Ui.Box("spacer", row);
            var price = Ui.Label("reaction-price", row);
            var change = Ui.Label("reaction-change", row);
            _live.Add(() =>
            {
                var (p, pct) = quote();
                Ui.SetText(price, Fmt.Price(p));
                Ui.SetText(change, Fmt.Percent(pct));
                Ui.SetSign(change, pct);
            });
            if (stock == null) return;
            Ui.Button("TRADE", () => Context.Trade(stock), "row-btn", row, "trade-" + stock);
            Ui.Button("PROFILE", () => Context.OpenUrl(TickerSite.Host + "/" + stock), "row-btn", row, "profile-" + stock);
        }

        private VisualElement MakeRow()
        {
            var row = Ui.Box("news-row newsroom-row");
            var meta = Ui.Box("news-meta", row);
            Ui.Label("news-tickers", meta);
            Ui.Box("spacer", meta);
            Ui.Label("news-time muted", meta);
            Ui.Label("news-headline", row);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is NewsItem item) Read(item);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            NewsItem item = _items[index];
            row.userData = item;
            row.name = "story-" + item.Id;
            Ui.SetText((Label)row[0][0], NewsText.Tag(item));
            Ui.SetText((Label)row[0][2], NewsText.Ago(item.Time, Context.Market.Now));
            Ui.SetText((Label)row[1], item.Headline);
            row.EnableInClassList("major", item.IsMajor);
            row.EnableInClassList("selected", item == _reading);
            row.EnableInClassList("fresh", (Context.Market.Now - item.Time).TotalMinutes < 15);
        }
    }
}
