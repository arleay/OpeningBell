using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpeningBell.Core;
using OpeningBell.Economy;
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
        [Tooltip("Bills, living costs, starting bank balance and store items.")]
        [SerializeField] private EconomySettings economySettings;
        [Tooltip("Scripted emails: onboarding, reminders, notices.")]
        [SerializeField] private EmailLibrary emailLibrary;
        [Tooltip("Bikes, parts and service prices sold in the city's shops.")]
        [SerializeField] private VehicleLibrary vehicleLibrary;
        [SerializeField] private BrokerRules brokerRules = new BrokerRules();
        [SerializeField] private long seed = 18492;

        [Tooltip("yyyy-MM-dd. Should be a weekday.")]
        [SerializeField] private string startDate = "2030-01-07";
        [SerializeField] private int startMinuteOfDay = 6 * 60;
        [Tooltip("Personal brokerage cash for a new game. PROP_SPEC: you start broke and work (0).")]
        [SerializeField] private double startingCash = 0;

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
        public EconomySystem Economy { get; private set; }
        public Inbox Inbox { get; private set; }
        /// <summary>Prop firm accounts and their rules (PROP_SPEC.md).</summary>
        public PropDesk Prop { get; private set; }
        /// <summary>The counter job at Sal's Pizza (PROP_SPEC §5).</summary>
        public Job Job { get; } = new Job("Sal's Pizza", 15m);
        /// <summary>Chips and tables at The Meridian (CASINO_SPEC). Chips are bought from and cashed into the bank.</summary>
        public global::OpeningBell.Casino.CasinoFloor Casino { get; } = new global::OpeningBell.Casino.CasinoFloor();
        /// <summary>The player's hedge fund (FUND_SPEC): exists once registered; company money is separate from the bank.</summary>
        public global::OpeningBell.Fund.HedgeFund Fund { get; private set; }

        /// <summary>
        /// New-game money in place of the configured $0 (brokerage cash, bank). Scene tests set it so the many tests
        /// that trade the personal account keep the classic $10,000 / $1,800 start.
        /// </summary>
        public static (decimal Cash, decimal Bank)? StartingMoneyOverride { get; set; }

        /// <summary>On shift at a job: time runs at the working pace (like trading at the desk).</summary>
        public bool IsWorking => Job.OnShift;
        /// <summary>What the player has drawn on charts, per symbol (saved with the game).</summary>
        public ChartDrawings Drawings { get; } = new ChartDrawings();
        /// <summary>Furniture and tech bought (wherever it is), the store's loaner, and the homes owned.</summary>
        public global::OpeningBell.Home.Belongings Belongings { get; } = new global::OpeningBell.Home.Belongings();
        public global::OpeningBell.Home.Rental Rental { get; } = new global::OpeningBell.Home.Rental();
        public global::OpeningBell.Home.MovingCart Cart { get; } = new global::OpeningBell.Home.MovingCart();
        /// <summary>Timberline Home's and Circuit Stop's online stores: carts and orders.</summary>
        public global::OpeningBell.Home.HomeShop Shop { get; } = new global::OpeningBell.Home.HomeShop();
        /// <summary>Homes an online order can be delivered to (id, name), filled in by the city; empty without it.</summary>
        public Func<IReadOnlyList<(string Id, string Name)>> ShopDestinations { get; set; }
        /// <summary>Where an order's delivery truck is, in words ("Truck 1.2 km away · about 4 min"), or null.</summary>
        public Func<int, string> OrderTracking { get; set; }
        /// <summary>Opens the phone's map on an order's truck (null without the city's phone).</summary>
        public Action<int> TrackOrder { get; set; }
        /// <summary>
        /// A picture of a product (item id, colour), rendered by the city on request and handed to the callback
        /// (possibly a frame or two later); never called back without the city.
        /// </summary>
        public Action<string, int, Action<Texture2D>> ProductPreview { get; set; }
        public global::OpeningBell.Home.Estate Estate { get; } = new global::OpeningBell.Home.Estate();
        /// <summary>What the player has found on the map (the city's discovery fills it; saved with the game).</summary>
        public HashSet<string> Discovered { get; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>The player's bikes and cars (spec §16: persistent identity).</summary>
        public global::OpeningBell.Vehicles.Fleet Vehicles { get; private set; }
        /// <summary>Vehicle content (models, 3D meshes, traffic mix).</summary>
        public VehicleLibrary VehicleLibrary => vehicleLibrary;
        private EmailDirector _emails;

        /// <summary>Set by <see cref="StartNewGame"/> so the reloaded scene ignores the (deleted) save.</summary>
        private static bool _forceNewGame;

        /// <summary>The look chosen in the character creator before a new game reloads the scene.</summary>
        public static PlayerLook PendingLook { get; set; }

        /// <summary>The player's character; null until chosen (a new game shows the creator).</summary>
        public PlayerLook Look { get; set; }

        /// <summary>True when this session continued a save rather than starting fresh.</summary>
        public bool Continued { get; private set; }

        /// <summary>Player-controlled fast-forward on top of the base time scales (learning aid; difficulty may lock it later).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsPaused
        {
            get => Clock.IsPaused;
            set => Clock.IsPaused = value;
        }

        /// <summary>Seated trading slows time; walking around lets it pass faster (spec §7).</summary>
        public bool IsAtWorkstation { get; set; }

        /// <summary>
        /// Where to save the player instead of their body (set while driving: beside the car, so a reload doesn't
        /// put them inside it). Null = the body's position.
        /// </summary>
        public Vector3? PlayerSavePosition { get; set; }

        private void Awake()
        {
            if (catalog == null || marketSettings == null || economySettings == null)
            {
                Debug.LogError("GameBootstrap: assign the Security Catalog, Market Settings and Economy Settings assets.", this);
                enabled = false;
                return;
            }

            SaveGame save = TryLoadSave();
            ulong worldSeed = unchecked((ulong)(save?.Seed ?? seed));
            DateTime start = save != null ? new DateTime(save.Clock) : ConfiguredStart();

            // Loading = build from the same definitions, then overwrite the runtime state.
            Market = new MarketSimulation(marketSettings.Config, catalog.CreateSpecs(), catalog.Index, new SeededRandomService(worldSeed), start,
                newsLibrary != null ? newsLibrary.Templates : null,
                scenario != null ? scenario.ScheduledNews : null);
            if (save != null) Market.RestoreState(save.Market);

            Clock = new GameClock(start, tradingTimeScale);
            Account = new Account(Market);
            Orders = new OrderManager(Market, Account, brokerRules);
            // New game: deposit before the day recorder opens day 1, so savings aren't counted as profit.
            decimal cash = StartingMoneyOverride?.Cash ?? (decimal)startingCash;
            if (save == null && cash > 0m) Account.Deposit(cash, "Starting savings");
            Days = new TradingDayRecorder(Market, Account, Orders);
            Seed = worldSeed;

            // A save without economy data (older build) gets a fresh, funded economy from its load date.
            bool restoreEconomy = save != null && save.HasEconomy;
            Economy = new EconomySystem(economySettings.Config, economySettings.StoreItems, Account, start,
                fundBank: !restoreEconomy && StartingMoneyOverride == null);
            if (restoreEconomy) Economy.RestoreState(save.Economy);
            else if (StartingMoneyOverride is { Bank: > 0m } money) Economy.Receive(money.Bank, "Opening balance", start);

            Inbox = new Inbox();
            if (save != null && save.HasInbox) Inbox.RestoreState(save.Inbox);

            // Evaluations, activations and resets are paid from the bank; payouts land there.
            Prop = new PropDesk(Market, brokerRules, worldSeed)
            {
                Charge = (amount, what) => Economy.Spend(amount, what, Market.Now),
                Pay = (amount, what) => Economy.Receive(amount, what, Market.Now),
            };
            Prop.Notice += (account, title, body) =>
                Inbox.Deliver($"prop:{account.Id}:{Market.Now.Ticks}:{title}", Market.Now, account.Firm.Name, title, body);
            if (save != null && save.HasProp) Prop.RestoreState(save.Prop);
            if (save != null && save.HasJob) Job.RestoreState(save.Job);
            if (save != null && save.HasCasino) Casino.RestoreState(save.Casino);
            _emails = new EmailDirector(emailLibrary != null ? emailLibrary.Emails : Array.Empty<EmailDefinition>(),
                Inbox, Account, Orders, Economy, ConfiguredStart().Date);

            // The fund's owner money moves through the bank, like everything else personal.
            Fund = new global::OpeningBell.Fund.HedgeFund(Market, brokerRules, Belongings, worldSeed)
            {
                ChargeOwner = (amount, what) => Economy.Spend(amount, what, Market.Now),
                PayOwner = (amount, what) => Economy.Receive(amount, what, Market.Now),
            };
            Fund.Noticed += notice =>
            {
                if (notice.Level == global::OpeningBell.Fund.NoticeLevel.Routine) return;
                Inbox.Deliver($"fund:{notice.Time}:{notice.Title}:{notice.Employee}", Market.Now, Fund.Exists ? Fund.Name : "Ledgerline", notice.Title, notice.Body);
            };

            Vehicles = new global::OpeningBell.Vehicles.Fleet(vehicleLibrary != null ? vehicleLibrary.CreateCatalog()
                : new global::OpeningBell.Vehicles.VehicleCatalog(Array.Empty<global::OpeningBell.Vehicles.VehicleModel>(),
                    Array.Empty<global::OpeningBell.Vehicles.PartSpec>(), 0m, 0m));
            if (save != null && save.HasVehicles) Vehicles.RestoreState(save.Vehicles);

            if (save == null && PendingLook != null)
            {
                Look = PendingLook;
                PendingLook = null;
            }
            if (save != null)
            {
                TradingState.Restore(save.Trading, Account, Orders, Days);
                Continued = true;
                if (save.HasLook) Look = save.Look;
                if (save.HasDrawings) Drawings.RestoreState(save.Drawings);
                if (save.Discovered != null) Discovered.UnionWith(save.Discovered);
                if (save.HasHome && save.Home != null)
                {
                    Belongings.RestoreState(save.Home.Belongings);
                    Rental.RestoreState(save.Home.Rental);
                    Cart.RestoreState(save.Home.Cart);
                    Shop.RestoreState(save.Home.Shop);
                    Estate.RestoreState(save.Home.Estate);
                }
                if (save.HasFund && save.Fund != null) Fund.RestoreState(save.Fund);
                if (save.HasPlayer) PlacePlayer(save.Player);
                Debug.Log($"Loaded save '{saveSlot}' (day {Days.DayNumber}, {Clock.Now:ddd MMM d HH:mm}).");
            }
            // -fundtest: enough in the bank to register ($150k) and furnish a floor, until the fund exists.
            if (FundTest && !Fund.Exists && Economy.Bank.Balance < 500_000m)
                Economy.DevDeposit(500_000m - Economy.Bank.Balance, Clock.Now);
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
                HasEconomy = true,
                Economy = Economy.CaptureState(),
                HasInbox = true,
                Inbox = Inbox.CaptureState(),
                HasVehicles = true,
                Vehicles = Vehicles.CaptureState(),
                HasLook = Look != null,
                Look = Look ?? new PlayerLook(),
                HasDrawings = true,
                Drawings = Drawings.CaptureState(),
                HasProp = true,
                Prop = Prop.CaptureState(),
                HasJob = true,
                Job = Job.CaptureState(),
                HasCasino = true,
                Casino = Casino.CaptureState(),
                HasFund = true,
                Fund = Fund.CaptureState(),
                Discovered = new List<string>(Discovered),
                HasHome = true,
                Home = new global::OpeningBell.Home.HomeSaveData
                {
                    Belongings = Belongings.CaptureState(), Rental = Rental.CaptureState(), Cart = Cart.CaptureState(), Shop = Shop.CaptureState(), Estate = Estate.CaptureState(),
                },
            };
            if (player != null)
            {
                Vector3 p = PlayerSavePosition ?? player.position;
                save.HasPlayer = true;
                save.Player = new PlayerSaveData { X = p.x, Y = p.y, Z = p.z, Yaw = player.eulerAngles.y };
            }
            SaveSystem.Write(save, saveSlot);
        }

        /// <summary>Deletes the save and reloads the scene into a fresh game.</summary>
        public void StartNewGame()
        {
            SaveSystem.Delete(saveSlot);
            _forceNewGame = true;
            Time.timeScale = 1f; // the pause menu stopped physics
            enabled = false; // no quit-time save of the abandoned game
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
        }

        private DateTime ConfiguredStart() =>
            DateTime.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMinutes(startMinuteOfDay);

        private SaveGame TryLoadSave()
        {
            if (_forceNewGame)
            {
                _forceNewGame = false;
                return null;
            }
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
            Economy.AdvanceTo(Clock.Now);
            Fund.AdvanceTo(Clock.Now);
            _emails.Update(Clock.Now);
        }

        /// <summary>
        /// The career milestone for business ownership (FUND_SPEC §1): trading days with at least one fill (personal or
        /// prop), and proven profit (personal realized net of commissions, plus prop payouts received).
        /// </summary>
        /// <summary>The -fundtest launch flag: the business milestone counts as met (playtesting only).</summary>
        public static bool FundTest => Environment.GetCommandLineArgs().Contains("-fundtest");

        public (int TradingDays, decimal ProvenProfit) Career
        {
            get
            {
                var days = new HashSet<DateTime>();
                foreach (TradingDayReport r in Days.Completed) if (r.Fills > 0) days.Add(r.Date.Date);
                decimal paid = 0m;
                foreach (PropAccount a in Prop.Accounts)
                {
                    paid += a.TotalPaidOut;
                    foreach (PropDay d in a.Days) if (d.Traded) days.Add(d.Date.Date);
                }
                (int, decimal) career = (days.Count, Account.RealizedPnL - Account.TotalCommissions + paid);
                // Playtesting the fund without earning the milestone first: launch with -fundtest.
                if (FundTest) career = (Math.Max(career.Item1, Fund.Config.RequiredTradingDays), Math.Max(career.Item2, Fund.Config.RequiredProvenProfit));
                return career;
            }
        }

        private void Update()
        {
            // Working a shift runs at the desk's pace whatever the market is doing: a shift is a few hours you live through.
            float baseScale = IsWorking ? tradingTimeScale
                : Market.Session == MarketSession.Closed ? closedTimeScale
                : IsAtWorkstation ? tradingTimeScale : walkingTimeScale;
            Clock.TimeScale = baseScale * SpeedMultiplier;
            Clock.Advance(Mathf.Min(Time.unscaledDeltaTime, maxFrameSeconds));
            Market.AdvanceTo(Clock.Now);
            Economy.AdvanceTo(Clock.Now);
            Fund.AdvanceTo(Clock.Now);
            _emails.Update(Clock.Now);
        }
    }
}
