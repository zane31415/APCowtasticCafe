from __future__ import annotations
import random
from typing import TYPE_CHECKING

from BaseClasses import Item, ItemClassification

if TYPE_CHECKING:
    from .world import CowtasticWorld

# Ingredient TOKENS match the game's Fillings/Toppings enum names exactly. These
# are the *unlockable* ingredients — each gets an "Ingredient: <display>" item.
# Order here drives item IDs and the first 13 serve-location IDs; never reorder.
INGREDIENTS = [
    "Espresso", "Coffee", "Chocolate", "Tea", "Milk", "Cream", "Sugar",
    "Ice", "Boba", "Sprinkles", "WhipedCream", "CaramelSauce", "ChocolateSauce",
]

# Breast Milk is a core, always-available mechanic: it has serve checks but NO
# unlock item (never gated). It's appended AFTER the unlockable ingredients so
# existing item/location IDs don't shift. Anything here gets serve locations but
# is skipped when creating unlock items.
SERVE_ONLY_INGREDIENTS = ["BreastMilk"]

# Every ingredient that has serve locations, in serve-location-ID order.
SERVE_INGREDIENTS = INGREDIENTS + SERVE_ONLY_INGREDIENTS

# Game enum token -> Archipelago display name. Tokens that are misspelled or
# run-together in the game's enums are prettified here for the tracker, item
# names, and location names. Tokens not listed are shown as-is. The Unity client
# mirrors this map exactly (ArchipelagoClient.IngredientDisplay).
INGREDIENT_DISPLAY = {
    "WhipedCream":    "Whipped Cream",
    "CaramelSauce":   "Caramel Sauce",
    "ChocolateSauce": "Cocoa Powder",
    "BreastMilk":     "Breast Milk",
}


def display_name(token: str) -> str:
    """The Archipelago-facing name for an ingredient enum token."""
    return INGREDIENT_DISPLAY.get(token, token)


# These ingredients are unlocked from the start in the base game.
# Their serve locations are still in logic immediately; no unlock item is placed.
STARTING_INGREDIENTS = {"Milk", "Coffee"}

# Stretchy Candy = milk-capacity upgrade. The goal is the in-game "barista
# overflows" good end, which fires when the barista's max size reaches the
# threshold. In Arcade mode that is: start size 4 + 4 per candy >= 79.9, so
# REQUIRED_CANDY = 19 candy must be collectable for the run to be winnable.
# (The actual goal is detected in-game; this is just the logic requirement.)
REQUIRED_CANDY = 19

# Useful non-filler items placed after mandatory progression items.
# "Milk Flow Increase" is added separately, its count driven by an option.
USEFUL_ITEMS: dict[str, int] = {
    "Fullness Tolerance": 5,
    "Happiness Upgrade":  5,
}

# Cosmetic unlocks (filler). These replace generic "Barista Smile :-)" filler
# when there is room in the pool. Names here must match the client's COSMETIC map.
# IMPORTANT: order here drives item IDs — never reorder existing entries.
COSMETICS = [
    "Barista Bikini", "Top Only", "No Apron", "Poofy Pants", "Underwear", "No Pants",
    "Blue Milk", "Chocolate Milk", "Creamy Milk", "Green Milk", "Honey Milk",
    "Rainbow Milk", "Raspberry Milk", "Space Milk", "Strawberry Milk",
    "Thick Milk", "Void Milk",
]

# Category buckets used when shuffling cosmetics into the filler pool.
# Splitting lets us interleave categories proportionally so triage doesn't
# always cut from the same bucket (e.g. all milks or all tops).
_COSMETIC_TOPS  = ["Barista Bikini", "Top Only", "No Apron", "Poofy Pants", "Underwear", "No Pants"]
_COSMETIC_MILKS = [
    "Blue Milk", "Chocolate Milk", "Creamy Milk", "Green Milk", "Honey Milk",
    "Rainbow Milk", "Raspberry Milk", "Space Milk", "Strawberry Milk",
    "Thick Milk", "Void Milk",
]

# ---------------------------------------------------------------------------
# Censorship mode (option). Purely a display rename — the actual cosmetics,
# items, and logic are unchanged; only the AP-facing names differ. These
# censored names get their OWN item IDs (registered below) so the per-slot
# option can pick either set without disturbing a multiworld's data package.
# The Unity client mirrors all of this (ArchipelagoClient censored tables).
# ---------------------------------------------------------------------------
CENSORED_MILK_FLOW = "Supply Rate Increase"  # alias for "Milk Flow Increase"

# Garment split for cosmetic censoring: 3 tops, 3 pants, the rest (milks) extra.
_CENSOR_TOPS  = ["Barista Bikini", "Top Only", "No Apron"]
_CENSOR_PANTS = ["Poofy Pants", "Underwear", "No Pants"]


def _build_censored_cosmetics() -> dict[str, str]:
    """Real cosmetic name -> censored label, numbered within each category in
    COSMETICS order (e.g. 'Barista Bikini' -> 'Cosmetic Top #1')."""
    names: dict[str, str] = {}
    t = p = e = 0
    for cos in COSMETICS:
        if cos in _CENSOR_TOPS:
            t += 1; names[cos] = f"Cosmetic Top #{t}"
        elif cos in _CENSOR_PANTS:
            p += 1; names[cos] = f"Cosmetic Pants #{p}"
        else:
            e += 1; names[cos] = f"Cosmetic Extra #{e}"
    return names


CENSORED_COSMETIC = _build_censored_cosmetics()


def censor_item_name(name: str) -> str:
    """Map a real item name to its censored equivalent (identity if no alias)."""
    if name == "Milk Flow Increase":
        return CENSORED_MILK_FLOW
    return CENSORED_COSMETIC.get(name, name)


def _shuffled_cosmetics() -> list[str]:
    """Return all cosmetics in a randomly interleaved order, with each category
    shuffled independently and then merged proportionally so that triage cuts
    are spread across tops and milks rather than exhausting one category first."""
    tops  = _COSMETIC_TOPS[:]
    milks = _COSMETIC_MILKS[:]
    random.shuffle(tops)
    random.shuffle(milks)

    result: list[str] = []
    ti = mi = 0
    while ti < len(tops) or mi < len(milks):
        tops_left  = len(tops)  - ti
        milks_left = len(milks) - mi
        # Pick proportionally: tops get tops_left/(tops_left+milks_left) chance.
        if tops_left > 0 and (milks_left == 0 or random.random() < tops_left / (tops_left + milks_left)):
            result.append(tops[ti]); ti += 1
        else:
            result.append(milks[mi]); mi += 1
    return result

# ---------------------------------------------------------------------------
# ID table — IDs must be globally unique across all apworlds.
# Using 771771000 as the base; reserve 000–099 for items, 100+ for locations.
# Real items occupy 000–034; censored aliases live at 050+ (see below).
# ---------------------------------------------------------------------------
_BASE = 771771000
_id = _BASE

ITEM_NAME_TO_ID: dict[str, int] = {}

for _ing in INGREDIENTS:
    ITEM_NAME_TO_ID[f"Ingredient: {display_name(_ing)}"] = _id
    _id += 1

ITEM_NAME_TO_ID["Stretchy Candy"]      = _id; _id += 1
ITEM_NAME_TO_ID["Fullness Tolerance"]  = _id; _id += 1
ITEM_NAME_TO_ID["Happiness Upgrade"]   = _id; _id += 1
ITEM_NAME_TO_ID["Milk Flow Increase"]  = _id; _id += 1
ITEM_NAME_TO_ID["Barista Smile :-)"]   = _id; _id += 1

# Cosmetics are appended last; the client mirrors this exact order when
# decoding received item IDs, so never reorder existing entries.
for _cos in COSMETICS:
    ITEM_NAME_TO_ID[_cos] = _id
    _id += 1

# Censored aliases get their OWN stable IDs at base+50 so toggling censorship on
# a slot never shifts real item IDs and both name sets coexist in the data
# package. Order: Supply Rate Increase, then censored cosmetics in COSMETICS
# order. The client mirrors this exactly (ArchipelagoClient censored ItemIdToName).
_CENSOR_ITEM_BASE = _BASE + 50
_cid = _CENSOR_ITEM_BASE
ITEM_NAME_TO_ID[CENSORED_MILK_FLOW] = _cid; _cid += 1
for _cos in COSMETICS:
    ITEM_NAME_TO_ID[CENSORED_COSMETIC[_cos]] = _cid
    _cid += 1

ITEM_CLASSIFICATIONS: dict[str, ItemClassification] = {
    **{f"Ingredient: {display_name(ing)}": ItemClassification.progression for ing in INGREDIENTS},
    "Stretchy Candy":     ItemClassification.progression,
    "Fullness Tolerance": ItemClassification.useful,
    "Happiness Upgrade":  ItemClassification.useful,
    "Milk Flow Increase": ItemClassification.useful,
    "Barista Smile :-)":  ItemClassification.filler,
    **{cos: ItemClassification.filler for cos in COSMETICS},
    # Censored aliases mirror the classification of the item they rename.
    CENSORED_MILK_FLOW:   ItemClassification.useful,
    **{CENSORED_COSMETIC[cos]: ItemClassification.filler for cos in COSMETICS},
}


class CowtasticItem(Item):
    game = "Cowtastic Cafe"


def create_item(world: CowtasticWorld, name: str) -> CowtasticItem:
    return CowtasticItem(name, ITEM_CLASSIFICATIONS[name], ITEM_NAME_TO_ID[name], world.player)


def create_all_items(world: CowtasticWorld) -> None:
    pool: list[CowtasticItem] = []

    # Starting ingredients are precollected — their locations are immediately in logic.
    for ing in STARTING_INGREDIENTS:
        world.multiworld.push_precollected(create_item(world, f"Ingredient: {display_name(ing)}"))

    # Remaining ingredients are randomized progression items.
    for ing in INGREDIENTS:
        if ing not in STARTING_INGREDIENTS:
            pool.append(create_item(world, f"Ingredient: {display_name(ing)}"))

    # Milk capacity upgrades (progression): the required amount plus any extras.
    candy_count = REQUIRED_CANDY + world.options.extra_candy.value
    for _ in range(candy_count):
        pool.append(create_item(world, "Stretchy Candy"))

    serve_locations = len(SERVE_INGREDIENTS) * world.options.checks_per_ingredient.value
    total_locations = serve_locations + world.options.shop_locations.value
    remaining = total_locations - len(pool)

    # Fill remaining slots: Milk Flow Increase (option-driven count) and the
    # other useful upgrades first, then cosmetics (which take the place of
    # generic filler), and finally plain "Barista Smile :-)" if slots are left.
    useful_flat: list[str] = ["Milk Flow Increase"] * world.options.milk_flow.value
    for name, count in USEFUL_ITEMS.items():
        useful_flat.extend([name] * count)

    fillers = useful_flat + _shuffled_cosmetics()
    censored = bool(world.options.censorship_mode.value)
    for i in range(remaining):
        name = fillers[i] if i < len(fillers) else "Barista Smile :-)"
        if censored:
            name = censor_item_name(name)  # rename only; same item, same slot
        pool.append(create_item(world, name))

    world.multiworld.itempool += pool
