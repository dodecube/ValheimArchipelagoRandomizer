INSTALLATION (MOD)
Valheim Archipelago Randomizer requires BepInEx and Jotunn in order to work properly. You can get them from: https://github.com/Valheim-Modding/Jotunn

Once both BepInEx and Jotunn have been installed, unzip the ValheimRandomizer zip file into your Valheim install directory. When you are finished, the files for the mod should be present in the Valheim/BepInEx/plugins/ValheimRandomizer directory.

If you use r2modman/Thunderstore: the mod looks for research.tsv and trophies.tsv first next to ValheimRandomizer.dll, then in BepInEx/plugins/ValheimRandomizer/. If the Research Bench is empty and the log shows DirectoryNotFoundException for research.tsv, open the profile folder (Settings -> Browse profile folder), go to BepInEx/plugins/, and make sure research.tsv and trophies.tsv sit either next to the DLL or in BepInEx/plugins/ValheimRandomizer/. You can copy them from the release zip (BepInEx/plugins/ValheimRandomizer/ inside the archive).

Once the game is open a popup window will let you connect to Archipelago.
Make sure that the 'Randomized' checkbox is checked before connecting to the AP server.

There is a non randomized mode in order to play "vanilla" progression.

INSTALLATION (APWORLD)
Open the apworld file and install normally.

PLAYING THE GAME
AP checks in this randomizer come from two sources:

The research bench:

The research bench is a new build item added by the randomizer. New researches will show up there based on your available materials and the currently unlocked tier.
Researching these items at the bench does not directly result in unlocking technologies. Rather, each research item is an AP item check. To unlock the technologies, the technology needs to be found as the item given from an AP check.
When receiving a new technology from an AP check, the build items often won't appear until the game detects the materials required for the first time. This can be from picking up the required materials, or if you already have them, simply rearranging them in your inventory will force the game to examine them again. For example, if you receive Wooden Walls, but do not see them available yet as a build item, picking up more wood or moving wood around in your inventory should make the game notice the recipe and report it to you.

The first time each trophy type is gained, it also results in an AP check being sent. When a trophy is RECEIVED from an AP check, permanent bonuses to various stats, such as Attack/Defense/HP/HP Regen/etc... are gained, based on what type of trophy was obtained.




GIFTS & PRANKS (0.2.6+)
The yaml options "gifts" (0-6) and "pranks" (0-3) add AP items to the Valheim pool.
When the Valheim player receives a gift, resources spawn next to them (Wood/Stone/Coal/Honey/Coins/CookedMeat).
When they receive a prank (Trap), hostile mobs spawn next to them (Greydwarfs/Wolves/Troll/Deathsquitos).
In a multiworld these items can be placed at any player's locations, so other players send gifts to Valheim by doing their own checks.
To keep items == locations, exactly (gifts + pranks) exploration checks exist per seed: first visits to Meadows, Black Forest, Ocean, Swamp, Mountain, Plains, Mistlands, Ashlands, Deep North (in that order). The mod learns which ones exist from slot_data["biomes"].
Gifts are tracked by server index in received_<slot>.txt next to the DLL so they are never duplicated on relog. If you reuse a slot name for a new seed, delete that file or run the console command apgift_reset.
Note: spawning happens on the Valheim client, so co-op hosting (host = a player's game) is recommended over a dedicated server.

FILES USED
research.tsv
	Contains all the available researches in the game. They will appear on the Research Bench and are used to gate repices behind a tiered list of researches.
trophy.tsv
	Contains special researches that'll unlock automatically upon collecting a specified item. It'll result in a permanent player boost unless None is specified.

TIER EXPLANATION
Tier 0 - Hammer
		Wood Stone Resin Feathers BoneFragments LeatherScraps Flint Dandelion Honey Raspberry Blueberries CarrotSeeds GreydwarfEye Mushroom SurtlingCore Thistle MushroomYellow
	[GATE] Workbench
Tier 1 - Stone/Wood/Flint
		BjornHide BjornPaw Ectoplasm TrollHide AncientSeed
	[GATE] Stone Axe
Tier 2 - Corewoord
		RoundLog
	[GATE] Antler pickaxe + Forge + Smelter + Charcoal Source(any of Raft (Surtlings in swamp)/Charcoal Kiln/(Cooking station + Firepit))
Tier 3 - Tin/Copper/Chitin
		Tin Copper CopperOre TinOre Chitin
	[GATE] Bronze
Tier 4 - Bronze
		Bloodbag Chain Entrails Guck Ooze Root TurnipSeeds 
	[GATE] BronzeAxe + Any ship
Tier 5 - Finewood/Ancient Bark AmberPearl Ruby Amber
		Finewood ElderBark
	[GATE] Bronze pickaxe
Tier 6 - Iron
		Iron WitheredBone Coins SilverNecklace
	[GATE] Iron Pickaxe + Any cold resist (Lox Cape/Wolf Fur Cape/Frost Resistance Mead/Firepit)
Tier 7 - Silver
		Silver SilverOre Obsidian Crystal WolfClaw WolfHairBundle FreezeGland Onion JuteRed WolfFang WolfPelt
	[GATE] Blast Furnace + Artisan Table
Tier 8 - Black Metal + Spinning wheel
		BlackMetalScrap Barley Cloudberry Flax LoxPelt Needle Tar UndeadBjornRibcage
	[GATE] Spinning wheel + Black Metal Axe + Wisp
Tier 9 - Yggdrassil
		YggdrasilWood Bilebag BlackCore GiantBloodSack JuteBlue Carapace DvergrNeedle MushroomJotunPuffs MushroomMagecap Mandible RoyalJelly ScaleHide
	[GATE] Black Metal Pickaxe + Sap Extractor + Eitr Refinery
Tier 10 - Refined Eitr
		Eitr Softtissue Sap Wisp BlackMarble
	[GATE] Drakkar
Tier 11 - Flametal
		Grausten FlametalNew Blackwood AskBladder AskHide AsksvinCarrionNeck AsksvinCarrionPelvic AsksvinCarrionRibcage AsksvinCarrionSkull BonemawSerpentTooth CelestialFeather CharcoalResin CharredBone Fiddleheadfern MoltenCore MorgenHeart Pot_Shard_Green ProustitePowder MushroomSmokePuff SulfurStone Vineberry


CUSTOMIZATION
Both research and trophies files can be modified. Upon launching the game the mod will create two files for the pieces and recipes that are not yet mentioned in the researches. Mod content can be added to these files.
If those files are modified the apworld needs to change accordingly. Use the tool ValheimRandomizerParser by putting both research.tsv and trophies.tsv files in the same folder. New files will be created to substitute the ones in the apworld.
To recreate a new apworld just modify the python files accordingly, zip the folder and change the extension to apworld (should be named valheimapworld.apworld).
