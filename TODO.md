# TODO

## Done
- [x] Phase 0: new Unity 6 project, git, docs
- [x] Phase 1: clock, seeded RNG, 10-stock universe plus index, factor price engine, sessions, overnight gaps, candles (1m/5m/15m/1h/1D). Headless full-day tests
- [x] Phase 2: account, ledger, positions, portfolio, market/limit orders, execution, commissions. Tests
- [x] Phase 3: UI Toolkit trading terminal. Watchlist, quote, chart (candles, volume, VWAP, avg cost, fill markers, crosshair, zoom/pan, 5 timeframes), order ticket, positions/orders/fills with close and cancel, account bar, pause/speed. PlayMode acceptance test trades through the UI

- [x] Phase 4: URP, Input System, FPS controller, primitive apartment (desk, chair, monitor, bed, kitchenette), raycast interaction with HUD prompt, workstation sit/stand with camera glide, terminal on the in-world monitor while standing, walking vs seated time scale. PlayMode acceptance test with simulated input

- [x] Phase 5: news engine (29 templates covering the spec's catalyst types plus flavour), scheduled plus random news, catalysts (permanent move, overreaction, attention), onboarding 8:15 APEX headline, News panel, watchlist dots, chart markers. Tests for isolation, direction, sector/market scope, determinism and rate

- [x] Phase 6: day reports (`TradingDayRecorder`), session summary at the close, bed plus sleep sequence with recap, wake on the next trading morning (weekends skipped), HUD clock/day number, window daylight cycle. PlayMode test plays three consecutive days

## Next
- [ ] **Save/load** (vertical-slice requirement, spec §41): explicit save models for account, ledger, positions, orders, day reports, clock, plus market state (the RNG state must be serializable for exact resume); autosave on sleep
- [ ] Phase 7 (Economy): rent, internet, subscriptions, purchases, simple equipment upgrades

## Later phases (do not start early)
- Phase 5: news catalysts. Hook in via the activity level (vol/volume) plus fair-value jumps in `PriceEngine`
- Phase 6: day loop · Phase 7: economy · Phase 8: polish
- Save system: explicit save models for account/ledger/portfolio/orders plus market state (the RNG state must be serializable for exact resume)

## Known debt / decisions to revisit
- No market holidays (`MarketSchedule.IsTradingDay`)
- No market impact from player orders. Fine at retail size; revisit for large accounts
- No queue model for limit orders at the bid/ask. Conservative (they wait for a trade-through)
- Day orders only; no GTC or extended-hours flag
- No regime system yet: `MarketDailyDrift/Volatility` are static config
- Content tuning: high idiosyncratic vol (e.g. CYRA 6%) makes sector/market moves hard to see in small caps. Revisit when news and regimes exist
- "Nortek" is a real company name (Nortek Inc.). Consider renaming NRTK's company before release
- UI polish (Phase 8): fill markers can sit under candle bodies; no price-flash on ticks; no trading hotkeys yet; the index isn't chartable
- The Close button outside regular hours posts a limit at the bid. It may not fill if the bid drops
- UI refresh allocates strings every 0.1s. Fine now; profile in Phase 8
- Apartment is placeholder primitives: no door, light switches or appliance interactions yet (spec §5 lists them; add when a phase needs them). The desk lamp shade floats (no stand)
- Active input handling is "Both". Test switching to Input System only (UI Toolkit runtime input) before release
- Escape while seated always stands up. A pause/settings menu will need its own key or a stack
- With the default seed (18492) the onboarding APEX headline draws a modest +3% move (the expected move is about +10%). Honest randomness; retune severity or the seed if the tutorial needs a clearer reaction
- Day loop: no weekend gameplay (slept through); no fatigue/sleep-quality effects (psychology system, spec §9); sleeping mid-afternoon after 4 PM skips after-hours trading without asking
- News: no earnings calendar or expectations model yet (spec §21); no news alerts while away from the desk (phone, later); headlines have no body text
