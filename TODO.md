# TODO

## Done
- [x] Phase 0: new Unity 6 project, git, docs
- [x] Phase 1: clock, seeded RNG, 10-stock universe plus index, factor price engine, sessions, overnight gaps, candles (1m/5m/15m/1h/1D). Headless full-day tests
- [x] Phase 2: account, ledger, positions, portfolio, market/limit orders, execution, commissions. Tests
- [x] Phase 3: UI Toolkit trading terminal. Watchlist, quote, chart (candles, volume, VWAP, avg cost, fill markers, crosshair, zoom/pan, 5 timeframes), order ticket, positions/orders/fills with close and cancel, account bar, pause/speed. PlayMode acceptance test trades through the UI

- [x] Phase 4: URP, Input System, FPS controller, primitive apartment (desk, chair, monitor, bed, kitchenette), raycast interaction with HUD prompt, workstation sit/stand with camera glide, terminal on the in-world monitor while standing, walking vs seated time scale. PlayMode acceptance test with simulated input

- [x] Phase 5: news engine (29 templates covering the spec's catalyst types plus flavour), scheduled plus random news, catalysts (permanent move, overreaction, attention), onboarding 8:15 APEX headline, News panel, watchlist dots, chart markers. Tests for isolation, direction, sector/market scope, determinism and rate

- [x] Phase 6: day reports (`TradingDayRecorder`), session summary at the close, bed plus sleep sequence with recap, wake on the next trading morning (weekends skipped), HUD clock/day number, window daylight cycle. PlayMode test plays three consecutive days

- [x] Save/load: exact-resume saves (market, news, RNG, account, orders, fills, day reports, clock, player position), atomic writes plus backup, version check, autosave on wake and quit, continue on launch. Tests for exact resume through JSON, file safety and restart-in-scene

- [x] Phase 7: separate bank account, bills plus daily living costs, overdraft fees, brokerage transfers (excluded from trading P&L), store with 6 visible apartment upgrades plus a fiber subscription, Bank/Store apps, HUD bill toasts, saved state. PlayMode test transfers, buys, pays rent and reloads

- [x] Phase 8 (first polish pass): pause menu (save, settings, new game, quit), onboarding via the Mail app (9 scripted emails), placeholder synthesized audio plus ambience, performance baseline and tests, chart marker outline, monitor glow

- [x] Phase 9 (city foundation, `WORLD_SPEC.md`): generated compact city (6 streets, 3 core blocks, outer ring, landmarks), apartment building with hallway and front door, Calder Building (lobby, receptionist with shifts/lunch, mailboxes, directory, restroom, elevator, stairs, floor 2, leasable Suite 204 with a second trading desk), Half Past Nine and Corner Mart (staffed, hours, counter purchases), traffic (lights, stop signs, right of way, yields to people), pedestrians (sidewalk graph, crosswalks, walk signals, benches), sun/day-night, lit windows and lamps at night. Acceptance test walks the whole commute

- [x] Phase 13 (small mobility, done before Phase 10 by choice): physics-based bikes, e-bikes (assist levels, battery by game time, chargers) and skateboards (pushes, carving, wheel/bearing/truck effects, speed wobbles); persistent owned vehicles (wear, service history, parts, resale, saved position); Curbside Skate and Hillside Cycles with spec cards, parts and services; curb ramps; first-person and chase camera. Acceptance: buy a board, ride it home, save; buy a bike, park it, reload, still there. City slice acceptance items 1–13 all covered

- [x] Rendering pass: post-processing (tonemapping, grading, bloom, vignette), SSAO, MSAA, soft cascaded shadows, Forward+ street-light pools and headlights at night, dimming sky, trilight ambient
- [x] Phase 10 (cars): raycast-suspension car physics (tyre curves, friction circle, torque curves, automatic with converter, FWD/RWD/AWD, drag and downforce), 8 cars from sedan to supercar, Kenney car kit models for driven cars and traffic, enter/exit with a camera glide, chase and hood cameras, fuel by game time, crash damage, used-car classifieds in STORE (delivered to the kerb), Tidewater Fuel station, cars saved where parked

## Next
- [ ] **Play the build yourself:** buy the $3,900 used sedan (transfer from brokerage first), drive to work, fill up at Tidewater Fuel
- [ ] **Debug panel** (spec §42, part of the §45 vertical slice; still missing): set cash/time, teleport to city spots, grant equipment or vehicles, seed/FPS/tick time. Dev builds only
- [ ] Art, next steps: free low-poly packs for buildings/props (Kenney City kits) and animated people (Kenney/Quaternius characters) to replace the boxes
- [ ] Then WORLD_SPEC Phase 11 (dealerships: used lot and standard dealer, test drives, resale) or core-game features (scanner, statistics/journal, psychology, stop orders). Your call

## Later phases (do not start early)
- WORLD_SPEC Phases 11–12 and 14–17: dealerships, mechanics, property, advanced vehicles, aviation, world polish

## Known debt / decisions to revisit
- No market holidays (`MarketSchedule.IsTradingDay`)
- No market impact from player orders. Fine at retail size; revisit for large accounts
- No queue model for limit orders at the bid/ask. Conservative (they wait for a trade-through)
- Day orders only; no GTC or extended-hours flag
- No regime system yet: `MarketDailyDrift/Volatility` are static config
- Content tuning: high idiosyncratic vol (e.g. CYRA 6%) makes sector/market moves hard to see in small caps. Revisit when news and regimes exist
- "Nortek" is a real company name (Nortek Inc.). Consider renaming NRTK's company before release
- UI polish: no price-flash on ticks; no trading hotkeys yet; the index isn't chartable
- Polish leftovers: audio is synthesized placeholder; no main/title menu (the game continues or starts from the pause menu); emails time-stamp at delivery (a skipped-past 8:20 email shows the skip time)
- The Close button outside regular hours posts a limit at the bid. It may not fill if the bid drops
- UI refresh allocates strings every 0.1s. Fine now; profile in Phase 8
- Apartment is placeholder primitives: no light switches or appliance interactions yet (spec §5 lists them; add when a phase needs them). The desk lamp shade floats (no stand)
- City (Phase 9):
  - Art is primitives plus procedural facades.
  - No curb ramps yet; the 15 cm curb is a step. Needed for bikes and cars.
  - No real street-light pools at night (only emissive lamps, lit windows, ambient). Forward+ or light decals in Phase 17.
  - Pedestrians have no physics: they can clip through cars that have stopped on a crosswalk.
  - Shop items (coffee, pastry, energy drink, sandwich) cost money but have no effect until psychology/needs exist.
  - The office lease can't be cancelled.
  - Receptionist lines aren't saved (the welcome repeats after a reload).
  - No map yet (§70). Directions are in the store listing.
  - Walking time scale stays 30×: the commute takes about 30–45 game minutes.
  - Deliveries still appear instantly at home, so the receptionist doesn't announce any.
  - One elevator car, two served floors.
- Small mobility (Phase 13):
  - The city is flat. Slope physics works (tested) but only stairs and ramps use it until a hills district exists.
  - No selling vehicles yet: resale value is shown, but selling comes with dealerships (Phase 11).
  - No helmets, locks or lights (no crash-injury, theft or night-visibility systems for them to act on).
  - No tricks or ollies. Skateboards can't climb curbs, so use the ramps at crosswalks.
  - No paint customisation yet (`OwnedVehicle` has no paint field; add with the body shop).
  - Pedestrians clip through a stopped bike.
  - Extra boards are "stored at home" with no way to swap them in yet.
- Cars (Phase 10):
  - Automatic only; manual/dual-clutch later (spec §22).
  - Open differentials only (no LSD or ESC/ABS modelling beyond the friction circle).
  - No tyre temperature or wet grip (weather is Phase 17).
  - Kenney models are recoloured only per model (palette texture), so there's no paint choice yet.
  - No interiors (the hood view looks over the bonnet).
  - Damage is condition and power only; no visual dents or broken parts (the kit has debris models for later).
  - AI traffic doesn't overtake: a car parked in a lane blocks it.
  - No parking garages or passes yet (§63), no insurance or registration (optional §83–84), no towing or breakdowns (§81–82).
  - Cars aren't sold anywhere but the classifieds until dealerships (Phase 11).
  - The engine sound is one synthesized loop.
  - Traffic cars don't honk or react to being hit.
- Active input handling is "Both". Test switching to Input System only (UI Toolkit runtime input) before release
- With the default seed (18492) the onboarding APEX headline draws a modest +3% move (the expected move is about +10%). Honest randomness; retune severity or the seed if the tutorial needs a clearer reaction
- Day loop: no weekend gameplay (slept through); no fatigue/sleep-quality effects (psychology system, spec §9); sleeping mid-afternoon after 4 PM skips after-hours trading without asking
- Save: single slot. Saving while seated restores the player standing. Older saves have no migration steps yet (none needed at v1)
- Economy balance: about $1,565/month of costs against $10k trading capital is intentionally tight; tune after playtests. Transfers are instant (real ACH takes days). Store upgrades are cosmetic until psychology, latency and multi-monitor exist. No income besides trading (spec §32's temp work and downsizing are later). Overdraft has no further consequence yet (no eviction)
- News: no earnings calendar or expectations model yet (spec §21); no news alerts while away from the desk (phone, later); headlines have no body text
