using System;
using System.Collections.Generic;

namespace ChitakTech
{
    /// <summary>
    /// A config used in <see cref="ChitakTechISRU"/> for each biome.
    /// Contains a lot of conditions when the recipe can work.
    /// </summary>
    public class ChitakTechISRUBiomeConfig
    {
        public string biomeName;

        /// <summary>If this biome config is disabled (converter cannot run in this biome)</summary>
        public bool disabled = false;

        /// <summary>Maximum operational altitude in meters.</summary>
        public double maxOperatingAltitude = double.MaxValue;
        /// <summary>Minimum operational altitude in meters.</summary>
        public double minOperatingAltitude = double.MinValue;
        /// <summary>Maximum survivable altitude in meters.</summary>
        public double maxAltitude = double.MaxValue;
        /// <summary>Minimum survivable altitude in meters.</summary>
        public double minAltitude = double.MinValue;

        /// <summary>Maximum operational G-force.</summary>
        public double maxOperatingG = double.MaxValue;
        /// <summary>Minimum operational G-force.</summary>
        public double minOperatingG = 0.0;
        /// <summary>Maximum survivable G-force.</summary>
        public double maxG = double.MaxValue;
        /// <summary>Minimum survivable G-force.</summary>
        public double minG = 0.0;

        /// <summary>Conditions where the recipe can operate in.</summary>
        public List<CTShared.SituationCondition> situationOperating = new List<CTShared.SituationCondition>();
        /// <summary>Conditions where the recipe will be destroyed in.</summary>
        public List<CTShared.SituationCondition> situationDestructive = new List<CTShared.SituationCondition>();

        public double maxOperatingTemperature = double.MaxValue;
        public double minOperatingTemperature = double.MinValue;
        public double maxTemperature = double.MaxValue;
        public double minTemperature = double.MinValue;

        /// <summary>Maximum operational pressure in kPa.</summary>
        public double maxOperatingPressure = double.MaxValue;
        /// <summary>Minimum operational pressure in kPa.</summary>
        public double minOperatingPressure = 0.0;
        /// <summary>Maximum survivable pressure in kPa.</summary>
        public double maxPressure = double.MaxValue;
        /// <summary>Minimum survivable pressure in kPa.</summary>
        public double minPressure = 0.0;

        /// <summary>How much to multiply passive consumption resource amounts by.</summary>
        public double passiveMultiplier = 1.0;
        /// <summary>How much to multiply passive consumption's period by.</summary>
        public double passivePeriodMultiplier = 1.0;

        /// <summary>How much to multiply charge rate by.</summary>
        public double chargeRateMultiplier = 1.0;
        /// <summary>How much to multiply charge decay by.</summary>
        public double chargeDecayRateInactiveMultiplier = 1.0;
        public double chargeThresholdMultiplier = 1.0;
        public double chargeDecayMultiplier = 1.0;
        /// <summary>How much to multiply charging consumption resource amounts by.</summary>
        public double chargeConsumptionMultiplier = 1.0;

        /// <summary>How much to input resource amounts by.</summary>
        public double inputMultiplier = 1.0;
        /// <summary>How much to output resource amounts by.</summary>
        public double outputMultiplier = 1.0;

        /// <summary>How much to multiply recipeTime by.</summary>
        public double speedMul = 1.0;

        // Explicit allowlist shared by maintenance parsing and multiplier composition.
        internal static readonly Dictionary<string, string> MultiplierFields =
            new Dictionary<string, string>
            {
                { "passiveMul", nameof(passiveMultiplier) },
                { "inMul", nameof(inputMultiplier) },
                { "outMul", nameof(outputMultiplier) },
                { "speedMul", nameof(speedMul) },
                { "chargeRateMul", nameof(chargeRateMultiplier) },
                { "chargeDecayRateInactiveMul", nameof(chargeDecayRateInactiveMultiplier) },
                { "chargeThresholdMul", nameof(chargeThresholdMultiplier) },
                { "chargeDecayMul", nameof(chargeDecayMultiplier) },
                { "chargeConMul", nameof(chargeConsumptionMultiplier) },
                { "passivePeriodMul", nameof(passivePeriodMultiplier) },
                { "workersEngineersMul", nameof(workersEngineersMultiplier) },
                { "workersPilotsMul", nameof(workersPilotsMultiplier) },
                { "workersScientistsMul", nameof(workersScientistsMultiplier) },
                { "maxInteractionDistanceMul", nameof(maxInteractionDistanceMultiplier) },
                { "maxDisplayDistanceMul", nameof(maxDisplayDistanceMultiplier) }
            };

        public ChitakTechISRUBiomeConfig WithMaintenance(IEnumerable<MaintenanceStage> stages)
        {
            var copy = (ChitakTechISRUBiomeConfig)MemberwiseClone();
            foreach (MaintenanceStage stage in stages)
                foreach (var mapping in MultiplierFields)
                {
                    if (!stage.multipliers.TryGetValue(mapping.Key, out double multiplier)) continue;
                    var field = typeof(ChitakTechISRUBiomeConfig).GetField(mapping.Value);
                    double value = (double)field.GetValue(copy) * multiplier;
                    if (double.IsInfinity(value)) value = double.MaxValue;
                    if (mapping.Key == "speedMul" || mapping.Key == "passivePeriodMul")
                        value = Math.Max(double.Epsilon, value);
                    field.SetValue(copy, value);
                }
            return copy;
        }

        /// <summary>How much to multiply amount of pilot workers by.</summary>
        public double workersPilotsMultiplier = 1.0;
        /// <summary>How much to multiply amount of engineer workers by.</summary>
        public double workersEngineersMultiplier = 1.0;
        /// <summary>How much to multiply amount of scientist workers by.</summary>
        public double workersScientistsMultiplier = 1.0;

        /// <summary>How much to multiply max interaction distance by.</summary>
        public double maxInteractionDistanceMultiplier = 1.0;
        /// <summary>How much to multiply max display distance by.</summary>
        public double maxDisplayDistanceMultiplier = 1.0;

        /// <summary>What deposits the recipe can work in.</summary>
        public List<string> depositConditions = new List<string>();

        public readonly List<ChitakTechISRURecipe.ResourceInput> inputs
            = new List<ChitakTechISRURecipe.ResourceInput>();
        public readonly List<ChitakTechISRURecipe.ResourceOutput> outputs
            = new List<ChitakTechISRURecipe.ResourceOutput>();
        public bool hasResourceConfigurationError = false;

        ///// Functions /////
        /// <summary>
        /// Make a biome config from a biome config node in a ISRU recipe.
        /// </summary>
        /// <param name="node">The node BIOME_CONFIG in PLANET_CONFIG in a ISRU module.</param>
        /// <param name="ConverterName">The name of the converter the biome config belongs to.</param>
        /// <param name="recipeName">The recipe name used when reporting invalid biome-local resources.</param>
        public ChitakTechISRUBiomeConfig(ConfigNode node, string ConverterName = "UNKNOWN",
            string recipeName = "UNKNOWN")
        {
            if (node != null)
            {
                biomeName = CTShared.GetStrValueFromCFG(node, "name", "ALL")?.Trim();
                if (string.IsNullOrEmpty(biomeName)) biomeName = "ALL";

                // "disabled" is the documented/configured key. Keep "disable" as a
                // backwards-compatible alias for older third-party recipes.
                string disabledValue = node.HasValue("disabled") ? node.GetValue("disabled")
                    : (node.HasValue("disable") ? node.GetValue("disable") : null);
                if (disabledValue != null && !bool.TryParse(disabledValue, out disabled))
                    CTShared.LogError(
                        "Converter \"" + ConverterName + "\": Biome config \"" + biomeName
                        + "\": invalid disabled value \"" + disabledValue + "\"; defaulting to false.",
                        "ChitakTechISRUBiomeConfig/constructor");

                // Altitude
                minOperatingAltitude = CTShared.GetDoubleValueFromCFG(node, "minOperatingAltitude", minOperatingAltitude);
                maxOperatingAltitude = CTShared.GetDoubleValueFromCFG(node, "maxOperatingAltitude", maxOperatingAltitude);
                minAltitude = CTShared.GetDoubleValueFromCFG(node, "minAltitude", minAltitude);
                maxAltitude = CTShared.GetDoubleValueFromCFG(node, "maxAltitude", maxAltitude);

                // G-Force
                minOperatingG = CTShared.GetDoubleValueFromCFG(node, "minOperatingG", minOperatingG);
                maxOperatingG = CTShared.GetDoubleValueFromCFG(node, "maxOperatingG", maxOperatingG);
                maxG = CTShared.GetDoubleValueFromCFG(node, "maxG", maxG);
                minG = CTShared.GetDoubleValueFromCFG(node, "minG", minG);

                // Situation
                situationOperating.Clear();
                foreach (string situationOperatingStr in node.GetValues("situationOperating"))
                {
                    if (Enum.TryParse(situationOperatingStr, true, out CTShared.SituationCondition parsed)
                        && Enum.IsDefined(typeof(CTShared.SituationCondition), parsed))
                        situationOperating.Add(parsed);
                    else
                        CTShared.LogError(
                            "Converter \"" + ConverterName + "\": Biome config \"" + biomeName + "\": Unknown situationOperating situationCondition \"" + situationOperatingStr + "\" — condition ignored.",
                            "ChitakTechISRU/LoadSharedConfig");
                }
                situationDestructive.Clear();
                foreach (string situationDestructiveStr in node.GetValues("situationDestructive"))
                {
                    if (Enum.TryParse(situationDestructiveStr, true, out CTShared.SituationCondition parsed)
                        && Enum.IsDefined(typeof(CTShared.SituationCondition), parsed))
                        situationDestructive.Add(parsed);
                    else
                        CTShared.LogError(
                            "Converter \"" + ConverterName + "\": Biome config \"" + biomeName + "\": Unknown situationDestructive situationCondition \"" + situationDestructiveStr + "\" — condition ignored.",
                            "ChitakTechISRU/LoadSharedConfig");
                }

                // Conditions
                depositConditions.Clear();
                foreach (string depositConditionStr in node.GetValues("depositCondition"))
                {
                    string trimmed = depositConditionStr?.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !depositConditions.Contains(trimmed))
                        depositConditions.Add(trimmed);
                }

                // Temperature
                minOperatingTemperature = CTShared.GetDoubleTemperatureValueFromCFG(node, "minOperatingTemperature", minOperatingTemperature);
                maxOperatingTemperature = CTShared.GetDoubleTemperatureValueFromCFG(node, "maxOperatingTemperature", maxOperatingTemperature);
                minTemperature = CTShared.GetDoubleTemperatureValueFromCFG(node, "minTemperature", minTemperature);
                maxTemperature = CTShared.GetDoubleTemperatureValueFromCFG(node, "maxTemperature", maxTemperature);

                // Pressure
                minOperatingPressure = CTShared.GetDoubleValueFromCFG(node, "minOperatingPressure", minOperatingPressure);
                maxOperatingPressure = CTShared.GetDoubleValueFromCFG(node, "maxOperatingPressure", maxOperatingPressure);
                minPressure = CTShared.GetDoubleValueFromCFG(node, "minPressure", minPressure);
                maxPressure = CTShared.GetDoubleValueFromCFG(node, "maxPressure", maxPressure);

                minOperatingG = ValidateNonNegative(minOperatingG, "minOperatingG",
                    ConverterName);
                maxOperatingG = ValidateNonNegative(maxOperatingG, "maxOperatingG",
                    ConverterName);
                minG = ValidateNonNegative(minG, "minG", ConverterName);
                maxG = ValidateNonNegative(maxG, "maxG", ConverterName);
                minOperatingPressure = ValidateNonNegative(minOperatingPressure,
                    "minOperatingPressure", ConverterName);
                maxOperatingPressure = ValidateNonNegative(maxOperatingPressure,
                    "maxOperatingPressure", ConverterName);
                minPressure = ValidateNonNegative(minPressure, "minPressure",
                    ConverterName);
                maxPressure = ValidateNonNegative(maxPressure, "maxPressure",
                    ConverterName);


                // Passive multipliers
                passiveMultiplier = CTShared.GetDoubleValueFromCFG(node, "passiveMul", passiveMultiplier);
                passivePeriodMultiplier = CTShared.GetDoubleValueFromCFG(node, "passivePeriodMul", passivePeriodMultiplier);

                // Charge multipliers
                chargeRateMultiplier = CTShared.GetDoubleValueFromCFG(node, "chargeRateMul", chargeRateMultiplier);
                chargeDecayRateInactiveMultiplier = CTShared.GetDoubleValueFromCFG(node, "chargeDecayRateInactiveMul", chargeDecayRateInactiveMultiplier);
                chargeThresholdMultiplier = CTShared.GetDoubleValueFromCFG(node, "chargeThresholdMul", chargeThresholdMultiplier);
                chargeDecayMultiplier = CTShared.GetDoubleValueFromCFG(node, "chargeDecayMul", chargeDecayMultiplier);
                chargeConsumptionMultiplier = CTShared.GetDoubleValueFromCFG(node, "chargeConMul", chargeConsumptionMultiplier);

                // I/O multipliers
                inputMultiplier = CTShared.GetDoubleValueFromCFG(node, "inMul", inputMultiplier);
                outputMultiplier = CTShared.GetDoubleValueFromCFG(node, "outMul", outputMultiplier);

                // Speed multiplier
                speedMul = CTShared.GetDoubleValueFromCFG(node, "speedMul", speedMul);

                // Worker multipliers
                workersPilotsMultiplier = CTShared.GetDoubleValueFromCFG(node, "workersPilotsMul", workersPilotsMultiplier);
                workersEngineersMultiplier = CTShared.GetDoubleValueFromCFG(node, "workersEngineersMul", workersEngineersMultiplier);
                workersScientistsMultiplier = CTShared.GetDoubleValueFromCFG(node, "workersScientistsMul", workersScientistsMultiplier);

                // Max distances multipliers
                maxInteractionDistanceMultiplier = CTShared.GetDoubleValueFromCFG(node, "maxInteractionDistanceMul", maxInteractionDistanceMultiplier);
                maxDisplayDistanceMultiplier = CTShared.GetDoubleValueFromCFG(node, "maxDisplayDistanceMul", maxDisplayDistanceMultiplier);

                passiveMultiplier = ValidateMultiplier(passiveMultiplier, "passiveMul", true, ConverterName);
                passivePeriodMultiplier = ValidateMultiplier(passivePeriodMultiplier, "passivePeriodMul", false, ConverterName);
                chargeRateMultiplier = ValidateMultiplier(chargeRateMultiplier, "chargeRateMul", true, ConverterName);
                chargeDecayRateInactiveMultiplier = ValidateMultiplier(chargeDecayRateInactiveMultiplier, "chargeDecayRateInactiveMul", true, ConverterName);
                chargeThresholdMultiplier = ValidateMultiplier(chargeThresholdMultiplier, "chargeThresholdMul", true, ConverterName);
                chargeDecayMultiplier = ValidateMultiplier(chargeDecayMultiplier, "chargeDecayMul", true, ConverterName);
                chargeConsumptionMultiplier = ValidateMultiplier(chargeConsumptionMultiplier, "chargeConMul", true, ConverterName);
                inputMultiplier = ValidateMultiplier(inputMultiplier, "inMul", true, ConverterName);
                outputMultiplier = ValidateMultiplier(outputMultiplier, "outMul", true, ConverterName);
                speedMul = ValidateMultiplier(speedMul, "speedMul", false, ConverterName);
                workersPilotsMultiplier = ValidateMultiplier(workersPilotsMultiplier, "workersPilotsMul", true, ConverterName);
                workersEngineersMultiplier = ValidateMultiplier(workersEngineersMultiplier, "workersEngineersMul", true, ConverterName);
                workersScientistsMultiplier = ValidateMultiplier(workersScientistsMultiplier, "workersScientistsMul", true, ConverterName);
                maxInteractionDistanceMultiplier = ValidateMultiplier(maxInteractionDistanceMultiplier, "maxInteractionDistanceMul", true, ConverterName);
                maxDisplayDistanceMultiplier = ValidateMultiplier(maxDisplayDistanceMultiplier, "maxDisplayDistanceMul", true, ConverterName);

                string resourceContext = "Recipe \"" + recipeName + "\": BIOME_CONFIG \""
                    + biomeName + "\"";
                foreach (ConfigNode inputNode in node.GetNodes("INPUT_RESOURCE"))
                {
                    if (ChitakTechISRURecipe.TryParseResourceInput(inputNode, resourceContext,
                            out ChitakTechISRURecipe.ResourceInput input))
                        inputs.Add(input);
                    else
                        hasResourceConfigurationError = true;
                }
                foreach (ConfigNode outputNode in node.GetNodes("OUTPUT_RESOURCE"))
                {
                    if (ChitakTechISRURecipe.TryParseResourceOutput(outputNode, resourceContext,
                            out ChitakTechISRURecipe.ResourceOutput output))
                        outputs.Add(output);
                    else
                        hasResourceConfigurationError = true;
                }

                ValidateRange(ref minOperatingAltitude, ref maxOperatingAltitude,
                    "minOperatingAltitude", "maxOperatingAltitude", ConverterName);
                ValidateRange(ref minAltitude, ref maxAltitude,
                    "minAltitude", "maxAltitude", ConverterName);
                ValidateRange(ref minOperatingG, ref maxOperatingG,
                    "minOperatingG", "maxOperatingG", ConverterName);
                ValidateRange(ref minG, ref maxG, "minG", "maxG", ConverterName);
                ValidateRange(ref minOperatingTemperature, ref maxOperatingTemperature,
                    "minOperatingTemperature", "maxOperatingTemperature", ConverterName);
                ValidateRange(ref minTemperature, ref maxTemperature,
                    "minTemperature", "maxTemperature", ConverterName);
                ValidateRange(ref minOperatingPressure, ref maxOperatingPressure,
                    "minOperatingPressure", "maxOperatingPressure", ConverterName);
                ValidateRange(ref minPressure, ref maxPressure,
                    "minPressure", "maxPressure", ConverterName);
            }
            else
            {
                biomeName = "ALL";
                CTShared.LogError("Converter \"" + ConverterName
                    + "\": Cannot load a null BIOME_CONFIG.",
                    "ChitakTechISRUBiomeConfig/constructor");
                return;
            }
        }

        private double ValidateNonNegative(double value, string fieldName,
            string converterName)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0)
                return value;

            CTShared.LogError(
                "Converter \"" + converterName + "\": Biome config \"" + biomeName
                + "\": " + fieldName + " cannot be negative; using 0.",
                "ChitakTechISRUBiomeConfig/constructor");
            return 0.0;
        }

        private double ValidateMultiplier(double value, string fieldName, bool allowZero, string converterName)
        {
            bool valid = !double.IsNaN(value) && !double.IsInfinity(value)
                && (allowZero ? value >= 0.0 : value > 0.0);
            if (valid) return value;

            CTShared.LogError(
                "Converter \"" + converterName + "\": Biome config \"" + biomeName + "\": "
                + fieldName + " must be " + (allowZero ? "non-negative" : "greater than zero")
                + "; defaulting to 1.",
                "ChitakTechISRUBiomeConfig/constructor");
            return 1.0;
        }

        private void ValidateRange(ref double minimum, ref double maximum,
            string minimumName, string maximumName, string converterName)
        {
            if (minimum <= maximum) return;

            CTShared.LogError(
                "Converter \"" + converterName + "\": Biome config \"" + biomeName + "\": "
                + minimumName + " is greater than " + maximumName + "; swapping the values.",
                "ChitakTechISRUBiomeConfig/constructor");
            double swap = minimum;
            minimum = maximum;
            maximum = swap;
        }
    }
}
