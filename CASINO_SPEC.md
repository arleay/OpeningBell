# CASINO_SPEC — The Meridian

Condensed from the casino master prompt (section numbers below are the prompt's, which code comments cite as §N). Fictional currency only: chips are bought with in-game bank money and cashed back into it.

## Non-negotiables
- **§14 No rubber-banding.** Outcomes never depend on wealth, streaks, trading or progress. House edge comes from the rules alone.
- **§15 Dedicated RNG.** Logic decides the outcome first; animation only shows it. Seeded in tests; production shoes shuffle from OS entropy (`RandomNumberGenerator`), never from the world seed.
- **§83 Atomic money.** Every chip change is one call that updates balance, stats and history together. Settlement is one step per round.
- **§82/§88 Save safety.** Chips, membership and any hand in play are saved. A settled round is already in the chips and isn't saved. A reload restores the exact cards and the shoe order, then the shoe reshuffles, so reloading can't scout or re-roll cards.
- **§112 Card forcing is test-only.** `Shoe.Stack` is `internal` (visible to test assemblies only). Release builds have no way to force cards.
- **§113 Reuse.** Bank = `EconomySystem`; saves = `SaveGame`; interaction = `Interactable`; staff = `StaffNpc`; building = `Kit`/`CityContext`.

## Blackjack rules (§17–22), per table in `BlackjackRules`
6 decks, cut at 75%. Dealer stands on all 17s. Blackjack pays 3:2 (exact cents: $5 pays $7.50). Dealer peeks under an ace or ten. Double on any first two cards and after splits. Split to 4 hands; split aces get one card each, and 21 after a split pays 1:1. No insurance or surrender.

Tables:
- Table 1: $5–$500, green felt.
- Table 2: $25–$2,500, red felt.

## Phases (§110): build one at a time
| Phase | Scope | Status |
|---|---|---|
| C1 Building & floor | exterior, entrance, interior, cashier, NPC placeholders, ambience | **done** (slice) |
| C2 Chips & accounting | buy / cash out, wallet, transaction safety, save/load | **done** |
| C3 Blackjack | rules, betting, dealer, UI, tests | **done** |
| C4 Roulette | board, bets, payouts, animation, tests | not started |
| C5 Slots | machines, definitions, probability, payouts, tests | cabinets placed as dressing only |
| C6 Poker | Hold'em, NPC AI, cash tables, evaluation, tests | room roped off |
| C7 Social | bar, restaurant, lounge, staff, evening routines | bar and lounge built (drinks via `ShopCounter`); no restaurant |
| C8 Hotel | reception, rooms, sleep, suites | — |
| C9 VIP / rewards | membership, VIP, host, high-limit, perks | free players' card only |
| C10 Integration | valet, audio, population, helipad… | parking bays only |

## The building (C1)
`City/Casino/Meridian.cs` sits on the old Silver Tide lot and keeps the marquee and pylon. Local frame: origin (515, 1.5, 8), x −37…37, z 0…32, ceiling 6.2 m.

- **Lobby:** glass doors, marble floor, bronze whale (Poly Haven) under a chandelier, host stand, security.
- **Cage (SW):** glass and brass grilles, a cashier, and a wall of safe-deposit boxes.
- **Slots:** four banks of cabinets (placeholders).
- **Pit (back centre):** the two tables with dealers and hanging lamps.
- **Tide Bar and lounge (NE):** bartender and drinks, Poly Haven sofas and chairs, chandelier.
- **High Limit / Poker (NW):** roped off.
- **Parking:** 20 guest bays on the west of the lot.

Patterned carpet, coffered ceiling, cove lights. Ten point lights, culled by the room-light radius.

## Code map
- **Pure (`OpeningBell.Casino`, no engine):**
  - `Cards.cs`: `Card`, `Shoe`.
  - `CasinoAccount.cs`: chips, stats, history, `ChipDenominations`.
  - `Blackjack.cs`: rules, hand, round, save.
  - `CasinoFloor.cs`: account, membership, per-table pending rounds, `CasinoSaveData`.
- **City:**
  - `Meridian`: the building.
  - `CasinoProps`: table, chips, cards, slot cabinet, carpet.
  - `BlackjackTable` + `BlackjackSeat`: seat view, cards laid one at a time, chip stacks, UI.
  - `CasinoCage`: sign-up, buy, cash out, history.
  - `CasinoUi` / `CasinoControls`: panel look and control handoff.
- **Wiring:** `GameBootstrap.Casino`; `SaveGame.HasCasino/Casino`.

## Tests
- `BlackjackTests` (13) and `CasinoFloorTests` (2), EditMode.
- `CasinoPlayTests` runs the vertical slice (§111) end to end. It writes `TestResults/casino-*.png`.
