using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// What the computer boots to: app icons on the left, live widgets on the right (clock, market with the index
    /// sparkline and the day's biggest movers, latest headlines, inbox). Widgets link into the apps.
    /// </summary>
    public sealed class DesktopScreen : TerminalPanel
    {
        private const int Movers = 3;
        private const int Headlines = 4;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly Label _time, _date, _session, _indexName, _indexLevel, _indexChange, _inbox, _inboxLatest;
        private readonly Sparkline _indexSpark;
        private readonly (Label ticker, Label change)[] _gainers = new (Label, Label)[Movers], _losers = new (Label, Label)[Movers];
        private readonly (VisualElement row, Label tag, Label headline)[] _headlines = new (VisualElement, Label, Label)[Headlines];
        private readonly List<SecurityRuntimeState> _sorted = new List<SecurityRuntimeState>();

        public DesktopScreen(TerminalContext context) : base(context, "desktop")
        {
            Root.Add(new Wallpaper());

            var icons = Ui.Box("desktop-icons", Root);
            foreach (var pin in Taskbar.Pinned)
            {
                TerminalApp app = pin.app;
                var icon = new Button(() => context.ShowApp(app)) { name = "icon-" + pin.id };
                icon.AddToClassList("desktop-icon");
                Glyph.Tile(pin.glyph, pin.tile, 64f, icon);
                Ui.Label("desktop-icon-label", icon, pin.label switch
                {
                    "TRADING" => "Trading Terminal",
                    "NEWS" => "News",
                    "MAIL" => "Mail",
                    _ => "Browser",
                });
                icons.Add(icon);
            }

            Ui.Box("spacer", Root);

            var widgets = Ui.Box("desktop-widgets", Root);

            var clock = Ui.Box("widget widget-clock", widgets);
            _time = Ui.Label("widget-time", clock);
            _time.name = "desktop-time";
            _date = Ui.Label("widget-date", clock);

            var markets = Widget(widgets, "MARKETS", () => context.ShowApp(TerminalApp.Broker), "widget-markets");
            _session = Ui.Label("session-badge widget-session", markets.header);
            var indexRow = Ui.Box("widget-index", markets.body);
            var indexText = Ui.Box("widget-index-text", indexRow);
            _indexName = Ui.Label("widget-index-name", indexText);
            _indexLevel = Ui.Label("widget-index-level", indexText);
            _indexChange = Ui.Label("widget-index-change", indexText);
            _indexSpark = new Sparkline("widget-spark");
            indexRow.Add(_indexSpark);
            var movers = Ui.Box("widget-movers", markets.body);
            MoverColumn(movers, "TOP GAINERS", _gainers);
            MoverColumn(movers, "TOP LOSERS", _losers);

            var news = Widget(widgets, "HEADLINES", () => context.ShowApp(TerminalApp.News), "widget-news");
            for (int i = 0; i < Headlines; i++)
            {
                var row = Ui.Box("widget-headline", news.body);
                row.name = "desktop-headline-" + i;
                var tag = Ui.Label("widget-headline-tag", row);
                var headline = Ui.Label("widget-headline-text", row);
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (row.userData is NewsItem item) context.ReadStory(item);
                    e.StopPropagation();
                });
                _headlines[i] = (row, tag, headline);
            }

            var mail = Widget(widgets, "INBOX", () => context.ShowApp(TerminalApp.Mail), "widget-inbox");
            _inbox = Ui.Label("widget-inbox-count", mail.body);
            _inboxLatest = Ui.Label("widget-inbox-latest muted", mail.body);
        }

        /// <summary>A card with a title; clicking anywhere in it opens <paramref name="open"/>.</summary>
        private static (VisualElement header, VisualElement body) Widget(VisualElement parent, string title, Action open, string classes)
        {
            var card = Ui.Box("widget " + classes, parent);
            card.RegisterCallback<ClickEvent>(_ => open());
            var header = Ui.Box("widget-header", card);
            Ui.Label("widget-title", header, title);
            Ui.Box("spacer", header);
            return (header, Ui.Box("widget-body", card));
        }

        private void MoverColumn(VisualElement parent, string title, (Label ticker, Label change)[] rows)
        {
            var col = Ui.Box("widget-mover-col", parent);
            Ui.Label("widget-subtitle", col, title);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = Ui.Box("widget-mover", col);
                var ticker = Ui.Label("widget-mover-ticker", row);
                var change = Ui.Label("widget-mover-change", row);
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (row.userData is string t) Context.Trade(t);
                    e.StopPropagation();
                });
                rows[i] = (ticker, change);
            }
        }

        public override void Refresh()
        {
            DateTime now = Context.Clock.Now;
            Ui.SetText(_time, now.ToString("h:mm tt", C));
            Ui.SetText(_date, $"{now.ToString("dddd, MMMM d", C)}  ·  Day {Context.Game.Days.DayNumber}");

            MarketSimulation market = Context.Market;
            Ui.SetText(_session, Taskbar.SessionName(market.Session, market.Now.TimeOfDay));
            _session.EnableInClassList("session-open", market.Session == MarketSession.Regular);
            _session.EnableInClassList("session-extended", market.Session == MarketSession.Premarket || market.Session == MarketSession.AfterHours);

            MarketIndex index = market.Index;
            Ui.SetText(_indexName, index.Spec.Name);
            Ui.SetText(_indexLevel, $"{index.Ticker}  {Fmt.Price(index.Level)}");
            Ui.SetText(_indexChange, $"{Fmt.PriceDelta(index.Change, index.Level)}  ({Fmt.Percent(index.ChangePercent)})");
            Ui.SetSign(_indexChange, index.Change);
            _indexSpark.Set(index.Candles.Get(Timeframe.Minute1), index.PreviousClose, market.Now);

            _sorted.Clear();
            _sorted.AddRange(market.Securities);
            _sorted.Sort((a, b) => b.ChangePercent.CompareTo(a.ChangePercent));
            for (int i = 0; i < Movers; i++)
            {
                SetMover(_gainers[i], i < _sorted.Count ? _sorted[i] : null);
                SetMover(_losers[i], i < _sorted.Count ? _sorted[_sorted.Count - 1 - i] : null);
            }

            IReadOnlyList<NewsItem> feed = market.News;
            for (int i = 0; i < Headlines; i++)
            {
                var (row, tag, headline) = _headlines[i];
                NewsItem item = i < feed.Count ? feed[feed.Count - 1 - i] : null;
                Ui.Show(row, item != null || i == 0);
                row.userData = item;
                Ui.SetText(tag, item == null ? "" : $"{NewsText.Tag(item)} · {NewsText.Ago(item.Time, now)}");
                Ui.SetText(headline, item == null ? "No stories yet today." : item.Headline);
                row.EnableInClassList("major", item != null && item.IsMajor);
            }

            var inbox = Context.Game.Inbox;
            int unread = inbox.UnreadCount;
            Ui.SetText(_inbox, unread == 0 ? "No unread mail" : unread == 1 ? "1 unread message" : $"{unread} unread messages");
            _inbox.EnableInClassList("attention", unread > 0);
            Ui.SetText(_inboxLatest, inbox.Emails.Count > 0 ? "Latest: " + inbox.Emails[inbox.Emails.Count - 1].Subject : "");
        }

        private static void SetMover((Label ticker, Label change) row, SecurityRuntimeState s)
        {
            row.ticker.parent.userData = s?.Ticker;
            Ui.SetText(row.ticker, s?.Ticker ?? "");
            Ui.SetText(row.change, s == null ? "" : Fmt.Percent(s.ChangePercent));
            Ui.SetSign(row.change, s?.ChangePercent ?? 0m);
        }
    }
}
