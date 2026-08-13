using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// NOTES:
// Deposit generation does not save biomes as they aren't needed anywhere other than placing the deposit at the correct position
/* Example deposit config:
KHEMISTRY_RESOURCE_DEPOSIT
{
    type = surface               // Can be surface (surface and underground deposit), surfaceOnly (only surface deposit), or underground (only underground deposit). Will fail to load if this is not defined.
    render = true                // Only checked for surface and surfaceOnly, this will render a model at where the deposit is if true. Defaults to false.
    maxAmount = 10               // Maximum amount of deposits allowed. Defaults to 10.
    minAmount = 5                // Minimum amount of deposits allowed. Defaults to 5.
    body = Kerbin                // What celestial body the deposit is located on. Will fail to load if this is not defined.
    biome = Shores               // If this is set, the deposit will only spawn in this biome.
    resource = H2O               // What resource is in this deposit. Will fail to load if this is not defined.
    resource2 = H2               // What resource is stored in the underground version of this deposit. This is only checked for surface deposits and the deposit will not load if it's not defined.
    depthSurface = 10            // For surface and surfaceOnly deposits, this is how deep the top layer of the deposit is in meters. Not checked for underground deposits. Defaults to 10.
    depthUndergroundStart = 100  // For underground deposits, this is the depth that the deposit starts at in meters. Not checked for surface and surfaceOnly deposits. Defaults to 100
    depthUnderground = 50        // How deep the underground part of the deposit is in meters. This is not checked for surfaceOnly. Defaults to 50
    minRadius = 10               // Minimum radius of the deposit in meters. Defaults to 10
    maxRadius = 20               // Maximum radius of the deposit in meters. Defaults to 20
}
*/
// KHEMSITRY_RECIPE can be put as a node at top level, and then a KhemistryAdvancedRecipeISRU can import all with a type of recipeType, as well as add INPUT_RESOURCE and OUTPUT_RESOURCE.
/* Example recipe config:
KHEMISTRY_RECIPE
{
    recipeType = electrolysis

    ConverterName = Collect Earth Air             // Converter name, must be unique
    StartActionName = Start collecting Earth Air  // Button name for starting the converter
    StopActionName = Stop collecting Earth Air    // Button name for stopping the converter
    planetCondition = Earth                       // Converter can only operate on this planet. Do not include if can work anywhere
    biomeCondition = Cool Deserts                 // Converter can only operate in this biome. Do not include if no planetCondition or can work anywhere on that planet
    altitudeMaxCondition = 10000                  // Maximum altitude from sea level this ISRU can operate at. Requires altitudeMinCondition, do not include if no altitude restrictions
    altitudeMinCondition = 0                      // Minimum altitude from sea level this ISRU can operate at. Requires altitudeMaxCondition, do not include if no altitude restrictions
    situationCondition = Landed                   // Converter can only operate in this situation. Possible values are Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital. Do not include the value to ignore this condition.
    depositCondition = GSOre                      // Converter can only operate when over this deposit. Should be the resource value of a surface deposit.
    powerfailResource = LVEnergy                  // If this resource runs out, the part will powerfail. Must be an INPUT_RESOURCE. Do not include to disable powerfails.
    powerfailResult = EXPLODE,10                  // The result if a powerfail occurs. Can be "EXPLODE,n", "MAINT", or "STOP". Requires powerfailResource to be set and valid.
                                                  // EXPLODE will explode the part with power n, MAINT will require an Engineer kerbal to come fix it, and STOP will just shut down the part.
    manualOperation = true                        // false by default; enables manual cycle mode
    manualRequiresStartup = false                 // true by default; if false, no Start/Stop, just Execute Cycle
    startStopShowRules = EVA+PAW                  // "PAW" default; controls Start/Stop button visibility
    manualShowRules = EVA                         // "PAW" default; controls Execute Cycle button visibility
    maxInteractionDistance = 5.0                  // 10.0 default; applies to all EVA-visible buttons
    recipeGroup = myGroup                         // null by default; enforces one-active-at-a-time per group. If null, the converter does not have a group.

    INPUT_RESOURCE
    {
        ResourceName = LVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    OUTPUT_RESOURCE
    {
        ResourceName = EarthAir
        Ratio = 1
        DumpExcess = false
    }

    PARAMETERS                                    // Parameters used for conditions to see if a recipe should be added or not
    {
        heat = 100
    }
}
*/
/* Example KhemistryAdvancedRecipeISRU
MODULE
{
    name = KhemistryAdvancedRecipeISRU
    recipeType = electrolysis           // Imports all KHEMISTRY_RECIPE with this recipeType
    multiplier = 10                     // Multiplies all inputs and outputs by this value
    INPUT_RESOURCE                      // Additional inputs or outputs added to each recipe imported
    {
        ResourceName = IVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    RECIPES                             // List of recipes to import with type recipeType (optional)
    {
        name = Electrolyze Sea Water    // Use name values to specify which recipes are imported, checks based on ConverterName
        name = Electrolyze Ocean Water
    }
    RECIPE_MULTIPLIERS                  // Multiplies each imported recipe by an amount (optional, if included will require RECIPES)
    {                                   // Each amount value corresponds with each name value in RECIPES and multiplies that recipe.
        amount = 10                     // Even if you want to import all recipes of that type, you will have to put them all into RECIPES first.
        amount = 5
    }
    CONDITIONS
    {
        condition = MORE_THAN_OR_EQUALS_I,heat,1000  // Conditions to check a recipe's PARAMETERS and only add it if all conditions succeed
    }
}
*/
/* Sample config for advanced ISRU (normal/EVA):
MODULE
{
    name = KhemistryAdvancedISRU
    ConverterName = Collect Earth Air             // Converter name, must be unique
    StartActionName = Start collecting Earth Air  // Button name for starting the converter
    StopActionName = Stop collecting Earth Air    // Button name for stopping the converter
    planetCondition = Earth                       // Converter can only operate on this planet. Do not include if can work anywhere
    biomeCondition = Cool Deserts                 // Converter can only operate in this biome. Do not include if no planetCondition or can work anywhere on that planet
    altitudeMaxCondition = 10000                  // Maximum altitude from sea level this ISRU can operate at. Requires altitudeMinCondition, do not include if no altitude restrictions
    altitudeMinCondition = 0                      // Minimum altitude from sea level this ISRU can operate at. Requires altitudeMaxCondition, do not include if no altitude restrictions
    situationCondition = Landed                   // Converter can only operate in this situation. Possible values are Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital. Do not include the value to ignore this condition.
    depositCondition = GSOre                      // Converter can only operate when over this deposit. Should be the resource value of a surface deposit.
    powerfailResource = LVEnergy                  // If this resource runs out, the part will powerfail. Must be an INPUT_RESOURCE. Do not include to disable powerfails.
    powerfailResult = EXPLODE,10                  // The result if a powerfail occurs. Can be "EXPLODE,n", "MAINT", or "STOP". Requires powerfailResource to be set and valid.
                                                  // EXPLODE will explode the part with power n, MAINT will require an Engineer kerbal to come fix it, and STOP will just shut down the part.
    manualOperation = true                        // false by default; enables manual cycle mode
    manualRequiresStartup = false                 // true by default; if false, no Start/Stop, just Execute Cycle
    startStopShowRules = EVA+PAW                  // "PAW" default; controls Start/Stop button visibility
    manualShowRules = EVA                         // "PAW" default; controls Execute Cycle button visibility
    maxInteractionDistance = 5.0                  // 10.0 default; applies to all EVA-visible buttons
    recipeGroup = myGroup                         // null by default; enforces one-active-at-a-time per group. If null, the converter does not have a group.

    INPUT_RESOURCE
    {
        ResourceName = LVEnergy
        Ratio = 2
        FlowMode = STAGE_PRIORITY_FLOW
    }
    OUTPUT_RESOURCE
    {
        ResourceName = EarthAir
        Ratio = 1
        DumpExcess = false
    }

    chargingRequired = true    // Does the converter need to be charged to be used
	chargeRate = 50.0          // Percent per second to fill charge (50 = 2 seconds to full). Not required if charging is disabled
	chargeDecayRate = 5.0      // Percent per second to lose charge when storage can no longer charge. Not required if charging is disabled

	CHARGE_CON_NAMES           // Resources used for charge consumption. Not required if charging is disabled
	{
		name = ElectricCharge
	}
	CHARGE_CON_AMOUNTS         // Amount of each resource used for charge consumption (per second). Not required if charging is disabled
	{
		amount = 5.0
	}
}
*/
/* Sample config for KhemistryBatchISRU recipes
KHEMISTRYBATCHISRU_RECIPE
{
    name = Cooling Recipe Name  // Required

    recipeType = cooling  // Defaults to NONE
    recipeSubtype = big  // Defaults to NONE
    recipeSubsubtype = highHeat  // Defaults to NONE
    // Multiple recipeType, recipeSubtype, and recipeSubsubtype can be included

    // Everything else is the same as in RECIPE nodes inside KhemistryBatchISRU

    // Charging
    // The length of CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS must match
	CHARGE_CON_NAMES  // If not included, the part will not have charging.
	{
		name = LVEnergy
	}
	CHARGE_CON_AMOUNTS  // If not included, the part will not have charging.
	{
		amount = 1
	}
	chargeRate = 50  // Percent per second. If not included, the part will not have charging.
	chargeDecay = 70  // Percent per second. If not included, the part will not have charging.

    // Per-planet and per-biome configuration.
    // name can be a planet and a biome, but ALL sets the parameters for all planets and biomes.
    // If a planet or biome is specified, it overrides ALL completely.
    // At least one ALL PLANET_CONFIG is required
	PLANET_CONFIG
	{
		name = Kerbin  // Defaults to ALL
        // At least one ALL BIOME_CONFIG is required for each PLANET_CONFIG
		BIOME_CONFIG
		{
			name = Grasslands  // Defaults to ALL

            // All of the following are optional
			maxAltitude = 9  // Meters
			minAltitude = 0  // Meters
			maxOperatingAltitude = 9  // Meters
			minOperatingAltitude = 0  // Meters
			minG = 0
			maxG = 3
			minOperatingG = 0
			maxOperatingG = 3
			situationOperating = Landed
			situationDestructive = SpaceLow
			minTemperature = 1000C  // Celsius
			maxTemperature = 9999C  // Celsius
			minOperatingTemperature = 1000C  // Celsius
			maxOperatingTemperature = 9999C  // Celsius
            // Kerbin's atmosphere at sea level is 101.325 kPa
			maxPressure = 7  // kPa
			minPressure = 1  // kPa
			maxOperatingPressure = 7  // kPa
			minOperatingPressure = 1  // kPa

            // All of the following are optional and default to 1.0
			passiveMul = 1.0
			inMul = 1.0
			outMul = 1.0
			speedMul = 1.0
			chargeRateMul = 1.0
			chargeDecayMul = 1.0
			chargeConMul = 1.0
			passivePeriodMul = 1.0
			workersEngineersMul = 1.0
			workersPilotsMul = 1.0
			workersScientistsMul = 1.0
            maxInteractionDistanceMul = 1.0
            maxDisplayDistanceMul = 1.0
		}
	}
    // No INPUT_RESOURCE nodes means the converter makes things out of nothing
    INPUT_RESOURCE
    {
        name = KerbinBadlandsSoil  // Required
        amount = 1  // Resource units. Required
        flowmode = STAGE_PRIORITY_FLOW  // Defaults to STAGE_PRIORITY_FLOW
    }
	PINPUT_RESOURCE
	{
		name = KerbinAir  // Resource to consume
		amount = 0.043  // Amount in units
		period = 1  // Consume every period seconds. Defaults to 1
		powerfail = VOID  // Can be PAUSE, STOP, VOID, MAINT, or EXPLODE,n,t (n is the range in meters, t is the temperature at the center)
        ignorePowerfail = false  // This resource will not powerfail at all
	}
    // There must be at least one OUTPUT_RESOURCE or OUTPUT_RESOURCE_MATERIAL node
    OUTPUT_RESOURCE
    {
        name = HeavyOil  // Required
        amount = 0.1  // Resource units. Required
        dumpExcess = false  // Defaults to false
    }
	OUTPUT_RESOURCE_MATERIAL
	{
		name = Raw Bloom Iron
		shape = Bloom
		size = 0.25x0.20x0.12
		PARAMS
		{
			source = SLaterite
		}
		outVolume = 0.006
	}

    // Other variables
	recipeTime = 100  // Seconds. Required
    controlRules = EVA+PAW  // EVA, PAW, or EVA+PAW. Defaults to PAW
    workersEngineers = 2  // Defaults to 0
    workersPilots = 2  // Defaults to 0
    workersScientists = 1  // Defaults to 0
    workersType = EVA+CREW  // EVA, CREW, or EVA+CREW. Defaults to EVA
}
*/
/* Sample config for KhemistryBatchISRU
MODULE
{
	name = KhemistryBatchISRU

    // Load recipes with these parameters
	recipeType = cooling  // Load with this recipeType. Optional
    recipeSubtype = big  // Load with thus recipeSubtype. Optional
    recipeSubsubtype = highHeat  // Load with thus recipeSubsubtype. Optional
    RECIPE_NAMES  // Load recipes wih these names. Ignores all recipe conditions. Optional
    {
        name = Cool Neutronium
    }

    // Recipe multipliers
    recipeMultiplier = 10   // Multiplies all inputs and outputs by this value. Defaults to 1
    RECIPE_MULTIPLIERS  // For each recipe in RECIPE_NAMES, multiply that recipe by a number. Optional
    {
        name = 0.01
    }

    // Recipes added locally to this part, in addition to any recipes loaded by recipeTypes.
	RECIPE
	{
        // Charging
        // The length of CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS must match
		CHARGE_CON_NAMES  // If not included, the part will not have charging.
		{
			name = LVEnergy
		}
		CHARGE_CON_AMOUNTS  // If not included, the part will not have charging.
		{
			amount = 1
		}
		chargeRate = 50  // Percent per second. If not included, the part will not have charging.
		chargeDecay = 70  // Percent per second. If not included, the part will not have charging.

        // Per-planet and per-biome configuration.
        // name can be a planet and a biome, but ALL sets the parameters for all planets and biomes.
        // If a planet or biome is specified, it overrides ALL completely.
        // At least one ALL PLANET_CONFIG is required
		PLANET_CONFIG
		{
			name = ALL  // Defaults to ALL
            // At least one ALL BIOME_CONFIG is required for each PLANET_CONFIG
			BIOME_CONFIG
			{
				name = ALL  // Defaults to ALL

                // All of the following are optional
				maxAltitude = 9  // Meters
				minAltitude = 0  // Meters
				maxOperatingAltitude = 9  // Meters
				minOperatingAltitude = 0  // Meters
				minG = 0
				maxG = 3
				minOperatingG = 0
				maxOperatingG = 3
				situationOperating = Landed
				situationDestructive = SpaceLow
				minTemperature = 1000C  // Celsius
				maxTemperature = 9999C  // Celsius
				minOperatingTemperature = 1000C  // Celsius
				maxOperatingTemperature = 9999C  // Celsius
                // Kerbin's atmosphere at sea level is 101.325 kPa
				maxPressure = 7  // kPa
				minPressure = 1  // kPa
				maxOperatingPressure = 7  // kPa
				minOperatingPressure = 1  // kPa

                // All of the following are optional and default to 1.0
				passiveMul = 1.0
				inMul = 1.0
				outMul = 1.0
				speedMul = 1.0
				chargeRateMul = 1.0
				chargeDecayMul = 1.0
				chargeConMul = 1.0
				passivePeriodMul = 1.0
				workersEngineersMul = 1.0
				workersPilotsMul = 1.0
				workersScientistsMul = 1.0
                maxInteractionDistanceMul = 1.0
                maxDisplayDistanceMul = 1.0
			}
		}
        // No INPUT_RESOURCE nodes means the converter makes things out of nothing
        INPUT_RESOURCE
        {
            name = KerbinBadlandsSoil  // Required
            amount = 1  // Resource units. Required
            flowmode = STAGE_PRIORITY_FLOW  // Defaults to STAGE_PRIORITY_FLOW
        }
	    PINPUT_RESOURCE
	    {
		    name = KerbinAir  // Resource to consume
		    amount = 0.043  // Amount in units
		    period = 1  // Consume every period seconds. Defaults to 1
		    powerfail = VOID  // Can be PAUSE, STOP, VOID, MAINT, or EXPLODE,n,t (n is the range in meters, t is the temperature at the center)
            ignorePowerfail = false  // This resource will not powerfail at all
	    }
        // There must be at least one OUTPUT_RESOURCE or OUTPUT_RESOURCE_MATERIAL node
        OUTPUT_RESOURCE
        {
            name = HeavyOil  // Required
            amount = 0.1  // Resource units. Required
            dumpExcess = false  // Defaults to false
        }
		OUTPUT_RESOURCE_MATERIAL
		{
			name = Raw Bloom Iron
			shape = Bloom
			size = 0.25x0.20x0.12
			PARAMS
			{
				source = SLaterite
			}
			outVolume = 0.006
		}

        // Other variables
		recipeTime = 100  // Seconds. Required
        controlRules = EVA+PAW  // EVA, PAW, or EVA+PAW. Defaults to PAW
        workersEngineers = 2  // Defaults to 0
        workersPilots = 2  // Defaults to 0
        workersScientists = 1  // Defaults to 0
        workersType = EVA+CREW  // EVA, CREW, or EVA+CREW. Defaults to EVA
	}
	maxInteractionDistance = 2  // Meters. Defaults to 7. Controls how far away the ISRU can be interacted with.
	maxDisplayDistance = 2  // Meters. Defaults to 10. Controls how far away the ISRU will show its display fields.
    workersCrewSamePart = false  // If workersType is CREW or EVA+CREW, this will only check those that are on the same part as the converter. Defaults to false
}
*/

namespace Khemistry
{
    ////////////////////////////// Material System //////////////////////////////
    /// <summary>
    /// A material usually loaded from configs. It defines its name, allowed shapes, and allowed parameters.
    /// An instance of this material used as a resource is <see cref="KhemistryMaterialInstance"/>.
    /// </summary>
    public class KhemistryMaterial
    {
        public string name = "LOADFAIL";
        public List<string> shapes = new List<string>();
        public List<string> parameters = new List<string>();
        public KhemistryMaterial(ConfigNode configNode)
        {
            // Check if the config node is valid
            if (configNode.name != "KHEMISTRY_MATERIAL")
            {
                KShared.LogError("KhemistryMaterial loading failed because the node isn't named KHEMISTRY_MATERIAL!", "KhemistryMaterial/constructor");
                return;
            }
            if (!configNode.HasNode("SHAPES") || !configNode.HasValue("name"))
            {
                KShared.LogError("KhemistryMaterial loading failed because one of the nodes isn't present!", "KhemistryMaterial/constructor");
                return;
            }

            // Set material name from the config
            name = configNode.GetValue("name");

            // Set shapes from the config
            foreach (string shape in configNode.GetNode("SHAPES").GetValues("name"))
                shapes.Add(shape);

            // Set parameters (if there are any) from the config
            if (configNode.HasNode("PARAMS"))
                foreach (string param in configNode.GetNode("PARAMS").GetValues("name"))
                    parameters.Add(param);
        }
    }
    /// <summary>
    /// Instance of a <see cref="KhemistryMaterial"/> with its own shape, size, volume, and parameters.
    /// </summary>
    public class KhemistryMaterialInstance
    {
        public string shape = "null";
        public string size = "null";
        public float volume = 0f;
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        public KhemistryMaterial material;

        public KhemistryMaterialInstance(KhemistryMaterial material, string shape, string size, float volume, Dictionary<string, string> parameters)
        {
            // Assign values
            this.material = material;
            this.shape = shape;
            this.size = size;
            this.volume = volume;
            this.parameters = parameters;

            // Check parameter validity
            foreach (string key in parameters.Keys)
                if (!material.parameters.Contains(key))
                    KShared.LogError("Material instance of material " + material.name + " has an invalid parameter " + key + " with value " + parameters[key] + "!", "KhemistryMaterialInstance/constructor");

            // Check shape validity
            if (!material.shapes.Contains(shape))
                KShared.LogError("Material instance of material " + material.name + " has an invalid shape " + shape + "!", "KhemistryMaterialInstance/constructor");
        }

        /// <summary>
        /// Checks if it is possible to merge the volumes of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// For this to be true, they must be exactly the same except for the volume.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to test merging for.</param>
        /// <returns>If possible to merge the two <see cref="KhemistryMaterialInstance"/>.</returns>
        public bool CanMerge(KhemistryMaterialInstance other)
        {
            if (shape == other.shape && size == other.size && material.name == other.material.name)
            {
                foreach (string key in parameters.Keys)
                {
                    if (!other.parameters.ContainsKey(key))
                        return false;
                    if (other.parameters[key] != parameters[key])
                        return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Merge the volumes of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to merge.</param>
        /// <returns>If merging succeeded or not.</returns>
        public bool Merge(KhemistryMaterialInstance other)
        {
            if (CanMerge(other))
            {
                volume += other.volume;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A PartModule that stores <see cref="KhemistryMaterialInstance"/> and merges them as needed.
    /// Uses a completely different resource system than stock KSP.
    /// </summary>
    public class KhemistryMaterialStorage : PartModule
    {
        [KSPField(isPersistant = false)]
        public float volume = 1f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistrymatstorage",
                  groupDisplayName = "Khemistry Material Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistrymatstorage")]
        public string volumeDisplay = "0 / 0";

        public List<string> supportedNames = new List<string>();
        public List<string> supportedShapes = new List<string>();
        public Dictionary<string, string> paramRequirements = new Dictionary<string, string>();

        public List<KhemistryMaterialInstance> contents = new List<KhemistryMaterialInstance>();
        private bool _fatalConfigError = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                contentsDisplay = "ERROR: see log";
                return;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        public void FixedUpdate()
        {
            UpdateUI();
        }

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.LogError("partInfo.partConfig is null!",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = null;
            foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (n.GetValue("name") == "KhemistryMaterialStorage") { moduleNode = n; break; }
            }

            if (moduleNode == null)
            {
                KShared.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            supportedNames.Clear();
            if (!moduleNode.HasNode("SUPPORTED_NAMES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage but no SUPPORTED_NAMES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_NAMES").GetValues("name"))
                supportedNames.Add(n.Trim());
            if (supportedNames.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty SUPPORTED_NAMES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            supportedShapes.Clear();
            if (!moduleNode.HasNode("SUPPORTED_SHAPES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage but no SUPPORTED_SHAPES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_SHAPES").GetValues("name"))
                supportedShapes.Add(n.Trim());
            if (supportedShapes.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty SUPPORTED_SHAPES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            paramRequirements.Clear();
            if (!moduleNode.HasNode("PARAM_REQUIREMENTS"))
            {
                return;
            }
            paramRequirements = KShared.NodeToDictionary(moduleNode.GetNode("PARAM_REQUIREMENTS"));
            if (paramRequirements.Keys.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty PARAM_REQUIREMENTS node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
        }

        /// <summary>
        /// Add a material instance to storage.
        /// If the material is already present, it will be merged with the existing one.
        /// </summary>
        /// <param name="mat">The material instance to add to storage.</param>
        /// <returns>Whether the material was added. This can only be false if there wasn't enough space.</returns>
        public bool AddMaterial(KhemistryMaterialInstance mat)
        {
            if (DoesExceedCapacity(ComputeCurrentVolume(mat.volume)))
                return false;

            foreach (KhemistryMaterialInstance m in contents)
                if (m.Merge(mat))
                    return true;

            contents.Add(mat);
            return true;
        }

        /// <summary>
        /// Compute the current volume taken up by the contents of this storage.
        /// Can accept a value to add to the total volume, usually used to check if adding a new material would exceed capacity.
        /// </summary>
        /// <param name="usedVolume">An additional amount to add to the volume being used.</param>
        /// <returns>How much volume is used.</returns>
        private float ComputeCurrentVolume(float usedVolume = 0f)
        {
            foreach (KhemistryMaterialInstance m in contents)
                usedVolume += m.volume;
            return usedVolume;
        }

        private bool DoesExceedCapacity(float volumeToCompare) => volumeToCompare >= volume;

        private void UpdateUI()
        {
            List<string> contentsDisplayNames = new List<string>();
            foreach (KhemistryMaterialInstance m in contents)
                if (m.volume > 0)
                    contentsDisplayNames.Add(m.material.name + " as " + m.shape + " (" + KShared.DictToString(m.parameters) + ")");
            contentsDisplay = string.Join("\n", contentsDisplayNames);
            volumeDisplay = $"{ComputeCurrentVolume():F10} / {volume:F10}";
        }
    }

    ////////////////////////////// Deposit System //////////////////////////////

    /// <summary>
    /// Shared data for both deposit types, <see cref="KhemistryUDeposit"/> and <see cref="KhemistryGDeposit"/>.
    /// </summary>
    public class KhemistryDeposit
    {
        // Shared variables
        public Vector2 Position { get; set; }  // In latitude, longitude format
        public float Depth { get; set; }  // In meters
        public string Resource { get; set; }  // Internal name of a resource
        public string Planet { get; set; }  // Planet the resource is on
        public float Radius { get; set; }  // Radius of the deposit in meters

        // Deposit distance logic
        public float DistanceFromDeposit(float lat, float lon)
        {
            try
            {
                return (float)KShared.LatLonDistanceMeters(Position[0], Position[1], lat, lon, Planet);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured, returning 0 meters. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryDeposit/DistanceFromDeposit");
                return 0f;
            }
        }
        public bool IsInsideDeposit(float lat, float lon) => DistanceFromDeposit(lat, lon) <= Radius;
    }

    /// <summary>
    /// A deposit where resources can be extracted from.
    /// This is the underground version of a deposit, see <see cref="KhemistryGDeposit"/> for its aboveground counterpart.
    /// </summary>
    public class KhemistryUDeposit : KhemistryDeposit
    {
        public float DepthStart { get; set; }

        public bool IsDepthInsideDeposit(float depth2) => depth2 > DepthStart && depth2 < DepthStart + Depth;

        public KhemistryUDeposit(KShared kinst, string planet, string requiredBiome, float depthStart, float depth, string resource, float minRadius, float maxRadius, float latOverride = -12345, float lonOverride = -12345)
        {
            try
            {
                Planet = planet;
                DepthStart = depthStart;
                Depth = depth;
                Resource = resource;

                if (minRadius == maxRadius)
                {
                    Radius = minRadius;
                }
                else
                {
                    // Keep rolling a radius until it clears minRadius, up to a sane attempt cap —
                    // a misconfigured minRadius >= maxRadius (or a negative value reaching this
                    // constructor via a mislabeled call) would otherwise spin forever.
                    const int maxRadiusAttempts = 10000;
                    float tmp = -1.0f;
                    int radiusAttempts = 0;
                    while (!(minRadius > tmp))
                    {
                        radiusAttempts++;
                        if (radiusAttempts >= maxRadiusAttempts)
                        {
                            KShared.LogError(
                                "Could not roll a radius above minRadius " + minRadius + " with maxRadius " + maxRadius +
                                " after " + maxRadiusAttempts + " attempts (minRadius >= maxRadius?). Using maxRadius instead.",
                                "KhemistryUDeposit/constructor");
                            tmp = maxRadius;
                            break;
                        }
                        tmp = (float)(kinst.rand.NextDouble() * maxRadius);
                    }
                    Radius = tmp;
                }

                // Generate position
                if ((int)latOverride == -12345 || (int)lonOverride == -12345)  // If either of them are not set, calculate as normal
                {
                    Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                    if (requiredBiome != null)  // If it is null, any biome is supported
                    {
                        // Just keep randomizing the deposit until it hits the right biome, up to a
                        // sane attempt cap — an unmatched/misspelled biome name would otherwise spin
                        // forever since GetBiomeNameFromLatLon always returns *some* biome name.
                        const int maxBiomeAttempts = 10000;
                        int attempts = 0;
                        while (KShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                        {
                            attempts++;
                            if (attempts >= maxBiomeAttempts)
                            {
                                KShared.LogError(
                                    "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                    "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                    "Available biomes for the planet: " + KShared.ListToString(KShared.GetBiomeNames(planet)),
                                    "KhemistryUDeposit/constructor");
                                break;
                            }
                            Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                        }
                    }
                }
                else  // If both are set, ignore requiredBiome and override the position
                {
                    Position = new Vector2(latOverride, lonOverride);
                }
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryUDeposit/constructor");
            }
        }
    }

    /// <summary>
    /// A deposit where resources can be extracted from. Can have a pair underground deposit.
    /// This is the aboveground version of a deposit, see <see cref="KhemistryUDeposit"/> for its underground counterpart.
    /// </summary>
    public class KhemistryGDeposit : KhemistryDeposit
    {
        public KhemistryUDeposit PairGDeposit { get; set; }

        /// <summary>
        /// Helper function to see if a depth is inside the deposit.
        /// Uses -1 in the comparison to make sure 0 works as well.
        /// </summary>
        /// <param name="depth2">Depth of the point in meters.</param>
        /// <returns>Whether the depth is inside the deposit.</returns>
        public bool IsDepthInsideDeposit(float depth2) => depth2 > -1 && depth2 < Depth;

        public KhemistryGDeposit(KShared kinst, string planet, string requiredBiome, float depth, string resource, float minRadius, float maxRadius, string resource2, float underDepth)
        {
            try
            {
                // Set values to make sure everything works
                Planet = planet;
                Depth = depth;
                Resource = resource;

                // if it works, it works — keep rolling a radius until it clears minRadius, up to a
                // sane attempt cap; a misconfigured minRadius >= maxRadius would otherwise spin forever.
                const int maxRadiusAttempts = 10000;
                float tmp = -1.0f;
                int radiusAttempts = 0;
                while (!(minRadius > tmp))
                {
                    radiusAttempts++;
                    if (radiusAttempts >= maxRadiusAttempts)
                    {
                        KShared.LogError(
                            "Could not roll a radius above minRadius " + minRadius + " with maxRadius " + maxRadius +
                            " after " + maxRadiusAttempts + " attempts (minRadius >= maxRadius?). Using maxRadius instead.",
                            "KhemistryGDeposit/constructor");
                        tmp = maxRadius;
                        break;
                    }
                    tmp = (float)(kinst.rand.NextDouble() * maxRadius);
                }
                Radius = tmp;

                // Generate position
                Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                if (requiredBiome != null)
                {
                    // Just keep randomizing the deposit until it hits the right biome, up to a sane
                    // attempt cap — an unmatched/misspelled biome name would otherwise spin forever
                    // since GetBiomeNameFromLatLon always returns *some* biome name.
                    const int maxBiomeAttempts = 10000;
                    int attempts = 0;
                    while (KShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                    {
                        attempts++;
                        if (attempts >= maxBiomeAttempts)
                        {
                            KShared.LogError(
                                "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                "Available biomes for the planet: " + KShared.ListToString(KShared.GetBiomeNames(planet)),
                                "KhemistryGDeposit/constructor");
                            break;
                        }
                        Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                    }
                }

                // Create the underground pair of the surface deposit, giving it the counterpart resource and overriding the position to the surface deposit's position
                // The biome is not passed here because the override will ignore it anyway
                // If resource2 is null, the deposit is considered "surfaceOnly" and the underground deposit won't be created
                if (resource2 != null)
                    PairGDeposit = new KhemistryUDeposit(kinst, planet, null, depth, underDepth, resource2, minRadius, maxRadius, latOverride: Position[0], lonOverride: Position[1]);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryGDeposit/constructor");
            }
        }
    }

    ////////////////////////////// ISRU Recipe System //////////////////////////////

    // Recipe condition, used in AdvancedISRURecipe to select recipes based on their PARAMETERS node
    /* There are many recipe conditions, so I listed them below:
     * Format: "[configSyntax] = when true".
     * Everything that isn't defined is replaced with NONE.
     * Every condition is actually [1,2,3], but not all are used so they just get auto-replaced with NONE.
     * 
     * [HAS_PARAM] = Recipe has the PARAMETERS node. Shouldn't be used everywhere as other parameter-related conditions won't work if there is no PARAMETERS node.
     * [HAS,value] = Recipe has the "value" value in PARAMETERS.
     * 
     * [EQUALS,value,something] = Recipe has "value" set to "something".
     * 
     * [IS_STR,value] = Recipe's "value" is not a boolean, integer, or a float.
     * [IS_BOOL,value] = Recipe's "value" is a boolean. Any conditions requiring a boolean will check this first.
     * [IS_INT,value] = Recipe's "value" is an integer. Any conditions requiring an integer will check this first.
     * [IS_FLOAT,value] = Recipe's "value" is a float. Any conditions requiring a float will check this first.
     * 
     * [IS_TRUE,value] = Recipe's "value" is a boolean and is true.
     * [IS_FALSE,value] = Recipe's "value" is a boolean and is false.
     * 
     * [MORE_THAN_I,value,1] = Recipe's "value" is an integer and it is more than "1".
     * [LESS_THAN_I,value,-1] = Recipe's "value" is an integer and it is less than "-1".
     * [MORE_THAN_OR_EQUALS_I,value,-1] = Recipe's "value" is an integer and it is more than or equals to "-1".
     * [LESS_THAN_OR_EQUALS_I,value,-1] = Recipe's "value" is an integer and it is less than or equals to "-1".
     * 
     * [MORE_THAN_F,value,1] = Recipe's "value" is a float and it is more than "1".
     * [LESS_THAN_F,value,-1] = Recipe's "value" is a float and it is less than "-1".
     * [MORE_THAN_OR_EQUALS_F,value,-1] = Recipe's "value" is a float and it is more than or equals to "-1".
     * [LESS_THAN_OR_EQUALS_F,value,-1] = Recipe's "value" is a float and it is less than or equals to "-1".
    */
    public enum ConditionType
    {
        None,
        HasParam,
        Has,
        Equals,
        IsBool,
        IsInt,
        IsFloat,
        IsString,
        IsTrue,
        IsFalse,
        MoreThanInt,
        LessThanInt,
        MoreThanOrEqualsInt,
        LessThanOrEqualsInt,
        MoreThanFloat,
        LessThanFloat,
        MoreThanOrEqualsFloat,
        LessThanOrEqualsFloat
    }

    /// <summary>
    /// Stores a recipe condition for <see cref="KhemistryAdvancedRecipeISRU"/>.
    /// </summary>
    public class AdvancedISRURecipeCondition
    {
        // Condition type
        public ConditionType Condition = ConditionType.None;
        // Parameter name
        public string Value = "NONE";
        // Comparison value
        public string Value2 = "NONE";

        private static readonly Dictionary<string, ConditionType> ConditionMap = new Dictionary<string, ConditionType>() {
            { "HAS_PARAM", ConditionType.HasParam },
            { "HAS", ConditionType.Has },
            { "EQUALS", ConditionType.Equals },
            { "IS_STR", ConditionType.IsString },
            { "IS_BOOL", ConditionType.IsBool },
            { "IS_INT", ConditionType.IsInt },
            { "IS_FLOAT", ConditionType.IsFloat },
            { "IS_TRUE", ConditionType.IsTrue },
            { "IS_FALSE", ConditionType.IsFalse },
            { "MORE_THAN_I", ConditionType.MoreThanInt },
            { "LESS_THAN_I", ConditionType.LessThanInt },
            { "MORE_THAN_OR_EQUALS_I", ConditionType.MoreThanOrEqualsInt },
            { "LESS_THAN_OR_EQUALS_I", ConditionType.LessThanOrEqualsInt },
            { "MORE_THAN_F", ConditionType.MoreThanFloat },
            { "LESS_THAN_F", ConditionType.LessThanFloat },
            { "MORE_THAN_OR_EQUALS_F", ConditionType.MoreThanOrEqualsFloat },
            { "LESS_THAN_OR_EQUALS_F", ConditionType.LessThanOrEqualsFloat }
        };
        public static ConditionType ParseConditionType(string condition)
        {
            try
            {
                return ConditionMap.TryGetValue(condition, out ConditionType result)
                    ? result
                    : ConditionType.None;
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured, returning ConditionType.None. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "AdvancedISRURecipeCondition/ParseConditionType");
                return ConditionType.None;
            }
        }

        public AdvancedISRURecipeCondition(string conditionStr)
        {
            try
            {
                if (string.IsNullOrEmpty(conditionStr))
                {
                    Condition = ConditionType.None;
                    Value = "NONE";
                    Value2 = "NONE";
                    return;
                }

                string[] parts = conditionStr.Split(',');

                Condition = parts.Length > 0
                    ? ParseConditionType(parts[0].Trim())
                    : ConditionType.None;

                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    Value = parts[1].Trim();
                else
                    Value = "NONE";

                if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    Value2 = parts[2].Trim();
                else
                    Value2 = "NONE";
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "AdvancedISRURecipeCondition/constructor");
            }
        }

        /// <summary>
        /// Checks whether this condition is satisfied.
        /// </summary>
        public bool CheckCondition(ConfigNode node)
        {
            try
            {
                if (Condition == ConditionType.None)
                    return true;

                ConfigNode parameters = node.GetNode("PARAMETERS");

                if (parameters == null)
                    return false;

                if (Condition == ConditionType.HasParam)
                    return true;

                if (!parameters.HasValue(Value))
                    return false;

                string configValue = parameters.GetValue(Value);

                switch (Condition)
                {
                    case ConditionType.Has:
                        return true;

                    case ConditionType.Equals:
                        return configValue == Value2;

                    case ConditionType.IsBool:
                        return bool.TryParse(configValue, out _);

                    case ConditionType.IsInt:
                        return int.TryParse(configValue, out _);

                    case ConditionType.IsFloat:
                        return float.TryParse(
                            configValue,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out _);

                    case ConditionType.IsString:
                        return !bool.TryParse(configValue, out _) &&
                               !int.TryParse(configValue, out _) &&
                               !float.TryParse(
                                   configValue,
                                   NumberStyles.Float,
                                   CultureInfo.InvariantCulture,
                                   out _);

                    case ConditionType.IsTrue:
                        {
                            return bool.TryParse(configValue, out bool result) && result;
                        }

                    case ConditionType.IsFalse:
                        {
                            return bool.TryParse(configValue, out bool result) && !result;
                        }

                    case ConditionType.MoreThanInt:
                    case ConditionType.LessThanInt:
                    case ConditionType.MoreThanOrEqualsInt:
                    case ConditionType.LessThanOrEqualsInt:
                        {
                            if (!int.TryParse(configValue, out int actual))
                                return false;

                            if (!int.TryParse(Value2, out int expected))
                                return false;

                            switch (Condition)
                            {
                                case ConditionType.MoreThanInt:
                                    return actual > expected;

                                case ConditionType.LessThanInt:
                                    return actual < expected;

                                case ConditionType.MoreThanOrEqualsInt:
                                    return actual >= expected;

                                case ConditionType.LessThanOrEqualsInt:
                                    return actual <= expected;
                            }

                            return false;
                        }

                    case ConditionType.MoreThanFloat:
                    case ConditionType.LessThanFloat:
                    case ConditionType.MoreThanOrEqualsFloat:
                    case ConditionType.LessThanOrEqualsFloat:
                        {
                            if (!float.TryParse(
                                configValue,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out float actual))
                                return false;

                            if (!float.TryParse(
                                Value2,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out float expected))
                                return false;

                            switch (Condition)
                            {
                                case ConditionType.MoreThanFloat:
                                    return actual > expected;

                                case ConditionType.LessThanFloat:
                                    return actual < expected;

                                case ConditionType.MoreThanOrEqualsFloat:
                                    return actual >= expected;

                                case ConditionType.LessThanOrEqualsFloat:
                                    return actual <= expected;
                            }

                            return false;
                        }

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured, returning condition fail. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "AdvancedISRURecipeCondition/CheckCondition");
                return false;
            }
        }
    }

    /// <summary>
    /// A recipe to use in <see cref="KhemistryAdvancedRecipeISRU"/>.
    /// It defines everything a <see cref="KhemistryAdvancedISRU"/> does but can be overriden by values in the ISRU loading it.
    /// </summary>
    public class KhemistryRecipe
    {
        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start Converter";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop Converter";

        public struct ResourceInput
        {
            public string resourceName;
            public double ratio;
            public ResourceFlowMode flowMode;
        }

        public struct ResourceOutput
        {
            public string resourceName;
            public double ratio;
            public bool dumpExcess;
        }

        public enum SituationCondition
        {
            Any, Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital
        }

        public enum PowerfailResult { Pause, Stop, Void, Maint, Explode }

        public List<ResourceInput> _inputs = new List<ResourceInput>();
        public List<ResourceOutput> _outputs = new List<ResourceOutput>();

        public string _planetCondition = null;
        public string _biomeCondition = null;
        public double _altMin = double.MinValue;
        public double _altMax = double.MaxValue;
        public SituationCondition _situationCondition = SituationCondition.Any;
        public string _depositCondition = null;

        public string _powerfailResource = null;
        public PowerfailResult _powerfailResult = PowerfailResult.Pause;
        public float _powerfailExplosionRadius = 0f;
        public float _powerfailExplosionTemperature = 0f;  // Celsius

        public bool _manualOperation = false;
        public bool _manualRequiresStartup = true;

        public bool _startStopShowPAW = true;
        public bool _startStopShowEVA = false;
        public bool _manualShowPAW = true;
        public bool _manualShowEVA = false;

        public float _maxInteractionDistance = 10f;

        public bool chargingRequired = false;
        public float chargeRate = 0f;
        public float chargeDecayRate = 0f;
        public readonly List<string> ChargeNames = new List<string>();
        public readonly List<float> ChargeAmounts = new List<float>();

        public ConfigNode mainNode = new ConfigNode();

        public KhemistryRecipe(ConfigNode node)
        {
            try
            {
                ConverterName = KShared.GetStrValueFromCFG(node, "ConverterName", "Converter");
                StartActionName = KShared.GetStrValueFromCFG(node, "StartActionName", null);
                StopActionName = KShared.GetStrValueFromCFG(node, "StopActionName", null);
                _planetCondition = KShared.GetStrValueFromCFG(node, "planetCondition", null);
                _biomeCondition = KShared.GetStrValueFromCFG(node, "biomeCondition", null);
                _altMin = KShared.GetFloatValueFromCFG(node, "altitudeMinCondition", (float)double.MinValue);
                _altMax = KShared.GetFloatValueFromCFG(node, "altitudeMaxCondition", (float)double.MaxValue);

                _situationCondition = SituationCondition.Any;
                string sitStr = KShared.GetStrValueFromCFG(node, "situationCondition", null);
                if (sitStr != null)
                {
                    if (sitStr.Equals("FlyindHigh", StringComparison.OrdinalIgnoreCase))
                        sitStr = "FlyingHigh";
                    if (Enum.TryParse(sitStr, true, out SituationCondition parsed))
                        _situationCondition = parsed;
                    else
                        KShared.LogError("Unknown situationCondition \"" + sitStr + "\" — condition ignored.", "KhemistryRecipe/constructor");
                }

                _depositCondition = KShared.GetStrValueFromCFG(node, "depositCondition", null);

                _manualOperation = false;
                _manualRequiresStartup = true;
                if (bool.TryParse(KShared.GetStrValueFromCFG(node, "manualOperation", "false"), out bool tmpB))
                    _manualOperation = tmpB;
                if (bool.TryParse(KShared.GetStrValueFromCFG(node, "manualRequiresStartup", "true"), out tmpB))
                    _manualRequiresStartup = tmpB;

                KShared.ParseShowRule(
                    KShared.GetStrValueFromCFG(node, "startStopShowRules", "PAW"),
                    out _startStopShowPAW, out _startStopShowEVA, "startStopShowRules");

                KShared.ParseShowRule(
                    KShared.GetStrValueFromCFG(node, "manualShowRules", "PAW"),
                    out _manualShowPAW, out _manualShowEVA, "manualShowRules");

                _maxInteractionDistance = 10f;
                if (float.TryParse(node.GetValue("maxInteractionDistance"), out float distTmp))
                    _maxInteractionDistance = distTmp;

                foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;

                    double.TryParse(inputNode.GetValue("Ratio"), out double ratio);

                    ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                    string flowStr = inputNode.GetValue("FlowMode");
                    if (!string.IsNullOrEmpty(flowStr))
                    {
                        if (Enum.TryParse(flowStr.Trim(), true, out ResourceFlowMode parsed))
                            flowMode = parsed;
                        else
                            KShared.LogError(
                                "Recipe \"" + ConverterName + "\": Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                                "KhemistryRecipe/constructor");
                    }

                    _inputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
                }

                foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;

                    double.TryParse(outputNode.GetValue("Ratio"), out double ratio);

                    bool.TryParse(outputNode.GetValue("DumpExcess"), out bool dumpExcess);

                    _outputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
                }

                if (_inputs.Count == 0 && _outputs.Count == 0)
                    KShared.LogError(
                        "Recipe \"" + ConverterName + "\" has no INPUT_RESOURCE or OUTPUT_RESOURCE nodes — it will do nothing.",
                        "KhemistryRecipe/constructor");

                _powerfailResource = null;
                _powerfailResult = PowerfailResult.Pause;
                _powerfailExplosionRadius = 0f;
                _powerfailExplosionTemperature = 0f;

                string pfRes = KShared.GetStrValueFromCFG(node, "powerfailResource", null);
                string pfResultRaw = KShared.GetStrValueFromCFG(node, "powerfailResult", null);

                if (pfRes != null)
                {
                    bool found = false;
                    foreach (ResourceInput inp in _inputs)
                        if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                    if (!found)
                    {
                        KShared.LogError("powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.", "KhemistryRecipe/constructor");
                    }
                    else
                    {
                        _powerfailResource = pfRes;
                        if (pfResultRaw != null)
                        {
                            string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                            if (pfResult == "PAUSE")
                            {
                                _powerfailResult = PowerfailResult.Pause;
                            }
                            else if (pfResult == "STOP")
                            {
                                _powerfailResult = PowerfailResult.Stop;
                            }
                            else if (pfResult == "VOID")
                            {
                                _powerfailResult = PowerfailResult.Void;
                            }
                            else if (pfResult == "MAINT")
                            {
                                _powerfailResult = PowerfailResult.Maint;
                            }
                            else if (pfResult.StartsWith("EXPLODE,"))
                            {
                                string[] parts = pfResult.Substring(8).Split(',');
                                if (parts.Length == 2
                                    && float.TryParse(parts[0], out float radius)
                                    && float.TryParse(parts[1], out float tempC))
                                {
                                    _powerfailResult = PowerfailResult.Explode;
                                    _powerfailExplosionRadius = radius;
                                    _powerfailExplosionTemperature = tempC;
                                }
                                else
                                {
                                    KShared.LogError("Could not parse EXPLODE radius/temperature \"" + pfResultRaw + "\" (expected EXPLODE,radiusMeters,tempCelsius) — defaulting to PAUSE.", "KhemistryRecipe/constructor");
                                    _powerfailResult = PowerfailResult.Pause;
                                }
                            }
                            else
                            {
                                KShared.LogError("Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to PAUSE.", "KhemistryRecipe/constructor");
                                _powerfailResult = PowerfailResult.Pause;
                            }
                        }
                    }
                }
                else if (pfResultRaw != null)
                {
                    KShared.LogError("powerfailResult set without powerfailResource — powerfailResult ignored.", "KhemistryRecipe/constructor");
                }

                if (bool.TryParse(KShared.GetStrValueFromCFG(node, "chargingRequired", "false"), out tmpB))
                    chargingRequired = tmpB;

                if (float.TryParse(node.GetValue("chargeRate"), out float chgTmp)) chargeRate = chgTmp;
                if (float.TryParse(node.GetValue("chargeDecayRate"), out chgTmp)) chargeDecayRate = chgTmp;

                if (node.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        ChargeNames.Add(n.Trim());
                if (node.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out float amtTmp)) ChargeAmounts.Add(amtTmp); }
                if (ChargeNames.Count != ChargeAmounts.Count)
                    KShared.LogError(
                        "Recipe \"" + ConverterName + "\": CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryRecipe/constructor");

                mainNode = new ConfigNode();
                node.CopyTo(mainNode);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryRecipe/constructor");
            }
        }
    }

    ////////////////////////////// Batch ISRU System //////////////////////////////

    /// <summary>
    /// A config used in <see cref="KhemistryBatchISRU"/> for each biome.
    /// Contains a lot of conditions when the recipe can work.
    /// </summary>
    public class BatchISRUBiomeConfig
    {
        public string biomeName;

        public double minOperatingAltitude = double.MinValue;
        public double maxOperatingAltitude = double.MaxValue;
        public double minAltitude = double.MinValue;
        public double maxAltitude = double.MaxValue;

        public double minOperatingG = double.MinValue;
        public double maxOperatingG = double.MaxValue;
        public double maxG = double.MaxValue;
        public double minG = double.MinValue;

        public List<KShared.SituationCondition> situationOperating = new List<KShared.SituationCondition>();
        public List<KShared.SituationCondition> situationDestructive = new List<KShared.SituationCondition>();

        public double minOperatingTemperature = double.MinValue;
        public double maxOperatingTemperature = double.MaxValue;
        public double minTemperature = double.MinValue;
        public double maxTemperature = double.MaxValue;

        public double minOperatingPressure = double.MinValue;
        public double maxOperatingPressure = double.MaxValue;
        public double minPressure = double.MinValue;
        public double maxPressure = double.MaxValue;

        public double passiveMultiplier = 1.0;  // unused!
        public double passivePeriodMultiplier = 1.0;  // unused!

        public double chargeRateMultiplier = 1.0;  // unused!
        public double chargeDecayMultiplier = 1.0;  // unused!
        public double chargeConsumptionMultiplier = 1.0;  // unused!

        public double inputMultiplier = 1.0;
        public double outputMultiplier = 1.0;

        public double speedMul = 1.0;

        public double workersPilotsMultiplier = 1.0;
        public double workersEngineersMultiplier = 1.0;
        public double workersScientistsMultiplier = 1.0;

        public double maxInteractionDistanceMultiplier = 1.0;
        public double maxDisplayDistanceMultiplier = 1.0;

        public List<string> depositConditions = new List<string>();

        ///// Functions /////
        /// <summary>
        /// Make a biome config from a biome config node in a BatchISRU recipe.
        /// </summary>
        /// <param name="node">The node BIOME_CONFIG in PLANET_CONFIG in a BatchISRU module.</param>
        /// <param name="ConverterName">The name of the converter the biome config belongs to.</param>
        public BatchISRUBiomeConfig(ConfigNode node, string ConverterName = "UNKNOWN")
        {
            if (node.HasValue("name"))
            {
                biomeName = node.GetValue("name");

                // Altitude
                minOperatingAltitude = KShared.GetDoubleValueFromCFG(node, "minOperatingAltitude", minOperatingAltitude);
                maxOperatingAltitude = KShared.GetDoubleValueFromCFG(node, "maxOperatingAltitude", maxOperatingAltitude);
                minAltitude = KShared.GetDoubleValueFromCFG(node, "minAltitude", minAltitude);
                maxAltitude = KShared.GetDoubleValueFromCFG(node, "maxAltitude", maxAltitude);

                // G-Force
                minOperatingG = KShared.GetDoubleValueFromCFG(node, "minOperatingG", minOperatingG);
                maxOperatingG = KShared.GetDoubleValueFromCFG(node, "maxOperatingG", maxOperatingG);
                maxG = KShared.GetDoubleValueFromCFG(node, "maxG", maxG);
                minG = KShared.GetDoubleValueFromCFG(node, "minG", minG);

                // Situation
                situationOperating.Clear();
                foreach (string situationOperatingStr in node.GetValues("situationOperating"))
                {
                    if (Enum.TryParse(situationOperatingStr, true, out KShared.SituationCondition parsed))
                        situationOperating.Add(parsed);
                    else
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": Biome config \"" + biomeName + "\": Unknown situationOperating situationCondition \"" + situationOperatingStr + "\" — condition ignored.",
                            "KhemistryBatchISRU/LoadSharedConfig");
                }
                situationDestructive.Clear();
                foreach (string situationDestructiveStr in node.GetValues("situationDestructive"))
                {
                    if (Enum.TryParse(situationDestructiveStr, true, out KShared.SituationCondition parsed))
                        situationDestructive.Add(parsed);
                    else
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": Biome config \"" + biomeName + "\": Unknown situationDestructive situationCondition \"" + situationDestructiveStr + "\" — condition ignored.",
                            "KhemistryBatchISRU/LoadSharedConfig");
                }

                // Conditions
                depositConditions.Clear();
                foreach (string depositConditionStr in node.GetValues("depositCondition"))
                    depositConditions.Add(depositConditionStr);

                // Temperature
                minOperatingTemperature = KShared.GetDoubleTemperatureValueFromCFG(node, "minOperatingTemperature", minOperatingTemperature);
                maxOperatingTemperature = KShared.GetDoubleTemperatureValueFromCFG(node, "maxOperatingTemperature", maxOperatingTemperature);
                minTemperature = KShared.GetDoubleTemperatureValueFromCFG(node, "minTemperature", minTemperature);
                maxTemperature = KShared.GetDoubleTemperatureValueFromCFG(node, "maxTemperature", maxTemperature);

                // Pressure
                minOperatingPressure = KShared.GetDoubleValueFromCFG(node, "minOperatingPressure", minOperatingPressure);
                maxOperatingPressure = KShared.GetDoubleValueFromCFG(node, "maxOperatingPressure", maxOperatingPressure);
                minPressure = KShared.GetDoubleValueFromCFG(node, "minPressure", minPressure);
                maxPressure = KShared.GetDoubleValueFromCFG(node, "maxPressure", maxPressure);


                // Passive multipliers
                passiveMultiplier = KShared.GetDoubleValueFromCFG(node, "passiveMul", passiveMultiplier);
                passivePeriodMultiplier = KShared.GetDoubleValueFromCFG(node, "passivePeriodMul", passivePeriodMultiplier);

                // Charge multipliers
                chargeRateMultiplier = KShared.GetDoubleValueFromCFG(node, "chargeRateMul", chargeRateMultiplier);
                chargeDecayMultiplier = KShared.GetDoubleValueFromCFG(node, "chargeDecayMul", chargeDecayMultiplier);
                chargeConsumptionMultiplier = KShared.GetDoubleValueFromCFG(node, "chargeConMul", chargeConsumptionMultiplier);

                // I/O multipliers
                inputMultiplier = KShared.GetDoubleValueFromCFG(node, "inMul", inputMultiplier);
                outputMultiplier = KShared.GetDoubleValueFromCFG(node, "outMul", outputMultiplier);

                // Speed multiplier
                speedMul = KShared.GetDoubleValueFromCFG(node, "speedMul", speedMul);

                // Worker multipliers
                workersPilotsMultiplier = KShared.GetDoubleValueFromCFG(node, "workersPilotsMul", workersPilotsMultiplier);
                workersEngineersMultiplier = KShared.GetDoubleValueFromCFG(node, "workersEngineersMul", workersEngineersMultiplier);
                workersScientistsMultiplier = KShared.GetDoubleValueFromCFG(node, "workersScientistsMul", workersScientistsMultiplier);

                // Max distances multipliers
                maxInteractionDistanceMultiplier = KShared.GetDoubleValueFromCFG(node, "maxInteractionDistanceMul", maxInteractionDistanceMultiplier);
                maxDisplayDistanceMultiplier = KShared.GetDoubleValueFromCFG(node, "maxDisplayDistanceMul", maxDisplayDistanceMultiplier);
            }
            else
            {
                KShared.LogNoValueInNode("BIOME_CONFIG", "name", "Converter \"" + ConverterName + "\": Recipe ", "BatchISRUBiomeConfig/constructor");
                return;
            }
        }
    }

    /// <summary>
    /// A recipe for <see cref="KhemistryBatchISRU"/>.
    /// Contains inputs, outputs and multiple <see cref="BatchISRUBiomeConfig"/> to use.
    /// </summary>
    public class KhemistryBatchISRURecipe
    {
        ///// Structs and enums /////
        public struct ResourceInput
        {
            public string resourceName;
            public double amount;
            public ResourceFlowMode flowMode;
        }
        public struct PassiveResourceInput
        {
            public string resourceName;
            public double amount;
            public double period;  // Consume every period seconds
            public PowerfailResult powerfail;
            public double powerfailExplosionRadius;         // Only used if powerfail == PowerfailResult.Explode
            public double powerfailExplosionTemperature;     // Celsius, only used if powerfail == PowerfailResult.Explode
            public ResourceFlowMode flowMode;
            public bool ignorePowerfail;  // If true, the resource will be always consumed even if it tries to powerfail
        }

        public struct ResourceOutput
        {
            public string resourceName;
            public double amount;
            public bool dumpExcess;
        }

        public struct ResourceOutputMaterial
        {
            public string name;
            public string shape;
            public string size;
            public bool usesParams;
            public Dictionary<string, string> parameters;
            public double amount;
            public double outVolume;
        }

        public enum PowerfailResult { Pause, Stop, Explode, Maint, Void }

        ///// Variables /////
        public readonly List<ResourceInput> _inputs = new List<ResourceInput>();
        public readonly List<PassiveResourceInput> _passiveInputs = new List<PassiveResourceInput>();
        public readonly List<ResourceOutput> _outputs = new List<ResourceOutput>();
        public readonly List<ResourceOutputMaterial> _outputMaterials = new List<ResourceOutputMaterial>();
        public Dictionary<ResourceOutputMaterial, double> _materialOutputAmount = new Dictionary<ResourceOutputMaterial, double>();

        public double _recipeTime = 0;  // in seconds

        public uint _workersPilots = 0;
        public uint _workersEngineers = 0;
        public uint _workersScientists = 0;

        public bool _workersEVA = true;
        public bool _workersCREW = true;

        // Keyed by planet name (or "ALL"), then by biome name (or "ALL") for that planet.
        public Dictionary<string, Dictionary<string, BatchISRUBiomeConfig>> _planetConfigs = new Dictionary<string, Dictionary<string, BatchISRUBiomeConfig>>();

        ///// Identity /////
        public string _name = "Recipe";
        public List<string> _recipeTypes = new List<string>();
        public List<string> _recipeSubtypes = new List<string>();
        public List<string> _recipeSubsubtypes = new List<string>();

        ///// Charging (optional per-recipe) /////
        public bool _chargingRequired = false;
        public float _chargeRate = 0f;
        public float _chargeDecay = 0f;
        public readonly List<string> _chargeNames = new List<string>();
        public readonly List<float> _chargeAmounts = new List<float>();

        ///// Controls /////
        public bool _controlsShowPAW = true;
        public bool _controlsShowEVA = false;

        public ConfigNode mainNode = new ConfigNode();

        ///// Functions /////

        /// <summary>
        /// Loads everything shared between a local RECIPE node in KhemistryBatchISRU and a
        /// top level KHEMISTRYBATCHISRU_RECIPE node: identity, charging, planet/biome configs,
        /// inputs/outputs/materials, timing, control rules, and worker requirements.
        /// </summary>
        public KhemistryBatchISRURecipe(ConfigNode node, string ConverterName)
        {
            try
            {
                _name = KShared.GetStrValueFromCFG(node, "name", ConverterName);

                _recipeTypes.Clear();
                _recipeTypes.AddRange(node.GetValues("recipeType"));
                _recipeSubtypes.Clear();
                _recipeSubtypes.AddRange(node.GetValues("recipeSubtype"));
                _recipeSubsubtypes.Clear();
                _recipeSubsubtypes.AddRange(node.GetValues("recipeSubsubtype"));

                ///// Charging /////
                _chargeNames.Clear();
                _chargeAmounts.Clear();
                if (node.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (node.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                        if (float.TryParse(a, out float amtTmp)) _chargeAmounts.Add(amtTmp);
                if (_chargeNames.Count != _chargeAmounts.Count)
                {
                    KShared.LogError(
                        "Recipe \"" + _name + "\": CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch — charging disabled for this recipe.",
                        "KhemistryBatchISRURecipe/constructor");
                    _chargeNames.Clear();
                    _chargeAmounts.Clear();
                }

                _chargeRate = KShared.GetFloatValueFromCFG(node, "chargeRate", 0f);
                _chargeDecay = KShared.GetFloatValueFromCFG(node, "chargeDecay", 0f);
                _chargingRequired = _chargeNames.Count > 0 && _chargeRate > 0f;

                ///// Planet/biome configs /////
                _planetConfigs.Clear();
                if (node.HasNode("PLANET_CONFIG"))
                {
                    foreach (ConfigNode planetNode in node.GetNodes("PLANET_CONFIG"))
                    {
                        string planetName = KShared.GetStrValueFromCFG(planetNode, "name", "ALL");

                        if (!planetNode.HasNode("BIOME_CONFIG"))
                        {
                            KShared.LogNoNode("BIOME_CONFIG", "Converter \"" + ConverterName + "\": Recipe \"" + _name + "\" ", "KhemistryBatchISRURecipe/constructor");
                            continue;
                        }

                        if (!_planetConfigs.TryGetValue(planetName, out Dictionary<string, BatchISRUBiomeConfig> biomeDict))
                        {
                            biomeDict = new Dictionary<string, BatchISRUBiomeConfig>();
                            _planetConfigs.Add(planetName, biomeDict);
                        }

                        foreach (ConfigNode biomeNode in planetNode.GetNodes("BIOME_CONFIG"))
                        {
                            BatchISRUBiomeConfig biomeConfig = new BatchISRUBiomeConfig(biomeNode, ConverterName);
                            string biomeKey = biomeConfig.biomeName ?? "ALL";
                            biomeDict[biomeKey] = biomeConfig;
                        }
                    }
                }
                else
                {
                    // Instead of requiring a node just use an empty one with name=ALL
                    //KShared.LogNoNode("PLANET_CONFIG", "Converter \"" + ConverterName + "\": Recipe \"" + _name + "\" ", "KhemistryBatchISRURecipe/constructor");
                    Dictionary<string, BatchISRUBiomeConfig> biomeDict = new Dictionary<string, BatchISRUBiomeConfig>();
                    ConfigNode configNode = new ConfigNode("BIOME_CONFIG");
                    configNode.AddValue("name", "ALL");
                    biomeDict.Add("ALL", new BatchISRUBiomeConfig(configNode, ConverterName));
                    _planetConfigs.Add("ALL", biomeDict);
                }

                ///// Inputs /////
                _inputs.Clear();
                foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("name");
                    if (string.IsNullOrEmpty(resName)) continue;

                    double amount = KShared.GetDoubleValueFromCFG(inputNode, "amount", 0.0);

                    ResourceFlowMode flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW;
                    string flowStr = inputNode.GetValue("flowmode");
                    if (!string.IsNullOrEmpty(flowStr))
                    {
                        if (Enum.TryParse(flowStr.Trim(), true, out ResourceFlowMode parsed))
                            flowMode = parsed;
                        else
                            KShared.LogError(
                                "Recipe \"" + _name + "\": Unknown flowmode \"" + flowStr + "\" for " + resName + ", defaulting to STAGE_PRIORITY_FLOW.",
                                "KhemistryBatchISRURecipe/constructor");
                    }

                    _inputs.Add(new ResourceInput { resourceName = resName, amount = amount, flowMode = flowMode });
                }

                ///// Passive inputs (PINPUT_RESOURCE) /////
                _passiveInputs.Clear();
                foreach (ConfigNode pinputNode in node.GetNodes("PINPUT_RESOURCE"))
                {
                    string resName = pinputNode.GetValue("name");
                    if (string.IsNullOrEmpty(resName))
                    {
                        KShared.LogNoValueInNode("PINPUT_RESOURCE", "name", "Recipe \"" + _name + "\" ", "KhemistryBatchISRURecipe/constructor");
                        continue;
                    }

                    double amount = KShared.GetDoubleValueFromCFG(pinputNode, "amount", 0.0);
                    double period = KShared.GetDoubleValueFromCFG(pinputNode, "period", 1.0);
                    if (period <= 0.0) period = 1.0;

                    ResourceFlowMode flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW;
                    string pFlowStr = pinputNode.GetValue("flowmode");
                    if (!string.IsNullOrEmpty(pFlowStr))
                    {
                        if (Enum.TryParse(pFlowStr.Trim(), true, out ResourceFlowMode pParsed))
                            flowMode = pParsed;
                        else
                            KShared.LogError(
                                "Recipe \"" + _name + "\": Unknown flowmode \"" + pFlowStr + "\" for PINPUT_RESOURCE " + resName + ", defaulting to STAGE_PRIORITY_FLOW.",
                                "KhemistryBatchISRURecipe/constructor");
                    }

                    bool.TryParse(pinputNode.GetValue("ignorePowerfail"), out bool ignorePowerfail);

                    // Accept both spellings: "powerfail" (correct, used in actual configs) and
                    // "powefail" (the original literal spec) — the former takes precedence.
                    PowerfailResult powerfail = PowerfailResult.Pause;
                    double explosionRadius = 0.0;
                    double explosionTemperature = 0.0;
                    string pfRaw = pinputNode.GetValue("powerfail") ?? pinputNode.GetValue("powefail");
                    if (!string.IsNullOrEmpty(pfRaw))
                    {
                        string pf = pfRaw.Trim().Trim('"').ToUpper();
                        if (pf == "PAUSE")
                        {
                            powerfail = PowerfailResult.Pause;
                        }
                        else if (pf == "STOP")
                        {
                            powerfail = PowerfailResult.Stop;
                        }
                        else if (pf == "VOID")
                        {
                            powerfail = PowerfailResult.Void;
                        }
                        else if (pf == "MAINT")
                        {
                            powerfail = PowerfailResult.Maint;
                        }
                        else if (pf.StartsWith("EXPLODE,"))
                        {
                            string[] parts = pf.Substring(8).Split(',');
                            if (parts.Length == 2
                                && double.TryParse(parts[0], out double radius)
                                && double.TryParse(parts[1], out double tempC))
                            {
                                powerfail = PowerfailResult.Explode;
                                explosionRadius = radius;
                                explosionTemperature = tempC;
                            }
                            else
                            {
                                KShared.LogError(
                                    "Recipe \"" + _name + "\": Could not parse EXPLODE radius/temperature \"" + pfRaw + "\" for PINPUT_RESOURCE " + resName + " (expected EXPLODE,radiusMeters,tempCelsius) — defaulting to PAUSE.",
                                    "KhemistryBatchISRURecipe/constructor");
                                powerfail = PowerfailResult.Pause;
                            }
                        }
                        else
                        {
                            KShared.LogError(
                                "Recipe \"" + _name + "\": Unknown powefail \"" + pfRaw + "\" for PINPUT_RESOURCE " + resName + " — defaulting to PAUSE.",
                                "KhemistryBatchISRURecipe/constructor");
                            powerfail = PowerfailResult.Pause;
                        }
                    }

                    _passiveInputs.Add(new PassiveResourceInput
                    {
                        resourceName = resName,
                        amount = amount,
                        period = period,
                        powerfail = powerfail,
                        powerfailExplosionRadius = explosionRadius,
                        powerfailExplosionTemperature = explosionTemperature,
                        flowMode = flowMode,
                        ignorePowerfail = ignorePowerfail
                    });
                }

                ///// Outputs /////
                _outputs.Clear();
                foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("name");
                    if (string.IsNullOrEmpty(resName)) continue;

                    double amount = KShared.GetDoubleValueFromCFG(outputNode, "amount", 0.0);
                    bool.TryParse(outputNode.GetValue("dumpExcess"), out bool dumpExcess);

                    _outputs.Add(new ResourceOutput { resourceName = resName, amount = amount, dumpExcess = dumpExcess });
                }

                ///// Output materials /////
                _outputMaterials.Clear();
                foreach (ConfigNode matNode in node.GetNodes("OUTPUT_RESOURCE_MATERIAL"))
                {
                    string matName = matNode.GetValue("name");
                    if (string.IsNullOrEmpty(matName)) continue;

                    string shape = matNode.GetValue("shape");
                    string size = matNode.GetValue("size");
                    double amount = KShared.GetDoubleValueFromCFG(matNode, "amount", 1.0);
                    double outVolume = KShared.GetDoubleValueFromCFG(matNode, "outVolume", 0.0);

                    bool usesParams = matNode.HasNode("PARAMS");
                    Dictionary<string, string> parameters = new Dictionary<string, string>();
                    if (usesParams)
                        foreach (string key in matNode.GetNode("PARAMS").values.DistinctNames())
                            parameters.Add(key, matNode.GetNode("PARAMS").GetValue(key));

                    _outputMaterials.Add(new ResourceOutputMaterial
                    {
                        name = matName,
                        shape = shape,
                        size = size,
                        usesParams = usesParams,
                        parameters = parameters,
                        amount = amount,
                        outVolume = outVolume
                    });
                }

                if (_outputs.Count == 0 && _outputMaterials.Count == 0)
                    KShared.LogError(
                        "Recipe \"" + _name + "\" has no OUTPUT_RESOURCE nor OUTPUT_RESOURCE_MATERIAL nodes — it will do nothing.",
                        "KhemistryBatchISRURecipe/constructor");

                ///// Timing and control /////
                _recipeTime = KShared.GetDoubleValueFromCFG(node, "recipeTime", 0.0);
                if (_recipeTime <= 0.0)
                    KShared.LogError(
                        "Recipe \"" + _name + "\" has no valid recipeTime set — it will never complete a batch.",
                        "KhemistryBatchISRURecipe/constructor");

                KShared.ParseShowRule(
                    KShared.GetStrValueFromCFG(node, "controlRules", "PAW"),
                    out _controlsShowPAW, out _controlsShowEVA, "controlRules", _name);

                ///// Workers /////
                _workersEngineers = (uint)KShared.GetIntValueFromCFG(node, "workersEngineers", 0);
                _workersPilots = (uint)KShared.GetIntValueFromCFG(node, "workersPilots", 0);
                _workersScientists = (uint)KShared.GetIntValueFromCFG(node, "workersScientists", 0);

                _workersEVA = true;
                _workersCREW = false;
                string workersTypeStr = KShared.GetStrValueFromCFG(node, "workersType", "EVA").Trim().ToUpper();
                switch (workersTypeStr)
                {
                    case "EVA": _workersEVA = true; _workersCREW = false; break;
                    case "CREW": _workersEVA = false; _workersCREW = true; break;
                    case "EVA+CREW":
                    case "CREW+EVA": _workersEVA = true; _workersCREW = true; break;
                    default:
                        KShared.LogError(
                            "Recipe \"" + _name + "\": Unknown workersType \"" + workersTypeStr + "\" — defaulting to EVA.",
                            "KhemistryBatchISRURecipe/constructor");
                        break;
                }

                mainNode = new ConfigNode();
                node.CopyTo(mainNode);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryBatchISRURecipe/constructor");
            }
        }

        /// <summary>
        /// Looks up the applicable BatchISRUBiomeConfig for a given planet/biome, falling back
        /// from exact biome → ALL biome on that planet → ALL planet/ALL biome → null (no config,
        /// recipe cannot operate at the current location).
        /// </summary>
        public BatchISRUBiomeConfig GetBiomeConfig(string planet, string biome)
        {
            if (_planetConfigs.TryGetValue(planet, out Dictionary<string, BatchISRUBiomeConfig> biomeDict))
            {
                if (biome != null && biomeDict.TryGetValue(biome, out BatchISRUBiomeConfig exact))
                    return exact;
                if (biomeDict.TryGetValue("ALL", out BatchISRUBiomeConfig planetAll))
                    return planetAll;
            }

            if (_planetConfigs.TryGetValue("ALL", out Dictionary<string, BatchISRUBiomeConfig> allPlanetDict))
            {
                if (biome != null && allPlanetDict.TryGetValue(biome, out BatchISRUBiomeConfig exactAll))
                    return exactAll;
                if (allPlanetDict.TryGetValue("ALL", out BatchISRUBiomeConfig globalAll))
                    return globalAll;
            }

            return null;
        }

        /// <summary>
        /// Returns a copy of this recipe with all resource/material amounts multiplied by
        /// the given factor. Used when importing recipes by recipeType/RECIPE_NAMES with a
        /// recipeMultiplier or per-name multiplier applied. Timing, workers, charging, and
        /// planet configs are shared by reference (not affected by the multiplier).
        /// </summary>
        public KhemistryBatchISRURecipe ScaledCopy(double multiplier)
        {
            KhemistryBatchISRURecipe copy = new KhemistryBatchISRURecipe();
            copy._name = _name;
            copy._recipeTypes = _recipeTypes;
            copy._recipeSubtypes = _recipeSubtypes;
            copy._recipeSubsubtypes = _recipeSubsubtypes;
            copy._chargingRequired = _chargingRequired;
            copy._chargeRate = _chargeRate;
            copy._chargeDecay = _chargeDecay;
            copy._chargeNames.AddRange(_chargeNames);
            copy._chargeAmounts.AddRange(_chargeAmounts);
            copy._controlsShowPAW = _controlsShowPAW;
            copy._controlsShowEVA = _controlsShowEVA;
            copy._planetConfigs = _planetConfigs;
            copy._recipeTime = _recipeTime;
            copy._workersEngineers = _workersEngineers;
            copy._workersPilots = _workersPilots;
            copy._workersScientists = _workersScientists;
            copy._workersEVA = _workersEVA;
            copy._workersCREW = _workersCREW;
            copy.mainNode = mainNode;

            if (multiplier <= 0.0) multiplier = 1.0;

            foreach (ResourceInput inp in _inputs)
                copy._inputs.Add(new ResourceInput { resourceName = inp.resourceName, amount = inp.amount * multiplier, flowMode = inp.flowMode });
            foreach (PassiveResourceInput pinp in _passiveInputs)
                copy._passiveInputs.Add(new PassiveResourceInput
                {
                    resourceName = pinp.resourceName,
                    amount = pinp.amount * multiplier,
                    period = pinp.period,
                    powerfail = pinp.powerfail,
                    powerfailExplosionRadius = pinp.powerfailExplosionRadius,
                    powerfailExplosionTemperature = pinp.powerfailExplosionTemperature,
                    flowMode = pinp.flowMode,
                    ignorePowerfail = pinp.ignorePowerfail
                });
            foreach (ResourceOutput outp in _outputs)
                copy._outputs.Add(new ResourceOutput { resourceName = outp.resourceName, amount = outp.amount * multiplier, dumpExcess = outp.dumpExcess });
            foreach (ResourceOutputMaterial mat in _outputMaterials)
                copy._outputMaterials.Add(new ResourceOutputMaterial
                {
                    name = mat.name,
                    shape = mat.shape,
                    size = mat.size,
                    usesParams = mat.usesParams,
                    parameters = mat.parameters,
                    amount = mat.amount * multiplier,
                    outVolume = mat.outVolume
                });

            return copy;
        }

        /// <summary>Parameterless constructor used internally by ScaledCopy.</summary>
        public KhemistryBatchISRURecipe() { }

        /// <summary>True if this recipe is tagged with the given recipeType/Subtype/Subsubtype (any left null are not checked).</summary>
        public bool MatchesTypes(string recipeType, string recipeSubtype, string recipeSubsubtype)
        {
            if (recipeType != null && !_recipeTypes.Contains(recipeType)) return false;
            if (recipeSubtype != null && !_recipeSubtypes.Contains(recipeSubtype)) return false;
            if (recipeSubsubtype != null && !_recipeSubsubtypes.Contains(recipeSubsubtype)) return false;
            return true;
        }

        ///// Module-level recipe overrides /////

        // Values on the MODULE node that belong to the module itself (identity, converter naming,
        // recipe-selection filters, and RECIPE/RECIPE_NAMES/RECIPE_MULTIPLIERS bookkeeping) rather
        // than being a recipe field to override. These are never applied on top of a loaded recipe.
        private static readonly HashSet<string> _moduleOnlyValueKeys = new HashSet<string>
        {
            "name", "ConverterName", "StartActionName", "StopActionName",
            "recipeType", "recipeSubtype", "recipeSubsubtype",
            "recipeMultiplier", "maxInteractionDistance", "workersCrewSamePart"
        };
        private static readonly HashSet<string> _moduleOnlyNodeKeys = new HashSet<string>
        {
            "RECIPE", "RECIPE_NAMES", "RECIPE_MULTIPLIERS"
        };

        // Node types that are merged by their "name" value (resource name) instead of being wholesale
        // replaced: a module entry with a resource name matching a base entry overrides just that
        // entry, and a module entry with a new resource name is added alongside the base entries.
        private static readonly HashSet<string> _keyedByNameNodeKeys = new HashSet<string>
        {
            "INPUT_RESOURCE", "OUTPUT_RESOURCE", "PINPUT_RESOURCE", "OUTPUT_RESOURCE_MATERIAL"
        };

        // Node types that hold a single node full of repeated values (e.g. CHARGE_CON_NAMES holding
        // several "name" values), where the module's values are appended after the recipe's own,
        // rather than the module's node replacing the recipe's node outright. Maps the node name to
        // the value key it repeats.
        private static readonly Dictionary<string, string> _appendedValueNodeKeys = new Dictionary<string, string>
        {
            { "CHARGE_CON_NAMES", "name" },
            { "CHARGE_CON_AMOUNTS", "amount" }
        };

        /// <summary>
        /// Merges a node type that holds a single set of repeated values (e.g. CHARGE_CON_NAMES'
        /// repeated "name" values) by concatenating every base occurrence's values followed by every
        /// override occurrence's values, into one node, preserving order.
        /// </summary>
        private static ConfigNode MergeAppendedValueNode(ConfigNode baseRecipeNode, ConfigNode overrideModuleNode, string nodeName, string valueKey)
        {
            ConfigNode merged = new ConfigNode(nodeName);
            foreach (ConfigNode n in baseRecipeNode.GetNodes(nodeName))
                foreach (string v in n.GetValues(valueKey))
                    merged.AddValue(valueKey, v);
            foreach (ConfigNode n in overrideModuleNode.GetNodes(nodeName))
                foreach (string v in n.GetValues(valueKey))
                    merged.AddValue(valueKey, v);
            return merged;
        }

        /// <summary>
        /// Merges all nodes of a given name (e.g. all PINPUT_RESOURCE nodes) between a base recipe
        /// node and an override (MODULE) node, keyed by each node's "name" value. A base entry whose
        /// "name" also appears on the override is replaced entirely by the override's entry; base
        /// entries with no matching override entry are kept; override entries with a "name" not
        /// present on the base are appended as new entries.
        /// </summary>
        private static List<ConfigNode> MergeKeyedByName(ConfigNode baseRecipeNode, ConfigNode overrideModuleNode, string nodeName)
        {
            List<string> keyOrder = new List<string>();
            Dictionary<string, ConfigNode> keyed = new Dictionary<string, ConfigNode>();

            foreach (ConfigNode n in baseRecipeNode.GetNodes(nodeName))
            {
                string key = n.GetValue("name") ?? ("\0unnamed" + keyOrder.Count);
                if (!keyed.ContainsKey(key)) keyOrder.Add(key);
                keyed[key] = n;
            }

            foreach (ConfigNode n in overrideModuleNode.GetNodes(nodeName))
            {
                string key = n.GetValue("name") ?? ("\0unnamed" + keyOrder.Count);
                if (!keyed.ContainsKey(key)) keyOrder.Add(key);
                keyed[key] = n;
            }

            List<ConfigNode> result = new List<ConfigNode>();
            foreach (string key in keyOrder)
            {
                ConfigNode copy = new ConfigNode(nodeName);
                keyed[key].CopyTo(copy);
                result.Add(copy);
            }
            return result;
        }

        /// <summary>
        /// Merges a single override PLANET_CONFIG node onto a (possibly null) base PLANET_CONFIG node.
        /// BIOME_CONFIG entries are merged by name: an override biome fully replaces the base biome of
        /// the same name, and any base biomes not touched by the override are kept as-is.
        /// </summary>
        private static ConfigNode MergePlanetConfigNode(ConfigNode basePlanet, ConfigNode overridePlanet)
        {
            ConfigNode merged = new ConfigNode("PLANET_CONFIG");
            merged.AddValue("name", KShared.GetStrValueFromCFG(overridePlanet, "name", "ALL"));

            List<string> biomeOrder = new List<string>();
            Dictionary<string, ConfigNode> biomeNodes = new Dictionary<string, ConfigNode>();

            if (basePlanet != null)
            {
                foreach (ConfigNode biomeNode in basePlanet.GetNodes("BIOME_CONFIG"))
                {
                    string biomeName = KShared.GetStrValueFromCFG(biomeNode, "name", "ALL");
                    if (!biomeNodes.ContainsKey(biomeName)) biomeOrder.Add(biomeName);
                    biomeNodes[biomeName] = biomeNode;
                }
            }

            foreach (ConfigNode biomeNode in overridePlanet.GetNodes("BIOME_CONFIG"))
            {
                string biomeName = KShared.GetStrValueFromCFG(biomeNode, "name", "ALL");
                if (!biomeNodes.ContainsKey(biomeName)) biomeOrder.Add(biomeName);
                biomeNodes[biomeName] = biomeNode;
            }

            foreach (string biomeName in biomeOrder)
            {
                ConfigNode copy = new ConfigNode("BIOME_CONFIG");
                biomeNodes[biomeName].CopyTo(copy);
                merged.AddNode(copy);
            }

            return merged;
        }

        /// <summary>
        /// Merges all PLANET_CONFIG nodes on a base recipe node with all PLANET_CONFIG nodes on an
        /// override (MODULE) node: planets present in both are merged (see MergePlanetConfigNode),
        /// planets only in the base are kept unchanged, and planets only in the override are added
        /// as new entries.
        /// </summary>
        private static List<ConfigNode> MergePlanetConfigs(ConfigNode baseRecipeNode, ConfigNode overrideModuleNode)
        {
            List<string> planetOrder = new List<string>();
            Dictionary<string, ConfigNode> basePlanets = new Dictionary<string, ConfigNode>();
            foreach (ConfigNode p in baseRecipeNode.GetNodes("PLANET_CONFIG"))
            {
                string pName = KShared.GetStrValueFromCFG(p, "name", "ALL");
                if (!basePlanets.ContainsKey(pName)) planetOrder.Add(pName);
                basePlanets[pName] = p;
            }

            List<string> overridePlanetOrder = new List<string>();
            Dictionary<string, ConfigNode> overridePlanets = new Dictionary<string, ConfigNode>();
            foreach (ConfigNode p in overrideModuleNode.GetNodes("PLANET_CONFIG"))
            {
                string pName = KShared.GetStrValueFromCFG(p, "name", "ALL");
                if (!overridePlanets.ContainsKey(pName)) overridePlanetOrder.Add(pName);
                overridePlanets[pName] = p;
            }

            List<ConfigNode> result = new List<ConfigNode>();
            HashSet<string> handled = new HashSet<string>();

            foreach (string pName in planetOrder)
            {
                if (overridePlanets.TryGetValue(pName, out ConfigNode ovPlanet))
                {
                    result.Add(MergePlanetConfigNode(basePlanets[pName], ovPlanet));
                    handled.Add(pName);
                }
                else
                {
                    ConfigNode copy = new ConfigNode("PLANET_CONFIG");
                    basePlanets[pName].CopyTo(copy);
                    result.Add(copy);
                }
            }

            foreach (string pName in overridePlanetOrder)
            {
                if (handled.Contains(pName)) continue;
                ConfigNode copy = new ConfigNode("PLANET_CONFIG");
                overridePlanets[pName].CopyTo(copy);
                result.Add(copy);
            }

            return result;
        }

        /// <summary>
        /// Applies the recipe-related values and nodes on a <see cref="KhemistryBatchISRU"/> MODULE node on top of
        /// a loaded recipe's config node, returning a new merged node suitable for re-parsing into a
        /// <see cref="KhemistryBatchISRURecipe"/>. Module-only bookkeeping (identity, converter naming, recipe
        /// selection filters, RECIPE/RECIPE_NAMES/RECIPE_MULTIPLIERS) is ignored. Every other value
        /// or node present on the MODULE node fully overrides the matching key on the recipe, except
        /// PLANET_CONFIG (and its BIOME_CONFIG children), which are merged instead — see
        /// <see cref="MergePlanetConfigs"/>/<see cref="MergePlanetConfigNode"/>.
        /// </summary>
        public static ConfigNode ApplyModuleOverrides(ConfigNode moduleNode, ConfigNode baseRecipeNode)
        {
            ConfigNode merged = new ConfigNode();
            baseRecipeNode.CopyTo(merged);

            foreach (string valueName in moduleNode.values.DistinctNames())
            {
                if (_moduleOnlyValueKeys.Contains(valueName)) continue;

                while (merged.HasValue(valueName)) merged.RemoveValue(valueName);
                foreach (string v in moduleNode.GetValues(valueName))
                    merged.AddValue(valueName, v);
            }

            foreach (string nodeName in moduleNode.nodes.DistinctNames())
            {
                if (_moduleOnlyNodeKeys.Contains(nodeName) || nodeName == "PLANET_CONFIG") continue;

                if (_appendedValueNodeKeys.TryGetValue(nodeName, out string valueKey))
                {
                    ConfigNode mergedAppended = MergeAppendedValueNode(merged, moduleNode, nodeName, valueKey);
                    while (merged.HasNode(nodeName)) merged.RemoveNode(nodeName);
                    merged.AddNode(mergedAppended);
                    continue;
                }

                if (_keyedByNameNodeKeys.Contains(nodeName))
                {
                    List<ConfigNode> mergedKeyed = MergeKeyedByName(merged, moduleNode, nodeName);
                    while (merged.HasNode(nodeName)) merged.RemoveNode(nodeName);
                    foreach (ConfigNode n in mergedKeyed)
                        merged.AddNode(n);
                    continue;
                }

                while (merged.HasNode(nodeName)) merged.RemoveNode(nodeName);
                foreach (ConfigNode n in moduleNode.GetNodes(nodeName))
                {
                    ConfigNode copy = new ConfigNode(nodeName);
                    n.CopyTo(copy);
                    merged.AddNode(copy);
                }
            }

            if (moduleNode.HasNode("PLANET_CONFIG"))
            {
                List<ConfigNode> mergedPlanets = MergePlanetConfigs(merged, moduleNode);
                while (merged.HasNode("PLANET_CONFIG")) merged.RemoveNode("PLANET_CONFIG");
                foreach (ConfigNode p in mergedPlanets)
                    merged.AddNode(p);
            }

            return merged;
        }
    }

    /// <summary>
    /// An ISRU module that uses batches.
    /// Stock and Advanced ISRU modules always use units/second, but this module uses batches of resouces.
    /// It has a similar amount of features as <see cref="KhemistryAdvancedISRU"/> but also has a few new ones.
    /// </summary>
    public class KhemistryBatchISRU : PartModule
    {
        ///// Activity and displays /////        
        [KSPField(isPersistant = true)] public bool isRunning = false;
        [KSPField(isPersistant = true)] public bool needsMaintenance = false;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Status", groupName = "khemistrybatchisru",
                  groupDisplayName = "Khemistry Batch ISRU", groupStartCollapsed = false)]
        public string statusDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistrybatchisru")]
        public string chargeDisplay = "N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Progress", groupName = "khemistrybatchisru")]
        public string progressDisplay = "Off";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistrybatchisru")]
        public string stateDisplay = "Off";

        public bool IsCurrentlyActive { get; protected set; } = false;

        ///// Animation /////
        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        private Animation _activeAnim;
        private string _activeAnimationName;
        private bool _animationPlaying = false;

        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryBatchISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = WrapMode.Loop;

            KShared.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryBatchISRU/SetupActiveAnimation");
        }

        /// <summary>
        /// Start or stop the active animation.
        /// Will play it until it is turned off.
        /// </summary>
        /// <param name="playing">Whether to play the active animation or not.</param>
        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        /// <summary>
        /// Play the active animation once, without looping.
        /// </summary>
        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        ///// State /////
        /// <summary>
        /// The state of a converter.
        /// <list type="bullet">Off: The converter is currently turned off and is not running.</list>
        /// <list type="bullet">Charging: Same as Off but the converter is charging.</list>
        /// <list type="bullet">On: The converter is currently turned on and is running.</list>
        /// </summary>
        public enum ConverterState { Off, Charging, On }

        /// <summary>
        /// The current state of this converter, see <see cref="ConverterState"/> for options.
        /// </summary>
        [KSPField(isPersistant = true)]
        public ConverterState state = ConverterState.Off;

        ///// Charging /////
        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start Converter";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop Converter";

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;

        protected readonly List<string> _chargeNames = new List<string>();
        protected readonly List<float> _chargeAmounts = new List<float>();

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistrybatchisru")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == ConverterState.On) return;
            state = ConverterState.Charging;
            KShared.Log("Charging enabled.", "KhemistryBatchISRU/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistrybatchisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != ConverterState.Charging) return;
            state = ConverterState.Off;
            KShared.Log("Charging disabled.", "KhemistryBatchISRU/DisableCharging");
        }

        ///// Powerfail /////
        /// <summary>
        /// Applies a powerfail result against the current batch. PAUSE leaves the batch and
        /// converter untouched (caller is expected to just skip this tick). STOP refunds
        /// everything consumed so far this batch (via _passiveConsumedThisBatch) and resets
        /// progress, then stops the converter. VOID does the same but discards the consumed
        /// resources instead of refunding them. MAINT is VOID plus a maintenance requirement.
        /// EXPLODE destroys the part and applies falling-off heat to nearby parts.
        /// </summary>
        protected void TriggerPowerfail(Part contextPart, KhemistryBatchISRURecipe.PowerfailResult powerfailResult,
            double explosionRadius = 0.0, double explosionTemperatureCelsius = 0.0)
        {
            KShared.Log(
                "Converter \"" + ConverterName + "\" powerfailed. Result: " + powerfailResult,
                "KhemistryBatchISRU/TriggerPowerfail");

            switch (powerfailResult)
            {
                case KhemistryBatchISRURecipe.PowerfailResult.Pause:
                    statusDisplay = "Paused";
                    break;
                case KhemistryBatchISRURecipe.PowerfailResult.Stop:
                    RefundPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case KhemistryBatchISRURecipe.PowerfailResult.Void:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail, resources lost)";
                    break;
                case KhemistryBatchISRURecipe.PowerfailResult.Maint:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    needsMaintenance = true;
                    statusDisplay = "Needs maintenance";
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + ConverterName + "\": Requires maintenance by an Engineer.",
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    break;
                case KhemistryBatchISRURecipe.PowerfailResult.Explode:
                    KShared.TriggerExplosionWithHeat(contextPart, (float)explosionRadius, (float)explosionTemperatureCelsius);
                    break;
            }
        }

        /// <summary>
        /// Gives back everything withdrawn by passive inputs during the in-progress batch.
        /// </summary>
        protected void RefundPassiveConsumption()
        {
            if (_activeRecipe == null) return;
            for (int i = 0; i < _activeRecipe._passiveInputs.Count && i < _passiveConsumedThisBatch.Count; i++)
            {
                double amount = _passiveConsumedThisBatch[i];
                if (amount <= 0.0) continue;
                part.RequestResource(_activeRecipe._passiveInputs[i].resourceName, -amount, _activeRecipe._passiveInputs[i].flowMode);
                _passiveConsumedThisBatch[i] = 0.0;
            }
        }

        /// <summary>
        /// Discards the tracked in-progress-batch consumption without refunding it.
        /// </summary>
        protected void ClearPassiveConsumption()
        {
            for (int i = 0; i < _passiveConsumedThisBatch.Count; i++)
                _passiveConsumedThisBatch[i] = 0.0;
        }

        ///// Converter state buttons /////        
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Prepare converter",
                  groupName = "khemistrybatchisru", active = false)]
        public void TurnOnConverter()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = ConverterState.On;
            KShared.Log("Converter turned ON.", "KhemistryAdvancedISRUBase/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistrybatchisru", active = false)]
        public void TurnOffConverter()
        {
            state = ConverterState.Off;
            KShared.Log("Converter turned OFF.", "KhemistryAdvancedISRUBase/TurnOffContainer");
        }

        ///// Variables /////
        protected bool _controlsShowPAW = true;
        protected bool _controlsShowEVA = false;

        protected KhemistryRuntimeData _runtimeData = null;

        // The actual values, multiplied by a multiplier
        protected float _maxInteractionDistance = 7f;
        protected float _maxDisplayDistance = 10f;

        // The values loaded from the config
        protected float _configMaxInteractionDistance = 7f;
        protected float _configMaxDisplayDistance = 10f;

        protected List<KhemistryBatchISRURecipe> recipes = new List<KhemistryBatchISRURecipe>();

        protected bool _fatalConfigError = false;
        protected double _outputWarnCooldown = 0.0;

        ///// Recipe importing /////
        [KSPField(isPersistant = false)] public string recipeType = null;
        [KSPField(isPersistant = false)] public string recipeSubtype = null;
        [KSPField(isPersistant = false)] public string recipeSubsubtype = null;

        [KSPField(isPersistant = false)] public float recipeMultiplier = 1f;

        [KSPField(isPersistant = false)] public bool workersCrewSamePart = false;

        protected readonly List<string> _recipeNames = new List<string>();
        protected readonly List<float> _recipeMultipliers = new List<float>();

        ///// Active recipe /////
        [KSPField(isPersistant = true)] public string activeRecipeName = null;
        [KSPField(isPersistant = true)] public double batchProgress = 0.0;

        protected KhemistryBatchISRURecipe _activeRecipe = null;

        // Parallel to _activeRecipe._passiveInputs; not persisted (periods are short, so
        // losing phase across a save/reload is a harmless simplification).
        protected readonly List<double> _passiveTimers = new List<double>();

        // Cumulative amount actually withdrawn per passive input since the last time
        // batchProgress was reset to 0 — needed so STOP can refund exactly what was taken
        // during the in-progress batch, while VOID/MAINT discard it instead.
        protected readonly List<double> _passiveConsumedThisBatch = new List<double>();

        protected readonly Dictionary<KhemistryBatchISRURecipe.ResourceOutputMaterial, double> _materialOutputAmount =
            new Dictionary<KhemistryBatchISRURecipe.ResourceOutputMaterial, double>();

        ///// Converter controlling /////
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
        groupName = "khemistrybatchisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (state != ConverterState.On) return;
            isRunning = true;
            KShared.Log("Converter \"" + ConverterName + "\" started.", "KhemistryAdvancedISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistrybatchisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Log("Converter \"" + ConverterName + "\" stopped.", "KhemistryAdvancedISRU/StopConverter");
            UpdateEventVisibility();
        }
        [KSPAction("Start Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StartConverterAction(KSPActionParam param)
        {
            StartConverter();
        }

        [KSPAction("Stop Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StopConverterAction(KSPActionParam param)
        {
            StopConverter();
        }

        ///// Config loading /////
        /// <summary>
        /// Load the config values from part information.
        /// </summary>
        protected void LoadConfigFromPartInfo()
        {
            ConfigNode moduleNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryBatchISRU");
            KShared shared = KShared.Instance;

            ///// Charging /////
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                        if (float.TryParse(a, out float tmp))
                            _chargeAmounts.Add(tmp);
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryBatchISRU/LoadConfigFromPartInfo");
            }

            ///// Recipes: local RECIPE nodes /////
            recipes.Clear();
            if (moduleNode.HasNode("RECIPE"))
            {
                foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
                {
                    ConfigNode mergedNode = KhemistryBatchISRURecipe.ApplyModuleOverrides(moduleNode, recipeNode);
                    recipes.Add(new KhemistryBatchISRURecipe(mergedNode, ConverterName));
                }
            }

            ///// Recipes: imported by name (RECIPE_NAMES & RECIPE_MULTIPLIERS) /////
            recipeMultiplier = KShared.GetFloatValueFromCFG(moduleNode, "recipeMultiplier", 1f);

            recipeType = KShared.GetStrValueFromCFG(moduleNode, "recipeType", null);
            recipeSubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubtype", null);
            recipeSubsubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubsubtype", null);

            _recipeNames.Clear();
            _recipeMultipliers.Clear();
            if (moduleNode.HasNode("RECIPE_NAMES"))
            {
                if (!moduleNode.GetNode("RECIPE_NAMES").HasValue("name"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": Node RECIPE_NAMES is present but has no \"name\" values, skipping.",
                        "KhemistryBatchISRU/LoadConfigFromPartInfo");
                else
                    _recipeNames.AddRange(moduleNode.GetNode("RECIPE_NAMES").GetValues("name"));

                if (moduleNode.HasNode("RECIPE_MULTIPLIERS"))
                {
                    foreach (string amt in moduleNode.GetNode("RECIPE_MULTIPLIERS").GetValues("amount"))
                        if (float.TryParse(amt, out float mTmp))
                            _recipeMultipliers.Add(mTmp);

                    if (_recipeMultipliers.Count != _recipeNames.Count)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": RECIPE_NAMES and RECIPE_MULTIPLIERS have unequal counts ("
                            + _recipeNames.Count + ", " + _recipeMultipliers.Count + ") — ignoring RECIPE_MULTIPLIERS.",
                            "KhemistryBatchISRU/LoadConfigFromPartInfo");
                        _recipeMultipliers.Clear();
                    }
                }
            }
            else if (moduleNode.HasNode("RECIPE_MULTIPLIERS"))
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Node RECIPE_MULTIPLIERS is present but no RECIPE_NAMES node is present.",
                    "KhemistryBatchISRU/LoadConfigFromPartInfo");

            if (shared != null)
            {
                if (_recipeNames.Count > 0)
                {
                    for (int i = 0; i < _recipeNames.Count; i++)
                    {
                        string wantedName = _recipeNames[i];
                        KhemistryBatchISRURecipe found = shared.batchRecipeList.FirstOrDefault(r => r._name == wantedName);
                        if (found == null)
                        {
                            KShared.LogError(
                                "Converter \"" + ConverterName + "\": RECIPE_NAMES entry \"" + wantedName
                                + "\" does not match any loaded KHEMISTRYBATCHISRU_RECIPE.",
                                "KhemistryBatchISRU/LoadConfigFromPartInfo");
                            continue;
                        }
                        // Global recipeMultiplier and the per-name RECIPE_MULTIPLIERS entry stack:
                        // global is applied first, then the local (per-name) multiplier.
                        float localMult = (i < _recipeMultipliers.Count) ? _recipeMultipliers[i] : 1f;
                        ConfigNode mergedFoundNode = KhemistryBatchISRURecipe.ApplyModuleOverrides(moduleNode, found.mainNode);
                        KhemistryBatchISRURecipe overriddenFound = new KhemistryBatchISRURecipe(mergedFoundNode, ConverterName);
                        recipes.Add(overriddenFound.ScaledCopy(recipeMultiplier * localMult));
                    }
                }
                if (recipeType != null || recipeSubtype != null || recipeSubsubtype != null)
                {
                    foreach (KhemistryBatchISRURecipe candidate in shared.batchRecipeList)
                    {
                        if (candidate.MatchesTypes(recipeType, recipeSubtype, recipeSubsubtype))
                        {
                            ConfigNode mergedCandidateNode = KhemistryBatchISRURecipe.ApplyModuleOverrides(moduleNode, candidate.mainNode);
                            KhemistryBatchISRURecipe overriddenCandidate = new KhemistryBatchISRURecipe(mergedCandidateNode, ConverterName);
                            
                            // Check if this wasn't already added by RECIPE_NAMES logic
                            foreach (KhemistryBatchISRURecipe recipe in recipes)
                                if (recipe._name == overriddenCandidate._name)
                                    continue;  // skip this candidate

                            recipes.Add(overriddenCandidate.ScaledCopy(recipeMultiplier));
                        }
                    }
                }
            }

            if (recipes.Count == 0)
            {
                _fatalConfigError = true;
                KShared.LogError("Converter \"" + ConverterName + "\": No recipes were loaded!",
                        "KhemistryBatchISRU/LoadSharedConfig");
                return;
            }

            if (bool.TryParse(KShared.GetStrValueFromCFG(moduleNode, "workersCrewSamePart", "false"), out bool wcspTmp))
                workersCrewSamePart = wcspTmp;
            _configMaxInteractionDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxInteractionDistance", _configMaxInteractionDistance);
            _configMaxDisplayDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxDisplayDistance", _configMaxDisplayDistance);
            _maxInteractionDistance = _configMaxInteractionDistance;
            _maxDisplayDistance = _configMaxDisplayDistance;

            ///// Select active recipe /////
            KhemistryBatchISRURecipe initial = null;
            if (!string.IsNullOrEmpty(activeRecipeName))
                initial = recipes.FirstOrDefault(r => r._name == activeRecipeName);
            if (initial == null) initial = recipes[0];
            ApplyRecipe(initial);
        }

        /// <summary>
        /// Makes the given recipe the active one: applies its own charging fields
        /// (falling back to module-level charging if the recipe doesn't define its own),
        /// resets batch progress, and updates control show-rules.
        /// </summary>
        protected void ApplyRecipe(KhemistryBatchISRURecipe recipe)
        {
            _activeRecipe = recipe;
            activeRecipeName = recipe._name;
            batchProgress = 0.0;

            _passiveTimers.Clear();
            _passiveConsumedThisBatch.Clear();
            for (int i = 0; i < recipe._passiveInputs.Count; i++)
            {
                _passiveTimers.Add(0.0);
                _passiveConsumedThisBatch.Add(0.0);
            }

            if (recipe._chargingRequired)
            {
                chargingRequired = true;
                chargeRate = recipe._chargeRate;
                chargeDecayRate = recipe._chargeDecay;
                _chargeNames.Clear();
                _chargeNames.AddRange(recipe._chargeNames);
                _chargeAmounts.Clear();
                _chargeAmounts.AddRange(recipe._chargeAmounts);
            }

            _controlsShowPAW = recipe._controlsShowPAW;
            _controlsShowEVA = recipe._controlsShowEVA;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Switch Recipe",
                  groupName = "khemistrybatchisru")]
        public void SwitchRecipe()
        {
            if (isRunning)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Stop the converter before switching recipes.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (recipes.Count <= 1)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": No other recipes available to switch to.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null) return;

            var labels = new List<string>();
            foreach (KhemistryBatchISRURecipe r in recipes)
                labels.Add(r._name + (r == _activeRecipe ? " [Active]" : ""));

            shared.ShowSelector("Switch Recipe", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx < 0) return;
                if (recipes[idx] == _activeRecipe) return;

                ApplyRecipe(recipes[idx]);
                UpdateEventVisibility();
                UpdateUI();

                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Switched to recipe \"" + _activeRecipe._name + "\".", 5f, ScreenMessageStyle.UPPER_CENTER));
                KShared.Log("Converter \"" + ConverterName + "\" switched active recipe to \"" + _activeRecipe._name + "\".",
                    "KhemistryBatchISRU/SwitchRecipe");
            });
        }

        ///// Main code /////

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistrybatchisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Requires maintenance by an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Log("Converter \"" + ConverterName + "\" maintained by " + kerbal.name + ".",
                "KhemistryAdvancedISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + ConverterName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                statusDisplay = "ERROR: see log";
                return;
            }

            Fields["statusDisplay"].guiUnfocusedRange = _maxDisplayDistance;
            Fields["chargeDisplay"].guiUnfocusedRange = _maxDisplayDistance;
            Fields["progressDisplay"].guiUnfocusedRange = _maxDisplayDistance;
            Fields["stateDisplay"].guiUnfocusedRange = _maxDisplayDistance;

            Events["StartConverter"].guiName = StartActionName;
            Events["StopConverter"].guiName = StopActionName;
            Actions["StartConverterAction"].guiName = StartActionName;
            Actions["StopConverterAction"].guiName = StopActionName;

            Events["StartConverter"].unfocusedRange = _configMaxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _configMaxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _configMaxInteractionDistance;
            Events["SwitchRecipe"].unfocusedRange = _configMaxInteractionDistance;

            if (!chargingRequired)
                this.state = ConverterState.On;

            _runtimeData = new KhemistryRuntimeData(vessel);  // vessel could be null

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        protected void UpdateEventVisibility()
        {
            ApplyShowRule(Events["StartConverter"],
                showPAW: !isRunning && !needsMaintenance && _controlsShowPAW,
                showEVA: !isRunning && !needsMaintenance && _controlsShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: isRunning && _controlsShowPAW,
                showEVA: isRunning && _controlsShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            ApplyShowRule(Events["SwitchRecipe"],
                showPAW: !isRunning && recipes.Count > 1 && _controlsShowPAW,
                showEVA: !isRunning && recipes.Count > 1 && _controlsShowEVA);
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = showPAW;
            ev.guiActiveUnfocused = showEVA;
            ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryBatchISRU/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        public void UpdateUI()
        {
            chargeDisplay = chargingRequired
                ? string.Format("{0:F1}%", chargePercent)
                : "N/A";

            if (state == ConverterState.On)
                stateDisplay = "Ready";
            else
                stateDisplay = state.ToString();

            Events["EnableCharging"].active = chargingRequired && state != ConverterState.Charging && state != ConverterState.On;
            Events["DisableCharging"].active = chargingRequired && state == ConverterState.Charging;
            Events["TurnOnConverter"].active = state != ConverterState.On;
            Events["TurnOffConverter"].active = state == ConverterState.On;
        }

        public void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == ConverterState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != ConverterState.Charging) return;

            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = ConverterState.On;
                KShared.Log("Converter fully charged, now ON.",
                    "KhemistryBatchISRU/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        ///// Batch cycle /////

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            _runtimeData.Update(vessel);

            double dt = TimeWarp.fixedDeltaTime;
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

            HandleCharging(dt);
            UpdateUI();
            TryTransferMaterialOutputBuffer();
            UpdateEventVisibility();

            if (needsMaintenance || !isRunning || state != ConverterState.On || _activeRecipe == null)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : (!isRunning ? "Stopped" : "Not ready");
                progressDisplay = "Off";
                SetActiveAnimationPlaying(false);
                return;
            }

            RunBatchCycle(dt);
            SetActiveAnimationPlaying(true);
        }

        /// <summary>
        /// Check the current BIOME_CONFIG to update values and explode if needed.
        /// </summary>
        /// <param name="biomeConfig">The <see cref="BatchISRUBiomeConfig"/> fetched outside the function.</param>
        /// <returns>If <see langword="true"/>, an explosion or error happened.</returns>
        protected bool CheckBiomeConfig(BatchISRUBiomeConfig biomeConfig)
        {
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryBatchISRU/CheckBiomeConfig");
                return true;
            }

            // One hundred and one ways to explode
            if (biomeConfig.situationDestructive.Contains(_runtimeData.sitCon) ||
                _runtimeData.alt < biomeConfig.minAltitude || _runtimeData.alt > biomeConfig.maxAltitude ||
                _runtimeData.g < biomeConfig.minG || _runtimeData.g > biomeConfig.maxG ||
                _runtimeData.temperature < biomeConfig.minTemperature || _runtimeData.temperature > biomeConfig.maxTemperature ||
                _runtimeData.pressure < biomeConfig.minPressure || _runtimeData.pressure > biomeConfig.maxPressure)
            {
                TriggerPowerfail(part, KhemistryBatchISRURecipe.PowerfailResult.Explode);
                return true;
            }

            // Apply multipliers
            _maxInteractionDistance = _configMaxInteractionDistance * (float)biomeConfig.maxInteractionDistanceMultiplier;
            _maxDisplayDistance = _configMaxDisplayDistance * (float)biomeConfig.maxDisplayDistanceMultiplier;

            return false;
        }

        /// <summary>
        /// Looks up the applicable biome config for the active recipe at the vessel's current
        /// location and, if found and operable, advances batch progress; consumes the full
        /// batch of inputs and produces the full batch of outputs once recipeTime is reached.
        /// </summary>
        protected void RunBatchCycle(double dt)
        {
            // Reflects pre-tick progress for every early-return branch below (converter is
            // "on" but may be paused this tick); recomputed again once progress actually advances.
            progressDisplay = FormatProgress(batchProgress, _activeRecipe._recipeTime);

            BatchISRUBiomeConfig biomeConfig = _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome);
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryBatchISRU/RunBatchCycle");
                return;
            }

            if (CheckBiomeConfig(biomeConfig))
                return;

            if (biomeConfig.situationOperating.Count > 0 && !biomeConfig.situationOperating.Contains(_runtimeData.sitCon))
            {
                statusDisplay = "Wrong situation (" + _runtimeData.sitCon + ")";
                return;
            }

            if (_runtimeData.alt < biomeConfig.minOperatingAltitude || _runtimeData.alt > biomeConfig.maxOperatingAltitude)
            {
                statusDisplay = "Out of operating altitude range";
                return;
            }

            if (_runtimeData.g < biomeConfig.minOperatingG || _runtimeData.g > biomeConfig.maxOperatingG)
            {
                statusDisplay = "Out of operating G range";
                return;
            }

            if (_runtimeData.temperature < biomeConfig.minOperatingTemperature || _runtimeData.temperature > biomeConfig.maxOperatingTemperature)
            {
                statusDisplay = "Out of operating temperature range";
                return;
            }

            if (_runtimeData.pressure < biomeConfig.minOperatingPressure || _runtimeData.pressure > biomeConfig.maxOperatingPressure)
            {
                statusDisplay = "Out of operating pressure range";
                return;
            }

            if (!CountWorkers(out uint engineers, out uint pilots, out uint scientists))
            {
                statusDisplay = "No workers nearby";
                return;
            }

            double reqEngineers = _activeRecipe._workersEngineers * biomeConfig.workersEngineersMultiplier;
            double reqPilots = _activeRecipe._workersPilots * biomeConfig.workersPilotsMultiplier;
            double reqScientists = _activeRecipe._workersScientists * biomeConfig.workersScientistsMultiplier;

            if (engineers < reqEngineers || pilots < reqPilots || scientists < reqScientists)
            {
                statusDisplay = "Insufficient workers";
                return;
            }

            if (!ProcessPassiveInputs(biomeConfig, dt))
            {
                progressDisplay = FormatProgress(batchProgress, _activeRecipe._recipeTime);
                return;
            }

            batchProgress += dt * biomeConfig.speedMul;

            double effectiveRecipeTime = _activeRecipe._recipeTime;
            progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);

            if (batchProgress < effectiveRecipeTime)
            {
                statusDisplay = string.Format("Running ({0:F0}%)", 100.0 * batchProgress / Math.Max(effectiveRecipeTime, 1e-6));
                return;
            }

            if (!TryRunBatch(biomeConfig))
            {
                statusDisplay = "Insufficient resources / no output space";
                return;
            }

            batchProgress -= effectiveRecipeTime;
            if (batchProgress < 0.0) batchProgress = 0.0;
            ClearPassiveConsumption();
            progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);
            statusDisplay = "Batch complete";
            PlayActiveAnimationOnce();
        }

        /// <summary>Formats batch progress as "0% (1.2 / 3.4 sec)", or "Off" if there's no valid recipeTime.</summary>
        protected static string FormatProgress(double progress, double recipeTime)
        {
            if (recipeTime <= 0.0) return "Off";
            double pct = 100.0 * progress / recipeTime;
            return string.Format("{0:F0}% ({1:F1} / {2:F1} sec)", pct, progress, recipeTime);
        }

        /// <summary>
        /// Processes every PINPUT_RESOURCE on the active recipe: consumes <c>amount</c> every
        /// <c>period</c> seconds, tracking the cumulative amount taken from each since the batch
        /// last completed/reset. On insufficient resource: if ignorePowerfail, silently skips
        /// consumption for that tick and the batch continues normally; otherwise applies the
        /// configured powefail result — PAUSE (default) just stalls this tick, STOP refunds
        /// everything consumed so far this batch and halts the converter, VOID/MAINT discard
        /// it instead (MAINT also requiring maintenance), and EXPLODE destroys the part with
        /// falling-off heat. Returns false if the current tick's batch progress should not be
        /// advanced (anything other than a successful tick).
        /// </summary>
        protected bool ProcessPassiveInputs(BatchISRUBiomeConfig biomeConfig, double dt)
        {
            if (_activeRecipe._passiveInputs.Count == 0) return true;

            for (int i = 0; i < _activeRecipe._passiveInputs.Count; i++)
            {
                KhemistryBatchISRURecipe.PassiveResourceInput pinp = _activeRecipe._passiveInputs[i];
                double timer = (i < _passiveTimers.Count) ? _passiveTimers[i] : 0.0;
                timer += dt;

                while (timer >= pinp.period)
                {
                    timer -= pinp.period;
                    double needed = pinp.amount * biomeConfig.inputMultiplier;
                    if (needed <= 0.0) continue;

                    double got = part.RequestResource(pinp.resourceName, needed, pinp.flowMode);

                    if (got < needed * 0.999)
                    {
                        // Passive consumption is all-or-nothing per tick — refund any partial draw.
                        if (got > 0.0) part.RequestResource(pinp.resourceName, -got, pinp.flowMode);

                        if (pinp.ignorePowerfail)
                            continue;  // "nothing happens" — resource just isn't consumed this tick

                        if (i < _passiveTimers.Count) _passiveTimers[i] = timer;

                        if (pinp.powerfail == KhemistryBatchISRURecipe.PowerfailResult.Pause)
                            statusDisplay = "Paused: out of " + pinp.resourceName;

                        TriggerPowerfail(part, pinp.powerfail, pinp.powerfailExplosionRadius, pinp.powerfailExplosionTemperature);
                        return false;
                    }

                    if (i < _passiveConsumedThisBatch.Count) _passiveConsumedThisBatch[i] += needed;
                }

                if (i < _passiveTimers.Count) _passiveTimers[i] = timer;
            }

            return true;
        }

        /// <summary>
        /// Attempts to consume a full batch of the active recipe's INPUT_RESOURCE amounts
        /// (all-or-nothing) and, if successful, produces the OUTPUT_RESOURCE amounts and
        /// buffers OUTPUT_RESOURCE_MATERIAL production for KhemistryMaterialStorage pickup.
        /// </summary>
        protected bool TryRunBatch(BatchISRUBiomeConfig biomeConfig)
        {
            var names = new List<string>();
            var amounts = new List<float>();
            foreach (var inp in _activeRecipe._inputs)
            {
                names.Add(inp.resourceName);
                amounts.Add((float)(inp.amount * biomeConfig.inputMultiplier));
            }

            if (!ConsumeVesselResources(names, amounts, 1.0)) return false;

            foreach (var outp in _activeRecipe._outputs)
            {
                double toAdd = outp.amount * biomeConfig.outputMultiplier;
                if (toAdd <= 0.0) continue;
                double got = part.RequestResource(outp.resourceName, -toAdd, ResourceFlowMode.STAGE_PRIORITY_FLOW);
                if (outp.dumpExcess && Math.Abs(got) < toAdd * 0.999 && _outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + ConverterName + "\": Not enough space for output \"" + outp.resourceName + "\", excess dumped.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 10.0;
                }
            }

            foreach (var mat in _activeRecipe._outputMaterials)
            {
                if (mat.amount <= 0.0) continue;
                if (!_materialOutputAmount.ContainsKey(mat)) _materialOutputAmount.Add(mat, 0.0);
                _materialOutputAmount[mat] += mat.amount;
            }

            return true;
        }

        /// <summary>
        /// Counts nearby engineer/pilot/scientist workers for the active recipe's workersType:
        /// CREW counts seated crew on this vessel (optionally restricted to this part if
        /// workersCrewSamePart is set), EVA counts a nearby EVA kerbal within maxInteractionDistance.
        /// </summary>
        protected bool CountWorkers(out uint engineers, out uint pilots, out uint scientists)
        {
            engineers = 0; pilots = 0; scientists = 0;
            bool anyWorkerTypeAllowed = _activeRecipe._workersEVA || _activeRecipe._workersCREW;
            if (!anyWorkerTypeAllowed) return true;  // No workers required at all

            if (_activeRecipe._workersCREW)
            {
                IEnumerable<ProtoCrewMember> crew = workersCrewSamePart
                    ? part.protoModuleCrew
                    : vessel.GetVesselCrew();

                foreach (ProtoCrewMember c in crew)
                {
                    if (c.trait == "Engineer") engineers++;
                    else if (c.trait == "Pilot") pilots++;
                    else if (c.trait == "Scientist") scientists++;
                }
            }

            if (_activeRecipe._workersEVA)
            {
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null || !v.isEVA || v.loaded == false) continue;
                    double dist = Vector3d.Distance(v.transform.position, part.transform.position);
                    if (dist > _maxInteractionDistance) continue;

                    foreach (ProtoCrewMember c in v.GetVesselCrew())
                    {
                        if (c.trait == "Engineer") engineers++;
                        else if (c.trait == "Pilot") pilots++;
                        else if (c.trait == "Scientist") scientists++;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to drain any buffered material output into a KhemistryMaterialStorage
        /// module on the vessel. Only whole units are ever moved.
        /// </summary>
        protected bool TryTransferMaterialOutputBuffer()
        {
            if (vessel == null || part == null) return false;
            if (_materialOutputAmount.Count == 0) return false;

            bool transferredAny = false;
            foreach (var matOutput in _materialOutputAmount.Keys.ToList())
            {
                double buffered = _materialOutputAmount[matOutput];
                double wholeUnits = Math.Floor(buffered);
                if (wholeUnits < 1.0) continue;

                KhemistryMaterial material = KShared.Instance?.materialList.FirstOrDefault(m => m.name == matOutput.name);
                if (material == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": OUTPUT_RESOURCE_MATERIAL \"" + matOutput.name
                        + "\" does not match any loaded KHEMISTRY_MATERIAL definition.",
                        "KhemistryBatchISRU/TryTransferMaterialOutputBuffer");
                    continue;
                }

                KhemistryMaterialInstance instance = new KhemistryMaterialInstance(
                    material, matOutput.shape, matOutput.size,
                    (float)(matOutput.outVolume * wholeUnits), matOutput.parameters);

                bool placed = false;
                foreach (Part vesselPart in vessel.parts)
                {
                    foreach (KhemistryMaterialStorage storageModule in vesselPart.Modules.OfType<KhemistryMaterialStorage>())
                    {
                        if (storageModule.AddMaterial(instance))
                        {
                            // Keep any fractional remainder instead of discarding it.
                            _materialOutputAmount[matOutput] = buffered - wholeUnits;
                            transferredAny = true;
                            placed = true;
                            break;
                        }
                    }
                    if (placed) break;
                }
            }

            return transferredAny;
        }
    }

    ////////////////////////////// Shared Data //////////////////////////////

    /// <summary>
    /// Runtime data used by <see cref="KhemistryBatchISRU"/>.
    /// This is checked by <see cref="BatchISRUBiomeConfig"/> to see if a recipe can run.
    /// </summary>
    public class KhemistryRuntimeData
    {
        // While vessel is null this tries to mimick Kerbin
        public double alt = 0;
        public double g = 0;
        public double temperature = 293.15;
        public double pressure = 104;
        public KShared.SituationCondition sitCon = new KShared.SituationCondition();
        public string planet = "Kerbin";
        public string biome = "Grasslands";

        public KhemistryRuntimeData(Vessel vessel)
        {
            // If vessel is null just don't update
            if(vessel != null)
                Update(vessel);
        }
        public void Update(Vessel vessel)
        {
            // If vessel is null just don't update
            if (vessel != null)
            {
                alt = vessel.altitude;  // meters
                g = vessel.geeForce;  // Gs
                temperature = vessel.externalTemperature;  // Kelvin
                pressure = vessel.staticPressurekPa;  // kPa
                sitCon = KShared.GetVesselSituation(vessel);
                planet = vessel.mainBody?.name ?? "";
                biome = ScienceUtil.GetExperimentBiome(vessel.mainBody, vessel.latitude, vessel.longitude);
            }
        }
    }

    /// <summary>
    /// A version of <see cref="KShared"/> that loads during the MainMenu scene.
    /// Mainly loads many top-level configs.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KSharedMainMenu : MonoBehaviour
    {
        private static KSharedMainMenu _instance;
        public static KSharedMainMenu Instance => _instance;

        public KShared kinst;

        public void Awake()
        {
            kinst = KShared.Instance;
            if (kinst == null)
            {
                Debug.Log("Khemistry (KSharedMainMenu/Awake): No KShared.Instance and Khemistry is about to have a bad time");
            }

            if (_instance != null)
            {
                KShared.LogError("Another instance of KSharedMainMenu was found, self destructing...", "KSharedMainMenu/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Celestial body list
            kinst.celestialBodies = FlightGlobals.Bodies.Select(b => b.bodyName).ToList();

            // Resource deposits
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RESOURCE_DEPOSIT"))
            {
                if (!node.HasValue("resource"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT does not define a resource it contains and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("type"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a type and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("body"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not define a body to be placed on and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (node.GetValue("type") == "surface" && !node.HasValue("resource2"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" is a surface type deposit without a resource2 value. It was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }

                if (node.GetValue("type") != "underground" && node.GetValue("render") == "true")
                {
                    KShared.LogWarning("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" attempts to render but that is not implemented yet.", "KSharedMainMenu/Awake");
                }

                int maxAmount = KShared.GetIntValueFromCFG(node, "maxAmount", 10) + 1;
                int minAmount = KShared.GetIntValueFromCFG(node, "minAmount", 5);
                int maxRadius = KShared.GetIntValueFromCFG(node, "maxRadius", 20) + 1;
                int minRadius = KShared.GetIntValueFromCFG(node, "minRadius", 10);
                string body = node.GetValue("body");
                string resource = node.GetValue("resource");
                string biome = KShared.GetStrValueFromCFG(node, "biome", null);
                float depthUnderground = KShared.GetFloatValueFromCFG(node, "depthUnderground", 50);

                if (node.GetValue("type") == "surface")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, node.GetValue("resource2"), KShared.GetFloatValueFromCFG(node, "depthUndergroundStart", 100)));
                }
                else if (node.GetValue("type") == "surfaceOnly")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, null, 0));
                }
                else if (node.GetValue("type") == "underground")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.undergroundDeposits.Add(new KhemistryUDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthUndergroundStart", 100), depthUnderground, resource, minRadius, maxRadius));
                }
                else
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a valid type and was not loaded. The type was \"" + node.GetValue("type") + "\".", "KSharedMainMenu/Awake");
                }
            }
            KShared.Log("Created " + kinst.undergroundDeposits.Count().ToString() + " underground deposits.", "KSharedMainMenu/Awake");
            KShared.Log("Created " + kinst.surfaceDeposits.Count().ToString() + " surface deposits.", "KSharedMainMenu/Awake");

            // KhemistryRecipeISRU recipes
            ConfigNode[] nodes2 = GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RECIPE");
            foreach (ConfigNode node in nodes2)
            {
                if (!node.HasValue("recipeType"))
                {
                    KShared.LogError("A KHEMISTRY_RECIPE has no recipeType!", "KSharedMainMenu/Awake");
                    continue;
                }
                string recipeT = node.GetValue("recipeType");
                if (!kinst.recipeDict.ContainsKey(recipeT))
                    kinst.recipeDict.Add(recipeT, new List<KhemistryRecipe>());

                kinst.recipeDict[recipeT].Add(new KhemistryRecipe(node));
            }

            // KhemistryRecipeISRU recipe counts
            KShared.Log("Created " + kinst.recipeDict.Keys.Count().ToString() + " recipe types.", "KSharedMainMenu/Awake");
            foreach (string recipeType in kinst.recipeDict.Keys)
            {
                KShared.Log("Created " + kinst.recipeDict[recipeType].Count().ToString() + " recipes for recipe type " + recipeType, "KSharedMainMenu/Awake");
            }

            // KhemistryBatchISRU recipes
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRYBATCHISRU_RECIPE"))
            {
                if (!node.HasValue("name"))
                {
                    KShared.LogError("A KHEMISTRYBATCHISRU_RECIPE has no name!", "KSharedMainMenu/Awake");
                    continue;
                }
                kinst.batchRecipeList.Add(new KhemistryBatchISRURecipe(node, node.GetValue("name")));
            }
            KShared.Log("Created " + kinst.batchRecipeList.Count.ToString() + " KhemistryBatchISRU recipes.", "KSharedMainMenu/Awake");

            // Material definitions
            int materialCount = 0;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_MATERIAL"))
            {
                KhemistryMaterial tmp = new KhemistryMaterial(node);
                if (tmp != null)
                {
                    kinst.materialList.Add(tmp);
                    materialCount++;
                }
            }
            KShared.Log("Created " + materialCount.ToString() + " material definitions.", "KSharedMainMenu/Awake");
        }
    }

    /// <summary>
    /// The shared data for many Khemistry classes.
    /// Contains various methods and variables, used for GUI and as helpers. Handles all logging.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KShared : MonoBehaviour
    {
        private static KShared _instance;
        public static KShared Instance => _instance;

        private bool _selectorVisible = false;
        private Vector2 _selectorScroll = Vector2.zero;
        private string _selectorTitle = "";
        private List<string> _selectorOptions;
        private Action<string> _selectorCallback;
        private Rect _windowRect = new Rect(0, 0, 320, 300);
        private int _windowId;

        private bool _amountVisible = false;
        private string _amountTitle = "";
        private float _amountValue = 0f;
        private float _amountMin = 0f;
        private float _amountMax = 1f;
        private Action<float> _amountCallback;
        private Rect _amountRect = new Rect(0, 0, 320, 130);
        private int _amountWindowId;

        public List<KhemistryUDeposit> undergroundDeposits = new List<KhemistryUDeposit>();
        public List<KhemistryGDeposit> surfaceDeposits = new List<KhemistryGDeposit>();

        public Dictionary<string, List<KhemistryRecipe>> recipeDict = new Dictionary<string, List<KhemistryRecipe>>();

        public List<KhemistryBatchISRURecipe> batchRecipeList = new List<KhemistryBatchISRURecipe>();

        public List<KhemistryMaterial> materialList = new List<KhemistryMaterial>();

        public System.Random rand = new System.Random();
        public List<string> celestialBodies = new List<string>();

        /// <summary>
        /// Finds and returns the MODULE config node for this converter from partConfig.
        /// Matches on both module class name and ConverterName to support multiple
        /// converters per part. Pass the expected module name (e.g. "KhemistryAdvancedISRU"
        /// or "KhemistryEVAAdvancedISRU").
        /// </summary>
        public static ConfigNode FindModuleConfigNode(Part part, string ConverterName, string moduleName)
        {
            ConfigNode result = null;

            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != moduleName) continue;
                    if (n.GetValue("ConverterName") == ConverterName) { result = n; break; }
                }
            }

            if (result != null) return result;

            string targetPartName = part.partInfo?.name ?? part.name;
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string nodeName = partNode.GetValue("name") ?? "";
                int slash = nodeName.LastIndexOf('/');
                if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != moduleName) continue;
                    if (n.GetValue("ConverterName") == ConverterName) { result = n; break; }
                }
                if (result != null) break;
            }

            if (result == null)
                KShared.LogError(
                    "Could not find MODULE " + moduleName + " with ConverterName=\"" + ConverterName
                    + "\" in partConfig or GameDatabase!",
                    moduleName + "/FindModuleConfigNode");

            return result;
        }

        /// <summary>
        /// Returns a biome name using a latitude-longitude position on a CelestialBody.
        /// This function will cause a NullReferenceException if the planet does not exist.
        /// </summary>
        /// <param name="planet">The planet used to find the biome.</param>
        /// <param name="pos">The latitude-longitude Vector2 position to find the biome at.</param>
        /// <returns>The name of the biome.</returns>
        public static string GetBiomeNameFromLatLon(string planet, Vector2 pos) => FlightGlobals.GetBodyByName(planet).BiomeMap.GetAtt(pos[0] * Mathf.Deg2Rad, pos[1] * Mathf.Deg2Rad).name;

        /// <summary>
        /// Gets a list of biome names for a planet.
        /// Will return an empty list if the planet does not exist or has no biomes.
        /// </summary>
        /// <param name="planet">The internal name of the planet to fetch biomes for.</param>
        /// <returns>List of biomes for the planet.</returns>
        public static List<string> GetBiomeNames(string planet)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(planet);
            if (body == null || body.BiomeMap == null)
                return new List<string>();

            return body.BiomeMap.Attributes
                .Where(b => b != null)
                .Select(b => b.name)
                .ToList();
        }

        public static int GetIntValueFromCFG(ConfigNode node, string value, int defaultValue)
        {
            if (node.HasValue(value))
                if (int.TryParse(node.GetValue(value), out int tmp))
                    return tmp;
            return defaultValue;
        }
        public static float GetFloatValueFromCFG(ConfigNode node, string value, float defaultValue)
        {
            if (node.HasValue(value))
                if (float.TryParse(node.GetValue(value), out float tmp))
                    return tmp;
            return defaultValue;
        }
        public static double GetDoubleValueFromCFG(ConfigNode node, string value, double defaultValue)
        {
            if (node.HasValue(value))
                if (double.TryParse(node.GetValue(value), out double tmp))
                    return tmp;
            return defaultValue;
        }
        /// <summary>
        /// Gets a temperature value from a config node.
        /// The value has to end in K, C, or F. If no unit is specified, K is assumed.
        /// The default value must be in Kelvin.
        /// </summary>
        /// <param name="node">The config node to get the value from.</param>
        /// <param name="value">The value inside the config node.</param>
        /// <param name="defaultValue">Default to this Kelvin temperature.</param>
        /// <returns>Returns a Kelvin temperature as a double.</returns>
        public static double GetDoubleTemperatureValueFromCFG(ConfigNode node, string value, double defaultValue)
        {
            if (node.HasValue(value))
            {
                string val = node.GetValue(value);
                if (val.EndsWith("K"))
                    if (double.TryParse(val.Substring(0, val.Length - 1), out double tmp))
                        return tmp;
                if (val.EndsWith("C"))
                    if (double.TryParse(val.Substring(0, val.Length - 1), out double tmp))
                        return tmp + 273.15;
                if (val.EndsWith("F"))
                    if (double.TryParse(val.Substring(0, val.Length - 1), out double tmp))
                        return DoubleFarenheitToCelsius(tmp) + 273.15;
            }
            return defaultValue;
        }
        public static string GetStrValueFromCFG(ConfigNode node, string value, string defaultValue) => node.HasValue(value) ? node.GetValue(value) : defaultValue;

        public static double DoubleFarenheitToCelsius(double f) => (f - 32) * (5 / 9);
        public static float FloatFarenheitToCelsius(float f) => (f - 32) * (5 / 9);

        public static double LatLonDistanceMeters(
            double lat1Deg,
            double lon1Deg,
            double lat2Deg,
            double lon2Deg,
            string body)
        {
            double lat1 = DegreesToRadians(lat1Deg);
            double lon1 = DegreesToRadians(lon1Deg);
            double lat2 = DegreesToRadians(lat2Deg);
            double lon2 = DegreesToRadians(lon2Deg);

            double dLat = lat2 - lat1;
            double dLon = lon2 - lon1;

            double a =
                Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Pow(Math.Sin(dLon / 2), 2);

            double c = 2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

            return FlightGlobals.GetBodyByName(body).Radius * c;
        }

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        public List<string> SurfaceDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryGDeposit deposit in surfaceDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
        }
        public List<string> UndergroundDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
        }

        public static void ParseShowRule(string raw, out bool showPAW, out bool showEVA,
            string fieldName, string moduleName = null)
        {
            string val = raw.Trim().Trim('"').ToUpper();
            switch (val)
            {
                case "PAW":
                    showPAW = true; showEVA = false; break;
                case "EVA":
                    showPAW = false; showEVA = true; break;
                case "EVA+PAW":
                case "PAW+EVA":
                    showPAW = true; showEVA = true; break;
                default:
                    if (moduleName == null)
                        KShared.LogError("Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    else
                        KShared.LogError("Converter \"" + moduleName + "\": Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    showPAW = true; showEVA = false; break;
            }
        }

        public static Dictionary<string, string> NodeToDictionary(ConfigNode node)
        {
            var dict = new Dictionary<string, string>();

            foreach (ConfigNode.Value value in node.values)
            {
                dict[value.name] = value.value;
            }

            return dict;
        }

        /// <summary>
        /// Converts a list into a readable string.
        /// Example: ["a", "b", "c"] becomes "a, b, c".
        /// </summary>
        /// <param name="list">The List<string> to convert into a string.</param>
        /// <returns>The list as a readable string.</returns>
        public static string ListToString(List<string> list) => string.Join(", ", list);

        /// <summary>
        /// Converts a dictionary into a readable string.
        /// Example: {a: 1, b: 2, c: 3} becomes "a: 1, b: 2, c: 3".
        /// </summary>
        /// <param name="dict">The Dictionary<string, string> to convert into a string.</param>
        /// <returns>The dictionary as a readable string.</returns>
        public static string DictToString(Dictionary<string, string> dict)
        {
            List<string> tmp = new List<string>();
            foreach (string key in dict.Keys)
                tmp.Add(key + " = " + dict[key]);
            return ListToString(tmp);
        }

        public enum SituationCondition
        {
            Any, Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital
        }

        /// <summary>Maps a vessel's current KSP Vessel.Situations into a SituationCondition value.</summary>
        public static SituationCondition GetVesselSituation(Vessel v)
        {
            Vessel.Situations sit = v.situation;
            CelestialBody body = v.mainBody;
            double alt = v.altitude;

            switch (sit)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.PRELAUNCH:
                    return SituationCondition.Landed;
                case Vessel.Situations.SPLASHED:
                    return SituationCondition.Splashed;
                case Vessel.Situations.FLYING:
                    return (body != null && alt >= body.scienceValues.flyingAltitudeThreshold)
                        ? SituationCondition.FlyingHigh : SituationCondition.FlyingLow;
                case Vessel.Situations.SUB_ORBITAL:
                    return SituationCondition.SubOrbital;
                case Vessel.Situations.ORBITING:
                case Vessel.Situations.ESCAPING:
                    return (body != null && alt >= body.scienceValues.spaceAltitudeThreshold)
                        ? SituationCondition.SpaceHigh : SituationCondition.SpaceLow;
                default:
                    return SituationCondition.Any;
            }
        }

        public void Awake()
        {
            if (_instance != null)
            {
                KShared.LogError("Another instance of KShared was found, self destructing...", "KShared/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _windowId = GUIUtility.GetControlID(FocusType.Passive);
            _amountWindowId = GUIUtility.GetControlID(FocusType.Passive);
            KShared.Log("KShared initialized.", "KShared/Awake");
        }

        public void ShowSelector(string title, List<string> options, Action<string> onSelect)
        {
            _selectorTitle = title;
            _selectorOptions = options;
            _selectorCallback = onSelect;
            _selectorScroll = Vector2.zero;
            _windowRect = new Rect(
                (Screen.width - _windowRect.width) / 2f,
                (Screen.height - _windowRect.height) / 2f,
                _windowRect.width,
                _windowRect.height
            );
            _selectorVisible = true;
        }

        private void OnGUI()
        {
            if (_selectorVisible)
                _windowRect = GUILayout.Window(
                    _windowId,
                    _windowRect,
                    DrawSelectorWindow,
                    _selectorTitle,
                    HighLogic.Skin.window);

            if (_amountVisible)
                _amountRect = GUILayout.Window(
                    _amountWindowId,
                    _amountRect,
                    DrawAmountWindow,
                    _amountTitle,
                    HighLogic.Skin.window);
        }

        public void ShowAmountSelector(string title, float min, float max, float initial, Action<float> onConfirm)
        {
            _amountTitle = title;
            _amountMin = min;
            _amountMax = max;
            _amountValue = Mathf.Clamp(initial, min, max);
            _amountCallback = onConfirm;
            _amountRect = new Rect(
                (Screen.width - _amountRect.width) / 2f,
                (Screen.height - _amountRect.height) / 2f,
                _amountRect.width, _amountRect.height);
            _amountVisible = true;
        }

        private void DrawAmountWindow(int windowId)
        {
            GUILayout.Label(
                string.Format("{0:F3}  /  {1:F3}", _amountValue, _amountMax),
                HighLogic.Skin.label);
            _amountValue = GUILayout.HorizontalSlider(
                _amountValue, _amountMin, _amountMax,
                HighLogic.Skin.horizontalSlider,
                HighLogic.Skin.horizontalSliderThumb);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm", HighLogic.Skin.button))
            {
                _amountVisible = false;
                _amountCallback(_amountValue);
            }
            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
                _amountVisible = false;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private void DrawSelectorWindow(int windowId)
        {
            _selectorScroll = GUILayout.BeginScrollView(
                _selectorScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(220f)
            );
            foreach (string option in _selectorOptions)
            {
                if (GUILayout.Button(option, HighLogic.Skin.button))
                {
                    _selectorVisible = false;
                    _selectorCallback(option);
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
                _selectorVisible = false;

            GUI.DragWindow();
        }

        /// <summary>
        /// Writes a log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="func">Optional function name the log message came from.</param>
        public static void Log(string message, string func = null)
        {
            if (func != null)
                Debug.Log("Khemistry (" + func + "): " + message);
            else
                Debug.Log("Khemistry: " + message);
        }

        /// <summary>
        /// Writes an error log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The error message to send.</param>
        /// <param name="func">Optional function name the error log message came from.</param>
        public static void LogError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("Khemistry (" + func + "): " + message);
            else
                Debug.LogError("Khemistry: " + message);
        }

        /// <summary>
        /// Writes a warning log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The warning message to send.</param>
        /// <param name="func">Optional function name the warning log message came from.</param>
        public static void LogWarning(string message, string func = null)
        {
            if (func != null)
                Debug.LogWarning("Khemistry (" + func + "): " + message);
            else
                Debug.LogWarning("Khemistry: " + message);
        }

        /// <summary>
        /// Destroys the given part and applies falling-off thermal damage to every other
        /// loaded part within radiusMeters of it: centerTempCelsius (converted to Kelvin) is
        /// applied at distance 0, linearly fading to no effect at radiusMeters. Any part whose
        /// resulting temperature reaches its max temperature (or skin max temperature) explodes
        /// as a consequence, same as it would from any other overheat in KSP.
        /// </summary>
        public static void TriggerExplosionWithHeat(Part contextPart, float radiusMeters, float centerTempCelsius)
        {
            if (contextPart == null) return;

            if (radiusMeters <= 0f)
            {
                contextPart.explode();
                return;
            }

            double centerTempKelvin = centerTempCelsius + 273.15;
            Vector3 center = contextPart.transform.position;

            var nearbyParts = new List<Part>();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || !v.loaded) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null || p == contextPart) continue;
                    if (Vector3.Distance(p.transform.position, center) <= radiusMeters)
                        nearbyParts.Add(p);
                }
            }

            contextPart.explode();

            foreach (Part p in nearbyParts)
            {
                if (p == null) continue;

                float dist = Vector3.Distance(p.transform.position, center);
                double falloff = Math.Max(0.0, 1.0 - (dist / radiusMeters));
                if (falloff <= 0.0) continue;

                double appliedTempKelvin = p.temperature + (centerTempKelvin - p.temperature) * falloff;
                if (appliedTempKelvin > p.temperature) p.temperature = appliedTempKelvin;
                if (appliedTempKelvin > p.skinTemperature) p.skinTemperature = appliedTempKelvin;

                if (p.temperature >= p.maxTemp || p.skinTemperature >= p.skinMaxTemp)
                    p.explode();
            }
        }

        public static void LogNoValueInNode(string node, string value, string beginning, string source)
        {
            KShared.LogError(beginning + " failed to load because node \"" + node + "\" did not have a \"" + value + "\" value!", source);
        }
        public static void LogNoNode(string node, string beginning, string source)
        {
            KShared.LogError(beginning + " failed to load because node \"" + node + "\" was not found!", source);
        }
    }

    ////////////////////////////// Fluid Cell System //////////////////////////////

    /// <summary>
    /// A part that can hold some resources and be carried by a kerbal to transfer resources between vessels.
    /// </summary>
    public class KhemistryFluidCell : PartModule
    {
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        public HashSet<string> AllowedResources = new HashSet<string>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            AllowedResources.Clear();
            if (node.HasNode("ALLOWED_RESOURCES"))
            {
                foreach (string name in node.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                    AllowedResources.Add(name.Trim());
                KShared.Log(
                    "Loaded " + AllowedResources.Count + " allowed resources.",
                    "KhemistryFluidCell/OnLoad");
            }
            else
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryFluidCell but no ALLOWED_RESOURCES node. This part is now capable of storing anything.",
                    "KhemistryFluidCell/OnLoad");
            }
        }

        public override void OnUpdate()
        {
            ContentsDisplay = string.IsNullOrEmpty(ResourceName)
                ? "Empty"
                : string.Format("{0}: {1:F2} / {2:F2} kg", ResourceName, ResourceAmount, ResourceMaxAmount);
        }
    }

    ////////////////////////////// Obsolete Degrading Battery System //////////////////////////////

    /// <summary>
    /// An obsolete <see cref="PartModule"/> that simulates a battery that degrades over time.
    /// Should be ported over to the <see cref="KhemistryFluidCell"/> and <see cref="KhemistryAdvancedStorage"/> modules.
    /// </summary>
    public class KhemistryDegradingBattery : PartModule
    {
        [KSPField(isPersistant = false)]
        public string ResourceName = "ElectricCharge";

        [KSPField(isPersistant = false)]
        public double DegradeTime = -1.0;

        [KSPField(isPersistant = true)]
        public double OriginalMaxAmount = -1.0;

        [KSPField(isPersistant = true)]
        public double StartTime = -1.0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
        public string HealthDisplay = "Battery Life: 100%";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Time Remaining",
         groupName = "batterydeg")]
        public string HealthTimeDisplay = "Time until 0% battery life: Battery cannot degrade.";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryDegradingBattery but no resource node for " + ResourceName,
                    "KhemistryDegradingBattery/OnStart");
                return;
            }

            if (OriginalMaxAmount < 0) OriginalMaxAmount = resource.maxAmount;
            if (StartTime < 0) StartTime = Planetarium.GetUniversalTime();

            ApplyDegradation(resource);
        }

        public override void OnUpdate()
        {
            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null) return;
            ApplyDegradation(resource);
        }

        private void ApplyDegradation(PartResource resource)
        {
            if (DegradeTime <= 0) return;

            double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
            double degradeSeconds = DegradeTime * 60.0;
            double fraction = Math.Max(0.0, 1.0 - (elapsedSeconds / degradeSeconds));
            double newMax = OriginalMaxAmount * fraction;

            resource.maxAmount = newMax;
            if (resource.amount > resource.maxAmount)
                resource.amount = resource.maxAmount;

            HealthDisplay = string.Format("Battery Life: {0:F1}%", fraction * 100.0);
            double remaining = Math.Max(0, degradeSeconds - elapsedSeconds);
            HealthTimeDisplay = string.Format("Time until 0%: {0:F0}s", remaining);
        }
    }

    ////////////////////////////// Resource/Recipe Library //////////////////////////////

    /// <summary>
    /// Information about a resource shown in the Resource Library.
    /// </summary>
    public class KhemistryResourceInfo
    {
        public string name;
        public string displayName;
        public string abbreviation;
        public float unitCost;
        public float density;
        public float volume;
        public string flowMode;
        public string transfer;
        public bool isTweakable;
        public bool isVisible;
        public string description;
    }

    /// <summary>
    /// Information about an input/output shown in the Recipe Library.
    /// </summary>
    public class KhemistryRecipeIO
    {
        public string resourceName;
        public double ratio;
    }

    /// <summary>
    /// Information about a recipe shown in the Recipe Library.
    /// </summary>
    public class KhemistryRecipeInfo
    {
        public string converterName;
        public bool generatesHeat;
        public string partTitle;
        public List<KhemistryRecipeIO> inputs = new List<KhemistryRecipeIO>();
        public List<KhemistryRecipeIO> outputs = new List<KhemistryRecipeIO>();
    }

    /// <summary>
    /// Loads the data for the Resource and Recipe Library from the <see cref="GameDatabase"/>.
    /// The Resource Library is unusable until this finishes loading.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryLoader : MonoBehaviour
    {
        public static List<KhemistryResourceInfo> Resources { get; private set; }
        public static List<KhemistryRecipeInfo> Recipes { get; private set; }
        public static bool IsLoaded { get; private set; } = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);
            LoadData();
        }

        private void LoadData()
        {
            KShared.Log("Loading resource and recipe library...", "KhemistryLibraryLoader/LoadData");

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string resName = node.GetValue("name");
                string desc = node.GetValue("khemistryDescription");
                if (!string.IsNullOrEmpty(resName) && !string.IsNullOrEmpty(desc))
                    descriptions[resName] = desc;
            }

            Resources = new List<KhemistryResourceInfo>();
            foreach (PartResourceDefinition def in PartResourceLibrary.Instance.resourceDefinitions)
            {
                descriptions.TryGetValue(def.name, out string description);
                Resources.Add(new KhemistryResourceInfo
                {
                    name = def.name,
                    displayName = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName,
                    abbreviation = def.abbreviation,
                    unitCost = def.unitCost,
                    density = def.density,
                    volume = def.volume,
                    flowMode = def.resourceFlowMode.ToString(),
                    transfer = def.resourceTransferMode.ToString(),
                    isTweakable = def.isTweakable,
                    isVisible = def.isVisible,
                    description = description
                });
            }
            Resources.Sort((a, b) =>
                string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

            Recipes = new List<KhemistryRecipeInfo>();
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string partTitle = partNode.GetValue("title") ?? partNode.GetValue("name") ?? "Unknown Part";
                foreach (ConfigNode moduleNode in partNode.GetNodes("MODULE"))
                {
                    if (moduleNode.GetValue("name") != "ModuleResourceConverter") continue;

                    var recipe = new KhemistryRecipeInfo
                    {
                        converterName = moduleNode.GetValue("ConverterName") ?? "Unnamed Converter",
                        generatesHeat = string.Equals(moduleNode.GetValue("GeneratesHeat"), "true",
                                            StringComparison.OrdinalIgnoreCase),
                        partTitle = partTitle
                    };

                    foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
                    {
                        string resName = inputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        double.TryParse(inputNode.GetValue("Ratio"), out double ratio);
                        recipe.inputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }
                    foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
                    {
                        string resName = outputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                        recipe.outputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }

                    Recipes.Add(recipe);
                }
            }

            IsLoaded = true;
            KShared.Log(
                string.Format("Library loaded: {0} resources, {1} recipes.", Resources.Count, Recipes.Count),
                "KhemistryLibraryLoader/LoadData");
        }
    }

    /// <summary>
    /// The GUI for the Resource Library.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryGUI : MonoBehaviour
    {
        private const int MainWindowId = 856201;
        private const int DetailWindowId = 856202;
        private const int RecipeWindowId = 856203;

        private bool _mainVisible = false;
        private bool _detailVisible = false;
        private bool _recipeVisible = false;

        private Rect _mainRect;
        private Rect _detailRect;
        private Rect _recipeRect;

        private string _searchText = "";
        private Vector2 _mainScroll = Vector2.zero;

        private KhemistryResourceInfo _selectedResource;
        private Vector2 _detailScroll = Vector2.zero;

        private List<KhemistryRecipeInfo> _filteredRecipes;
        private string _recipeTitle = "";
        private Vector2 _recipeScroll = Vector2.zero;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _buttonTexture;

        private GUIStyle _wrapLabel;
        private GUIStyle _centeredLabel;
        private GUIStyle _boldLabel;
        private bool _stylesReady = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);

            float sw = Screen.width;
            float sh = Screen.height;
            float detailW = sw / 3f;
            _mainRect = new Rect(sw * 0.05f, sh * 0.1f, 700f, 500f);
            _detailRect = new Rect(sw * 0.63f, sh * 0.1f, detailW, 560f);
            _recipeRect = new Rect(sw * 0.05f, sh * 0.1f, 900f, 500f);

            _buttonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color icon = new Color(0.25f, 0.60f, 0.90f, 1f);
            Color[] pixels = new Color[38 * 38];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = icon;
            _buttonTexture.SetPixels(pixels);
            _buttonTexture.Apply();

            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;
            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                () => _mainVisible = true,
                () => _mainVisible = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                _buttonTexture
            );
        }

        private void OnLauncherDestroyed() { _toolbarButton = null; }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _wrapLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
            _centeredLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _boldLabel = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_mainVisible)
                _mainRect = GUILayout.Window(MainWindowId, _mainRect, DrawMainWindow, "Khemistry Resource Library", HighLogic.Skin.window);
            if (_detailVisible && _selectedResource != null)
                _detailRect = GUILayout.Window(DetailWindowId, _detailRect, DrawDetailWindow, "", HighLogic.Skin.window);
            if (_recipeVisible && _filteredRecipes != null)
                _recipeRect = GUILayout.Window(RecipeWindowId, _recipeRect, DrawRecipeWindow, _recipeTitle, HighLogic.Skin.window);
        }

        private void DrawMainWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _mainVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", HighLogic.Skin.label, GUILayout.Width(55));
            _searchText = GUILayout.TextField(_searchText, HighLogic.Skin.textField);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(230));
            GUILayout.Label("Abbreviation", _boldLabel, GUILayout.Width(120));
            GUILayout.Label("Cost per KG", _boldLabel, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _mainScroll = GUILayout.BeginScrollView(_mainScroll, HighLogic.Skin.scrollView);

            if (!KhemistryLibraryLoader.IsLoaded)
            {
                GUILayout.Label("Resources and recipes are still loading.", _wrapLabel);
            }
            else
            {
                string filter = _searchText.Trim().ToLower();
                foreach (KhemistryResourceInfo res in KhemistryLibraryLoader.Resources)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        !res.displayName.ToLower().Contains(filter) &&
                        !res.name.ToLower().Contains(filter))
                        continue;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(res.displayName, HighLogic.Skin.button, GUILayout.Width(230)))
                        OpenDetailWindow(res);
                    GUILayout.Label(res.abbreviation ?? "-", HighLogic.Skin.label, GUILayout.Width(120));
                    GUILayout.Label(res.unitCost.ToString("F2"), HighLogic.Skin.label, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void OpenDetailWindow(KhemistryResourceInfo res)
        {
            _selectedResource = res;
            _detailVisible = true;
            _detailScroll = Vector2.zero;
        }

        private void DrawDetailWindow(int id)
        {
            KhemistryResourceInfo res = _selectedResource;
            float labelW = Screen.width / 3f - 60f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(res.displayName, _boldLabel, GUILayout.Width(labelW - 35f));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
            {
                _detailVisible = false;
                _recipeVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string desc = string.IsNullOrEmpty(res.description) ? "No description available." : res.description;
            GUILayout.Label(desc, _centeredLabel, GUILayout.Width(labelW));

            GUILayout.Space(8);

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, HighLogic.Skin.scrollView);

            DrawRow("Internal Name", res.name);
            DrawRow("Abbreviation", res.abbreviation ?? "-");
            DrawRow("Cost per KG", res.unitCost.ToString("F4"));
            DrawRow("Can be adjusted in VAB?", res.isTweakable ? "Yes" : "No");
            DrawRow("Hidden resource?", res.isVisible ? "No" : "Yes");
            DrawRow("Flow mode", res.flowMode ?? "-");
            DrawRow("Transfer method", res.transfer ?? "-");

            GUILayout.Space(6);

            string densityLine;
            if (Approx(res.density, 0.001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 kilogram";
            else if (Approx(res.density, 1f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 ton";
            else if (Approx(res.density, 0.000001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 gram";
            else densityLine = string.Format(
                    "This resource has special density and volume parameters. " +
                    "Every unit of this resource weighs {0:F6} kilograms and each internal " +
                    "volume unit is filled by {1} of this resource.",
                    res.density * 1000.0, res.volume);

            GUILayout.Label(densityLine, _wrapLabel, GUILayout.Width(labelW));
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            if (GUILayout.Button("Recipes that use this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: true);
            if (GUILayout.Button("Recipes that produce this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: false);

            GUI.DragWindow();
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _boldLabel, GUILayout.Width(180));
            GUILayout.Label(value, _wrapLabel);
            GUILayout.EndHorizontal();
        }

        private static bool Approx(float a, float b)
            => Math.Abs(a - b) < Math.Abs(b) * 0.01f + 1e-9f;

        private void OpenRecipeWindow(string resourceName, bool isInput)
        {
            _filteredRecipes = KhemistryLibraryLoader.Recipes.Where(r =>
                isInput
                    ? r.inputs.Any(i => i.resourceName == resourceName)
                    : r.outputs.Any(o => o.resourceName == resourceName)
            ).ToList();

            _recipeTitle = isInput
                ? "Recipes that use " + resourceName
                : "Recipes that produce " + resourceName;
            _recipeScroll = Vector2.zero;
            _recipeVisible = true;
        }

        private void DrawRecipeWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _recipeVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(200));
            GUILayout.Label("Produces heat?", _boldLabel, GUILayout.Width(100));
            GUILayout.Label("Inputs", _boldLabel, GUILayout.Width(270));
            GUILayout.Label("Outputs", _boldLabel, GUILayout.Width(270));
            GUILayout.EndHorizontal();

            _recipeScroll = GUILayout.BeginScrollView(_recipeScroll, HighLogic.Skin.scrollView);

            if (_filteredRecipes == null || _filteredRecipes.Count == 0)
            {
                GUILayout.Label("No recipes found.", _wrapLabel);
            }
            else
            {
                foreach (KhemistryRecipeInfo recipe in _filteredRecipes)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical(GUILayout.Width(200));
                    GUILayout.Label(recipe.converterName, _boldLabel);
                    GUILayout.Label("(" + recipe.partTitle + ")", _wrapLabel);
                    GUILayout.EndVertical();

                    GUILayout.Label(recipe.generatesHeat ? "Yes" : "No", HighLogic.Skin.label, GUILayout.Width(100));

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.inputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO input in recipe.inputs)
                    {
                        string btnLabel = string.Format("{0:G4}x {1}/sec", input.ratio, input.resourceName);
                        KhemistryResourceInfo inputRes = FindResource(input.resourceName);
                        if (inputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(inputRes); }
                        else GUILayout.Label(btnLabel, _wrapLabel);
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.outputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO output in recipe.outputs)
                    {
                        string btnLabel = string.Format("{0:G4}x {1}/sec", output.ratio, output.resourceName);
                        KhemistryResourceInfo outputRes = FindResource(output.resourceName);
                        if (outputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(outputRes); }
                        else GUILayout.Label(btnLabel, _wrapLabel);
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private KhemistryResourceInfo FindResource(string name)
        {
            if (!KhemistryLibraryLoader.IsLoaded) return null;
            return KhemistryLibraryLoader.Resources.FirstOrDefault(r => r.name == name);
        }
    }

    ////////////////////////////// Storage System //////////////////////////////

    /* Example config node
MODULE
{
	name = KhemistryAdvancedStorage
    storageType = multiShared  // Can be type or storageType. "single" stores one resource, "multi" stores multiple and can be configured, "multiShared" stores multiple all at the same time
	maximumResources = 1000.0  // Maximum resources it can hold in total. This will be shared for multiShare types but per-resource for others.
	chargingRequired = true    // Does the container need to be charged to be used
	passiveConsumption = true  // Does the container have a passive consumption
	maxInputRate = 10.0        // Maximum transfer rate to the container. Do not include if you want it to be unlimited
	maxOutputRate = 10.0       // Maximum transfer rate from the container. Do not include if you want it to be unlimited
	chargeRate = 50.0          // Percent per second to fill charge (50 = 2 seconds to full). Not required if charging is disabled
	chargeDecayRate = 5.0      // Percent per second to lose charge when storage can no longer charge. Not required if charging is disabled
    filledUnpoweredResult = boiloff,1         // What will happen if the storage is not on but has a resource. Possible options are listed below
    passiveUnsatisfiedResult = destroy,500    // What will happen if the storage cannot consume resources as part of passive consumption. Possible options are listed below
                                              // off = The container will turn off
                                              // void = All resources will be voided
                                              // destroy,50 = The part will blow up with the specified power
                                              // boiloff,1 = All resources will slowly (or not) disappear at the specified amount per second.
                                              // Note that boiloff can only be applied to filledUnpoweredResult because passiveUnsatisfiedResult is only checked once and the container turns off.
                                              // Also the fields can have double quotes (") around them but that is not recommended to do.

	SUPPORTED_RESOURCES        // Resource the container supports and can hold at the same time.
	{                          // More than one entry with single type will error out and remove all after the first one.
		name = LiquidFuel      // If it isn't present, the part will error out and the storage will not show up.
		name = Oxidizer
		name = MonoPropellant
	}

	PASSIVE_CON_NAMES          // Resources used for passive consumption. Not required if passive consumption is disabled
	{
		name = ElectricCharge
	}
	PASSIVE_CON_AMOUNTS        // Amount of each resource used for passive consumption (per second). Not required if passive consumption is disabled
	{
		amount = 0.5
	}

	CHARGE_CON_NAMES           // Resources used for charge consumption. Not required if charging is disabled
	{
		name = ElectricCharge
	}
	CHARGE_CON_AMOUNTS         // Amount of each resource used for charge consumption (per second). Not required if charging is disabled
	{
		amount = 5.0
	}
}
    */
    /// <summary>
    /// A versatile storage system that can be configured to store multiple resources, require charging, and have passive consumption.
    /// See the comment above for a sample config.
    /// </summary>
    public class KhemistryAdvancedStorage : PartModule
    {
        [KSPField(isPersistant = false)]
        public string storageType = "single";

        [KSPField(isPersistant = false)]
        public float maximumResources = 1000f;

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public bool passiveConsumption = false;

        [KSPField(isPersistant = false)]
        public float maxInputRate = -1f;

        [KSPField(isPersistant = false)]
        public float maxOutputRate = -1f;

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;

        public enum StorageState { Off, Charging, On }

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public StorageState state = StorageState.Off;

        [KSPField(isPersistant = true)]
        public string activeResource = "";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryadvstorage",
                  groupDisplayName = "Khemistry Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistryadvstorage")]
        public string volumeDisplay = "0 / 0";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryadvstorage")]
        public string chargeDisplay = "N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryadvstorage")]
        public string stateDisplay = "Off";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Active Resource", groupName = "khemistryadvstorage")]
        public string activeResourceDisplay = "(none)";

        private readonly List<string> _supportedResources = new List<string>();

        private readonly List<string> _passiveNames = new List<string>();
        private readonly List<float> _passiveAmounts = new List<float>();

        private readonly List<string> _chargeNames = new List<string>();
        private readonly List<float> _chargeAmounts = new List<float>();

        private enum ConsequenceType { Off, Void, Destroy, Boiloff }

        private struct ConsequenceConfig
        {
            public ConsequenceType type;
            public float value;
        }

        private ConsequenceConfig _passiveUnsatisfiedResult;
        private ConsequenceConfig _filledUnpoweredResult;

        private readonly Dictionary<string, double> _frozenAmounts = new Dictionary<string, double>();

        private bool _passiveUnsatisfiedFired = false;
        private double _filledUnpoweredAccum = 0.0;

        private bool _fatalConfigError = false;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryadvstorage")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == StorageState.On) return;
            state = StorageState.Charging;
            KShared.Log("Charging enabled.", "KhemistryAdvancedStorage/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryadvstorage", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != StorageState.Charging) return;
            state = StorageState.Off;
            KShared.Log("Charging disabled.", "KhemistryAdvancedStorage/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn on container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOnContainer()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Container must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = StorageState.On;
            _passiveUnsatisfiedFired = false;
            KShared.Log("Container turned ON.", "KhemistryAdvancedStorage/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOffContainer()
        {
            state = StorageState.Off;
            KShared.Log("Container turned OFF.", "KhemistryAdvancedStorage/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Select resource",
                  groupName = "khemistryadvstorage", active = false)]
        public void SelectResource()
        {
            if (storageType != "multi") return;

            if (!string.IsNullOrEmpty(activeResource))
            {
                var def = PartResourceLibrary.Instance.GetDefinition(activeResource);
                if (def != null)
                {
                    PartResource pr = part.Resources.Get(def.id);
                    if (pr != null && pr.amount >= 1.0)
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Container must be nearly empty to switch resource.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
                    if (pr != null) pr.amount = 0.0;
                }
            }

            if (_supportedResources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No supported resources configured.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "KShared not available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Select active resource", new List<string>(_supportedResources), label =>
            {
                activeResource = label;
                KShared.Log("Active resource set to " + activeResource,
                    "KhemistryAdvancedStorage/SelectResource");
                ZeroNonActiveResources();
            });
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                contentsDisplay = "ERROR: see log";
                return;
            }

            EnsureResourcesExistOnPart();
            SnapshotFrozenAmounts();
            _passiveUnsatisfiedFired = false;
            _filledUnpoweredAccum = 0.0;
            UpdateUI();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;

            HandleCharging(dt);
            HandlePassiveConsumption(dt);
            HandleTransferBlocking();
            HandleFilledUnpowered(dt);
            EnforceCapacity();
            UpdateUI();
        }

        public override void OnUpdate()
        {
            if (_fatalConfigError) return;
            UpdateUI();
        }

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.LogError("partInfo.partConfig is null!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = null;
            foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (n.GetValue("name") == "KhemistryAdvancedStorage") { moduleNode = n; break; }
            }

            if (moduleNode == null)
            {
                KShared.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            _supportedResources.Clear();
            if (!moduleNode.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage but no SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());
            if (_supportedResources.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage with an empty SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            storageType = moduleNode.GetValue("storageType") ?? moduleNode.GetValue("type") ?? "single";

            if (float.TryParse(moduleNode.GetValue("maximumResources"), out float tmp)) maximumResources = tmp;
            maxInputRate = float.TryParse(moduleNode.GetValue("maxInputRate"), out tmp) ? tmp : -1f;
            maxOutputRate = float.TryParse(moduleNode.GetValue("maxOutputRate"), out tmp) ? tmp : -1f;
            if (float.TryParse(moduleNode.GetValue("chargeRate"), out tmp)) chargeRate = tmp;
            if (float.TryParse(moduleNode.GetValue("chargeDecayRate"), out tmp)) chargeDecayRate = tmp;

            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out bool tmpB)) chargingRequired = tmpB;
            if (bool.TryParse(moduleNode.GetValue("passiveConsumption"), out tmpB)) passiveConsumption = tmpB;

            _passiveUnsatisfiedResult = ParseConsequence(
                moduleNode.GetValue("passiveUnsatisfiedResult"), allowBoiloff: false,
                "passiveUnsatisfiedResult", "off");

            _filledUnpoweredResult = ParseConsequence(
                moduleNode.GetValue("filledUnpoweredResult"), allowBoiloff: true,
                "filledUnpoweredResult", "off");

            _passiveNames.Clear();
            _passiveAmounts.Clear();
            if (passiveConsumption)
            {
                if (moduleNode.HasNode("PASSIVE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("PASSIVE_CON_NAMES").GetValues("name"))
                        _passiveNames.Add(n.Trim());
                if (moduleNode.HasNode("PASSIVE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("PASSIVE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _passiveAmounts.Add(tmp); }
                if (_passiveNames.Count != _passiveAmounts.Count)
                    KShared.LogError("PASSIVE_CON_NAMES and PASSIVE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _chargeAmounts.Add(tmp); }
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            if ((storageType == "single" || storageType == "multi") && string.IsNullOrEmpty(activeResource))
                if (_supportedResources.Count > 0) activeResource = _supportedResources[0];

            if (storageType == "single" && _supportedResources.Count > 1)
            {
                KShared.LogError(
                    "storageType=single but multiple SUPPORTED_RESOURCES defined; only first will be used.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                string keep = _supportedResources[0];
                _supportedResources.Clear();
                _supportedResources.Add(keep);
                activeResource = keep;
            }

            KShared.Log(
                string.Format("Config loaded. storageType={0}, max={1}, chargingRequired={2}, passiveConsumption={3}, passiveUnsatisfiedResult={4}, filledUnpoweredResult={5}",
                    storageType, maximumResources, chargingRequired, passiveConsumption,
                    _passiveUnsatisfiedResult.type, _filledUnpoweredResult.type),
                "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
        }

        /// <summary>
        /// Parses "off", "void", "destroy,10", "boiloff,1.5" into a ConsequenceConfig.
        /// Falls back to the specified default if the raw value is null/invalid.
        /// </summary>
        private ConsequenceConfig ParseConsequence(string raw, bool allowBoiloff, string fieldName, string fallback)
        {
            string src = string.IsNullOrEmpty(raw) ? fallback : raw.Trim().Trim('"').Trim().ToLower();

            if (src == "off") return new ConsequenceConfig { type = ConsequenceType.Off };
            if (src == "void") return new ConsequenceConfig { type = ConsequenceType.Void };

            if (src.StartsWith("destroy,"))
            {
                if (float.TryParse(src.Substring(8), out float v))
                    return new ConsequenceConfig { type = ConsequenceType.Destroy, value = v };
                KShared.LogError("Could not parse destroy power in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            if (allowBoiloff && src.StartsWith("boiloff,"))
            {
                if (float.TryParse(src.Substring(8), out float v))
                    return new ConsequenceConfig { type = ConsequenceType.Boiloff, value = v };
                KShared.LogError("Could not parse boiloff rate in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            KShared.LogError("Unknown consequence value " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                "KhemistryAdvancedStorage/ParseConsequence");
            return new ConsequenceConfig { type = ConsequenceType.Off };
        }

        private void EnsureResourcesExistOnPart()
        {
            foreach (string resName in _supportedResources)
            {
                var def = PartResourceLibrary.Instance.GetDefinition(resName);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + resName + "\" in SUPPORTED_RESOURCES.",
                        "KhemistryAdvancedStorage/EnsureResourcesExistOnPart");
                    continue;
                }

                PartResource existing = part.Resources.Get(def.id);
                if (existing == null)
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("name", resName);
                    node.AddValue("amount", 0.0);
                    node.AddValue("maxAmount", maximumResources);
                    part.AddResource(node);
                }
                else
                {
                    existing.maxAmount = maximumResources;
                    if (existing.amount < 0.0) existing.amount = 0.0;
                }
            }

            if (storageType == "multi")
                ZeroNonActiveResources();
        }

        private void ZeroNonActiveResources()
        {
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (!string.IsNullOrEmpty(activeResource) && pr.resourceName != activeResource)
                    pr.amount = 0.0;
            }
        }

        private void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == StorageState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != StorageState.Charging) return;

            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = StorageState.On;
                KShared.Log("Container fully charged, now ON.",
                    "KhemistryAdvancedStorage/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        private void HandlePassiveConsumption(double dt)
        {
            if (!passiveConsumption) return;

            if (state == StorageState.On)
            {
                bool satisfied = ConsumeVesselResources(_passiveNames, _passiveAmounts, dt);
                if (satisfied)
                {
                    _passiveUnsatisfiedFired = false;
                    return;
                }
            }

            if (!HasAnyStoredResources()) return;

            if (!_passiveUnsatisfiedFired)
            {
                _passiveUnsatisfiedFired = true;
                ApplyConsequence(_passiveUnsatisfiedResult, "passiveUnsatisfiedResult");
            }
        }

        private void HandleFilledUnpowered(double dt)
        {
            if (state == StorageState.On) return;

            if (!HasAnyStoredResources()) return;

            _filledUnpoweredAccum += dt;

            while (_filledUnpoweredAccum >= 0.1)
            {
                _filledUnpoweredAccum -= 0.1;
                ApplyConsequence(_filledUnpoweredResult, "filledUnpoweredResult", tickDt: 0.1);
            }
        }

        // ── Consequence execution ──────────────────────────────────────────────────

        /// <summary>
        /// Executes a consequence. tickDt is only used by Boiloff (the value in config is per second;
        /// we receive a 0.1 s tick so we apply value * 0.1 per call).
        /// </summary>
        private void ApplyConsequence(ConsequenceConfig cfg, string source, double tickDt = 0.0)
        {
            switch (cfg.type)
            {
                case ConsequenceType.Off:
                    break;

                case ConsequenceType.Void:
                    KShared.Log("Voiding all stored resources (" + source + ").",
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    foreach (PartResource pr in part.Resources)
                    {
                        if (_supportedResources.Contains(pr.resourceName))
                            pr.amount = 0.0;
                    }
                    break;

                case ConsequenceType.Destroy:
                    KShared.Log(
                        string.Format("Destroying part with power {0:F1} ({1}).", cfg.value, source),
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    part.explode();
                    break;

                case ConsequenceType.Boiloff:
                    ApplyBoiloff(cfg.value * (float)tickDt, source);
                    break;
            }
        }

        /// <summary>
        /// Reduces stored resources by a flat amount per tick, distributed proportionally
        /// across all resources that currently have any amount. Works correctly for all
        /// storage types:
        ///   single / multi   — only the active resource has any amount, so it drains alone.
        ///   multiShared      — all resources drain proportionally to their current fill.
        /// </summary>
        private void ApplyBoiloff(float amountPerTick, string source)
        {
            var filled = new List<PartResource>();
            double total = 0.0;
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0) { filled.Add(pr); total += pr.amount; }
            }
            if (filled.Count == 0 || total <= 0.0) return;

            double toDrain = Math.Min(amountPerTick, total);

            foreach (PartResource pr in filled)
            {
                double share = (pr.amount / total) * toDrain;
                pr.amount = Math.Max(0.0, pr.amount - share);
                _frozenAmounts[pr.resourceName] = pr.amount;
            }

            KShared.Log(
                string.Format("Boiloff: drained {0:F4} units ({1}).", toDrain, source),
                "KhemistryAdvancedStorage/ApplyBoiloff");
        }

        // ── Vessel resource consumption ────────────────────────────────────────────

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        private bool HasAnyStoredResources()
        {
            foreach (PartResource pr in part.Resources)
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 0.0)
                    return true;
            return false;
        }

        private void SnapshotFrozenAmounts()
        {
            _frozenAmounts.Clear();
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                _frozenAmounts[pr.resourceName] = pr.amount;
            }
        }

        private void HandleTransferBlocking()
        {
            bool shouldFreeze =
                (chargingRequired && state != StorageState.On) ||
                (!chargingRequired && state == StorageState.Off);

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;

                if (!shouldFreeze)
                {
                    _frozenAmounts[pr.resourceName] = pr.amount;
                }
                else
                {
                    if (_frozenAmounts.TryGetValue(pr.resourceName, out double frozen))
                        pr.amount = frozen;
                    else
                        _frozenAmounts[pr.resourceName] = pr.amount;
                }
            }
        }

        private void EnforceCapacity()
        {
            foreach (PartResource pr in part.Resources)
                if (pr.amount < 0.0) pr.amount = 0.0;

            if (storageType == "multiShared")
            {
                double total = 0.0;
                var list = new List<PartResource>();
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    list.Add(pr);
                    total += pr.amount;
                }

                if (total > maximumResources && total > 0.0)
                {
                    double scale = maximumResources / total;
                    foreach (PartResource pr in list) pr.amount *= scale;
                }

                foreach (PartResource pr in list) pr.maxAmount = maximumResources;
            }
            else
            {
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    pr.amount = Math.Min(pr.amount, maximumResources);
                    pr.amount = Math.Max(pr.amount, 0.0);
                    pr.maxAmount = maximumResources;
                }

                if (storageType == "multi")
                    ZeroNonActiveResources();
            }
        }

        private void UpdateUI()
        {
            double total = 0.0;
            var parts = new List<string>();

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0)
                {
                    parts.Add(string.Format("{0}: {1:F2}", pr.resourceName, pr.amount));
                    total += pr.amount;
                }
            }

            contentsDisplay = parts.Count == 0 ? "Empty" : string.Join(", ", parts.ToArray());
            volumeDisplay = string.Format("{0:F2} / {1:F2}", total, maximumResources);

            chargeDisplay = chargingRequired
                ? string.Format("{0:F1}%", chargePercent)
                : "N/A";

            stateDisplay = state.ToString();

            activeResourceDisplay = (storageType == "single" || storageType == "multi")
                ? (string.IsNullOrEmpty(activeResource) ? "(none)" : activeResource)
                : "(multiShared)";

            Events["EnableCharging"].active = chargingRequired && state != StorageState.Charging && state != StorageState.On;
            Events["DisableCharging"].active = chargingRequired && state == StorageState.Charging;
            Events["TurnOnContainer"].active = state != StorageState.On;
            Events["TurnOffContainer"].active = state == StorageState.On;
            Events["SelectResource"].active = storageType == "multi";
        }
    }

    ////////////////////////////// ISRU System //////////////////////////////

    /// <summary>
    /// Contains all shared config, cycle logic, and helpers for AdvancedISRUs.
    /// </summary>
    public abstract class KhemistryAdvancedISRUBase : PartModule
    {
        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start Converter";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop Converter";

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;

        protected readonly List<string> _chargeNames = new List<string>();
        protected readonly List<float> _chargeAmounts = new List<float>();

        public enum ConverterState { Off, Charging, On }

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public ConverterState state = ConverterState.Off;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryisru")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == ConverterState.On) return;
            state = ConverterState.Charging;
            KShared.Log("Charging enabled.", "KhemistryAdvancedISRUBase/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != ConverterState.Charging) return;
            state = ConverterState.Off;
            KShared.Log("Charging disabled.", "KhemistryAdvancedISRUBase/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Prepare converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOnConverter()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = ConverterState.On;
            KShared.Log("Converter turned ON.", "KhemistryAdvancedISRUBase/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOffConverter()
        {
            state = ConverterState.Off;
            KShared.Log("Converter turned OFF.", "KhemistryAdvancedISRUBase/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Auto Manual Cycle",
                  groupName = "khemistryisru")]
        public void EnableAuto()
        {
            if (!_manualOperation) return;  // Shouldn't be active if not manual ISRU anyway
            _manualAuto = true;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Auto Manual Cycle",
                  groupName = "khemistryisru")]
        public void DisableAuto()
        {
            if (!_manualOperation) return;  // Shouldn't be active if not manual ISRU anyway
            _manualAuto = false;
        }

        [KSPField(isPersistant = true)] public bool isRunning = false;
        [KSPField(isPersistant = true)] public bool needsMaintenance = false;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Status", groupName = "khemistryisru",
                  groupDisplayName = "Khemistry ISRU", groupStartCollapsed = false)]
        public string statusDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryisru")]
        public string chargeDisplay = "N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryisru")]
        public string stateDisplay = "Off";

        public bool IsCurrentlyActive { get; protected set; } = false;

        protected struct ResourceInput
        {
            public string resourceName;
            public double ratio;
            public ResourceFlowMode flowMode;
        }

        protected struct ResourceOutput
        {
            public string resourceName;
            public double ratio;
            public bool dumpExcess;
        }

        protected struct ResourceOutputMaterial
        {
            public string name;
            public string shape;
            public string size;
            public bool usesParams;
            public Dictionary<string, string> parameters;
            public double ratio;
            public double outVolume;
        }

        protected enum PowerfailResult { Pause, Stop, Void, Maint, Explode }

        protected readonly List<ResourceInput> _inputs = new List<ResourceInput>();
        protected readonly List<ResourceOutput> _outputs = new List<ResourceOutput>();
        protected readonly List<ResourceOutputMaterial> _outputMaterials = new List<ResourceOutputMaterial>();

        protected Dictionary<ResourceOutputMaterial, double> _materialOutputAmount = new Dictionary<ResourceOutputMaterial, double>();

        protected string _planetCondition = null;
        protected string _biomeCondition = null;
        protected double _altMin = double.MinValue;
        protected double _altMax = double.MaxValue;
        protected KShared.SituationCondition _situationCondition = KShared.SituationCondition.Any;
        protected string _depositCondition = null;

        protected string _powerfailResource = null;
        protected PowerfailResult _powerfailResult = PowerfailResult.Pause;
        protected float _powerfailExplosionRadius = 0f;
        protected float _powerfailExplosionTemperature = 0f;  // Celsius

        protected bool _manualOperation = false;
        protected bool _manualRequiresStartup = true;
        protected bool _manualAuto = false;  // If true, the converter is automatically doing manual cycles if a kerbal is nearby.

        protected bool _startStopShowPAW = true;
        protected bool _startStopShowEVA = false;
        protected bool _manualShowPAW = true;
        protected bool _manualShowEVA = false;

        protected float _maxInteractionDistance = 10f;

        protected string _recipeGroup = null;

        protected string _displayName = "Converter";

        protected bool _fatalConfigError = false;
        protected double _outputWarnCooldown = 0.0;

        /// <summary>
        /// Runs one converter cycle against the kerbal's suit cell instead of the vessel
        /// network. The dictionary maps resource name → amount (at most one entry, since
        /// the suit cell holds one resource type at a time). Mutates the dictionary in place.
        /// Returns true if the cycle ran.
        /// </summary>
        public bool RunOneCycleSuitCell(Part contextPart, Dictionary<string, double> suitCell,
    float suitMaxAmount, double dt)
        {
            if (!CheckConditions(contextPart.vessel, out string conditionReason))
            {
                statusDisplay = "Inactive: " + conditionReason;
                return false;
            }

            double currentTotal = DictTotal(suitCell);
            double inputConsumed = 0.0;
            foreach (ResourceInput inp in _inputs) inputConsumed += inp.ratio * dt;
            double outputProduced = 0.0;
            foreach (ResourceOutput o in _outputs)
                if (!o.dumpExcess) outputProduced += o.ratio * dt;

            if (currentTotal - inputConsumed + outputProduced > suitMaxAmount + 1e-9)
            {
                if (_outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Not enough space in suit cell, converter paused!",
                            _displayName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 5.0;
                }
                statusDisplay = "Paused: suit cell full";
                return false;
            }

            bool powerfailShort = false;
            if (_powerfailResource != null)
            {
                suitCell.TryGetValue(_powerfailResource, out double pfNeeded);
#pragma warning disable IDE0059 // Claude thinks this is required for some reason, don't want to break the code
                double pfAvailable = pfNeeded;
#pragma warning restore IDE0059 // Claude thinks this is required for some reason, don't want to break the code
                suitCell.TryGetValue(_powerfailResource, out pfAvailable);
                if (pfAvailable < GetInputRatio(_powerfailResource) * dt * 0.999)
                    powerfailShort = true;
            }

            bool allSatisfied = true;
            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) continue;
                suitCell.TryGetValue(inp.resourceName, out double available);
                if (available < inp.ratio * dt * 0.999) { allSatisfied = false; break; }
            }

            if (!allSatisfied)
            {
                if (powerfailShort)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Powerfailed due to lack of {1}!",
                            _displayName, _powerfailResource),
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    TriggerPowerfail(contextPart, null);
                }
                else
                {
                    statusDisplay = "Insufficient resources";
                }
                return false;
            }

            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) continue;
                suitCell.TryGetValue(inp.resourceName, out double current);
                double remaining = current - inp.ratio * dt;
                if (remaining < 1e-9) suitCell.Remove(inp.resourceName);
                else suitCell[inp.resourceName] = remaining;
            }

            foreach (ResourceOutput output in _outputs)
            {
                if (output.ratio <= 0.0) continue;
                double toAdd = output.ratio * dt;
                if (output.dumpExcess)
                {
                    double spaceLeft = suitMaxAmount - DictTotal(suitCell);
                    toAdd = Math.Min(toAdd, Math.Max(0.0, spaceLeft));
                }
                if (toAdd <= 1e-12) continue;
                suitCell.TryGetValue(output.resourceName, out double existing);
                suitCell[output.resourceName] = existing + toAdd;
            }

            statusDisplay = _manualOperation ? "Waiting for manual cycle" : "Running";
            return true;
        }

        private static double DictTotal(Dictionary<string, double> dict)
        {
            double total = 0.0;
            foreach (var kvp in dict) total += kvp.Value;
            return total;
        }

        // ── Config loading ─────────────────────────────────────────────────────────

        protected void LoadSharedConfig(ConfigNode moduleNode, string moduleName)
        {
            _inputs.Clear();
            foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
            {
                string resName = inputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double.TryParse(inputNode.GetValue("Ratio"), out double ratio);

                ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                string flowStr = inputNode.GetValue("FlowMode");
                if (!string.IsNullOrEmpty(flowStr))
                {
                    if (Enum.TryParse(flowStr.Trim(), true, out ResourceFlowMode parsed))
                        flowMode = parsed;
                    else
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                            moduleName + "/LoadSharedConfig");
                }

                _inputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
            }

            _outputs.Clear();
            foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
            {
                string resName = outputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double.TryParse(outputNode.GetValue("Ratio"), out double ratio);

                bool.TryParse(outputNode.GetValue("DumpExcess"), out bool dumpExcess);

                _outputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
            }

            _outputMaterials.Clear();
            foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE_MATERIAL"))
            {
                string matName = outputNode.GetValue("name");
                if (string.IsNullOrEmpty(matName)) continue;

                string shape = outputNode.GetValue("shape");
                string size = outputNode.GetValue("size");
                double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                double.TryParse(outputNode.GetValue("outVolume"), out double vol);
                bool usesParams = outputNode.HasNode("PARAMS");
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                if (usesParams)
                    foreach (string key in outputNode.GetNode("PARAMS").values.DistinctNames())
                        parameters.Add(key, outputNode.GetNode("PARAMS").GetValue(key));

                _outputMaterials.Add(new ResourceOutputMaterial
                {
                    name = matName,
                    shape = shape,
                    size = size,
                    usesParams = usesParams,
                    parameters = parameters,
                    ratio = ratio,
                    outVolume = vol
                });
            }

            if (_outputs.Count == 0 && _outputMaterials.Count == 0)
                KShared.LogError(
                    "Converter \"" + ConverterName + "\" has no OUTPUT_RESOURCE nor OUTPUT_RESOURCE_MATERIAL nodes — it will do nothing.",
                    moduleName + "/LoadSharedConfig");

            _planetCondition = NullIfEmpty(moduleNode.GetValue("planetCondition"));
            _biomeCondition = NullIfEmpty(moduleNode.GetValue("biomeCondition"));
            if (_biomeCondition != null && _planetCondition == null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": biomeCondition set without planetCondition — biomeCondition ignored.",
                    moduleName + "/LoadSharedConfig");
                _biomeCondition = null;
            }

            _altMin = double.MinValue;
            _altMax = double.MaxValue;
            if (double.TryParse(moduleNode.GetValue("altitudeMinCondition"), out double altTmp)) _altMin = altTmp;
            if (double.TryParse(moduleNode.GetValue("altitudeMaxCondition"), out altTmp)) _altMax = altTmp;

            _situationCondition = KShared.SituationCondition.Any;
            string sitStr = NullIfEmpty(moduleNode.GetValue("situationCondition"));
            if (sitStr != null)
            {
                if (Enum.TryParse(sitStr, true, out KShared.SituationCondition parsed))
                    _situationCondition = parsed;
                else
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": Unknown situationCondition \"" + sitStr + "\" — condition ignored.",
                        moduleName + "/LoadSharedConfig");
            }

            _depositCondition = NullIfEmpty(moduleNode.GetValue("depositCondition"));

            if (float.TryParse(moduleNode.GetValue("chargeRate"), out float tmp)) chargeRate = tmp;
            if (float.TryParse(moduleNode.GetValue("chargeDecayRate"), out tmp)) chargeDecayRate = tmp;

            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out bool tmp2)) chargingRequired = tmp2;

            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _chargeAmounts.Add(tmp); }
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            _powerfailResource = null;
            _powerfailResult = PowerfailResult.Pause;
            _powerfailExplosionRadius = 0f;
            _powerfailExplosionTemperature = 0f;

            string pfRes = NullIfEmpty(moduleNode.GetValue("powerfailResource"));
            string pfResultRaw = NullIfEmpty(moduleNode.GetValue("powerfailResult"));

            if (pfRes != null)
            {
                bool found = false;
                foreach (ResourceInput inp in _inputs)
                    if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                if (!found)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.",
                        moduleName + "/LoadSharedConfig");
                }
                else
                {
                    _powerfailResource = pfRes;
                    if (pfResultRaw != null)
                    {
                        string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                        if (pfResult == "PAUSE")
                        {
                            _powerfailResult = PowerfailResult.Pause;
                        }
                        else if (pfResult == "STOP")
                        {
                            _powerfailResult = PowerfailResult.Stop;
                        }
                        else if (pfResult == "VOID")
                        {
                            _powerfailResult = PowerfailResult.Void;
                        }
                        else if (pfResult == "MAINT")
                        {
                            _powerfailResult = PowerfailResult.Maint;
                        }
                        else if (pfResult.StartsWith("EXPLODE,"))
                        {
                            string[] parts = pfResult.Substring(8).Split(',');
                            if (parts.Length == 2
                                && float.TryParse(parts[0], out float radius)
                                && float.TryParse(parts[1], out float tempC))
                            {
                                _powerfailResult = PowerfailResult.Explode;
                                _powerfailExplosionRadius = radius;
                                _powerfailExplosionTemperature = tempC;
                            }
                            else
                            {
                                KShared.LogError(
                                    "Converter \"" + ConverterName + "\": Could not parse EXPLODE radius/temperature \"" + pfResultRaw + "\" (expected EXPLODE,radiusMeters,tempCelsius) — defaulting to PAUSE.",
                                    moduleName + "/LoadSharedConfig");
                                _powerfailResult = PowerfailResult.Pause;
                            }
                        }
                        else
                        {
                            KShared.LogError(
                                "Converter \"" + ConverterName + "\": Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to PAUSE.",
                                moduleName + "/LoadSharedConfig");
                            _powerfailResult = PowerfailResult.Pause;
                        }
                    }
                }
            }
            else if (pfResultRaw != null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": powerfailResult set without powerfailResource — powerfailResult ignored.",
                    moduleName + "/LoadSharedConfig");
            }

            _manualOperation = false;
            _manualRequiresStartup = true;
            if (bool.TryParse(moduleNode.GetValue("manualOperation"), out bool tmpB)) _manualOperation = tmpB;
            if (bool.TryParse(moduleNode.GetValue("manualRequiresStartup"), out tmpB)) _manualRequiresStartup = tmpB;

            KShared.ParseShowRule(
                NullIfEmpty(moduleNode.GetValue("startStopShowRules")) ?? "PAW",
                out _startStopShowPAW, out _startStopShowEVA,
                "startStopShowRules", moduleName);

            KShared.ParseShowRule(
                NullIfEmpty(moduleNode.GetValue("manualShowRules")) ?? "PAW",
                out _manualShowPAW, out _manualShowEVA,
                "manualShowRules", moduleName);

            _maxInteractionDistance = 10f;
            if (float.TryParse(moduleNode.GetValue("maxInteractionDistance"), out float distTmp))
                _maxInteractionDistance = distTmp;

            _recipeGroup = NullIfEmpty(moduleNode.GetValue("recipeGroup"));

            KShared.Log(
                string.Format("Converter \"{0}\" loaded: {1} inputs, {2} outputs, manual={3}, requiresStartup={4}, group={5}",
                    ConverterName, _inputs.Count, _outputs.Count + _outputMaterials.Count,
                    _manualOperation, _manualRequiresStartup, _recipeGroup ?? "none"),
                moduleName + "/LoadSharedConfig");
        }

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedISRUBase/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        public void UpdateUI()
        {
            chargeDisplay = chargingRequired
                ? string.Format("{0:F1}%", chargePercent)
                : "N/A";

            if (state == ConverterState.On)
                stateDisplay = "Ready";
            else
                stateDisplay = state.ToString();

            Events["EnableCharging"].active = chargingRequired && state != ConverterState.Charging && state != ConverterState.On;
            Events["DisableCharging"].active = chargingRequired && state == ConverterState.Charging;
            Events["TurnOnConverter"].active = state != ConverterState.On;
            Events["TurnOffConverter"].active = state == ConverterState.On;

            Events["EnableAuto"].active = _manualOperation && !_manualAuto;
            Events["DisableAuto"].active = _manualOperation && _manualAuto;
        }

        public void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == ConverterState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != ConverterState.Charging) return;

            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = ConverterState.On;
                KShared.Log("Converter fully charged, now ON.",
                    "KhemistryAdvancedISRUBase/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        public bool FreeKerbalNearby(string occupation)
        {
            // Position of the converter
            Vector3 myPos = part.transform.position;

            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                // Ignore anything that isn't an EVA kerbal
                if (v.vesselType != VesselType.EVA)
                    continue;

                // Get the distance to this kerbal
                float distance = Vector3.Distance(myPos, v.transform.position);

                // Ignore the kerbal if it is too far
                if (distance > _maxInteractionDistance)
                    continue;

                // The EVA vessel usually has exactly one part
                Part evaPart = v.rootPart;

                // Get the KhemistryKerbal module
                KhemistryKerbal module = evaPart.FindModuleImplementing<KhemistryKerbal>();

                if (module != null)
                {
                    // This kerbal is already occupied by this ISRU
                    if (module.occupation == occupation) return true;
                    // This kerbal is already occupied and is doing something
                    if (!string.IsNullOrEmpty(module.occupation)) continue;
                    // This kerbal cannot be occupied
                    if (!module.canBeOccupied) continue;

                    KShared.Log(
                        string.Format("Found free kerbal {0} for occupation \"{1}\" at distance {2:F1}m.",
                            module.name, occupation, distance),
                        "KhemistryAdvancedISRUBase/FreeKerbalNearby");

                    module.occupation = occupation;
                    return true;
                }
            }

            return false;
        }

        public void RunOneCycle(Part contextPart, double dt)
        {
            IsCurrentlyActive = false;

            if (!CheckConditions(contextPart.vessel, out string conditionReason))
            {
                statusDisplay = "Inactive: " + conditionReason;
                return;
            }

            string blockedResource = CheckOutputSpace(contextPart.vessel, dt);
            if (blockedResource != null)
            {
                if (_outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": No output space for {1}, converter paused!",
                            _displayName, blockedResource),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 5.0;
                }
                statusDisplay = "Paused: " + blockedResource + " full";
                return;
            }

            bool powerfailShort = false;
            if (_powerfailResource != null)
            {
                double pfNeeded = GetInputRatio(_powerfailResource) * dt;
                double pfAvailable = GetVesselResourceAmount(contextPart.vessel, _powerfailResource);
                if (pfAvailable < pfNeeded * 0.999)
                    powerfailShort = true;
            }

            if (!ConsumeInputs(contextPart, dt, out var pulled))
            {
                if (powerfailShort)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Converter \"{0}\": Powerfailed due to lack of {1}!",
                            _displayName, _powerfailResource),
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    TriggerPowerfail(contextPart, pulled);
                }
                else
                {
                    // Not the designated powerfail resource — just a transient shortage.
                    // Always give back whatever was pulled and retry next tick.
                    RefundPulled(contextPart, pulled);
                    statusDisplay = "Insufficient resources";
                }
                return;
            }

            if (_manualAuto && !FreeKerbalNearby("Running converter " + ConverterName))
            {
                statusDisplay = "No kerbal to operate the machine";
                return;
            }

            ProduceOutputs(contextPart, dt);
            statusDisplay = (!_manualAuto && _manualOperation) ? "Waiting for manual cycle" : "Running";
            IsCurrentlyActive = true;
        }

        protected bool CheckConditions(Vessel v, out string reason)
        {
            reason = null;

            if (_planetCondition != null)
            {
                string currentBody = v.mainBody?.name ?? "";
                if (!currentBody.Equals(_planetCondition, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "wrong body (" + currentBody + ")";
                    return false;
                }

                if (_biomeCondition != null)
                {
                    string currentBiome = ScienceUtil.GetExperimentBiome(
                        v.mainBody, v.latitude, v.longitude);
                    if (!currentBiome.Equals(_biomeCondition, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "wrong biome (" + currentBiome + ")";
                        return false;
                    }
                }
            }

            double alt = v.altitude;
            if (_altMin != double.MinValue && alt < _altMin)
            {
                reason = string.Format("below min altitude ({0:F0} m)", _altMin);
                return false;
            }
            if (_altMax != double.MaxValue && alt > _altMax)
            {
                reason = string.Format("above max altitude ({0:F0} m)", _altMax);
                return false;
            }

            if (_situationCondition != KShared.SituationCondition.Any && !CheckSituation(v))
            {
                reason = "wrong situation (" + v.situation + ")";
                return false;
            }

            if (!KShared.Instance.SurfaceDepositsAtPoint((float)v.latitude, (float)v.longitude, v.mainBody.name, 0).Contains(_depositCondition) && _depositCondition != null && _depositCondition != "")
            {
                reason = "not at deposit " + _depositCondition;
                return false;
            }

            return true;
        }

        protected bool CheckSituation(Vessel v)
        {
            Vessel.Situations sit = v.situation;
            CelestialBody body = v.mainBody;
            double alt = v.altitude;

            switch (_situationCondition)
            {
                case KShared.SituationCondition.Landed:
                    return sit == Vessel.Situations.LANDED || sit == Vessel.Situations.PRELAUNCH;
                case KShared.SituationCondition.Splashed:
                    return sit == Vessel.Situations.SPLASHED;
                case KShared.SituationCondition.FlyingLow:
                    return sit == Vessel.Situations.FLYING
                        && body != null && alt < body.scienceValues.flyingAltitudeThreshold;
                case KShared.SituationCondition.FlyingHigh:
                    return sit == Vessel.Situations.FLYING
                        && body != null && alt >= body.scienceValues.flyingAltitudeThreshold;
                case KShared.SituationCondition.SpaceLow:
                    return (sit == Vessel.Situations.ORBITING || sit == Vessel.Situations.SUB_ORBITAL)
                        && body != null && alt < body.scienceValues.spaceAltitudeThreshold;
                case KShared.SituationCondition.SpaceHigh:
                    return (sit == Vessel.Situations.ORBITING
                         || sit == Vessel.Situations.SUB_ORBITAL
                         || sit == Vessel.Situations.ESCAPING)
                        && body != null && alt >= body.scienceValues.spaceAltitudeThreshold;
                case KShared.SituationCondition.SubOrbital:
                    return sit == Vessel.Situations.SUB_ORBITAL;
                default:
                    return true;
            }
        }

        protected string CheckOutputSpace(Vessel v, double dt)
        {
            foreach (ResourceOutput output in _outputs)
            {
                if (output.dumpExcess) continue;
                double needed = output.ratio * dt;
                if (needed <= 0.0) continue;
                if (GetVesselResourceSpace(v, output.resourceName) < needed * 0.001)
                    return output.resourceName;
            }
            foreach (ResourceOutputMaterial outputM in _outputMaterials)
            {
                double needed = outputM.ratio * dt;
                if (needed <= 0.0) continue;
                if (_materialOutputAmount.Keys.Contains(outputM))
                    if (_materialOutputAmount[outputM] + needed > 2)
                        return outputM.name;
            }
            return null;
        }

        protected double GetVesselResourceSpace(Vessel v, string resourceName)
        {
            double space = 0.0;
            foreach (Part p in v.parts)
                foreach (PartResource pr in p.Resources)
                    if (pr.resourceName == resourceName && pr.flowState)
                        space += pr.maxAmount - pr.amount;
            return space;
        }

        protected double GetVesselResourceAmount(Vessel v, string resourceName)
        {
            double total = 0.0;
            foreach (Part p in v.parts)
                foreach (PartResource pr in p.Resources)
                    if (pr.resourceName == resourceName && pr.flowState)
                        total += pr.amount;
            return total;
        }

        protected double GetInputRatio(string resourceName)
        {
            foreach (ResourceInput inp in _inputs)
                if (inp.resourceName.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
                    return inp.ratio;
            return 0.0;
        }

        protected bool ConsumeInputs(Part contextPart, double dt, out List<(string name, ResourceFlowMode mode, double amount)> pulled)
        {
            pulled = new List<(string name, ResourceFlowMode mode, double amount)>(_inputs.Count);
            bool allSatisfied = true;

            foreach (ResourceInput inp in _inputs)
            {
                if (inp.ratio <= 0.0) { pulled.Add((inp.resourceName, inp.flowMode, 0.0)); continue; }
                double needed = inp.ratio * dt;
                double got = contextPart.RequestResource(inp.resourceName, needed, inp.flowMode);
                pulled.Add((inp.resourceName, inp.flowMode, got));
                if (got < needed * 0.999) allSatisfied = false;
            }

            // NOTE: does not auto-refund on failure anymore — the caller decides whether the
            // pulled amounts should be returned (PAUSE/STOP) or discarded (VOID/MAINT).
            return allSatisfied;
        }

        private static void RefundPulled(Part contextPart, List<(string name, ResourceFlowMode mode, double amount)> pulled)
        {
            if (pulled == null) return;
            foreach (var entry in pulled)
                if (entry.amount > 0.0)
                    contextPart.RequestResource(entry.name, -entry.amount, entry.mode);
        }

        protected void ProduceOutputs(Part contextPart, double dt)
        {
            foreach (ResourceOutput output in _outputs)
            {
                if (output.ratio <= 0.0) continue;
                contextPart.RequestResource(output.resourceName, -(output.ratio * dt), ResourceFlowMode.ALL_VESSEL);
            }
            foreach (ResourceOutputMaterial outputM in _outputMaterials)
            {
                if (outputM.ratio <= 0.0) continue;
                if (!_materialOutputAmount.ContainsKey(outputM))
                    _materialOutputAmount.Add(outputM, 0);
                _materialOutputAmount[outputM] += outputM.ratio * dt;
            }
        }

        /// <summary>
        /// Attempts to drain any buffered material output into a KhemistryMaterialStorage
        /// on the vessel. Only whole units are ever moved (fractional production stays
        /// buffered until it accumulates to a whole unit); every buffered material is
        /// attempted each call, not just the first one that succeeds.
        /// </summary>
        protected bool TryTransferMaterialOutputBuffer()
        {
            if (vessel == null || part == null) return false;
            if (_materialOutputAmount.Count == 0) return false;

            bool transferredAny = false;

            // Snapshot the keys: we mutate _materialOutputAmount's values while iterating.
            foreach (ResourceOutputMaterial matOutput in _materialOutputAmount.Keys.ToList())
            {
                double buffered = _materialOutputAmount[matOutput];
                double wholeUnits = Math.Floor(buffered);
                if (wholeUnits < 1.0) continue;  // Only whole numbers are ever output

                KhemistryMaterialInstance instance = ConstructMaterialInstanceFromOutputMaterial(matOutput, wholeUnits);
                if (instance == null) continue;  // Lookup failure already logged below

                bool placed = false;
                foreach (Part vesselPart in vessel.parts)
                {
                    foreach (KhemistryMaterialStorage storageModule in vesselPart.Modules.OfType<KhemistryMaterialStorage>())
                    {
                        if (storageModule.AddMaterial(instance))
                        {
                            // Keep any fractional remainder instead of discarding it.
                            _materialOutputAmount[matOutput] = buffered - wholeUnits;
                            transferredAny = true;
                            placed = true;
                            break;
                        }
                    }
                    if (placed) break;
                }
            }

            return transferredAny;
        }

        /// <summary>
        /// Builds a KhemistryMaterialInstance for a produced OUTPUT_RESOURCE_MATERIAL, resolving
        /// its KhemistryMaterial definition from KShared's loaded material list.
        /// wholeUnits is the number of whole units accumulated in the buffer to actually output;
        /// outVolume in the config is treated as the volume of a single whole unit.
        /// </summary>
        private KhemistryMaterialInstance ConstructMaterialInstanceFromOutputMaterial(ResourceOutputMaterial outputM, double wholeUnits)
        {
            KhemistryMaterial material = KShared.Instance?.materialList.FirstOrDefault(m => m.name == outputM.name);
            if (material == null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": OUTPUT_RESOURCE_MATERIAL \"" + outputM.name
                    + "\" does not match any loaded KHEMISTRY_MATERIAL definition.",
                    "KhemistryAdvancedISRUBase/ConstructMaterialInstanceFromOutputMaterial");
                return null;
            }

            return new KhemistryMaterialInstance(
                material,
                outputM.shape,
                outputM.size,
                (float)(outputM.outVolume * wholeUnits),
                outputM.parameters
            );
        }

        /// <summary>
        /// Applies the current powerfail result. `pulled` is whatever ConsumeInputs withdrew
        /// this tick before discovering the shortfall (null/empty if nothing was withdrawn,
        /// e.g. the suit-cell path, which checks availability before taking anything). PAUSE
        /// refunds and leaves the converter running. STOP refunds and stops it. VOID/MAINT
        /// discard the pulled amounts instead of refunding them (MAINT also requires an
        /// Engineer). EXPLODE destroys the part and applies falling-off heat to nearby parts.
        /// </summary>
        protected void TriggerPowerfail(Part contextPart, List<(string name, ResourceFlowMode mode, double amount)> pulled)
        {
            KShared.Log(
                "Converter \"" + _displayName + "\" powerfailed. Result: " + _powerfailResult,
                "KhemistryAdvancedISRUBase/TriggerPowerfail");

            switch (_powerfailResult)
            {
                case PowerfailResult.Pause:
                    RefundPulled(contextPart, pulled);
                    statusDisplay = "Paused: out of " + _powerfailResource;
                    break;
                case PowerfailResult.Stop:
                    RefundPulled(contextPart, pulled);
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case PowerfailResult.Void:
                    // Pulled amounts are intentionally NOT refunded — they're lost.
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail, resources lost)";
                    break;
                case PowerfailResult.Maint:
                    isRunning = false;
                    needsMaintenance = true;
                    statusDisplay = "Needs maintenance";
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + _displayName + "\": Requires maintenance by an Engineer.",
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    break;
                case PowerfailResult.Explode:
                    KShared.TriggerExplosionWithHeat(contextPart, _powerfailExplosionRadius, _powerfailExplosionTemperature);
                    break;
            }
        }

        // ── Recipe group check ─────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether another converter in the same recipeGroup is already running
        /// on the given part. For EVA use, pass the Kerbal's part and this will check
        /// all EVA ISRU modules across all stored items in the Kerbal's inventory.
        /// </summary>
        public bool CheckRecipeGroup(Part contextPart)
        {
            if (_recipeGroup == null) return true;

            foreach (PartModule pm in contextPart.Modules)
            {
                KhemistryAdvancedISRUBase other = pm as KhemistryAdvancedISRUBase;
                if (other == null || other == this) continue;
                if (other._recipeGroup != _recipeGroup) continue;
                if (other.isRunning)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Another converter in group " + _recipeGroup + " is already running!",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    return false;
                }
            }
            return true;
        }

        protected static string NullIfEmpty(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public void TickCooldown(double dt)
        {
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);
        }

        protected abstract void LoadConfigFromPartInfo();
        protected abstract void UpdateEventVisibility();
    }

    /// <summary>
    /// The simplest version of an <see cref="KhemistryAdvancedISRUBase"/>.
    /// It behaves similarly to the stock and SystemHeat converters.
    /// </summary>
    public class KhemistryAdvancedISRU : KhemistryAdvancedISRUBase
    {
        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        private Animation _activeAnim;
        private string _activeAnimationName;
        private bool _animationPlaying = false;

        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryAdvancedISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;

            KShared.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryAdvancedISRU/SetupActiveAnimation");
        }

        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                statusDisplay = "ERROR: see log";
                return;
            }

            _displayName = _recipeGroup != null
                ? ConverterName + " (" + _recipeGroup + ")"
                : ConverterName;

            string startLabel = _recipeGroup != null
                ? StartActionName + " (" + _recipeGroup + ")"
                : StartActionName;
            string stopLabel = _recipeGroup != null
                ? StopActionName + " (" + _recipeGroup + ")"
                : StopActionName;

            Events["StartConverter"].guiName = startLabel;
            Events["StopConverter"].guiName = stopLabel;
            Actions["StartConverterAction"].guiName = startLabel;
            Actions["StopConverterAction"].guiName = stopLabel;

            Events["StartConverter"].unfocusedRange = _maxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _maxInteractionDistance;
            Events["ExecuteCycle"].unfocusedRange = _maxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            if (!chargingRequired)
                this.state = ConverterState.On;

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        public void FixedUpdate()
        {
            // Checks to prevent any null references
            if (!HighLogic.LoadedSceneIsFlight) return;  // Make sure we are in flight scene and not the VAB/SPH
            if (vessel == null || part == null) return;  // Make sure the vessel and part are not null
            if (_fatalConfigError) return;  // Make sure no config errors occured

            // Useful variables
            double dt = TimeWarp.fixedDeltaTime;
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

            // Charge, update UI, and output materials
            HandleCharging(dt);
            UpdateUI();
            TryTransferMaterialOutputBuffer();

            // Handle status display with manual operation
            // This normally returns if the converter is manual, but manualAuto will make it behave like automatic operation
            if (!_manualAuto && _manualOperation)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance"
                    : !isRunning ? "Stopped"
                    : "Waiting for manual cycle";
                UpdateEventVisibility();
                return;
            }

            // Handle status display with automatic operation
            // If manual with manualRequiresStartup=false, there is no Start/Stop button to ever set isRunning,
            // so treat it as always "running" for the purposes of the automatic/manualAuto cycle gate.
            bool effectivelyRunning = isRunning || (_manualOperation && !_manualRequiresStartup);
            if (!effectivelyRunning || needsMaintenance)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : "Stopped";
                UpdateEventVisibility();
                SetActiveAnimationPlaying(false);
                return;
            }

            // Show if the converter is disabled
            if (state != ConverterState.On)
            {
                statusDisplay = "Not ready";
                return;
            }

            // Run the converter, update the status display, and play animations
            RunOneCycle(part, dt);
            UpdateEventVisibility();
            SetActiveAnimationPlaying(effectivelyRunning);
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
                  groupName = "khemistryisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (!CheckRecipeGroup(part)) return;
            if (state != ConverterState.On) return;
            isRunning = true;
            KShared.Log("Converter \"" + _displayName + "\" started.", "KhemistryAdvancedISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Log("Converter \"" + _displayName + "\" stopped.", "KhemistryAdvancedISRU/StopConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistryisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance by an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Log("Converter \"" + _displayName + "\" maintained by " + kerbal.name + ".",
                "KhemistryAdvancedISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + _displayName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Execute Cycle",
                  groupName = "khemistryisru")]
        public void ExecuteCycle()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (!_manualRequiresStartup)
                if (!CheckRecipeGroup(part)) return;

            RunOneCycle(part, TimeWarp.fixedDeltaTime);
            UpdateEventVisibility();

            if (IsCurrentlyActive)
                PlayActiveAnimationOnce();
        }

        [KSPAction("Start Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StartConverterAction(KSPActionParam param)
        {
            StartConverter();
        }

        [KSPAction("Stop Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StopConverterAction(KSPActionParam param)
        {
            StopConverter();
        }

        protected override void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryAdvancedISRU/LoadConfigFromPartInfo");
            ConfigNode moduleNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryAdvancedISRU");
            if (moduleNode == null) { _fatalConfigError = true; return; }
            LoadSharedConfig(moduleNode, "KhemistryAdvancedISRU");
        }

        protected override void UpdateEventVisibility()
        {
            bool startStopEnabled = !_manualOperation || _manualRequiresStartup;

            ApplyShowRule(Events["StartConverter"],
                showPAW: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowPAW,
                showEVA: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: startStopEnabled && isRunning && _startStopShowPAW,
                showEVA: startStopEnabled && isRunning && _startStopShowEVA);

            bool cycleEnabled = _manualOperation && !needsMaintenance && (!_manualRequiresStartup || isRunning);

            ApplyShowRule(Events["ExecuteCycle"],
                showPAW: cycleEnabled && _manualShowPAW,
                showEVA: cycleEnabled && _manualShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = showPAW;
            ev.guiActiveUnfocused = showEVA;
            ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }
    }

    /// <summary>
    /// An <see cref="KhemistryAdvancedISRUBase"/> that uses KHEMISTRY_RECIPE recipes, see config at the top of source.
    ///
    /// The module's own MODULE config may define any field a normal <see cref="KhemistryAdvancedISRU"/>
    /// would (conditions, powerfail, manual operation, show rules,
    /// maxInteractionDistance, charging...). If present, that value overrides the same
    /// field on every loaded recipe wholesale. ConverterName, StartActionName, and
    /// StopActionName are NOT overridable — each recipe must define its own, since they
    /// exist specifically to differentiate between recipes. Two further exceptions:
    ///   - INPUT_RESOURCE / OUTPUT_RESOURCE nodes on the module are ADDED to whichever
    ///     recipe is active (after the recipe's own ratios have been scaled by
    ///     `multiplier`). If a module resource shares a ResourceName with one already
    ///     present on the recipe, the ratios are summed and FlowMode/DumpExcess are
    ///     taken from the module's entry.
    ///   - CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS entries from the recipe and the module
    ///     are concatenated (recipe's first, then the module's). Duplicate names are
    ///     skipped (first occurrence wins) with a logged warning.
    ///
    /// The module can also have RECIPES and RECIPE_MULTIPLIERS nodes to only load
    /// specific recipes of type resourceType, as well as multiply the inputs and outputs
    /// of each by something. Both are optional, however if RECIPE_MULTIPLIERS is
    /// present, RECIPES must be too.
    ///
    /// Only one recipe runs at a time; recipeGroup is intentionally never read or
    /// checked, since recipes belonging to this module can never run concurrently.
    /// </summary>
    public class KhemistryAdvancedRecipeISRU : KhemistryAdvancedISRUBase
    {
        [KSPField(isPersistant = false)]
        public string recipeType = null;

        [KSPField(isPersistant = false)]
        public float multiplier = 1f;

        [KSPField(isPersistant = true)]
        public string activeRecipeName = null;

        public List<string> allowedRecipes = new List<string>();
        public List<float> multiplierRecipes = new List<float>();

        public List<AdvancedISRURecipeCondition> rConditions = new List<AdvancedISRURecipeCondition>();

        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        private Animation _activeAnim;
        private string _activeAnimationName;
        private bool _animationPlaying = false;

        private readonly List<KhemistryRecipe> _recipes = new List<KhemistryRecipe>();
        private KhemistryRecipe _activeRecipe = null;

        private string _ovPlanetCondition, _ovBiomeCondition, _ovDepositCondition;
        private string _ovPowerfailResource, _ovPowerfailResultRaw;
        private string _ovSituationConditionRaw;
        private string _ovStartStopShowRulesRaw, _ovManualShowRulesRaw;
        private double? _ovAltMin, _ovAltMax;
        private float? _ovMaxInteractionDistance;
        private bool? _ovManualOperation, _ovManualRequiresStartup, _ovChargingRequired;
        private float? _ovChargeRate, _ovChargeDecayRate;

        private readonly List<ResourceInput> _extraInputs = new List<ResourceInput>();
        private readonly List<ResourceOutput> _extraOutputs = new List<ResourceOutput>();
        private readonly List<string> _ownChargeNames = new List<string>();
        private readonly List<float> _ownChargeAmounts = new List<float>();

        // ── Config loading: own module config is looked up the same robust way
        //    KhemistryAdvancedISRU does, via partInfo.partConfig / GameDatabase,
        //    NOT via OnLoad's node. OnLoad's node is only the full original .cfg text
        //    the first time a part is cloned fresh from its prefab — on any later
        //    reload (quicksave/quickload, scene switch, revert) KSP instead passes in
        //    the *persisted* module snapshot, which only contains isPersistant=true
        //    KSPFields. Custom fields like chargingRequired's override, the extra
        //    INPUT_RESOURCE/OUTPUT_RESOURCE, and CHARGE_CON_NAMES/AMOUNTS are not
        //    persistent, so reading them from OnLoad's node would silently revert them
        //    to "not specified" on every reload. ───────────────────────────────────────

        private ConfigNode FindRecipeModuleConfigNode()
        {
            ConfigNode result = null;

            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != "KhemistryAdvancedRecipeISRU") continue;
                    if (n.GetValue("recipeType") == recipeType) { result = n; break; }
                }
            }

            if (result != null) return result;

            string targetPartName = part.partInfo?.name ?? part.name;
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string nodeName = partNode.GetValue("name") ?? "";
                int slash = nodeName.LastIndexOf('/');
                if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") != "KhemistryAdvancedRecipeISRU") continue;
                    if (n.GetValue("recipeType") == recipeType) { result = n; break; }
                }
                if (result != null) break;
            }

            if (result == null)
                KShared.LogError(
                    "Could not find MODULE KhemistryAdvancedRecipeISRU with recipeType=\"" + recipeType
                    + "\" in partConfig or GameDatabase!",
                    "KhemistryAdvancedRecipeISRU/FindRecipeModuleConfigNode");

            return result;
        }

        private void LoadOwnModuleConfig(ConfigNode node)
        {
            multiplier = KShared.GetFloatValueFromCFG(node, "multiplier", 1f);

            if (node.HasNode("RECIPES"))
            {
                if (!node.GetNode("RECIPES").HasValue("name"))
                    KShared.LogError(
                            "KhemistryAdvancedRecipeISRU: Node RECIPES is present but no \"name\" values inside, skipping node.",
                            "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
                else
                    allowedRecipes = node.GetNode("RECIPES").GetValues("name").ToList();
                if (node.HasNode("RECIPE_MULTIPLIERS"))
                {
                    if (!node.GetNode("RECIPE_MULTIPLIERS").HasValue("amount"))
                        KShared.LogError(
                                "KhemistryAdvancedRecipeISRU: Node RECIPE_MULTIPLIERS is present but no \"amount\" values inside, skipping node.",
                                "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
                    else
                    {
                        multiplierRecipes.Clear();
                        foreach (string recipe in node.GetNode("RECIPE_MULTIPLIERS").GetValues("amount"))
                            multiplierRecipes.Add(float.Parse(recipe));
                        if (allowedRecipes.Count != multiplierRecipes.Count)
                        {
                            KShared.LogError(
                                "KhemistryAdvancedRecipeISRU: RECIPE and RECIPE_MULTIPLIERS nodes have unequal amounts of \"name\" and \"amount\" values respectively (" + allowedRecipes.Count.ToString() + ", " + multiplierRecipes.Count.ToString() + "), reverting to skip those nodes.",
                                "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
                            allowedRecipes.Clear();
                            multiplierRecipes.Clear();
                        }
                    }
                }
            }
            else if (node.HasNode("RECIPE_MULTIPLIERS"))
            {
                KShared.LogError(
                            "KhemistryAdvancedRecipeISRU: Node RECIPE_MULTIPLIERS is present but no RECIPES node is present.",
                            "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
            }

            ConfigNode conditionsNode = node.GetNode("CONDITIONS");
            if (conditionsNode != null)
            {
                string[] conditions = conditionsNode.GetValues("condition");
                for (int i = 0; i < conditions.Length; i++)
                {
                    string conditionStr = conditions[i];
                    if (!string.IsNullOrEmpty(conditionStr))
                        rConditions.Add(new AdvancedISRURecipeCondition(conditionStr));
                }
            }

            _ovPlanetCondition = KShared.GetStrValueFromCFG(node, "planetCondition", null);
            _ovBiomeCondition = KShared.GetStrValueFromCFG(node, "biomeCondition", null);
            _ovDepositCondition = KShared.GetStrValueFromCFG(node, "depositCondition", null);
            _ovPowerfailResource = KShared.GetStrValueFromCFG(node, "powerfailResource", null);
            _ovPowerfailResultRaw = KShared.GetStrValueFromCFG(node, "powerfailResult", null);
            _ovSituationConditionRaw = KShared.GetStrValueFromCFG(node, "situationCondition", null);
            _ovStartStopShowRulesRaw = KShared.GetStrValueFromCFG(node, "startStopShowRules", null);
            _ovManualShowRulesRaw = KShared.GetStrValueFromCFG(node, "manualShowRules", null);

            _ovAltMin = node.HasValue("altitudeMinCondition")
                ? (double?)KShared.GetFloatValueFromCFG(node, "altitudeMinCondition", 0f) : null;
            _ovAltMax = node.HasValue("altitudeMaxCondition")
                ? (double?)KShared.GetFloatValueFromCFG(node, "altitudeMaxCondition", 0f) : null;
            _ovMaxInteractionDistance = node.HasValue("maxInteractionDistance")
                ? (float?)KShared.GetFloatValueFromCFG(node, "maxInteractionDistance", 10f) : null;

            _ovManualOperation = ParseNullableBool(node, "manualOperation");
            _ovManualRequiresStartup = ParseNullableBool(node, "manualRequiresStartup");
            _ovChargingRequired = ParseNullableBool(node, "chargingRequired");

            _ovChargeRate = node.HasValue("chargeRate")
                ? (float?)KShared.GetFloatValueFromCFG(node, "chargeRate", 0f) : null;
            _ovChargeDecayRate = node.HasValue("chargeDecayRate")
                ? (float?)KShared.GetFloatValueFromCFG(node, "chargeDecayRate", 0f) : null;

            _extraInputs.Clear();
            foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
            {
                string resName = inputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double.TryParse(inputNode.GetValue("Ratio"), out double ratio);

                ResourceFlowMode flowMode = ResourceFlowMode.ALL_VESSEL;
                string flowStr = inputNode.GetValue("FlowMode");
                if (!string.IsNullOrEmpty(flowStr))
                {
                    if (Enum.TryParse(flowStr.Trim(), true, out ResourceFlowMode parsed))
                        flowMode = parsed;
                    else
                        KShared.LogError(
                            "KhemistryAdvancedRecipeISRU: Unknown FlowMode \"" + flowStr + "\" for " + resName + ", defaulting to ALL_VESSEL.",
                            "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
                }

                _extraInputs.Add(new ResourceInput { resourceName = resName, ratio = ratio, flowMode = flowMode });
            }

            _extraOutputs.Clear();
            foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
            {
                string resName = outputNode.GetValue("ResourceName");
                if (string.IsNullOrEmpty(resName)) continue;

                double.TryParse(outputNode.GetValue("Ratio"), out double ratio);

                bool.TryParse(outputNode.GetValue("DumpExcess"), out bool dumpExcess);

                _extraOutputs.Add(new ResourceOutput { resourceName = resName, ratio = ratio, dumpExcess = dumpExcess });
            }

            _ownChargeNames.Clear();
            _ownChargeAmounts.Clear();
            if (node.HasNode("CHARGE_CON_NAMES"))
                foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                    _ownChargeNames.Add(n.Trim());
            if (node.HasNode("CHARGE_CON_AMOUNTS"))
                foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                { if (float.TryParse(a, out float tmp)) _ownChargeAmounts.Add(tmp); }
            if (_ownChargeNames.Count != _ownChargeAmounts.Count)
                KShared.LogError(
                    "KhemistryAdvancedRecipeISRU: CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                    "KhemistryAdvancedRecipeISRU/LoadOwnModuleConfig");
        }

        private static bool? ParseNullableBool(ConfigNode node, string key)
        {
            if (!node.HasValue(key)) return null;
            return bool.TryParse(node.GetValue(key), out bool b) ? (bool?)b : null;
        }

        protected override void LoadConfigFromPartInfo()
        {
            if (string.IsNullOrEmpty(recipeType))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has a KhemistryAdvancedRecipeISRU with no recipeType set!",
                    "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode ownModuleNode = FindRecipeModuleConfigNode();
            if (ownModuleNode == null)
            {
                _fatalConfigError = true;
                return;
            }
            LoadOwnModuleConfig(ownModuleNode);

            var shared = KShared.Instance;
            if (shared == null || !shared.recipeDict.TryGetValue(recipeType, out List<KhemistryRecipe> recipeList) || recipeList.Count == 0)
            {
                string availableKeys = (shared != null && shared.recipeDict.Count > 0)
                    ? string.Join(", ", shared.recipeDict.Keys.Select(k => "\"" + k + "\""))
                    : "(none loaded)";
                KShared.LogError(
                    "No KHEMISTRY_RECIPE entries found for recipeType \"" + recipeType + "\" (length "
                    + (recipeType?.Length ?? -1) + ")! Available recipeType keys: " + availableKeys,
                    "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            List<KhemistryRecipe> tmpRecipes = new List<KhemistryRecipe>();
            _recipes.Clear();

            if (allowedRecipes.Count == 0)
                tmpRecipes.AddRange(recipeList);
            else
                foreach (KhemistryRecipe recipe in recipeList)
                    if (allowedRecipes.Contains(recipe.ConverterName))
                        tmpRecipes.Add(recipe);

            bool failed = false;
            foreach (KhemistryRecipe recipe in tmpRecipes)
            {
                if (recipe.mainNode == null)
                {
                    KShared.LogError(
                            "mainNode is null for recipe \"" + recipe.ConverterName + "\", skipping recipe.",
                            "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                    continue;
                }
                failed = false;
                foreach (AdvancedISRURecipeCondition condition in rConditions)
                {
                    if (!condition.CheckCondition(recipe.mainNode))
                    {
                        failed = true;
                        break;
                    }
                }
                if (!failed)
                    _recipes.Add(recipe);
            }

            if (multiplierRecipes.Count > 0)
                foreach (KhemistryRecipe recipe in _recipes)
                {
                    if (allowedRecipes.Contains(recipe.ConverterName))
                    {
                        if (allowedRecipes.Count != multiplierRecipes.Count)
                        {
                            KShared.Log(
                                "allowedRecipes amount is not equal to the multiplierRecipes amount (" + allowedRecipes.Count.ToString() + ", " + multiplierRecipes.Count.ToString() + "), skipping recipe multiplication.",
                                "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                            break;
                        }

                        recipe._inputs = recipe._inputs
                            .Select(inp => new KhemistryRecipe.ResourceInput
                            {
                                resourceName = inp.resourceName,
                                ratio = inp.ratio * multiplierRecipes[allowedRecipes.IndexOf(recipe.ConverterName)],
                                flowMode = inp.flowMode
                            })
                            .ToList();
                    }
                    else
                        KShared.Log(
                            recipe.ConverterName.ToString() + " was not found in allowedRecipes but is in multiplierRecipes, skipping this recipe.",
                            "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
                }

            KhemistryRecipe initial = null;
            if (!string.IsNullOrEmpty(activeRecipeName))
            {
                foreach (KhemistryRecipe r in _recipes)
                    if (r.ConverterName == activeRecipeName) { initial = r; break; }
            }
            if (initial == null) initial = _recipes[0];

            ApplyRecipe(initial);
            activeRecipeName = _activeRecipe.ConverterName;

            KShared.Log(
                "Loaded " + _recipes.Count + " recipe(s) for recipeType \"" + recipeType + "\", active: \"" + _activeRecipe.ConverterName + "\".",
                "KhemistryAdvancedRecipeISRU/LoadConfigFromPartInfo");
        }

        private void ApplyRecipe(KhemistryRecipe recipe)
        {
            _activeRecipe = recipe;

            ConverterName = recipe.ConverterName;
            StartActionName = recipe.StartActionName ?? ("Start " + ConverterName);
            StopActionName = recipe.StopActionName ?? ("Stop " + ConverterName);

            _planetCondition = _ovPlanetCondition ?? recipe._planetCondition;
            _biomeCondition = _ovBiomeCondition ?? recipe._biomeCondition;
            _altMin = _ovAltMin ?? recipe._altMin;
            _altMax = _ovAltMax ?? recipe._altMax;

            string sitRaw = _ovSituationConditionRaw ?? recipe._situationCondition.ToString();
            _situationCondition = KShared.SituationCondition.Any;
            if (sitRaw != null)
            {
                if (Enum.TryParse(sitRaw, true, out KShared.SituationCondition parsedSit))
                    _situationCondition = parsedSit;
                else
                    KShared.LogError(
                        "Converter \"" + recipe.ConverterName + "\": Unknown situationCondition \"" + sitRaw + "\" — condition ignored.",
                        "KhemistryAdvancedRecipeISRU/ApplyRecipe");
            }

            _depositCondition = _ovDepositCondition ?? recipe._depositCondition;

            _manualOperation = _ovManualOperation ?? recipe._manualOperation;
            _manualRequiresStartup = _ovManualRequiresStartup ?? recipe._manualRequiresStartup;

            if (_ovStartStopShowRulesRaw != null)
                KShared.ParseShowRule(_ovStartStopShowRulesRaw, out _startStopShowPAW, out _startStopShowEVA,
                    "startStopShowRules", "KhemistryAdvancedRecipeISRU");
            else
            {
                _startStopShowPAW = recipe._startStopShowPAW;
                _startStopShowEVA = recipe._startStopShowEVA;
            }

            if (_ovManualShowRulesRaw != null)
                KShared.ParseShowRule(_ovManualShowRulesRaw, out _manualShowPAW, out _manualShowEVA,
                    "manualShowRules", "KhemistryAdvancedRecipeISRU");
            else
            {
                _manualShowPAW = recipe._manualShowPAW;
                _manualShowEVA = recipe._manualShowEVA;
            }

            _maxInteractionDistance = _ovMaxInteractionDistance ?? recipe._maxInteractionDistance;

            _recipeGroup = null;

            chargingRequired = _ovChargingRequired ?? recipe.chargingRequired;
            chargeRate = _ovChargeRate ?? recipe.chargeRate;
            chargeDecayRate = _ovChargeDecayRate ?? recipe.chargeDecayRate;

            _chargeNames.Clear();
            _chargeAmounts.Clear();
            AddChargeEntries(recipe.ChargeNames, recipe.ChargeAmounts);
            AddChargeEntries(_ownChargeNames, _ownChargeAmounts);

            _inputs.Clear();
            var workingInputs = new List<ResourceInput>();
            foreach (KhemistryRecipe.ResourceInput ri in recipe._inputs)
                workingInputs.Add(new ResourceInput { resourceName = ri.resourceName, ratio = ri.ratio * multiplier, flowMode = ri.flowMode });
            foreach (ResourceInput extra in _extraInputs)
            {
                int idx = workingInputs.FindIndex(w => w.resourceName.Equals(extra.resourceName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    ResourceInput merged = workingInputs[idx];
                    merged.ratio += extra.ratio;
                    merged.flowMode = extra.flowMode;
                    workingInputs[idx] = merged;
                }
                else
                {
                    workingInputs.Add(extra);
                }
            }
            _inputs.AddRange(workingInputs);

            _outputs.Clear();
            var workingOutputs = new List<ResourceOutput>();
            foreach (KhemistryRecipe.ResourceOutput ro in recipe._outputs)
                workingOutputs.Add(new ResourceOutput { resourceName = ro.resourceName, ratio = ro.ratio * multiplier, dumpExcess = ro.dumpExcess });
            foreach (ResourceOutput extra in _extraOutputs)
            {
                int idx = workingOutputs.FindIndex(w => w.resourceName.Equals(extra.resourceName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    ResourceOutput merged = workingOutputs[idx];
                    merged.ratio += extra.ratio;
                    merged.dumpExcess = extra.dumpExcess;
                    workingOutputs[idx] = merged;
                }
                else
                {
                    workingOutputs.Add(extra);
                }
            }
            _outputs.AddRange(workingOutputs);

            if (_inputs.Count == 0 && _outputs.Count == 0)
                KShared.LogError(
                    "Converter \"" + ConverterName + "\" (recipe) has no INPUT_RESOURCE or OUTPUT_RESOURCE — it will do nothing.",
                    "KhemistryAdvancedRecipeISRU/ApplyRecipe");

            string pfResFinal = _ovPowerfailResource ?? recipe._powerfailResource;
            string pfResultRawFinal = _ovPowerfailResultRaw ?? CanonicalPowerfailResultRaw(recipe);
            ResolvePowerfail(pfResFinal, pfResultRawFinal);

            _displayName = ConverterName;

            if (Events["StartConverter"] != null) Events["StartConverter"].guiName = StartActionName;
            if (Events["StopConverter"] != null) Events["StopConverter"].guiName = StopActionName;
            if (Actions["StartConverterAction"] != null) Actions["StartConverterAction"].guiName = StartActionName;
            if (Actions["StopConverterAction"] != null) Actions["StopConverterAction"].guiName = StopActionName;

            if (_activeAnim != null && !string.IsNullOrEmpty(_activeAnimationName))
                _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;
        }

        private void AddChargeEntries(List<string> names, List<float> amounts)
        {
            int count = Math.Min(names.Count, amounts.Count);
            for (int i = 0; i < count; i++)
            {
                string name = names[i].Trim();
                bool duplicate = false;
                foreach (string existing in _chargeNames)
                    if (existing.Equals(name, StringComparison.OrdinalIgnoreCase)) { duplicate = true; break; }

                if (duplicate)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": Duplicate charge resource \"" + name + "\" ignored (already defined).",
                        "KhemistryAdvancedRecipeISRU/ApplyRecipe");
                    continue;
                }
                _chargeNames.Add(name);
                _chargeAmounts.Add(amounts[i]);
            }
        }

        private static string CanonicalPowerfailResultRaw(KhemistryRecipe recipe)
        {
            switch (recipe._powerfailResult)
            {
                case KhemistryRecipe.PowerfailResult.Pause: return "PAUSE";
                case KhemistryRecipe.PowerfailResult.Stop: return "STOP";
                case KhemistryRecipe.PowerfailResult.Void: return "VOID";
                case KhemistryRecipe.PowerfailResult.Maint: return "MAINT";
                case KhemistryRecipe.PowerfailResult.Explode:
                    return "EXPLODE," + recipe._powerfailExplosionRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "," + recipe._powerfailExplosionTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
                default: return null;
            }
        }

        private void ResolvePowerfail(string pfRes, string pfResultRaw)
        {
            _powerfailResource = null;
            _powerfailResult = PowerfailResult.Pause;
            _powerfailExplosionRadius = 0f;
            _powerfailExplosionTemperature = 0f;

            if (pfRes != null)
            {
                bool found = false;
                foreach (ResourceInput inp in _inputs)
                    if (inp.resourceName.Equals(pfRes, StringComparison.OrdinalIgnoreCase)) { found = true; break; }

                if (!found)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": powerfailResource \"" + pfRes + "\" is not a defined INPUT_RESOURCE — powerfail disabled.",
                        "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                }
                else
                {
                    _powerfailResource = pfRes;
                    if (pfResultRaw != null)
                    {
                        string pfResult = pfResultRaw.Trim().Trim('"').ToUpper();
                        if (pfResult == "PAUSE")
                        {
                            _powerfailResult = PowerfailResult.Pause;
                        }
                        else if (pfResult == "STOP")
                        {
                            _powerfailResult = PowerfailResult.Stop;
                        }
                        else if (pfResult == "VOID")
                        {
                            _powerfailResult = PowerfailResult.Void;
                        }
                        else if (pfResult == "MAINT")
                        {
                            _powerfailResult = PowerfailResult.Maint;
                        }
                        else if (pfResult.StartsWith("EXPLODE,"))
                        {
                            string[] parts = pfResult.Substring(8).Split(',');
                            if (parts.Length == 2
                                && float.TryParse(parts[0], out float radius)
                                && float.TryParse(parts[1], out float tempC))
                            {
                                _powerfailResult = PowerfailResult.Explode;
                                _powerfailExplosionRadius = radius;
                                _powerfailExplosionTemperature = tempC;
                            }
                            else
                            {
                                KShared.LogError(
                                    "Converter \"" + ConverterName + "\": Could not parse EXPLODE radius/temperature \"" + pfResultRaw + "\" (expected EXPLODE,radiusMeters,tempCelsius) — defaulting to PAUSE.",
                                    "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                                _powerfailResult = PowerfailResult.Pause;
                            }
                        }
                        else
                        {
                            KShared.LogError(
                                "Converter \"" + ConverterName + "\": Unknown powerfailResult \"" + pfResultRaw + "\" — defaulting to PAUSE.",
                                "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
                            _powerfailResult = PowerfailResult.Pause;
                        }
                    }
                }
            }
            else if (pfResultRaw != null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": powerfailResult set without powerfailResource — powerfailResult ignored.",
                    "KhemistryAdvancedRecipeISRU/ResolvePowerfail");
            }
        }

        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryAdvancedRecipeISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = _manualOperation ? WrapMode.Once : WrapMode.Loop;

            KShared.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryAdvancedRecipeISRU/SetupActiveAnimation");
        }

        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                statusDisplay = "ERROR: see log";
                return;
            }

            Events["StartConverter"].unfocusedRange = _maxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _maxInteractionDistance;
            Events["SwitchRecipe"].unfocusedRange = _maxInteractionDistance;
            Events["ExecuteCycle"].unfocusedRange = _maxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            if (!chargingRequired)
                this.state = ConverterState.On;

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

            HandleCharging(dt);
            UpdateUI();

            if (!_manualAuto && _manualOperation)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance"
                    : !isRunning ? "Stopped"
                    : "Waiting for manual cycle";
                UpdateEventVisibility();
                return;
            }

            // If manual with manualRequiresStartup=false, there is no Start/Stop button to ever set isRunning,
            // so treat it as always "running" for the purposes of the automatic/manualAuto cycle gate.
            bool effectivelyRunning = isRunning || (_manualOperation && !_manualRequiresStartup);
            if (!effectivelyRunning || needsMaintenance)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : "Stopped";
                UpdateEventVisibility();
                SetActiveAnimationPlaying(false);
                return;
            }

            if (state != ConverterState.On)
            {
                statusDisplay = "Not ready";
                return;
            }

            RunOneCycle(part, dt);
            UpdateEventVisibility();
            SetActiveAnimationPlaying(effectivelyRunning);
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Switch Recipe",
                  groupName = "khemistryisru")]
        public void SwitchRecipe()
        {
            if (isRunning)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Stop the converter before switching recipes.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (_recipes.Count <= 1)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": No other recipes available to switch to.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null) return;

            var labels = new List<string>();
            foreach (KhemistryRecipe r in _recipes)
                labels.Add(r.ConverterName + (r == _activeRecipe ? " [Active]" : ""));

            shared.ShowSelector("Switch Recipe", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx < 0) return;
                if (_recipes[idx] == _activeRecipe) return;

                ApplyRecipe(_recipes[idx]);
                activeRecipeName = _activeRecipe.ConverterName;
                UpdateEventVisibility();
                UpdateUI();

                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Switched to recipe \"" + _displayName + "\".", 5f, ScreenMessageStyle.UPPER_CENTER));
                KShared.Log("Switched active recipe to \"" + _displayName + "\".",
                    "KhemistryAdvancedRecipeISRU/SwitchRecipe");
            });
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
                  groupName = "khemistryisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (state != ConverterState.On) return;
            isRunning = true;
            KShared.Log("Converter \"" + _displayName + "\" started.", "KhemistryAdvancedRecipeISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Log("Converter \"" + _displayName + "\" stopped.", "KhemistryAdvancedRecipeISRU/StopConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistryisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = null;
            var crew = FlightGlobals.ActiveVessel?.GetVesselCrew();
            if (crew != null)
                foreach (ProtoCrewMember c in crew) { kerbal = c; break; }
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Maintenance requires an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Log("Converter \"" + _displayName + "\" maintained by " + kerbal.name + ".",
                "KhemistryAdvancedRecipeISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + _displayName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Execute Cycle",
                  groupName = "khemistryisru")]
        public void ExecuteCycle()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + _displayName + "\": Requires maintenance.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            RunOneCycle(part, TimeWarp.fixedDeltaTime);
            UpdateEventVisibility();

            if (IsCurrentlyActive)
                PlayActiveAnimationOnce();
        }

        [KSPAction("Start Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StartConverterAction(KSPActionParam param) => StartConverter();

        [KSPAction("Stop Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StopConverterAction(KSPActionParam param) => StopConverter();

        protected override void UpdateEventVisibility()
        {
            bool startStopEnabled = !_manualOperation || _manualRequiresStartup;

            ApplyShowRule(Events["StartConverter"],
                showPAW: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowPAW,
                showEVA: startStopEnabled && !isRunning && !needsMaintenance && _startStopShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: startStopEnabled && isRunning && _startStopShowPAW,
                showEVA: startStopEnabled && isRunning && _startStopShowEVA);

            ApplyShowRule(Events["SwitchRecipe"], showPAW: _startStopShowPAW, showEVA: _startStopShowEVA);

            bool cycleEnabled = _manualOperation && !needsMaintenance && (!_manualRequiresStartup || isRunning);

            ApplyShowRule(Events["ExecuteCycle"],
                showPAW: cycleEnabled && _manualShowPAW,
                showEVA: cycleEnabled && _manualShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = showPAW;
            ev.guiActiveUnfocused = showEVA;
            ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }
    }

    /// <summary>
    /// A version of <see cref="KhemistryAdvancedISRUBase"/> that runs on a kerbal.
    /// Does not use the stock resource system and instead uses fluid cells.
    /// </summary>
    public class KhemistryEVAAdvancedISRU : KhemistryAdvancedISRUBase
    {
        [KSPField(isPersistant = false)]
        public bool useSuitCell = false;

        public bool UseSuitCell => useSuitCell;

        public HashSet<string> SupportedResources = new HashSet<string>();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            SupportedResources.Clear();
            if (node.HasNode("SUPPORTED_RESOURCES"))
            {
                foreach (string name in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                    SupportedResources.Add(name.Trim());
                KShared.Log(
                    "Loaded " + SupportedResources.Count + " allowed resources.",
                    "KhemistryEVAAdvancedISRU/OnLoad");
            }
            else
            {
                KShared.Log(
                    "Part \"" + part.name + "\" has KhemistryEVAAdvancedISRU with no SUPPORTED_RESOURCES node " +
                    "(OK for kerbal-native modules; inventory-item modules won't accept any resource).",
                    "KhemistryEVAAdvancedISRU/OnLoad");
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                statusDisplay = "ERROR: see log";
                return;
            }

            _displayName = _recipeGroup != null
                ? ConverterName + " (" + _recipeGroup + ")"
                : ConverterName;
        }

        protected override void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryEVAAdvancedISRU/LoadConfigFromPartInfo");
            ConfigNode moduleNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryEVAAdvancedISRU");
            if (moduleNode == null) { _fatalConfigError = true; return; }
            LoadSharedConfig(moduleNode, "KhemistryEVAAdvancedISRU");

            if (_outputMaterials.Count > 0)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\" (KhemistryEVAAdvancedISRU) defines OUTPUT_RESOURCE_MATERIAL, "
                    + "which is not supported for EVA converters — EVA output goes to fluid cells, not vessel "
                    + "KhemistryMaterialStorage modules. This module will not load.",
                    "KhemistryEVAAdvancedISRU/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            if (bool.TryParse(moduleNode.GetValue("useSuitCell"), out bool tmpB))
                useSuitCell = tmpB;
        }

        protected override void UpdateEventVisibility() { }

        public bool IsConfigLoaded => !_fatalConfigError;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? ConverterName : _displayName;

        public bool IsManual => _manualOperation;

        public bool ManualRequiresStartup => _manualRequiresStartup;
    }

    ////////////////////////////// Kerbal-side Logic //////////////////////////////

    /// <summary>
    /// A <see cref="PartModule"/> applied to kerbals, it handles all EVA-side logic and rendering.
    /// </summary>
    public class KhemistryKerbal : PartModule
    {
        ///// Occupation System /////

        // Current occupation of the kerbal, null if none
        public string occupation = null;  // Apparently it gets set to "" if i don't do this
        // Can the kerbal get occupied
        public bool canBeOccupied = true;
        // Is the kerbal frozen (cannot move)
        public bool kerbalFrozen = false;
        // String to show the kerbal's current occupation
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Current occupation")]
        public string OccupationString = "Free";

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Leave current occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void LeaveOccupation() => occupation = null;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void DisableOccupation()
        {
            occupation = null;
            canBeOccupied = false;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EnableOccupation() => canBeOccupied = true;


        // Serialized as "ResA:1.5000|ResB:2.0000" — same format as KhemistryEVACombinedProcessor
        [KSPField(isPersistant = true)]
        public string suitCellResourcesData = "";

        private float _suitCellMaxAmount = 0f;
        private float _suitCellTransferDistance = 10f;
        private readonly HashSet<string> _suitCellAllowedResources = new HashSet<string>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is clearly used elsewhere in the code and shouldn't be readonly")]
        private HashSet<string> FluidCellPartNames = new HashSet<string>();
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is clearly used elsewhere in the code and shouldn't be readonly")]
        private HashSet<string> _evaISRUPartNames = new HashSet<string>();

        private ModuleInventoryPart _inventory;
        private KerbalEVA eva;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells available";

        private struct FluidCellRef
        {
            public bool isSuit;
            public StoredPart stored;
        }

        private struct ISRUHandle
        {
            public bool isLive;
            public KhemistryEVAAdvancedISRU liveModule;
            public StoredPart stored;
            public KhemistryEVAAdvancedISRU prefab;

            public KhemistryEVAAdvancedISRU Config => isLive ? liveModule : prefab;
            public string ConverterName => Config.ConverterName;
            public string DisplayName => Config.DisplayName;
        }

        private Dictionary<string, double> GetSuitCellDict()
    => KhemistryEVACombinedProcessor.Deserialize(suitCellResourcesData);

        private void SetSuitCellFromDict(Dictionary<string, double> dict)
    => suitCellResourcesData = KhemistryEVACombinedProcessor.Serialize(dict);

        private void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryKerbal/LoadConfigFromPartInfo");
            FluidCellPartNames.Clear();
            _evaISRUPartNames.Clear();
            _suitCellMaxAmount = 0f;
            _suitCellTransferDistance = 10f;
            _suitCellAllowedResources.Clear();

            ConfigNode moduleNode = null;

            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                }
            }

            if (moduleNode == null)
            {
                string targetPartName = part.partInfo?.name ?? part.name;
                foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
                {
                    string nodeName = partNode.GetValue("name") ?? "";
                    int slash = nodeName.LastIndexOf('/');
                    if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                    if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                    {
                        if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                    }
                    if (moduleNode != null) break;
                }
            }

            if (moduleNode == null)
            {
                KShared.LogError(
                    "Could not find KhemistryKerbal MODULE node for part \"" + part.name + "\".",
                    "KhemistryKerbal/LoadConfigFromPartInfo");
                return;
            }

            if (moduleNode.HasNode("FLUID_CELL_PARTS"))
                foreach (string name in moduleNode.GetNode("FLUID_CELL_PARTS").GetValues("name"))
                    FluidCellPartNames.Add(name.Trim());

            if (moduleNode.HasNode("EVA_ISRU_PARTS"))
                foreach (string name in moduleNode.GetNode("EVA_ISRU_PARTS").GetValues("name"))
                    _evaISRUPartNames.Add(name.Trim());

            if (moduleNode.HasNode("SUIT_CELL"))
            {
                ConfigNode suitNode = moduleNode.GetNode("SUIT_CELL");
                if (float.TryParse(suitNode.GetValue("maxAmount"), out float tmp))
                    _suitCellMaxAmount = tmp;
                if (float.TryParse(suitNode.GetValue("transferDistance"), out tmp))
                    _suitCellTransferDistance = tmp;
                if (suitNode.HasNode("ALLOWED_RESOURCES"))
                    foreach (string n in suitNode.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                        _suitCellAllowedResources.Add(n.Trim());
            }

            KShared.Log(
                string.Format("Loaded {0} fluid cell part names, {1} EVA ISRU part names, suitCell={2}.",
                    FluidCellPartNames.Count, _evaISRUPartNames.Count, _suitCellMaxAmount > 0f),
                "KhemistryKerbal/LoadConfigFromPartInfo");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            eva = part.FindModuleImplementing<KerbalEVA>();

            var allHandlers = part.FindModulesImplementing<KhemistryKerbal>();
            if (allHandlers.Count > 1 && allHandlers[0] != this)
            {
                KShared.Log("Duplicate handler found, removing self.", "KhemistryKerbal/OnStart");
                return;
            }

            LoadConfigFromPartInfo();

            _inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (_inventory == null)
                KShared.LogError("No ModuleInventoryPart on Kerbal.", "KhemistryKerbal/OnStart");
            else
                KShared.Log("Inventory found.", "KhemistryKerbal/OnStart");

            KShared.Log("OnStart complete!", "KhemistryKerbal/OnStart");
        }

        public override void OnUpdate()
        {
            if (!string.IsNullOrEmpty(occupation))  // If not free
            {
                Events["LeaveOccupation"].active = true;
                OccupationString = occupation;  // Show occupation
                if (!kerbalFrozen && eva != null)  // If not frozen and have reference
                {
                    eva.strafeSpeed /= 100;  // Freeze kerbal
                    eva.walkSpeed /= 100;
                    eva.runSpeed /= 100;
                    eva.swimSpeed /= 100;
                    eva.ladderClimbSpeed /= 100;
                    kerbalFrozen = true;
                }
            }
            else
            {
                Events["LeaveOccupation"].active = false;
                OccupationString = "Free";
                if (kerbalFrozen && eva != null)
                {
                    eva.strafeSpeed *= 100;
                    eva.walkSpeed *= 100;
                    eva.runSpeed *= 100;
                    eva.swimSpeed *= 100;
                    eva.ladderClimbSpeed *= 100;
                    kerbalFrozen = false;
                }
            }

            UpdateFluidCellDisplay();

            Events["EnableOccupation"].active = !canBeOccupied;
            Events["DisableOccupation"].active = canBeOccupied;
        }

        private List<FluidCellRef> GetAllCellRefs()
        {
            var result = new List<FluidCellRef>();
            if (_suitCellMaxAmount > 0f)
                result.Add(new FluidCellRef { isSuit = true });
            foreach (StoredPart stored in GetHeldCellSnapshots())
                result.Add(new FluidCellRef { isSuit = false, stored = stored });
            return result;
        }

        private string GetCellLabel(FluidCellRef cell, int index)
            => cell.isSuit ? "Cell 0 (suit)" : string.Format("Cell {0}", index);

        private string ReadCellResourceName(FluidCellRef cell)
        {
            if (cell.isSuit)
            {
                var dict = GetSuitCellDict();
                if (dict.Count == 0) return "";
                var names = new List<string>();
                foreach (var kvp in dict) names.Add(kvp.Key);
                return string.Join(", ", names.ToArray());
            }
            return ReadResourceName(cell.stored);
        }

        private float ReadCellResourceAmount(FluidCellRef cell)
        {
            if (cell.isSuit)
                return (float)KhemistryEVACombinedProcessor.GetTotal(GetSuitCellDict());
            return ReadResourceAmount(cell.stored);
        }

        private float ReadCellMaxAmount(FluidCellRef cell)
            => cell.isSuit ? _suitCellMaxAmount : ReadMaxAmount(cell.stored.partName);

        private void UpdateFluidCellDisplay()
        {
            var cells = GetAllCellRefs();
            if (cells.Count == 0) { CellContentsDisplay = "No cells available"; return; }
            var parts = new List<string>();
            for (int i = 0; i < cells.Count; i++)
            {
                string label = GetCellLabel(cells[i], i);
                if (cells[i].isSuit)
                {
                    var dict = GetSuitCellDict();
                    double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                    if (dict.Count == 0)
                        parts.Add(string.Format("{0}: Empty (0/{1:F2})", label, _suitCellMaxAmount));
                    else
                    {
                        var cp = new List<string>();
                        foreach (var kvp in dict)
                            cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                        parts.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})",
                            label, string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                    }
                }
                else
                {
                    string resName = ReadResourceName(cells[i].stored);
                    float resAmount = ReadResourceAmount(cells[i].stored);
                    float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                    parts.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", label)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", label, resName, resAmount, maxAmount));
                }
            }
            CellContentsDisplay = string.Join("  |  ", parts.ToArray());
        }

        private List<ISRUHandle> GetAllISRUHandles()
        {
            var result = new List<ISRUHandle>();

            foreach (KhemistryEVAAdvancedISRU m in part.FindModulesImplementing<KhemistryEVAAdvancedISRU>())
            {
                if (!m.IsConfigLoaded) continue;
                result.Add(new ISRUHandle { isLive = true, liveModule = m });
            }

            foreach (StoredPart stored in GetEVAISRUSnapshots())
                foreach (KhemistryEVAAdvancedISRU prefab in GetPrefabISRUModules(stored))
                    if (prefab.IsConfigLoaded)
                        result.Add(new ISRUHandle { isLive = false, stored = stored, prefab = prefab });

            return result;
        }

        private bool ReadISRUBool(ISRUHandle h, string key)
        {
            if (h.isLive)
            {
                if (key == "isRunning") return h.liveModule.isRunning;
                if (key == "needsMaintenance") return h.liveModule.needsMaintenance;
                return false;
            }
            return ReadISRUBool(h.stored, h.Config.ConverterName, key);
        }

        private void WriteISRUBool(ISRUHandle h, string key, bool value)
        {
            if (h.isLive)
            {
                if (key == "isRunning") h.liveModule.isRunning = value;
                else if (key == "needsMaintenance") h.liveModule.needsMaintenance = value;
                return;
            }
            WriteISRUBool(h.stored, h.Config.ConverterName, key, value);
        }

        private List<StoredPart> GetEVAISRUSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;

            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                if (_evaISRUPartNames.Count > 0 && !_evaISRUPartNames.Contains(stored.partName))
                    continue;
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                if (ap == null) continue;
                if (ap.partPrefab.FindModuleImplementing<KhemistryEVAAdvancedISRU>() == null) continue;
                result.Add(stored);
            }
            return result;
        }

        private List<KhemistryEVAAdvancedISRU> GetPrefabISRUModules(StoredPart stored)
        {
            AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
            return ap?.partPrefab.FindModulesImplementing<KhemistryEVAAdvancedISRU>()
                ?? new List<KhemistryEVAAdvancedISRU>();
        }

        private ProtoPartModuleSnapshot GetISRUSnapshot(StoredPart stored, string converterName)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
            {
                if (moduleSnap.moduleName != "KhemistryEVAAdvancedISRU") continue;
                if (moduleSnap.moduleValues.GetValue("ConverterName") == converterName)
                    return moduleSnap;
            }
            return null;
        }

        private bool ReadISRUBool(StoredPart stored, string converterName, string key)
        {
            string val = GetISRUSnapshot(stored, converterName)?.moduleValues.GetValue(key);
            return val != null && bool.TryParse(val, out bool result) && result;
        }

        private void WriteISRUBool(StoredPart stored, string converterName, string key, bool value)
            => GetISRUSnapshot(stored, converterName)?.moduleValues.SetValue(key, value.ToString());

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Use Held Converter",
                  groupName = "evaisru", groupDisplayName = "EVA Converters", groupStartCollapsed = false,
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EVAUseConverter()
        {
            var shared = KShared.Instance;
            if (shared == null) return;
            KShared.Log("Called! (Use Held Converter button)", "KhemistryKerbal/EVAUseConverter");

            var options = GetAllISRUHandles();

            if (options.Count == 0)
            {
                KShared.Log("No EVA converters were found.", "KhemistryKerbal/EVAUseConverter");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No EVA converters available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var labels = new List<string>();
            foreach (ISRUHandle h in options)
            {
                bool running = ReadISRUBool(h, "isRunning");
                bool maint = ReadISRUBool(h, "needsMaintenance");
                string suffix = maint ? " [MAINT]" : running ? " [Running]" : " [Stopped]";
                labels.Add(h.DisplayName + suffix);
            }

            if (options.Count == 1)
                ShowConverterActionMenu(options[0]);
            else
                shared.ShowSelector("Select converter", labels, label =>
                {
                    int idx = labels.IndexOf(label);
                    if (idx >= 0) ShowConverterActionMenu(options[idx]);
                });
        }

        private void ShowConverterActionMenu(ISRUHandle handle)
        {
            var shared = KShared.Instance;
            bool running = ReadISRUBool(handle, "isRunning");
            bool maint = ReadISRUBool(handle, "needsMaintenance");

            var actions = new List<string>();

            if (maint)
            {
                actions.Add("Perform Maintenance");
            }
            else
            {
                bool startStopEnabled = !handle.Config.IsManual || handle.Config.ManualRequiresStartup;
                if (startStopEnabled)
                {
                    if (!running) actions.Add("Start");
                    else actions.Add("Stop");
                }
                if (handle.Config.IsManual && (!handle.Config.ManualRequiresStartup || running))
                    actions.Add("Execute Cycle");
            }

            if (actions.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No actions available for \"" + handle.DisplayName + "\".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (actions.Count == 1)
            {
                ExecuteConverterAction(handle, actions[0]);
                return;
            }

            shared.ShowSelector("Action: " + handle.DisplayName, actions,
                action => ExecuteConverterAction(handle, action));
        }

        private void ExecuteConverterAction(ISRUHandle handle, string action)
        {
            switch (action)
            {
                case "Start":
                    if (!handle.Config.CheckRecipeGroup(part)) return;
                    WriteISRUBool(handle, "isRunning", true);
                    KShared.Log("EVA converter \"" + handle.DisplayName + "\" started.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Stop":
                    WriteISRUBool(handle, "isRunning", false);
                    KShared.Log("EVA converter \"" + handle.DisplayName + "\" stopped.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\" stopped.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Execute Cycle":
                    if (!handle.Config.ManualRequiresStartup && !handle.Config.CheckRecipeGroup(part)) return;
                    if (handle.Config.UseSuitCell)
                    {
                        if (_suitCellMaxAmount > 0f)
                        {
                            var suitDict = GetSuitCellDict();
                            handle.Config.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, TimeWarp.fixedDeltaTime);
                            SetSuitCellFromDict(suitDict);
                        }
                        else
                        {
                            ScreenMessages.PostScreenMessage(new ScreenMessage(
                                "No suit cell configured for this kerbal.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        }
                    }
                    else
                    {
                        handle.Config.RunOneCycle(part, TimeWarp.fixedDeltaTime);
                    }
                    KShared.Log("EVA converter \"" + handle.DisplayName + "\" cycle executed.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    break;

                case "Perform Maintenance":
                    ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
                    if (kerbal == null || kerbal.trait != "Engineer")
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Maintenance requires an Engineer.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
                    WriteISRUBool(handle, "needsMaintenance", false);
                    KShared.Log("EVA converter \"" + handle.DisplayName + "\" maintained.",
                        "KhemistryKerbal/ExecuteConverterAction");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + handle.DisplayName + "\": Maintenance complete.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    break;
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;

            double dt = TimeWarp.fixedDeltaTime;

            foreach (KhemistryEVAAdvancedISRU liveISRU in part.FindModulesImplementing<KhemistryEVAAdvancedISRU>())
            {
                if (!liveISRU.IsConfigLoaded) continue;
                liveISRU.TickCooldown(dt);
                if (liveISRU.IsManual) continue;
                if (!liveISRU.isRunning || liveISRU.needsMaintenance) continue;

                if (liveISRU.UseSuitCell)
                {
                    if (_suitCellMaxAmount <= 0f) continue;
                    var suitDict = GetSuitCellDict();
                    liveISRU.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, dt);
                    SetSuitCellFromDict(suitDict);
                }
                else
                {
                    liveISRU.RunOneCycle(part, dt);
                }
            }

            foreach (StoredPart stored in GetEVAISRUSnapshots())
            {
                foreach (KhemistryEVAAdvancedISRU prefab in GetPrefabISRUModules(stored))
                {
                    if (!prefab.IsConfigLoaded) continue;
                    prefab.TickCooldown(dt);
                    if (prefab.IsManual) continue;

                    bool running = ReadISRUBool(stored, prefab.ConverterName, "isRunning");
                    bool maint = ReadISRUBool(stored, prefab.ConverterName, "needsMaintenance");
                    if (!running || maint) continue;

                    prefab.isRunning = running;
                    prefab.needsMaintenance = maint;

                    if (prefab.UseSuitCell)
                    {
                        if (_suitCellMaxAmount > 0f)
                        {
                            var suitDict = GetSuitCellDict();
                            prefab.RunOneCycleSuitCell(part, suitDict, _suitCellMaxAmount, dt);
                            SetSuitCellFromDict(suitDict);
                        }
                    }
                    else
                    {
                        prefab.RunOneCycle(part, dt);
                    }

                    WriteISRUBool(stored, prefab.ConverterName, "isRunning", prefab.isRunning);
                    WriteISRUBool(stored, prefab.ConverterName, "needsMaintenance", prefab.needsMaintenance);
                }
            }

            foreach (StoredPart stored in GetProcessorSnapshots())
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                if (prefab == null || !prefab.IsConfigLoaded) continue;

                bool running = ReadProcessorBool(stored, "isRunning");
                string converterName = ReadProcessorField(stored, "activeConverterName");
                if (!running || string.IsNullOrEmpty(converterName)) continue;

                var resources = DeserializeProcessorResources(stored);
                bool cycled = prefab.RunConversionCycle(resources, converterName, dt);
                WriteProcessorResources(stored, resources);

                if (!cycled)
                {
                    WriteProcessorField(stored, "isRunning", "False");
                    KShared.Log(
                        "Processor converter \"" + converterName + "\" stopped: insufficient inputs.",
                        "KhemistryKerbal/FixedUpdate");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + converterName + "\" stopped: insufficient inputs.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                }
            }
        }

        private List<StoredPart> GetHeldCellSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                if (FluidCellPartNames.Contains(stored.partName))
                    result.Add(stored);
            }
            return result;
        }

        private ProtoPartModuleSnapshot GetCellModuleSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
                if (moduleSnap.moduleName == "KhemistryFluidCell") return moduleSnap;
            return null;
        }

        private string ReadResourceName(StoredPart stored)
            => GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceName") ?? "";

        private float ReadResourceAmount(StoredPart stored)
        {
            string val = GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceAmount");
            return val != null ? float.Parse(val) : 0f;
        }

        private float ReadMaxAmount(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.ResourceMaxAmount ?? 100f;

        private float ReadTransferDistance(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.TransferDistance ?? 10f;

        private HashSet<string> ReadAllowedResources(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.AllowedResources
                ?? new HashSet<string>();

        private void WriteResourceName(StoredPart stored, string name)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceName", name);

        private void WriteResourceAmount(StoredPart stored, float amount)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceAmount", amount.ToString("F4"));

        private List<Part> GetPartsInRange(float range)
        {
            KShared.Log("Called with range " + range.ToString(), "KhemistryKerbal/GetPartsInRange");
            var result = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                foreach (Part p in v.parts)
                {
                    if (p == this.part) continue;
                    if (Vector3.Distance(this.part.transform.position, p.transform.position) <= range)
                        result.Add(p);
                }
            KShared.Log("Acquired " + result.Count.ToString() + " parts.", "KhemistryKerbal/GetPartsInRange");
            return result;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from cell to nearby part",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVASendResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVASendResources!"); return; }
            KShared.Log("Called! (Transfer from ... to nearby part button)", "KhemistryKerbal/EVASendResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForSend(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    if (cells[i].isSuit)
                    {
                        var dict = GetSuitCellDict();
                        double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                        if (dict.Count == 0)
                            labels.Add(string.Format("{0}: Empty (0/{1:F2})", cellLabel, _suitCellMaxAmount));
                        else
                        {
                            var cp = new List<string>();
                            foreach (var kvp in dict)
                                cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                            labels.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})", cellLabel,
                                string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                        }
                    }
                    else
                    {
                        string resName = ReadResourceName(cells[i].stored);
                        float resAmount = ReadResourceAmount(cells[i].stored);
                        float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                        labels.Add(string.IsNullOrEmpty(resName)
                            ? string.Format("{0}: Empty", cellLabel)
                            : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                    }
                }
                shared.ShowSelector("Which cell to send from?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForSend(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForSend(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForSend(); return; }

            string resourceName = ReadResourceName(cell.stored);
            float resourceAmount = ReadResourceAmount(cell.stored);
            float range = ReadTransferDistance(cell.stored.partName);

            if (string.IsNullOrEmpty(resourceName) || resourceAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is empty.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var targetParts = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!targetParts.ContainsKey(lbl)) targetParts.Add(lbl, p);
                    break;
                }

            if (targetParts.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                targetParts.Keys.ToList(), label =>
                {
                    Part target = targetParts[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;
                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;
                    float newAmount = resourceAmount - (float)pushed;
                    if (newAmount <= 0.001f) { WriteResourceName(cell.stored, ""); WriteResourceAmount(cell.stored, 0f); }
                    else WriteResourceAmount(cell.stored, newAmount);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} kg of {1}.", pushed, resourceName),
                        5.0f, ScreenMessageStyle.UPPER_CENTER));
                });
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from nearby part to cell",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVATakeResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVATakeResources!"); return; }
            KShared.Log("Called! (Transfer from ... to cell button)", "KhemistryKerbal/EVATakeResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForTake(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    string resName = ReadCellResourceName(cells[i]);
                    float resAmount = ReadCellResourceAmount(cells[i]);
                    float maxAmount = ReadCellMaxAmount(cells[i]);
                    labels.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", cellLabel)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                }
                shared.ShowSelector("Which cell to fill?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForTake(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForTake(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForTake(); return; }
            KShared.Log("Called!", "KhemistryKerbal/ShowPartSelectorForTake");

            string currentResource = ReadResourceName(cell.stored);
            float currentAmount = ReadResourceAmount(cell.stored);
            float maxAmount = ReadMaxAmount(cell.stored.partName);
            float range = ReadTransferDistance(cell.stored.partName);
            HashSet<string> allowed = ReadAllowedResources(cell.stored.partName);

            if (currentAmount >= maxAmount)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is full.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            float spaceRemaining = maxAmount - currentAmount;
            var optionParts = new Dictionary<string, Part>();
            var optionResources = new Dictionary<string, string>();

            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0) continue;
                    if (!string.IsNullOrEmpty(currentResource) && pr.resourceName != currentResource) continue;
                    if (string.IsNullOrEmpty(currentResource) && allowed.Count > 0
                        && !allowed.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!optionParts.ContainsKey(lbl))
                    {
                        optionParts.Add(lbl, p);
                        optionResources.Add(lbl, pr.resourceName);
                    }
                }

            if (optionParts.Count == 0)
            {
                KShared.Log("No nearby parts with resource " + currentResource + " were detected.", "KhemistryKerbal/ShowPartSelectorForTake");
                string msg = string.IsNullOrEmpty(currentResource)
                    ? "No allowed resources found within range."
                    : "No nearby parts have " + currentResource + ".";
                ScreenMessages.PostScreenMessage(new ScreenMessage(msg, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowPartSelectorForTake");
            KShared.Instance.ShowSelector("Take resources from...", optionParts.Keys.ToList(), label =>
            {
                Part source = optionParts[label];
                string resourceName = optionResources[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                if (sourceResource == null) return;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min(amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        WriteResourceName(cell.stored, resourceName);
                        WriteResourceAmount(cell.stored, currentAmount + (float)taken);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} kg of {1}.", taken, resourceName),
                            5.0f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        private void ShowSuitCellPartSelectorForTake()
        {
            KShared.Log("Called!", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            var dict = GetSuitCellDict();
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(dict);
            double spaceRemaining = _suitCellMaxAmount - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                KShared.Log("Unable to take resources: suit cell is full.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, PartResource resource)>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0.0) continue;
                    if (_suitCellAllowedResources.Count > 0
                        && !_suitCellAllowedResources.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F2})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(lbl))
                        options.Add(lbl, (p, pr));
                }

            if (options.Count == 0)
            {
                KShared.Log("No nearby parts have any of the allowed resources.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have allowed resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            KShared.Instance.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, sourceResource) = options[label];
                string resourceName = sourceResource.resourceName;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min((double)amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        var d = GetSuitCellDict();
                        d.TryGetValue(resourceName, out double existing);
                        d[resourceName] = existing + taken;
                        SetSuitCellFromDict(d);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} of {1}.", taken, resourceName),
                            5f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        private List<StoredPart> GetProcessorSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                if (ap?.partPrefab.FindModuleImplementing<KhemistryEVACombinedProcessor>() != null)
                    result.Add(stored);
            }
            return result;
        }

        private KhemistryEVACombinedProcessor GetPrefabProcessor(StoredPart stored)
            => PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                .FindModuleImplementing<KhemistryEVACombinedProcessor>();

        private ProtoPartModuleSnapshot GetProcessorSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot snap in stored.snapshot.modules)
                if (snap.moduleName == "KhemistryEVACombinedProcessor") return snap;
            return null;
        }

        private string ReadProcessorField(StoredPart stored, string key)
            => GetProcessorSnapshot(stored)?.moduleValues.GetValue(key) ?? "";

        private void WriteProcessorField(StoredPart stored, string key, string value)
            => GetProcessorSnapshot(stored)?.moduleValues.SetValue(key, value);

        private bool ReadProcessorBool(StoredPart stored, string key)
        {
            return bool.TryParse(ReadProcessorField(stored, key), out bool result) && result;
        }

        private Dictionary<string, double> DeserializeProcessorResources(StoredPart stored)
            => KhemistryEVACombinedProcessor.Deserialize(ReadProcessorField(stored, "storedResourcesData"));

        private void WriteProcessorResources(StoredPart stored, Dictionary<string, double> resources)
            => WriteProcessorField(stored, "storedResourcesData",
                KhemistryEVACombinedProcessor.Serialize(resources));

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Use Held Processor",
                  groupName = "processoreva", groupDisplayName = "Processors", groupStartCollapsed = false,
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EVAUseProcessor()
        {
            KShared.Log("Called! (Use Held Processor button)", "KhemistryKerbal/EVAUseProcessor");
            var shared = KShared.Instance;
            if (shared == null) return;

            var processors = GetProcessorSnapshots();
            if (processors.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No processors in inventory.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (processors.Count == 1)
            {
                ShowProcessorActionMenu(processors[0]);
                return;
            }

            var labels = new List<string>();
            foreach (StoredPart stored in processors)
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                string name = prefab != null ? stored.partName : stored.partName;
                bool running = ReadProcessorBool(stored, "isRunning");
                string conv = ReadProcessorField(stored, "activeConverterName");
                string suffix = running ? " [" + conv + "]" : " [Stopped]";
                labels.Add(name + suffix);
            }

            shared.ShowSelector("Select processor", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowProcessorActionMenu(processors[idx]);
            });
        }

        private void ShowProcessorActionMenu(StoredPart stored)
        {
            var shared = KShared.Instance;
            KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
            if (prefab == null || !prefab.IsConfigLoaded) return;

            bool running = ReadProcessorBool(stored, "isRunning");
            var actions = new List<string>();

            if (prefab.Converters.Count > 0)
            {
                if (!running) actions.Add("Start Converter");
                else actions.Add("Stop Converter");
            }

            actions.Add("Transfer In (from nearby)");

            var resources = DeserializeProcessorResources(stored);
            if (KhemistryEVACombinedProcessor.GetTotal(resources) > 0.0)
                actions.Add("Transfer Out (to nearby)");

            if (actions.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No actions available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Processor: " + stored.partName, actions,
                action => ExecuteProcessorAction(stored, prefab, action));
        }

        private void ExecuteProcessorAction(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string action)
        {
            var shared = KShared.Instance;
            if (shared == null) return;

            switch (action)
            {
                case "Start Converter":
                    {
                        if (prefab.Converters.Count == 1)
                        {
                            WriteProcessorField(stored, "activeConverterName", prefab.Converters[0].name);
                            WriteProcessorField(stored, "isRunning", "True");
                            ScreenMessages.PostScreenMessage(new ScreenMessage(
                                "Converter \"" + prefab.Converters[0].name + "\" started.",
                                4f, ScreenMessageStyle.UPPER_CENTER));
                        }
                        else
                        {
                            var names = new List<string>();
                            foreach (var conv in prefab.Converters) names.Add(conv.name);
                            shared.ShowSelector("Select converter to start", names, name =>
                            {
                                WriteProcessorField(stored, "activeConverterName", name);
                                WriteProcessorField(stored, "isRunning", "True");
                                ScreenMessages.PostScreenMessage(new ScreenMessage(
                                    "Converter \"" + name + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                            });
                        }
                        break;
                    }
                case "Stop Converter":
                    WriteProcessorField(stored, "isRunning", "False");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter stopped.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Transfer In (from nearby)":
                    ShowProcessorTransferInMenu(stored, prefab);
                    break;

                case "Transfer Out (to nearby)":
                    ShowProcessorTransferOutMenu(stored, prefab);
                    break;
            }
        }

        private void ShowProcessorTransferInMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            KShared.Log("Called!", "KhemistryKerbal/ShowProcessorTransferInMenu");
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(resources);
            double spaceRemaining = prefab.MaxTotalStorage - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, string resourceName)>();
            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (!prefab.SupportedResources.Contains(pr.resourceName)) continue;
                    if (pr.amount <= 0.0) continue;
                    string label = string.Format("{0} / {1}  ({2}: {3:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, (p, pr.resourceName));
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have supported resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, resourceName) = options[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = sourcePart.Resources.Get(def.id);
                if (sourceResource == null) return;

                double taken = Math.Min(sourceResource.amount, spaceRemaining);
                sourceResource.amount -= taken;

                var res = DeserializeProcessorResources(stored);
                res.TryGetValue(resourceName, out double existing);
                res[resourceName] = existing + taken;
                WriteProcessorResources(stored, res);

                KShared.Log(
                    string.Format("Processor received {0:F4} of {1} from {2}.",
                        taken, resourceName, sourcePart.partInfo.title),
                    "KhemistryKerbal/ProcessorTransferIn");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    string.Format("Received {0:F2} of {1}.", taken, resourceName),
                    5f, ScreenMessageStyle.UPPER_CENTER));
            });
        }

        private void ShowProcessorTransferOutMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);

            if (resources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (resources.Count == 1)
            {
                string only = ""; double onlyAmount = 0.0;
                foreach (var kvp in resources) { only = kvp.Key; onlyAmount = kvp.Value; }
                ShowProcessorTransferOutTargets(stored, prefab, only, onlyAmount);
                return;
            }

            var resLabels = new List<string>();
            var resKeys = new List<string>();
            foreach (var kvp in resources)
            {
                resLabels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                resKeys.Add(kvp.Key);
            }

            shared.ShowSelector("Which resource to send?", resLabels, label =>
            {
                int idx = resLabels.IndexOf(label);
                if (idx >= 0)
                    ShowProcessorTransferOutTargets(stored, prefab, resKeys[idx], resources[resKeys[idx]]);
            });
        }

        private void ShowProcessorTransferOutTargets(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string resourceName, double resourceAmount)
        {
            var shared = KShared.Instance;
            var options = new Dictionary<string, Part>();

            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string label = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;

                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;

                    var res = DeserializeProcessorResources(stored);
                    double remaining = resourceAmount - pushed;
                    if (remaining < 1e-9) res.Remove(resourceName);
                    else res[resourceName] = remaining;
                    WriteProcessorResources(stored, res);

                    KShared.Log(
                        string.Format("Processor sent {0:F4} of {1} to {2}.",
                            pushed, resourceName, target.partInfo.title),
                        "KhemistryKerbal/ProcessorTransferOut");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
        private void ShowSuitCellPartSelectorForSend()
        {
            var dict = GetSuitCellDict();
            if (dict.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (dict.Count == 1)
            {
                foreach (var kvp in dict) { ShowSuitCellSendTargets(kvp.Key, kvp.Value); return; }
            }

            var labels = new List<string>();
            var keys = new List<string>();
            var amounts = new List<double>();
            foreach (var kvp in dict)
            {
                labels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                keys.Add(kvp.Key);
                amounts.Add(kvp.Value);
            }

            KShared.Instance.ShowSelector("Which resource to send?", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowSuitCellSendTargets(keys[idx], amounts[idx]);
            });
        }

        private void ShowSuitCellSendTargets(string resourceName, double resourceAmount)
        {
            var options = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(lbl)) options.Add(lbl, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;
                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;
                    var d = GetSuitCellDict();
                    d.TryGetValue(resourceName, out double existing);
                    double remaining = existing - pushed;
                    if (remaining < 1e-9) d.Remove(resourceName);
                    else d[resourceName] = remaining;
                    SetSuitCellFromDict(d);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
    }

    /// <summary>
    /// An <see cref="KhemistryAdvancedISRUBase"/> that is both an ISRU and a <see cref="KhemistryFluidCell"/>.
    /// This is used in EVA parts that must work from a kerbal's inventory.
    /// </summary>
    public class KhemistryEVACombinedProcessor : PartModule
    {
        [KSPField(isPersistant = true)]
        public string storedResourcesData = "";

        [KSPField(isPersistant = true)]
        public bool isRunning = false;

        [KSPField(isPersistant = true)]
        public string activeConverterName = "";

        [KSPField(isPersistant = false)]
        public float maxTotalStorage = 200f;

        [KSPField(isPersistant = false)]
        public float transferDistance = 10f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryprocessor",
                  groupDisplayName = "Processor", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Converter", groupName = "khemistryprocessor")]
        public string converterDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Capacity", groupName = "khemistryprocessor")]
        public string capacityDisplay = "0 / 0";

        public struct ProcessorConverter
        {
            public string name;
            public List<(string resourceName, double ratio)> inputs;
            public List<(string resourceName, double ratio)> outputs;
        }

        private readonly List<string> _supportedResources = new List<string>();
        private readonly List<ProcessorConverter> _converters = new List<ProcessorConverter>();
        private bool _fatalConfigError = false;

        public bool IsConfigLoaded => !_fatalConfigError;
        public List<string> SupportedResources => _supportedResources;
        public List<ProcessorConverter> Converters => _converters;
        public float MaxTotalStorage => maxTotalStorage;
        public float TransferDistance => transferDistance;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            _supportedResources.Clear();
            _converters.Clear();
            _fatalConfigError = false;

            if (float.TryParse(node.GetValue("maxTotalStorage"), out float tmp)) maxTotalStorage = tmp;
            if (float.TryParse(node.GetValue("transferDistance"), out tmp)) transferDistance = tmp;

            if (!node.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryEVACombinedProcessor but no SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());

            if (_supportedResources.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has an empty SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }

            foreach (ConfigNode convNode in node.GetNodes("CONVERTER"))
            {
                string convName = convNode.GetValue("ConverterName");
                if (string.IsNullOrEmpty(convName))
                {
                    KShared.LogError("A CONVERTER node is missing ConverterName, skipping.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }

                var conv = new ProcessorConverter
                {
                    name = convName,
                    inputs = new List<(string, double)>(),
                    outputs = new List<(string, double)>()
                };

                foreach (ConfigNode inputNode in convNode.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double.TryParse(inputNode.GetValue("Ratio"), out double ratio);
                    conv.inputs.Add((resName, ratio));
                }

                foreach (ConfigNode outputNode in convNode.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                    conv.outputs.Add((resName, ratio));
                }

                _converters.Add(conv);
            }

            KShared.Log(
                string.Format("OnLoad: {0} supported resources, {1} converters, maxStorage={2}, transferDist={3}",
                    _supportedResources.Count, _converters.Count, maxTotalStorage, transferDistance),
                "KhemistryEVACombinedProcessor/OnLoad");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (_fatalConfigError)
            {
                contentsDisplay = "ERROR: see log";
                converterDisplay = "ERROR";
                return;
            }

            UpdateDisplay(Deserialize(storedResourcesData));
        }

        public static Dictionary<string, double> Deserialize(string data)
        {
            var dict = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(data)) return dict;
            foreach (string entry in data.Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int sep = entry.IndexOf(':');
                if (sep < 1) continue;
                string name = entry.Substring(0, sep);
                if (double.TryParse(entry.Substring(sep + 1), out double amount) && amount > 0.0)
                    dict[name] = amount;
            }
            return dict;
        }

        public static string Serialize(Dictionary<string, double> dict)
        {
            var parts = new List<string>();
            foreach (var kvp in dict)
                if (kvp.Value > 0.0)
                    parts.Add(kvp.Key + ":" + kvp.Value.ToString("F4"));
            return string.Join("|", parts.ToArray());
        }

        public static double GetTotal(Dictionary<string, double> dict)
        {
            double total = 0.0;
            foreach (var kvp in dict) total += kvp.Value;
            return total;
        }

        public bool RunConversionCycle(Dictionary<string, double> resources,
            string converterName, double dt)
        {
            try
            {
                ProcessorConverter? found = null;
                foreach (var conv in _converters)
                    if (conv.name == converterName) { found = conv; break; }

                if (found == null) return false;
                var c = found.Value;

                foreach (var (resourceName, ratio) in c.inputs)
                {
                    double needed = ratio * dt;
                    if (!resources.TryGetValue(resourceName, out double available)
                        || available < needed * 0.999)
                        return false;
                }

                double inputSum = 0.0;
                foreach (var (resourceName, ratio) in c.inputs) inputSum += ratio * dt;
                double outputSum = 0.0;
                foreach (var (resourceName, ratio) in c.outputs) outputSum += ratio * dt;
                double currentTotal = GetTotal(resources);
                if (currentTotal - inputSum + outputSum > maxTotalStorage)
                    return false;

                foreach (var (resourceName, ratio) in c.inputs)
                {
                    double needed = ratio * dt;
                    resources[resourceName] -= needed;
                    if (resources[resourceName] < 1e-9)
                        resources.Remove(resourceName);
                }

                foreach (var (resourceName, ratio) in c.outputs)
                {
                    resources.TryGetValue(resourceName, out double existing);
                    resources[resourceName] = existing + ratio * dt;
                }

                return true;
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured, returning cycle failure. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryEVACombinedProcessor/RunConversionCycle");
                return false;
            }
        }

        public void UpdateDisplay(Dictionary<string, double> resources)
        {
            double total = GetTotal(resources);

            if (resources.Count == 0)
                contentsDisplay = "Empty";
            else
            {
                var parts = new List<string>();
                foreach (var kvp in resources)
                    parts.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                contentsDisplay = string.Join(", ", parts.ToArray());
            }

            capacityDisplay = string.Format("{0:F2} / {1:F2}", total, maxTotalStorage);

            converterDisplay = (isRunning && !string.IsNullOrEmpty(activeConverterName))
                ? "Running: " + activeConverterName
                : "Stopped";
        }
    }
}