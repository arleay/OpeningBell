using System;
using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    public sealed class WatchlistPanel : TerminalPanel
    {
        private sealed class Row
        {
            public SecurityRuntimeState Security;
            public VisualElement Root, NewsDot;
            public Label Last, Change, Volume;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<string> _newsToday = new HashSet<string>();

        public WatchlistPanel(TerminalContext context) : base(context, "watchlist")
        {
            Ui.Label("panel-title", Root, "WATCHLIST");
            var header = Ui.Box("table-row table-header", Root);
            Ui.Label("col-symbol", header, "SYMBOL");
            Ui.Label("col-num", header, "LAST");
            Ui.Label("col-num", header, "CHG%");
            Ui.Label("col-num", header, "VOL");

            foreach (var security in context.Market.Securities)
            {
                var row = new Row { Security = security, Root = Ui.Box("table-row watch-row", Root) };
                row.Root.name = "watch-" + security.Ticker;
                var symbolCell = Ui.Box("col-symbol symbol-cell", row.Root);
                Ui.Label("symbol", symbolCell, security.Ticker);
                row.NewsDot = Ui.Box("news-dot", symbolCell);
                row.Last = Ui.Label("col-num", row.Root);
                row.Change = Ui.Label("col-num", row.Root);
                row.Volume = Ui.Label("col-num muted", row.Root);
                string ticker = security.Ticker;
                row.Root.RegisterCallback<ClickEvent>(_ => Context.Select(ticker));
                _rows.Add(row);
            }

            context.SelectionChanged += Refresh;
        }

        public override void Refresh()
        {
            // Stocks with a headline today get a dot, so catalysts are visible at a glance.
            _newsToday.Clear();
            var feed = Context.Market.News;
            DateTime today = Context.Market.TradingDate;
            for (int i = feed.Count - 1; i >= 0 && feed[i].Time.Date >= today; i--)
                foreach (string ticker in feed[i].Tickers) _newsToday.Add(ticker);

            foreach (Row row in _rows)
            {
                var s = row.Security;
                Ui.Show(row.NewsDot, _newsToday.Contains(s.Ticker));
                Ui.SetText(row.Last, Fmt.Price(s.Last));
                Ui.SetText(row.Change, Fmt.Percent(s.ChangePercent));
                Ui.SetSign(row.Change, s.Change);
                Ui.SetText(row.Volume, Fmt.Volume(s.DayVolume));
                row.Root.EnableInClassList("selected", s.Ticker == Context.SelectedTicker);
            }
        }
    }
}
