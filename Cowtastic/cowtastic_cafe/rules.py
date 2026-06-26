from __future__ import annotations
import math
from typing import TYPE_CHECKING

from .items import INGREDIENTS, REQUIRED_CANDY, STARTING_INGREDIENTS
from .locations import location_name, shop_location_name, shop_slot_price

if TYPE_CHECKING:
    from .world import CowtasticWorld


def set_all_rules(world: CowtasticWorld) -> None:
    set_location_rules(world)
    set_shop_rules(world)
    set_completion_condition(world)


def set_location_rules(world: CowtasticWorld) -> None:
    checks = world.options.checks_per_ingredient.value

    for ing in INGREDIENTS:
        if ing in STARTING_INGREDIENTS:
            continue  # Always reachable — no unlock needed.
        item_name = f"Ingredient: {ing}"
        for c in range(checks):
            loc = world.get_location(location_name(ing, c + 1))
            # You can only serve drinks with an ingredient you've unlocked.
            loc.access_rule = lambda state, item=item_name: state.has(item, world.player)


def _ingredient_count(state, world: CowtasticWorld) -> int:
    # Starting ingredients are precollected, so they always count.
    return sum(1 for ing in INGREDIENTS
               if state.has(f"Ingredient: {ing}", world.player))


def set_shop_rules(world: CowtasticWorld) -> None:
    # Each $50 of a shop slot's price requires one more ingredient to be in
    # logic. The 2 starting ingredients cover slots up to $100; pricier slots
    # need more. ceil(price / 50) ingredients required.
    for n in range(world.options.shop_locations.value):
        price = shop_slot_price(n + 1)
        required = math.ceil(price / 50)
        loc = world.get_location(shop_location_name(n + 1))
        loc.access_rule = lambda state, r=required: _ingredient_count(state, world) >= r


def set_completion_condition(world: CowtasticWorld) -> None:
    # Goal: trigger the in-game "barista overflows" good end, which requires
    # enough milk-capacity upgrades. We model that with a Victory event placed
    # at "Max Milk Capacity", gated behind the required number of Stretchy Candy.
    world.multiworld.completion_condition[world.player] = \
        lambda state: state.has("Victory", world.player)

    victory_loc = world.get_location("Max Milk Capacity")
    victory_loc.access_rule = \
        lambda state: state.has("Stretchy Candy", world.player, REQUIRED_CANDY)
