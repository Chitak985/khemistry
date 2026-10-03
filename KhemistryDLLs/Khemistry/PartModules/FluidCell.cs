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
        ////////// Main Config Fields //////////
        
        /// <summary>The maximum amount of resources the cell can hold.</summary>
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;
        /// <summary>The maximum resource transfer distance in meters.</summary>
        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        ////////// Transfer Rate Config Fields //////////
        
        /// <summary>Maximum resource input rate when transferring into the cell from a part.</summary>
        [KSPField] public double maxInputRate = -1;
        /// <summary>Maximum resource output rate when transferring from the cell into a part.</summary>
        [KSPField] public double maxOutputRate = -1;
        
        /// <summary>Maximum resource input rate when transferring into the cell inside the kerbal.</summary>
        [KSPField] public double maxInputRateInternal = -1;
        /// <summary>Maximum resource output rate when transferring out of the cell inside the kerbal.</summary>
        [KSPField] public double maxOutputRateInternal = -1;

        ////////// Runtime Variables //////////    
        
        private readonly FluidCellRateBudget rateBudget = new FluidCellRateBudget();
        
        /// <summary>Contents stored as "ResourceA:1.5|ResourceB:2".</summary>
        [KSPField(isPersistant = true)]
        public string StoredResourcesData = "";
        
        /// <summary>Union of every group, useful for knowing if a resource can be stored in this cell at all.</summary>
        public HashSet<string> SupportedResources = new HashSet<string>();

        ////////// Obsolete Config Fields //////////
        
        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        ////////// KSPEvents //////////
        
        [KSPEvent(guiActive = true, guiActiveEditor = false,
            guiName = "Cell Contents")]
        public void OpenCellContents()
        {
            KShared.Instance?.ShowResourceContents("Cell Contents",
                () => part == null ? null : GetStoredResources());
        }

        ////////// Transfer Rate Functions //////////
        
        /// <summary>
        /// Get the transfer rate to use.
        /// </summary>
        /// <param name="input">If the rate is for input, otherwise for output.</param>
        /// <param name="internalTransfer">If the rate is for internal transfer, otherwise for external.</param>
        /// <returns>The transfer rate to use in these conditions.</returns>
        internal double GetRate(bool input, bool internalTransfer)
            => internalTransfer ? (input ? maxInputRateInternal : maxOutputRateInternal)
                : (input ? maxInputRate : maxOutputRate);

        internal static int RateChannel(bool input, bool internalTransfer)
            => (internalTransfer ? 2 : 0) + (input ? 0 : 1);

        internal double LimitTransfer(FluidCellRateBudget budget, double amount, bool internalTransfer)
            => Math.Sign(amount) * Math.Min(Math.Abs(amount),
                budget.Available(RateChannel(amount < 0, internalTransfer),
                    GetRate(amount < 0, internalTransfer), UnityEngine.Time.fixedTime, TimeWarp.fixedDeltaTime));

        internal void RecordTransfer(FluidCellRateBudget budget, double amount, bool internalTransfer)
            => budget.Record(RateChannel(amount < 0, internalTransfer),
                Math.Abs(amount), UnityEngine.Time.fixedTime);

        ////////// Supported Resource Groups Functions //////////
        
        private readonly List<HashSet<string>> _supportedResourceGroups
            = new List<HashSet<string>>();

        /// <summary>
        /// Check if the fluid cell has any supported resource groups.
        /// </summary>
        public bool HasSupportedResourceGroups => _supportedResourceGroups.Count > 0;

        ////////// Contents Serialization Functions //////////
        
        /// <summary>
        /// Turn a serialized contents string into a dictionary.
        /// </summary>
        /// <param name="data">The serialized contents string.</param>
        /// <returns>The contents as a dictionary.</returns>
        internal static Dictionary<string, double> DeserializeResources(string data)
        {
            Dictionary<string, double> result =
                new Dictionary<string, double>(StringComparer.Ordinal);
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

        /// <summary>
        /// Turn a dictionary into a serialized contents string.
        /// </summary>
        /// <param name="resources">The contents as a dictionary.</param>
        /// <returns>The serialized contents string.</returns>
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

        ////////// Stroed Resources Functions //////////
        
        /// <summary>
        /// Get the total amount of resources stored inside the provided dictionary contents.
        /// </summary>
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

        /// <summary>
        /// Get the stored resources in the fluid cell's serialized contents.
        /// </summary>
        public Dictionary<string, double> GetStoredResources()
            => DeserializeResources(StoredResourcesData);

        /// <summary>
        /// Get the stored amount of a resource inside the fluid cell's serialized contents.
        /// </summary>
        public double GetStoredAmount(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return 0.0;
            GetStoredResources().TryGetValue(resourceName.Trim(), out double amount);
            return amount;
        }

        /// <summary>
        /// Get the total amount of resources stored inside the fluid cell's serialized contents.
        /// </summary>
        public double GetStoredTotal()
            => GetResourceTotal(GetStoredResources());

        ////////// Check Addable Resources Functions //////////
        
        /// <summary>
        /// Get a string <see cref="HashSet"/> of resources that can be added to the fluid cell.
        /// </summary>
        public HashSet<string> GetAddableResources(IEnumerable<string> storedResources)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (!HasSupportedResourceGroups()) return result;

            HashSet<string> stored = new HashSet<string>(StringComparer.Ordinal);
            if (storedResources != null)
                foreach (string name in storedResources)
                    if (!string.IsNullOrWhiteSpace(name))
                        stored.Add(name.Trim());

            foreach (HashSet<string> group in _supportedResourceGroups)
                if (group.IsSupersetOf(stored))
                    result.UnionWith(group);
            return result;
        }

        /// <summary>
        /// Check if a resource can be added to the fluid cell.
        /// </summary>
        public bool CanAddResource(string resourceName,
            IEnumerable<string> storedResources)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return false;
            return !HasSupportedResourceGroups()
                || GetAddableResources(storedResources).Contains(resourceName.Trim());
        }

        ////////// Stored Resource Requesting Functions //////////
        
        /// <summary>
        /// Uses the Part.RequestResource return contract against the private dictionary:
        /// positive amounts consume and negative amounts produce.
        /// </summary>
        public double RequestStoredResource(string resourceName, double amount)
            => RequestStoredResourceCore(resourceName, amount, false);

        /// <summary>
        /// Requests a stored resource from the fluid cell.
        /// This accepts negative values, so this function is able to
        /// insert or remove a resource from the cell.
        /// </summary>
        /// <param name="resourceName">Name of the resource to remove/add.</param>
        /// <param name="amount">Amount of the resource to remove (positive) or add (negative).</param>
        /// <param name="migrating">Disables transfer rate limits, used for migrated fluid cells.</param>
        /// <returns>Amount of the stored resource that was removed (positive) or added (negative).</returns>
        private double RequestStoredResourceCore(string resourceName, double amount, bool migrating)
        {
            // Verify parameters
            if (string.IsNullOrWhiteSpace(resourceName)
                || !KShared.IsFinite(amount)
                || amount == 0.0)
                return 0.0;

            // Apply transfer limits unless disabled
            if (!migrating) amount = LimitTransfer(rateBudget, amount, false);

            // Handle transfer limits completely stopping request
            if (amount == 0) return 0.0;

            // Get the resource to operate on
            resourceName = resourceName.Trim();
            Dictionary<string, double> resources = GetStoredResources();
            resources.TryGetValue(resourceName, out double current);

            // Set up retrn value
            double returnTmp = 0.0;

            // Remove the resource from the fluid cell
            if (amount > 0.0)
            {
                // Get amount being removed, limiting it at how much is currently stored
                double removed = Math.Min(amount, current);
                if (removed <= 0.0) return 0.0;

                // Get remaining resource amount
                double remaining = current - removed;

                // Remove the resource from the cell
                if (remaining <= 1e-9) resources.Remove(resourceName);
                else resources[resourceName] = remaining;
                returnTmp = removed;
            }
            else
            {
                // Ensure resource can be added to the cell
                if (!CanAddResource(resourceName, resources.Keys)) return 0.0;
    
                // Get the total amount of resources for capacity restriction
                double total = GetResourceTotal(resources);
                if (!KShared.IsFinite(total) || !KShared.IsFinite(ResourceMaxAmount)
                    || ResourceMaxAmount <= 0f)
                    return 0.0;
    
                // Get the amount being added with capacity restriction
                double added = Math.Min(-amount,
                    Math.Max(0.0, ResourceMaxAmount - total)
                );
    
                // Stop if cell is full or new amount would no longer be finite
                if (added <= 0.0 || !KShared.IsFinite(current + added)) return 0.0;
    
                // Add the resource
                resources[resourceName] = current + added;
                returnTmp = -added;
            }
            
            // Update serialized contents
            StoredResourcesData = SerializeResources(resources);

            // Record transfer for rate limiter unless disabled
            if (!migrating) RecordTransfer(rateBudget, returnTmp, false);

            // Return the resource amount removed from or added to the cell
            return returnTmp;
        }

        ////////// Small Helper Functions //////////
        
        /// <summary>
        /// Validate and return a rate value, disabling it if it is invalid.
        /// </summary>
        private static double ValidateRate(double rate, string rateName)
        {
            if (KShared.IsFinite(rate)) return rate;
            KShared.LogError($"Invalid fluid cell {rateName}; using -1 (rate is disabled).", "KhemistryFluidCell/OnLoad");
            return -1;
        }

        /// <summary>
        /// Add a supported resource group to the fluid cell.
        /// </summary>
        private static void AddSupportedResourceGroup(HashSet<string> group)
        {
            if (group.Count == 0)
                KShared.LogError("The provided supported resources group is empty, skipping.",
                    "KhemistryFluidCell/AddSupportedResourceGroup");
            else
                _supportedResourceGroups.Add(group);
                SupportedResources.UnionWith(group);
        }
        
        ////////// Overriden PartModule Functions //////////

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // Load transfer rates
            maxInputRate = ValidateRate(maxInputRate, "maxInputRate");
            maxOutputRate = ValidateRate(maxOutputRate, "maxOutputRate");
            maxInputRateInternal = ValidateRate(maxInputRateInternal, "maxInputRateInternal");
            maxOutputRateInternal = ValidateRate(maxOutputRateInternal, "maxOutputRateInternal");

            // Prepare supported resource groups
            _supportedResourceGroups.Clear();
            SupportedResources.Clear();

            // Load normal supported resource groups
            foreach (ConfigNode supportedNode in node.GetNodes("SUPPORTED_RESOURCES"))
            {
                // Set up the supported resources group
                HashSet<string> group = new HashSet<string>(StringComparer.Ordinal);

                // Load all supported resources from the node
                foreach (string name in supportedNode.GetValues("name"))
                {
                    // Clean up string
                    string trimmed = name?.Trim();

                    // Verify string and add supported resource to group
                    if (!string.IsNullOrEmpty(trimmed))
                        group.Add(trimmed);
                    else
                        KShared.LogError($"Part \"{part.name}\" "
                            + "has an empty SUPPORTED_RESOURCES resource, skipping.",
                            "KhemistryFluidCell/OnLoad");
                }

                // Add the supported resources group
                AddSupportedResourceGroup(group);
            }
            
            // Load SUPPORTED_RESOURCES_SINGULAR and make each of its entries a separate group
            foreach (ConfigNode supportedSNode in node.GetNodes("SUPPORTED_RESOURCES_SINGULAR"))
            {
                // Get all the values
                foreach (string name in supportedSNode.GetValues("name"))
                {
                    // Clean up string
                    string trimmed = name?.Trim();

                    // Verify string and add supported resource group
                    if (!string.IsNullOrEmpty(trimmed))
                        AddSupportedResourceGroup(
                            new HashSet<string>(StringComparer.Ordinal) { trimmed }
                        );
                    else
                        KShared.LogError($"Part \"{part.name}\" "
                            + "has an empty SUPPORTED_RESOURCES_SINGULAR resource, skipping.",
                            "KhemistryFluidCell/OnLoad");
                }
            }

            // No clue what this accomplishes
            StoredResourcesData = SerializeResources(
                DeserializeResources(StoredResourcesData));

            // Print how many supported resources were loaded (or not)
            if (HasSupportedResourceGroups())
                KShared.Log(
                    $"Loaded {SupportedResources.Count} resources in "
                    $"{_supportedResourceGroups.Count} supported resource groups.",
                    "KhemistryFluidCell/OnLoad");
            else
                KShared.Log(
                    "Loaded no supported resource groups whatsoever, "
                    + "fluid cell can now store anything!",
                    "KhemistryFluidCell/OnLoad");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // Get supported resource groups from prefab if there aren't any
            if (!HasSupportedResourceGroups())
            {
                KhemistryFluidCell prefab = part.partInfo?.partPrefab
                    ?.FindModuleImplementing<KhemistryFluidCell>();
                if (prefab != null && prefab != this)
                    foreach (HashSet<string> prefabGroup in prefab._supportedResourceGroups)
                        AddSupportedResourceGroup(
                            new HashSet<string>(prefabGroup, StringComparer.Ordinal)
                        );
            }

            // Verify ResourceMaxAmount
            if (!KShared.IsFinitePositive(ResourceMaxAmount))
            {
                KShared.LogError($"Part \"{part.name}\" "
                    + "has an invalid KhemistryFluidCell ResourceMaxAmount; using 100.",
                    "KhemistryFluidCell/OnStart");
                ResourceMaxAmount = 100f;
            }
            
            // Verify TransferDistance
            if (!KShared.IsFiniteNonNegative(TransferDistance))
            {
                KShared.LogError($"Part \"{part.name}\" has an invalid KhemistryFluidCell TransferDistance; using 10.",
                    "KhemistryFluidCell/OnStart");
                TransferDistance = 10f;
            }

            // Migrate obsolete fields
            ResourceName = ResourceName?.Trim() ?? "";
            if (!string.IsNullOrEmpty(ResourceName)
                && !KShared.IsFinitePositive(ResourceAmount))
            {
                KShared.LogWarning("Obsolete fields detected, they will be migrated but "
                    + "please consider updating to the new format to remove this warning.",
                    "KhemistryFluidCell/OnStart");
                double added = -RequestStoredResourceCore(ResourceName, -ResourceAmount, true);
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

    }
}
