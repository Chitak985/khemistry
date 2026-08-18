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
/* Sample config for KhemistryISRU recipes
KHEMISTRYBATCHISRU_RECIPE
{
    name = Cooling Recipe Name  // Required

    recipeType = cooling  // Defaults to NONE
    recipeSubtype = big  // Defaults to NONE
    recipeSubsubtype = highHeat  // Defaults to NONE
    // Multiple recipeType, recipeSubtype, and recipeSubsubtype can be included

    // Everything else is the same as in RECIPE nodes inside KhemistryISRU

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
/* Sample config for KhemistryISRU
MODULE
{
	name = KhemistryISRU

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
    ////////////////////////////// Batch ISRU System //////////////////////////////

    /// <summary>
    /// A config used in <see cref="KhemistryISRU"/> for each biome.
    /// Contains a lot of conditions when the recipe can work.
    /// </summary>
    public class KhemistryISRUBiomeConfig
    {
        public string biomeName;

        public bool disabled = false;

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
        /// Make a biome config from a biome config node in a ISRU recipe.
        /// </summary>
        /// <param name="node">The node BIOME_CONFIG in PLANET_CONFIG in a ISRU module.</param>
        /// <param name="ConverterName">The name of the converter the biome config belongs to.</param>
        public KhemistryISRUBiomeConfig(ConfigNode node, string ConverterName = "UNKNOWN")
        {
            if (node.HasValue("name"))
            {
                biomeName = node.GetValue("name");

                if (node.HasValue("disable"))
                    if (node.GetValue("disable") == "true")
                        disabled = true;

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
                            "KhemistryISRU/LoadSharedConfig");
                }
                situationDestructive.Clear();
                foreach (string situationDestructiveStr in node.GetValues("situationDestructive"))
                {
                    if (Enum.TryParse(situationDestructiveStr, true, out KShared.SituationCondition parsed))
                        situationDestructive.Add(parsed);
                    else
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": Biome config \"" + biomeName + "\": Unknown situationDestructive situationCondition \"" + situationDestructiveStr + "\" — condition ignored.",
                            "KhemistryISRU/LoadSharedConfig");
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
                KShared.LogNoValueInNode("BIOME_CONFIG", "name", "Converter \"" + ConverterName + "\": Recipe ", "KhemistryISRUBiomeConfig/constructor");
                return;
            }
        }
    }

    /// <summary>
    /// A recipe for <see cref="KhemistryISRU"/>.
    /// Contains inputs, outputs and multiple <see cref="KhemistryISRUBiomeConfig"/> to use.
    /// </summary>
    public class KhemistryISRURecipe
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
            public string outVolume;
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
        public Dictionary<string, Dictionary<string, KhemistryISRUBiomeConfig>> _planetConfigs = new Dictionary<string, Dictionary<string, KhemistryISRUBiomeConfig>>();

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
        /// Loads everything shared between a local RECIPE node in KhemistryISRU and a
        /// top level KHEMISTRYBATCHISRU_RECIPE node: identity, charging, planet/biome configs,
        /// inputs/outputs/materials, timing, control rules, and worker requirements.
        /// </summary>
        public KhemistryISRURecipe(ConfigNode node, string ConverterName)
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
                        "KhemistryISRURecipe/constructor");
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
                            KShared.LogNoNode("BIOME_CONFIG", "Converter \"" + ConverterName + "\": Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
                            continue;
                        }

                        if (!_planetConfigs.TryGetValue(planetName, out Dictionary<string, KhemistryISRUBiomeConfig> biomeDict))
                        {
                            biomeDict = new Dictionary<string, KhemistryISRUBiomeConfig>();
                            _planetConfigs.Add(planetName, biomeDict);
                        }

                        foreach (ConfigNode biomeNode in planetNode.GetNodes("BIOME_CONFIG"))
                        {
                            KhemistryISRUBiomeConfig biomeConfig = new KhemistryISRUBiomeConfig(biomeNode, ConverterName);
                            string biomeKey = biomeConfig.biomeName ?? "ALL";
                            biomeDict[biomeKey] = biomeConfig;
                        }
                    }
                }
                else
                {
                    // Instead of requiring a node just use an empty one with name=ALL
                    //KShared.LogNoNode("PLANET_CONFIG", "Converter \"" + ConverterName + "\": Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
                    Dictionary<string, KhemistryISRUBiomeConfig> biomeDict = new Dictionary<string, KhemistryISRUBiomeConfig>();
                    ConfigNode configNode = new ConfigNode("BIOME_CONFIG");
                    configNode.AddValue("name", "ALL");
                    biomeDict.Add("ALL", new KhemistryISRUBiomeConfig(configNode, ConverterName));
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
                                "KhemistryISRURecipe/constructor");
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
                        KShared.LogNoValueInNode("PINPUT_RESOURCE", "name", "Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
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
                                "KhemistryISRURecipe/constructor");
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
                                    "KhemistryISRURecipe/constructor");
                                powerfail = PowerfailResult.Pause;
                            }
                        }
                        else
                        {
                            KShared.LogError(
                                "Recipe \"" + _name + "\": Unknown powefail \"" + pfRaw + "\" for PINPUT_RESOURCE " + resName + " — defaulting to PAUSE.",
                                "KhemistryISRURecipe/constructor");
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
                    string outVolume = KShared.GetStrValueFromCFG(matNode, "outVolume", "0");

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
                        "KhemistryISRURecipe/constructor");

                ///// Timing and control /////
                _recipeTime = KShared.GetDoubleValueFromCFG(node, "recipeTime", 0.0);
                if (_recipeTime <= 0.0)
                    KShared.LogError(
                        "Recipe \"" + _name + "\" has no valid recipeTime set — it will never complete a batch.",
                        "KhemistryISRURecipe/constructor");

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
                            "KhemistryISRURecipe/constructor");
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
                "KhemistryISRURecipe/constructor");
            }
        }

        /// <summary>
        /// Looks up the applicable KhemistryISRUBiomeConfig for a given planet/biome, falling back
        /// from exact biome → ALL biome on that planet → ALL planet/ALL biome → null (no config,
        /// recipe cannot operate at the current location).
        /// </summary>
        public KhemistryISRUBiomeConfig GetBiomeConfig(string planet, string biome)
        {
            if (_planetConfigs.TryGetValue(planet, out Dictionary<string, KhemistryISRUBiomeConfig> biomeDict))
            {
                if (biome != null && biomeDict.TryGetValue(biome, out KhemistryISRUBiomeConfig exact))
                    return exact;
                if (biomeDict.TryGetValue("ALL", out KhemistryISRUBiomeConfig planetAll))
                    return planetAll;
            }

            if (_planetConfigs.TryGetValue("ALL", out Dictionary<string, KhemistryISRUBiomeConfig> allPlanetDict))
            {
                if (biome != null && allPlanetDict.TryGetValue(biome, out KhemistryISRUBiomeConfig exactAll))
                    return exactAll;
                if (allPlanetDict.TryGetValue("ALL", out KhemistryISRUBiomeConfig globalAll))
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
        public KhemistryISRURecipe ScaledCopy(double multiplier)
        {
            KhemistryISRURecipe copy = new KhemistryISRURecipe();
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
        public KhemistryISRURecipe() { }

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
        /// Applies the recipe-related values and nodes on a <see cref="KhemistryISRU"/> MODULE node on top of
        /// a loaded recipe's config node, returning a new merged node suitable for re-parsing into a
        /// <see cref="KhemistryISRURecipe"/>. Module-only bookkeeping (identity, converter naming, recipe
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
    /// An ISRU module that uses batches and is the main Khemistry ISRU module.
    /// </summary>
    public class KhemistryISRU : PartModule
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
                    "KhemistryISRU/SetupActiveAnimation");
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
                "KhemistryISRU/SetupActiveAnimation");
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
        [KSPField(isPersistant = false)] public string StartActionName = "Start working";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop working";

        /// <summary>
        /// depositCondition values loaded from the MODULE node (0 or more). If non-empty, the
        /// converter may only be started while at least one of the listed deposit resource names
        /// (surface or underground) is present at the vessel's current location.
        /// </summary>
        protected readonly List<string> _depositConditions = new List<string>();

        /// <summary>
        /// The moduleType loaded from the MODULE node. "normal" (default) behaves as before;
        /// "kerbalEVA" is EVA-suit-cell-routed ISRU meant to live on a kerbal part; "partEVA" is
        /// reserved for future use and is not currently implemented.
        /// </summary>
        [KSPField(isPersistant = false)] public string moduleType = "normal";

        /// <summary>
        /// The KhemistryKerbal this converter routes resources/materials through when
        /// moduleType == "kerbalEVA". Only set (and required) in that mode.
        /// </summary>
        protected KhemistryKerbal _kerbalHost = null;

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
            KShared.Log("Charging enabled.", "KhemistryISRU/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistrybatchisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != ConverterState.Charging) return;
            state = ConverterState.Off;
            KShared.Log("Charging disabled.", "KhemistryISRU/DisableCharging");
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
        protected void TriggerPowerfail(Part contextPart, KhemistryISRURecipe.PowerfailResult powerfailResult,
            double explosionRadius = 0.0, double explosionTemperatureCelsius = 0.0)
        {
            KShared.Log(
                "Converter \"" + ConverterName + "\" powerfailed. Result: " + powerfailResult,
                "KhemistryISRU/TriggerPowerfail");

            switch (powerfailResult)
            {
                case KhemistryISRURecipe.PowerfailResult.Pause:
                    statusDisplay = "Paused";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Stop:
                    RefundPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Void:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail, resources lost)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Maint:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    needsMaintenance = true;
                    statusDisplay = "Needs maintenance";
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + ConverterName + "\": Requires maintenance by an Engineer.",
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    break;
                case KhemistryISRURecipe.PowerfailResult.Explode:
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
            KShared.Log("Converter turned ON.", "KhemistryISRU/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistrybatchisru", active = false)]
        public void TurnOffConverter()
        {
            state = ConverterState.Off;
            KShared.Log("Converter turned OFF.", "KhemistryISRU/TurnOffContainer");
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

        protected List<KhemistryISRURecipe> recipes = new List<KhemistryISRURecipe>();

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

        protected KhemistryISRURecipe _activeRecipe = null;

        // Parallel to _activeRecipe._passiveInputs; not persisted (periods are short, so
        // losing phase across a save/reload is a harmless simplification).
        protected readonly List<double> _passiveTimers = new List<double>();

        // Cumulative amount actually withdrawn per passive input since the last time
        // batchProgress was reset to 0 — needed so STOP can refund exactly what was taken
        // during the in-progress batch, while VOID/MAINT discard it instead.
        protected readonly List<double> _passiveConsumedThisBatch = new List<double>();

        protected readonly Dictionary<KhemistryISRURecipe.ResourceOutputMaterial, double> _materialOutputAmount =
            new Dictionary<KhemistryISRURecipe.ResourceOutputMaterial, double>();

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

            if (_depositConditions.Count > 0 && !IsAtRequiredDeposit())
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No matching deposit (" + string.Join(", ", _depositConditions) + ") found at this location.",
                    "KhemistryISRU/StartConverter");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Can't operate — not at a required deposit.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            isRunning = true;
            KShared.Log("Converter \"" + ConverterName + "\" started.", "KhemistryISRU/StartConverter");
            UpdateEventVisibility();
        }

        /// <summary>
        /// Whether the vessel's current location satisfies at least one of this converter's
        /// depositCondition entries (surface or underground). Logs an error and returns false —
        /// rather than throwing — if KShared isn't loaded yet. Always true if depositCondition
        /// is empty (no restriction configured).
        /// </summary>
        protected bool IsAtRequiredDeposit()
        {
            if (_depositConditions.Count == 0) return true;

            KShared shared = KShared.Instance;
            // KSharedMainMenu (and thus its deposit lists) may not be loaded yet.
            if (shared == null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Cannot check depositCondition — KShared instance is not loaded yet.",
                    "KhemistryISRU/IsAtRequiredDeposit");
                return false;
            }

            if (vessel == null || vessel.mainBody == null) return false;

            List<string> here = shared.SurfaceDepositsAtPoint((float)vessel.latitude, (float)vessel.longitude, vessel.mainBody.name, 0);
            here.AddRange(shared.UndergroundDepositsAtPoint((float)vessel.latitude, (float)vessel.longitude, vessel.mainBody.name, 0));
            return _depositConditions.Any(d => here.Contains(d));
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistrybatchisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Log("Converter \"" + ConverterName + "\" stopped.", "KhemistryISRU/StopConverter");
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
            ConfigNode moduleNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryISRU");
            if (moduleNode == null)
            {
                // Already logged by FindModuleConfigNode — fail loudly instead of NRE-ing through
                // the rest of this method (which would otherwise be silently swallowed by Unity's
                // per-callback exception handling and never show up as a Khemistry-prefixed log).
                _fatalConfigError = true;
                statusDisplay = "ERROR: config node not found, see log";
                return;
            }
            KShared shared = KShared.Instance;

            ///// Deposit conditions /////
            _depositConditions.Clear();
            _depositConditions.AddRange(moduleNode.GetValues("depositCondition"));

            ///// Module type /////
            moduleType = KShared.GetStrValueFromCFG(moduleNode, "moduleType", "normal");

            if (moduleType == "partEVA")
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": moduleType=partEVA is not implemented yet — falling back to normal.",
                    "KhemistryISRU/LoadConfigFromPartInfo");
                moduleType = "normal";
            }

            if (moduleType == "kerbalEVA")
            {
                // kerbalEVA-specific defaults: a bare converter with no explicit ConverterName
                // or recipeType is named "Kerbal" and imports the "kerbalEVA" recipeType.
                ConverterName = KShared.GetStrValueFromCFG(moduleNode, "ConverterName", "Kerbal");
                StartActionName = KShared.GetStrValueFromCFG(moduleNode, "StartActionName", "Start working");
                StopActionName = KShared.GetStrValueFromCFG(moduleNode, "StopActionName", "Stop working");
            }

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
                        "KhemistryISRU/LoadConfigFromPartInfo");
            }

            ///// Recipes: local RECIPE nodes /////
            recipes.Clear();
            if (moduleNode.HasNode("RECIPE"))
            {
                foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
                {
                    ConfigNode mergedNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, recipeNode);
                    recipes.Add(new KhemistryISRURecipe(mergedNode, ConverterName));
                }
            }

            ///// Recipes: imported by name (RECIPE_NAMES & RECIPE_MULTIPLIERS) /////
            recipeMultiplier = KShared.GetFloatValueFromCFG(moduleNode, "recipeMultiplier", 1f);

            recipeType = KShared.GetStrValueFromCFG(moduleNode, "recipeType", moduleType == "kerbalEVA" ? "kerbalEVA" : null);
            recipeSubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubtype", null);
            recipeSubsubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubsubtype", null);

            _recipeNames.Clear();
            _recipeMultipliers.Clear();
            if (moduleNode.HasNode("RECIPE_NAMES"))
            {
                if (!moduleNode.GetNode("RECIPE_NAMES").HasValue("name"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": Node RECIPE_NAMES is present but has no \"name\" values, skipping.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
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
                            "KhemistryISRU/LoadConfigFromPartInfo");
                        _recipeMultipliers.Clear();
                    }
                }
            }
            else if (moduleNode.HasNode("RECIPE_MULTIPLIERS"))
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Node RECIPE_MULTIPLIERS is present but no RECIPE_NAMES node is present.",
                    "KhemistryISRU/LoadConfigFromPartInfo");

            if (shared != null)
            {
                if (_recipeNames.Count > 0)
                {
                    for (int i = 0; i < _recipeNames.Count; i++)
                    {
                        string wantedName = _recipeNames[i];
                        KhemistryISRURecipe found = shared.batchRecipeList.FirstOrDefault(r => r._name == wantedName);
                        if (found == null)
                        {
                            KShared.LogError(
                                "Converter \"" + ConverterName + "\": RECIPE_NAMES entry \"" + wantedName
                                + "\" does not match any loaded KHEMISTRYBATCHISRU_RECIPE.",
                                "KhemistryISRU/LoadConfigFromPartInfo");
                            continue;
                        }
                        // Global recipeMultiplier and the per-name RECIPE_MULTIPLIERS entry stack:
                        // global is applied first, then the local (per-name) multiplier.
                        float localMult = (i < _recipeMultipliers.Count) ? _recipeMultipliers[i] : 1f;
                        ConfigNode mergedFoundNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, found.mainNode);
                        KhemistryISRURecipe overriddenFound = new KhemistryISRURecipe(mergedFoundNode, ConverterName);
                        recipes.Add(overriddenFound.ScaledCopy(recipeMultiplier * localMult));
                    }
                }
                if (recipeType != null || recipeSubtype != null || recipeSubsubtype != null)
                {
                    foreach (KhemistryISRURecipe candidate in shared.batchRecipeList)
                    {
                        if (candidate.MatchesTypes(recipeType, recipeSubtype, recipeSubsubtype))
                        {
                            ConfigNode mergedCandidateNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, candidate.mainNode);
                            KhemistryISRURecipe overriddenCandidate = new KhemistryISRURecipe(mergedCandidateNode, ConverterName);

                            // Check if this wasn't already added by RECIPE_NAMES logic
                            foreach (KhemistryISRURecipe recipe in recipes)
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
                        "KhemistryISRU/LoadSharedConfig");
                return;
            }

            if (moduleType == "kerbalEVA")
            {
                StripKerbalEVAIncompatibleFields(moduleNode);
                // Interaction/display distance are meaningless here — the GUI lives on the
                // kerbal itself, so it's always "in range" of its own converter.
                if (moduleNode.HasValue("maxInteractionDistance"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"maxInteractionDistance\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                if (moduleNode.HasValue("maxDisplayDistance"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"maxDisplayDistance\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                if (moduleNode.HasValue("workersCrewSamePart"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"workersCrewSamePart\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                _configMaxInteractionDistance = float.MaxValue;
                _configMaxDisplayDistance = float.MaxValue;
            }
            else
            {
                if (bool.TryParse(KShared.GetStrValueFromCFG(moduleNode, "workersCrewSamePart", "false"), out bool wcspTmp))
                    workersCrewSamePart = wcspTmp;
                _configMaxInteractionDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxInteractionDistance", _configMaxInteractionDistance);
                _configMaxDisplayDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxDisplayDistance", _configMaxDisplayDistance);
            }
            _maxInteractionDistance = _configMaxInteractionDistance;
            _maxDisplayDistance = _configMaxDisplayDistance;

            ///// Select active recipe /////
            KhemistryISRURecipe initial = null;
            if (!string.IsNullOrEmpty(activeRecipeName))
                initial = recipes.FirstOrDefault(r => r._name == activeRecipeName);
            if (initial == null) initial = recipes[0];
            ApplyRecipe(initial);
        }

        /// <summary>
        /// For moduleType == "kerbalEVA": strips out worker-count/worker-type/control-rule and
        /// distance-multiplier fields that don't make sense for suit-cell-routed EVA ISRU
        /// (logging a warning per field actually present in the config), and forces any MAINT
        /// powerfailResult to VOID since there's no Engineer to perform EVA self-maintenance.
        /// </summary>
        private void StripKerbalEVAIncompatibleFields(ConfigNode moduleNode)
        {
            void WarnIfPresent(ConfigNode n, string key, string context)
            {
                if (n != null && n.HasValue(key))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"" + key
                        + "\" in " + context + " is ignored for kerbalEVA converters.",
                        "KhemistryISRU/StripKerbalEVAIncompatibleFields");
            }

            WarnIfPresent(moduleNode, "workersType", "the MODULE node");
            WarnIfPresent(moduleNode, "controlRules", "the MODULE node");

            foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
            {
                WarnIfPresent(recipeNode, "workersEngineers", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersPilots", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersScientists", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersType", "a RECIPE node");
                WarnIfPresent(recipeNode, "controlRules", "a RECIPE node");

                foreach (ConfigNode planetNode in recipeNode.GetNodes("PLANET_CONFIG"))
                    foreach (ConfigNode biomeNode in planetNode.GetNodes("BIOME_CONFIG"))
                        WarnBiomeMultipliers(biomeNode);
                foreach (ConfigNode biomeNode in recipeNode.GetNodes("BIOME_CONFIG"))
                    WarnBiomeMultipliers(biomeNode);

                void WarnBiomeMultipliers(ConfigNode biomeNode)
                {
                    WarnIfPresent(biomeNode, "maxDisplayDistanceMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "maxInteractionDistanceMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersScientistsMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersPilotsMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersEngineersMul", "a BIOME_CONFIG node");
                }
            }

            foreach (KhemistryISRURecipe recipe in recipes)
            {
                recipe._workersEngineers = 0;
                recipe._workersPilots = 0;
                recipe._workersScientists = 0;
                recipe._workersEVA = false;
                recipe._workersCREW = false;

                for (int i = 0; i < recipe._passiveInputs.Count; i++)
                {
                    var pinp = recipe._passiveInputs[i];
                    if (pinp.powerfail == KhemistryISRURecipe.PowerfailResult.Maint)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): Recipe \"" + recipe._name
                            + "\" has a MAINT powerfailResult, which requires an Engineer and doesn't apply here — treating as VOID.",
                            "KhemistryISRU/StripKerbalEVAIncompatibleFields");
                        pinp.powerfail = KhemistryISRURecipe.PowerfailResult.Void;
                        recipe._passiveInputs[i] = pinp;
                    }
                }
            }
        }

        /// <summary>
        /// Makes the given recipe the active one: applies its own charging fields
        /// (falling back to module-level charging if the recipe doesn't define its own),
        /// resets batch progress, and updates control show-rules.
        /// </summary>
        protected void ApplyRecipe(KhemistryISRURecipe recipe)
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
            foreach (KhemistryISRURecipe r in recipes)
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
                    "KhemistryISRU/SwitchRecipe");
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
                "KhemistryISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + ConverterName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }

        /// <summary>
        /// Fully hides this converter from the PAW: disables every event/action and every
        /// displayed KSPField (including statusDisplay itself, so the error message the caller
        /// sets afterward via the field's raw value is never actually rendered).
        /// </summary>
        protected void DisableAllUI()
        {
            foreach (BaseEvent e in Events) e.active = false;
            foreach (BaseField f in Fields)
                f.guiActive = f.guiActiveEditor = false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

            // Peek moduleType early (before LoadConfigFromPartInfo runs its full parse) so the
            // kerbal-host / duplicate checks below can bail out before doing anything else.
            ConfigNode precheckNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryISRU");
            moduleType = KShared.GetStrValueFromCFG(precheckNode, "moduleType", "normal");

            if (moduleType == "kerbalEVA")
            {
                _kerbalHost = part.FindModuleImplementing<KhemistryKerbal>();
                if (_kerbalHost == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" has moduleType=kerbalEVA but part \"" + part.name
                        + "\" has no KhemistryKerbal module — this module only works on a kerbal part.",
                        "KhemistryISRU/OnStart");
                    _fatalConfigError = true;
                    DisableAllUI();
                    statusDisplay = "ERROR: not on a kerbal part, see log";
                    return;
                }

                // Only one kerbalEVA-type KhemistryISRU may run per kerbal. Rather than
                // re-deriving each sibling's moduleType from its (possibly already-renamed)
                // ConverterName — which breaks once the first instance renames itself to
                // "Kerbal" — claim a slot on the (already deduplicated, singular) KhemistryKerbal
                // host. This is robust regardless of how many duplicate module instances KSP's
                // EVA-construct/DLC part assembly ends up creating, and in what order they start.
                if (_kerbalHost.kerbalEVAISRU != null && _kerbalHost.kerbalEVAISRU != this)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": another kerbalEVA KhemistryISRU is already present on this kerbal — only one is allowed, disabling this one.",
                        "KhemistryISRU/OnStart");
                    _fatalConfigError = true;
                    DisableAllUI();
                    statusDisplay = "ERROR: duplicate kerbalEVA module, see log";
                    return;
                }
                _kerbalHost.kerbalEVAISRU = this;
            }

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                DisableAllUI();
                statusDisplay = "ERROR: see log";
                return;
            }

            Fields["statusDisplay"].guiActiveUnfocused = true;
            Fields["chargeDisplay"].guiActiveUnfocused = true;
            Fields["progressDisplay"].guiActiveUnfocused = true;
            Fields["stateDisplay"].guiActiveUnfocused = true;

            Fields["statusDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["chargeDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["progressDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["stateDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;

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
        /// Requests (positive amount) or produces (negative amount) a resource from wherever
        /// this converter actually draws from: the vessel resource network normally, or the
        /// kerbal's fluid suit cell when moduleType == "kerbalEVA". Same amount/return contract
        /// as <see cref="Part.RequestResource(string, double)"/>.
        /// </summary>
        private double RequestResourceRouted(string name, double amount)
        {
            if (moduleType == "kerbalEVA" && _kerbalHost != null)
                return _kerbalHost.RequestSuitCellResource(name, amount);
            return part.RequestResource(name, amount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
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
                        "KhemistryISRU/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = RequestResourceRouted(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        RequestResourceRouted(names[i], -pulled[i]);
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
                    "KhemistryISRU/HandleCharging");
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
        /// <param name="biomeConfig">The <see cref="KhemistryISRUBiomeConfig"/> fetched outside the function.</param>
        /// <returns>If <see langword="true"/>, an explosion or error happened.</returns>
        protected bool CheckBiomeConfig(KhemistryISRUBiomeConfig biomeConfig)
        {
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryISRU/CheckBiomeConfig");
                return true;
            }

            // One hundred and one ways to explode
            if (biomeConfig.situationDestructive.Contains(_runtimeData.sitCon) ||
                _runtimeData.alt < biomeConfig.minAltitude || _runtimeData.alt > biomeConfig.maxAltitude ||
                _runtimeData.g < biomeConfig.minG || _runtimeData.g > biomeConfig.maxG ||
                _runtimeData.temperature < biomeConfig.minTemperature || _runtimeData.temperature > biomeConfig.maxTemperature ||
                _runtimeData.pressure < biomeConfig.minPressure || _runtimeData.pressure > biomeConfig.maxPressure)
            {
                TriggerPowerfail(part, KhemistryISRURecipe.PowerfailResult.Explode);
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

            KhemistryISRUBiomeConfig biomeConfig = _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome);
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryISRU/RunBatchCycle");
                return;
            }

            if (CheckBiomeConfig(biomeConfig))
                return;

            if (biomeConfig.disabled)
            {
                statusDisplay = "Disabled in this biome";
                return;
            }

            if (biomeConfig.situationOperating.Count > 0 && !biomeConfig.situationOperating.Contains(_runtimeData.sitCon))
            {
                statusDisplay = "Wrong situation (" + _runtimeData.sitCon + ")";
                return;
            }

            if (_depositConditions.Count > 0 && !IsAtRequiredDeposit())
            {
                statusDisplay = "Not at a required deposit";
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
        protected bool ProcessPassiveInputs(KhemistryISRUBiomeConfig biomeConfig, double dt)
        {
            if (_activeRecipe._passiveInputs.Count == 0) return true;

            for (int i = 0; i < _activeRecipe._passiveInputs.Count; i++)
            {
                KhemistryISRURecipe.PassiveResourceInput pinp = _activeRecipe._passiveInputs[i];
                double timer = (i < _passiveTimers.Count) ? _passiveTimers[i] : 0.0;
                timer += dt;

                while (timer >= pinp.period)
                {
                    timer -= pinp.period;
                    double needed = pinp.amount * biomeConfig.inputMultiplier;
                    if (needed <= 0.0) continue;

                    double got = RequestResourceRouted(pinp.resourceName, needed);

                    if (got < needed * 0.999)
                    {
                        // Passive consumption is all-or-nothing per tick — refund any partial draw.
                        if (got > 0.0) RequestResourceRouted(pinp.resourceName, -got);

                        if (pinp.ignorePowerfail)
                            continue;  // "nothing happens" — resource just isn't consumed this tick

                        if (i < _passiveTimers.Count) _passiveTimers[i] = timer;

                        if (pinp.powerfail == KhemistryISRURecipe.PowerfailResult.Pause)
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
        protected bool TryRunBatch(KhemistryISRUBiomeConfig biomeConfig)
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
                double got = RequestResourceRouted(outp.resourceName, -toAdd);
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

        private static readonly System.Text.RegularExpressions.Regex _randfPattern =
            new System.Text.RegularExpressions.Regex(
                @"^randf\(\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]+)\s*\)$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// If the given value is a randf(a,b,n) expression, replaces it with a random float
        /// between a and b (inclusive on both ends), rounded to n decimal places. Negative n is
        /// an error and is treated as 0. Non-randf values are returned unchanged.
        /// </summary>
        protected static string ResolveRandf(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var match = _randfPattern.Match(value.Trim());
            if (!match.Success) return value;

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has non-numeric bounds — leaving value as-is.",
                    "KhemistryISRU/ResolveRandf");
                return value;
            }

            int n = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (n < 0)
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has a negative decimal-place count — treating as 0.",
                    "KhemistryISRU/ResolveRandf");
                n = 0;
            }

            double lo = Math.Min(a, b);
            double hi = Math.Max(a, b);
            double roll = lo + UnityEngine.Random.value * (hi - lo);
            double rounded = Math.Round(roll, n, MidpointRounding.AwayFromZero);

            return rounded.ToString("F" + n, CultureInfo.InvariantCulture);
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
                        "KhemistryISRU/TryTransferMaterialOutputBuffer");
                    continue;
                }

                string resolvedSize = ResolveRandf(matOutput.size);
                Dictionary<string, string> resolvedParameters = new Dictionary<string, string>();
                foreach (var kv in matOutput.parameters)
                    resolvedParameters[kv.Key] = ResolveRandf(kv.Value);

                if (!KShared.TryEvaluateOutVolumeExpression(matOutput.outVolume, resolvedSize, resolvedParameters,
                        "KhemistryISRU/TryTransferMaterialOutputBuffer", out double perUnitVolume))
                    continue;  // error already logged

                KhemistryMaterialInstance instance = new KhemistryMaterialInstance(
                    material, matOutput.shape, resolvedSize,
                    (float)(perUnitVolume * wholeUnits), resolvedParameters);

                bool placed = false;
                if (moduleType == "kerbalEVA" && _kerbalHost != null)
                {
                    if (_kerbalHost.TryAddMaterialToSuitCell(instance))
                    {
                        _materialOutputAmount[matOutput] = buffered - wholeUnits;
                        transferredAny = true;
                        placed = true;
                    }
                }
                else
                {
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
            }

            return transferredAny;
        }
    }

    ////////////////////////////// Shared Data //////////////////////////////

    /// <summary>
    /// Runtime data used by <see cref="KhemistryISRU"/>.
    /// This is checked by <see cref="KhemistryISRUBiomeConfig"/> to see if a recipe can run.
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
            if (vessel != null)
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

            // KhemistryISRU recipes
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRYBATCHISRU_RECIPE"))
            {
                if (!node.HasValue("name"))
                {
                    KShared.LogError("A KHEMISTRYBATCHISRU_RECIPE has no name!", "KSharedMainMenu/Awake");
                    continue;
                }
                kinst.batchRecipeList.Add(new KhemistryISRURecipe(node, node.GetValue("name")));
            }
            KShared.Log("Created " + kinst.batchRecipeList.Count.ToString() + " KhemistryISRU recipes.", "KSharedMainMenu/Awake");

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

    /// <summary>
    /// An ISRU that is both an EVA ISRU and a <see cref="KhemistryFluidCell"/>.
    /// This is used in EVA parts that must work from a kerbal's inventory.
    /// !TODO: Merge into KhemistryISRU
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