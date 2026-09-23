# Town redesign + homes (plan, 2026-09-23)

Source: two briefs from the user (town redesign; furniture/tech/delivery/property). This file condenses their
requirements and records the plan. Original, not a copy of any other game's map, names or assets.

## Target
Small but dense stylized Pacific-Northwest industrial/coastal town. Developed area ~1.1 × 0.7 km (walk 8–10 min at
3 m/s, drive 2–3 min), forest ring beyond. Every block has a purpose; landmarks visible from most streets; no huge
empty lots, boulevards or grass fields inside town; no perfect grid; natural boundaries (bay, river, forest, hills,
rail tunnel, blocked roads), no invisible walls where avoidable. Keep existing systems; extend them.

## Districts (x east, z north; the player's apartment stays at the origin on Maple St)
| # | District | Where | Identity / landmark |
|---|---|---|---|
| 1 | Old industrial | SW, x -560..-100, z -60..-300 | warehouses, mechanic (full service), body/tire shop, scrap yard, storage, container depot, closed factory with **smokestack** |
| 2 | Maple St commercial strip | Maple St x -250..130 | laundromat, pawn, liquor, barber, thrift, diner, pharmacy, hardware, tattoo, bakery, auto parts, smoke shop; flats above; alleys behind; existing coffee/skate/bike/mart |
| 3 | Downtown / civic | x 130..300, z -80..170 | Calder Building, bank, **city hall clock tower**, police, courthouse, plaza, real **parking garage** (roof access), **Harborview Tower** (high-rise, penthouse) |
| 4 | Residential | north of Grove (middle, curving streets uphill to the forest), south of the rail line (working class: duplexes, small houses, cheap flats) | mixed upkeep, hoops, bins, sheds, parked cars |
| 5 | Highway entrance | W, x -560..-250 | Kell Highway from the forest, **tall gas-station sign**, fast food, car dealer, used lot, motorcycle shop, car wash, motel, big supermarket, **billboard**, **furniture store + tech store** on a shared lot |
| 6 | Canal / waterfront | canal x ~300..330 from the river (N) to the bay (S); bay shore along the south | bridges, paths under bridges, retaining walls, docks, fishing pier, drainage pipes |
| 7 | Outskirts / forest | all round | dirt roads, trail to an **overlook + radio mast**, trailer park, campsite, abandoned shed, **water tower** hill, mansions on the NE hill (private winding road), rail tunnel |
| 8 | Entertainment | across the canal, x 330..520 | **casino sign**, nightclub, bar, arcade, diner, motel; busy at night |

**Elevated rail ("Northline")**: viaduct ~7 m up along z ≈ -60 (behind the south side of Maple), from a tunnel in the
west hills, over roads, across the canal, into the east. Station stairs; shadowed service road beneath with parking,
dumpsters, bus stop, small businesses, a camp. A train runs on it.

## Roads
2–3 major (Kell Highway, Maple St, Harbor Rd), 4–8 medium, many small streets, alleys, service roads, dead ends,
bridges, underpasses. Widths by class: highway 12, avenue 11, street 10, residential 8, industrial 11, alley 5.5, dirt 5.
Curves are chains of bend nodes. Heights follow terrain (grades ≤ 8%), bridges where the ground drops.

## Systems to add / extend
- Terrain (runtime TerrainData from a height function, flattened under roads/lots), water (bay, canal, river), forest.
- Road-based sidewalks and pedestrian graph (not block rings); traffic lanes with heights; alleys drivable, not in traffic.
- Generic data-driven businesses: footprint, sign, hours, staff, interior theme (enterable, real windows).
- NPC schedules by time and business hours; idle spots (smoke, talk, wait at bus stop); fewer people at night/in rain.
- Traffic by time of day; vehicle mix incl. delivery vans/box trucks/taxis; parked cars on kerbs, lots, driveways.
- Business hours everywhere; closed = locked, dim, no staff.
- Night: signs/neon, gas canopy, windows. Weather: clear/cloudy/overcast/rain/heavy rain/fog/storm; wet roads,
  rain particles and sound, lower grip, fewer pedestrians.
- Clutter, alleys (fire escapes, back doors), rooftop access (ladders, garage, fire escapes), hidden shortcuts.
- Map UI: places labelled once discovered; icons (home, stores, mechanic, fuel, dealer, casino, police, ATM, parking).
- Performance: layer cull distances, interiors off at range, static batching, shared materials, pooled NPCs/traffic.
- Mechanic shop (WORLD_SPEC Phase 12): drive into a bay; repairs and upgrades (engine, brakes, suspension,
  transmission, turbo, ECU, exhaust, weight, tyres/wheels, paint, tint).

## Part B: furniture, tech, delivery, property (HOME brief)
- Furniture store (departments: living, bedroom, office, dining, entry, decor; budget/mid/premium tiers; colour
  variants by material) with showroom items to inspect/buy, checkout → purchase order, warehouse pickup bay.
- Tech store next door: monitors (sizes/tiers, fictional brands), keyboards, mice, PC tower, laptop, speakers,
  monitor arms (1/2/3/4/5/6), boxed items (carry box, unbox at home).
- Store loaner pickup + trailer (hitched, pivoting), return deadline in game time with 1h/30m/10m reminders, grace,
  late fee, damage charge, deposit; return in the RENTAL RETURN bay; warn if cargo left. Optional paid home delivery.
- Trailer: gate/ramp open/close, cargo volume, items secured when released inside, keep when saved.
- Carry system: small items normal speed, large items slow; stable (kinematic while carried/placed).
- Placement mode: preview (valid/invalid), R rotate (15° snap, Shift fine), G snap toggle, LMB place, RMB cancel,
  wall alignment, undo; support rules (floor / desk surface / table surface / wall / ceiling / desk mount), no
  nonsense (chair on bed, desk on nightstand, floating, through walls, blocking doors); indoor/outdoor tags; move,
  store, sell (40–70%).
- Desks: monitor capacity (2/3/4/6) and mount points; arms hold up to their count; ≤ 6 monitors per desk; any number of
  desks; landscape/portrait; each monitor powered on/off and shows chart (symbol) / watchlist / portfolio / news /
  market / blank from the real simulation; pooled render textures, update rate by visibility/distance.
- Sit at any desk to trade.
- Properties across tiers (cheap flat → house tiers → high-rise → penthouse → mansion) with real floorplans, garages
  with working doors, freight elevator in the high-rise, lockable doors, lights; own several at once.
- Save everything: furniture per property (position, rotation, variant, condition, attachments), monitor setup,
  rental state and trailer cargo, property ownership, locks, lights, garage doors.
- Tests 1–14 from the brief (couch trip + reload, placement rejections, monitor limits, 30 monitors perf, power state
  persists, charts use real data, rental on time/late fees, cargo persists, multiple properties).

## Order
A1 audit ✅ · A2 plan (this file) ✅ · A3 roads/terrain/water/blocks · A4 landmarks · A5 commercial strip · A6 industrial
+ mechanic · A7 residential · A8 canal/waterfront · A9 outskirts/forest · A10 traffic/parking · A11 NPC schedules ·
A12 business hours · A13 night lighting · A14 weather · A15 clutter · A16 shortcuts/rooftops · A17 optimisation ·
A18 playtest. Then B1–B22 in the brief's order.
