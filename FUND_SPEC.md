# Hedge fund and office expansion (FUND_SPEC)

Source: the user's hedge fund / office tycoon brief (2026-09-28, 30 sections). This file condenses it, records how it
plugs into what already exists, and tracks the phases. The brief is the scope; nothing here is a reduction of it.

## 1. Integration points (phase F1 audit)

| Need | Existing system | How the fund uses it |
|---|---|---|
| Market, orders, execution, impact | `MarketSimulation`, `OrderManager`, `ExecutionEngine`, `IMarketData.ReportAggressiveFlow` | Every employee trades through their own `Account` + `OrderManager` on the same market (like `PropDesk`). Aggressive fills report flow, so impact is shared. New: `LiquidityShare` makes the company's desks eat the same hidden book depth within a tick. |
| Money, accounts | `Account`/`AccountLedger` (brokerage), `EconomySystem` bank | Company operating cash is a new append-only `CompanyLedger`. Owner contributions and withdrawals move money between the personal bank and the company, explicitly. |
| Progression | None formal (day reports, prop payouts) | New `CareerProgress` milestone: completed trading days + proven profit (personal + prop payouts), configurable. |
| Property | `Estate`, `HomeSpec`, `HomeWorld.Buy` | The office floor is a `HomeSpec` (`HomeKind.Office`, id `harborview_office`): furniture placement, delivery, storage and selling work unchanged. Leased (company pays rent) or bought. |
| Furniture, desks, monitors | `HomeCatalog`, `Belongings`, `Carrier` placement | Workstations are placed store items. `WorkstationRules` (pure) checks desk, chair, computer, monitors, peripherals, power, network and access. New catalog items for the office (coffee station, water cooler, reception desk, meeting table, book shelf). Company-owned items (`OwnedItem.Owner`). |
| Computer / websites | `BrowserApp` + `BrowserPage` sites | Kell Valley Business Registry (`kvregistry.gov`) to form the fund; Ledgerline fund portal (`ledgerline.com`) as the company dashboard. |
| NPCs | `NpcBody` (Tiny characters, shared animator), `CharacterStyle` | Employees are `NpcBody`s in suits (75% dark), with persistent looks. New animator states: sit enter/exit, seated typing and reading (upper layer). Built-in NavMesh (`UnityEngine.AI` module) baked at runtime for the tower. |
| Vehicles, traffic, parking | `TrafficSimulation`, `TrafficView`, `ParkingKind`, Harborview P1 garage, `VehicleLibrary` car ladder | Employee cars are traffic cars with a destination (new routing), then a scripted garage path to a reserved bay on P1. Cars come from the game's models, chosen by each employee's wealth. |
| Time, sessions, saving | `GameClock`, `MarketSchedule`, `SaveGame` (additive blocks) | Business time is the market-local clock; `Fund.AdvanceTo(now)` in fixed one-minute steps plus the market's tick events. Saved as `SaveGame.HasFund/Fund` (additive; old saves load with no fund). |
| Interaction | `Interactable`, `PlayerInteractor`, HUD | Employees are `Interactable` ("Manage John Carter"); [E] opens an in-world management panel. |

Missing foundations built here (called out, not faked): career milestone, company ledger, routed traffic cars, NavMesh
for NPCs, NPC elevator riding, seated work animations, company-card purchasing.

## 2. Rules

- The fund simulation is pure C# (`OpeningBell.Fund`, `noEngineReferences`), money in `decimal`, randomness from the
  world seed through a counter-based stream (`CounterRandom`: stateless, so saves hold no RNG state and results don't
  depend on how time is chunked). Unity only displays it and walks bodies about.
- Business state never depends on rendering: bodies follow the simulation (and catch up); the simulation never waits
  for a body. Nothing is decided by frame rate or by whether the office is loaded or watched.
- Employees read only what the player can see: quotes, candles, VWAP, day high/low, published news. Never the flow
  model's hidden state, never the future.
- No invented profit: all P&L comes from fills. Hard limits (capital, loss lockout, position limits, instruments,
  authorisation) are enforced by an order gate and a tick monitor, never by the employee's goodwill.

## 3. Money model

- **Operating cash**: the company's cash (`CompanyLedger`). Formation: $50,000 fee (spent) + $100,000 starting cash
  (transferred). Configurable (`FundConfig`).
- **Allocated capital**: cash moved into an employee's desk account. **Desk equity** = desk cash + open P&L.
- **Company equity** = operating cash + Σ desk equity − accrued liabilities (wages, commissions, bills due).
- **Collectible** (per desk) = realized desk cash above its allocated base that isn't posting margin:
  `max(0, min(Cash − Base, BuyingPower))`. Losses must be earned back first (the base is the floor). Collect moves it
  to operating cash: equity is unchanged (tested). Collect All uses the same rule.
- **Commission** on new positive cumulative net realized P&L (after commissions and fees) above the employee's
  high-water mark. Accrued at each realized fill, paid with payroll. Recovering a loss pays nothing.
- **Accrual vs payment**: expenses are recognised when accrued (wages per minute on site, commissions per fill, bills on
  their date); paying settles the liability. Never counted twice.
- **Payroll**: weekly, Friday after the close. Unpaid wages become overdue (delinquency: morale falls daily,
  reputation suffers, resignations follow; nothing is forgiven silently).
- **Closure**: the owner can wind down, or 15 business days of delinquency forces it: trading stops, positions are
  closed through the market, capital recalled, liabilities paid (the owner's bank covers any shortfall, stated), the
  rest returned to the owner.

## 4. People

- Applicants arrive over time per job listing (reach and reputation scale how many and how experienced). Each is a
  persistent person: name, look, experience, specialty (sector), strategy, skills, traits, pay expectations, start
  date, history. The pool doesn't reshuffle.
- Skills (0–100): Analysis, Timing, Trade management, Reward:risk planning, Risk management, Self-control, Execution,
  Adaptability, Specialisation, Stamina, Learning, Leadership. Traits: Ambition, Loyalty, Sociability, Composure, Greed,
  Caution, Flash (spending).
- Shown stats are labelled estimates: historical win rate and reward:risk (skill + noise), estimated win rate under
  reference conditions (model), and recorded results over a labelled sample.
- Negotiation: hourly, commission, or both. Offer value vs expectation (experience, ambition, interest, reputation);
  accept / counter / reject / withdraw after repeated lowballs. Signed terms are locked.
- Schedule (market clock): arrive ~8:30 ± personal jitter, prepare, trade 9:30–4:00 with breaks, end-of-day tasks,
  leave ~4:15–4:40. Weekends off. States: Applicant, Hired (awaiting start), Commuting, Arriving, Waiting for
  workstation, Preparing, Trading, On break, Training, Risk locked, Leaving, Off duty, Former.
- Satisfaction responds to pay, pay reliability, workstation quality, waiting without equipment, workload, training,
  amenities, reputation and stress; each change has a visible reason. Warnings → raise requests → resignation notice
  (days to respond) → resignation.

## 5. Trading brain

- Strategies on public data: Breakout (opening range / session extremes), VWAP reversion, Trend pullback, News
  momentum, Scalping. Each scores setups; perception noise falls with Analysis (and rises with fatigue and stress);
  Timing sets patience; Reward:risk planning filters targets; Risk management sizes; Trade management moves stops and
  takes profit; Execution sets order-entry delays and mistakes; Adaptability handles regime changes; Specialisation
  helps in the employee's sector/strategy.
- Self-control drives visible behaviour: overtrading and revenge sizing after losses, chasing late entries, early
  profit-taking, holding losers (inside the hard stop), greed after a good day. Each is logged ("Trade frequency rose
  after three consecutive losses").
- Orders: entries (limit or market), then a bracket (take-profit + stop). Brackets keep working through breaks.
- Hard limits: allocation (desk buying power), max contracts per position, max open positions, max risk per trade
  (clamped after behaviour), daily loss lockout (flatten + locked for the day), approved instruments and strategies,
  authorisation, company exposure cap per symbol and per sector. Overnight holding isn't offered: the broker settles
  every contract flat at the close.

## 6. Training

Categories with levels, price, duration and effect: Market analysis, Entry timing, Trade management, Reward:risk
planning, Risk management, Self-control, Execution, Adaptability, Specialisation, Focus & stamina, Learning efficiency
(capped), Leadership (mentoring up to N juniors). First market analysis: $50,000, 10 in-game minutes, +5 points of
estimated baseline win rate at reference skill; later levels cost more, take longer, gain less near the cap. Queued
until the employee is at a valid desk, in work hours and flat; paid once; progress saved; pauses with the game; resumes
after interruptions. Visible: books on the desk, reading, a progress marker.

## 7. Office floor (Harborview Level 26)

Directly below the penthouse (floor slab under the penthouse's). Residents' lift gets an office stop; the garage (P1)
and lobby reach it without passing the penthouse. Plan: lift lobby and reception (north centre), waiting area,
trading floor (south half, water views) with a fit-out-later expansion wing, private office (NE), meeting/training room
(NW), coffee station and break area (west), restrooms, storage/utility. Finished shell (glass, floors, ceilings,
lights, doors, power and data points), no working furniture. Signage shows the fund's name.

## 8. Parking and commutes

Reserved staff bays on Harborview P1 (south row, signed), overflow in the other P1 bays, then transit (walk from the
bus stop). Cars from the game's model ladder by each employee's savings and spending trait, reviewed monthly (no
supercar from one good day). Seen commutes drive through traffic to the ramp and park; unseen ones resolve at their
time; cars never appear in view.

## 9. Phases

- F1 audit and integration points (this file) ✅
- F2 fund core: config, company ledger, registration, owner transfers, career milestone, save; registry site and
  Ledgerline overview ✅
- F3 office floor: architecture, lift stop, lease/purchase, signage; workstation rules and office catalog items ✅
- F4 recruitment: listings, applicant generation, profiles, negotiation, hiring, persistent people ✅
- F5 real trading: desks, liquidity share, strategies, brain, risk limits, company exposure ✅ (calibrated with
  `TraderCalibrationTests.SetupEdge`: setup quality is built from the features that measurably predict outcomes;
  weak analysts misread one-bar wiggles and place stops badly, impatient traders chase extended moves, fear exits are
  decided once per trade)
- F6 payroll, commissions, collection, ledger, finances, reports ✅
- F7 employee panel on [E], training (costs, queue, time, visible study) ✅
- F8 daily routines on the floor (NavMesh, lift riding, waiting, breaks, coffee), satisfaction, departures ✅ (lift:
  staff appear in the car when its doors open at 26 and step into it to leave)
- F9 parking, routed cars, commutes, employee wealth and cars ✅ except routing through street traffic (a watched
  arrival starts at the top of the ramp) and overflow bays (beyond the five reserved bays, staff come by transit)
- F10 reputation and expansion ✅, notifications grouping ✅, polish, persistence, performance ✅ (unwatched staff are
  placed, not simulated; bays change only out of view); admin roles not started (brief §25: after the core loop)
