# World / City / Vehicle / Property Expansion (condensed master prompt)

Extends `PROJECT_SPEC.md`. Section numbers (§) follow the original prompt. The goal is a compact, dense, original first-person city where almost every building exists for a gameplay reason, in the spirit of Schedule I's structure. **Copy nothing of Schedule I** (map, names, art, mechanics). Use fictional brands only, no real logos or trademarks (§94). Quality target (§100): "a real place that happens to support a trading career", not a trading menu with streets attached.

## World (§1–2, 13–15, 70–75, 91–93)
- Compact, walkable, dense, learnable: shortcuts, parking, hours, routes, alleys, routines. No giant empty open world, and no roads added just to lengthen travel.
- Scale target: 5–10 min on foot, 2–4 min by bike or e-bike, 1–3 min by car, faster by helicopter.
- Districts:
  - **Downtown/financial:** office, brokerage offices, bank, coffee, restaurants, parking garages, luxury tower.
  - **Residential:** starter apartment, houses, townhomes, high-end homes, garages, parks.
  - **Commercial:** electronics, furniture, mechanic, car and motorcycle dealers, bike/e-bike shop, skate shop, clothing, convenience, grocery.
  - **Industrial/service:** big mechanic, body shop, storage, warehouse, towing, fuel, used lot.
  - **Luxury hills** (later): mansions, luxury dealer, helipad, aviation showroom.
  - **Aviation** (later): heli dealer, hangar, helipad, maintenance.
- Recognisable landmarks per district (§93).
- Day/night changes activity:
  - Morning: commuters, busy coffee shops.
  - Trading hours: busy downtown.
  - Evening: office workers leave, restaurants fill.
  - Night: fewer people, some shops closed.
  - Businesses have opening hours (§74): mechanic 8–18, dealership 9–19, skate shop 10–20, coffee 6–22.
- Weather later (§14; affects visibility, traction, braking, pedestrians, lighting), and not before driving is stable.
- Parking matters without being tedious (§15, §63): street, lots, garages (ticket or pass), reserved office spots. The player's vehicle remembers where it's parked and never vanishes.
- Map (§70) shows businesses, property, services and destinations, but not hidden places. GPS route line later (§71). No early fast travel (§72).
- Stream or partition only as needed; LOD, occlusion and pooled NPCs/traffic when needed (§91–92).
- World audio (§75): traffic, footsteps, doors, elevator, city ambience, birds, HVAC, engines, horns, garage echo, shop music, tools.

## Buildings, office, receptionist, elevator (§3–8)
- The player's office is inside a real downtown building (floor 2), with no teleporting in. The ground floor has:
  - front entrance and lobby
  - reception or security desk
  - elevators and stairs
  - seating
  - mailboxes or package area
  - directory and notices
  - access doors
- Routine: park → walk → door → lobby → receptionist ("Morning.", "Package came in for you.") → elevator or stairs → hallway → unlock office.
  - It must stay quick; later conveniences speed it up: premium access card, parking pass, private elevator.
- Receptionists have routines (greet, type, phone, coffee, leave the desk, lunch), comment on weather or deliveries, and relay small notices ("monitor delivered", "maintenance", "permit expires"). They are not quest gates.
- Office progression (§6):
  1. Starter room: 1 desk, 2-monitor capacity, cheap chair, poor light.
  2. Professional: 4–6 monitors, lounge, storage.
  3. Executive suite: windows, conference room, kitchenette, assistant, restroom, premium network.
  4. Private trading floor: workstations, research, server room, rooftop helipad.
- Every upgrade must mean something.
- Modular, reusable building code, not unique code per building (§7).
- Elevator (§8): call button, doors, floor select, travel delay, indicator, audio and chime. A controlled transition between floors is fine.

## Streets and NPCs (§9–12, 73)
- Sidewalks everywhere: curb ramps, crosswalks, storefronts, benches, bins, bike racks, streetlights, meters.
- Roads: two-way and one-way, intersections, lights, stop signs, parking, lots, alleys, service roads. Keep the topology simple.
- Traffic: follows lanes, stops at lights and stop signs, yields, avoids collisions, parks or despawns. Density follows the time of day. Stability matters more than AAA AI.
- Pedestrians: walk sidewalks, cross streets, visit shops, enter and exit buildings, sit, wait at crosswalks. Schedule-based. A few good NPCs beat many broken ones.
- Important NPCs keep schedules (e.g. the mechanic opens at 8, lunches at 12, closes at 18).

## Vehicles (§16–35, 76–90)
- **Persistent identity** for every owned vehicle: id, make/model, type, price, mileage, condition, fuel or charge, paint, mods, performance and cosmetic parts, damage, tyres, engine, location, insurance, registration, ownership date, maintenance history, resale value. Never disposable prefabs.
- **Types:** skateboard, bicycle, e-bike, scooter, motorcycle, compact, sedan, hatchback, sports, muscle, luxury, SUV, truck, van, supercar, helicopter. Boats and utility vehicles maybe later.
- **Physics from characteristics, never flat speed stats:** mass, wheelbase, CoM, drivetrain, power and torque curve, gearing, grip, suspension, steering, brakes, aero.
  - Cars (§19): traction, body roll, under/oversteer, wheelspin, weight transfer, surface grip.
  - Drivetrains (§20):
    - FWD: understeer, front traction limit.
    - RWD: stronger launch feel, power oversteer.
    - AWD: traction, heavier.
  - Engine (§21): RPM, torque curve, gears, throttle, engine braking, redline. Economy, turbo (boost builds), big displacement (low-end torque) and electric (instant torque) must feel different.
  - Transmission (§22): automatic, manual, dual-clutch, with an auto accessibility option.
  - Suspension (§23): springs, damping, travel, ride height, anti-roll.
  - Tyres (§24): economy, touring, sport, performance, semi-slick (later all-season, summer, winter).
  - Brakes (§25): pads, rotors, kit, fade.
- **Damage (§26):** body, engine, suspension, tyres, glass, with handling effects.
- **Energy:**
  - Fuel (§27): physical stations, tank size and efficiency; don't make refuelling frequent.
  - EV and e-bike charge (§28): home, apartment, public and fast chargers; battery degradation that isn't oppressive.
- **Bikes and boards:**
  - Bicycle (§29): pedal, coast, lean, slopes, rolling resistance; commuter, road, mountain and BMX types.
  - E-bike (§30): assist level, battery, regen.
  - Skateboard (§31): push, roll, carve, slope, brake, balance; tricks later. Deck, wheels, bearings and trucks change handling.
- **Motorcycles (§32):** lean, counter-steer approximation, traction; scooter, standard, sport, cruiser. No "car with two wheels".
- **Helicopters (§33–35), late game:**
  - Collective, cyclic, yaw, rotor RPM, lift, drag; assisted hover on Casual.
  - Must land on a helipad or flat ground; hard landings damage it.
  - High fuel use.
  - Stored in a hangar or approved helipad only, never spawned anywhere.
- **Camera and use (§77–80):**
  - Cockpit and chase cameras (hood optional).
  - Smooth [E] enter and exit, never exiting into walls.
  - Only owned or authorised vehicles; no stealing.
  - Storage: trunk, cargo, truck bed.
- **Services (§81–84):** towing and breakdowns (rare, never for well-maintained vehicles), optional insurance, light registration.
- **Collisions (§85–87):** use mass, impulse and thresholds, and no orbit launches. Surface grip for dry, wet, gravel, snow and grass. Deep water stalls the vehicle.
- **Code structure (§88–90):**
  - Separate classes: VehicleDefinition (SO), VehicleRuntimeState, VehicleController, VehiclePhysicsConfig, Ownership, Damage, Maintenance, Customization.
  - Data-driven fields.
  - Full save of vehicle state.
- **Audio (§76):** reacts to RPM, throttle, load and gear. EV whine, higher-pitched motorcycles, rotor and turbine.

## Commerce (§36–56, 62–69)
- **Dealer classes:** used, mainstream, performance, luxury, exotic, plus motorcycles, bikes/e-bikes, skate, and helicopters (needs money, a hangar and a landing site).
- **Dealerships:** rotating inventory; inspect, sit inside, test drive (must return it), buy, finance, trade in.
- **Used lot:** varied condition, with readable warning signs; no hidden scams.
- **Financing:** down payment, principal, interest, term, monthly payment. Missed payments bring penalties, then repossession. Keep it understandable.
- **Mechanic** (a deep system; you drive in):
  - Services: maintenance, repair, diagnostics, upgrades, tuning, cosmetics.
  - Diagnostics report on the engine, transmission, brakes, suspension, tyres, battery, cooling and electronics.
  - Maintenance intervals are generous.
  - Real component upgrades:
    - engine: intake, exhaust, ECU, cam, turbo or supercharger, intercooler, fuel, cooling, internals
    - transmission and drivetrain: clutch, gearbox, ratios, LSD, AWD
    - suspension: springs, coilovers, shocks, sway bars, alignment
    - brakes, tyres, weight, aero
  - Tuning uses understandable sliders and presets.
  - Every job gives a quote (parts, labour, time); the vehicle is unavailable meanwhile; rush service costs extra.
- **Other shops:** body shop, tyre shop, detailing. Visual mods: paint, wraps, wheels, tint, spoilers, bumpers, lights, interior, plates.
- **Resale:** depends on age, mileage, damage, maintenance and demand. Mods don't automatically raise value. Prices drift mildly, with no buy/sell exploit loops.
- **Real estate:** a physical real estate office or listings; inspect, tour, rent, buy.
- **Shops with a purpose:** fuel station, convenience store, coffee shop (a social hub), electronics (monitors, PCs, networking, UPS, phone), furniture.
- **Deliveries:** large purchases are delivered, and the receptionist may notify you.

## Property (§57–61)
- **Garages** physically store vehicles:
  - starter apartment: 1 outdoor spot
  - townhouse: 1
  - house: 2
  - luxury house: 4
  - mansion: large
- **Progression:** rental → nicer apartment → condo → townhouse → house → luxury house → mansion/penthouse. Each step matters.
- **Home upgrades:** some give stats, some are pure status. Home office options: desk, monitors, network, sound insulation, backup power, climate control.
- **Home vs office:** home is cheaper and has no commute; the office has infrastructure, prestige, staff and business growth. Neither is strictly best.

## Progression and status (§95–97)
- **Mobility ladder:** walk → skateboard → bike → e-bike → cheap used car → better car → performance/luxury → helicopter.
- **Each vehicle has a job:**
  - skateboard: cheap speed
  - bike: range
  - e-bike: fast and cheap
  - cheap car: weather protection
  - performance car: fast but expensive
  - luxury car: comfort and status
  - truck: storage
  - helicopter: long distance
- **Status is subtle**, e.g. the receptionist asks "New car?". No arcade meter.

## Phase plan (§98), do not implement everything at once
- **9 City foundation:** streets, sidewalks, office building, lobby, receptionist, elevator, exterior access, basic traffic, basic pedestrians. ✅
- **10 Ground transport:** vehicle framework, car driving, parking, fuel, enter/exit, save. ✅
- **11 Dealerships:** used and standard dealers, buying, test drives, resale.
- **12 Mechanics:** repair, maintenance, diagnostics, upgrades, damage.
- **13 Small mobility:** skateboards, bicycles, e-bikes, dedicated shops. ✅ (done before 10 by choice)
- **14 Property:** apartments, houses, garages, home upgrades, office upgrades.
- **15 Advanced vehicles:** motorcycles, performance cars, advanced tuning.
- **16 Aviation:** helicopters, dealer, hangars, helipads, flight controls.
- **17 World polish:** traffic, NPC routines, audio, weather, business schedules, optimisation.

## First city slice acceptance (§99)
1. Exit the apartment.
2. Walk onto the street.
3. Travel downtown.
4. Enter the office building.
5. Pass the receptionist.
6. Use the elevator.
7. Enter the trading office.
8. Trade normally.
9. Leave the office.
10. Visit a shop.
11. Buy a basic mobility item.
12. Ride it home.
13. The world saves correctly.

No helicopters before this works. All 13 items are covered (Phase 9 plus Phase 13).
