using System;
using System.Globalization;
using OpeningBell.Core;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>
    /// Composition root: builds the pure simulation objects and drives them from Unity's frame loop.
    /// Holds no game logic of its own.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private SecurityCatalog catalog;
        [SerializeField] private MarketSettings marketSettings;
        [Tooltip("Optional. Without it the market runs with no news.")]
        [SerializeField] private NewsLibrary newsLibrary;
        [Tooltip("Optional scripted headlines, e.g. the onboarding day.")]
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private BrokerRules brokerRules = new BrokerRules();
        [SerializeField] private long seed = 18492;

        [Tooltip("yyyy-MM-dd. Should be a weekday.")]
        [SerializeField] private string startDate = "2030-01-07";
        [SerializeField] private int startMinuteOfDay = 6 * 60;
        [SerializeField] private double startingCash = 10000;

        [Tooltip("In-game seconds per real second while seated at the workstation during market sessions.")]
        [SerializeField] private float tradingTimeScale = 10f;
        [Tooltip("In-game seconds per real second while away from the desk during market sessions.")]
        [SerializeField] private float walkingTimeScale = 30f;
        [Tooltip("In-game seconds per real second while the market is closed.")]
        [SerializeField] private float closedTimeScale = 120f;
        [Tooltip("Caps one frame's real delta so a hitch cannot skip a large chunk of market time.")]
        [SerializeField] private float maxFrameSeconds = 0.25f;

        public GameClock Clock { get; private set; }
        public MarketSimulation Market { get; private set; }
        public Account Account { get; private set; }
        public OrderManager Orders { get; private set; }
        public TradingDayRecorder Days { get; private set; }

        /// <summary>Player-controlled fast-forward on top of the base time scales (learning aid; difficulty may lock it later).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsPaused
        {
            get => Clock.IsPaused;
            set => Clock.IsPaused = value;
        }

        /// <summary>Seated trading slows time; walking around lets it pass faster (spec §7).</summary>
        public bool IsAtWorkstation { get; set; }

        private void Awake()
        {
            if (catalog == null || marketSettings == null)
            {
                Debug.LogError("GameBootstrap: assign the Security Catalog and Market Settings assets.", this);
                enabled = false;
                return;
            }

            DateTime start = DateTime.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMinutes(startMinuteOfDay);
            var random = new SeededRandomService(unchecked((ulong)seed));

            Market = new MarketSimulation(marketSettings.Config, catalog.CreateSpecs(), catalog.Index, random, start,
                newsLibrary != null ? newsLibrary.Templates : null,
                scenario != null ? scenario.ScheduledNews : null);
            Clock = new GameClock(Market.Now, tradingTimeScale);
            Account = new Account(Market);
            Account.Deposit((decimal)startingCash, "Starting savings");
            Orders = new OrderManager(Market, Account, brokerRules);
            Days = new TradingDayRecorder(Market, Account, Orders);
        }

        /// <summary>
        /// Jumps game time forward (sleep, debug) and simulates the market through the gap immediately,
        /// so orders, news and day reports are all settled before anything reads them.
        /// </summary>
        public void SkipTo(DateTime target)
        {
            Clock.JumpTo(target);
            Market.AdvanceTo(Clock.Now);
        }

        private void Update()
        {
            float baseScale = Market.Session == MarketSession.Closed ? closedTimeScale
                : IsAtWorkstation ? tradingTimeScale : walkingTimeScale;
            Clock.TimeScale = baseScale * SpeedMultiplier;
            Clock.Advance(Mathf.Min(Time.unscaledDeltaTime, maxFrameSeconds));
            Market.AdvanceTo(Clock.Now);
        }
    }
}
