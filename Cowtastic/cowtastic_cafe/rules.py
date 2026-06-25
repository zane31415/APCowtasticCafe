from __future__ import annotations
from typing import TYPE_CHECKING

from .items import INGREDIENTS, CANDY_COUNT, STARTING_INGREDIENTS
from .locations import location_name

if TYPE_CHECKING:
    from .world import CowtasticWorld


def set_all_rules(world: CowtasticWorld) -> None:
    set_location_rules(world)
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


def set_completion_condition(world: CowtasticWorld) -> None:
    # Goal: collect all 20 milk capacity upgrades (Stretchy Candy).
    # We use a Victory event placed at "Max Milk Capacity" to represent this.
    world.multiworld.completion_condition[world.player] = \
        lambda state: state.has("Victory", world.player)

    victory_loc = world.get_location("Max Milk Capacity")
    victory_loc.access_rule = \
        lambda state: state.has("Stretchy Candy", world.player, CANDY_COUNT)
