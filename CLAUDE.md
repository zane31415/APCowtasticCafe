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
  When already connected (e.g. after a "try again" / "new game" returns to the
  menu) it instead shows a **Play Randomizer / Disconnect** panel so you never
  fall back into non-rando play (`InjectAlreadyConnectedPanel`).
- `Game/Manager/APNetworking.cs` — raw `ClientWebSocket` transport. Uses
  `wss://` for public servers, `ws://` only for localhost. Message queue is
  drained on the main thread.
- `Game/Manager/ArchipelagoClient.cs` — AP protocol (hand-rolled JSON, no
  library). Connect handshake, `ReceivedItems`, `LocationChecks`,
  `StatusUpdate` (goal), DeathLink `Bounce` send/receive. Holds the item-ID→name
  and location-name→ID tables, the ingredient token↔display map, and the
  consecutive-serve restore helper (`GetConsecutiveServeSent`).
- `Game/Manager/RandomizerManager.cs` — game-side glue: applies received items,
  tracks ingredient serves → location checks, shop queue/pricing, cosmetics,
  goal trigger, barista announcements, DeathLink send/receive. Re-initializes on
  scene reload (`OnSceneLoaded`) and replays all items so a fresh game restores
  state from nil.
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

`RandomizerManager` is a `DontDestroyOnLoad` singleton, so on a game-scene
reload its `_gameReady` flag and per-run state would otherwise persist stale. It
subscribes to `SceneManager.sceneLoaded`: on `Game_Arcade` it clears its state
and calls `ArchipelagoClient.ReplayTo(this)`, which re-applies cached slot data
and silently re-queues **all** items (`_allItems`) — no re-announce. It then
restores serve counts from already-sent locations via `GetConsecutiveServeSent`
(consecutive from #1; a gap stops the run so `collect` can't skip a check).

## Cross-boundary contracts (read before touching items/locations)

- **Item IDs**: assigned in `items.py` in this order — unlockable ingredients
  (in `INGREDIENTS` order, 13 of them), then Stretchy Candy, Fullness Tolerance,
  Happiness Upgrade, Milk Flow Increase, "Barista Smile :-)", then `COSMETICS`
  (in order). The client mirrors this **exactly** in `ArchipelagoClient`'s static
  ItemIdToName builder + `IngredientItemOrder` + `Cosmetics`. Reordering or
  inserting anywhere shifts IDs — update both sides.
- **Ingredient token ↔ display name.** `INGREDIENTS`/`SERVE_INGREDIENTS` entries
  are game enum **tokens** (`Fillings`/`Toppings` `ToString()`, e.g.
  `"WhipedCream"`, `"ChocolateSauce"`, `"BreastMilk"`). All AP-facing **names**
  (item names, location names, tracker) use the prettified DISPLAY name from
  `items.INGREDIENT_DISPLAY` (e.g. `WhipedCream`→"Whipped Cream",
  `ChocolateSauce`→"Cocoa Powder", `BreastMilk`→"Breast Milk"). The client
  mirrors this map in `ArchipelagoClient.IngredientDisplay` with
  `DisplayIngredient`/`TokenFromDisplay`. The client converts display→token when
  unlocking (`RandomizerManager.ApplyItem` → `UnlockIngredient`, which matches
  the `FillingTool` enum) and token→display when building location names
  (`TrackIngredientServe`). Tokens not in the map display as-is.
- **Serve ingredients & Breast Milk.** `SERVE_INGREDIENTS = INGREDIENTS +
  ["BreastMilk"]` (14). Breast Milk has serve checks but **no unlock item** (a
  core, always-available mechanic) — appended last so existing IDs don't shift.
  The client mirrors this in `ArchipelagoClient.ServeIngredientOrder`
  (item order vs serve order are now distinct lists).
- **Location names**: serve = `"Serve {DisplayIngredient} #{n}"` (display name
  may contain spaces, e.g. `"Serve Whipped Cream #3"`), shop = `"Shop Slot #{n}"`.
  Built identically in `locations.py` and the client's `LocationNameToId`. IDs:
  serve base `771771100` (10 per ingredient; Breast Milk is index 13 →
  `771771230`–`239`), shop base `771771300`.
- **Slot data** (`fill_slot_data`): `drinks_per_check`, `checks_per_ingredient`,
  `shop_locations`, `shop_base_price`, `shop_price_step`, `min_drink_quality`,
  `death_link`, `death_link_send_quality`, `death_link_penalty`,
  `censorship_mode`, `allow_milk_rate_adjustment`, and parallel
  arrays `loc_names`/`loc_items`/`loc_local` describing every real location's
  item (for shop labels + "sent"/"unlocked" barista messages). Parsed in
  `ArchipelagoClient.HandleConnected` → `RandomizerManager.ApplySlotData`.
- **Censorship aliases (dual IDs).** `censorship_mode` (option) is a per-slot
  rename only — no logic/placement change. Because an AP data package has ONE
  name per ID (global to the game), censored names get their OWN stable IDs and
  the apworld *creates* either the real or censored set per slot. Item aliases
  at base `771771050` (Supply Rate Increase, then censored cosmetics in
  `COSMETICS` order); censored Breast Milk serve locations at `771771400`–`409`
  ("Serve Secret Ingredient #n"). Cosmetics rename by garment: 3 `Cosmetic Top`,
  3 `Cosmetic Pants`, 11 `Cosmetic Extra` (`items.CENSORED_COSMETIC`). "Milk Flow
  Increase"→"Supply Rate Increase". The client mirrors all of this
  (`ArchipelagoClient` censored `ItemIdToName`, `CensoredCosmeticName`,
  `CensoredServeBase`, `CensoredMilkFlow`); it maps censored IDs→real effects
  (`_cosmeticToUnlockId` holds both names) and sends censored Breast Milk
  location IDs when `Censorship` is on (`TrackIngredientServe`,
  `GetConsecutiveServeSent`). Defined in BOTH `items.py`/`locations.py` and the
  client — keep the bases/order synced.

## Gameplay design (current)

- **Goal**: the in-game "barista overflows" good end (`GameWinManager` →
  `RandomizerManager.OnGameWon` → `SendGoalComplete`). Requires ~19 Stretchy
  Candy in Arcade (start size 4 + 4/candy ≥ 79.9). The apworld gates the
  Victory event at `REQUIRED_CANDY=19`; `extra_candy` option adds buffer.
- **Progression items**: ingredient unlocks + Stretchy Candy (milk capacity).
  Milk & Coffee are precollected (always unlocked); Breast Milk is always
  available with no item.
- **Locations**: N serve-checks per ingredient (every `drinks_per_check` drinks
  *asked for AND included AND rated at/above the quality gate*), plus shop-slot
  checks. 14 ingredients have serve checks (13 unlockable + Breast Milk).
- **Quality gate**: the minimum drink rating that earns a serve check is the
  `min_drink_quality` option (Perfect=0 … Okay=3, default Good=2). The game's
  `Ratings` array is ordered best→worst: Perfect, Great, Good, Okay, Meh, Bad,
  Ruined (indices 0–6; only configured in the Arcade/Holiday prefab inspector).
  `OrderManager.OrderFinished` counts a serve when `ratingIndex <=
  RandomizerManager.MinDrinkQuality`.
- **Breast Milk pacing**: in-game it's always available, but the apworld gates
  Serve Breast Milk #n behind `n*2` Stretchy Candy in **logic only** (clamped to
  total candy so high check counts stay reachable). See `rules.py`.
- **Shop**: shows the 6 cheapest unbought slots. Slot N costs
  `base + step*(N-1)`, capped 550, where base/step come from `shop_base_price`
  (default 50) / `shop_price_step` (default 25) options. Logic gating is
  **slot-position-based, not price-based**: slot n requires
  `max(2, ceil((n+1)/2))` ingredients (see `rules.set_shop_rules`), so changing
  prices never shifts accessibility. Pricing constants live in BOTH
  `locations.py` and `RandomizerManager` — keep synced.
- **Cosmetics** (filler): 17 milk/outfit unlocks; replace "Barista Smile :-)"
  filler when pool has room. To make triage cuts spread evenly, cosmetics are
  shuffled into the filler pool by category (`items._shuffled_cosmetics`:
  outfits + milks each shuffled, then interleaved proportionally). Customize
  shop disabled; locked at start; equipped immediately on receipt (milk via
  `MilkTypeController` preset, outfits via
  `BaristaController.SetBaristaTop`/`SetUnderwearType`).
- **Milk Flow Increase** (useful, count = `milk_flow` option): normally raises
  `ProductionRate` (the real flow), not just the bounds. With
  `allow_milk_rate_adjustment` on it instead only raises the rate CAP (see below).
- **Milk rate adjustment** (`allow_milk_rate_adjustment` option, off by default):
  purely a client control change (slot-data only; no logic/placement impact).
  When on, `RandomizerManager.ConvertShopButtons` keeps the two `Production`
  buttons (up = `UpgradeTimes>0`, down = `<0`) as **free** milk-rate up/down
  controls (`ButtonUpgrade.ConfigureAsMilkRate`) and converts only the other 4 to
  shop slots. Rate moves in 5 ml steps (`MilkFlowIncrement`) between floor =
  1 step and ceiling = `(MilkFlowReceived + 1)` steps — each Milk Flow Increase
  item raises the cap AND bumps the current rate up one notch, clamped
  (`RefreshMilkRateCap` + `IncreaseMilkRate`), so the first item already grants
  headroom above the floor. Buttons gray out (`interactable`) at their limit via
  `CanIncreaseFlow`/`CanDecreaseFlow`.
- **DeathLink** (`death_link` option, off by default): when on, the client adds
  the `DeathLink` tag via `ConnectUpdate`. Serving a drink rated at/below
  `death_link_send_quality` (Great=1 … Ruined=6, default Okay=3) sends a Bounce;
  receiving one subtracts `death_link_penalty` (0–100, default 20) happiness.
  Send check is in `OrderManager.OrderFinished` (outside the isFail branch so
  "Ruined" can trigger it). A **game over also always sends** a death (ignoring
  the quality gate) via `GameOver_Arcade` →
  `RandomizerManager.SendGameOverDeathLink`, which suppresses the send if a
  DeathLink was received in the last ~10 s (avoids echoing the death that just
  killed you). Send/receive plumbing in `ArchipelagoClient`
  (`SendDeathLink`/`HandleBounce`) and `RandomizerManager`
  (`SendDeathLink`/`ReceiveDeathLink`).
- **Barista announcements**: `BaristaTalkManager.AnnounceAP` shows
  "X unlocked!" (on receive) / "X sent!" (on checking a remote location),
  paced 4s apart in `RandomizerManager.Update`. The initial connect resync
  (`ReceivedItems` index 0) is applied **silently** (no announcements); only
  live streaming deliveries announce.

## Build / release

`./build_release.ps1` (PowerShell):
- `-SkipGame` → just rebuild `Cowtastic/cowtastic_cafe.apworld` (zips the
  folder with **forward-slash** entries — required for Python's zipimport).
- no args → also zip the Unity build from `Output/` + write `SETUP.txt` into
  `release/`. Build the Unity game from the editor into `Output/` first.

After any item/location/option change: regenerate seeds. `world_version` in
`archipelago.json` is **release-tracked** (currently `0.2.0`) — it tracks the
released apworld version, NOT bumped per incremental change. Only the `Output/`
data files (`Game.dll` = compiled scripts, `level*`, `globalgamemanagers`,
`sharedassets*`) reflect a Unity rebuild; the `.exe`/`UnityPlayer.dll` keep old
dates because Unity reuses them unchanged. Scripts compile to **`Game.dll`**
(via `Assets/_BaristaGame/Game.asmdef`), not `Assembly-CSharp.dll`. To confirm a
build took: `Game.dll` newer than your latest `.cs`; apworld bundle newer than
the `.py` sources.

## Gotchas learned the hard way

- **PowerShell scripts must be ASCII.** PS 5.1 reads UTF-8-no-BOM as Win-1252;
  a stray em-dash breaks tokenizing. Use `-f` formatting, not `MB`/`KB` literals
  next to text.
- **AP packets are JSON arrays** of multiple commands; `Connected` +
  `ReceivedItems` often arrive batched in one frame. Dispatch with independent
  `if`s, not `else if`.
- **WebSocket scheme**: public AP needs `wss://`.
- **Connect packet** needs `uuid`; `items_handling: 7`; version `class:Version`.
- **Bounce vs Bounced**: the client SENDS `"cmd":"Bounce"`; the server BROADCASTS
  `"cmd":"Bounced"` (past tense) to notify recipients, including yourself in
  solo testing. Dispatch must match on `"Bounced"`, not `"Bounce"`, or received
  DeathLinks are silently dropped. Because the broadcast echoes YOUR OWN bounce
  back, `HandleBounce` must ignore bounces whose `data.source` equals `SlotName`
  or you kill your own barista with your own DeathLink.
- **Breast Milk's "asked" gate needs Milk, not just BreastMilk.** It's added via
  fluster-level substitution (`IncreaseOneWantedBreastMilkActiveCustomer`/
  `OverrideMilkFillingLogic`), which pulls from whichever ordered filling has
  room (Chocolate/Milk/Tea/Cream/Espresso/Sugar/Coffee, shuffled) — not
  preferentially Milk. So `OrderManager.OrderFinished`'s serve-tracking check
  requires `askedF.Contains(BreastMilk) || askedF.Contains(Milk)`; requiring
  only `BreastMilk` (never actually orderable) meant the check almost never
  fired. The post-fluster recipe (`ActiveIngreedentPercentages[13]`) being
  nonzero is NOT a reliable "wanted milk" signal by itself — it only means
  *some* ingredient got substituted, not necessarily Milk.
- **Received-items dedupe**: the server resends the full inventory (index 0) on
  connect/reconnect — apply/announce only items past `_receivedCount` or you
  double-count (e.g. milk capacity).
- **Shop button labels** were driven by Unity `LocalizeStringEvent`, which
  overwrites `.text`. Fix: disable the localizer on converted buttons and own
  the text (see `ButtonUpgrade.ConfigureAsShopSlot`). That method also disables
  the old `TooltipTrigger` components (their hover text described the old upgrade
  mechanics that no longer exist).
- **Rating order lives in the prefab, not code.** The `Ratings` array (and its
  best→worst order that `min_drink_quality`/`death_link_send_quality` index
  into) is configured only in `GameManager Arcade.prefab` /
  `GameManager Holiday.prefab`. Reordering it there silently breaks both options.
- **`CalcOrderRating` can return null** (no configured `Rating` matches) — every
  use of the result in `OrderManager.OrderFinished` after that point must
  null-check `ratingClass`. An unguarded null access here throws mid-method and
  aborts before `orderIsActive = false`/`ActiveCustomer = null` run, wedging
  the whole order-serving loop until a scene reload (no order can start while
  `orderIsActive` stays stuck true).

## Known issues / follow-ups

- **Barista announcements are sometimes dropped** (logged but not always shown).
  Low priority. Likely the talk system being busy/inactive when
  `StartBaristaTalk` fires, or the 4s pacing colliding with other dialogue.
  Consider a more robust queue that waits for the dialogue box to be free.
- Reconnect to a *different* slot in the same app session won't reset
  `_receivedCount` (no in-game reconnect flow exists, so not hit in practice).
- `extra_candy` very high + tiny `shop_locations`/`checks` could overfill the
  pool vs. available locations (generation error). Defaults are safe.
