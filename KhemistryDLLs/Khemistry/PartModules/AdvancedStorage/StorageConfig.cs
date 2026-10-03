using System;
using System.Collections.Generic;
using System.Globalization;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage
    {
        private readonly Dictionary<string, double> _resourceCapacities =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _resourceVolumeMultipliers =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private double ResourceVolumeMultiplier(string name) => storageType == "multiShared"
            && _resourceVolumeMultipliers.TryGetValue(name, out double multiplier) ? multiplier : 1.0;

        private double SharedCapacity => KShared.Multiply(maximumResources, _environment.Multiplier("volumeMul"));

        private double UsedCapacity
        {
            get
            {
                double total = 0;
                foreach (var pair in _resources) total += pair.Value * ResourceVolumeMultiplier(pair.Key);
                return total;
            }
        }

        private void LoadResourceVolumeMultipliers(ConfigNode module, ConfigNode resources)
        {
            var nodes = module.GetNodes("SUPPORTED_RESOURCE_VOL_MUL");
            if (nodes.Length == 0 || storageType != "multiShared") return;
            string[] names = resources?.GetValues("name") ?? new string[0];
            string[] amounts = nodes[0].GetValues("amount");
            bool valid = nodes.Length == 1 && names.Length > 0 && names.Length == amounts.Length;
            var parsed = new Dictionary<string, double>(StringComparer.Ordinal);
            if (valid)
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i]?.Trim();
                    if (string.IsNullOrEmpty(name) || !_supportedResources.Contains(name)
                        || parsed.ContainsKey(name)
                        || !double.TryParse(amounts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                        || !KShared.IsFinite(value) || value <= 0)
                    { valid = false; break; }
                    parsed.Add(name, value);
                }
            if (!valid)
            {
                KShared.LogWarning("Invalid SUPPORTED_RESOURCE_VOL_MUL: use one node with a finite positive amount "
                    + "for each unique SUPPORTED_RESOURCES name, in the same order. Using normal shared capacity.",
                    "KhemistryAdvancedStorage/LoadSupportedResources");
                return;
            }
            foreach (var pair in parsed) _resourceVolumeMultipliers.Add(pair.Key, pair.Value);
        }

        /// <summary>Capacity for this name with the active biome multiplier, independent of contents and rates.</summary>
        public double GetResourceCapacity(string name)
        {
            if (name == null || !_supportedResources.Contains(name)) return 0;
            double capacity = storageType == "multi" && _resourceCapacities.TryGetValue(name, out double specific)
                ? specific : (storageType == "single" ? DegradationCapacity : maximumResources);
            return KShared.Multiply(capacity, _environment.Multiplier("volumeMul")) / ResourceVolumeMultiplier(name);
        }

        private bool LoadSupportedResources(ConfigNode module)
        {
            const string context = "KhemistryAdvancedStorage/LoadSupportedResources";
            _supportedResources.Clear();
            _resourceCapacities.Clear();
            _resourceMultipliers.Clear();
            _resourceVolumeMultipliers.Clear();
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
            LoadResourceVolumeMultipliers(module, group);
            if (_supportedResources.Count > 0) return true;
            KShared.LogError("No valid supported resources were configured; storage disabled.", context);
            return false;
        }
    }
}
