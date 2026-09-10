import typing
from dataclasses import dataclass
from functools import cached_property

from Options import (
    Choice,
    Range,
    DeathLink,
    Toggle,
    DefaultOnToggle,
    StartInventoryPool,
    ItemDict,
    PerGameCommonOptions,
)

class Goal(Choice):
    """Boss Trophy required to goal.
    Eikthyr: Eikthyr
    TheElder: The Elder
    Bonemass: Bonemass
    Moder: Moder
    Yagluth: Yagluth
    Queen: Queen
    Fader: Fader"""


    auto_display_name = False
    display_name = "Goal Boss"
    option_Eikthyr = 0
    option_TheElder = 1
    option_Bonemass = 2
    option_Moder = 3
    option_Yagluth = 4
    option_Queen = 5
    option_Fader = 6

class Gifts(Range):
    """Number of gift items (useful resource bundles) added to the Valheim item pool.
    When the Valheim player receives one, the mod spawns the resources next to them.
    In a multiworld these can be found at any player's checks, so friends can
    effectively send gifts to the Valheim player by doing their own checks."""
    display_name = "Gift Items"
    range_start = 0
    range_end = 6
    default = 4


class Pranks(Range):
    """Number of prank items (hostile mob ambushes) added to the Valheim item pool.
    When the Valheim player receives one, the mod spawns hostile mobs next to them.
    Classification: Trap."""
    display_name = "Prank Items"
    range_start = 0
    range_end = 3
    default = 2


@dataclass
class ValheimOptions(PerGameCommonOptions):
    goal: Goal
    gifts: Gifts
    pranks: Pranks
