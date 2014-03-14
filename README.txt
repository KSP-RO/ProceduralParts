
ProceduralParts allows you to procedurally generate (currently) fuel tanks, SRBs, Structural parts, and decouplers in a range of sizes and shapes. The parts are fully tweakable with multiple options for customization of the shape, surface texture, and other parameters.

====  Features  =====

* Everything accessible by tweaking
* A broad range of shapes including cylinders, truncated cones, filleted cylinders, bezier cones.
* New part shapes are easy to develop and plug in, so cuboid / pill shaped / whatever else you want shaped will be able to be created.
* Most stuff configurable in the config file, including resources and fill ratios, tech levels, available shapes
* Diverse support for career mode - tank shapes, dimensions, and contents all limited by researched tech
* Multiple textures available for part surfaces. These are fully compatible with StretchySRB textures.

* Tanks - to allow tech limiting different parts available for different 'groups' of fuels (Liquid fuels, SRBs, Monoprop). 
* SRBs - tweakable bells that are designed for surface or vacuum, with variable ISP, and tweakable thrust (or burn time for real fuels)
* Deprecation support for StretchySRB - you can continue to use stretchy SRBs for your existing ships, but hide all the tanks so they don't appear in the VAB
* A Module - TankContentSwitcher that can be applied to existing tanks (with say module manager) and allow their contents to be tweaked. Tweak any tank in the VAB into a Liquid fuel only or oxidizer tank.

==== Acknowlagements ====

ProceduralParts has an extended family tree:

* StretchyTanks is the original module by the great Ancient Gammoner.
* StretchySRBs was created and updated by NathanKell and e-dog.
* ProceduralParts is a near complete re-write by Swamp Ig. 

Also featuring:

* Models by Tiberion 
* Further textures by Chestburster and Dante80.
* Config code by jsimmonds


==== Installation ====

Just put the ProceduralParts folder into your GameData folder and you should be away.

If you have been using StretchySRBs, you can just delete the old GameData folder but that will cause any ships in current save games to be destroyed. 

If you just want to hide the tanks in the VAB so they don't clutter up the list, unzip StretchySRBDeprecated.zip into the GameData/StretchySRB/Parts folder and overwrite the existing config files. All this does is sets the category for the part to -1 instead of Propulsion so that they aren't visible in the VAB, so just undo this to reverse it.


==== Customization ====

The original texture customization system from StretchySRB is still present. This system works off confignodes. There is a file in the StretchyTanks/Parts folder called STTextures.cfg that defines the textures. 

You can create your own textures, place them in any location, and create a config file with STRETCHYTANKTEXTURES nodes for each texture. All documentation on the node syntax is in the STTextures.cfg file.

Most other stuff including fuel mass ratios, fuel types, tech levels ect are all available within the config file and are well documented. Note that I tried to make everything as 'stock alike' as possible to ensure the mod isn't cheaty. This means a few things have changed as compared to StretchySRB. If you want it back the old way then look through the config file and fiddle with it.

There is the scope for adding your own tank shapes, however if you want to do this you'd best get in touch with me so I can talk you though it. Other surface of revolution type tanks will be pretty easy to implement, just calculate the profile.


==== Integration with other mods ====

Proper integration with Modular Fuel Tanks or Real Fuels (REQUIRES MFT v4/RF v4 or above!) - when done stretching, go into action editor->tank setup and update each fuel amount the desired amount to get your final amount.

Note when using with Real Fuels: Stretchy SRB is ModularEngines enabled, so your thrust will be corrected when not in vacuum. It is also techlevel enabled, although only Isp will change (not casing mass). Given this the tweakable interface will change slightly between stock and using modular engines - burn time will be tweakable rather than thrust.


==== Plans for the future ====

New shapes:
- Add optional mounting pod for surface mounts to pod tank. 
- Cuboid parts, with customizable side lengths
- Extruded parts, such as hexagonal and octagonal pieces

Tank types:
- Xenon tank

Other parts:
- Procedural command module, possibly with rescaling / tweakable IVA.

==== Licence ====

Remains as CC-BY-SA 3.0 Unported.