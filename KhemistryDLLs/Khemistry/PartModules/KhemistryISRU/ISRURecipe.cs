using System;
using System.Collections.Generic;

using System.Globalization;

namespace Khemistry
{
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

        public struct ResourceInputMaterial
        {
            public string name;
            public string shape;
            public string size;
            public bool usesParams;
            public Dictionary<string, string> parameters;
            public int amount;
        }

        public enum PowerfailResult { Pause, Stop, Explode, Maint, Void }

        ///// Variables /////
        public readonly List<ResourceInput> _inputs = new List<ResourceInput>();
        public readonly List<ResourceInputMaterial> _inputMaterials = new List<ResourceInputMaterial>();
        public readonly List<PassiveResourceInput> _passiveInputs = new List<PassiveResourceInput>();
        public readonly List<ResourceOutput> _outputs = new List<ResourceOutput>();
        public readonly List<ResourceOutputMaterial> _outputMaterials = new List<ResourceOutputMaterial>();
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
        public readonly List<string> _depositConditions = new List<string>();

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
                _depositConditions.Clear();
                foreach (string condition in node.GetValues("depositCondition"))
                    if (!string.IsNullOrWhiteSpace(condition)) _depositConditions.Add(condition.Trim());

                ///// Charging /////
                _chargeNames.Clear();
                _chargeAmounts.Clear();
                if (node.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in node.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (node.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in node.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                        if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float amtTmp)) _chargeAmounts.Add(amtTmp);
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

                ///// Input materials /////
                _inputMaterials.Clear();
                foreach (ConfigNode matNode in node.GetNodes("INPUT_MATERIAL"))
                {
                    string matName = matNode.GetValue("name");
                    if (string.IsNullOrEmpty(matName))
                    {
                        KShared.LogNoValueInNode("INPUT_MATERIAL", "name", "Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    string shape = matNode.GetValue("shape");
                    string size = matNode.GetValue("size");
                    int materialAmount = KShared.GetIntValueFromCFG(matNode, "amount", 1);
                    if (string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(size) || materialAmount <= 0)
                    {
                        KShared.LogError(
                            "Recipe \"" + _name + "\": INPUT_MATERIAL \"" + matName
                            + "\" requires non-empty shape/size and an amount greater than zero; entry skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    bool usesParams = matNode.HasNode("PARAM_REQUIREMENTS");
                    Dictionary<string, string> parameters = new Dictionary<string, string>();
                    if (usesParams)
                        foreach (string key in matNode.GetNode("PARAM_REQUIREMENTS").values.DistinctNames())
                            parameters.Add(key, matNode.GetNode("PARAM_REQUIREMENTS").GetValue(key));

                    _inputMaterials.Add(new ResourceInputMaterial
                    {
                        name = matName,
                        shape = shape,
                        size = size,
                        usesParams = usesParams,
                        parameters = parameters,
                        amount = materialAmount
                    });
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
                                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius)
                                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tempC))
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
                foreach (ConfigNode matNode in node.GetNodes("OUTPUT_MATERIAL"))
                {
                    string matName = matNode.GetValue("name");
                    if (string.IsNullOrEmpty(matName)) continue;

                    string shape = matNode.GetValue("shape");
                    string size = matNode.GetValue("size");
                    double amount = KShared.GetDoubleValueFromCFG(matNode, "amount", 1.0);
                    string outVolume = KShared.GetStrValueFromCFG(matNode, "outVolume", "0");

                    if (string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(size)
                        || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                    {
                        KShared.LogError(
                            "Recipe \"" + _name + "\": OUTPUT_MATERIAL \"" + matName
                            + "\" requires non-empty shape/size and an amount greater than zero; entry skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

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
                        "Recipe \"" + _name + "\" has no OUTPUT_RESOURCE nor OUTPUT_MATERIAL nodes — it will do nothing.",
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
                _workersEngineers = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersEngineers", 0));
                _workersPilots = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersPilots", 0));
                _workersScientists = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersScientists", 0));

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
            copy._depositConditions.AddRange(_depositConditions);
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
            foreach (ResourceInputMaterial mat in _inputMaterials)
            {
                int scaledAmount = (int)Math.Round(mat.amount * multiplier, MidpointRounding.AwayFromZero);
                if (mat.amount > 0 && multiplier > 0.0 && scaledAmount < 1) scaledAmount = 1;
                copy._inputMaterials.Add(new ResourceInputMaterial
                {
                    name = mat.name,
                    shape = mat.shape,
                    size = mat.size,
                    usesParams = mat.usesParams,
                    parameters = new Dictionary<string, string>(mat.parameters),
                    amount = scaledAmount
                });
            }
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
                    parameters = new Dictionary<string, string>(mat.parameters),
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
            "INPUT_RESOURCE", "OUTPUT_RESOURCE", "PINPUT_RESOURCE", "INPUT_MATERIAL", "OUTPUT_MATERIAL"
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
}
