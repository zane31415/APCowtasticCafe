from dataclasses import dataclass
from Options import Range, Choice, Toggle, DeathLink, PerGameCommonOptions


class DrinksPerCheck(Range):
    """How many drinks with each ingredient you must serve to earn one check.
    Example: with 3 drinks per check and 3 checks per ingredient,
    checks fire when you've served 3, 6, and 9 drinks with that ingredient."""
    display_name = "Drinks Per Check"
    range_start = 1
    range_end = 20
    default = 3


class ChecksPerIngredient(Range):
    """How many location checks exist per ingredient (14 ingredients have serve
    checks: the 13 unlockable ones plus always-available Breast Milk).
    At the default of 4, with 10 shop slots, all cosmetics fit in the pool with
    room to spare. More than that pads the game out with filler; fewer checks
    per ingredient will triage away cosmetics (and then useful items).
    Minimum 2: below that the mandatory items don't fit (14*2 + 10 = 38 >= 31)."""
    display_name = "Checks Per Ingredient"
    range_start = 2
    range_end = 10
    default = 4


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
    barista's actual milk production rate. Set to 0 to have none.
    Note: useful and cosmetic items are only added until locations are filled, so
    high values here combined with low check counts may result in fewer items
    being placed than requested.
    Also note: There is currently no way to lower this flow once received. Only
    play with the max flow you're willing to play with."""
    display_name = "Milk Flow Increases"
    range_start = 0
    range_end = 20
    default = 5


class MinDrinkQuality(Choice):
    """Minimum drink rating required to earn a serve-location check.
    Perfect is the strictest; Okay is the most lenient."""
    display_name = "Minimum Drink Quality"
    option_perfect = 0
    option_great   = 1
    option_good    = 2
    option_okay    = 3
    default = 2  # Good


class DeathLinkSendQuality(Choice):
    """When DeathLink is enabled, serving a drink rated at this quality or
    worse sends a death to all DeathLink participants. Ratings run from best to
    worst: Great, Good, Okay, Meh, Bad, Ruined. (Perfect is excluded — it can
    never be a death trigger.)"""
    display_name = "DeathLink Send Quality"
    option_great   = 1
    option_good    = 2
    option_okay    = 3
    option_meh     = 4
    option_bad     = 5
    option_ruined  = 6
    default = 4  # Meh or below


class DeathLinkPenalty(Range):
    """How much happiness the barista loses when a DeathLink is received
    from another player. 0 = no effect; 100 = full happiness wipe."""
    display_name = "DeathLink Penalty"
    range_start = 0
    range_end   = 100
    default = 20


class ShopBasePrice(Range):
    """Starting price (in coins) for the first shop slot.
    Logic gating uses slot position, not actual price, so changing this only
    affects in-game cost, not item accessibility."""
    display_name = "Shop Base Price"
    range_start = 10
    range_end = 100
    default = 50


class ShopPriceStep(Range):
    """How much each subsequent shop slot costs compared to the previous one.
    Slot N costs base + step*(N-1), capped at 550.
    Logic gating is based on slot position (every 2 slots = 1 more ingredient
    required), so changing this only affects in-game cost."""
    display_name = "Shop Price Step"
    range_start = 5
    range_end = 50
    default = 25


@dataclass
class CowtasticOptions(PerGameCommonOptions):
    drinks_per_check: DrinksPerCheck
    checks_per_ingredient: ChecksPerIngredient
    shop_locations: ShopLocations
    extra_candy: ExtraCandy
    milk_flow: MilkFlow
    shop_base_price: ShopBasePrice
    shop_price_step: ShopPriceStep
    min_drink_quality: MinDrinkQuality
    death_link: DeathLink
    death_link_send_quality: DeathLinkSendQuality
    death_link_penalty: DeathLinkPenalty
