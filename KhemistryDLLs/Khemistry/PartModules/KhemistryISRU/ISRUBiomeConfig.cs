using System;
using System.Collections.Generic;

namespace Khemistry
{
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
}
