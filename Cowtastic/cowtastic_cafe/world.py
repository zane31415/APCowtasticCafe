from __future__ import annotations

from worlds.AutoWorld import World

from .items import ITEM_NAME_TO_ID, CowtasticItem, create_all_items, create_item
from .locations import LOCATION_NAME_TO_ID, create_all_locations
from .options import CowtasticOptions
from .regions import create_regions
from .rules import set_all_rules


class CowtasticWorld(World):
    """Cowtastic Cafe — serve drinks, unlock ingredients, and grow your milk capacity
    to reach the ultimate goal: maximum milk!"""

    game = "Cowtastic Cafe"
    options_dataclass = CowtasticOptions
    options: CowtasticOptions

    item_name_to_id = ITEM_NAME_TO_ID
    location_name_to_id = LOCATION_NAME_TO_ID

    # Players start in the Menu region.
    origin_region_name = "Menu"

    def create_regions(self) -> None:
        create_regions(self)
        create_all_locations(self)

    def set_rules(self) -> None:
        set_all_rules(self)

    def create_items(self) -> None:
        create_all_items(self)

    def create_item(self, name: str) -> CowtasticItem:
        return create_item(self, name)

    def get_filler_item_name(self) -> str:
        return "Decoration"

    def fill_slot_data(self) -> dict:
        """Export settings the Unity client needs to match location checks."""
        return {
            "drinks_per_check":      self.options.drinks_per_check.value,
            "checks_per_ingredient": self.options.checks_per_ingredient.value,
            "shop_locations":        self.options.shop_locations.value,
        }
