# Cowtastic Cafe — Archipelago Mod

This repo is the open-source Unity game **Cowtastic Open Café** with an
**Archipelago (AP) multiworld randomizer** layered on top. The mod is
**rando-only** — normal/standalone play is not a goal, so it's fine to disable
or repurpose base-game systems for the randomizer.

## Two halves

1. **The apworld** — Python, in `Cowtastic/cowtastic_cafe/`. Runs inside an
   Archipelago install to generate multiworld seeds. Zipped to
   `cowtastic_cafe.apworld`.
2. **The Unity client** — C#, in `Assets/_BaristaGame/Scripts/Game/`. Talks to
   the AP server over a raw WebSocket and drives the game.

These two MUST agree on item IDs, location IDs, and slot-data field names. See
"Cross-boundary contracts" below — this is the #1 source of bugs.

## Key scripts (all under `Assets/_BaristaGame/Scripts/`)

- `Game/Manager/ArchipelagoMenu.cs` — builds the connect screen on the main
  menu at runtime (host/port/slot/password), saves last values to PlayerPrefs.
- `Game/Manager/APNetworking.cs` — raw `ClientWebSocket` transport. Uses
  `wss://` for public servers, `ws://` only for localhost. Message queue is
  drained on the main thread.
- `Game/Manager/ArchipelagoClient.cs` — AP protocol (hand-rolled JSON, no
  library). Connect handshake, `ReceivedItems`, `LocationChecks`,
  `StatusUpdate` (goal). Holds the item-ID→name and location-name→ID tables.
- `Game/Manager/RandomizerManager.cs` — game-side glue: applies received items,
  tracks ingredient serves → location checks, shop queue/pricing, cosmetics,
  goal trigger, barista announcements.
- `Game/ButtonUpgrade.cs` — the six upgrade buttons are converted at runtime
  into AP shop-slot buttons (`ConfigureAsShopSlot`).
- `Game/FillingTool.cs` — ingredient machines; `PurchasingEnabled=false` for
  the rando so ingredients come only from AP.

### Scene placement (important!)
`ArchipelagoClient`, `APNetworking`, `ArchipelagoMenu` live on **MainMenu**.
`RandomizerManager` is created at runtime and does **not** exist on the menu —
so anything the client receives during the connect handshake (slot data, the
initial item resync) must be **cached and flushed** once `RandomizerManager`
appears. Don't assume `RandomizerManager.Instance` is non-null in the client.

## Cross-boundary contracts (read before touching items/locations)

- **Item IDs**: assigned in `items.py` in this order — ingredients (in
  `INGREDIENTS` order), then Stretchy Candy, Fullness Tolerance, Happiness
  Upgrade, Milk Flow Increase, Decoration, then `COSMETICS` (in order). The
  client mirrors this **exactly** in `ArchipelagoClient`'s static ItemIdToName
  builder + `IngredientOrder` + `Cosmetics`. Reordering or inserting anywhere
  shifts IDs — update both sides and bump `world_version`.
- **Ingredient tokens = game enum names.** `INGREDIENTS` entries match
  `Fillings`/`Toppings` enum `ToString()` (e.g. `"WhipedCream"` — note the
  game's misspelling, `"ChocolateSauce"`). This lets the client unlock/track by
  name with no mapping. Display prettification happens only in
  `RandomizerManager.PrettyItemName` (e.g. ChocolateSauce → "Cocoa Powder").
- **Location names**: serve = `"Serve {Ingredient} #{n}"`, shop =
  `"Shop Slot #{n}"`. Built identically in `locations.py` and the client's
  `LocationNameToId`. IDs: serve base `771771100` (10 per ingredient), shop base
  `771771300`.
- **Slot data** (`fill_slot_data`): `drinks_per_check`, `checks_per_ingredient`,
  `shop_locations`, and parallel arrays `loc_names`/`loc_items`/`loc_local`
  describing every real location's item (for shop labels + "sent"/"unlocked"
  barista messages). Parsed in `ArchipelagoClient.HandleConnected`.

## Gameplay design (current)

- **Goal**: the in-game "barista overflows" good end (`GameWinManager` →
  `RandomizerManager.OnGameWon` → `SendGoalComplete`). Requires ~19 Stretchy
  Candy in Arcade (start size 4 + 4/candy ≥ 79.9). The apworld gates the
  Victory event at `REQUIRED_CANDY=19`; `extra_candy` option adds buffer.
- **Progression items**: ingredient unlocks + Stretchy Candy (milk capacity).
  Milk & Coffee are precollected (always unlocked).
- **Locations**: N serve-checks per ingredient (every `drinks_per_check` drinks
  *asked for AND included AND rated Good+*), plus shop-slot checks.
- **Shop**: shows the 6 cheapest unbought slots. Slot N costs
  `50 + 25*(N-1)`, capped 550. Apworld gates slot logic by ingredient count
  (`ceil(price/50)`; $50 of price = 1 ingredient). Pricing constants live in
  BOTH `locations.py` (`shop_slot_price`) and `RandomizerManager` — keep synced.
- **Cosmetics** (filler): 17 milk/outfit unlocks; replace Decoration filler
  when pool has room. Customize shop disabled; locked at start; equipped
  immediately on receipt (milk via `MilkTypeController` preset, outfits via
  `BaristaController.SetBaristaTop`/`SetUnderwearType`).
- **Milk Flow Increase** (useful, count = `milk_flow` option): raises
  `ProductionRate` (the real flow), not just the bounds.
- **Barista announcements**: `BaristaTalkManager.AnnounceAP` shows
  "X unlocked!" (on receive) / "X sent!" (on checking a remote location),
  paced 4s apart in `RandomizerManager.Update`.

## Build / release

`./build_release.ps1` (PowerShell):
- `-SkipGame` → just rebuild `Cowtastic/cowtastic_cafe.apworld` (zips the
  folder with **forward-slash** entries — required for Python's zipimport).
- no args → also zip the Unity build from `Output/` + write `SETUP.txt` into
  `release/`. Build the Unity game from the editor into `Output/` first.

After any item/location/option change: regenerate seeds and bump
`world_version` in `archipelago.json`.

## Gotchas learned the hard way

- **PowerShell scripts must be ASCII.** PS 5.1 reads UTF-8-no-BOM as Win-1252;
  a stray em-dash breaks tokenizing. Use `-f` formatting, not `MB`/`KB` literals
  next to text.
- **AP packets are JSON arrays** of multiple commands; `Connected` +
  `ReceivedItems` often arrive batched in one frame. Dispatch with independent
  `if`s, not `else if`.
- **WebSocket scheme**: public AP needs `wss://`.
- **Connect packet** needs `uuid`; `items_handling: 7`; version `class:Version`.
- **Received-items dedupe**: the server resends the full inventory (index 0) on
  connect/reconnect — apply/announce only items past `_receivedCount` or you
  double-count (e.g. milk capacity).
- **Shop button labels** were driven by Unity `LocalizeStringEvent`, which
  overwrites `.text`. Fix: disable the localizer on converted buttons and own
  the text (see `ButtonUpgrade.ConfigureAsShopSlot`).

## Known issues / follow-ups

- **Barista announcements are sometimes dropped** (logged but not always shown).
  Low priority. Likely the talk system being busy/inactive when
  `StartBaristaTalk` fires, or the 4s pacing colliding with other dialogue.
  Consider a more robust queue that waits for the dialogue box to be free.
- Reconnect to a *different* slot in the same app session won't reset
  `_receivedCount` (no in-game reconnect flow exists, so not hit in practice).
- `extra_candy` very high + tiny `shop_locations`/`checks` could overfill the
  pool vs. available locations (generation error). Defaults are safe.
