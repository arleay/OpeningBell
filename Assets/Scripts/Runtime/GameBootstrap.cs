using System;
using System.Globalization;
using System.Linq;
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

        [Header("Save")]
        [SerializeField] private string saveSlot = "slot1";
        [Tooltip("Continue from the save slot if one exists (the -newgame command-line flag overrides this).")]
        [SerializeField] private bool continueFromSave = true;
        [Tooltip("Body whose position is saved/restored (needs to stay in the Runtime layer, so just a Transform).")]
        [SerializeField] private Transform player;

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

            SaveGame save = TryLoadSave();
            ulong worldSeed = unchecked((ulong)(save?.Seed ?? seed));
            DateTime start = save != null
                ? new DateTime(save.Clock)
                : DateTime.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMinutes(startMinuteOfDay);

            // Loading = build from the same definitions, then overwrite the runtime state.
            Market = new MarketSimulation(marketSettings.Config, catalog.CreateSpecs(), catalog.Index, new SeededRandomService(worldSeed), start,
                newsLibrary != null ? newsLibrary.Templates : null,
                scenario != null ? scenario.ScheduledNews : null);
            if (save != null) Market.RestoreState(save.Market);

            Clock = new GameClock(start, tradingTimeScale);
            Account = new Account(Market);
            Orders = new OrderManager(Market, Account, brokerRules);
            // New game: deposit before the day recorder opens day 1, so savings aren't counted as profit.
            if (save == null) Account.Deposit((decimal)startingCash, "Starting savings");
            Days = new TradingDayRecorder(Market, Account, Orders);
            Seed = worldSeed;

            if (save != null)
            {
                TradingState.Restore(save.Trading, Account, Orders, Days);
                if (save.HasPlayer) PlacePlayer(save.Player);
                Debug.Log($"Loaded save '{saveSlot}' (day {Days.DayNumber}, {Clock.Now:ddd MMM d HH:mm}).");
            }
        }

        public ulong Seed { get; private set; }

        /// <summary>Writes the save slot. Called on waking (end of day) and on quit.</summary>
        public void Save()
        {
            var save = new SaveGame
            {
                Seed = unchecked((long)Seed),
                Clock = Clock.Now.Ticks,
                Market = Market.CaptureState(),
                Trading = TradingState.Capture(Account, Orders, Days),
            };
            if (player != null)
            {
                Vector3 p = player.position;
                save.HasPlayer = true;
                save.Player = new PlayerSaveData { X = p.x, Y = p.y, Z = p.z, Yaw = player.eulerAngles.y };
            }
            SaveSystem.Write(save, saveSlot);
        }

        private SaveGame TryLoadSave()
        {
            if (!continueFromSave || Environment.GetCommandLineArgs().Contains("-newgame")) return null;
            if (SaveSystem.TryRead(saveSlot, out SaveGame save, out string error))
            {
                if (error != null) Debug.LogWarning(error);
                return save;
            }
            if (error != null) Debug.LogError($"Could not load save '{saveSlot}': {error}. Starting a new game.");
            return null;
        }

        private void PlacePlayer(PlayerSaveData p)
        {
            if (player == null) return;
            // A CharacterController overrides direct moves while enabled.
            var body = player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            player.SetPositionAndRotation(new Vector3(p.X, p.Y, p.Z), Quaternion.Euler(0f, p.Yaw, 0f));
            if (body != null) body.enabled = true;
        }

        private void OnApplicationQuit()
        {
            if (enabled && Market != null) Save();
        }

        [ContextMenu("Delete Save (start fresh next run)")]
        private void DeleteSave() => SaveSystem.Delete(saveSlot);

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
