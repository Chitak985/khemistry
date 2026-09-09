using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    /// <summary>
    /// A dictionary-backed resource cell that can be carried by a kerbal. Its contents are
    /// deliberately not PartResources; real KSP resources exist only at transfer endpoints.
    /// </summary>
    public class KhemistryFluidCell : PartModule
    {
        /// <summary>The maximum amount of resources the cell can hold.</summary>
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        /// <summary>The maximum resource transfer distance in meters.</summary>
        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        /// <summary>Canonical contents, serialized as "ResourceA:1.5|ResourceB:2".</summary>
        [KSPField(isPersistant = true)]
        public string StoredResourcesData = "";

        // Obsolete pre-dictionary fields. Keep loading them so existing saves can migrate
        // without losing contents, but never use them as active storage.
        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        private readonly List<HashSet<string>> _supportedResourceGroups
            = new List<HashSet<string>>();

        /// <summary>
        /// Union of every group, for callers that only need to know whether a resource
        /// belongs to this cell at all.
        /// </summary>
        public HashSet<string> SupportedResources = new HashSet<string>();

        public bool HasSupportedResourceGroups => _supportedResourceGroups.Count > 0;

        internal static Dictionary<string, double> DeserializeResources(string data)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(data)) return result;

            foreach (string entry in data.Split('|'))
            {
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || separator >= entry.Length - 1) continue;
                string name = entry.Substring(0, separator).Trim();
                if (string.IsNullOrEmpty(name)
                    || !double.TryParse(entry.Substring(separator + 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double amount)
                    || !KShared.IsFinite(amount) || amount <= 0.0)
                    continue;

                result.TryGetValue(name, out double current);
                double combined = current + amount;
                if (KShared.IsFinite(combined)) result[name] = combined;
            }
            return result;
        }

        internal static string SerializeResources(
            IDictionary<string, double> resources)
        {
            if (resources == null) return "";
            return string.Join("|", resources
                .Where(value => !string.IsNullOrWhiteSpace(value.Key)
                    && KShared.IsFinite(value.Value) && value.Value > 0.0)
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => value.Key.Trim() + ":"
                    + value.Value.ToString("R", CultureInfo.InvariantCulture))
                .ToArray());
        }

        internal static double GetResourceTotal(
            IDictionary<string, double> resources)
        {
            if (resources == null) return 0.0;
            double total = 0.0;
            foreach (KeyValuePair<string, double> value in resources)
            {
                if (!KShared.IsFinite(value.Value) || value.Value <= 0.0) continue;
                total += value.Value;
                if (!KShared.IsFinite(total)) return double.PositiveInfinity;
            }
            return total;
        }

        public Dictionary<string, double> GetStoredResources()
            => DeserializeResources(StoredResourcesData);

        public double GetStoredAmount(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return 0.0;
            GetStoredResources().TryGetValue(resourceName.Trim(), out double amount);
            return amount;
        }

        public double GetStoredTotal()
            => GetResourceTotal(GetStoredResources());

        public HashSet<string> GetAddableResources(IEnumerable<string> storedResources)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (_supportedResourceGroups.Count == 0) return result;

            var stored = new HashSet<string>(StringComparer.Ordinal);
            if (storedResources != null)
                foreach (string name in storedResources)
                    if (!string.IsNullOrWhiteSpace(name)) stored.Add(name.Trim());

            foreach (HashSet<string> group in _supportedResourceGroups)
                if (group.IsSupersetOf(stored)) result.UnionWith(group);
            return result;
        }

        public bool CanAddResource(string resourceName,
            IEnumerable<string> storedResources)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return false;
            return !HasSupportedResourceGroups
                || GetAddableResources(storedResources).Contains(resourceName.Trim());
        }

        /// <summary>
        /// Uses the Part.RequestResource return contract against the private dictionary:
        /// positive amounts consume and negative amounts produce.
        /// </summary>
        public double RequestStoredResource(string resourceName, double amount)
        {
            if (string.IsNullOrWhiteSpace(resourceName) || !KShared.IsFinite(amount)
                || amount == 0.0)
                return 0.0;

            resourceName = resourceName.Trim();
            Dictionary<string, double> resources = GetStoredResources();
            resources.TryGetValue(resourceName, out double current);

            if (amount > 0.0)
            {
                double removed = Math.Min(amount, current);
                if (removed <= 0.0) return 0.0;
                double remaining = current - removed;
                if (remaining <= 1e-9) resources.Remove(resourceName);
                else resources[resourceName] = remaining;
                StoredResourcesData = SerializeResources(resources);
                return removed;
            }

            if (!CanAddResource(resourceName, resources.Keys)) return 0.0;
            double total = GetResourceTotal(resources);
            if (!KShared.IsFinite(total) || !KShared.IsFinite(ResourceMaxAmount)
                || ResourceMaxAmount <= 0f)
                return 0.0;
            double added = Math.Min(-amount,
                Math.Max(0.0, ResourceMaxAmount - total));
            if (added <= 0.0 || !KShared.IsFinite(current + added)) return 0.0;
            resources[resourceName] = current + added;
            StoredResourcesData = SerializeResources(resources);
            return -added;
        }

        [KSPField(isPersistant = false, guiActive = true,
            guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _supportedResourceGroups.Clear();
            SupportedResources.Clear();

            foreach (ConfigNode supportedNode in node.GetNodes("SUPPORTED_RESOURCES"))
            {
                var group = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in supportedNode.GetValues("name"))
                {
                    string trimmed = name?.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) group.Add(trimmed);
                }
                if (group.Count == 0) continue;
                _supportedResourceGroups.Add(group);
                SupportedResources.UnionWith(group);
            }
            
            foreach (ConfigNode supportedSNode in node.GetNodes("SUPPORTED_RESOURCES_SINGULAR"))
            {
                var tmp = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in supportedSNode.GetValues("name"))
                {
                    string trimmed = name?.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        tmp.Clear();
                        tmp.Add(trimmed);
                        _supportedResourceGroups.Add(tmp);
                        SupportedResources.UnionWith(tmp);
                    }
                }
            }

            StoredResourcesData = SerializeResources(
                DeserializeResources(StoredResourcesData));
            if (_supportedResourceGroups.Count > 0)
                KShared.Log(
                    "Loaded " + SupportedResources.Count + " resources in "
                    + _supportedResourceGroups.Count + " supported resource groups.",
                    "KhemistryFluidCell/OnLoad");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (_supportedResourceGroups.Count == 0)
            {
                KhemistryFluidCell prefab = part.partInfo?.partPrefab
                    ?.FindModuleImplementing<KhemistryFluidCell>();
                if (prefab != null && prefab != this)
                {
                    foreach (HashSet<string> prefabGroup in prefab._supportedResourceGroups)
                    {
                        var group = new HashSet<string>(prefabGroup,
                            StringComparer.Ordinal);
                        _supportedResourceGroups.Add(group);
                        SupportedResources.UnionWith(group);
                    }
                }
            }

            if (float.IsNaN(ResourceMaxAmount) || float.IsInfinity(ResourceMaxAmount)
                || ResourceMaxAmount <= 0f)
            {
                KShared.LogError("Part \"" + part.name
                    + "\" has an invalid KhemistryFluidCell ResourceMaxAmount; using 100.",
                    "KhemistryFluidCell/OnStart");
                ResourceMaxAmount = 100f;
            }
            if (float.IsNaN(TransferDistance) || float.IsInfinity(TransferDistance)
                || TransferDistance < 0f)
            {
                KShared.LogError("Part \"" + part.name
                    + "\" has an invalid KhemistryFluidCell TransferDistance; using 10.",
                    "KhemistryFluidCell/OnStart");
                TransferDistance = 10f;
            }

            // Migrate only the obsolete module fields. PartResources are intentionally not
            // consulted: a tank on the same part is separate from this cell's contents.
            ResourceName = ResourceName?.Trim() ?? "";
            if (!string.IsNullOrEmpty(ResourceName) && ResourceAmount > 0f
                && !float.IsNaN(ResourceAmount) && !float.IsInfinity(ResourceAmount))
            {
                double added = -RequestStoredResource(ResourceName, -ResourceAmount);
                double remainder = Math.Max(0.0, ResourceAmount - added);
                ResourceAmount = remainder >= float.MaxValue
                    ? float.MaxValue : (float)remainder;
                if (ResourceAmount <= 1e-6f)
                {
                    ResourceAmount = 0f;
                    ResourceName = "";
                }
                else
                    KShared.LogWarning("Only part of legacy fluid-cell resource \""
                        + ResourceName + "\" fit in the dictionary; preserving the remainder.",
                        "KhemistryFluidCell/OnStart");
            }
        }

        public override void OnUpdate()
        {
            Dictionary<string, double> resources = GetStoredResources();
            var displayed = resources
                .Select(value => string.Format("{0}: {1:F2}", value.Key, value.Value))
                .ToList();
            string contents = displayed.Count == 0
                ? "Empty" : string.Join(", ", displayed.ToArray());
            ContentsDisplay = string.Format("{0} ({1:F2} / {2:F2})", contents,
                GetResourceTotal(resources), ResourceMaxAmount);
        }
    }
}
