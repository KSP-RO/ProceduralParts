ProceduralParts allows you to procedurally generate a number of different parts in a range of sizes and shapes. The parts are fully tweakable with multiple options for customization of the shape, surface texture, and other parameters.

This is nearing official release. Will concentrate on finding any more bugs and then make an official first release.

[table="class: grid, align: left"]
[tr]
	[td][URL="https://github.com/Swamp-Ig/ProceduralParts/releases/download/v0.9.14/ProceduralParts-0.9.14.zip"][SIZE=4][B]Download[/B][/SIZE][/URL][/td]
	[td][URL="https://github.com/Swamp-Ig/ProceduralParts/issues"][SIZE=4][B]Report Bugs[/B][/SIZE][/URL][/td]
	[td][URL="https://github.com/Swamp-Ig/ProceduralParts"][SIZE=4][B]Source GIT[/B][/SIZE][/URL][/td]
[/tr]
[/table]

[SIZE=4][B]Features[/B][/SIZE]

[SIZE=3][B]The features include[/B][/SIZE]
[LIST]
[*] Everything accessible by tweaking
[*] A broad range of shapes including cylinders, truncated cones, filleted cylinders, bezier cones.
[*] New part shapes are easy to develop and plug in, so cuboid / pill shaped / whatever else you want shaped will be able to be created.
[*] Most stuff configurable in the config file, including resources and fill ratios, tech levels, available shapes
[*] Diverse support for career mode - tank shapes, dimensions, and contents all limited by researched tech
[*] All supplied parts are carefully designed to be as 'stock alike' as possible in their tech level requirements - You can't create a monster tank before you've discovered basic rocketry for example.
[*] Other mod support - tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC. Heat shields for Deadly Reentry. (thanks to OtherBarry)
[*] Plays nicely with Ferram Aerospace Research
[*] Multiple textures available for part surfaces. These are fully compatible with StretchySRB textures.
[*] Deprecation support for StretchySRB - see below for details.
[*] A Module - TankContentSwitcher that can be applied to existing tanks (with say module manager) and allow their contents to be tweaked. Tweak any tank in the VAB into a Liquid fuel only or oxidizer tank.
[/list]

[SIZE=3][B]Parts available[/B][/SIZE]
[list]
[*] [B]Tanks[/B] Different parts supplied for different 'groups' of fuels (Liquid fuels, SRBs, Monoprop, Xenon). The multiple part approach is to allow for tech limiting of sizes and volumes.
[*] [B]SRBs[/B] Tweakable thrust (or burn time for real fuels). Tweak between a choice of two bells that are designed for surface or vacuum, with varying ISPs.
[*] [B]Decoupler[/B] Tweakable diameters (with tech defined limits), ejection impulse, and can be in either decoupler or separator mode (again tech dependent).
[*] [B]Structural Part[/B] Good for fuselage, adapters, whatever. Half as light as the equivalent tank.
[*] [B]Batteries[/B] It's a bit rough and ready, but it works well enough. 
[*] [B]Nose Cone[/B] Specialized structural part for nose cones. The shape is limited to a smooth cone with a bounded ratio of diameter to length. 
[*] [B]Heat Shield[/B] Built to the same specs as Deadly Reentry. Will shield any sized object from heat. (requires deadly reentry) 
[/LIST]

[SIZE=4][B]Screen Shots[/B][/SIZE]
[imgur]AKAGF[/imgur]

[size=4][B]Installation[/B][/size]
Just extract the zip into your KSP folder and you should be away. Some of the integration with other mods requires the latest version of ModuleManager, which is included in the zip. 

[size=4][B]Upgrades[/B][/size]
[list]
[*]Make sure you delete any old versions of ProceduralParts. 
[*]There's a handful of deprecated parts as was previously used for real fuels. If you didn't use these parts, then you can safely delete the PartsDeprecated folder in the main install directory.
[/list]

[size=4][B]Known Issues[/B][/size]
[List]
[*] Sometimes if the procedural part is the lowest part on the rocket, it may explode on the launch pad. Easily worked around with a launch clamp. This is fixable but will take more effort than its worth.
[/list]

[size=4][B]Custom Textures and Texture Packs [/B][/size]
Procedural Parts is compatible with all texture packs for StretchySRBs. It's easy to [URL="https://github.com/Swamp-Ig/ProceduralParts/blob/master/Parts/STTextures.cfg"]roll your own texture packs[/URL] too. 

Here's some texture packs that other people have compiled:

[size=3][B]Planeguy868[/B][/size]
[URL="http://www.mediafire.com/download/gz8f35398bs7a14/planeguy868.zip"]Download[/URL]. 
Installation instructions: download and extract it to KSP's GameData folder.

[spoiler=preview]
[IMG]http://i.imgur.com/Zsq4zeYm.png[/IMG]
[IMG]http://i.imgur.com/6uSoyXCm.png[/IMG]
[/spoiler]

[size=3][B]Ferram4's Saturn and Nova Textures[/B][/size]
[URL="http://www.mediafire.com/download/9mi9tjb5akaiaaz/SaturnNovaTexturePack.zip"]Download[/URL]. 
Installation instructions in zip.

[spoiler=preview]
[IMG]http://i.imgur.com/YZyRRBN.jpg[/IMG]
[/spoiler]

[size=3][B]blackheart612[/B][/size]
[URL="http://forum.kerbalspaceprogram.com/threads/68892"]Full thread![/URL]

[spoiler=preview]
[imgur]m2OTv[/imgur]
[/spoiler]

[size=4][B]Compatibility with StretchyTanks / StretchySRBs [/B][/size]
This is essentially a completely new mod and can run alongside either of the previous mods. This is useful if you have pre-existing ships in your save file still using those parts. If you don't have any ships using those parts, then you can delete the old mod.

There's a module manager patch file present that will hide all the StretchySRB tanks in the VAB so they don't clutter it up. If for whatever reason you want to continue using StretchySRBs, then delete ProceduralParts\ModuleManager\StretchyTanks_Hide.cfg and this won't happen.

[size=4][B] Integration with Real Fuels and Modular Fuel Tanks [/B][/size]
Integration with Real Fuels and Modular Fuels Tanks is complete. Ensure you have Real Fuels version 6.1 or newer, and Modular Fuel Tanks 5.0.1 or newer. There's one or two bugs still to get through, stay tuned for updates on those two.

For MFT, the existing tank types are turned into the corresponding MFT type.

For real fuels, there's an SRB which can be switched between low altitude and high altitude versions, plus a tank which can be switched between the various RF tank types. 

The old real fuels system with multiple parts for different tank types is preserved as a deprecated option (hidden in the VAB). If you don't have any old tanks on ships or craft you can delete the PartsDeprecated from the root of the install.

[SIZE=4][B]Integration with other mods[/B][/SIZE]
Thanks to OtherBarry, there are now tanks for RealFuels, Kethane, Extraplanetary Launchpads, and TAC.
There's also a procedural heat-shield for Deadly Reentry.
All part's drag models will automatically update if using Ferram Aerospace Research.
The tank types will automatically appear if the mods are installed. They should be 'fair' compared to their unmodded versions.

[SIZE=4][B]How to [s]cheat in career mode[/s] have lower tech restrictions[/B][/SIZE]
The current tech restrictions have been tailored to closely mimic stock, with a bit of room to alter the original specs. Note that [B]this will not be changed[/B] with the out of the box config.

[spoiler=how to increase dimension text limits]
If you'd like more generous limits, you can create a MM patch (ie: cut and paste this into a file called mycheats.cfg in your GameData dir) and tweak to your liking:

[CODE]
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
[/CODE]
This will affect all procedural tanks and the SRB. The name of the Real Fuels SRB is "proceduralSRBRealFuels" so you'll need to make another similar patch for that one if you want to mess with that too.
[/spoiler]

[spoiler=how to allow all shapes in early tech levels]
If you'd like to be able to use all the shapes from the early game then use the following MM patch:
[CODE]
@PART[*] 
{
	@MODULE[ProceduralShape*]
	{
		-techRequired = dummy
	}
}
[/CODE]
This will affect all parts.
[/spoiler]

[SIZE=4][B]Future plans[/B][/SIZE]
[LIST]
[*] Cuboid parts, with customizable side lengths
[*] Extruded parts, such as hexagonal and octagonal pieces
[*] Add optional mounting pod for surface mounts to pod tank. 
[*] Procedural command module, possibly with rescaling / tweakable IVA.
[/LIST]

[SIZE=4][B]Features That Are Not Planned[/B][/SIZE]
[List]
[*] Shapes with 'holes' in them and concave shapes - including toroids. 
[*] Procedural wings, procedural fairings - there's good mods for these already.
[*] Procedural engines - May happen one day, but not a priority.
[/list]

[SIZE=4][B]Acknowledgements[/B][/SIZE]
[SIZE=3][B]ProceduralParts has an extended family tree[/B][/SIZE]
[list]
[*] StretchyTanks is the original module by the great Ancient Gammoner.
[*] StretchySRBs was created and updated by NathanKell and e-dog.
[*] ProceduralParts is a near complete re-write by Swamp Ig. 
[/list]

[SIZE=3][B]Also featuring[/B][/SIZE]
[list]
[*] Extensive work on config and mod integration by OtherBarry
[*] Models by Tiberion 
[*] Further textures by Chestburster and Dante80.
[*] Config code by jsimmonds
[/list]

[SIZE=4][B]Licence[/B][/SIZE]
Remains as CC-BY-SA 3.0 Unported.

[size=4][B]Change Log[/B][/size]
[SIZE=3][B]0.9.x[/B][/SIZE]
[list]
[*] Tight integration with real fuels / modular fuel tanks.
[*] Procedural heat-shields for Deadly Reentry (Thanks to OtherBarry )
[*] Updating the drag model during tweaking of parts for Ferram Aerospace Research
[*] Procedural tanks for resources in other mods: Kethane, TAC, Extraplanetary Launchpads
[*] Better formatting of tweaker values. These now show four significant figures and an SI prefix.
[*] Xenon tank
[*] Hiding of parts depending on what addins are installed
[*] Resource improvements - can set a tank to be empty in the VAB ( waste, Kethane ) or to have a fixed amount of resource regardless of the volume
[*] Battery Part
[*] Shape limiting on tech levels. 
[/list]

[URL="https://github.com/Swamp-Ig/ProceduralParts/blob/master/ChangeLog.txt"]Click for full changelog[/URL]
