from __future__ import annotations
import math
from typing import TYPE_CHECKING

from .items import INGREDIENTS, REQUIRED_CANDY, STARTING_INGREDIENTS, display_name
from .locations import location_name, shop_location_name, censored_serve_name

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
        item_name = f"Ingredient: {display_name(ing)}"
        for c in range(checks):
            loc = world.get_location(location_name(display_name(ing), c + 1))
            # You can only serve drinks with an ingredient you've unlocked.
            loc.access_rule = lambda state, item=item_name: state.has(item, world.player)

    # Breast Milk is always available in-game, but milk production is slow, so
    # for pacing its serve checks are gated behind Stretchy Candy: check #n
    # requires n*2 candy. Clamp to the total candy in the pool so the highest
    # checks stay reachable when few candy exist.
    total_candy = REQUIRED_CANDY + world.options.extra_candy.value
    bm_display = display_name("BreastMilk")
    censored = bool(world.options.censorship_mode.value)
    for c in range(checks):
        required_candy = min((c + 1) * 2, total_candy)
        name = censored_serve_name(c + 1) if censored else location_name(bm_display, c + 1)
        loc = world.get_location(name)
        loc.access_rule = lambda state, n=required_candy: \
            state.has("Stretchy Candy", world.player, n)


def _ingredient_count(state, world: CowtasticWorld) -> int:
    # Starting ingredients are precollected, so they always count.
    return sum(1 for ing in INGREDIENTS
               if state.has(f"Ingredient: {display_name(ing)}", world.player))


def set_shop_rules(world: CowtasticWorld) -> None:
    # Gating is purely slot-position-based so it stays stable regardless of the
    # player's chosen base/step prices. The 2 starting ingredients (Milk, Coffee)
    # cover the first 3 slots; each additional pair of slots needs 1 more ingredient.
    # Formula: required = max(2, ceil((slot + 1) / 2))
    #   slot 1 → 2, slot 2 → 2, slot 3 → 2, slot 4 → 3, slot 5 → 3, slot 6 → 4 …
    for n in range(world.options.shop_locations.value):
        slot = n + 1
        required = max(2, math.ceil((slot + 1) / 2))
        loc = world.get_location(shop_location_name(slot))
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
