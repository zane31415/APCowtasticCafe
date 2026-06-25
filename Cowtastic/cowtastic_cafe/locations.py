from __future__ import annotations
from typing import TYPE_CHECKING

from BaseClasses import Location, ItemClassification

from .items import INGREDIENTS, CowtasticItem

if TYPE_CHECKING:
    from .world import CowtasticWorld

# ---------------------------------------------------------------------------
# ID tables. IDs are stable regardless of the player's chosen option values,
# so seeds always decode correctly.
#   Serve locations: base 771771100, 10 slots per ingredient (max 10 checks).
#   Shop locations:  base 771771300, up to 50 (matches ShopLocations range_end).
# ---------------------------------------------------------------------------
_BASE = 771771100
_MAX_CHECKS = 10

_SHOP_BASE = 771771300
_MAX_SHOP = 50


def _loc_id(ingredient_index: int, check_index: int) -> int:
    return _BASE + ingredient_index * _MAX_CHECKS + check_index


def location_name(ingredient: str, check_number: int) -> str:
    """Stable AP location name, e.g. 'Serve Sugar #2'."""
    return f"Serve {ingredient} #{check_number}"


def shop_location_name(slot_number: int) -> str:
    """Stable shop location name, e.g. 'Shop Slot #3'."""
    return f"Shop Slot #{slot_number}"


# Pre-register all possible locations.
LOCATION_NAME_TO_ID: dict[str, int] = {
    location_name(ing, c + 1): _loc_id(i, c)
    for i, ing in enumerate(INGREDIENTS)
    for c in range(_MAX_CHECKS)
}
LOCATION_NAME_TO_ID.update({
    shop_location_name(n + 1): _SHOP_BASE + n
    for n in range(_MAX_SHOP)
})


class CowtasticLocation(Location):
    game = "Cowtastic Cafe"


def create_all_locations(world: CowtasticWorld) -> None:
    cafe = world.get_region("Cafe")
    checks = world.options.checks_per_ingredient.value

    for i, ing in enumerate(INGREDIENTS):
        for c in range(checks):
            name = location_name(ing, c + 1)
            loc = CowtasticLocation(world.player, name, _loc_id(i, c), cafe)
            cafe.locations.append(loc)

    # Shop locations — always accessible (no unlock required).
    for n in range(world.options.shop_locations.value):
        name = shop_location_name(n + 1)
        loc = CowtasticLocation(world.player, name, _SHOP_BASE + n, cafe)
        cafe.locations.append(loc)

    # Victory event — no ID, never shuffled into the pool.
    victory_loc = CowtasticLocation(world.player, "Max Milk Capacity", None, cafe)
    victory_item = CowtasticItem("Victory", ItemClassification.progression, None, world.player)
    victory_loc.place_locked_item(victory_item)
    cafe.locations.append(victory_loc)
