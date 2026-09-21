using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// "Session closed" card that pops over the terminal at the regular close (spec §50): the day's results so far.
    /// After-hours trading still counts toward the final day report.
    /// </summary>
    public sealed class DaySummaryPanel : TerminalPanel
    {
        private readonly Label _title, _pnl, _realized, _commissions, _fills, _winLoss, _best, _worst, _equity;

        public bool IsOpen => Root.style.display == DisplayStyle.Flex;

        public DaySummaryPanel(TerminalContext context) : base(context, "day-summary")
        {
            Root.name = "day-summary";
            _title = Ui.Label("summary-title", Root);
            Ui.Label("muted summary-sub", Root,
                $"Regular session closed. After-hours trading runs until {context.Market.Schedule.AfterHoursClose:hh\\:mm}.");

            var grid = Ui.Box("summary-grid", Root);
            _pnl = Ui.Stat("DAY P&L", grid, "stat summary-stat");
            _realized = Ui.Stat("REALIZED (NET)", grid, "stat summary-stat");
            _commissions = Ui.Stat("COMMISSIONS", grid, "stat summary-stat");
            _fills = Ui.Stat("FILLS", grid, "stat summary-stat");
            _winLoss = Ui.Stat("WINNERS / LOSERS", grid, "stat summary-stat");
            _best = Ui.Stat("BEST TRADE", grid, "stat summary-stat");
            _worst = Ui.Stat("WORST TRADE", grid, "stat summary-stat");
            _equity = Ui.Stat("EQUITY", grid, "stat summary-stat");

            Ui.Button("CONTINUE", () => Ui.Show(Root, false), "summary-continue", Root, "summary-continue");
            Ui.Show(Root, false);

            context.Market.SessionChanged += (previous, current) =>
            {
                if (previous == MarketSession.Regular && current == MarketSession.AfterHours) Open();
            };
        }

        public void Open()
        {
            Ui.Show(Root, true);
            Refresh();
        }

        public override void Refresh()
        {
            if (!IsOpen) return;
            TradingDayReport day = Context.Game.Days.Current;
            if (day == null) return;

            var account = Context.Account;
            Ui.SetText(_title, $"DAY {day.DayNumber} · {Fmt.Date(day.Date).ToUpperInvariant()}");
            SetSigned(_pnl, account.DailyPnL);
            SetSigned(_realized, day.RealizedPnL - day.Commissions);
            Ui.SetText(_commissions, Fmt.Money(day.Commissions));
            Ui.SetText(_fills, day.Fills.ToString());
            Ui.SetText(_winLoss, $"{day.Winners} / {day.Losers}");
            SetSigned(_best, day.BestTrade);
            SetSigned(_worst, day.WorstTrade);
            Ui.SetText(_equity, Fmt.Money(account.Equity));
        }

        private static void SetSigned(Label label, decimal value)
        {
            Ui.SetText(label, Fmt.SignedMoney(value));
            Ui.SetSign(label, value);
        }
    }
}
