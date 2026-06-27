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
        return "Barista Smile :-)"

    def fill_slot_data(self) -> dict:
        """Export settings the Unity client needs to match location checks."""
        loc_names, loc_items, loc_local = self._location_item_map()
        return {
            "drinks_per_check":      self.options.drinks_per_check.value,
            "checks_per_ingredient": self.options.checks_per_ingredient.value,
            "shop_locations":        self.options.shop_locations.value,
            "shop_base_price":       self.options.shop_base_price.value,
            "shop_price_step":       self.options.shop_price_step.value,
            "min_drink_quality":     self.options.min_drink_quality.value,
            "death_link":            int(self.options.death_link.value),
            "death_link_send_quality": self.options.death_link_send_quality.value,
            "death_link_penalty":    self.options.death_link_penalty.value,
            # Parallel arrays describing what each real location will hand out.
            # The client uses these to label shop buttons and to announce
            # "X sent!" (remote) / "X unlocked!" (local) via the barista.
            "loc_names":  loc_names,
            "loc_items":  loc_items,
            "loc_local":  loc_local,   # 1 = item is this player's, 0 = remote
        }

    def _location_item_map(self):
        names, items, local = [], [], []
        for loc in self.multiworld.get_locations(self.player):
            if loc.address is None:
                continue  # skip events (e.g. the Victory location)
            names.append(loc.name)
            item = loc.item
            if item is None:
                items.append("???")
                local.append(0)
            elif item.player == self.player:
                items.append(item.name)
                local.append(1)
            else:
                owner = self.multiworld.get_player_name(item.player)
                items.append(f"{item.name} ({owner})")
                local.append(0)
        return names, items, local
