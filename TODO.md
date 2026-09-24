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

- [x] Art pass 1 (free CC0 packs, see CREDITS.md): animated Quaternius people (pedestrians, staff, riders) on a shared humanoid animator; Kenney buildings for every background lot plus the skyline; Kenney modular upper floors on the apartment, shops and Calder; nature-kit street trees and a dressed park; bins and dumpsters; lit kit windows at night; head bob, sway, strafe lean, landing dip, sprint FOV, jump; surface footsteps. Gallery test writes `TestResults/art-*.png`
- [x] Phone extras: map teleport ($250); My Cars app lists your cars and brings one to the nearest free kerb or lot spot (outside a shop, the street in front); ambient parked cars keep off spots your cars are in

## Next
- [ ] **Play the build yourself:** buy the $3,900 used sedan (transfer from brokerage first), drive to work, fill up at Tidewater Fuel
- [ ] **Debug panel** (spec §42, part of the §45 vertical slice; still missing): set cash/time, teleport to city spots, grant equipment or vehicles, seed/FPS/tick time. Dev builds only
- [x] Art pass 2a: apartment furnished with the Kenney Furniture Kit (`ApartmentInterior`): kit beds, desks, chairs, keyboard/mouse, desk lamp with a base, kitchenette (sink, drawers, fridge, microwave, wall cabinets, bin), loveseat, coffee table, rug, plant, bookcase, nightstands, coat rack, ceiling lamp, window trim. Upgrade variants and interactables unchanged. Gallery shots `art-apartment*.png`
- [x] Player body and brawling: first-person body (legs, hands, shadow; head hidden from the camera), punches steered into view with IK, NPC reactions (stagger, knockdown and get-up), flee or fight back, chase with lost-sight/distance give-up, bystanders scatter. `CombatPlayTests` writes `combat-*.png`
- [x] Small open town: the core now sits in an outer grid (Oak, Birch, Willow, Pine) of suburban house rows, lawns, a town field and green edges; the tall outer ring and skyline towers are gone and the town centre is low-rise. Traffic density scaled for ~3 km of road. Gallery: `art-town-aerial.png`, `art-town-street.png`, `art-mainstreet.png`
- [x] Walkable houses in three tiers (starter, family, mansion with pool) replace the solid kit houses; pedestrians walk to house doors
- [x] Round rotating minimap with icons (home, office, shops, fuel, parked cars)
- [x] Title screen with Continue / New Game / Quit and a character creator (body, outfit, skin, hair and top colours, name) with a turntable preview; the look is saved
- [x] Food props (Quaternius Ultimate Food Pack): stocked mart shelves and drinks fridge, café pastries, apartment fruit and soda
- [x] Tiny character set (Quaternius Ultimate Animated Character Pack, re-rigged in Blender): a third body type in the creator, player-only; drag to turn the preview
- [x] First-person body: the body slides back as you look down and pins the neck behind the eyes while walking/running (`FirstPersonBodyTests`)
- [x] Order ticket quantity (and price/transfer fields) accept only digits (`TicketInput.Restrict`)
- [x] Phone (Tab): home screen with clock and market widget; Messages (fills, bills), Phone (contacts, recents, keypad, calls as subtitles), Maps (pan/zoom town map, places by distance), PennyBridge (watchlist, positions, orders, stock page with chart, market/limit order sheet), News (feed, articles with live reaction). Headline and fill notifications bottom right; Tab opens the one showing. `PhoneTests` writes `phone-*.png`
- [x] Order-flow market (MARKET_SPEC.md stages M1–M6): price now comes from participants trading through a lightweight book with remembered levels, stops and breakouts; hidden day types and regimes; statistics harness and perf test
- [x] MARKET_SPEC.md M7 (stops, brackets, impact), M9 (structure detection) and chart stages C-A..C-F (themes, drawings, position/TP/SL lines, indicators and panes, ICT/liquidity overlays). `ChartToolsTests` writes `terminal-chart*.png`
- [ ] Continue MARKET_SPEC.md: M8 supply/demand zones as participant interest, M10 configurable time macros + premarket ramp, M11 news → participant bias, M12 remaining statistics (breakout follow-through, FVG interaction, gap behaviour), multi-timeframe structure (4H) read by participants, anchored VWAP
- [x] Market news can tilt sectors (`SectorTilt`): geopolitical and macro headlines (fictional President Hale, the Varenn Mountains, Kessar Strait) sink the index while lifting energy/defence, hit tech on tariffs, banks on a lender run, etc. Market news rate 0.3 → 0.6/day
- [ ] Art pass 2b: shop, lobby and office furniture (same `Kit.Fit` approach; `ModelSheet` for orientation/shelf heights), ground-floor trims on the enterable buildings, suburban houses for the outer ring
- [x] Phase 11 (dealerships): First Street Motors (new cars, lot behind the Maple shops, driveway off First St, showroom office) and Harbor Auto Sales (used lot behind Main Street, driveway off Harbor Ave), both 9 AM–7 PM. Weekly stock from the world seed (no save data); aim at a car for its spec card, [E] to test drive (brought round to the exit; free if returned to the lot, $250 recovery plus damage otherwise); [E] at the price board to buy (second press confirms), delivered to a bay; sell/trade in at the sales desk for 85% of private value. Used stickers list every flaw. `DealershipTests`, `DealerPlayTests` (writes `dealer-*.png`)
- [x] TOWN_SPEC.md Part A (A3–A18): the new Kell Valley (8 districts, terrain, canal/river/bay, elevated rail, landmarks, ~50 businesses with hours and staff, the Foundry and the mechanic, residential streets, waterfront, forest and outskirts, traffic with parking and deliveries, NPC routines, night lighting, weather, clutter, ladders/rooftops and the Lantern Parkade, a map filled in by discovery with taxi fast travel, Kell Valley Police, optimisation: merged meshes, cull layers, room-light radius). EditMode 170/170, PlayMode 36/36
- [x] TOWN_SPEC.md Part B: Timberline Home and Circuit Stop (showrooms of the whole catalog, colours, tiers, confirm to buy; tech boxed into your hands), pickup yard and $79 home delivery; carrying and placement (preview, R/Shift+R, G snap, LMB/RMB, Ctrl+Z, B storage, X sell 40–70%; floor/surface/wall/ceiling/desk-mount rules, overlaps, doorways, indoor/outdoor); desks (2/3/4/6 standing) and 1–6 screen arms, never more than 6 a desk; monitors with live chart/watchlist/portfolio/news/movers, power, portrait, pooled textures redrawn by distance; trade at any desk with a screen; loaner pickup + hitched trailer (gate, beds, 2 h loan, reminders, late and damage fees, saved with cargo); six houses (starter/family/mansion, sold empty, open house by day) and the Harborview penthouse (freight hoist), keypad locks, light switches, garages; all saved (`OpeningBell.Home`). `HomeRulesTests`, `HomePlayTests` (writes `home-*.png`)
- [ ] **Next:** play it: buy a desk and two monitors at Circuit Stop, borrow the loaner, bring a sofa home to a house you buy. Then Known debt below, or back to MARKET_SPEC M8+

## Later phases (do not start early)
- WORLD_SPEC Phases 12 and 14–17 (property, Phase 14, can now use the town's houses): mechanics, property, advanced vehicles, aviation, world polish

## Known debt / decisions to revisit
- Part B: the loaner's drive home is only exercised by hand (tests move cargo by carrying); reversing with the trailer is basic raycast-wheel physics. Garages only on lots deep enough (34 m+). The penthouse is one open loft with a bedroom wall. Monitor text is TextMesh (fine up close, fuzzy far off). Sold items vanish instantly (no buyer)
- Phone: you stand still while it's out (control is off); no texting back or incoming calls; call history isn't saved; the phone can't be used in vehicles or at the desk
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
- Apartment: no light switches or appliance interactions yet (spec §5 lists them; add when a phase needs them). Monitors, PC tower and the espresso machine are still primitives (the kit has no fitting models). Walls and floor are flat colour; the ceiling lamp's canopy renders dark from below
- City (Phase 9):
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
  - Bikes and boards can't be sold yet (car dealers buy cars only).
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
  - Dealerships (Phase 11): no financing (spec lists it; later), no luxury/exotic dealer (the exotics stay in the classifieds), no paint or trim choice. Test drives have no time limit, and a test car is dropped (not saved) if you save mid-drive. Trade-in is sell-then-buy, not one combined deal. Display cars don't show wear visually.
  - The engine sound is one synthesized loop.
  - Traffic cars don't honk or react to being hit.
- Art pass 1:
  - Shop/lobby/office interiors, ground floors of the enterable buildings, lamp posts, signals and signs are still primitives.
  - Kenney buildings are stretched to fit lots (up to ~25% wider, deeper rows); side walls at row ends show it.
  - Kit night windows all glow together (the palette can't vary per window); the procedural parking garage still scatters.
  - People have one animation set: no sitting-down transition, no per-person clothing colours, and the Typing pose uses the UAL "Interact" loop. Riders use the driving loop (no pedalling).
  - Pedestrians still walk through each other (no local avoidance beyond the simulation's rules).
  - The UAL root-motion file used to measure walk speed isn't in the project; `CityArtBuilder` keeps the measured 0.95 m/s.
- Town:
  - Houses: no locks or owners yet (every front door opens), interior doors are open doorways, family bedrooms are big and sparse, and nobody lives inside. Property purchase (Phase 14) can build on them.
  - Minimap: no full-screen map or custom waypoints yet; the office icon shows even before the lease.
  - The town field is empty grass (room for a market or pitch later).
  - From high up, the ground's edge shows beyond the green edges.
- Title / creator: no settings screen on the title (the pause menu has them); the name isn't shown anywhere yet; outfits come as whole characters (no separate tops/bottoms). The 2019 Animated Character Pack can't be used (its feet aren't parented to the legs, so no humanoid avatar), and the Universal Base Characters are unclothed bases for outfit packs we don't have.
- Brawling:
  - No consequences (health, police, reputation) by design for now; the player can't be hurt.
  - Fighters ignore traffic (cars don't stop for them) and steer around obstacles only locally; no pathfinding.
  - One animation set: the get-up is the fall played backwards; punches come from the UAL jab and cross.
  - The player's head is collapsed for the camera, so the player's shadow has no head.
  - Riders and staff only react in place (no chasing from behind a counter or off a bike).
- Active input handling is "Both". Test switching to Input System only (UI Toolkit runtime input) before release
- With the default seed (18492) the onboarding APEX headline draws a modest +3% move (the expected move is about +10%). Honest randomness; retune severity or the seed if the tutorial needs a clearer reaction
- Day loop: no weekend gameplay (slept through); no fatigue/sleep-quality effects (psychology system, spec §9); sleeping mid-afternoon after 4 PM skips after-hours trading without asking
- Save: single slot. Saving while seated restores the player standing. Older saves have no migration steps yet (none needed at v1)
- Economy balance: about $1,565/month of costs against $10k trading capital is intentionally tight; tune after playtests. Transfers are instant (real ACH takes days). Store upgrades are cosmetic until psychology, latency and multi-monitor exist. No income besides trading (spec §32's temp work and downsizing are later). Overdraft has no further consequence yet (no eviction)
- News: no earnings calendar or expectations model yet (spec §21); no news alerts while away from the desk (phone, later); headlines have no body text
