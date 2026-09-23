using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Headline feed, newest first. Clicking a headline selects its stock.</summary>
    public sealed class NewsPanel : TerminalPanel
    {
        private const int RowHeight = 44;
        private const double FreshMinutes = 15;

        private readonly List<NewsItem> _items = new List<NewsItem>();
        private readonly ListView _list;
        private readonly Label _title;
        private int _knownCount = -1;
        private long _knownMinute = -1;

        public NewsPanel(TerminalContext context) : base(context, "news")
        {
            _title = Ui.Label("panel-title", Root, "NEWS");
            _list = new ListView(_items, RowHeight, MakeRow, BindRow) { selectionType = SelectionType.None, name = "news-list" };
            _list.AddToClassList("news-list");
            Root.Add(_list);
        }

        public override void Refresh()
        {
            IReadOnlyList<NewsItem> feed = Context.Market.News;
            long minute = Context.Market.Now.Ticks / System.TimeSpan.TicksPerMinute;
            if (feed.Count == _knownCount && minute == _knownMinute) return; // "fresh" highlighting only changes by the minute

            _knownMinute = minute;
            if (feed.Count != _knownCount)
            {
                _knownCount = feed.Count;
                _items.Clear();
                for (int i = feed.Count - 1; i >= 0; i--) _items.Add(feed[i]);
                Ui.SetText(_title, $"NEWS ({feed.Count})");
            }
            _list.RefreshItems();
        }

        private VisualElement MakeRow()
        {
            var row = Ui.Box("news-row");
            var meta = Ui.Box("news-meta", row);
            Ui.Label("news-time muted", meta);
            Ui.Label("news-tickers", meta);
            Ui.Label("news-headline", row);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is NewsItem item && item.Tickers.Count > 0) Context.Select(item.Tickers[0]);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            NewsItem item = _items[index];
            row.userData = item;
            row.name = "news-" + item.Id;
            Ui.SetText((Label)row[0][0], Fmt.Minutes(item.Time));
            Ui.SetText((Label)row[0][1], item.Type == CatalystType.PresidentTweet ? "BREAKING · PRESIDENT"
                : item.IsMajor ? "BREAKING · MARKET"
                : item.Scope == NewsScope.Market ? "MARKET" : string.Join(" ", item.Tickers));
            row.EnableInClassList("major", item.IsMajor);
            Ui.SetText((Label)row[1], item.Headline);
            row.EnableInClassList("fresh", (Context.Market.Now - item.Time).TotalMinutes < FreshMinutes);
        }
    }
}
