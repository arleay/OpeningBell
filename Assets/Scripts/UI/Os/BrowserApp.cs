using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// A web browser with no real internet: every address resolves to a site built into the game (search, bank,
    /// marketplace, stock pages) and anything else gets a "can't reach" page. Plain words in the address bar search.
    /// Back/forward history like a real browser.
    /// </summary>
    public sealed class BrowserApp : TerminalPanel
    {
        public const string Home = SearchPage.Host;

        private sealed class Site
        {
            public string Host, Id, Bookmark, Title, Keywords;
            public GlyphKind Glyph;
            public BrowserPage Page;
        }

        private readonly List<Site> _sites = new List<Site>();
        private readonly NotFoundPage _notFound;
        private readonly VisualElement _view;
        private readonly TextField _address;
        private readonly Label _tab;
        private readonly Button _back, _forward;
        private readonly Dictionary<Site, Button> _bookmarks = new Dictionary<Site, Button>();
        private readonly List<string> _history = new List<string>();
        private int _index = -1;
        private BrowserPage _page;

        public string Url => _index >= 0 ? _history[_index] : "";
        public string Title => _tab.text;
        internal TerminalContext Ctx => Context;

        public BrowserApp(TerminalContext context, BankApp bank, StoreApp store) : base(context, "browser-app")
        {
            var tabs = Ui.Box("browser-tabs", Root);
            var tab = Ui.Box("browser-tab", tabs);
            Tile(tab, GlyphKind.Browser, "tile-browser", 16f);
            _tab = Ui.Label("browser-tab-title", tab);
            _tab.name = "browser-tab";

            var toolbar = Ui.Box("browser-toolbar", Root);
            _back = NavButton(toolbar, GlyphKind.Back, "nav-back", Back);
            _forward = NavButton(toolbar, GlyphKind.Forward, "nav-forward", Forward);
            NavButton(toolbar, GlyphKind.Reload, "nav-reload", () => Load(Url));
            NavButton(toolbar, GlyphKind.Home, "nav-home", () => Navigate(Home));
            var bar = Ui.Box("address-bar", toolbar);
            bar.Add(new Glyph(GlyphKind.Lock, 14f, new Color32(0x7c, 0x87, 0x95, 0xff), "address-lock"));
            _address = new TextField { name = "address" };
            _address.textEdition.placeholder = "Search or type an address";
            _address.AddToClassList("address-field");
            _address.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                Navigate(_address.value);
                _address.Blur();
            });
            bar.Add(_address);

            var bookmarks = Ui.Box("bookmarks-bar", Root);
            _view = Ui.Box("browser-view", Root);

            AddSite(new Site { Host = SearchPage.Host, Id = "search", Bookmark = "Kestrel Search", Title = "Kestrel Search",
                Keywords = "search web kestrel", Glyph = GlyphKind.Search, Page = new SearchPage(this) });
            AddSite(new Site { Host = "kvcu.com", Id = "bank", Bookmark = "KV Credit Union", Title = "Kell Valley Credit Union · Online Banking",
                Keywords = "bank banking credit union checking account transfer bills balance money kvcu",
                Glyph = GlyphKind.Bank,
                Page = new SiteFrame(this, "site-bank", GlyphKind.Bank, "Kell Valley Credit Union", "Online Banking", bank, scroll: false) });
            AddSite(new Site { Host = "valleymarket.com", Id = "market", Bookmark = "Valley Market", Title = "Valley Market · Shop, services and classifieds",
                Keywords = "store shop buy equipment chair monitor computer desk services lease office classifieds used cars car",
                Glyph = GlyphKind.Store,
                Page = new SiteFrame(this, "site-market", GlyphKind.Store, "Valley Market", "Equipment, services and local classifieds", store, scroll: true) });
            AddSite(new Site { Host = TickerSite.Host, Id = "tickerpage", Bookmark = "Tickerpage", Title = "Tickerpage · Stock quotes and market data",
                Keywords = "stocks stock quotes quote market markets ticker prices shares data movers",
                Glyph = GlyphKind.Chart, Page = new TickerSite(this) });
            AddSite(new Site { Host = PropFirms.Ridgeback.Host, Id = "ridgeback", Bookmark = "Ridgeback Funding",
                Title = "Ridgeback Funding · Pass the Trading Challenge, trade our capital",
                Keywords = "prop firm funded futures evaluation challenge combine payout ridgeback trading account",
                Glyph = GlyphKind.Trading,
                Page = new PropFirmSite(this, PropFirms.Ridgeback, "Pass the Trading Challenge. Trade our capital. Keep 90%.") });
            AddSite(new Site { Host = PropFirms.Harbor.Host, Id = "harbor", Bookmark = "Profit Harbor",
                Title = "Profit Harbor · Get funded, get paid daily",
                Keywords = "prop firm funded futures evaluation test pro daily payout harbor trading account",
                Glyph = GlyphKind.Trading,
                Page = new PropFirmSite(this, PropFirms.Harbor, "Get funded. Get paid daily.") });
            AddSite(new Site { Host = RegistrySite.Host, Id = "registry", Bookmark = "Business Registry",
                Title = "Kell Valley Business Registry · Form a company",
                Keywords = "business registry register company form fund hedge fund incorporate licence license",
                Glyph = GlyphKind.Bank, Page = new RegistrySite(this) });
            AddSite(new Site { Host = LedgerlineSite.Host, Id = "ledgerline", Bookmark = "Ledgerline",
                Title = "Ledgerline · Fund administration",
                Keywords = "ledgerline fund hedge fund dashboard employees traders recruit hire payroll capital risk office workstations training",
                Glyph = GlyphKind.Chart, Page = new LedgerlineSite(this) });
            _notFound = new NotFoundPage(this);
            _view.Add(_notFound.Root);

            foreach (Site site in _sites)
            {
                string host = site.Host;
                // Icon + label as children: a Button with children no longer sizes itself to its own text.
                var b = Ui.Button("", () => Navigate(host), "bookmark", bookmarks, "bookmark-" + site.Id);
                Glyph.Tile(site.Glyph, "tile-" + site.Id, 16f, b);
                Ui.Label("bookmark-label", b, site.Bookmark);
                _bookmarks[site] = b;
            }

            context.UrlRequested += Navigate;
            Navigate(Home);
        }

        private void AddSite(Site site)
        {
            _sites.Add(site);
            _view.Add(site.Page.Root);
        }

        private static void Tile(VisualElement parent, GlyphKind kind, string tile, float size) => Glyph.Tile(kind, tile, size, parent);

        private static Button NavButton(VisualElement parent, GlyphKind glyph, string name, Action onClick)
        {
            var b = new Button(onClick) { name = name };
            b.AddToClassList("nav-btn");
            b.Add(new Glyph(glyph, 16f, new Color32(0xc4, 0xcc, 0xd6, 0xff)));
            parent.Add(b);
            return b;
        }

        /// <summary>Goes to an address (or searches for plain words), adding it to the history.</summary>
        public void Navigate(string input)
        {
            string url = Resolve(input);
            if (_index >= 0 && _history[_index] == url)
            {
                Load(url);
                return;
            }
            if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            _history.Add(url);
            _index = _history.Count - 1;
            Load(url);
        }

        private void Back()
        {
            if (_index <= 0) return;
            _index--;
            Load(_history[_index]);
        }

        private void Forward()
        {
            if (_index >= _history.Count - 1) return;
            _index++;
            Load(_history[_index]);
        }

        /// <summary>
        /// Canonical "host/path": scheme and www. dropped, host lower-cased. Anything with a space or without a dot
        /// is a search, as in a real address bar.
        /// </summary>
        public static string Resolve(string input)
        {
            string s = (input ?? "").Trim();
            if (s.Length == 0) return Home;
            foreach (string scheme in new[] { "https://", "http://" })
                if (s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) s = s.Substring(scheme.Length);
            if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.IndexOf(' ') >= 0 || s.IndexOf('.') < 0) return SearchPage.Host + "/search?q=" + (input ?? "").Trim();
            int slash = s.IndexOf('/');
            string host = (slash < 0 ? s : s.Substring(0, slash)).ToLowerInvariant();
            string path = slash < 0 ? "" : s.Substring(slash + 1).Trim('/');
            return path.Length > 0 ? host + "/" + path : host;
        }

        private void Load(string url)
        {
            int slash = url.IndexOf('/');
            string host = slash < 0 ? url : url.Substring(0, slash);
            string path = slash < 0 ? "" : url.Substring(slash + 1);
            Site site = _sites.Find(s => s.Host == host);

            _page = site?.Page ?? _notFound;
            foreach (Site s in _sites) Ui.Show(s.Page.Root, s.Page == _page);
            Ui.Show(_notFound.Root, _page == _notFound);
            string title = _page.Show(_page == _notFound ? url : path);
            Ui.SetText(_tab, title);
            _address.SetValueWithoutNotify("https://" + url);
            _back.SetEnabled(_index > 0);
            _forward.SetEnabled(_index < _history.Count - 1);
            foreach (var pair in _bookmarks) pair.Value.EnableInClassList("active", pair.Key == site);
            _page.Refresh();
        }

        public override void Refresh() => _page?.Refresh();

        /// <summary>What the search engine can find besides stocks and news: the other sites.</summary>
        internal IEnumerable<(string url, string title, string keywords)> Directory()
        {
            foreach (Site s in _sites)
                if (s.Host != SearchPage.Host) yield return (s.Host, s.Title, s.Keywords);
        }
    }

    /// <summary>One website. <see cref="Show"/> lays out the page for a path and returns the tab title.</summary>
    internal abstract class BrowserPage
    {
        protected readonly BrowserApp Browser;
        protected TerminalContext Context => Browser.Ctx;
        public VisualElement Root { get; }

        protected BrowserPage(BrowserApp browser, string classes)
        {
            Browser = browser;
            Root = Ui.Box("browser-page " + classes);
        }

        public abstract string Show(string path);
        public virtual void Refresh() { }

        /// <summary>A site's coloured header: logo, name, tagline.</summary>
        protected static VisualElement Header(VisualElement parent, string siteClass, GlyphKind glyph, string name, string tagline)
        {
            var header = Ui.Box("site-header " + siteClass, parent);
            Glyph.Tile(glyph, "site-logo", 34f, header);
            var text = Ui.Box("site-titles", header);
            Ui.Label("site-name", text, name);
            Ui.Label("site-tagline", text, tagline);
            return header;
        }
    }

    /// <summary>An existing terminal app presented as a website: the site's header over the app.</summary>
    internal sealed class SiteFrame : BrowserPage
    {
        private readonly TerminalPanel _content;
        private readonly string _title;

        public SiteFrame(BrowserApp browser, string siteClass, GlyphKind glyph, string name, string tagline, TerminalPanel content, bool scroll)
            : base(browser, "site-frame")
        {
            _content = content;
            _title = name;
            Header(Root, siteClass, glyph, name, tagline);
            if (scroll)
            {
                var sv = new ScrollView(ScrollViewMode.Vertical);
                sv.AddToClassList("site-body");
                sv.AddToClassList("app-grow");
                sv.Add(content.Root);
                Root.Add(sv);
            }
            else
            {
                var body = Ui.Box("site-body app-grow", Root);
                body.Add(content.Root);
            }
        }

        public override string Show(string path) => _title;
        public override void Refresh() => _content.Refresh();
    }

    /// <summary>
    /// Kestrel, the search engine and home page. It indexes only what exists in the game: the sites, every listed
    /// stock (by ticker, company or sector) and the day's headlines.
    /// </summary>
    internal sealed class SearchPage : BrowserPage
    {
        public const string Host = "kestrel.com";
        private const int MaxStocks = 8, MaxNews = 6;

        private readonly VisualElement _home, _results, _list;
        private readonly TextField _homeBox, _resultsBox;
        private readonly Label _summary;

        public SearchPage(BrowserApp browser) : base(browser, "search-page")
        {
            _home = Ui.Box("search-home", Root);
            Ui.Label("search-logo", _home, "Kestrel");
            _homeBox = SearchBox(_home, "search-box");
            Ui.Label("muted search-hint", _home, "Search stocks, news and sites in Kell Valley. Try a ticker, a company or \"bank\".");

            _results = Ui.Box("search-results", Root);
            var top = Ui.Box("search-results-top", _results);
            Ui.Label("search-logo small", top, "Kestrel");
            _resultsBox = SearchBox(top, "search-box-results");
            _summary = Ui.Label("muted search-summary", _results);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("app-grow");
            _results.Add(scroll);
            _list = scroll.contentContainer;
            _list.name = "search-list";
        }

        private TextField SearchBox(VisualElement parent, string name)
        {
            var box = new TextField { name = name };
            box.textEdition.placeholder = "Search Kestrel or type an address";
            box.AddToClassList("search-field");
            box.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                string q = box.value.Trim();
                if (q.Length > 0) Browser.Navigate(Host + "/search?q=" + q);
            });
            parent.Add(box);
            return box;
        }

        public override string Show(string path)
        {
            const string prefix = "search?q=";
            string q = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length).Trim() : "";
            Ui.Show(_home, q.Length == 0);
            Ui.Show(_results, q.Length > 0);
            if (q.Length == 0)
            {
                _homeBox.SetValueWithoutNotify("");
                return "Kestrel Search";
            }
            _resultsBox.SetValueWithoutNotify(q);
            Search(q);
            return q + " · Kestrel Search";
        }

        private void Search(string query)
        {
            _list.Clear();
            string[] terms = Terms(query);
            int found = 0;

            foreach (var (url, title, keywords) in Browser.Directory())
                if (Any(terms, title + " " + keywords + " " + url))
                {
                    Result(url, title, "https://" + url, Snippet(url), () => Browser.Navigate(url));
                    found++;
                }

            MarketSimulation market = Context.Market;
            int stocks = 0;
            foreach (SecurityRuntimeState s in market.Securities)
            {
                if (stocks >= MaxStocks) break;
                bool tickerHit = Array.Exists(terms, t => string.Equals(t, s.Ticker, StringComparison.OrdinalIgnoreCase));
                if (!tickerHit && !Any(terms, s.Spec.CompanyName + " " + s.Spec.Sector)) continue;
                string ticker = s.Ticker;
                string url = TickerSite.Host + "/" + ticker;
                Result(url, $"{s.Spec.CompanyName} ({ticker}) stock price, news and data · Tickerpage", "https://" + url,
                    $"{Fmt.Price(s.Last)}  {Fmt.Percent(s.ChangePercent)} today · {s.Spec.Sector} · volume {Fmt.Volume(s.DayVolume)}",
                    () => Browser.Navigate(url), "result-" + ticker);
                stocks++;
                found++;
            }

            IReadOnlyList<NewsItem> news = market.News;
            int stories = 0;
            for (int i = news.Count - 1; i >= 0 && stories < MaxNews; i--)
            {
                NewsItem item = news[i];
                if (!Any(terms, item.Headline + " " + string.Join(" ", item.Tickers))) continue;
                NewsItem story = item;
                Result("news", item.Headline, "Kell Street Wire · " + NewsText.Ago(item.Time, market.Now), NewsText.Tag(item),
                    () => Context.ReadStory(story), "result-news-" + item.Id);
                stories++;
                found++;
            }

            Ui.SetText(_summary, found == 0 ? "" : found == 1 ? "1 result" : $"{found} results");
            if (found == 0)
                Ui.Label("search-empty", _list, $"No results for \"{query}\". Try a company name, a ticker, or \"bank\".");
        }

        private static string Snippet(string host) => host switch
        {
            "kvcu.com" => "Check your balance, move money to and from your brokerage, and see upcoming bills.",
            "valleymarket.com" => "Desk equipment, services and office leases, plus used cars from private sellers.",
            TickerSite.Host => "Live quotes, today's movers, company data and the latest headlines for every listed stock.",
            "ridgebackfunding.com" => "Trading Challenge from $49/mo. Hit the target, get an Express Funded account, keep 90% of payouts.",
            "profitharbor.com" => "Tests from $150/mo. Pass, go PRO and withdraw daily once you've built your buffer. 80/20 split.",
            _ => "",
        };

        private void Result(string url, string title, string shownUrl, string snippet, Action open, string name = null)
        {
            var row = Ui.Box("search-result", _list);
            if (name != null) row.name = name;
            Ui.Label("result-url", row, shownUrl);
            Ui.Label("result-title", row, title);
            if (snippet.Length > 0) Ui.Label("result-snippet", row, snippet);
            row.RegisterCallback<ClickEvent>(_ => open());
        }

        private static string[] Terms(string query)
        {
            var terms = new List<string>();
            foreach (string t in query.Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries))
                if (t.Length >= 2) terms.Add(t);
            return terms.ToArray();
        }

        private static bool Any(string[] terms, string text)
        {
            foreach (string t in terms)
                if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    /// <summary>What any address outside the game's sites gets.</summary>
    internal sealed class NotFoundPage : BrowserPage
    {
        private readonly Label _detail;

        public NotFoundPage(BrowserApp browser) : base(browser, "not-found-page")
        {
            var box = Ui.Box("not-found", Root);
            Ui.Label("not-found-title", box, "This site can't be reached");
            _detail = Ui.Label("not-found-detail", box);
            _detail.name = "not-found-detail";
            Ui.Label("muted not-found-hint", box, "Check the address for typos, or search for it on Kestrel.");
            Ui.Button("SEARCH KESTREL", () => Browser.Navigate(BrowserApp.Home), "", box, "not-found-search");
        }

        public override string Show(string url)
        {
            int slash = url.IndexOf('/');
            string host = slash < 0 ? url : url.Substring(0, slash);
            Ui.SetText(_detail, $"{host}'s server address could not be found.");
            return host;
        }
    }
}
