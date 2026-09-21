using OpeningBell.Market;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Top strip: broker, clock, session, index, account metrics, time controls.</summary>
    public sealed class AccountBarPanel : TerminalPanel
    {
        private static readonly (string label, float multiplier)[] Speeds = { ("1x", 1f), ("5x", 5f), ("30x", 30f), ("120x", 120f) };

        private readonly Label _clock, _session, _index;
        private readonly Label _equity, _cash, _buyingPower, _dayPnl, _realized, _unrealized;
        private readonly Button _pause;
        private readonly Button[] _speedButtons = new Button[Speeds.Length];

        public AccountBarPanel(TerminalContext context) : base(context, "account-bar")
        {
            var left = Ui.Box("bar-group", Root);
            Ui.Label("brand", left, context.Orders.Rules.Name.ToUpperInvariant());
            _clock = Ui.Label("clock", left);
            _session = Ui.Label("session-badge", left);
            _session.name = "session-badge";
            _index = Ui.Label("index-quote", left);

            Ui.Box("spacer", Root);

            var metrics = Ui.Box("bar-group", Root);
            _equity = Ui.Stat("EQUITY", metrics);
            _cash = Ui.Stat("CASH", metrics);
            _buyingPower = Ui.Stat("BUYING POWER", metrics);
            _dayPnl = Ui.Stat("DAY P&L", metrics);
            _realized = Ui.Stat("REALIZED (NET)", metrics);
            _unrealized = Ui.Stat("UNREALIZED", metrics);

            var time = Ui.Box("bar-group time-controls", Root);
            _pause = Ui.Button("PAUSE", TogglePause, "time-btn", time, "time-pause");
            for (int i = 0; i < Speeds.Length; i++)
            {
                float multiplier = Speeds[i].multiplier;
                _speedButtons[i] = Ui.Button(Speeds[i].label, () => SetSpeed(multiplier), "time-btn", time, "speed-" + Speeds[i].label);
            }
        }

        public override void Refresh()
        {
            var market = Context.Market;
            var account = Context.Account;

            Ui.SetText(_clock, $"DAY {Context.Game.Days.DayNumber}  ·  {Fmt.Date(Context.Clock.Now)}  {Fmt.Clock(Context.Clock.Now)}");
            RefreshSession(market.Session);

            MarketIndex index = market.Index;
            Ui.SetText(_index, $"{index.Ticker} {Fmt.Price(index.Level)}  {Fmt.Percent(index.ChangePercent)}");
            Ui.SetSign(_index, index.Change);

            Ui.SetText(_equity, Fmt.Money(account.Equity));
            Ui.SetText(_cash, Fmt.Money(account.Cash));
            Ui.SetText(_buyingPower, Fmt.Money(account.BuyingPower));
            SetSigned(_dayPnl, account.DailyPnL);
            SetSigned(_realized, account.RealizedPnL - account.TotalCommissions);
            SetSigned(_unrealized, account.UnrealizedPnL);

            Ui.SetText(_pause, Context.Game.IsPaused ? "RESUME" : "PAUSE");
            _pause.EnableInClassList("active", Context.Game.IsPaused);
            for (int i = 0; i < Speeds.Length; i++)
                _speedButtons[i].EnableInClassList("active", Context.Game.SpeedMultiplier == Speeds[i].multiplier);
        }

        private void RefreshSession(MarketSession session)
        {
            string text = session switch
            {
                MarketSession.Premarket => "PRE-MARKET",
                MarketSession.Regular => "MARKET OPEN",
                MarketSession.AfterHours => "AFTER HOURS",
                _ => "CLOSED",
            };
            Ui.SetText(_session, text);
            _session.EnableInClassList("session-open", session == MarketSession.Regular);
            _session.EnableInClassList("session-extended", session == MarketSession.Premarket || session == MarketSession.AfterHours);
        }

        private static void SetSigned(Label label, decimal value)
        {
            Ui.SetText(label, Fmt.SignedMoney(value));
            Ui.SetSign(label, value);
        }

        private void TogglePause()
        {
            Context.Game.IsPaused = !Context.Game.IsPaused;
            Refresh();
        }

        private void SetSpeed(float multiplier)
        {
            Context.Game.SpeedMultiplier = multiplier;
            Context.Game.IsPaused = false;
            Refresh();
        }
    }
}
