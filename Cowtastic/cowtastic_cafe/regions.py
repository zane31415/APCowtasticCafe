from __future__ import annotations
from typing import TYPE_CHECKING

from BaseClasses import Region

if TYPE_CHECKING:
    from .world import CowtasticWorld


def create_regions(world: CowtasticWorld) -> None:
    # The game has one continuous play space, so one region is sufficient.
    # All ingredient-serve checks live here; access rules go on the locations.
    menu = Region("Menu", world.player, world.multiworld)
    cafe = Region("Cafe", world.player, world.multiworld)

    # The player can always enter the cafe from the menu.
    menu.connect(cafe, "Start Game")

    world.multiworld.regions += [menu, cafe]
