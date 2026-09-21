# TODO

## Done
- [x] Phase 0: new Unity 6 project, git, docs
- [x] Phase 1: clock, seeded RNG, 10-stock universe plus index, factor price engine, sessions, overnight gaps, candles (1m/5m/15m/1h/1D). Headless full-day tests
- [x] Phase 2: account, ledger, positions, portfolio, market/limit orders, execution, commissions. Tests

## Next: Phase 3 (Trading UI)
- [ ] Choose UI Toolkit vs uGUI for the terminal (the panels must later map onto multiple in-world monitors)
- [ ] Watchlist, selected security, L1 quote, candlestick chart plus volume (and VWAP), order entry, open orders, positions, P&L
- [ ] Add a camera and UI to `Scenes/Main.unity`

## Later phases (do not start early)
- Phase 4: URP package plus pipeline asset (not installed yet), FPS controller, apartment, workstation mode
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
