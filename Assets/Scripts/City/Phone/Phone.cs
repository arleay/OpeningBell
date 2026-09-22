using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// The player's phone, drawn in the HUD's bottom-right corner: Tab takes it out (the mouse is freed, the world
    /// keeps running), Tab or Esc puts it away. A home screen with a clock and market widget opens Messages, Phone,
    /// Maps, PennyBridge (the broker, on mobile) and News. Headlines and order fills pop up as notifications even with
    /// the phone away; Tab while one is showing opens it.
    /// </summary>
    public sealed class Phone : MonoBehaviour
    {
        internal const float Width = 300f, Height = 620f, Bezel = 11f;
        internal const float ScreenWidth = Width - Bezel * 2f, ScreenHeight = Height - Bezel * 2f;
        private const float Margin = 24f, BannerWidth = 340f, BannerSeconds = 6f;
        private const int MaxBanners = 3;

        private sealed class Banner
        {
            public VisualElement Root;
            public float Until;
            public PhoneAppId App;
            public Action Open;
        }

        internal GameBootstrap Game { get; private set; }
        internal FirstPersonController Player { get; private set; }
        internal InteractionHud Hud { get; private set; }
        internal Minimap Map { get; private set; }
        internal CityContext City { get; private set; }

        private PlayerInteractor _interactor;
        private GameInput _input;
        private PauseMenu _pause;
        private Texture2D _wallpaper;

        private VisualElement _device, _screen, _content, _banners;
        private Label _statusTime;
        private readonly Dictionary<PhoneAppId, PhoneScreen> _apps = new Dictionary<PhoneAppId, PhoneScreen>();
        private readonly Dictionary<PhoneAppId, Label> _badges = new Dictionary<PhoneAppId, Label>();
        private readonly List<Banner> _live = new List<Banner>();
        private PhoneScreen _current;
        private float _slide, _sinceRefresh;

        public bool IsOpen { get; private set; }
        public PhoneAppId CurrentApp => _current?.Id ?? PhoneAppId.Home;
        /// <summary>Headlines of the notifications on screen now (for tests).</summary>
        public IEnumerable<string> BannerTexts
        {
            get { foreach (Banner b in _live) yield return ((Label)b.Root.Q("banner-text")).text; }
        }

        internal PhoneScreen App(PhoneAppId id) => _apps.TryGetValue(id, out PhoneScreen s) ? s : null;

        public void Configure(GameBootstrap game, FirstPersonController player, InteractionHud hud, Minimap map, CityContext city)
        {
            Game = game;
            Player = player;
            Hud = hud;
            Map = map;
            City = city;
            _interactor = FindAnyObjectByType<PlayerInteractor>();
            _input = FindAnyObjectByType<GameInput>();
            _pause = FindAnyObjectByType<PauseMenu>();

            game.Market.NewsPublished += OnNews;
            game.Orders.OrderFilled += OnFill;
        }

        private void OnDestroy()
        {
            if (Game != null)
            {
                Game.Market.NewsPublished -= OnNews;
                Game.Orders.OrderFilled -= OnFill;
            }
            if (_wallpaper != null) Destroy(_wallpaper);
        }

        // ------------------------------------------------------------------ open / close

        private bool CanOpen => _device != null && Hud.Root.resolvedStyle.display != DisplayStyle.None &&
                                Player.ControlEnabled && !Player.Suspended && (_pause == null || !_pause.IsOpen);

        /// <summary>Takes the phone out, on the home screen (or <paramref name="app"/>).</summary>
        public void Open(PhoneAppId app = PhoneAppId.Home)
        {
            if (IsOpen || !CanOpen) return;
            IsOpen = true;
            Player.ControlEnabled = false; // frees the cursor
            Player.Browsing = true;
            if (_interactor != null) _interactor.enabled = false;
            _input?.UseMenuControls();
            Show(app);
            _device.style.display = DisplayStyle.Flex;
            PlaceBanners();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _screen.focusController?.focusedElement?.Blur();
            _input?.UsePlayerControls();
            if (_interactor != null) _interactor.enabled = true;
            Player.Browsing = false;
            Player.ControlEnabled = true;
            PlaceBanners();
        }

        /// <summary>Switches to an app (Home = the home screen).</summary>
        public void Show(PhoneAppId app)
        {
            foreach (PhoneScreen s in _apps.Values) s.Root.style.display = DisplayStyle.None;
            _current = _apps[app];
            _current.Root.style.display = DisplayStyle.Flex;
            _current.Opened();
            _current.Refresh();
            RefreshBadges();
        }

        private void Update()
        {
            if (Game == null) return;
            if (_device == null)
            {
                if (Hud.Root == null) return;
                Build(Hud.Root);
            }

            if (_input != null)
            {
                if (_input.Phone.WasPressedThisFrame())
                {
                    if (IsOpen) Close();
                    else if (_live.Count > 0) OpenBanner(_live[_live.Count - 1]);
                    else Open();
                }
                else if (IsOpen && _input.CloseMenu.WasPressedThisFrame())
                {
                    Close();
                }
            }
            // Anything else taking control (sleep, the title, a cutscene) puts the phone away.
            if (IsOpen && (Player.Suspended || (_pause != null && _pause.IsOpen))) Close();

            // Slide up from below the screen edge.
            _slide = Mathf.MoveTowards(_slide, IsOpen ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            float ease = 1f - (1f - _slide) * (1f - _slide);
            _device.style.translate = new Translate(0f, (1f - ease) * (Height + Margin + 20f));
            _device.style.display = _slide > 0f ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateBanners();

            _sinceRefresh += Time.unscaledDeltaTime;
            if (_sinceRefresh >= 0.25f)
            {
                _sinceRefresh = 0f;
                _statusTime.text = Game.Clock.Now.ToString("h:mm", CultureInfo.InvariantCulture);
                if (_slide > 0f) _current?.Refresh();
                RefreshBadges();
            }
        }

        // ------------------------------------------------------------------ device

        private void Build(VisualElement root)
        {
            _wallpaper = PhoneKit.Wallpaper();

            // The body: titanium edge, black bezel, side buttons outside the frame.
            _device = PhoneKit.Box(root, "phone");
            PhoneKit.Absolute(_device, right: Margin, bottom: Margin);
            _device.style.width = Width;
            _device.style.height = Height;
            _device.style.display = DisplayStyle.None;
            SideButton(-4f, 118f, 36f); // action button
            SideButton(-4f, 172f, 58f); // volume up
            SideButton(-4f, 240f, 58f); // volume down
            SideButton(Width, 196f, 86f); // side button
            SideButton(Width, 330f, 48f); // camera control

            var frame = PhoneKit.Box(_device);
            PhoneKit.Absolute(frame, 0f, 0f);
            frame.style.width = Width;
            frame.style.height = Height;
            frame.style.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
            PhoneKit.Radius(frame, 52f);
            PhoneKit.Border(frame, 3f, new Color(0.62f, 0.62f, 0.65f));

            _screen = PhoneKit.Box(frame, "phone-screen");
            PhoneKit.Absolute(_screen, Bezel - 3f, Bezel - 3f);
            _screen.style.width = ScreenWidth;
            _screen.style.height = ScreenHeight;
            _screen.style.overflow = Overflow.Hidden;
            _screen.style.backgroundColor = PhoneKit.Screen;
            PhoneKit.Radius(_screen, 42f);

            _content = PhoneKit.Box(_screen);
            PhoneKit.Absolute(_content, 0f, 0f);
            _content.style.width = ScreenWidth;
            _content.style.height = ScreenHeight;

            var home = new HomeScreen(this, _wallpaper);
            Add(home);
            Add(new MessagesApp(this));
            Add(new CallsApp(this));
            Add(new MapsApp(this));
            Add(new PennyBridgeApp(this));
            Add(new NewsApp(this));

            BuildStatusBar();

            // The home indicator: a pill at the bottom, and the way home.
            var indicator = PhoneKit.Box(_screen, "home-indicator");
            PhoneKit.Absolute(indicator, (ScreenWidth - 150f) / 2f, ScreenHeight - 22f);
            indicator.style.width = 150f;
            indicator.style.height = 20f;
            indicator.style.justifyContent = Justify.Center;
            indicator.style.alignItems = Align.Center;
            var bar = PhoneKit.Box(indicator);
            bar.style.width = 112f;
            bar.style.height = 5f;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            PhoneKit.Radius(bar, 3f);
            bar.pickingMode = PickingMode.Ignore;
            PhoneKit.Tap(indicator, () => Show(PhoneAppId.Home));

            _banners = PhoneKit.Box(root, "phone-banners");
            _banners.pickingMode = PickingMode.Ignore;
            _banners.style.width = BannerWidth;
            _banners.style.flexDirection = FlexDirection.ColumnReverse;
            PlaceBanners();

            _current = home;
            Show(PhoneAppId.Home);
        }

        private void Add(PhoneScreen screen)
        {
            _apps[screen.Id] = screen;
            screen.Root.style.display = DisplayStyle.None;
            _content.Add(screen.Root);
        }

        private void SideButton(float x, float y, float length)
        {
            var b = PhoneKit.Box(_device);
            PhoneKit.Absolute(b, x, y);
            b.style.width = 4f;
            b.style.height = length;
            b.style.backgroundColor = new Color(0.55f, 0.55f, 0.58f);
            PhoneKit.Radius(b, 2f);
        }

        private void BuildStatusBar()
        {
            var bar = PhoneKit.Box(_screen, "status-bar");
            bar.pickingMode = PickingMode.Ignore;
            PhoneKit.Absolute(bar, 0f, 0f);
            bar.style.width = ScreenWidth;
            bar.style.height = 46f;

            _statusTime = PhoneKit.Label(bar, "", 15f, Color.white, true);
            PhoneKit.Absolute(_statusTime, 34f, 15f);

            // The Dynamic Island.
            var island = PhoneKit.Box(bar);
            PhoneKit.Absolute(island, (ScreenWidth - 96f) / 2f, 10f);
            island.style.width = 96f;
            island.style.height = 28f;
            island.style.backgroundColor = Color.black;
            PhoneKit.Radius(island, 14f);

            // Signal, Wi-Fi and battery, painted.
            var icons = PhoneKit.Box(bar);
            PhoneKit.Absolute(icons, ScreenWidth - 92f, 17f);
            icons.style.width = 66f;
            icons.style.height = 14f;
            icons.generateVisualContent += ctx =>
            {
                Painter2D p = ctx.painter2D;
                p.fillColor = Color.white;
                for (int i = 0; i < 4; i++) // signal bars
                {
                    float h = 4f + i * 2.6f, x = i * 4.5f;
                    Rect(p, x, 12f - h, 3f, h);
                }
                p.strokeColor = Color.white; // wifi arcs
                p.lineWidth = 2f;
                p.lineCap = LineCap.Round;
                for (int i = 0; i < 3; i++)
                {
                    p.BeginPath();
                    p.Arc(new Vector2(29f, 13f), 3f + i * 3.6f, Angle.Degrees(225f), Angle.Degrees(315f));
                    p.Stroke();
                }
                p.lineWidth = 1.2f; // battery
                p.strokeColor = new Color(1f, 1f, 1f, 0.5f);
                p.BeginPath();
                p.MoveTo(new Vector2(41f, 1.5f));
                p.LineTo(new Vector2(62f, 1.5f));
                p.LineTo(new Vector2(62f, 11.5f));
                p.LineTo(new Vector2(41f, 11.5f));
                p.ClosePath();
                p.Stroke();
                p.fillColor = Color.white;
                Rect(p, 42.5f, 3f, 15f, 7f);
                p.fillColor = new Color(1f, 1f, 1f, 0.5f);
                Rect(p, 63f, 4.5f, 1.8f, 4f);
            };
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        internal void RegisterBadge(PhoneAppId app, VisualElement icon)
        {
            var badge = PhoneKit.Label(icon.parent, "", 12f, Color.white, true);
            PhoneKit.Absolute(badge, right: -4f, top: -5f);
            badge.style.minWidth = 20f;
            badge.style.height = 20f;
            badge.style.backgroundColor = new Color(1f, 0.23f, 0.19f);
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Radius(badge, 10f);
            PhoneKit.Pad(badge, 5f, 0f);
            badge.pickingMode = PickingMode.Ignore;
            badge.style.display = DisplayStyle.None;
            _badges[app] = badge;
        }

        private void RefreshBadges()
        {
            foreach (var pair in _badges)
            {
                int n = _apps[pair.Key].Unread;
                pair.Value.text = n > 99 ? "99+" : n.ToString(CultureInfo.InvariantCulture);
                pair.Value.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------ notifications

        private void OnNews(NewsItem item)
        {
            string tag = item.Scope == NewsScope.Market ? "MARKETS" : string.Join(" ", item.Tickers);
            Notify(PhoneAppId.News, tag, item.Headline, () => ((NewsApp)_apps[PhoneAppId.News]).Read(item));
        }

        private void OnFill(Fill fill)
        {
            Notify(PhoneAppId.Messages, "PennyBridge", MessagesApp.FillText(fill), () => ((MessagesApp)_apps[PhoneAppId.Messages]).OpenThread(MessagesApp.BrokerThread));
        }

        /// <summary>A banner in the corner (above the phone when it's out). Tapping it, or Tab, opens it.</summary>
        public void Notify(PhoneAppId app, string title, string text, Action open = null)
        {
            if (_banners == null) return;
            var root = PhoneKit.Box(_banners);
            root.name = "banner";
            root.style.flexDirection = FlexDirection.Row;
            root.style.backgroundColor = new Color(0.13f, 0.13f, 0.15f, 0.94f);
            PhoneKit.Border(root, 1f, new Color(1f, 1f, 1f, 0.1f));
            PhoneKit.Radius(root, 20f);
            PhoneKit.Pad(root, 12f, 11f);
            root.style.marginTop = 8f;
            root.style.translate = new Translate(BannerWidth + Margin + 10f, 0f);

            var icon = PhoneKit.Icon(app, 38f);
            icon.style.flexShrink = 0;
            icon.style.marginRight = 10f;
            root.Add(icon);
            var col = PhoneKit.Box(root);
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            var head = PhoneKit.Row(col);
            PhoneKit.Label(head, PhoneKit.Title(app).ToUpperInvariant() + (string.IsNullOrEmpty(title) ? "" : "  ·  " + title), 11f, PhoneKit.Muted, true);
            PhoneKit.Label(head, "now", 11f, PhoneKit.Muted);
            var body = PhoneKit.Label(col, text, 14f, PhoneKit.Text, true);
            body.name = "banner-text";
            body.style.marginTop = 2f;
            body.style.maxHeight = 40f;
            body.style.overflow = Overflow.Hidden;
            var hint = PhoneKit.Label(col, "Tab to open", 11f, new Color(1f, 1f, 1f, 0.35f));
            hint.style.marginTop = 3f;

            var banner = new Banner { Root = root, Until = Time.unscaledTime + BannerSeconds, App = app, Open = open };
            PhoneKit.Tap(root, () => OpenBanner(banner));
            _live.Add(banner);
            while (_live.Count > MaxBanners) Dismiss(_live[0]);
            RefreshBadges();
        }

        private void OpenBanner(Banner banner)
        {
            Dismiss(banner);
            if (!IsOpen) Open(banner.App);
            else Show(banner.App);
            if (IsOpen) banner.Open?.Invoke();
        }

        private void Dismiss(Banner banner)
        {
            _live.Remove(banner);
            banner.Root.RemoveFromHierarchy();
        }

        private void UpdateBanners()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Banner b = _live[i];
                float left = b.Until - Time.unscaledTime;
                if (left <= 0f) { Dismiss(b); continue; }
                // In from the right edge, out the same way.
                float t = Mathf.Min(BannerSeconds - left, left);
                float k = Mathf.Clamp01(t / 0.3f);
                b.Root.style.translate = new Translate((1f - k * (2f - k)) * (BannerWidth + Margin + 10f), 0f);
            }
            // Only visible where the HUD is (hidden at the desk, where the terminal has its own news).
            _banners.style.display = Hud.Root.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Banners sit in the corner, or beside the phone when it's out (above it they'd run into the minimap and the
        /// clock in the top right).
        /// </summary>
        private void PlaceBanners()
        {
            if (_banners == null) return;
            PhoneKit.Absolute(_banners, right: IsOpen ? Margin + Width + 16f : Margin, bottom: Margin);
        }
    }

    /// <summary>One app (or the home screen): builds its own UI into Root and refreshes while visible.</summary>
    internal abstract class PhoneScreen
    {
        protected readonly Phone Phone;
        public readonly VisualElement Root;
        public abstract PhoneAppId Id { get; }

        protected PhoneScreen(Phone phone)
        {
            Phone = phone;
            Root = PhoneKit.Box();
            Root.name = "app-" + GetType().Name;
            PhoneKit.Absolute(Root, 0f, 0f);
            Root.style.width = Phone.ScreenWidth;
            Root.style.height = Phone.ScreenHeight;
            Root.style.backgroundColor = PhoneKit.Screen;
        }

        public virtual void Opened() { }
        public virtual void Refresh() { }

        private string _signature;
        private readonly List<Action> _updaters = new List<Action>();

        /// <summary>
        /// Lists are rebuilt only when what they show changes (a new position, a new order), described by
        /// <paramref name="signature"/>; otherwise the updaters registered while building refresh the numbers in
        /// place. Rebuilding every refresh would reset scrolling and swallow clicks that span a rebuild.
        /// </summary>
        protected bool Rebuild(string signature)
        {
            if (signature == _signature)
            {
                foreach (Action update in _updaters) update();
                return false;
            }
            _signature = signature;
            _updaters.Clear();
            return true;
        }

        /// <summary>Registers a value to keep fresh, and applies it now.</summary>
        protected void Live(Action update)
        {
            _updaters.Add(update);
            update();
        }

        /// <summary>Forces the next Rebuild check to rebuild.</summary>
        protected void Invalidate() => _signature = null;
        /// <summary>Badge count on the home screen icon.</summary>
        public virtual int Unread => 0;

        /// <summary>A large iOS-style title under the status bar, with an optional back link.</summary>
        protected static VisualElement Header(VisualElement parent, string title, Action back = null, string backText = "Back")
        {
            var header = PhoneKit.Box(parent);
            header.style.paddingTop = 50f;
            header.style.paddingLeft = header.style.paddingRight = 18f;
            header.style.paddingBottom = 8f;
            header.style.flexShrink = 0;
            if (back != null)
            {
                var link = PhoneKit.Label(header, "‹ " + backText, 16f, PhoneKit.Blue);
                link.name = "back";
                link.style.marginBottom = 4f;
                PhoneKit.Tap(link, back);
            }
            var t = PhoneKit.Label(header, title, 28f, PhoneKit.Text, true);
            t.name = "title";
            return header;
        }

        protected static ScrollView List(VisualElement parent)
        {
            var list = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.style.flexGrow = 1;
            list.contentContainer.style.paddingLeft = list.contentContainer.style.paddingRight = 14f;
            list.contentContainer.style.paddingBottom = 34f;
            parent.Add(list);
            return list;
        }

        /// <summary>A rounded grouped-list card.</summary>
        protected static VisualElement Card(VisualElement parent)
        {
            var card = PhoneKit.Box(parent);
            card.style.backgroundColor = PhoneKit.Card;
            PhoneKit.Radius(card, 14f);
            card.style.marginBottom = 12f;
            card.style.overflow = Overflow.Hidden;
            return card;
        }

        protected static string When(DateTime time, DateTime now) =>
            time.Date == now.Date ? time.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : (now.Date - time.Date).TotalDays < 7 ? time.ToString("ddd", CultureInfo.InvariantCulture)
            : time.ToString("M/d/yy", CultureInfo.InvariantCulture);
    }

    /// <summary>Wallpaper, a clock, a market widget, the app grid and the dock.</summary>
    internal sealed class HomeScreen : PhoneScreen
    {
        public override PhoneAppId Id => PhoneAppId.Home;

        private readonly Label _date, _time, _index, _indexChange, _pnl, _session;

        public HomeScreen(Phone phone, Texture2D wallpaper) : base(phone)
        {
            Root.style.backgroundImage = new StyleBackground(wallpaper);
            Root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            Root.style.alignItems = Align.Center;

            _date = PhoneKit.Label(Root, "", 15f, new Color(1f, 1f, 1f, 0.85f), true);
            _date.style.marginTop = 58f;
            _time = PhoneKit.Label(Root, "", 64f, Color.white, true);
            _time.style.marginTop = -4f;

            // Market widget: the index and today's P&L at a glance.
            var widget = PhoneKit.Box(Root, "market-widget");
            widget.style.width = Phone.ScreenWidth - 32f;
            widget.style.marginTop = 10f;
            widget.style.backgroundColor = new Color(0.05f, 0.05f, 0.1f, 0.42f);
            PhoneKit.Border(widget, 1f, new Color(1f, 1f, 1f, 0.16f));
            PhoneKit.Radius(widget, 22f);
            PhoneKit.Pad(widget, 16f, 12f);
            var top = PhoneKit.Row(widget);
            PhoneKit.Label(top, "PENNYBRIDGE", 11f, new Color(1f, 1f, 1f, 0.7f), true);
            _session = PhoneKit.Label(top, "", 11f, new Color(1f, 1f, 1f, 0.7f), true);
            var line = PhoneKit.Row(widget);
            line.style.marginTop = 6f;
            _index = PhoneKit.Label(line, "", 17f, Color.white, true);
            _indexChange = PhoneKit.Label(line, "", 15f, Color.white, true);
            _indexChange.style.marginLeft = 8f;
            _pnl = PhoneKit.Label(widget, "", 13f, new Color(1f, 1f, 1f, 0.85f));
            _pnl.style.marginTop = 4f;
            PhoneKit.Tap(widget, () => Phone.Show(PhoneAppId.PennyBridge));

            var grid = PhoneKit.Row(Root, Justify.FlexStart);
            grid.style.width = Phone.ScreenWidth - 24f;
            grid.style.marginTop = 22f;
            foreach (PhoneAppId app in new[] { PhoneAppId.PennyBridge, PhoneAppId.News, PhoneAppId.Maps })
                AppButton(grid, app, true);

            // The dock: a frosted shelf at the bottom.
            var dock = PhoneKit.Row(Root, Justify.SpaceAround);
            PhoneKit.Absolute(dock, 12f, bottom: 26f);
            dock.style.width = Phone.ScreenWidth - 24f;
            dock.style.height = 88f;
            dock.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
            PhoneKit.Border(dock, 1f, new Color(1f, 1f, 1f, 0.2f));
            PhoneKit.Radius(dock, 32f);
            PhoneKit.Pad(dock, 24f, 0f);
            AppButton(dock, PhoneAppId.Calls, false);
            AppButton(dock, PhoneAppId.Messages, false);
        }

        private void AppButton(VisualElement parent, PhoneAppId app, bool label)
        {
            var cell = PhoneKit.Box(parent, "app-" + app);
            cell.style.width = 69f;
            cell.style.alignItems = Align.Center;
            var holder = PhoneKit.Box(cell);
            var icon = PhoneKit.Icon(app, 58f);
            holder.Add(icon);
            Phone.RegisterBadge(app, icon);
            if (label)
            {
                var name = PhoneKit.Label(cell, PhoneKit.Title(app), 11f, Color.white);
                name.style.marginTop = 5f;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.NoWrap;
            }
            PhoneKit.Tap(cell, () => Phone.Show(app));
        }

        public override void Refresh()
        {
            DateTime now = Phone.Game.Clock.Now;
            _date.text = now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
            _time.text = now.ToString("h:mm", CultureInfo.InvariantCulture);

            MarketSimulation market = Phone.Game.Market;
            _index.text = $"{market.Index.Ticker} {OpeningBell.UI.Fmt.Price(market.Index.Level)}";
            _indexChange.text = OpeningBell.UI.Fmt.Percent(market.Index.ChangePercent);
            _indexChange.style.color = market.Index.Change >= 0m ? new Color(0.45f, 0.95f, 0.55f) : new Color(1f, 0.5f, 0.45f);
            _pnl.text = $"Today {OpeningBell.UI.Fmt.SignedMoney(Phone.Game.Account.DailyPnL)}   ·   Equity {OpeningBell.UI.Fmt.Money(Phone.Game.Account.Equity)}";
            _session.text = PennyBridgeApp.SessionText(market.Session);
        }
    }
}
