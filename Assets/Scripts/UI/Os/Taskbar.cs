using System;
using OpeningBell.Market;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Bottom bar of the computer: home button, pinned apps, and a tray with the index, market session, game speed and
    /// clock. It is on screen in every app, so time controls and the mail badge are always one click away.
    /// </summary>
    public sealed class Taskbar : TerminalPanel
    {
        internal static readonly (TerminalApp app, string id, string label, GlyphKind glyph, string tile)[] Pinned =
        {
            (TerminalApp.Broker, "broker", "TRADING", GlyphKind.Trading, "tile-trading"),
            (TerminalApp.News, "news", "NEWS", GlyphKind.News, "tile-news"),
            (TerminalApp.Mail, "mail", "MAIL", GlyphKind.Mail, "tile-mail"),
            (TerminalApp.Browser, "browser", "BROWSER", GlyphKind.Browser, "tile-browser"),
        };

        private static readonly (string label, float multiplier)[] Speeds =
            { ("1x", 1f), ("2x", 2f), ("5x", 5f), ("10x", 10f), ("30x", 30f), ("60x", 60f), ("120x", 120f) };

        private readonly Button _home, _pause, _mail;
        private readonly Button[] _appButtons = new Button[Pinned.Length];
        private readonly Button[] _speedButtons = new Button[Speeds.Length];
        private readonly Label _index, _session, _time, _date;

        public Taskbar(TerminalContext context) : base(context, "taskbar")
        {
            _home = new Button(() => context.ShowApp(TerminalApp.Desktop)) { name = "app-desktop", tooltip = "Desktop" };
            _home.AddToClassList("task-home");
            _home.Add(new Glyph(GlyphKind.Home, 20f, new Color32(0xe0, 0xa9, 0x3b, 0xff)));
            Root.Add(_home);

            for (int i = 0; i < Pinned.Length; i++)
            {
                TerminalApp app = Pinned[i].app;
                Button b = Ui.Button(Pinned[i].label, () => context.ShowApp(app), "task-app", Root, "app-" + Pinned[i].id);
                // Absolutely placed so the button keeps its own text (tests and badges read Button.text).
                VisualElement tile = Glyph.Tile(Pinned[i].glyph, Pinned[i].tile, 22f, b);
                tile.AddToClassList("task-tile");
                _appButtons[i] = b;
            }
            _mail = _appButtons[Array.FindIndex(Pinned, p => p.app == TerminalApp.Mail)];
            context.AppChanged += Refresh;

            Ui.Box("spacer", Root);

            var tray = Ui.Box("bar-group tray", Root);
            _index = Ui.Label("index-quote", tray);
            _session = Ui.Label("session-badge", tray);
            _session.name = "session-badge";
            var time = Ui.Box("bar-group time-controls", tray);
            _pause = Ui.Button("PAUSE", TogglePause, "time-btn", time, "time-pause");
            for (int i = 0; i < Speeds.Length; i++)
            {
                float multiplier = Speeds[i].multiplier;
                _speedButtons[i] = Ui.Button(Speeds[i].label, () => SetSpeed(multiplier), "time-btn", time, "speed-" + Speeds[i].label);
            }
            var clock = Ui.Box("tray-clock", tray);
            _time = Ui.Label("tray-time", clock);
            _time.name = "tray-time";
            _date = Ui.Label("tray-date muted", clock);
        }

        public override void Refresh()
        {
            _home.EnableInClassList("active", Context.App == TerminalApp.Desktop);
            for (int i = 0; i < Pinned.Length; i++)
                _appButtons[i].EnableInClassList("active", Context.App == Pinned[i].app);
            int unread = Context.Game.Inbox.UnreadCount;
            Ui.SetText(_mail, unread > 0 ? $"MAIL ({unread})" : "MAIL");
            _mail.EnableInClassList("attention", unread > 0);

            MarketIndex index = Context.Market.Index;
            Ui.SetText(_index, $"{index.Ticker} {Fmt.Price(index.Level)}  {Fmt.Percent(index.ChangePercent)}");
            Ui.SetSign(_index, index.Change);
            RefreshSession(Context.Market.Session);

            DateTime now = Context.Clock.Now;
            Ui.SetText(_time, Fmt.Clock(now));
            Ui.SetText(_date, $"DAY {Context.Game.Days.DayNumber} · {Fmt.Date(now)}");

            Ui.SetText(_pause, Context.Game.IsPaused ? "RESUME" : "PAUSE");
            _pause.EnableInClassList("active", Context.Game.IsPaused);
            for (int i = 0; i < Speeds.Length; i++)
                _speedButtons[i].EnableInClassList("active", Context.Game.SpeedMultiplier == Speeds[i].multiplier);
        }

        /// <summary>Session name; the regular session gets named phases (midday is slower, the last hour busier).</summary>
        public static string SessionName(MarketSession session, TimeSpan t) => session switch
        {
            MarketSession.Premarket => "PRE-MARKET",
            MarketSession.Regular when t >= new TimeSpan(15, 0, 0) => "POWER HOUR",
            MarketSession.Regular when t >= new TimeSpan(11, 30, 0) && t < new TimeSpan(13, 30, 0) => "MIDDAY",
            MarketSession.Regular => "MARKET OPEN",
            MarketSession.AfterHours => "AFTER HOURS",
            _ => "CLOSED",
        };

        private void RefreshSession(MarketSession session)
        {
            Ui.SetText(_session, SessionName(session, Context.Market.Now.TimeOfDay));
            _session.EnableInClassList("session-open", session == MarketSession.Regular);
            _session.EnableInClassList("session-extended", session == MarketSession.Premarket || session == MarketSession.AfterHours);
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
