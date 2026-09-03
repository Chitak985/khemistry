using System;
using System.Collections.Generic;

using System.Globalization;

namespace Khemistry
{
    public partial class KShared
    {
        // Help get a value safely
        public static int GetIntValueFromCFG(ConfigNode node, string value, int defaultValue)
        {
            if (node != null && node.HasValue(value))
                if (int.TryParse(node.GetValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tmp))
                    return tmp;
            return defaultValue;
        }
        public static float GetFloatValueFromCFG(ConfigNode node, string value, float defaultValue)
        {
            if (node != null && node.HasValue(value))
                if (float.TryParse(node.GetValue(value), NumberStyles.Float, CultureInfo.InvariantCulture, out float tmp))
                    if (!float.IsNaN(tmp) && !float.IsInfinity(tmp)) return tmp;
            return defaultValue;
        }
        public static double GetDoubleValueFromCFG(ConfigNode node, string value, double defaultValue)
        {
            if (node != null && node.HasValue(value))
                if (double.TryParse(node.GetValue(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double tmp))
                    if (!double.IsNaN(tmp) && !double.IsInfinity(tmp)) return tmp;
            return defaultValue;
        }
        public static string GetStrValueFromCFG(ConfigNode node, string value, string defaultValue)
            => (node != null && node.HasValue(value)) ? node.GetValue(value) : defaultValue;

        /// <summary>
        /// Gets charging nodes from a config node.
        /// Note that it will return empty lists of both if a length mismatch occurs,
        /// there are no values inside either of the nodes, or the nodes aren't present.
        /// An error will only be logged if a length mismatch happens.
        /// </summary>
        /// <param name="moduleNode">The node containing (or not) CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS.</param>
        /// <param name="amounts">The <c>List&lt;float&gt;</c> for the charging amounts.</param>
        /// <returns>The <c>List&lt;string&gt;</c> for the charging resource names.</returns>
        public static List<string> GetChargingFromCFG(ConfigNode moduleNode, out List<float> amounts)
        {
            amounts = new List<float>();
            List<string> names = new List<string>();

            if (moduleNode == null)
                return names;

            bool invalid = false;

            if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                {
                    string trimmed = n?.Trim();
                    if (string.IsNullOrEmpty(trimmed)) invalid = true;
                    else names.Add(trimmed);
                }
            if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float tmp)
                        && !float.IsNaN(tmp) && !float.IsInfinity(tmp) && tmp > 0f)
                        amounts.Add(tmp);
                    else
                        invalid = true;

            if (invalid || names.Count != amounts.Count)
            {
                amounts.Clear();
                names.Clear();
                KShared.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS must contain equal numbers of non-empty names and finite positive amounts.",
                    "KShared/GetChargingFromCFG");
            }
            return names;
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
            if (node != null && node.HasValue(value))
            {
                string val = node.GetValue(value).Trim();
                if (val.Length == 0) return defaultValue;

                char suffix = char.ToUpperInvariant(val[val.Length - 1]);
                bool hasUnit = suffix == 'K' || suffix == 'C' || suffix == 'F';
                string numericText = hasUnit ? val.Substring(0, val.Length - 1).Trim() : val;
                if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    return defaultValue;
                if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return defaultValue;

                double converted = !hasUnit || suffix == 'K' ? parsed
                    : suffix == 'C' ? parsed + 273.15
                    : DoubleFarenheitToCelsius(parsed) + 273.15;
                return double.IsNaN(converted) || double.IsInfinity(converted)
                    ? defaultValue : converted;
            }
            return defaultValue;
        }

        /// <summary>
        /// Finds and returns the MODULE config node for this converter from partConfig.
        /// Matches on both module class name and ConverterName to support multiple
        /// converters per part. Pass the expected module name (e.g. "KhemistryAdvancedISRUBase"
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
                    string nodeConverterName = n.HasValue("ConverterName") ? n.GetValue("ConverterName") : "Converter";
                    if (nodeConverterName == ConverterName) { result = n; break; }
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
                    string nodeConverterName = n.HasValue("ConverterName") ? n.GetValue("ConverterName") : "Converter";
                    if (nodeConverterName == ConverterName) { result = n; break; }
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
        /// Finds the config node that corresponds to a specific PartModule occurrence. This is
        /// needed for modules without a unique ConverterName when a part contains more than one
        /// instance of the same module class.
        /// </summary>
        public static ConfigNode FindModuleConfigNode(PartModule targetModule, string moduleName)
        {
            Part part = targetModule?.part;
            if (part == null || string.IsNullOrEmpty(moduleName)) return null;

            int occurrence = 0;
            bool foundTarget = false;
            foreach (PartModule candidate in part.Modules)
            {
                if (candidate == null || candidate.moduleName != moduleName) continue;
                if (ReferenceEquals(candidate, targetModule))
                {
                    foundTarget = true;
                    break;
                }
                occurrence++;
            }
            if (!foundTarget)
            {
                KShared.LogError("Could not locate this " + moduleName
                    + " instance in part.Modules.", moduleName + "/FindModuleConfigNode");
                return null;
            }

            ConfigNode FindOccurrence(ConfigNode partNode)
            {
                int current = 0;
                foreach (ConfigNode moduleNode in partNode?.GetNodes("MODULE")
                         ?? new ConfigNode[0])
                {
                    if (moduleNode.GetValue("name") != moduleName) continue;
                    if (current++ == occurrence) return moduleNode;
                }
                return null;
            }

            ConfigNode result = FindOccurrence(part.partInfo?.partConfig);
            if (result != null) return result;

            string targetPartName = part.partInfo?.name ?? part.name;
            if (GameDatabase.Instance != null)
                foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
                {
                    string nodeName = partNode.GetValue("name") ?? "";
                    int slash = nodeName.LastIndexOf('/');
                    if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                    if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    result = FindOccurrence(partNode);
                    if (result != null) return result;
                }

            KShared.LogError("Could not find occurrence " + occurrence + " of MODULE "
                + moduleName + " in the part configuration.",
                moduleName + "/FindModuleConfigNode");
            return null;
        }
    }
}
