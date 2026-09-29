using System;
using System.Collections.Generic;
using OpeningBell.Core;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Applications on the player's computer (spec §6). The computer boots to the desktop; banking and shopping
    /// are websites inside the browser.
    /// </summary>
    public enum TerminalApp
    {
        Desktop,
        Broker,
        News,
        Mail,
        Browser,
    }

    /// <summary>
    /// State shared by every terminal panel (services + selected symbol). Panels talk only through this, so
    /// they can later be split across several in-world monitors without knowing about each other.
    /// </summary>
    public sealed class TerminalContext
    {
        public GameBootstrap Game { get; }
        public GameClock Clock => Game.Clock;
        public MarketSimulation Market => Game.Market;
        public EconomySystem Economy => Game.Economy;

        // ------------------------------------------------ accounts (PROP_SPEC §3)

        private readonly List<TradingAccount> _accounts = new List<TradingAccount>();
        private readonly HashSet<string> _followers = new HashSet<string>();
        private readonly List<CopyTarget> _copyTargets = new List<CopyTarget>();
        private readonly TradeCopier _copier = new TradeCopier();
        private bool _accountsDirty = true;

        /// <summary>The account the trading app shows and trades: personal brokerage or a prop firm account.</summary>
        public TradingAccount Active { get; private set; }
        public Account Account => Active.Account;
        public OrderManager Orders => Active.Orders;
        public event Action ActiveChanged;
        /// <summary>The account list or a follower changed.</summary>
        public event Action AccountsChanged;

        /// <summary>Personal first, then every prop account still worth showing.</summary>
        public IReadOnlyList<TradingAccount> Accounts
        {
            get
            {
                if (_accountsDirty) SyncAccounts();
                return _accounts;
            }
        }

        public void SetActive(TradingAccount account)
        {
            if (account == null || account == Active) return;
            _choseAccount = true;
            Active = account;
            _followers.Remove(account.Id);
            ActiveChanged?.Invoke();
            AccountsChanged?.Invoke();
        }

        public bool IsFollower(TradingAccount account) => _followers.Contains(account.Id);

        /// <summary>Checked accounts copy every order placed on the active one.</summary>
        public void SetFollower(TradingAccount account, bool follow)
        {
            if (account == Active || (follow && !account.Tradeable)) follow = false;
            if (follow ? _followers.Add(account.Id) : _followers.Remove(account.Id)) AccountsChanged?.Invoke();
        }

        public int FollowerCount
        {
            get
            {
                int n = 0;
                foreach (TradingAccount a in Accounts)
                    if (a != Active && a.Tradeable && _followers.Contains(a.Id)) n++;
                return n;
            }
        }

        /// <summary>What happened to the copies of the last order ("Copied to 2 accounts.").</summary>
        public string CopyNote => _copier.LastNote;

        private IReadOnlyList<CopyTarget> Followers()
        {
            _copyTargets.Clear();
            foreach (TradingAccount a in Accounts)
                if (a != Active && a.Tradeable && _followers.Contains(a.Id)) _copyTargets.Add(new CopyTarget(a.Id, a.Orders));
            return _copyTargets;
        }

        /// <summary>Places an order on the active account and copies it to the followers.</summary>
        public Order Place(Func<OrderManager, Order> place) => _copier.Place(Orders, Followers(), place);

        public List<Order> PlaceMany(Func<OrderManager, List<Order>> place) => _copier.PlaceMany(Orders, Followers(), place);

        public bool Cancel(long orderId) => _copier.Cancel(Orders, orderId);

        public string Modify(long orderId, decimal price) => _copier.Modify(Orders, orderId, price);

        private void SyncAccounts()
        {
            _accountsDirty = false;
            TradingAccount previous = Active;
            _accounts.Clear();
            _accounts.Add(_personal);
            foreach (PropAccount p in Game.Prop.Accounts)
            {
                // Closed accounts and evaluations that became funded accounts drop off the list.
                if (p.Status == PropStatus.Closed || p.ActivatedAs.Length > 0) continue;
                if (!_propAccounts.TryGetValue(p, out TradingAccount t)) _propAccounts[p] = t = new TradingAccount(p);
                _accounts.Add(t);
            }
            if (Active == null || !_accounts.Contains(Active)) Active = _personal;
            // On an empty personal account you didn't pick yourself, show the newest prop account you can trade.
            if (Active == _personal && !_choseAccount && _personal.Account.Equity <= 0m)
                for (int i = _accounts.Count - 1; i > 0; i--)
                    if (_accounts[i].Tradeable)
                    {
                        Active = _accounts[i];
                        break;
                    }
            if (Active != previous) ActiveChanged?.Invoke();
            AccountsChanged?.Invoke();
        }

        private readonly TradingAccount _personal;
        private bool _choseAccount; // the player picked an account themselves: stop choosing for them
        private readonly Dictionary<PropAccount, TradingAccount> _propAccounts = new Dictionary<PropAccount, TradingAccount>();

        /// <summary>Buys a used car from a listing and has it delivered (the city decides where). Error message, or null.</summary>
        public Func<Vehicles.UsedListing, string> BuyUsedCar { get; set; }

        public TerminalApp App { get; private set; } = TerminalApp.Desktop;
        public event Action AppChanged;

        public void ShowApp(TerminalApp app)
        {
            if (app == App) return;
            App = app;
            AppChanged?.Invoke();
        }

        // Cross-app links (a headline on the desktop, a ticker in a search result). The target app listens.
        public event Action<string> UrlRequested;
        public event Action<NewsItem> StoryRequested;

        public void OpenUrl(string url)
        {
            ShowApp(TerminalApp.Browser);
            UrlRequested?.Invoke(url);
        }

        public void ReadStory(NewsItem item)
        {
            ShowApp(TerminalApp.News);
            StoryRequested?.Invoke(item);
        }

        /// <summary>Opens the trading terminal on <paramref name="ticker"/>.</summary>
        public void Trade(string ticker)
        {
            Select(ticker);
            ShowApp(TerminalApp.Broker);
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
            _personal = new TradingAccount(game.Account, game.Orders);
            Active = _personal;
            game.Prop.Changed += () => _accountsDirty = true;
        }

        public void Select(string ticker)
        {
            if (ticker == SelectedTicker || !Market.TryGetSecurity(ticker, out _)) return;
            SelectedTicker = ticker;
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>One account the terminal can trade: the personal brokerage or a prop firm account.</summary>
    public sealed class TradingAccount
    {
        private readonly Account _account;
        private readonly OrderManager _orders;

        /// <summary>Null for the personal brokerage.</summary>
        public PropAccount Prop { get; }

        public TradingAccount(Account account, OrderManager orders)
        {
            _account = account;
            _orders = orders;
        }

        public TradingAccount(PropAccount prop) => Prop = prop;

        // A prop account's Account is replaced when it's reset, so always go through it.
        public Account Account => Prop?.Account ?? _account;
        public OrderManager Orders => Prop?.Orders ?? _orders;
        public bool Tradeable => Prop == null || Prop.IsTradeable;
        public string Id => Prop?.Id ?? "personal";
        public string Firm => Prop?.Firm.Name ?? _orders.Rules.Name;

        /// <summary>"50K Test", "150K Express Funded", "Personal".</summary>
        public string Kind => Prop == null ? "Personal" : $"{Prop.Plan.Label} {Prop.PhaseName}";

        /// <summary>ACTIVE / PASSED / FAILED for prop accounts; empty for personal.</summary>
        public string Status => Prop == null ? "" : Prop.Status.ToString().ToUpperInvariant();
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
