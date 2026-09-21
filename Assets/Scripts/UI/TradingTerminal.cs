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

        private readonly List<TerminalPanel> _panels = new List<TerminalPanel>();
        private float _sinceRefresh;

        public VisualElement Root { get; private set; }
        public TerminalContext Context { get; private set; }

        private void Start()
        {
            Root = GetComponent<UIDocument>().rootVisualElement;
            Root.styleSheets.Add(styleSheet);
            Root.AddToClassList("terminal");
            Context = new TerminalContext(game);

            var accountBar = new AccountBarPanel(Context);
            var watchlist = new WatchlistPanel(Context);
            var quote = new QuotePanel(Context);
            var chart = new ChartPanel(Context);
            var orderEntry = new OrderEntryPanel(Context);
            var activity = new ActivityPanel(Context, orderEntry.Track);

            Root.Add(accountBar.Root);
            var body = Ui.Box("terminal-body", Root);
            body.Add(watchlist.Root);
            var center = Ui.Box("center-column", body);
            center.Add(quote.Root);
            center.Add(chart.Root);
            center.Add(activity.Root);
            body.Add(orderEntry.Root);

            _panels.AddRange(new TerminalPanel[] { accountBar, watchlist, quote, chart, orderEntry, activity });
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
