
ProceduralParts allows you to procedurally generate (currently) fuel tanks, SRBs, Structural parts, and decouplers in a range of sizes and shapes. The parts are fully tweakable with multiple options for customization of the shape, surface texture, and other parameters.

## Features

The features include:
[*] Everything accessible by tweaking
[*] A broad range of shapes including cylinders, truncated cones, filleted cylinders, bezier cones.
[*] New part shapes are easy to develop and plug in, so cuboid / pill shaped / whatever else you want shaped will be able to be created.
[*] Most stuff configurable in the config file, including resources and fill ratios, tech levels, available shapes
[*] Diverse support for career mode - tank shapes, dimensions, and contents all limited by researched tech
[*] All supplied parts are carefully designed to be as 'stock alike' as possible in their tech level requirements - You can't create a monster tank before you've discovered basic rocketry for example.
[*] Other mod support - tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC. (thanks to OtherBarry)
[*] Multiple textures available for part surfaces. These are fully compatible with StretchySRB textures.
[*] Deprecation support for StretchySRB - see below for details.
[*] A Module - TankContentSwitcher that can be applied to existing tanks (with say module manager) and allow their contents to be tweaked. Tweak any tank in the VAB into a Liquid fuel only or oxidizer 

Parts available:
* Tanks Different parts supplied for different 'groups' of fuels (Liquid fuels, SRBs, Monoprop, Xenon). The multiple part approach is to allow for tech limiting of sizes and volumes.
* SRBs Tweakable thrust (or burn time for real fuels). Tweak between a choice of two bells that are designed for surface or vacuum, with varying ISPs.
* Decoupler Tweakable diameters (with tech defined limits), ejection impulse, and can be in either decoupler or separator mode (again tech dependent).
* Structural Part Good for fuselage, adapters, whatever. Half as light as the equivalent tank.
* Nose Cone Specialized structural part for nose cones. The shape is limited to a smooth cone with a bounded ratio of diameter to length. 

## Installation

Just extract the zip into your KSP folder and you should be away.
Some of the integration with other mods requires the latest version of ModuleManager, which is included in the zip.

## Upgrades

Make sure you delete any old versions of ProceduralParts
There is sometimes some changes to parts which may require editing of your save file if you have ships in flight. Details of how to do this [are here](https://github.com/Swamp-Ig/ProceduralParts/wiki/Upgrading-between-versions)

## Customization

The original texture customization system from StretchySRB is still present. This system works off confignodes. There is a file in the StretchyTanks/Parts folder called STTextures.cfg that defines the textures. 

You can create your own textures, place them in any location, and create a config file with STRETCHYTANKTEXTURES nodes for each texture. All documentation on the node syntax is in the STTextures.cfg file.

Most other stuff including fuel mass ratios, fuel types, tech levels etc are all available within the config file and are well documented. Note that I tried to make everything as 'stock alike' as possible to ensure the mod isn't cheaty. This means a few things have changed as compared to StretchySRB. If you want it back the old way then look through the config file and fiddle with it.

There is the scope for adding your own tank shapes, however if you want to do this you'd best get in touch with me so I can talk you though it. Other surface of revolution type tanks will be pretty easy to implement, just calculate the profile.

## Compatibility with StretchyTanks / StretchySRBs

This is essentially a completely new mod and can run alongside either of the previous mods. This is useful if you have pre-existing ships in your save file still using those parts. If you don't have any ships using those parts, then you can delete the old mod.

There's a module manager patch file present that will hide all the StretchySRB tanks in the VAB so they don't clutter it up. If for whatever reason you want to continue using StretchySRBs, then delete ProceduralParts\ModuleManager\StretchyTanks_Hide.cfg and this won't happen.

## Integration with Real Fuels

Real Fuels integration is a work in progress, but does currently work fairly reasonably. You need the latest version of RF - v5.1.

I have created several different tank types including Standard, Cryo, Balloon, BalloonCryo, and Service.

For the SRBs, there are two parts available, one for surface and one for vac. You can tweak the sea-level thrust, but all the information displays as available in stock stretchy SRBs will require an update to Real Fuels to get it working.

Ultimately all the different tank types will be rolled into one part, with a tweaker to switch between the different tank types available to RF. Similar treatment will be given to the SRBs. When this happens the existing parts will be deprecated and optionally either hidden in the VAB, or removed entirely (if you have no flying ships).

Getting this to work will require some updates to real fuels, there's no specific time-frame, I will need to collaborate with the RF developers. Be patient 

You no longer need to download a separate part pack for real fuels, the mod will detect if it's installed and offer you the right tanks.

## Integration with other mods

Thanks to OtherBarry, there are now tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC.
The tank types will automatically appear if the mods are installed. They should be 'fair' compared to their unmodded versions.

## Plans for the future

* Improve functionality with Real Fuels. This will need some code changes on the real fuels end.
* Cuboid parts, with customizable side lengths
* Extruded parts, such as hexagonal and octagonal pieces
* Add optional mounting pod for surface mounts to pod tank.
* Procedural command module, possibly with rescaling / tweakable IVA.

## Features That Are Not Planned

* Shapes with 'holes' in them and concave shapes - including toroids. 
* Procedural wings, procedural fairings - there's good mods for these already.
* Procedural engines - May happen one day, but not a priority.

## Acknowledgements

ProceduralParts has an extended family tree:

* StretchyTanks is the original module by the great Ancient Gammoner.
* StretchySRBs was created and updated by NathanKell and e-dog.
* ProceduralParts is a near complete re-write by Swamp Ig. 

Also featuring:

* Extensive work on config and mod integration by OtherBarry
* Models by Tiberion 
* Further textures by Chestburster and Dante80.
* Config code by jsimmonds

## Licence

Remains as CC-BY-SA 3.0 Unported.