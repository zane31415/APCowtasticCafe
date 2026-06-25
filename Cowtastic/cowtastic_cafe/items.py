from __future__ import annotations
from typing import TYPE_CHECKING

from BaseClasses import Item, ItemClassification

if TYPE_CHECKING:
    from .world import CowtasticWorld

INGREDIENTS = [
    "Espresso", "Coffee", "Chocolate", "Tea", "Milk",
    "BreastMilk", "Cream", "Sugar", "Ice", "Boba", "Sprinkles",
]

# These ingredients are unlocked from the start in the base game.
# Their serve locations are still in logic immediately; no unlock item is placed.
STARTING_INGREDIENTS = {"Milk", "Coffee"}

# Milk capacity upgrade count must match the game's number of size upgrades.
CANDY_COUNT = 20

# Useful non-filler items placed after mandatory progression items.
USEFUL_ITEMS: dict[str, int] = {
    "Fullness Tolerance": 5,
    "Happiness Upgrade":  5,
    "Max Flow Upgrade":   5,
    "Min Flow Upgrade":   5,
}

# ---------------------------------------------------------------------------
# ID table — IDs must be globally unique across all apworlds.
# Using 771771000 as the base; reserve 000–099 for items, 100+ for locations.
# ---------------------------------------------------------------------------
_BASE = 771771000
_id = _BASE

ITEM_NAME_TO_ID: dict[str, int] = {}

for _ing in INGREDIENTS:
    ITEM_NAME_TO_ID[f"Ingredient: {_ing}"] = _id
    _id += 1

ITEM_NAME_TO_ID["Stretchy Candy"]      = _id; _id += 1
ITEM_NAME_TO_ID["Fullness Tolerance"]  = _id; _id += 1
ITEM_NAME_TO_ID["Happiness Upgrade"]   = _id; _id += 1
ITEM_NAME_TO_ID["Max Flow Upgrade"]    = _id; _id += 1
ITEM_NAME_TO_ID["Min Flow Upgrade"]    = _id; _id += 1
ITEM_NAME_TO_ID["Decoration"]          = _id; _id += 1

ITEM_CLASSIFICATIONS: dict[str, ItemClassification] = {
    **{f"Ingredient: {ing}": ItemClassification.progression for ing in INGREDIENTS},
    "Stretchy Candy":     ItemClassification.progression,
    "Fullness Tolerance": ItemClassification.useful,
    "Happiness Upgrade":  ItemClassification.useful,
    "Max Flow Upgrade":   ItemClassification.useful,
    "Min Flow Upgrade":   ItemClassification.useful,
    "Decoration":         ItemClassification.filler,
}


class CowtasticItem(Item):
    game = "Cowtastic Cafe"


def create_item(world: CowtasticWorld, name: str) -> CowtasticItem:
    return CowtasticItem(name, ITEM_CLASSIFICATIONS[name], ITEM_NAME_TO_ID[name], world.player)


def create_all_items(world: CowtasticWorld) -> None:
    pool: list[CowtasticItem] = []

    # Starting ingredients are precollected — their locations are immediately in logic.
    for ing in STARTING_INGREDIENTS:
        world.multiworld.push_precollected(create_item(world, f"Ingredient: {ing}"))

    # Remaining ingredients are randomized progression items.
    for ing in INGREDIENTS:
        if ing not in STARTING_INGREDIENTS:
            pool.append(create_item(world, f"Ingredient: {ing}"))

    # All 20 milk capacity upgrades are progression; collecting them all is the goal.
    for _ in range(CANDY_COUNT):
        pool.append(create_item(world, "Stretchy Candy"))

    serve_locations = len(INGREDIENTS) * world.options.checks_per_ingredient.value
    total_locations = serve_locations + world.options.shop_locations.value
    remaining = total_locations - len(pool)

    # Fill remaining slots with useful upgrades first, then filler decorations.
    useful_flat: list[str] = []
    for name, count in USEFUL_ITEMS.items():
        useful_flat.extend([name] * count)

    for i in range(remaining):
        name = useful_flat[i] if i < len(useful_flat) else "Decoration"
        pool.append(create_item(world, name))

    world.multiworld.itempool += pool
