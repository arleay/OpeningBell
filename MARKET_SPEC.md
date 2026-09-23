# Market & Chart Spec (order-flow market + trading platform chart)

Source of truth for the market rebuild and the chart upgrade (requested 2026-09-22). Condensed from the player's
brief; every requirement is kept. Section A is the brief, B is the audit of what existed, C is how it maps onto
this codebase, D is the stage checklist.

---------------------------------------------------------------------------------------------------------------
## A. The brief

### Principle
Price is NOT random candles and NOT scripted TA. No concept (ICT, S/R, RSI, MAs, supply/demand, FVG, order blocks)
ever determines future price. Price results from competing simulated forces:

    market conditions → participant behaviour → orders → liquidity → order flow → execution → price
    → technical structures emerge → participants react → repeat

Concepts influence *some participants'* interest; they never command price. Sometimes setups work beautifully,
sometimes partly, sometimes they fail completely. Clean days and ugly days both happen.

### Must keep working
Seeded reproducible randomness (same seed + start state + player actions → same market), exact save/load,
headless tests, the news engine and sector tilts, the time-of-day volume curve, player trading, multiple stocks,
the index, speed controls up to 120×.

### Performance
~10 stocks + 1 index at up to 120×. Lightweight aggregated model, ~one internal step per simulated second or two.
Batching allowed only if OHLC, volume, stop triggers, news and player orders stay correct.

### Participants: ~8 aggregated groups (not 25)
1. Institutions: large size, executed over time (meta-orders: buy 40k, pause, 65k, absorb sellers...), VWAP-style,
   avoid impact, accumulate/distribute, respond to news and market/sector, urgency changes aggression.
2. Market makers / liquidity providers: a lightweight book (several levels a side), spread, depth,
   replenishment, withdrawal under volatility.
3. Momentum / trend traders.
4. Mean-reversion traders (VWAP, RSI extremes...).
5. Breakout traders.
6. Level traders: S/R, VWAP, prior highs/lows, supply/demand, FVGs, order blocks, opening range, premarket range.
7. Retail / noise traders.
8. Resting conditional orders (stops, take-profits).

### Price formation
Aggressive buying vs available ask liquidity, aggressive selling vs bid liquidity. Excess walks the book;
balance consolidates. Never `price += random()` or "go to $150". Randomness only at the micro level: arrival
timing, sizes, participant decision variation, replenishment, spread, noise traders.

### Regimes and day types
Persistent regimes (strong/weak bull/bear trend, range, low/high-vol range, compression, expansion, breakout,
reversal, panic, recovery); they don't flip every few candles and transition because of flow, volatility,
volume, liquidity, news, failed structure, exhaustion.
Hidden DayProfile per day: trend up/down, range, gap-and-go, gap-and-fade, morning reversal, high-vol chop,
low-volume grind, news day, panic selloff, recovery, squeeze. It sets conditions, never a candle sequence.
Never exposed to the player.

### Multi-timeframe structure
Daily, 4H, 1H, 15m, 5m, 1m each track swings, HH/HL/LH/LL, range/trend state, volatility. They may disagree
(daily bull, 1H correcting, 5m reversing) and that is normal.

### Market memory
Previous day high/low/close/open, previous week high/low, premarket high/low, opening range high/low, swing
highs/lows, high-volume regions, old breakout levels, supply/demand zones, gaps, round numbers. Old levels fade
unless reinforced.

### Support / resistance
Emerges from history (reactions, volume, swings, round numbers, previous-session levels, congestion, prior
breakouts), has a strength score, influences limit placement and decisions. Outcomes: rejection, shallow bounce,
sweep, breakout, breakout-retest, consolidation, complete failure. Never guaranteed.

### Supply / demand
Zones come from real imbalance: low, high, creation time, creation volume, remaining interest, retests,
strength, timeframe. Retests consume interest. First touch not guaranteed. States: untouched, partially
mitigated, fully mitigated, invalidated.

### ICT / SMC (derived, never causal)
Detect FVGs, displacement, liquidity voids, equal highs/lows, buy/sell-side liquidity, BOS, CHoCH/MSS, order
blocks, breakers, mitigation, premium/discount, PDH/PDL, session highs/lows, previous-week levels. Some traders
react. FVG outcomes: untouched, partial, 50%, full, reaction, trade-through, irrelevance. No `FVG = reversal`.

### Liquidity, stops, sweeps
Stops accumulate as real conditional orders around equal highs/lows, prior highs/lows, obvious S/R, opening and
premarket ranges, round numbers, swings. Crossing them adds market flow (acceleration). Then either buyers
continue (breakout) or selling absorbs demand (failed breakout). Not predetermined by terminology.

### Indicators
Computed from simulated prices: SMA, EMA, VWAP, (anchored VWAP later), RSI, MACD, Bollinger, ATR, volume,
relative volume, (ADX). Some participants use them (RSI < 30 → more mean-reversion interest) but price can keep
falling.

### Time of day (Eastern)
- Premarket 4:00–9:30: real price action, thin book, wide spreads, low volume, bigger moves per share, gaps, news
  reactions; track premarket high/low/VWAP/volume; more active toward the open. 8:00–9:00 somewhat busier
  (catalysts). 9:00–9:25 volume, institutional prep, liquidity and arrivals up; spreads tighten. 9:25–9:30
  anticipation (queued orders); may move or compress, never forced.
- 9:30 open: the player must FEEL it. Volume, arrivals, participants, discovery, volatility, market orders, stop
  activation and liquidity jump; liquid spreads may tighten while turnover explodes. Any opening behaviour
  (drive up/down, two-way violence, gap continuation/fill, PMH breakout, PML breakdown, sweep, chop) and
  sometimes a quiet open.
- 9:30–9:45 usually the most active; 9:45–10:30 discovery continues (continuation, reversal, ORB, premarket
  retest, VWAP); 10:30–11:30 structure develops, volatility normalises.
- Midday 11:30–1:30: lower volume and volatility, slower arrivals, chop, more mean reversion; news can override.
- 1:30–3:00 activity builds (midday breakout, continuation, reversal, retests).
- Power hour 3:00–4:00: volume, institutions, rebalancing, closing positions, short covering. 3:50–4:00 urgency.
- 4:00 close: closing-auction-like volume, then after hours 4:00–8:00 (thin, wide, earnings spikes).
- Time macros: configurable windows (9:30–9:45, 9:45–10:30, 10:30–11:30, 11:30–13:30, 13:30–15:00, 15:00–15:50,
  15:50–16:00) that modify volume, volatility, liquidity, institutional activity, arrival rate and spread. Never
  directional ("10 AM changes participation", not "10 AM reverses").

### Volume, volatility, personalities, correlation
- U-shaped volume that varies day to day; depends on stock, day type, news, regime, volatility, market.
  Relative volume (0.7×, 4.5×) matters.
- Volatility clusters (compression → breakout → expansion → retrace → consolidation); no big/tiny alternation.
- Personalities: ADV, spread, float, volatility, beta, sector, institutional vs retail participation, news
  sensitivity. Large caps smooth and deep; small caps thin, wide, squeezy, violent failures.
- Every stock = broad market + sector + company flow; keep and deepen sector tilts.
- News changes participants (bias, retail activity, volatility, volume, spread, liquidity, urgency), not candles;
  strong news can overwhelm technical setups.

### Player orders
Market, limit, stop, (stop-limit), take-profit, stop-loss. Market orders fill against bid/ask with spread,
slippage and partial fills; retail size negligible impact, very large size can move price. Stops trigger
properly and fill at available liquidity when price gaps through them (stop 50.00, gap 50.20 → 49.70 fills
near 49.70). Brackets: TP + SL linked OCO.

### Chart (a serious trading tool)
- Customisation: bull/bear candle, wick, background, grid, crosshair, volume and indicator colours; themes plus
  custom; saved between sessions.
- Drawing tools: horizontal line, trendline, ray, rectangle (zones: FVG, OB, supply/demand, S/R, ranges; extend
  right), Fibonacci retracement; later vertical line, text, arrow, channel. Create, select, drag, resize,
  recolour, delete. Saved per symbol, exactly.
- Position line: `AVG 102.42 | +$437 | +2.1%` with quantity.
- TP/SL lines: draggable, synchronised with the real orders (dragging modifies the order).
- Indicators menu: add/remove EMA, SMA, VWAP, Bollinger, RSI, MACD, Volume, ATR (later stochastic, ADX, anchored
  VWAP); settings for length, colour, thickness, visibility; multiple EMAs. Overlays on the price pane;
  RSI/MACD/volume in resizable panes below.
- Optional ICT overlays (toggleable, default configurable): FVG zones that change when mitigated, BOS, CHoCH/MSS,
  swept highs/lows, equal highs/lows, order blocks if reliable. Liquidity lines: PMH/PML, PDH/PDL, ORH/ORL.
- Market clock with session status: PREMARKET, MARKET OPEN, MIDDAY, POWER HOUR, AFTER HOURS, CLOSED.
- Speeds 1×–120×; rendering may batch, the simulation may not skip stops, fills, OHLC, volume, news, structure.

### Save / load
Continuing after a load behaves identically to never quitting. Persist RNG state, regimes, day profiles,
participant state, institutional intentions, the book, resting conditional orders, market memory, zones, current
candles, news, market/sector state, player orders, positions, drawings, indicator configuration, chart settings.

### Testing (no runtime referee)
Never manipulate live outcomes ("FVG worked too often, force a failure"). Instead, headless statistics over
hundreds of sessions: daily return distribution, ranges, open/midday/close volatility, volume curve, S/R reaction
rates, FVG interaction, breakout success/failure, stop-run frequency, trend/range day frequency, gap behaviour.
Tune participants and parameters, never outcomes. Required tests: determinism, save/load continuation, gap-through
stops, 9:30 participation jump, performance (10 stocks + index at 120×), market statistics.

### Debug overlay (developer only)
Regime, day type, volume/volatility multipliers, session, institutional pressure, momentum / mean-reversion /
breakout participation, retail activity, bid/ask pressure, major liquidity, PMH/PML, PDH/PDL, opening range,
supply/demand zones, detected FVGs, sector and market bias. Never visible in normal play.

### Don't
Rewrite blindly, remove seeded randomness, break save/load, drop news or sector influence, generate candles
independently, let RSI / support / resistance / FVG / order blocks control price, force setups to fail, script
textbook patterns, make 25 agent types, build an exchange too slow for 120×, fake stops, make every open or every
stock identical.

---------------------------------------------------------------------------------------------------------------
## B. Audit (before the rebuild)

| System | Where | Notes |
|---|---|---|
| Market loop | `Market/MarketSimulation.cs` | Fixed 2 s ticks aligned to a grid (chunking-independent), closed periods skipped, sessions, day roll. |
| Price model | `Market/PriceEngine.cs` | log price = fair + deviation. Fair: βm·market + βs·sector + σ·z (+ news). Deviation: mean-reverting noise + weak momentum. Log-OU "activity" for vol/volume clustering. One trade print per tick, printed at bid or ask by direction. **No memory of levels, no book, no participants, no day types, no stops: this is why price drifts aimlessly.** |
| Time of day | `Market/IntradayProfile.cs` | U-shape (2u−1)² plus an opening burst; flat premarket / after hours. |
| Quotes | `PriceEngine.ComputeQuote` | Spread from bps × vol; displayed size from ADV. |
| Candles | `Market/Candles/*` | 1m, 5m, 15m, 1h, 1D from prints; daily = regular session only. |
| News | `Market/News/*` | Scheduled + Poisson random, own RNG stream; security/sector/market scope; sector tilts; moves fair value (immediate + delivered) plus transient overreaction and an attention (activity) boost. |
| Sectors/market | `PriceEngine.Tick` | Shared market and sector factor shocks from the market RNG; index = market factor. |
| Randomness | `Core/SeededRandom*` | Named streams: `market`, `security:TICKER`, `news`. Per-security streams keep one listing from changing another. |
| Execution | `Trading/Execution/*` | Player orders fill against Level 1 with synthetic hidden depth; never move price. Market + limit only, day orders. |
| Save/load | `Market/Save/*`, `Trading/Save/*` | Bit-exact doubles, RNG states, quotes, day stats, 1m + 1D candles, news queue/feed; orders, fills, positions. |
| Chart | `UI/Chart/ChartView.cs` (Painter2D) | Candles, volume, VWAP, avg-cost line, fill markers, news markers, crosshair, zoom/pan, 5 timeframes. Hard-coded colours. |
| Tests | `Tests/EditMode/Market/*` etc. | Determinism across chunking, adding a listing doesn't change others, correlation, U-shaped volume, daily vol/ADV match spec, extended spreads wider, news isolation/direction/rate, save/load exact resume. |

---------------------------------------------------------------------------------------------------------------
## C. Design in this codebase

**Keep:** the tick grid (2 s, "about one step per second or two"), sessions, candles, news engine, RNG streams,
the market/sector/idiosyncratic factor shocks. They become the *information* layer.

**Two layers per stock:**
1. *Information (fair value)*: the existing factor model: market + sector + idiosyncratic shocks, the day's hidden
   drift, and news. It is what informed participants believe the stock is worth. Correlation and calibrated daily
   volatility come from here, as before.
2. *Order flow (price)*: `OrderFlowEngine` in `Market/Flow/`. Each step, the participant groups produce
   aggressive flow (signed shares) and passive liquidity; the book absorbs it; price is the result. Participants:
   - Institutions: meta-orders (parent size, side, urgency, schedule along the volume curve, price tolerance)
     that arrive with news, factor moves and the day profile; part aggressive, part passive (absorption walls).
     Order splitting is what makes real trends persistent.
   - Market makers: depth per side from volatility, time of day and inventory (they lean against inventory, which
     is a natural source of mean reversion); withdraw after violent moves.
   - Informed/value flow: trades toward fair value (how news and factors reach price).
   - Momentum/trend: multi-horizon returns vs volatility.
   - Mean reversion: distance from VWAP, RSI extremes; stronger midday and in ranges.
   - Breakout traders: react to breaks of the opening range, premarket range, day and swing extremes.
   - Level traders: resting walls at remembered levels (strength-scored) and stops just beyond them.
   - Retail/noise: fat-tailed random flow, FOMO with big moves, panic.
   - Stops: clusters at prices beyond levels; crossing converts them to market flow the same step.
   Book: best bid/ask plus a depth curve (shares per tick away from the touch), discrete walls and stop clusters.
   Aggressive excess walks it; one print per step with the step's traded high/low kept for stops and wicks.
3. *Memory and structure* (`Market/Structure/`): levels (PDH/PDL/PDC/PDO, PWH/PWL, PMH/PML, ORH/ORL at 5/15/30 m,
   swings, round numbers, HVNs) with decaying strength; supply/demand zones from absorption and displacement
   origins; FVG and structure detection per timeframe. Participants read it; it never writes price.
4. *Day profile and regime* (`Market/Flow/DayProfile.cs`): drawn at the day roll from the stock's own stream,
   leaning on the market's day profile; sets participation weights, relative volume, volatility, drift and
   reversal hazards. Regimes persist with minimum durations and transition on conditions.
5. *Time macros*: configurable windows layered on `IntradayProfile` (which keeps the U-shape), plus premarket
   ramp, 9:25 anticipation, opening and closing auction prints.

**Determinism:** every stochastic draw comes from the existing named streams, in a fixed order per tick; new
state is saved bit-exactly. Player orders are inputs (they may consume book liquidity).

**Player trading:** stop and stop-limit orders, TP/SL brackets (OCO), stops triggered by the step's traded range
and filled against the book (gap-through fills at the market, not the stop). Large orders consume liquidity.

**Chart:** `ChartView` gains a theme model, drawing layer (per-symbol, saved), indicator engine + panes, position
and bracket lines wired to the order manager, optional ICT/liquidity overlays read from the structure layer, and a
developer overlay behind a debug flag.

---------------------------------------------------------------------------------------------------------------
## D. Stages (keep the game playable after each)

Market:
- [x] M1 Audit + spec (this file)
- [x] M2 Day profiles (11 types) and persistent regimes (range, compression, expansion, trend)
- [x] M3 Lightweight book: depth curve, level walls, stop clusters, institutional resting orders, liquidity withdrawal
- [x] M4 Participant groups: noise/retail+FOMO, value/informed, institutions (meta-orders), momentum, mean reversion, breakout, news traders, index arbitrage, resting stops
- [x] M5 Participants → flow → book walk → price (replaces the deviation noise)
- [x] M6 Market memory: PDH/PDL/PDC, previous week, premarket range, opening range (5/30 m), swings with equal-high merging, round numbers, day extremes
- [x] M7 Stop orders: resting conditional flow in the market; player stop/stop-limit (trigger on the tick range, regular session only, gap-through fills at the market), brackets (OCO, good until cancelled), price edits for dragging lines, square-root market impact for large player orders
- [ ] M8 Supply/demand zones and liquidity concentration
- [x] M9 Derived ICT/SMC detection (`StructureDetector`: FVGs with fill fraction, swings, BOS/CHoCH, sweeps, equal highs/lows); not yet read by participants
- [ ] M10 Time macros, premarket ramp (opening and closing crosses done)
- [ ] M11 News and sectors feed participants
- [ ] M12 Statistics suite and tuning (harness + calibration done: `MarketStatisticsTests`, `TestResults/market-stats.txt`, `market-*.png`); debug overlay

Chart:
- [x] C-A Colours and themes: 5 presets + custom colours per part, saved as a preference
- [x] C-B Drawing tools: horizontal line, trendline, ray, rectangle (extend right), Fibonacci, vertical line; select, drag, resize, recolour, width, delete; saved per symbol in the game save
- [x] C-C Position line with quantity and live $ / % P&L
- [x] C-D Draggable TP/SL (and limit/stop) lines on the real orders; + TP/SL creates a bracket; STOP in the order ticket
- [x] C-E Indicators menu: EMA, SMA, VWAP, Bollinger, RSI, MACD, ATR, Volume; period, colour, width, visibility; overlays on price, oscillators in panes
- [x] C-F Optional overlays (all off by default): FVGs (fade as filled), BOS/CHoCH, sweeps, equal highs/lows, liquidity lines; developer view (F10, dev builds only); session badge shows MIDDAY / POWER HOUR
