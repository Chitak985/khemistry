<img src="https://raw.githubusercontent.com/Chitak985/khemistry/refs/heads/main/Khemistry/Flags/Logo.png">
A GregTech-like mod for KSP, it adds many resources, recipes, machines, and long and complicated processing lines. Start from primitive tools in the Primitive Age and progress further through the game, eventually obtaining your first solid rocket boosters to begin your journey to space! <br><br>
It also has some very unique features, including but not limited to:

- A resource and recipe library that automatically fetches data on every single currently loaded resource! Extremely useful if you want to understand processing chains and how to make something. It is inspired by NEI for Minecraft.
- EVA items that can now move resources around different vessels, removing the need for things like KAS and docking ports simply to transfer a few resources. No longer have to deal with KSP glitchiness to transfer a resource, just EVA a kerbal and move it manually!
- Resource deposits that must be found in a specific biome and are required to even start progressing! Eventually you will need to set up mining outposts to automate mining so far away from your base.
- ISRUs that may need maintenance, charging, passively provided inputs, and are even affected by the current biome, planet, pressure, temperature, and many other environmental conditions!
- Degrading batteries that will become useless after some time based on complex equations with numerous variables, making it depend on charging cycles and quality of the battery. They will slowly degrade to zero capacity over the course of their life, however there are some batteries that are advanced enough to last years or even forever!
- Advanced storage containers that can charge, consume something to function, explode if things inside are not contained, store multiple types of resources at the same time using a shared volume, have maximum transfer speeds to simulate real world logistics, and be affected by the environment just like ChitakTech ISRUs!
- Complex processing lines that require deep understanding of the mod and its machines, making it a similar challenge to GregTech, Factorio, and Satisfactory!

## ChitakTech Construction Overhaul
An addon for ChitakTech, this mod overhauls VAB construction to require resources! This mod must be installed with ChitakTech and makes it play a bigger role than just more complicated ISRU processes. This mod is currently in development and including it as of now (10/14/2026) will make some parts impossible to construct due to various required resources being impossible to obtain or even hold.

**IMPORTANT**: Once it is complete, the mod will be integrated into ChitakTech, making it required and only possible to disable it with a config option!

## Known Issues:
- Far Future Technologies does not work properly with the mod in many ways. 
- Far Future Technologies replaces one of ChitakTech's patches and it is fixed by making one of the tanks forced to hold Uranium Oxide Powder and not have multiple variants, which could be problematic for some people. 
- Near Future Electrical will not allow KSP to load unless the Legacy folder is deleted. 
- Near Future Construction causes a fatal B9 Part Switch error "Ore tank is not found!" which is caused by a mod overwriting ChitakTech's patches. It will be fixed soon.
- Solid Fuel isn't replaced by any ChitakTech resource yet.
- Kerbalism is expected to be extremely glitchy but have not yet been tested.

## Dependencies:
- **ModuleManager** (extremely important)
- **Community Resource Pack** (needed mostly because its resources are used by other mods) 
- **Kopernicus** (used to add custom ores for all planets, mod may break without it)
- **B9 Part Switch** (used to assign the custom per planet ores to cargo tanks and some storage, mod will easily break without it)
- **SystemHeat** (used for many things, without it, some ISRU and all drills will become unusable)
- **ParallaxContinued** (yes it's a dependency, you can chop down trees and do similar things with its scatter objects)
- **CustomPrelaunchChecks** (used for the ChitakTechConstructionOverhaul addon so not strictly required for the base mod)
- **Resourceful Technologies** (my ISRU parts mod, ChitakTech integrates various parts from there into this mod)
- **Far Future Technologies** (while not strictly required and the game will work without it, its parts are used for progression) 

## Fully Supported Mods:
- **Station Parts Expansion Redux** (cargo parts can hold some planet-speciifc resources) 
- **Near Future Propulsion** 
- **Near Future Construction**
- **Near Future Electrical** (parts from this mod have full support, even deprecated ones. May have some problems with reactor core lifetime calculations. IMPORTANT: The "Legacy" folder inside the mod needs to be deleted in order for the game to finish loading!) 
- **CryoTanks** 
- **SpaceDust** (used to create resource belts around planets and atmosphere resources) 
- **Kerbalism** (this thing is kind of insane, I don't know if the mod even works properly with it as I have never tested it. Any feedback is appreciated!)

## Configurable Per-Version Support:
- **Promised Worlds Debdeb** (1.2.1)
- **Promised Worlds Tunn** (1.2.1)

## Full Support Using Procedural Patches
- **Kerbal Atomics** 
- **Near Future Exploration** 
- **Near Future Aeronautics** 
- **MkIVSystem** 
- **Near Future Spacecraft** 
- **Near Future Launch Vehicles**
- **Modular Fuel System**
- **Celestial Harmony** (0.9.3)

## Supported Mods:
- **Real Solar System** (switch from Kerbin resources to Earth resources and some other things) 

## Incompatible Mods:
These mods will be suported soon: 
- **ProceduralWings**
- **ProceduralFuelTanks**
- **Snacks**
- **USI Life Support**
- **Tac Life Support**

## Fully Supported Mods Without Support:
These mods are confirmed to work flawlessly with ChitakTech and need no patches to be compatible. 
- **Near Future Solar** 
- **Near Future Props** 
- **Heat Control**
- **Radioactivity** 
- **Deployable Engines** 
- **Dynamic Battery Storage** 
- **Waterfall** 
- **Waterfall Restock** 
- **Community Tech Tree**
- **Universal Storage 2**
- **Ferram Aerospace Research (FAR)**
- **Connected Living Space (CLS)**

## Current Statistics:
(As of 10/14/2026)

### KSP Resources
- 231x Alloys
- ULV-MAX Circuits
- 6x Composite Material
- 71x Construction Resources
- 25x Electric Resources (All GT tiers + additional MAX voltages, ULV through MAX+10)
- 1x Electronium Resource (temporary, used for Oprissmian's TheFiveEchoes mod)
- 702x EPDM polymers (will be replaced with a material)
- 3x Fuel Assemblies
- 3x Fuel Pellets
- 4x Fuel Resources
- 3x Fuel Rods
- 45x Impure Resource
- 18x Isotopes
- 79x Natural Resources
- 76x Not So Procedural Resources (these resources will be replaced by procedural generation)
- All Periodic Table Elements
- 4x Ores
- 76x Other Resources
- 3x Powders
- 78x SandLine-related Resources
- 7x "Snack"s

## ChitakTech Recipes
- 2x Acid Washing
- 3x Atmosphere Collector
- 2x Attrition Scrubbing
- 4x Cooling
- 2x Crushing
- 1x Distillation Tower
- 3x Electrolysis
- 8x Electrostatic Separation
- 6x Furnace 
- 4x Gravitational Collapser 
- 5x Gravitational Separator
- 16x Kerbal EVA
- 2x Kiln
- 6x Magnetic Separation
- 2x Miller
- 3x Mixer 
- 3x Name Converter 
- 3x Metallurgical Roasting
- 2x Settling Tank
- 30x Transformer
- 1x Washing

## Other ChitakTech Definitions
- 3x Deposit types
- 4x Parallax trees
- 5x Material types
- 5x SpaceDust bands
