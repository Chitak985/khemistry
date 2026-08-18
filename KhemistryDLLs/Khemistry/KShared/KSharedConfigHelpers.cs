using System;

namespace Khemistry
{
    public partial class KShared
    {
        // Help get a value safely
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
        public static string GetStrValueFromCFG(ConfigNode node, string value, string defaultValue)
            => (node != null && node.HasValue(value)) ? node.GetValue(value) : defaultValue;

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
    }
}
