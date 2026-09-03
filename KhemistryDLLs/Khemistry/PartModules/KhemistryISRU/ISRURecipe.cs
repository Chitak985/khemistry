using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Khemistry
{
    /// <summary>
    /// A recipe for <see cref="KhemistryISRU"/>.
    /// Contains inputs, outputs and multiple <see cref="KhemistryISRUBiomeConfig"/> to use.
    /// </summary>
    public class KhemistryISRURecipe
    {
        private static readonly Regex OutVolumeTokenPattern = new Regex(
            @"\[([A-Za-z_][A-Za-z0-9_]*)\]", RegexOptions.Compiled);
        private static readonly Regex RandfValuePattern = new Regex(
            @"^randf\(\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]+)\s*\)$",
            RegexOptions.Compiled);

        public bool IsValid { get; private set; }

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
                bool configurationError = false;

                _name = KShared.GetStrValueFromCFG(node, "name", ConverterName)?.Trim();
                if (string.IsNullOrEmpty(_name))
                {
                    _name = "Invalid recipe";
                    configurationError = true;
                    KShared.LogError("A KhemistryISRU recipe has an empty name.",
                        "KhemistryISRURecipe/constructor");
                }

                _recipeTypes.Clear();
                AddTrimmedDistinct(_recipeTypes, node.GetValues("recipeType"));
                _recipeSubtypes.Clear();
                AddTrimmedDistinct(_recipeSubtypes, node.GetValues("recipeSubtype"));
                if (_recipeSubtypes.Count == 0 && node.HasValue("recipeSubype"))
                {
                    AddTrimmedDistinct(_recipeSubtypes, node.GetValues("recipeSubype"));
                    KShared.LogWarning("Recipe \"" + _name
                        + "\" uses legacy misspelling \"recipeSubype\"; use \"recipeSubtype\".",
                        "KhemistryISRURecipe/constructor");
                }
                _recipeSubsubtypes.Clear();
                AddTrimmedDistinct(_recipeSubsubtypes, node.GetValues("recipeSubsubtype"));
                _depositConditions.Clear();
                foreach (string condition in node.GetValues("depositCondition"))
                {
                    string trimmed = condition?.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !_depositConditions.Contains(trimmed))
                        _depositConditions.Add(trimmed);
                }

                ///// Charging /////
                _chargeNames.Clear();
                _chargeAmounts.Clear();
                bool chargingConfigDeclared = node.HasNode("CHARGE_CON_NAMES")
                    || node.HasNode("CHARGE_CON_AMOUNTS");
                List<string> rawChargeNames = node.GetNodes("CHARGE_CON_NAMES")
                    .SelectMany(chargeNode => chargeNode.GetValues("name"))
                    .ToList();
                List<string> rawChargeAmounts = node.GetNodes("CHARGE_CON_AMOUNTS")
                    .SelectMany(chargeNode => chargeNode.GetValues("amount"))
                    .ToList();
                bool validChargingConfig = !chargingConfigDeclared
                    || (rawChargeNames.Count > 0 && rawChargeNames.Count == rawChargeAmounts.Count);
                if (validChargingConfig && chargingConfigDeclared)
                {
                    for (int i = 0; i < rawChargeNames.Count; i++)
                    {
                        string chargeName = rawChargeNames[i]?.Trim();
                        if (string.IsNullOrEmpty(chargeName)
                            || !float.TryParse(rawChargeAmounts[i], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out float chargeAmount)
                            || float.IsNaN(chargeAmount) || float.IsInfinity(chargeAmount)
                            || chargeAmount <= 0f)
                        {
                            validChargingConfig = false;
                            break;
                        }
                        _chargeNames.Add(chargeName);
                        _chargeAmounts.Add(chargeAmount);
                    }
                }
                if (!validChargingConfig)
                {
                    KShared.LogError(
                        "Recipe \"" + _name + "\": CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS must contain aligned, non-empty names and finite positive amounts; the recipe was disabled.",
                        "KhemistryISRURecipe/constructor");
                    _chargeNames.Clear();
                    _chargeAmounts.Clear();
                    configurationError = true;
                }

                _chargeRate = KShared.GetFloatValueFromCFG(node, "chargeRate", 0f);
                _chargeDecay = KShared.GetFloatValueFromCFG(node, "chargeDecay", 0f);
                if (float.IsNaN(_chargeRate) || float.IsInfinity(_chargeRate) || _chargeRate < 0f
                    || float.IsNaN(_chargeDecay) || float.IsInfinity(_chargeDecay) || _chargeDecay < 0f)
                {
                    KShared.LogError("Recipe \"" + _name
                        + "\": chargeRate/chargeDecay must be finite and non-negative; charging disabled.",
                        "KhemistryISRURecipe/constructor");
                    _chargeRate = 0f;
                    _chargeDecay = 0f;
                    _chargeNames.Clear();
                    _chargeAmounts.Clear();
                }
                _chargingRequired = _chargeNames.Count > 0 && _chargeRate > 0f;
                if (chargingConfigDeclared && !_chargingRequired)
                {
                    KShared.LogError("Recipe \"" + _name
                        + "\": charging resources require a finite positive chargeRate; the recipe was disabled.",
                        "KhemistryISRURecipe/constructor");
                    configurationError = true;
                }

                ///// Planet/biome configs /////
                _planetConfigs.Clear();
                if (node.HasNode("PLANET_CONFIG"))
                {
                    foreach (ConfigNode planetNode in node.GetNodes("PLANET_CONFIG"))
                    {
                        string planetName = KShared.GetStrValueFromCFG(planetNode, "name", "ALL")?.Trim();
                        if (string.IsNullOrEmpty(planetName)) planetName = "ALL";

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
                            string biomeKey = string.IsNullOrEmpty(biomeConfig.biomeName)
                                ? "ALL" : biomeConfig.biomeName;
                            if (biomeDict.ContainsKey(biomeKey))
                            {
                                configurationError = true;
                                KShared.LogError("Recipe \"" + _name + "\" contains duplicate biome \""
                                    + biomeKey + "\" for planet \"" + planetName + "\".",
                                    "KhemistryISRURecipe/constructor");
                                continue;
                            }
                            biomeDict.Add(biomeKey, biomeConfig);
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
                    string resName = inputNode.GetValue("name")?.Trim();
                    if (string.IsNullOrEmpty(resName))
                    {
                        configurationError = true;
                        KShared.LogNoValueInNode("INPUT_RESOURCE", "name", "Recipe \"" + _name + "\" ",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    double amount = KShared.GetDoubleValueFromCFG(inputNode, "amount", 0.0);
                    if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": INPUT_RESOURCE \""
                            + resName + "\" has an invalid amount and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    ResourceFlowMode flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW;
                    string flowStr = inputNode.GetValue("flowmode");
                    if (!string.IsNullOrEmpty(flowStr))
                    {
                        if (Enum.TryParse(flowStr.Trim(), true, out ResourceFlowMode parsed)
                            && Enum.IsDefined(typeof(ResourceFlowMode), parsed))
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
                    string matName = matNode.GetValue("name")?.Trim();
                    if (string.IsNullOrEmpty(matName))
                    {
                        configurationError = true;
                        KShared.LogNoValueInNode("INPUT_MATERIAL", "name", "Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    string shape = matNode.GetValue("shape")?.Trim();
                    string size = matNode.GetValue("size")?.Trim();
                    bool validMaterialAmount = TryReadOptionalInt(matNode, "amount", 1,
                        out int materialAmount);
                    if (string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(size)
                        || !validMaterialAmount || materialAmount <= 0)
                    {
                        configurationError = true;
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
                    string resName = pinputNode.GetValue("name")?.Trim();
                    if (string.IsNullOrEmpty(resName))
                    {
                        configurationError = true;
                        KShared.LogNoValueInNode("PINPUT_RESOURCE", "name", "Recipe \"" + _name + "\" ", "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    double amount = KShared.GetDoubleValueFromCFG(pinputNode, "amount", 0.0);
                    double period = 1.0;
                    bool validPeriod = pinputNode.HasValue("period")
                        ? TryReadRequiredDouble(pinputNode, "period", out period)
                        : (!pinputNode.HasValue("peirod")
                            || TryReadRequiredDouble(pinputNode, "peirod", out period));
                    if (!pinputNode.HasValue("period") && pinputNode.HasValue("peirod"))
                        KShared.LogWarning("Recipe \"" + _name
                            + "\": PINPUT_RESOURCE uses legacy misspelling \"peirod\"; use \"period\".",
                            "KhemistryISRURecipe/constructor");
                    if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": PINPUT_RESOURCE \""
                            + resName + "\" has an invalid amount and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }
                    if (!validPeriod || double.IsNaN(period) || double.IsInfinity(period)
                        || period <= 0.0)
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": PINPUT_RESOURCE \""
                            + resName + "\" has an invalid period and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    ResourceFlowMode flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW;
                    string pFlowStr = pinputNode.GetValue("flowmode");
                    if (!string.IsNullOrEmpty(pFlowStr))
                    {
                        if (Enum.TryParse(pFlowStr.Trim(), true, out ResourceFlowMode pParsed)
                            && Enum.IsDefined(typeof(ResourceFlowMode), pParsed))
                            flowMode = pParsed;
                        else
                            KShared.LogError(
                                "Recipe \"" + _name + "\": Unknown flowmode \"" + pFlowStr + "\" for PINPUT_RESOURCE " + resName + ", defaulting to STAGE_PRIORITY_FLOW.",
                                "KhemistryISRURecipe/constructor");
                    }

                    bool ignorePowerfail = false;
                    string ignorePowerfailRaw = pinputNode.GetValue("ignorePowerfail");
                    if (!string.IsNullOrEmpty(ignorePowerfailRaw)
                        && !bool.TryParse(ignorePowerfailRaw.Trim(), out ignorePowerfail))
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": PINPUT_RESOURCE \""
                            + resName + "\" has an invalid ignorePowerfail value \""
                            + ignorePowerfailRaw + "\" and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    // Accept both spellings: "powerfail" (correct, used in actual configs) and
                    // "powefail" (the original literal spec) — the former takes precedence.
                    PowerfailResult powerfail = PowerfailResult.Pause;
                    double explosionRadius = 0.0;
                    double explosionTemperature = 0.0;
                    string pfRaw = pinputNode.GetValue("powerfail") ?? pinputNode.GetValue("powefail");
                    if (!string.IsNullOrEmpty(pfRaw))
                    {
                        string pf = pfRaw.Trim().Trim('"').ToUpperInvariant();
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
                                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tempC)
                                && radius > 0.0 && !double.IsNaN(radius) && !double.IsInfinity(radius)
                                && !double.IsNaN(tempC) && !double.IsInfinity(tempC))
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
                    string resName = outputNode.GetValue("name")?.Trim();
                    if (string.IsNullOrEmpty(resName))
                    {
                        configurationError = true;
                        KShared.LogNoValueInNode("OUTPUT_RESOURCE", "name", "Recipe \"" + _name + "\" ",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    double amount = KShared.GetDoubleValueFromCFG(outputNode, "amount", 0.0);
                    if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": OUTPUT_RESOURCE \""
                            + resName + "\" has an invalid amount and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }
                    bool dumpExcess = false;
                    string dumpExcessRaw = outputNode.GetValue("dumpExcess");
                    if (!string.IsNullOrEmpty(dumpExcessRaw)
                        && !bool.TryParse(dumpExcessRaw.Trim(), out dumpExcess))
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": OUTPUT_RESOURCE \""
                            + resName + "\" has an invalid dumpExcess value \""
                            + dumpExcessRaw + "\" and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    _outputs.Add(new ResourceOutput { resourceName = resName, amount = amount, dumpExcess = dumpExcess });
                }

                ///// Output materials /////
                _outputMaterials.Clear();
                foreach (ConfigNode matNode in node.GetNodes("OUTPUT_MATERIAL"))
                {
                    string matName = matNode.GetValue("name")?.Trim();
                    if (string.IsNullOrEmpty(matName))
                    {
                        configurationError = true;
                        KShared.LogNoValueInNode("OUTPUT_MATERIAL", "name", "Recipe \"" + _name + "\" ",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    string shape = matNode.GetValue("shape")?.Trim();
                    string size = matNode.GetValue("size")?.Trim();
                    bool validOutputAmount = TryReadOptionalDouble(matNode, "amount", 1.0,
                        out double amount);
                    string outVolume = KShared.GetStrValueFromCFG(matNode, "outVolume", null)?.Trim();

                    if (string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(size)
                        || string.IsNullOrEmpty(outVolume)
                        || !validOutputAmount || double.IsNaN(amount)
                        || double.IsInfinity(amount) || amount <= 0.0)
                    {
                        configurationError = true;
                        KShared.LogError(
                            "Recipe \"" + _name + "\": OUTPUT_MATERIAL \"" + matName
                            + "\" requires non-empty shape/size/outVolume and an amount greater than zero; entry skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    if (double.TryParse(outVolume, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double literalVolume)
                        && (double.IsNaN(literalVolume) || double.IsInfinity(literalVolume)
                            || literalVolume <= 0.0))
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": OUTPUT_MATERIAL \"" + matName
                            + "\" has a non-positive or non-finite literal outVolume and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    bool usesParams = matNode.HasNode("PARAMS");
                    Dictionary<string, string> parameters = new Dictionary<string, string>();
                    bool validParameters = true;
                    if (usesParams)
                    {
                        HashSet<string> parameterNames =
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (ConfigNode.Value parameter in matNode.GetNode("PARAMS").values)
                        {
                            string key = parameter.name?.Trim();
                            if (string.IsNullOrEmpty(key)
                                || !Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$")
                                || string.Equals(key, "size", StringComparison.OrdinalIgnoreCase)
                                || !parameterNames.Add(key))
                            {
                                validParameters = false;
                                break;
                            }
                            parameters.Add(key, parameter.value);
                        }
                    }

                    if (!validParameters)
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": OUTPUT_MATERIAL \""
                            + matName + "\" has an empty, invalid, reserved, or case-duplicated "
                            + "parameter name and was skipped.",
                            "KhemistryISRURecipe/constructor");
                        continue;
                    }

                    if (!TryValidateOutVolumeDefinition(outVolume, size, parameters,
                            out string outVolumeError))
                    {
                        configurationError = true;
                        KShared.LogError("Recipe \"" + _name + "\": OUTPUT_MATERIAL \""
                            + matName + "\" has invalid outVolume: " + outVolumeError
                            + " Entry skipped.", "KhemistryISRURecipe/constructor");
                        continue;
                    }

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
                {
                    configurationError = true;
                    KShared.LogError(
                        "Recipe \"" + _name + "\" has no OUTPUT_RESOURCE nor OUTPUT_MATERIAL nodes — it will do nothing.",
                        "KhemistryISRURecipe/constructor");
                }

                ///// Timing and control /////
                _recipeTime = KShared.GetDoubleValueFromCFG(node, "recipeTime", 0.0);
                if (double.IsNaN(_recipeTime) || double.IsInfinity(_recipeTime) || _recipeTime <= 0.0)
                {
                    configurationError = true;
                    KShared.LogError(
                        "Recipe \"" + _name + "\" has no valid recipeTime set — it will never complete a batch.",
                        "KhemistryISRURecipe/constructor");
                }

                KShared.ParseShowRule(
                    KShared.GetStrValueFromCFG(node, "controlRules", "PAW"),
                    out _controlsShowPAW, out _controlsShowEVA, "controlRules", _name);

                ///// Workers /////
                _workersEngineers = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersEngineers", 0));
                _workersPilots = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersPilots", 0));
                _workersScientists = (uint)Math.Max(0, KShared.GetIntValueFromCFG(node, "workersScientists", 0));

                _workersEVA = true;
                _workersCREW = false;
                string workersTypeStr = KShared.GetStrValueFromCFG(node, "workersType", "EVA").Trim().ToUpperInvariant();
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
                IsValid = !configurationError && _recipeTime > 0.0 && !double.IsNaN(_recipeTime)
                    && !double.IsInfinity(_recipeTime)
                    && (_outputs.Count > 0 || _outputMaterials.Count > 0);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryISRURecipe/constructor");
            }
        }

        private static void AddTrimmedDistinct(List<string> destination, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (string value in values)
            {
                string trimmed = value?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !destination.Contains(trimmed))
                    destination.Add(trimmed);
            }
        }

        private static bool TryReadRequiredDouble(ConfigNode node, string key,
            out double value)
        {
            value = 0.0;
            return node != null && node.HasValue(key)
                && double.TryParse(node.GetValue(key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryReadOptionalDouble(ConfigNode node, string key,
            double defaultValue, out double value)
        {
            value = defaultValue;
            return node == null || !node.HasValue(key)
                || TryReadRequiredDouble(node, key, out value);
        }

        private static bool TryReadOptionalInt(ConfigNode node, string key, int defaultValue,
            out int value)
        {
            value = defaultValue;
            return node == null || !node.HasValue(key)
                || int.TryParse(node.GetValue(key), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value);
        }

        private static bool TryValidateOutVolumeDefinition(string expression, string size,
            Dictionary<string, string> parameters, out string error)
        {
            string localError = null;
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, KMathExpr.ValueRange> ranges =
                new Dictionary<string, KMathExpr.ValueRange>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> aliases =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> parameter in
                     parameters ?? new Dictionary<string, string>())
                values[parameter.Key] = parameter.Value;

            bool failed = false;
            string substituted = OutVolumeTokenPattern.Replace(expression ?? "", match =>
            {
                string name = match.Groups[1].Value;
                string raw = string.Equals(name, "size", StringComparison.OrdinalIgnoreCase)
                    ? size
                    : (values.TryGetValue(name, out string value) ? value : null);
                if (raw == null)
                {
                    localError = "[" + name + "] does not refer to size or a defined parameter.";
                    failed = true;
                    return "0";
                }

                if (!TryGetOutputNumberRange(raw, out KMathExpr.ValueRange range,
                        out string valueError))
                {
                    localError = "[" + name + "] uses invalid value \"" + raw + "\": " + valueError;
                    failed = true;
                    return "0";
                }
                if (!aliases.TryGetValue(name, out string alias))
                {
                    alias = "__outVolume" + aliases.Count;
                    aliases[name] = alias;
                    ranges[alias] = range;
                }
                return alias;
            });
            if (failed)
            {
                error = localError;
                return false;
            }

            if (!KMathExpr.TryEvaluateRange(substituted,
                    out KMathExpr.ValueRange result, out string expressionError, ranges))
            {
                error = expressionError;
                return false;
            }
            if (result.Minimum <= 0.0)
            {
                error = "the configured value range can produce a non-positive per-unit volume.";
                return false;
            }
            if (result.Maximum > float.MaxValue)
            {
                error = "the configured value range can exceed the supported per-unit volume.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryGetOutputNumberRange(string raw,
            out KMathExpr.ValueRange result,
            out string error)
        {
            error = null;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double number))
            {
                result = new KMathExpr.ValueRange(number, number);
                if (!double.IsNaN(number) && !double.IsInfinity(number)) return true;
                error = "the number is not finite.";
                return false;
            }

            Match random = RandfValuePattern.Match(raw?.Trim() ?? "");
            if (!random.Success)
            {
                result = new KMathExpr.ValueRange();
                error = "expected a finite number or randf(min,max,decimalPlaces).";
                return false;
            }
            if (!double.TryParse(random.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double minimum)
                || !double.TryParse(random.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double maximum)
                || double.IsNaN(minimum) || double.IsInfinity(minimum)
                || double.IsNaN(maximum) || double.IsInfinity(maximum)
                || !int.TryParse(random.Groups[3].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int decimalPlaces)
                || decimalPlaces < 0 || decimalPlaces > 15)
            {
                result = new KMathExpr.ValueRange();
                error = "randf bounds must be finite and decimalPlaces must be between 0 and 15.";
                return false;
            }

            double low = Math.Round(Math.Min(minimum, maximum), decimalPlaces,
                MidpointRounding.AwayFromZero);
            double high = Math.Round(Math.Max(minimum, maximum), decimalPlaces,
                MidpointRounding.AwayFromZero);
            result = new KMathExpr.ValueRange(low, high);
            if (!double.IsNaN(low) && !double.IsInfinity(low)
                && !double.IsNaN(high) && !double.IsInfinity(high)) return true;
            error = "the randf range is not finite after rounding.";
            return false;
        }

        /// <summary>
        /// Verifies references that cannot be checked until KSP resources and Khemistry material
        /// definitions have finished loading. Invalid recipes are disabled rather than being run
        /// with a silently omitted input or an unusable output.
        /// </summary>
        public bool ValidateReferences(IEnumerable<KhemistryMaterial> materials, string context)
        {
            if (!IsValid) return false;

            bool valid = true;
            PartResourceLibrary resourceLibrary = PartResourceLibrary.Instance;
            if (resourceLibrary != null)
            {
                foreach (string resourceName in _inputs.Select(input => input.resourceName)
                    .Concat(_passiveInputs.Select(input => input.resourceName))
                    .Concat(_outputs.Select(output => output.resourceName))
                    .Concat(_chargeNames).Distinct())
                {
                    if (resourceLibrary.GetDefinition(resourceName) != null) continue;
                    valid = false;
                    KShared.LogError("Recipe \"" + _name + "\" references unknown resource \""
                        + resourceName + "\".", context);
                }
            }

            List<KhemistryMaterial> definitions = materials?.Where(material => material != null).ToList()
                ?? new List<KhemistryMaterial>();

            foreach (ResourceInputMaterial input in _inputMaterials)
            {
                KhemistryMaterial definition = definitions.FirstOrDefault(material => material.name == input.name);
                if (!ValidateMaterialReference(definition, input.name, input.shape,
                        input.parameters?.Keys, "INPUT_MATERIAL", context))
                    valid = false;
            }

            foreach (ResourceOutputMaterial output in _outputMaterials)
            {
                KhemistryMaterial definition = definitions.FirstOrDefault(material => material.name == output.name);
                if (!ValidateMaterialReference(definition, output.name, output.shape,
                        output.parameters?.Keys, "OUTPUT_MATERIAL", context))
                    valid = false;
            }

            IsValid = valid;
            return valid;
        }

        private bool ValidateMaterialReference(KhemistryMaterial definition, string materialName,
            string shape, IEnumerable<string> parameterNames, string nodeName, string context)
        {
            if (definition == null)
            {
                KShared.LogError("Recipe \"" + _name + "\": " + nodeName + " \""
                    + materialName + "\" does not match a loaded KHEMISTRY_MATERIAL.", context);
                return false;
            }

            bool valid = true;
            if (!definition.shapes.Contains(shape))
            {
                valid = false;
                KShared.LogError("Recipe \"" + _name + "\": " + nodeName + " \""
                    + materialName + "\" uses unsupported shape \"" + shape + "\".", context);
            }

            foreach (string parameterName in parameterNames ?? Enumerable.Empty<string>())
            {
                if (definition.parameters.ContainsKey(parameterName)) continue;
                valid = false;
                KShared.LogError("Recipe \"" + _name + "\": " + nodeName + " \""
                    + materialName + "\" references unknown parameter \"" + parameterName + "\".",
                    context);
            }
            return valid;
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
            copy.IsValid = IsValid;

            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0.0)
                multiplier = 1.0;

            foreach (ResourceInput inp in _inputs)
                copy._inputs.Add(new ResourceInput { resourceName = inp.resourceName, amount = inp.amount * multiplier, flowMode = inp.flowMode });
            foreach (ResourceInputMaterial mat in _inputMaterials)
            {
                double scaled = Math.Round(mat.amount * multiplier, MidpointRounding.AwayFromZero);
                int scaledAmount = scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
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
            "recipeType", "recipeSubtype", "recipeSubype", "recipeSubsubtype",
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
