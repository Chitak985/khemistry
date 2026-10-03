using System;
using System.Collections.Generic;
using System.Globalization;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage
    {
        private readonly Dictionary<string, double> _resourceCapacities =
            new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>Capacity for this name with the active biome multiplier, independent of contents and rates.</summary>
        public double GetResourceCapacity(string name)
        {
            if (name == null || !_supportedResources.Contains(name)) return 0;
            double capacity = storageType == "multi" && _resourceCapacities.TryGetValue(name, out double specific)
                ? specific : (storageType == "single" ? DegradationCapacity : maximumResources);
            return KShared.Multiply(capacity, _environment.Multiplier("volumeMul"));
        }

        private bool LoadSupportedResources(ConfigNode module)
        {
            const string context = "KhemistryAdvancedStorage/LoadSupportedResources";
            _supportedResources.Clear();
            _resourceCapacities.Clear();
            _resourceMultipliers.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ConfigNode group = module.GetNode("SUPPORTED_RESOURCES");
            if (group != null)
                foreach (string raw in group.GetValues("name"))
                {
                    string name = raw?.Trim();
                    if (string.IsNullOrEmpty(name) || PartResourceLibrary.Instance?.GetDefinition(name) == null)
                    {
                        KShared.LogError("Empty or unknown resource in SUPPORTED_RESOURCES: " + raw, context);
                        continue;
                    }
                    if (seen.Add(name)) _supportedResources.Add(name);
                    else KShared.LogWarning("Ignoring duplicate supported resource \"" + name + "\".", context);
                }

            var individual = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode entry in module.GetNodes("SUPPORTED_RESOURCE"))
            {
                if (storageType != "multi")
                {
                    KShared.LogError("SUPPORTED_RESOURCE is only accepted with storageType=multi; node ignored. "
                        + "Use SUPPORTED_RESOURCES for single or multiShared storage.", context);
                    continue;
                }
                string name = entry.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(name) || PartResourceLibrary.Instance?.GetDefinition(name) == null)
                {
                    KShared.LogError("SUPPORTED_RESOURCE requires a known resource name.", context);
                    return false;
                }
                if (!individual.Add(name))
                {
                    KShared.LogError("Duplicate SUPPORTED_RESOURCE for \"" + name + "\"; storage disabled.", context);
                    return false;
                }
                var multipliers = new StorageMultipliers();
                if (!multipliers.Load(entry, true, false, context)) return false;
                _resourceMultipliers.Add(name, multipliers);
                if (entry.HasValue("amount"))
                {
                    if (!double.TryParse(entry.GetValue("amount"), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double capacity) || !KShared.IsFinite(capacity) || capacity <= 0)
                    {
                        KShared.LogError("SUPPORTED_RESOURCE \"" + name
                            + "\" amount must be finite and positive; storage disabled.", context);
                        return false;
                    }
                    _resourceCapacities.Add(name, capacity);
                }
                else KShared.LogWarning("SUPPORTED_RESOURCE \"" + name
                    + "\" has no amount; using maximumResources. Prefer SUPPORTED_RESOURCES for default capacities.",
                    context);
                // An individual entry overrides the capacity of a name also present in the plural list.
                if (seen.Add(name)) _supportedResources.Add(name);
            }
            if (_supportedResources.Count > 0) return true;
            KShared.LogError("No valid supported resources were configured; storage disabled.", context);
            return false;
        }
    }
}
