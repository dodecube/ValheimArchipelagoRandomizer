from __future__ import annotations
from typing import List, Dict, Any, cast

import itertools


from worlds.AutoWorld import World
from .Regions import create_regions
from .Locations import create_locations, location_table, biome_location_data_table
from .Items import create_item, create_items, item_table, ValheimItem
from . import Options

class ValheimWorld(World):
    """
    Valheim world integration for Archipelago.
    Regions and locations are created in `create_regions` (including a call to `create_locations`).
    Items are created in `create_items`.
    """
    game = "Valheim"
    location_name_to_id = location_table
    item_name_to_id = item_table
    options_dataclass = Options.ValheimOptions
    options: Options.ValheimOptions
    topology_present = True
    
    def create_regions(self):
        """
        Creates regions and locations for the world.
        `create_regions` internally calls `create_locations`.
        """
        create_regions(self)
        create_locations(self)

    def create_items(self):
        """
        Populates the item pool with items for the world.
        """
        create_items(self)

    def create_item(self, name: str) -> Item:
        return create_item(self, name, self.player)


    def fill_slot_data(self) -> Dict[str, Any]:
        vanilla_tech: List[str] = []
        trophyname="TrophyEikthyr"
        goaloption=self.options.goal.current_key
        match goaloption:
            case "eikthyr":
                trophyname="TrophyEikthyr"
            case "theelder":
                trophyname="TrophyTheElder"
            case "bonemass":
                trophyname="TrophyBonemass"
            case "moder":
                trophyname="TrophyDragonQueen"
            case "yagluth":
                trophyname="TrophyGoblinKing"
            case "queen":
                trophyname="TrophySeekerQueen"
            case "fader":
                trophyname="TrophyFader"
            case _:
                trophyname="NOMATCH"

        # Tell the mod which biome event checks exist in this seed
        # (game_id looks like "loc:eventBiomeMeadows" -> event id "eventBiomeMeadows").
        active_biomes = self.options.gifts.value + self.options.pranks.value
        biomes = [loc.game_id[4:] for loc in biome_location_data_table[:active_biomes]]

        slot_data: Dict[str, Any] = {
            "goal": trophyname,
            "biomes": biomes,
        }

        return slot_data

    def set_rules(self):
        trophyname="TrophyEikthyr"
        goaloption=self.options.goal.current_key
        match goaloption:
            case "eikthyr":
                trophyname="TrophyEikthyr"
            case "theelder":
                trophyname="TrophyTheElder"
            case "bonemass":
                trophyname="TrophyBonemass"
            case "moder":
                trophyname="TrophyDragonQueen"
            case "yagluth":
                trophyname="TrophyGoblinKing"
            case "queen":
                trophyname="TrophySeekerQueen"
            case "fader":
                trophyname="TrophyFader"
            case _:
                trophyname="NOMATCH"
        self.multiworld.completion_condition[self.player] = lambda state: state.can_reach_location(f"Trophy: {trophyname}", self.player)
