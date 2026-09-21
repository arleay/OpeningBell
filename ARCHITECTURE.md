# Architecture

Unity 6000.6.2f1. Vision/requirements: `PROJECT_SPEC.md`.

## Assemblies (dependency direction →)

| Assembly | Folder | Unity refs | Contents |
|---|---|---|---|
| `OpeningBell.Core` | `Scripts/Core` | none | `SeededRandom`, `SeededRandomService`, `GameClock` |
| `OpeningBell.Market` | `Scripts/Market` | none | specs, schedule, price engine, simulation, candles |
| `OpeningBell.Trading` | `Scripts/Trading` | none | orders, execution, positions, ledger, account |
| `OpeningBell.Runtime` | `Scripts/Runtime` | yes | ScriptableObjects, `GameBootstrap` (composition root) |
| `OpeningBell.Tests.EditMode` | `Tests/EditMode` | editor | NUnit tests (InternalsVisibleTo on Market/Trading) |

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

## Testing

`./run-tests.ps1` (close the Editor first) runs EditMode tests headlessly and prints failures. `-Filter` takes a test or class name.
