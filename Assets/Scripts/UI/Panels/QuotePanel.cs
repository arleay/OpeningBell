using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Level 1 quote and key stats for the selected symbol.</summary>
    public sealed class QuotePanel : TerminalPanel
    {
        private readonly Label _ticker, _company, _last, _change;
        private readonly Label _bid, _ask, _spread, _volume, _vwap, _range, _prevClose, _float, _marketCap, _avgVolume, _latestNews;

        public QuotePanel(TerminalContext context) : base(context, "quote")
        {
            var head = Ui.Box("quote-head", Root);
            _ticker = Ui.Label("quote-ticker", head);
            _ticker.name = "quote-ticker";
            _company = Ui.Label("quote-company muted", head);
            Ui.Box("spacer", head);
            _last = Ui.Label("quote-last", head);
            _change = Ui.Label("quote-change", head);

            var stats = Ui.Box("quote-stats", Root);
            _bid = Ui.Stat("BID", stats);
            _ask = Ui.Stat("ASK", stats);
            _spread = Ui.Stat("SPREAD", stats);
            _volume = Ui.Stat("VOLUME", stats);
            _vwap = Ui.Stat("VWAP", stats);
            _range = Ui.Stat("DAY RANGE", stats);
            _prevClose = Ui.Stat("PREV CLOSE", stats);
            _float = Ui.Stat("$ / POINT", stats);
            _marketCap = Ui.Stat("MARGIN", stats);
            _avgVolume = Ui.Stat("AVG VOL", stats);
            _latestNews = Ui.Label("quote-news", Root);
            _latestNews.name = "quote-news";

            context.SelectionChanged += Refresh;
        }

        public override void Refresh()
        {
            SecurityRuntimeState s = Context.Selected;
            SecuritySpec spec = s.Spec;
            Quote q = s.Quote;

            Ui.SetText(_ticker, s.Ticker);
            Ui.SetText(_company, $"{spec.CompanyName} · {spec.Sector}");
            Ui.SetText(_last, Fmt.Price(s.Last));
            Ui.SetText(_change, $"{Fmt.PriceDelta(s.Change, s.Last)}  {Fmt.Percent(s.ChangePercent)}");
            Ui.SetSign(_change, s.Change);

            Ui.SetText(_bid, $"{Fmt.Price(q.Bid)} × {Fmt.Volume(q.BidSize)}");
            Ui.SetText(_ask, $"{Fmt.Price(q.Ask)} × {Fmt.Volume(q.AskSize)}");
            Ui.SetText(_spread, Fmt.PriceDelta(q.Spread, q.Ask, signed: false));
            Ui.SetText(_volume, Fmt.Volume(s.DayVolume));
            Ui.SetText(_vwap, s.DayVolume > 0 ? Fmt.Price(s.Vwap) : "—");
            Ui.SetText(_range, s.DayVolume > 0 ? $"{Fmt.Price(s.DayLow)} – {Fmt.Price(s.DayHigh)}" : "—");
            Ui.SetText(_prevClose, Fmt.Price(s.PreviousClose));
            // Contract terms: dollars per 1.00 move per contract, and the day margin one contract posts.
            ContractSpec contract = ContractSpec.For(spec);
            Ui.SetText(_float, Fmt.Money(contract.PointValue));
            Ui.SetText(_marketCap, Fmt.Money(contract.Margin));
            Ui.SetText(_avgVolume, Fmt.Volume(spec.AverageDailyVolume));

            NewsItem latest = LatestNews(s.Ticker);
            Ui.SetText(_latestNews, latest == null ? "No recent news." : $"{Fmt.Minutes(latest.Time)}  {latest.Headline}");
        }

        private NewsItem LatestNews(string ticker)
        {
            var feed = Context.Market.News;
            for (int i = feed.Count - 1; i >= 0; i--)
                if (feed[i].Mentions(ticker)) return feed[i];
            return null;
        }
    }
}
