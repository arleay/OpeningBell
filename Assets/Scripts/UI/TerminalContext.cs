using System;
using OpeningBell.Core;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// State shared by every terminal panel (services + selected symbol). Panels talk only through this, so
    /// they can later be split across several in-world monitors without knowing about each other.
    /// </summary>
    /// <summary>Applications on the player's computer (spec §6). More unlock later.</summary>
    public enum TerminalApp
    {
        Broker,
        Bank,
        Store,
    }

    public sealed class TerminalContext
    {
        public GameBootstrap Game { get; }
        public GameClock Clock => Game.Clock;
        public MarketSimulation Market => Game.Market;
        public Account Account => Game.Account;
        public OrderManager Orders => Game.Orders;
        public EconomySystem Economy => Game.Economy;

        public TerminalApp App { get; private set; } = TerminalApp.Broker;
        public event Action AppChanged;

        public void ShowApp(TerminalApp app)
        {
            if (app == App) return;
            App = app;
            AppChanged?.Invoke();
        }

        public string SelectedTicker { get; private set; }

        public SecurityRuntimeState Selected
        {
            get
            {
                Market.TryGetSecurity(SelectedTicker, out var security);
                return security;
            }
        }

        public event Action SelectionChanged;

        public TerminalContext(GameBootstrap game)
        {
            Game = game;
            SelectedTicker = Market.Securities[0].Ticker;
        }

        public void Select(string ticker)
        {
            if (ticker == SelectedTicker || !Market.TryGetSecurity(ticker, out _)) return;
            SelectedTicker = ticker;
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>One self-contained terminal window. Refresh is called at the terminal's UI rate, not per frame.</summary>
    public abstract class TerminalPanel
    {
        public VisualElement Root { get; }
        protected TerminalContext Context { get; }

        protected TerminalPanel(TerminalContext context, string classes)
        {
            Context = context;
            Root = Ui.Box("panel " + classes);
        }

        public abstract void Refresh();
    }
}
