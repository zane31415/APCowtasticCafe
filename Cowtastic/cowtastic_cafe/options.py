from dataclasses import dataclass
from Options import Range, PerGameCommonOptions


class DrinksPerCheck(Range):
    """How many drinks with each ingredient you must serve to earn one check.
    Example: with 3 drinks per check and 3 checks per ingredient,
    checks fire when you've served 3, 6, and 9 drinks with that ingredient."""
    display_name = "Drinks Per Check"
    range_start = 1
    range_end = 20
    default = 3


class ChecksPerIngredient(Range):
    """How many location checks exist per unlocked ingredient.
    Note: must be at least 3 to fit all 20 milk capacity upgrades into the item pool
    alongside all 11 ingredient unlocks (11 * 3 = 33 locations >= 31 mandatory items)."""
    display_name = "Checks Per Ingredient"
    range_start = 3
    range_end = 10
    default = 3


class ShopLocations(Range):
    """How many shop location checks exist across the run.
    The in-game shop shows the next 6 unpurchased slots at a time;
    buying one sends a check and reveals the next slot in the queue."""
    display_name = "Shop Locations"
    range_start = 0
    range_end = 50
    default = 10


class ExtraCandy(Range):
    """Additional Stretchy Candy (milk-capacity upgrades) added to the pool
    beyond the amount required to win. The goal is the in-game 'barista
    overflows' good end; in Arcade mode 19 candy are required to grow the
    barista enough to trigger it. Extras are a buffer and let you keep growing
    past the requirement."""
    display_name = "Extra Candy"
    range_start = 0
    range_end = 30
    default = 1


class MilkFlow(Range):
    """How many 'Milk Flow Increase' items are in the pool. Each one raises the
    barista's actual milk production rate. Set to 0 to have none. They're added
    as space permits alongside the other useful upgrades."""
    display_name = "Milk Flow Increases"
    range_start = 0
    range_end = 20
    default = 5


@dataclass
class CowtasticOptions(PerGameCommonOptions):
    drinks_per_check: DrinksPerCheck
    checks_per_ingredient: ChecksPerIngredient
    shop_locations: ShopLocations
    extra_candy: ExtraCandy
    milk_flow: MilkFlow
