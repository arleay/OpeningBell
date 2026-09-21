using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Single-screen trading terminal: composes the panels into one UIDocument and refreshes them at a fixed
    /// UI rate. Panels only share a TerminalContext, so later monitors can each host a subset of them.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TradingTerminal : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private StyleSheet styleSheet;
        [Tooltip("Real seconds between UI refreshes. The market ticks independently.")]
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;
        [Tooltip("Resolution of the in-world monitor texture.")]
        [SerializeField] private Vector2Int worldResolution = new Vector2Int(1920, 1080);

        private readonly List<TerminalPanel> _panels = new List<TerminalPanel>();
        private UIDocument _document;
        private float _sinceRefresh;

        public VisualElement Root { get; private set; }
        public TerminalContext Context { get; private set; }

        /// <summary>What the in-world monitor displays while the terminal is not on screen.</summary>
        public RenderTexture WorldTexture { get; private set; }

        /// <summary>True: full-screen and interactive. False: rendered only into WorldTexture.</summary>
        public bool IsOnScreen => _document.panelSettings.targetTexture == null;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            // Runtime copy: switching targetTexture must never modify the shared PanelSettings asset.
            _document.panelSettings = Instantiate(_document.panelSettings);
            WorldTexture = new RenderTexture(worldResolution.x, worldResolution.y, 0, RenderTextureFormat.ARGB32) { name = "TerminalScreen" };
        }

        private void OnDestroy()
        {
            if (WorldTexture != null) WorldTexture.Release();
        }

        public void ShowOnScreen(bool onScreen)
        {
            _document.panelSettings.targetTexture = onScreen ? null : WorldTexture;
            if (!onScreen) Root?.focusController?.focusedElement?.Blur();
        }

        private void Start()
        {
            Root = _document.rootVisualElement;
            Root.styleSheets.Add(styleSheet);
            Root.AddToClassList("terminal");
            Context = new TerminalContext(game);

            var accountBar = new AccountBarPanel(Context);
            var watchlist = new WatchlistPanel(Context);
            var quote = new QuotePanel(Context);
            var chart = new ChartPanel(Context);
            var orderEntry = new OrderEntryPanel(Context);
            var activity = new ActivityPanel(Context, orderEntry.Track);
            var news = new NewsPanel(Context);

            Root.Add(accountBar.Root);
            var body = Ui.Box("terminal-body", Root);
            var left = Ui.Box("left-column", body);
            left.Add(watchlist.Root);
            left.Add(news.Root);
            var center = Ui.Box("center-column", body);
            center.Add(quote.Root);
            center.Add(chart.Root);
            center.Add(activity.Root);
            body.Add(orderEntry.Root);

            _panels.AddRange(new TerminalPanel[] { accountBar, watchlist, news, quote, chart, orderEntry, activity });
            RefreshAll();
        }

        private void Update()
        {
            _sinceRefresh += Time.unscaledDeltaTime;
            if (_sinceRefresh < refreshInterval) return;
            _sinceRefresh = 0f;
            RefreshAll();
        }

        public void RefreshAll()
        {
            foreach (TerminalPanel panel in _panels) panel.Refresh();
        }
    }
}
