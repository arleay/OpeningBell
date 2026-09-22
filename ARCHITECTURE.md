# Architecture

Unity 6000.6.2f1. Vision/requirements: `PROJECT_SPEC.md` (core game) and `WORLD_SPEC.md` (city, vehicles, property; Phases 9–17).

## Assemblies (dependency direction →)

| Assembly | Folder | Unity refs | Contents |
|---|---|---|---|
| `OpeningBell.Core` | `Scripts/Core` | none | `SeededRandom`, `SeededRandomService`, `GameClock` |
| `OpeningBell.Market` | `Scripts/Market` | none | specs, schedule, price engine, simulation, candles |
| `OpeningBell.Trading` | `Scripts/Trading` | none | orders, execution, positions, ledger, account |
| `OpeningBell.Economy` | `Scripts/Economy` | none | bank account, bills, living costs, transfers, store |
| `OpeningBell.Runtime` | `Scripts/Runtime` | yes | ScriptableObjects, `GameBootstrap` (composition root) |
| `OpeningBell.UI` | `Scripts/UI` | yes | trading terminal (UI Toolkit) |
| `OpeningBell.Gameplay` | `Scripts/Gameplay` | yes | input, FPS controller, interaction, workstation, HUD |
| `OpeningBell.City` | `Scripts/City` | yes | generated city (Phase 9): layout, builders, doors, elevator, NPCs, traffic, pedestrians |
| `OpeningBell.Tests.EditMode` | `Tests/EditMode` | editor | NUnit tests (InternalsVisibleTo on Market/Trading) |
| `OpeningBell.Tests.PlayMode` | `Tests/PlayMode` | yes | scene-level tests that drive the real UI |

Core, Market and Trading use `noEngineReferences`, so they cannot touch UnityEngine by construction. Unity code only displays, collects input and drives time.

## Simulation rules

- **Time:** `GameClock` is authoritative. `MarketSimulation.AdvanceTo(clock.Now)` runs fixed ticks (`TickSeconds`, default 2 game-sec) on a grid aligned to midnight. Closed periods are skipped without ticking. The result is identical regardless of framerate, time scale or how AdvanceTo is chunked (tested).
- **Randomness:** one world seed, and every consumer takes a named stream from `SeededRandomService`. Each security has its own stream (`security:TICKER`), so adding a security never changes the others' paths (tested). Never use `System.Random` or `UnityEngine.Random` in simulation code.
- **Data vs. state:** `SecurityDefinition` (SO) holds a `SecuritySpec`. The simulation clones specs and config at construction, and live state lives in `SecurityRuntimeState`.
- **Fairness:** the price engine never sees orders. Player orders do not move prices (no market impact yet).

## Price model (`PriceEngine`)

Log price = fair + deviation, per tick:
- `fair += βm·market + βs·sector + σ·z`: permanent. Systematic moves persist.
- `deviation = deviation·e^(−κΔ) + φ·momentum·Δt + σ·k·z'`: transient overreaction that mean-reverts.
- σ = spec vol × intraday profile (U-shape plus opening burst, quiet extended hours) × stochastic *activity* (log-OU, volatility clustering). Activity also drives volume and spread.
- A normalizer makes `DailyVolatility` mean **close-to-close** (intraday profile plus overnight gap). The volume normalizer keeps `AverageDailyVolume` accurate on average.
- Prints happen at the ask on up-ticks and at the bid on down-ticks (natural bid/ask bounce). Candles are built from prints. Daily candles use regular-session prints only.
- Market index = cumulative market factor.

## News (`NewsEngine`, Phase 5)

- Data: `NewsTemplate`s in `NewsLibrary` (SO), scripted `ScheduledNews` in a `ScenarioDefinition` (SO; `OnboardingScenario` has the 8:15 APEX headline). Without templates the market runs news-free, which the calibration tests rely on.
- Planning: scheduled items at construction; random items per trading day (Poisson counts per scope; timing premarket-heavy). The engine has its own `news` RNG stream, so news never alters untouched securities (tested).
- Each headline becomes a catalyst via `PriceEngine.ApplyNews`:
  - **Permanent move:** `bias × severity × NewsImpactDailyVols × dailyVol × U(0.4, 1.3) + uncertainty × magnitude × z`. The draw is hidden from the player and can go against the headline. 35% lands at once and the rest over ~15 minutes.
  - **Overreaction:** a random fraction of the move added to the mean-reverting deviation (pop-and-fade or slow grind).
  - **Attention:** a log boost to volume (and √ of it to volatility and spread) with a 2-hour half-life.
  - **Delivery:** catalysts are impulses consumed by the next tick, so they are part of that tick's return. Leftovers are priced in at the next day's overnight gap.
- Sector news hits sector members by sector beta. Market news moves the index and every stock by market beta.
- UI: News panel (click a headline to select its stock), a watchlist dot for stocks with news today, the latest headline in the quote panel, and headline markers on the chart.

## Money & accounting

- `decimal` everywhere. Prices sit on a tick grid ($0.01, or $0.0001 below $1), so trade notionals are exact.
- `Position` tracks signed quantity and **total cost basis**. Partial exits remove pro-rata cost rounded to cents, and a full exit removes the remainder. Realized + unrealized therefore always reconciles exactly.
- `AccountLedger` is append-only. Cash changes only via ledger entries (trade and commission are separate entries).
- Invariant (tested): `Equity − NetDeposits == Realized + Unrealized − Commissions`.
- Cash account only. `BuyingPower = Cash − reservations for open buys`.

## Execution (`ExecutionEngine`, `OrderManager`)

- Orders are evaluated on submit and after every tick. All orders are day orders: market orders are cancelled at the regular close, everything at the after-hours close.
- Market orders are regular session only (a `BrokerRules` flag). Limit orders work in all sessions.
- Hidden depth: levels spaced about half a spread apart with growing size, and at most `BookLevelsPerTick` levels per tick. Big orders pay slippage and partially fill across ticks.
- Limit fills: a new marketable order walks the book (price improvement possible). A resting order fills at its limit when the market trades through it. A resting order *inside* the spread fills from opposing aggressive flow. An order joining the bid/ask waits (no queue model, conservative).
- Commission is computed on the order's cumulative fills (per-share, minimum, % cap), so partial fills never overpay the minimum.

## UI (trading terminal)

- **UI Toolkit, built in code** (no UXML). Styling lives in `Assets/UI/Terminal.uss` and the panel config in `Assets/UI/TerminalPanelSettings.asset` (scales from a 1920×1080 reference).
- `TradingTerminal` composes panels (account bar, watchlist, quote, chart, order entry, activity). Each `TerminalPanel` owns its root element and talks to others only through `TerminalContext` (services plus selected ticker). An in-world monitor can later host any subset of panels with its own `UIDocument`/`PanelSettings` (`targetTexture`).
- Panels refresh at a fixed UI rate (0.1s real), not per frame. The chart repaints only when `TickCount` changes or on input.
- Chart: Painter2D geometry with pooled absolute Labels for text. Layout is computed in `Rebuild()` and only read while painting. Pure math (`ChartViewport`: visible range, nice steps, VWAP, candle lookup) is unit-tested.
- Speed/pause: `GameBootstrap.SpeedMultiplier` × base time scale, and `IsPaused`. Simulation determinism is unaffected, because the market still ticks on its fixed grid.
- USS gotcha: don't reset Labels with a type selector (`.terminal Label`). It outranks single-class rules; reset `.unity-label` instead.

## World & player (Phase 4)

- **Rendering:** URP (`Assets/Settings/URP-*.asset`), with placeholder primitives and flat-colour URP materials in `Assets/Art/Materials`. Realtime point lights only; baking and shadows come in Phase 8.
- **Input:** Input System package. All actions are defined in code in `GameInput`, in two maps: `Player` (WASD, mouse look, Shift, E) and `Workstation` (Esc). Active input handling is **Both**, so UI Toolkit keeps its legacy event path; revisit switching to Input-System-only once verified.
- **Interaction:** `PlayerInteractor` raycasts from the camera, finds an `Interactable` in the hit collider's parents, and raises `FocusChanged` for the HUD. Adding an interactable object is one subclass.
- **Workstation:** `WorkstationController` states run Standing → SittingDown → Seated → StandingUp. Sitting glides the camera to `SeatView`, then the terminal goes full-screen with a free cursor. Leaving reverses this and places the player at `StandPoint`.
- **Terminal presentation:** `TradingTerminal` clones its PanelSettings at runtime. While standing it renders into `WorldTexture`, shown on the monitor mesh (so the room shows live markets). While seated, `targetTexture` is null and it's a full-screen interactive overlay. The HUD uses its own screen PanelSettings.
- **Time:** during market sessions the base scale is `tradingTimeScale` (10) at the desk and `walkingTimeScale` (30) away from it; `closedTimeScale` (120) applies when the market is closed. Speed buttons multiply these.

## Day loop (Phase 6)

- `TradingDayRecorder` (Trading) opens a `TradingDayReport` when a trading day's first session starts and finishes it when the market closes at 20:00. It tracks start/end equity, gross realized P&L, commissions, fills, winners/losers and best/worst closing fill. Since marks don't move while closed, daily `NetPnL`s sum exactly to the account's change (tested). It's the base for later statistics and the journal.
- `GameBootstrap.SkipTo(t)` jumps the clock and simulates the market through the gap immediately (orders expire, news publishes, reports finish).
- Sleep (`SleepController`, `BedInteractable`, pure `SleepRules`): possible from 16:00 until 06:00. The sequence is fade out → `SkipTo(next trading day 06:00)` → recap of the latest finished day → wake at `WakePoint`. Weekends are slept through.
- At the regular close the terminal shows the `DaySummaryPanel` overlay. The HUD shows the day number, time and session. `DaylightCycle` drives the window glow and light from the clock.

## Economy (Phase 7)

- **Two accounts.** The brokerage (`Account`) holds trading capital. The bank (`BankAccount` in `EconomySystem`) pays life: rent, utilities, subscriptions, a daily living cost, and store purchases. Money moves between them via `TransferToBrokerage/FromBrokerage` (instant for now). Only unreserved brokerage cash can leave.
- **Transfers are not trading P&L.** `Account.Withdraw/Deposit` adjust `DayStartEquity`, and `TradingDayReport.Transfers` is subtracted from `NetPnL` (tested).
- **Charging:** `EconomySystem.AdvanceTo(now)` charges each calendar day crossed, at midnight: the living cost, then any bills due that day. Bill days are clamped to month length. Results don't depend on how time advances (tested). A bill that leaves the bank negative adds an overdraft fee; this is the "failure without game over" hook.
- **Data:** `EconomySettings` (SO) holds the starting bank balance ($1,800), living cost, overdraft fee, bills (rent $950 on the 15th, electricity, internet, phone) and store items. Services (fiber) add a monthly bill and can replace a base bill.
- **Presentation:** Bank and Store apps in the terminal (app tabs in the account bar; only the visible app refreshes). `EquipmentPresenter` swaps basic and upgraded objects per apartment slot and re-targets the terminal texture when the monitor changes. The HUD toasts notable charges.
- Upgrades are cosmetic for now; their benefits wait for the systems they'd affect (psychology, latency, multi-monitor).
- Saved via `SaveGame.HasEconomy/Economy` (additive to v1; older saves get a fresh economy).

## Polish layer (Phase 8)

- **Pause menu** (`PauseMenu`, HUD panel): Esc while standing. It pauses the game clock and offers Resume, Save, Settings (mouse sensitivity, master volume in `GameSettings`/PlayerPrefs), New Game (confirmed; `GameBootstrap.StartNewGame` deletes the slot and reloads the scene) and Quit (autosaves). Esc is "back out one level": the Player map opens the menu, the Workstation map stands up, and the Menu map closes the menu.
- **Mail / onboarding** (`Inbox`, `EmailDirector`, `EmailLibrary` SO): scripted emails keyed so each arrives once. Triggers: game start, time of day, first fill/win/loss, 3 days before each rent date, overdraft. Tokens (`{bank}`, `{rent}`, …) are filled at delivery. Terminal MAIL app with an unread badge; HUD toast on arrival. Saved in `SaveGame.Inbox`.
- **Audio** (`GameAudio`): cues for the opening/closing bell, fills (throttled), news, bills and mail; ambience from room tone, fridge hum and PC fan (3D). Clips are synthesized placeholders (`ProceduralSounds`, marked TODO). Silent while asleep. Volume comes from `GameSettings`.
- **Performance baseline:** a full 10-stock trading day with news simulates in about 0.4 s (about 13 µs per tick). Seated frames average about 0.4 ms with no gen0 GCs (batchmode). Guarded by `Performance_FullContentDay_SimulatesQuickly` and `FrameTime_WhileTrading_IsReasonable`.

## City (Phase 9, `WORLD_SPEC.md`)

- **Generated at load, not authored.** `CityBuilder` (scene object "City") builds everything in `Awake` from `CityPlan` (streets, blocks, shells, special buildings as numbers in code), so the layout is reviewable and identical every run. The apartment room stays scene-authored at the origin; the city wraps around it. Static geometry is statically batched; doors, signals and NPCs live under a separate dynamic root.
- **Layout:** a ring road with two cross streets (Maple, Grove, Cedar, First, Exchange, Harbor), three core blocks (residential, commercial, downtown) and an outer ring of buildings that closes the world (invisible walls at `CityPlan.World`, skyline boxes in the fog beyond). Roads sit at y −0.15, sidewalks and floors at 0, so blocks form real curbs. About 2 minutes' walk end to end; later districts extend it.
- **Materials:** five template assets in `Art/Materials/City` (lit, lit+emission, glass, unlit, sign) are cloned per colour (`Palette`), so their shader variants ship in builds. Facades use procedural window-grid textures (`FacadeTextures`, marked TODO(art)) with per-building UVs (one cell per ~3 m bay × ~3.2 m floor); their emission lights ~40% of windows at night. Signs are `TextMesh` with a custom depth-tested unlit shader (`OpeningBell/SignText`), since the font's default shader draws through walls. URP strips `_EMISSION` from a material whose emission colour is black, so the template keeps a white one.
- **Buildings:** `ApartmentBuilding` (hallway, front door, neighbour's unit), `CalderBuilding` (glass lobby, receptionist, mailboxes, directory, restroom, elevator, stairs, floor-2 corridor, Suite 204 with a desk), `Shops` (Half Past Nine coffee, Corner Mart), `ShellBuilder` (outside-only volumes with a front door that pedestrians use).
- **Office lease:** a `StoreCategory.Lease` store item (`office_suite_204`) whose monthly cost bills as rent (`BillCategory.Lease`, `TransactionKind.Rent`) and doesn't trigger the apartment-rent email. Key-card rules read `Economy.Owns`: suite door, floor-2 button, stair door, after-hours lobby.
- **Workstations:** `WorkstationController` handles any `Desk` (seat view, stand point, monitor). Every monitor samples the same terminal render texture. Null = the home desk.
- **Elevator:** `ElevatorLogic` (pure: requests, door timing, travel time) plus `Elevator` (visuals, chime, hum). Each floor has its own identical car interior stacked in the shaft; on arrival the player is moved by the floor height. Nothing moves and nothing can be fallen through (spec §8's controlled transition).
- **Traffic (`TrafficSimulation`, `RoadNetwork`):** kinematic cars on one-lane-each-way roads (right-hand traffic). Junction movements are Bezier curves; movements closer than 2.4 m conflict. A car decides at its stop line: lights (2-phase cycle in world seconds), stop signs on T-junction stems, right of way by movement priority (gap acceptance), don't-block-the-box, and nobody on the crosswalks it's about to cross. Deciding reserves the movement, so two cars can't commit to conflicting paths. A car that committed on green re-checks if it can still stop. Car following, and stopping for people, use path lookahead. Headless tests: no overlaps (separating-axis test), no stop-line crossings on red, no gridlock over 20 minutes.
- **Pedestrians (`SidewalkGraph`, `PedestrianSimulation`):** one loop per block along the sidewalk centre line, crosswalks at every junction approach, spurs to doors and benches. People walk door to door or door to bench, keep right, wait for the walk signal (lights) or a 17 m gap (elsewhere), sit, and step around the player. They're removed at their destination door.
- **Populations** follow the game clock (rush hours busy, nights quiet). Cars spawn and despawn only out of the camera frustum and more than 40 m away; pooled bodies are created far below the world, since a collider appearing at the origin (inside the apartment) shoves the player.
- **Staff NPCs (`StaffNpc`):** shift plus break (`WorkSchedule`/`Hours`), walk to a back door when off duty (snap if the player is more than 30 m away), fidget at the station, greet once per visit, lines on [E] Talk. Shop counters (`ShopCounter`) need staff present; card purchases use `EconomySystem.Spend` (declined, never overdrawn).
- **Daylight:** `DaylightCycle` now also drives the sun (arc east → south → west), ambient light, fog, and `NightFactor` for lamps and windows. The apartment window still uses its own gradient.
- **Not saved on purpose:** doors, elevator state, NPC and traffic positions (all time-derived or transient). The player position (anywhere in the city), the lease and purchases are saved as before.
- **Perf (batchmode):** street view with 18 cars and 21 people is about 1.1 ms per frame on the CPU.

## Save / load

- **Goal: exact resume.** Loading builds the simulation from the same definitions (catalog, config, news templates, seed), then overwrites runtime state. A loaded game continues tick-for-tick like the original. `SaveLoadTests.SavedGame_ResumesExactly_ThroughJson` saves mid-session with open orders and a queued scheduled headline, round-trips JSON, runs both two days on, and compares everything.
- **DTOs, not scene objects:** `MarketSimulation.CaptureState/RestoreState` (Market), `TradingState.Capture/Restore` (Trading), and the root `SaveGame` (Runtime; adds seed, clock, player position). Each domain restores its own internals through `internal` hooks.
- **Encoding (JsonUtility-safe and lossless):** money as invariant decimal strings; prices and notionals as fixed-point ×10⁴ longs (they live on a 0.0001 grid, which is asserted); engine doubles as raw IEEE bits; RNG state (`RandomState`) as longs; times as ticks.
- **Size caps:** daily candles in full; 1-minute candles for the last 2000 bars; 5m/15m/1h rebuilt from 1-minute on load (lossless). The last 300 headlines, 200 orders (plus any open), and 500 fills. About 1 MB with 10 stocks.
- **Definition drift:** saved securities missing from the catalog are dropped. New listings keep fresh state. Queued news with unknown templates is dropped.
- **Files (`SaveSystem`):** `persistentDataPath/saves/<slot>.json`. Writes go to a temp file and then `File.Replace`, keeping a `.bak`. A corrupt slot falls back to its backup. Newer-version saves are refused, with the upgrade step documented at the parse site. Bump `SaveGame.CurrentVersion` on breaking changes.
- **When:** autosave on waking (end of day) and on quit. The game continues the slot on launch; `-newgame` or the bootstrap's "Delete Save" context menu starts fresh. PlayMode tests redirect `SaveSystem.DirectoryOverride` to a temp folder and disable bootstraps on teardown, so test runs never touch real saves.

## Editor scripting gotcha

Opening or creating a scene in Single mode unloads in-memory assets that nothing references yet, and references assigned from them become null. Load assets **after** opening the scene. `DefaultContentTests.MainScene_HasNoUnassignedReferences` guards this.

## Testing

`./run-tests.ps1` (close the Editor first) runs EditMode tests headlessly and prints failures. `-Filter` takes a test or class name.
`./run-tests.ps1 -Platform PlayMode` loads `Main`. It trades through the terminal (via UI events) and walks, sits, trades, stands and leaves using simulated keyboard/mouse devices (Input System `QueueStateEvent`). It writes `TestResults/terminal-trading.png` and `TestResults/apartment-standing.png` for visual review.
`CityPlayTests` walks the Phase 9 commute with the real CharacterController: apartment, street, crossings, lobby, elevator, office trade, stairs, coffee, home, save. It writes `TestResults/city-*.png` (street, downtown, lobby, office, coffee shop, night).
