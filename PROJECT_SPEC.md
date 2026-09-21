# MASTER DEVELOPMENT PROMPT — FIRST-PERSON DAY TRADING SIMULATOR

You are the lead game designer, Unity engineer, systems designer, technical artist, UI/UX developer, economy designer, and QA engineer for a new PC game.

Your job is to design and implement a polished **first-person day-trading life simulation game in Unity**.

The game should combine:

* Serious, surprisingly deep day-trading simulation
* First-person movement and physical interaction
* An explorable apartment/office
* A stylized low-poly 3D aesthetic
* Physical progression from a broke beginner setup to a professional trading operation
* A tactile management/life-sim structure
* NPC relationships and a small explorable neighborhood/city hub
* Dynamic financial markets
* News catalysts
* Risk management
* Trading psychology
* Lifestyle expenses
* Equipment upgrades
* Career progression
* Reputation
* Financial consequences
* Long-term progression

The overall presentation can take **high-level inspiration from games such as Schedule I** in terms of:

* stylized low-poly 3D graphics
* first-person perspective
* physical interaction with objects
* slightly exaggerated character proportions
* compact explorable environments
* tactile computer interfaces
* upgrading a humble starting location
* satisfying progression
* environmental storytelling
* a mixture of serious systems and dry/comedic moments
* a world that feels alive despite having a relatively small scope

However:

**DO NOT copy Schedule I's map, characters, dialogue, missions, UI, models, textures, logos, names, story, or specific visual assets.**

Create an original identity.

---

# 1. WORKING TITLE

Use the temporary title:

**OPENING BELL**

Treat this only as a working title so it can easily be changed later.

Genre:

**First-Person Trading / Life Simulation / Management / Financial Simulation**

Platform:

**PC first**

Future Steam release should be considered when choosing architecture and controls.

Primary input:

* Keyboard
* Mouse

Architecture should leave room for controller support later.

---

# 2. CORE PLAYER FANTASY

The player starts with:

* a cheap apartment
* an old computer
* one monitor
* very little savings
* basic market information
* no reputation
* limited buying power
* limited access to professional tools

The fantasy is:

> "Start as an unknown retail trader sitting in a tiny apartment and gradually build yourself into a highly sophisticated professional trader."

The player should physically experience that progression.

At first, they may trade from:

* a folding table
* cheap office chair
* old laptop
* slow internet connection
* one small monitor

Eventually they can have:

* luxury apartment
* dedicated trading office
* multiple monitors
* ultrawide displays
* high-end PC
* standing desk
* professional news terminals
* data subscriptions
* market scanners
* faster order-routing technology
* backup internet
* premium research services
* assistants/researchers
* professional office space

The environment itself should visually communicate progression.

---

# 3. IMPORTANT DESIGN PHILOSOPHY

Do NOT build this as:

"Click BUY and watch a random number go up."

Trading must behave like a legitimate simulation.

At the same time, do not create such a complicated quantitative-finance model that development becomes impossible.

The target should be:

**easy to understand, difficult to master.**

A new player should be capable of:

1. finding a stock
2. reading its chart
3. buying shares
4. watching the position
5. selling it
6. understanding whether money was gained or lost

An experienced player should eventually care about:

* spread
* liquidity
* slippage
* volatility
* catalysts
* relative volume
* market regime
* sector strength
* position sizing
* risk/reward
* stop placement
* average cost
* short availability
* commissions
* margin
* buying power
* borrow fees
* realized P&L
* unrealized P&L
* drawdown

---

# 4. DO NOT CONNECT TO REAL BROKERAGE ACCOUNTS

The game uses:

**SIMULATED MONEY ONLY.**

Never place real trades.

Do not build Robinhood, Interactive Brokers, Schwab, Webull, etc. account connectivity into the core game.

Use fictional companies and fictional ticker symbols.

Examples:

* BLZE — Blaze Automotive
* NVRA — Navira Technologies
* OCEA — Oceanic Energy
* APEX — Apex Robotics
* TRON — Tronic Systems
* CYRA — Cyra Pharmaceuticals
* NRTK — Nortek Industries
* VSTA — Vista Entertainment
* GLXY — Galaxy Aerospace

Generate many more later through ScriptableObjects or data files.

Optional historical-data/import systems can be considered much later.

---

# 5. FIRST-PERSON GAMEPLAY

The player controls a physical character in first person.

Implement:

* WASD movement
* mouse look
* crouching if useful
* sprinting
* physical interaction
* doors
* chairs
* computers
* monitors
* light switches
* phones
* appliances
* packages
* furniture
* objects that can be inspected
* interactable terminals

Interaction should be contextual.

Example:

Walk to desk.

Aim at chair.

Prompt:

**[E] Sit**

After sitting:

* camera smoothly moves into trading position
* mouse cursor becomes available
* keyboard shortcuts switch from walking controls to trading controls
* the monitors become interactive

Press Escape or appropriate key to exit workstation mode.

Do NOT make the player interact with every trading menu through awkward FPS raycasting.

When seated at a computer, transition naturally into a proper trading interface.

---

# 6. THE PLAYER'S COMPUTER

The player's computer is one of the most important objects in the game.

It should eventually contain multiple applications.

Initial applications:

## Broker

Contains:

* Account balance
* Buying power
* Positions
* Orders
* Watchlist
* Level 1 quote
* Chart
* Order entry
* Trade history
* Realized P&L
* Unrealized P&L

## News

Financial headlines.

## Scanner

Initially locked or limited.

Later detects:

* biggest gainers
* biggest losers
* unusual volume
* gap ups
* gap downs
* high relative volume
* volatility
* news catalysts

## Email

Used for:

* bills
* broker notifications
* margin notices
* subscription renewals
* story messages
* NPC communication
* offers
* progression

## Bank

Displays:

* cash
* rent
* utilities
* subscription costs
* purchases
* trading transfers

## Store

Buy:

* monitors
* PCs
* desks
* chairs
* routers
* furniture
* accessories

## Calendar

Shows:

* market days
* earnings dates
* economic events
* rent due dates
* subscription renewals

Additional apps can unlock later.

---

# 7. MARKET HOURS

Use a fictionalized but recognizable U.S.-style trading structure.

Suggested schedule:

Premarket:
4:00 AM – 9:30 AM

Opening Bell:
9:30 AM

Regular Session:
9:30 AM – 4:00 PM

After Hours:
4:00 PM – 8:00 PM

However, gameplay time needs to remain practical.

Create configurable time scaling.

Suggested design:

Walking around:
time can advance relatively quickly.

During active trading:
time slows substantially.

Possible default:

1 real second = 5–15 in-game seconds during market hours.

Allow the balancing values to be changed easily.

Add optional pause functionality while learning, unless difficulty mode disables it.

---

# 8. DAILY GAME LOOP

A typical day might be:

### 6:00 AM

Wake up.

### 6:15 AM

Check phone.

Read overnight market news.

### 6:30 AM

Make coffee.

### 7:00 AM

Sit at computer.

### 7:10 AM

Run premarket scanner.

Find:

BLZE +24%

Reason:

Blaze Automotive announces major battery partnership.

### 7:20 AM

Research company.

Check:

* float
* market cap
* average volume
* catalyst
* previous close
* premarket high

### 9:30 AM

Market opens.

Volatility dramatically increases.

Player waits for setup.

### 9:37 AM

BLZE pulls back.

Player buys:

500 shares at $8.42.

### 9:39 AM

Price rises to $8.74.

Player sells.

Profit approximately:

$160 before fees/slippage.

### 10:45 AM

Player sees another setup.

Chases it emotionally.

Loses $240.

Now player must decide:

Keep trading?

Or stop for the day?

That decision should matter.

---

# 9. TRADING PSYCHOLOGY SYSTEM

Create a subtle psychology system.

Possible stats:

* Stress
* Confidence
* Fatigue
* Focus
* Discipline

These should NOT feel like cartoon RPG bars constantly dominating the screen.

They should primarily influence gameplay subtly.

Examples:

High fatigue:

* slightly slower information processing
* scanner alerts may be delayed
* visual focus effects
* increased likelihood of misclick warning events

High stress:

* heartbeat/audio feedback
* subtle mouse instability
* tunnel vision
* reduced awareness of notifications

Good sleep:

* improved focus

Food:

* improves energy

Too much caffeine:

* boosts alertness temporarily
* can increase stress later

Winning streak:

* increases confidence

But excessive confidence can lead to:

**Overconfidence**

This can encourage poor player decisions through environmental/UI feedback without directly forcing trades.

Do NOT secretly manipulate the market against the player because they are winning.

The market system must remain fair.

---

# 10. SERIOUS MARKET SIMULATION

Create a dedicated market simulation layer completely separated from rendering.

The Game Studio architecture principle of separating simulation state from rendering/UI should be followed.

Market simulation is the authoritative source of truth.

UI only displays the state.

The market engine should support:

* hundreds of fictional securities eventually
* market index
* sector indexes
* individual stock behavior
* volatility
* liquidity
* trading volume
* bid
* ask
* spread
* last price
* OHLC candles
* relative volume
* news events
* earnings
* gaps
* trends
* mean reversion
* momentum
* market-wide correlations
* sector correlations
* occasional idiosyncratic stock movement

Do NOT generate every security using totally independent random numbers.

---

# 11. MARKET FACTOR MODEL

Use multiple factors.

Example:

StockReturn =

MarketFactor

* SectorFactor
* CompanyFactor
* NewsImpact
* Noise

Each security should have:

* market beta
* sector
* volatility
* liquidity
* float
* market cap
* average volume
* news sensitivity
* momentum coefficient
* mean-reversion coefficient

This allows realistic relationships.

Example:

If the overall technology sector is weak:

most technology companies should experience downward pressure.

But a company with a strong catalyst may still rise.

---

# 12. MARKET REGIMES

Create changing market regimes.

Examples:

* Bull Market
* Bear Market
* Sideways Market
* High Volatility
* Low Volatility
* Risk-On
* Risk-Off
* Sector Rotation

Do not tell the player explicitly:

"THE MARKET IS NOW BEARISH."

Instead communicate it through:

* charts
* news
* price action
* volatility
* analyst commentary
* NPC conversations
* market index behavior

Experienced players should learn to recognize regimes.

---

# 13. INTRADAY MARKET BEHAVIOR

Volume and volatility should not remain constant.

Model typical intraday patterns.

Higher activity:

* market open
* major news releases
* market close

Lower activity:

* midday

Volume profile can approximately follow a U-shaped curve.

Spreads should react to:

* liquidity
* volatility
* market conditions
* news

---

# 14. CANDLES

Generate:

* 1 minute
* 5 minute
* 15 minute
* hourly
* daily

OHLCV data.

Chart system must support:

* candlesticks
* volume
* crosshair
* zoom
* pan
* current price
* average cost line
* entry markers
* exit markers

Later indicators:

* VWAP
* SMA
* EMA
* RSI
* volume averages

Do NOT implement 50 indicators immediately.

Start with:

* candlesticks
* volume
* VWAP

---

# 15. ORDER TYPES

Initial vertical slice:

* Market Buy
* Market Sell
* Limit Buy
* Limit Sell

Next phase:

* Stop Market
* Stop Limit

Later:

* Bracket Orders
* OCO
* Trailing Stops
* Short Selling

The system must maintain an actual order lifecycle.

Possible states:

* Pending
* Working
* PartiallyFilled
* Filled
* Cancelled
* Rejected

---

# 16. FILL SIMULATION

Do not automatically fill every order perfectly at the displayed price.

Model:

* bid
* ask
* spread
* liquidity
* slippage
* order size

Example:

Quote:

Bid:
$10.18

Ask:
$10.20

Buying market shares should normally execute near the ask.

Selling market shares should normally execute near the bid.

Large orders relative to available liquidity should receive worse average execution.

Limit orders should only execute when conditions permit.

Partial fills should eventually be possible.

Keep implementation computationally manageable.

We do NOT need to simulate every real exchange participant.

Create a convincing approximation of market microstructure.

---

# 17. ACCOUNTING SYSTEM

Track precisely:

* cash
* settled/unsettled funds if implemented
* buying power
* equity
* long market value
* short market value
* realized P&L
* unrealized P&L
* daily P&L
* total return
* commissions
* data fees
* borrow fees
* margin interest

Every transaction must be auditable.

Avoid floating-point money errors.

Prefer a consistent financial representation such as:

decimal where appropriate

or integer values representing cents/micro-units if Unity serialization requirements make that better.

Do not allow money values to drift due to float errors.

---

# 18. POSITION SYSTEM

Each position should track:

* ticker
* quantity
* direction
* average price
* current price
* market value
* unrealized P&L
* realized P&L

Scaling in must correctly update average cost.

Scaling out must correctly calculate realized P&L.

Write unit tests for this.

---

# 19. RISK MANAGEMENT

Risk management should be essential.

Track statistics:

* largest winner
* largest loser
* average winner
* average loser
* win percentage
* profit factor
* maximum drawdown
* average hold time
* total trades
* consecutive wins
* consecutive losses

Later provide a trading journal.

Journal automatically records:

* entry
* exit
* P&L
* chart snapshot/data
* catalyst
* position size
* time
* player notes

Players should gradually learn that:

being profitable is not simply about having more winning trades.

---

# 20. NEWS CATALYST ENGINE

Create a dynamic financial news system.

Catalyst types:

* Earnings beat
* Earnings miss
* Guidance raise
* Guidance cut
* CEO resignation
* FDA-style fictional regulatory approval
* Product launch
* Lawsuit
* Acquisition offer
* Acquisition rumor
* Major contract
* Government investigation
* Analyst upgrade
* Analyst downgrade
* Share offering
* Buyback
* Bankruptcy concerns
* Sector news
* Economic news

Each event should have:

* affected securities
* severity
* directional bias
* uncertainty
* initial reaction
* possible continuation
* possible reversal

Do NOT make news perfectly predictive.

A positive headline does not guarantee the stock increases forever.

Market expectations matter.

---

# 21. EARNINGS

Later implement earnings events.

Store:

* expected EPS
* actual EPS
* expected revenue
* actual revenue
* guidance
* prior expectations

The reaction should depend partly on:

**expectations vs reality**

not simply "profit good = stock goes up."

---

# 22. TRADING HALTS

Later support volatility halts.

Example:

A small-cap stock rises violently.

Trading halts.

Player cannot trade it temporarily.

During halt:

* orders remain managed according to their rules
* news may appear
* anticipation builds

When trading resumes:

possible gap up or gap down.

This should create tension.

---

# 23. SHORT SELLING

Not needed for first vertical slice.

Later implement:

* short inventory
* borrow availability
* borrow fee
* hard-to-borrow securities
* forced risk controls
* buy-to-cover

Some stocks should occasionally be unavailable to short.

---

# 24. BROKER PROGRESSION

The starting broker should have disadvantages:

* slower data
* limited scanner
* basic charts
* limited order types
* mediocre execution

Player can eventually unlock better brokers/tools.

Do not use real brokerage names.

Example fictional firms:

* PennyBridge
* Summit Securities
* Vertex Markets
* Meridian Pro
* Atlas Direct

Possible differences:

* commissions
* platform fee
* market-data cost
* execution quality
* margin
* short inventory
* routing speed
* available tools

---

# 25. PHYSICAL EQUIPMENT PROGRESSION

Purchases should physically appear in the world.

Possible items:

### Computers

* old laptop
* desktop
* gaming PC
* workstation

### Displays

* 19-inch monitor
* 24-inch monitor
* 27-inch monitor
* ultrawide
* vertical news monitor
* 6-monitor professional setup

### Furniture

* folding table
* budget desk
* executive desk
* standing desk

### Chairs

* cheap chair
* ergonomic chair
* premium chair

### Networking

* cheap router
* better router
* wired connection
* backup modem
* fiber upgrade

### Lifestyle

* coffee maker
* refrigerator
* couch
* television
* decorations
* better bed
* exercise equipment

Some upgrades provide small benefits.

Examples:

Better bed:
better sleep/focus.

Fiber internet:
reduced simulated market-data latency.

Additional monitor:
more panels simultaneously visible.

Better computer:
more advanced scanner tools can run.

Avoid turning furniture into absurd stat boosts.

---

# 26. MONITOR SYSTEM

One of the signature features should be multiple physical monitors.

Example progression:

One monitor:

player constantly switches windows.

Two monitors:

chart + order entry.

Three monitors:

chart + scanner + news.

Six monitors:

professional setup.

Architect the UI so multiple panels can eventually be assigned to different in-world displays.

Do NOT require the full advanced system for vertical slice.

But build the system so it can expand there.

---

# 27. PHONE

Give the player a smartphone.

Functions later include:

* text messages
* email
* bank
* market watchlist
* news alerts
* calendar
* contacts
* store delivery
* NPC communication

Player should be able to look at it while walking.

---

# 28. WORLD

Start small.

Do NOT attempt a huge city.

Create a dense small neighborhood/hub.

Vertical slice locations:

1. Player apartment
2. Apartment hallway
3. Building exterior
4. Small street
5. Convenience/coffee shop

Later:

* electronics store
* bank
* gym
* nicer apartments
* professional office building
* restaurant/bar
* brokerage office
* training/education location

The game's depth should come from systems rather than map size.

---

# 29. NPC SYSTEM

Eventually include recurring NPCs.

Examples:

### Neighbor

Comments on the player's lifestyle.

### Coffee Shop Employee

Knows player's routine.

### Veteran Trader

Potential mentor.

Does not magically give perfect trades.

### Reckless Trader

Constantly talks about oversized positions and "sure things."

Sometimes wins spectacularly.

Sometimes loses badly.

### Financial Reporter

Appears through media.

### Broker Representative

Unlocks better account features.

NPC dialogue should provide:

* atmosphere
* misinformation
* clues
* humor
* character development

NPCs must not always be reliable.

---

# 30. SOCIAL / REPUTATION SYSTEM

Player can eventually build a trading reputation.

Possible pathways:

* consistent profitable trader
* high-risk speculator
* disciplined professional
* famous financial personality

Do NOT reduce reputation to one arbitrary number.

Track several dimensions if necessary.

Possible future features:

* streaming
* financial social media
* chat rooms
* trading communities
* interviews
* mentorship

Avoid real-world pump-and-dump mechanics.

The player should not be rewarded for manipulating markets.

---

# 31. EXPENSES

Trading profits should not exist in isolation.

The player has life expenses.

Examples:

* rent
* electricity
* internet
* food
* equipment
* market data
* software subscriptions
* broker fees

This creates meaningful decisions.

Example:

Account:
$4,800

Rent:
$1,100 due in three days.

Trading loss today:
-$420.

Now continuing to trade feels consequential.

---

# 32. FAILURE

Avoid a simple instant GAME OVER.

If the player loses significant money:

* downgrade apartment
* cancel data subscriptions
* sell equipment
* take temporary work
* trade smaller size
* rebuild account

Bankruptcy could exist as an extreme state.

But recovery should usually be possible.

---

# 33. DIFFICULTY MODES

Eventually offer:

### Casual

* simplified fills
* forgiving expenses
* clear explanations
* slower market

### Trader

* realistic spread
* realistic slippage
* normal expenses
* realistic volatility

### Professional

* stricter margin
* no pausing during market session
* expensive market data
* harsher slippage
* realistic short constraints
* limited information

Do not implement all of this immediately.

Architect configuration so these can be introduced later.

---

# 34. ART DIRECTION

Create an original stylized 3D visual identity.

Target:

* low-poly
* clean silhouettes
* slightly exaggerated proportions
* warm interior lighting
* atmospheric nights
* readable materials
* simple but expressive NPC faces
* modest texture resolution
* strong color/value separation
* good performance

The world should feel:

slightly gritty
slightly humorous
cozy at night
tense during trading hours

Avoid photorealism.

Do not chase AAA graphics.

The game needs charm and readability.

Use primitives and placeholder assets during development.

Gameplay takes priority.

---

# 35. AUDIO

Audio is critical.

Include eventually:

* keyboard typing
* mouse clicks
* chair movement
* computer fans
* street ambience
* refrigerator hum
* coffee maker
* phone vibration
* notification sounds
* order-fill sound
* market-open bell
* news alerts
* rain
* traffic
* apartment neighbors

During intense trades, audio design can subtly increase tension.

Do not spam loud casino-style sounds.

This is not a slot machine.

---

# 36. UNITY TECHNICAL DIRECTION

Use:

**Unity + C#**

Prefer the current stable/LTS Unity version already associated with the project.

Do not upgrade the entire project unnecessarily.

Rendering:

**URP**

Use standard Unity systems where appropriate.

Preferred packages where sensible:

* Unity Input System
* Cinemachine
* TextMeshPro or appropriate modern Unity UI text
* Unity Test Framework

For complex trading interfaces, choose between:

* UI Toolkit
* uGUI

based on maintainability and actual requirements.

UI architecture must remain separate from trading simulation.

Do not tightly couple market code to MonoBehaviours.

---

# 37. ARCHITECTURE

Use a modular architecture.

Suggested high-level structure:

Assets/
Art/
Audio/
Materials/
Models/
Prefabs/
Scenes/
Scripts/
Core/
Player/
Interaction/
Market/
Trading/
Economy/
Time/
World/
NPC/
UI/
Save/
Audio/
Debug/
Tests/
ScriptableObjects/
Securities/
News/
Equipment/
Brokers/
Economy/
UI/
Settings/

Possible core systems:

GameManager

GameClock

MarketSimulation

SecurityRegistry

PriceEngine

MarketRegimeSystem

NewsEngine

OrderManager

ExecutionEngine

Portfolio

Account

BrokerSystem

EconomyManager

PlayerController

InteractionSystem

WorkstationController

ComputerOS

SaveManager

AudioManager

NPCManager

Use dependency injection or explicit references where helpful.

Do not turn GameManager into a 5,000-line god class.

---

# 38. PURE C# MARKET DOMAIN

Where practical, the financial simulation should use pure C# classes independent of Unity scene objects.

For example:

MarketSimulation

SecurityState

Order

Trade

Position

Portfolio

AccountLedger

ExecutionEngine

CandleAggregator

NewsEvent

These systems should be testable without loading a Unity scene.

Unity components should primarily:

* display
* animate
* collect input
* connect simulation to the scene

This is extremely important.

---

# 39. DETERMINISTIC SIMULATION

Support seeded market generation.

Example:

Seed = 18492

The same seed and same inputs should produce reproducible behavior where feasible.

This is useful for:

* testing
* debugging
* balancing
* replaying scenarios

Create a controlled random-number service rather than calling UnityEngine.Random everywhere.

---

# 40. SCRIPTABLEOBJECT DATA

Use ScriptableObjects for static definitions where appropriate.

Example SecurityDefinition:

Ticker

CompanyName

Sector

MarketCapCategory

BasePrice

BaseVolatility

AverageVolume

Float

LiquidityRating

MarketBeta

SectorBeta

MomentumCoefficient

MeanReversionCoefficient

Use runtime state separately.

Do not mutate persistent ScriptableObject definitions with live market values.

---

# 41. SAVE SYSTEM

Save:

* player money
* brokerage account
* portfolio
* statistics
* equipment
* apartment state
* progression
* current day
* subscriptions
* settings

Do NOT serialize scene objects directly.

Use explicit serializable save-data models.

Include save version number.

Plan for migration between versions.

Autosave:

* end of day
* major purchases
* exiting game

Allow manual save outside active trading where appropriate.

---

# 42. DEBUG TOOLS

Build developer tools early.

Create a debug panel capable of:

* changing account balance
* changing time
* opening/closing market
* spawning news
* forcing stock volatility
* selecting market regime
* modifying stock price
* granting equipment
* resetting portfolio
* displaying current simulation seed
* displaying FPS
* displaying market tick duration

These tools should compile out or hide in release builds.

---

# 43. PERFORMANCE

Target ordinary gaming PCs.

Do not prematurely optimize tiny systems.

But avoid obvious mistakes.

Market calculations should not depend on rendering framerate.

Use a fixed simulation interval.

Possible architecture:

Rendering:
every frame.

Market:
separate controlled simulation ticks.

Charts:
update at reasonable intervals.

NPC AI:
lower-frequency updates where practical.

---

# 44. TESTING REQUIREMENTS

Write tests for important financial behavior.

At minimum test:

### Position average price

Buy:
100 @ $10

Buy:
100 @ $12

Average:
$11

### Partial exit

Own:
200 @ $11

Sell:
100 @ $13

Realized profit:
$200

Remaining:
100 @ $11

### Complete exit

Remaining shares sold.

Position becomes closed.

### Market order

Market buy should execute against simulated ask.

### Limit order

Should not fill outside permitted price.

### Account balance

Cash and buying power must remain consistent.

### Saving

Portfolio survives save/reload accurately.

### Candle generation

Ticks aggregate correctly into OHLCV candles.

Add tests when bugs are discovered.

---

# 45. VERTICAL SLICE

Do NOT immediately attempt the entire design document.

The FIRST playable milestone should contain:

## Physical environment

One small apartment.

Player can:

* walk
* look around
* interact
* sit at desk
* use computer
* stand up

## Time

A working in-game clock.

## Market

10 fictional stocks.

One market index.

Prices change dynamically.

Market opens and closes.

## Computer

Functional trading UI.

Displays:

* ticker
* quote
* candlestick chart
* watchlist
* positions
* account balance

## Trading

Player can:

* select stock
* enter quantity
* market buy
* market sell
* limit buy
* limit sell

## Accounting

Correct:

* cash
* equity
* average cost
* realized P&L
* unrealized P&L

## News

At least several news events capable of affecting securities.

## Save

Save and load player/account state.

## Debug

Basic developer console/panel.

This should become the first genuinely playable build.

---

# 46. FIRST GAMEPLAY SCENARIO

Create a scripted onboarding day for testing.

Player starts with:

$10,000.

Apartment is simple.

Stocks include:

BLZE
NVRA
OCEA
APEX
CYRA
VSTA
NRTK
GLXY
MTRX
SOLR

At approximately 8:15 AM:

News:

"APEX Robotics announces major distribution agreement."

APEX receives:

* elevated volume
* increased volatility
* upward catalyst pressure

Player sees APEX on watchlist.

At market open:

APEX becomes highly active.

The player may:

* buy
* ignore it
* trade another stock
* lose money
* make money

Never force the outcome.

This scenario exists to test that:

news → market behavior → chart → trading → portfolio

works as one coherent system.

---

# 47. ONBOARDING

Teaching should happen largely through gameplay.

Example:

First day:

NPC/email explains:

"Market orders prioritize execution. Limit orders prioritize price."

Then player actually experiences spread/slippage.

Do not overwhelm the player with a textbook.

Introduce concepts progressively.

---

# 48. PROGRESSION PHASES

## Stage 1 — Broke Retail Trader

Capital:
$5,000–$10,000

One monitor.

Basic broker.

Tiny apartment.

Player learns fundamentals.

---

## Stage 2 — Developing Trader

Better monitor.

Scanner.

More capital.

Better data.

Larger universe of stocks.

---

## Stage 3 — Serious Trader

Multiple monitors.

Professional data.

Advanced order types.

Shorting.

Larger position sizes.

---

## Stage 4 — Professional

Premium office.

Advanced analytics.

High buying power.

More asset classes.

Professional contacts.

---

## Stage 5 — Market Veteran

Large capital.

Sophisticated risk controls.

Potential company/fund structure as an endgame system.

Do not build Stage 5 until the core gameplay proves fun.

---

# 49. FUTURE FEATURES

Architect for but DO NOT initially implement:

* options
* futures
* forex
* crypto
* extended office staff
* multiple apartments
* streaming career
* hedge fund
* multiplayer
* leaderboards
* historical market scenarios
* Steam Workshop
* modding
* controller support
* Steam achievements

Stocks come first.

---

# 50. GAME FEEL

Small details matter.

When the market opens:

* subtle clock change
* market-open audio cue
* scanners begin moving
* stock prices update faster
* charts become active
* notifications increase

When holding a large losing position:

* room remains physically unchanged
* but audio and player feedback can subtly create tension

When the market closes:

* terminal settles
* end-of-day statistics appear
* player can review trades
* outside lighting reflects late afternoon/evening

Trading should feel like an event occurring inside the physical world.

---

# 51. NO FAKE DIFFICULTY

Never make the market intentionally move against the player's specific order simply because they placed it.

The simulation should operate independently.

Player success must come from:

* pattern recognition
* information
* discipline
* risk control
* experience
* probabilistic outcomes

Losses should feel explainable after the fact even when unpredictable beforehand.

---

# 52. DEVELOPMENT RULES FOR CODEX

Before modifying files:

1. Inspect the existing repository.
2. Identify Unity version.
3. Inspect installed packages.
4. Inspect existing scenes/scripts.
5. Do not replace working systems unnecessarily.
6. Preserve existing conventions when reasonable.

When implementing:

* make incremental changes
* keep the project compiling
* avoid massive monolithic scripts
* use clear names
* comment reasoning, not obvious syntax
* remove dead experimental code
* do not duplicate systems
* do not silently introduce paid dependencies
* do not use copyrighted assets taken from other games

When an asset is unavailable:

USE A PLACEHOLDER.

Examples:

* Unity primitives
* simple materials
* basic icons
* procedural placeholder UI
* temporary sounds clearly marked TODO

Never block core implementation because art is missing.

---

# 53. USING THE GAME STUDIO PLUGIN

If the Game Studio plugin is available in Codex, use it selectively for:

* architecture review
* game-loop design
* UI/HUD planning
* asset workflow planning
* playtesting strategy
* QA checklists

However:

**This is a UNITY project.**

Do not convert the project into Phaser, Three.js, React Three Fiber, or another browser engine just because Game Studio specializes in browser development.

Use the plugin's design principles where helpful while implementing the actual game in Unity/C#.

---

# 54. IMPLEMENTATION PHASES

Follow these phases.

## PHASE 0 — Repository Assessment

Inspect everything.

Produce:

* project structure summary
* Unity version
* package list
* existing relevant systems
* risks
* recommended first implementation steps

Then proceed unless genuinely blocked.

---

## PHASE 1 — Technical Foundation

Implement:

* folder organization
* game bootstrap
* game clock
* seeded RNG
* core data models
* 10 security definitions
* market simulation
* candle aggregation
* basic tests

Acceptance criteria:

Market can simulate a full trading day without any UI.

---

## PHASE 2 — Trading Engine

Implement:

* account
* portfolio
* positions
* orders
* execution
* market orders
* limit orders
* P&L
* ledger

Acceptance criteria:

Automated tests prove trades and account calculations work.

---

## PHASE 3 — Trading UI

Implement:

* watchlist
* selected security
* quote
* chart
* volume
* order entry
* open orders
* positions
* P&L

Acceptance criteria:

A developer can trade the simulated market entirely through UI.

---

## PHASE 4 — First-Person Apartment

Implement:

* first-person controller
* small apartment
* desk
* chair
* computer
* sit interaction
* workstation mode
* return to FPS mode

Acceptance criteria:

Player can physically approach desk, sit down, trade, stand up, and walk away.

---

## PHASE 5 — News

Implement:

* news definitions
* scheduled news
* random news
* market impact
* news application

Acceptance criteria:

A news event can visibly alter the behavior of a target security.

---

## PHASE 6 — Day Loop

Implement:

Morning → Premarket → Open → Session → Close → Evening → Sleep → Next Day.

Acceptance criteria:

Player can complete several consecutive trading days.

---

## PHASE 7 — Economy

Implement:

* rent
* internet
* subscriptions
* purchases
* simple equipment upgrades

Acceptance criteria:

Trading performance has consequences outside the brokerage account.

---

## PHASE 8 — Polish Vertical Slice

Add:

* audio
* lighting
* animations
* improved UI
* tutorial
* save/load polish
* settings
* performance checks
* bug fixes

Only after this stage should major new features be added.

---

# 55. DEFINITION OF A SUCCESSFUL PROTOTYPE

The prototype succeeds if a player can:

1. Wake up in an apartment.
2. Walk to their desk.
3. Sit down.
4. Read market news.
5. Identify an active stock.
6. Examine its chart.
7. Place an order.
8. Receive a believable fill.
9. Watch the position fluctuate.
10. Exit the trade.
11. See correct profit/loss.
12. Finish the trading day.
13. Pay expenses.
14. Save.
15. Sleep.
16. Wake up to a different market the next day.

That loop needs to be compelling **before expanding the game.**

---

# 56. QUALITY BAR

Do not consider something complete merely because:

"the code compiles."

For each system verify:

* Does it work?
* Is the behavior understandable?
* Does the UI communicate it?
* Can it be tested?
* Can it be expanded?
* Does it create interesting decisions?
* Does it work with save/load?
* Does it remain independent from unrelated systems?

Prioritize:

**functionality → reliability → usability → game feel → visual polish**

in that order.

---

# 57. FIRST TASK

Begin by examining the repository and establishing the vertical-slice architecture.

Then create the foundation for:

1. GameClock
2. SeededRandomService
3. SecurityDefinition
4. SecurityRuntimeState
5. MarketSimulation
6. MarketIndex
7. PriceEngine
8. Candle/CandleAggregator
9. 10 fictional stocks
10. automated market simulation tests

The market must be able to run headlessly without the first-person world.

After those tests work, begin:

11. Account
12. Order
13. Position
14. Portfolio
15. ExecutionEngine
16. Market and limit order tests

Do not attempt the apartment, NPCs, or visual polish until the market/trading foundation functions correctly.

After completing each major phase:

* compile
* run relevant tests
* report what was added
* identify remaining TODOs
* identify any technical debt
* continue to the next logical phase

The ultimate goal is not simply to create a stock-chart application.

The goal is to create a **compelling first-person life simulator where the player's profession happens to be day trading, and where becoming a successful trader changes the physical world around them.**
