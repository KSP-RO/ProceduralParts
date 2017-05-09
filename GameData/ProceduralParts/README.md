ProceduralParts allows you to procedurally generate a number of different parts in a range of sizes and shapes. The parts are fully tweakable with multiple options for customization of the shape, surface texture, and other parameters.

## Features

#### The features include
* Everything accessible by tweaking
* A broad range of shapes including cylinders, truncated cones, filleted cylinders, bezier cones.
* New part shapes are easy to develop and plug in, so cuboid / pill shaped / whatever else you want shaped will be able to be created.
* Most stuff configurable in the config file, including resources and fill ratios, tech levels, available shapes
* Diverse support for career mode - tank shapes, dimensions, and contents all limited by researched tech
* All supplied parts are carefully designed to be as 'stock alike' as possible in their tech level requirements - You can't create a monster tank before you've discovered basic rocketry for example.
* Other mod support - tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC. Heat shields for Deadly Reentry. (thanks to OtherBarry)
* Plays nicely with Ferram Aerospace Research
* Multiple textures available for part surfaces. These are fully compatible with StretchySRB textures.
* Deprecation support for StretchySRB - see below for details.
* A Module - TankContentSwitcher that can be applied to existing tanks (with say module manager) and allow their contents to be tweaked. Tweak any tank in the VAB into a Liquid fuel only or oxidizer tank.

#### Parts available
* **Tanks** Different parts supplied for different 'groups' of fuels (Liquid fuels, SRBs, Monoprop, Xenon). The multiple part approach is to allow for tech limiting of sizes and volumes.
* **SRBs** Tweakable thrust (or burn time for real fuels). Tweak between a choice of two bells that are designed for surface or vacuum, with varying ISPs.
* **Decoupler** Tweakable diameters (with tech defined limits), ejection impulse, and can be in either decoupler or separator mode (again tech dependent).
* **Structural Part** Good for fuselage, adapters, whatever. Half as light as the equivalent tank.
* **Batteries** It's a bit rough and ready, but it works well enough. 
* **Nose Cone** Specialized structural part for nose cones. The shape is limited to a smooth cone with a bounded ratio of diameter to length. 
* **Heat Shield** Built to the same specs as Deadly Reentry. Will shield any sized object from heat. (requires deadly reentry) 

## Installation
Just extract the zip into your KSP folder and you should be away. Some of the integration with other mods requires the latest version of ModuleManager, which is included in the zip. 

## Upgrades
* Make sure you delete any old versions of ProceduralParts. 
* There's a handful of deprecated parts as was previously used for real fuels. If you didn't use these parts, then you can safely delete the PartsDeprecated folder in the main install directory.

## Known Issues
* Sometimes if the procedural part is the lowest part on the rocket, it may explode on the launch pad. Easily worked around with a launch clamp. This is fixable but will take more effort than its worth.

## Custom Textures and Texture Packs 
Procedural Parts is compatible with all texture packs for StretchySRBs. It's easy to [roll your own texture packs](https://github.com/Swamp-Ig/ProceduralParts/blob/master/Parts/STTextures.cfg) too. 

Here's some texture packs that other people have compiled:

#### Planeguy868
[Download](http://www.mediafire.com/download/gz8f35398bs7a14/planeguy868.zip). 
Installation instructions: download and extract it to KSP's GameData folder.

![image](http://i.imgur.com/Zsq4zeYm.png)
![image](http://i.imgur.com/6uSoyXCm.png)

#### Ferram4's Saturn and Nova Textures
[Download](http://www.mediafire.com/download/9mi9tjb5akaiaaz/SaturnNovaTexturePack.zip). 
Installation instructions in zip.

![image](http://i.imgur.com/YZyRRBN.jpg)

#### blackheart612
[Full thread!](http://forum.kerbalspaceprogram.com/threads/68892)
Install instructions and sample images in link.

## Compatibility with StretchyTanks / StretchySRBs 
This is essentially a completely new mod and can run alongside either of the previous mods. This is useful if you have pre-existing ships in your save file still using those parts. If you don't have any ships using those parts, then you can delete the old mod.

There's a module manager patch file present that will hide all the StretchySRB tanks in the VAB so they don't clutter it up. If for whatever reason you want to continue using StretchySRBs, then delete ProceduralParts\ModuleManager\StretchyTanks_Hide.cfg and this won't happen.

##  Integration with Real Fuels and Modular Fuel Tanks 
Integration with Real Fuels and Modular Fuels Tanks is complete. Ensure you have Real Fuels version 6.1 or newer, and Modular Fuel Tanks 5.0.1 or newer. There's one or two bugs still to get through, stay tuned for updates on those two.

For MFT, the existing tank types are turned into the corresponding MFT type.

For real fuels, there's an SRB which can be switched between low altitude and high altitude versions, plus a tank which can be switched between the various RF tank types. 

The old real fuels system with multiple parts for different tank types is preserved as a deprecated option (hidden in the VAB). If you don't have any old tanks on ships or craft you can delete the PartsDeprecated from the root of the install.

## Integration with other mods
Thanks to OtherBarry, there are now tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC.
There's also a procedural heat-shield for Deadly Reentry.
All part's drag models will automatically update if using Ferram Aerospace Research.
The tank types will automatically appear if the mods are installed. They should be 'fair' compared to their unmodded versions.

## How to ~~cheat in career mode~~ have lower tech restrictions
The current tech restrictions have been tailored to closely mimic stock, with a bit of room to alter the original specs. Note that **this will not be changed** with the out of the box config.

If you'd like more generous limits, you can create a MM patch (ie: cut and paste this into a file called mycheats.cfg in your GameData dir) and tweak to your liking:

~~~~
@PART[proceduralTank*] 
{
	@MODULE[ProceduralPart]
	{
		@TECHLIMIT,*
		{
			// Increase the max length for all tech levels by 3*
			@lengthMax *= 3
			// Corresponding volume increase
			@volumeMax *= 3

			// Increase the max diameter by double
			@diameterMax *= 2
			// Since volume goes up on diameter^2, need to use increase^2
			@volumeMax *= 4
		}
	}
}
~~~~
This will affect all procedural tanks and the SRB. The name of the Real Fuels SRB is "proceduralSRBRealFuels" so you'll need to make another similar patch for that one if you want to mess with that too.

If you'd like to be able to use all the shapes from the early game then use the following MM patch:
~~~~
@PART[*] 
{
	@MODULE[ProceduralShape*]
	{
		-techRequired = dummy
	}
}
~~~~
This will affect all parts.

## Future plans
* Cuboid parts, with customizable side lengths
* Extruded parts, such as hexagonal and octagonal pieces
* Add optional mounting pod for surface mounts to pod tank. 
* Procedural command module, possibly with rescaling / tweakable IVA.

## Features That Are Not Planned
* Shapes with 'holes' in them and concave shapes - including toroids. 
* Procedural wings, procedural fairings - there's good mods for these already.
* Procedural engines - May happen one day, but not a priority.

## Acknowledgements
#### ProceduralParts has an extended family tree
* StretchyTanks is the original module by the great Ancient Gammoner.
* StretchySRBs was created and updated by NathanKell and e-dog.
* ProceduralParts is a near complete re-write by Swamp Ig. 

#### Also featuring
* Extensive work on config and mod integration by OtherBarry
* Models by Tiberion 
* Further textures by Chestburster and Dante80.
* Config code by jsimmonds
