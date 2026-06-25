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
    default = 20


@dataclass
class CowtasticOptions(PerGameCommonOptions):
    drinks_per_check: DrinksPerCheck
    checks_per_ingredient: ChecksPerIngredient
    shop_locations: ShopLocations
