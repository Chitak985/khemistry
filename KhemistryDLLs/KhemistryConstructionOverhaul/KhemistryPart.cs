using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khemistry;

namespace KhemistryConstructionOverhaul
{
    public class KhemistryPart : PartModule
    {
        // Resource costs for this part, populated from the part config
        public Dictionary<string, double> ResourceDict = new Dictionary<string, double>();
        public List<KhemistryISRURecipe.ResourceInputMaterial> MaterialCosts =
            new List<KhemistryISRURecipe.ResourceInputMaterial>();
        public bool CostConfigurationValid { get; private set; }

        internal bool EnsureCostConfiguration(out string error)
        {
            error = null;
            if (CostConfigurationValid && ResourceDict != null && MaterialCosts != null) return true;
            ConfigNode config = KShared.FindModuleConfigNode(this, "KhemistryPart");
            if (config != null)
            {
                // Editor instances need not receive OnLoad. Rehydrate from the authoritative
                // matching MODULE, not from empty/default fields on the cloned component.
                OnLoad(config);
                if (CostConfigurationValid && ResourceDict != null && MaterialCosts != null) return true;
            }
            error = "Part \"" + (part?.partInfo?.title ?? part?.name ?? "<unknown>")
                + "\" (" + (part?.partInfo?.name ?? part?.name ?? "<unknown>")
                + ") has an invalid Khemistry construction-cost configuration: "
                + (config == null ? "the matching KhemistryPart MODULE could not be found."
                    : "its construction costs could not be loaded; see KhemistryPart/OnLoad errors.");
            KShared.LogError(error, "KhemistryPart/EnsureCostConfiguration");
            return false;
        }

        private KhemistryPart FindCorrespondingPrefabModule()
        {
            Part prefab = part?.partInfo?.partPrefab;
            if (prefab == null || prefab == part) return null;

            List<KhemistryPart> liveModules = part.Modules.OfType<KhemistryPart>().ToList();
            int moduleIndex = liveModules.IndexOf(this);
            List<KhemistryPart> prefabModules = prefab.Modules.OfType<KhemistryPart>().ToList();
            if (moduleIndex < 0 && prefabModules.Count == 1) return prefabModules[0];
            return moduleIndex >= 0 && moduleIndex < prefabModules.Count
                ? prefabModules[moduleIndex] : null;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            string partName = part?.name ?? "<unknown>";

            KShared.Log("OnLoad triggered", "KhemistryPart/OnLoad");

            bool hasResourceNames = node?.HasNode("RESOURCE_COST_NAMES") == true;
            bool hasResourceAmounts = node?.HasNode("RESOURCE_COST_AMOUNTS") == true;
            bool hasMaterialCosts = node?.GetNodes("MATERIAL_COST").Length > 0;
            if (node == null || (!hasResourceNames && !hasResourceAmounts && !hasMaterialCosts))
            {
                // Craft/vessel persistence nodes contain only KSPField values. Preserve the
                // matching prefab module's cost table rather than mistaking persistence data
                // for a malformed part config. The index matters if a part has several modules.
                if (CostConfigurationValid) return;
                KhemistryPart prefabModule = FindCorrespondingPrefabModule();
                if (prefabModule != null && prefabModule.ResourceDict != null
                    && prefabModule.MaterialCosts != null)
                {
                    ResourceDict = new Dictionary<string, double>(prefabModule.ResourceDict);
                    MaterialCosts = prefabModule.MaterialCosts
                        .Select(KhemistryConstructionMaterials.CopyRequirement).ToList();
                    CostConfigurationValid = prefabModule.CostConfigurationValid;
                    if (CostConfigurationValid) return;
                }
                KShared.LogError(
                    "Part \"" + partName + "\" has a KhemistryPart module but defines no " +
                    "resource or material construction costs in its config. " +
                    "Construction of this part will be blocked.",
                    "KhemistryPart/OnLoad");
                return;
            }

            // Cloned components may still share prefab collections. Never clear those.
            ResourceDict = new Dictionary<string, double>();
            MaterialCosts = new List<KhemistryISRURecipe.ResourceInputMaterial>();
            CostConfigurationValid = false;
            if (hasResourceNames != hasResourceAmounts)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has only one of RESOURCE_COST_NAMES/RESOURCE_COST_AMOUNTS; construction will be blocked.",
                    "KhemistryPart/OnLoad");
                return;
            }

            Dictionary<string, double> parsedCosts = new Dictionary<string, double>();
            if (hasResourceNames)
            {
                string[] names = node.GetNode("RESOURCE_COST_NAMES").GetValues("name");
                string[] amountsStr = node.GetNode("RESOURCE_COST_AMOUNTS").GetValues("amount");
                if (names.Length == 0 || names.Length != amountsStr.Length)
                {
                    KShared.LogError("Part \"" + partName
                        + "\" has empty or mismatched RESOURCE_COST_NAMES/RESOURCE_COST_AMOUNTS; construction will be blocked.",
                        "KhemistryPart/OnLoad");
                    return;
                }

                for (int i = 0; i < names.Length; i++)
                {
                    string resourceName = names[i]?.Trim();
                    if (string.IsNullOrEmpty(resourceName)
                        || !double.TryParse(amountsStr[i], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double amount)
                        || double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
                    {
                        KShared.LogError("Part \"" + partName
                            + "\" has invalid construction cost at index " + i
                            + "; construction will be blocked.", "KhemistryPart/OnLoad");
                        return;
                    }

                    parsedCosts.TryGetValue(resourceName, out double existing);
                    double total = existing + amount;
                    if (double.IsNaN(total) || double.IsInfinity(total))
                    {
                        KShared.LogError("Part \"" + partName
                            + "\" has construction costs that overflow for resource "
                            + resourceName + "; construction will be blocked.",
                            "KhemistryPart/OnLoad");
                        return;
                    }
                    parsedCosts[resourceName] = total;
                }
            }

            List<KhemistryISRURecipe.ResourceInputMaterial> parsedMaterialCosts =
                new List<KhemistryISRURecipe.ResourceInputMaterial>();
            foreach (ConfigNode materialNode in node.GetNodes("MATERIAL_COST"))
            {
                if (!KhemistryConstructionMaterials.TryParseCost(materialNode, partName,
                        out KhemistryISRURecipe.ResourceInputMaterial materialCost))
                    return;
                parsedMaterialCosts.Add(materialCost);
            }

            ResourceDict = parsedCosts;
            MaterialCosts = parsedMaterialCosts;
            CostConfigurationValid = true;
        }

        // Returns ("", "1") on success, or (errorMessage, "0") on failure.
        public List<string> BuyCheck()
        {
            var tmp = new List<string>();

            if (!EnsureCostConfiguration(out string configurationError))
            {
                tmp.Add(configurationError);
                tmp.Add("0");
                KShared.LogError("Part construction-cost configuration is invalid!",
                    "KhemistryPart/BuyCheck");
                return tmp;
            }

            if (!KhemistryResourceCheckManager.TryCheckCosts(ResourceDict, MaterialCosts,
                    out string error))
            {
                tmp.Add(error);
                tmp.Add("0");
                return tmp;
            }

            tmp.Add("");
            tmp.Add("1");
            KShared.Log("BuyCheck passed for part.", "KhemistryPart/BuyCheck");
            return tmp;
        }

        // Deducts resources after a successful BuyCheck.
        public void Buy()
        {
            if (!EnsureCostConfiguration(out _)) return;
            if (!KhemistryResourceCheckManager.TryCommitCosts(ResourceDict, MaterialCosts,
                    out string error))
                KShared.LogError("Construction balances changed before Buy; nothing was "
                    + "deducted. " + error, "KhemistryPart/Buy");
        }
    }

}

