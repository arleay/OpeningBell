# TODO

## Done
- [x] Phase 0: new Unity 6 project, git, docs
- [x] Phase 1: clock, seeded RNG, 10-stock universe plus index, factor price engine, sessions, overnight gaps, candles (1m/5m/15m/1h/1D). Headless full-day tests
- [x] Phase 2: account, ledger, positions, portfolio, market/limit orders, execution, commissions. Tests
- [x] Phase 3: UI Toolkit trading terminal. Watchlist, quote, chart (candles, volume, VWAP, avg cost, fill markers, crosshair, zoom/pan, 5 timeframes), order ticket, positions/orders/fills with close and cancel, account bar, pause/speed. PlayMode acceptance test trades through the UI

## Next: Phase 4 (First-person apartment)
- [ ] URP package plus pipeline asset (not installed yet); decide Input System vs legacy Input Manager (legacy is active now)
- [ ] FPS controller, small apartment (primitives), desk, chair, computer
- [ ] Sit interaction leads to workstation mode (the terminal becomes interactive); stand up returns to FPS
- [ ] Gate `TradingTerminal` behind workstation mode (currently always on screen)

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
