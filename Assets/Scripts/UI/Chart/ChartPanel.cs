using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;
using Position = OpeningBell.Trading.Position;

namespace OpeningBell.UI
{
    public sealed class ChartPanel : TerminalPanel
    {
        private static readonly (Timeframe tf, string label)[] Timeframes =
        {
            (Timeframe.Minute1, "1m"), (Timeframe.Minute5, "5m"), (Timeframe.Minute15, "15m"), (Timeframe.Hour1, "1h"), (Timeframe.Day1, "1D"),
        };

        private readonly ChartView _view;
        private readonly Label _title;
        private readonly Button[] _timeframeButtons = new Button[Timeframes.Length];
        private Timeframe _timeframe = Timeframe.Minute1;
        private string _ticker;
        private long _knownTick = -1;

        public ChartView View => _view;

        public ChartPanel(TerminalContext context) : base(context, "chart")
        {
            var toolbar = Ui.Box("chart-toolbar", Root);
            _title = Ui.Label("panel-title chart-title", toolbar);
            for (int i = 0; i < Timeframes.Length; i++)
            {
                Timeframe tf = Timeframes[i].tf;
                _timeframeButtons[i] = Ui.Button(Timeframes[i].label, () => SetTimeframe(tf), "tf-btn", toolbar, "tf-" + Timeframes[i].label);
            }
            Ui.Box("spacer", toolbar);
            Ui.Label("legend legend-vwap", toolbar, "VWAP");
            Ui.Label("legend legend-avg", toolbar, "AVG COST");
            Ui.Label("legend legend-news", toolbar, "NEWS");
            Ui.Label("legend muted", toolbar, "wheel: zoom · drag: pan · double-click: live");

            _view = new ChartView();
            Root.Add(_view);
            context.SelectionChanged += Refresh;
        }

        public override void Refresh()
        {
            SecurityRuntimeState s = Context.Selected;
            if (s.Ticker != _ticker)
            {
                _ticker = s.Ticker;
                LoadSeries();
            }

            Position position = Context.Account.Portfolio.Find(s.Ticker);
            _view.SetOverlays(position != null && position.IsOpen ? position.AveragePrice : 0m, Context.Orders.Fills, Context.Market.News);

            if (Context.Market.TickCount != _knownTick)
            {
                _knownTick = Context.Market.TickCount;
                _view.Refresh();
            }
        }

        private void SetTimeframe(Timeframe timeframe)
        {
            _timeframe = timeframe;
            LoadSeries();
        }

        private void LoadSeries()
        {
            SecurityRuntimeState s = Context.Selected;
            _view.SetSeries(s.Candles.Get(_timeframe), _timeframe, s.Ticker);
            for (int i = 0; i < Timeframes.Length; i++)
                _timeframeButtons[i].EnableInClassList("active", Timeframes[i].tf == _timeframe);
            Ui.SetText(_title, $"{s.Ticker} · {Timeframes[(int)_timeframe].label}");
        }
    }
}
