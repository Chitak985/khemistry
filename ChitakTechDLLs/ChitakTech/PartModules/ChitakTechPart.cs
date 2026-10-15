using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ChitakTech
{
    public class ChitakTechPart : PartModule
    {
        // Resource costs for this part, populated from the part config
        public Dictionary<string, double> ResourceDict = new Dictionary<string, double>();
        public List<ChitakTechISRURecipe.ResourceInputMaterial> MaterialCosts =
            new List<ChitakTechISRURecipe.ResourceInputMaterial>();
        public bool CostConfigurationValid { get; private set; }

        internal bool EnsureCostConfiguration(out string error)
        {
            error = null;
            if (CostConfigurationValid && ResourceDict != null && MaterialCosts != null) return true;
            ConfigNode config = CTShared.FindModuleConfigNode(this, "ChitakTechPart");
            if (config != null)
            {
                // Editor instances need not receive OnLoad. Rehydrate from the authoritative
                // matching MODULE, not from empty/default fields on the cloned component.
                OnLoad(config);
                if (CostConfigurationValid && ResourceDict != null && MaterialCosts != null) return true;
            }
            error = "Part \"" + (part?.partInfo?.title ?? part?.name ?? "<unknown>")
                + "\" (" + (part?.partInfo?.name ?? part?.name ?? "<unknown>")
                + ") has an invalid ChitakTech construction-cost configuration: "
                + (config == null ? "the matching ChitakTechPart MODULE could not be found."
                    : "its construction costs could not be loaded; see ChitakTechPart/OnLoad errors.");
            CTShared.LogError(error, "ChitakTechPart/EnsureCostConfiguration");
            return false;
        }

        private ChitakTechPart FindCorrespondingPrefabModule()
        {
            Part prefab = part?.partInfo?.partPrefab;
            if (prefab == null || prefab == part) return null;

            List<ChitakTechPart> liveModules = part.Modules.OfType<ChitakTechPart>().ToList();
            int moduleIndex = liveModules.IndexOf(this);
            List<ChitakTechPart> prefabModules = prefab.Modules.OfType<ChitakTechPart>().ToList();
            if (moduleIndex < 0 && prefabModules.Count == 1) return prefabModules[0];
            return moduleIndex >= 0 && moduleIndex < prefabModules.Count
                ? prefabModules[moduleIndex] : null;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            string partName = part?.name ?? "<unknown>";

            CTShared.Log("OnLoad triggered", "ChitakTechPart/OnLoad");

            bool hasResourceNames = node?.HasNode("RESOURCE_COST_NAMES") == true;
            bool hasResourceAmounts = node?.HasNode("RESOURCE_COST_AMOUNTS") == true;
            bool hasMaterialCosts = node?.GetNodes("MATERIAL_COST").Length > 0;
            if (node == null || (!hasResourceNames && !hasResourceAmounts && !hasMaterialCosts))
            {
                // Craft/vessel persistence nodes contain only KSPField values. Preserve the
                // matching prefab module's cost table rather than mistaking persistence data
                // for a malformed part config. The index matters if a part has several modules.
                if (CostConfigurationValid) return;
                ChitakTechPart prefabModule = FindCorrespondingPrefabModule();
                if (prefabModule != null && prefabModule.ResourceDict != null
                    && prefabModule.MaterialCosts != null)
                {
                    ResourceDict = new Dictionary<string, double>(prefabModule.ResourceDict);
                    MaterialCosts = prefabModule.MaterialCosts
                        .Select(ChitakTechConstructionMaterials.CopyRequirement).ToList();
                    CostConfigurationValid = prefabModule.CostConfigurationValid;
                    if (CostConfigurationValid) return;
                }
                CTShared.LogError(
                    "Part \"" + partName + "\" has a ChitakTechPart module but defines no " +
                    "resource or material construction costs in its config. " +
                    "Construction of this part will be blocked.",
                    "ChitakTechPart/OnLoad");
                return;
            }

            // Cloned components may still share prefab collections. Never clear those.
            ResourceDict = new Dictionary<string, double>();
            MaterialCosts = new List<ChitakTechISRURecipe.ResourceInputMaterial>();
            CostConfigurationValid = false;
            if (hasResourceNames != hasResourceAmounts)
            {
                CTShared.LogError("Part \"" + partName
                    + "\" has only one of RESOURCE_COST_NAMES/RESOURCE_COST_AMOUNTS; construction will be blocked.",
                    "ChitakTechPart/OnLoad");
                return;
            }

            Dictionary<string, double> parsedCosts = new Dictionary<string, double>();
            if (hasResourceNames)
            {
                string[] names = node.GetNode("RESOURCE_COST_NAMES").GetValues("name");
                string[] amountsStr = node.GetNode("RESOURCE_COST_AMOUNTS").GetValues("amount");
                if (names.Length == 0 || names.Length != amountsStr.Length)
                {
                    CTShared.LogError("Part \"" + partName
                        + "\" has empty or mismatched RESOURCE_COST_NAMES/RESOURCE_COST_AMOUNTS; construction will be blocked.",
                        "ChitakTechPart/OnLoad");
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
                        CTShared.LogError("Part \"" + partName
                            + "\" has invalid construction cost at index " + i
                            + "; construction will be blocked.", "ChitakTechPart/OnLoad");
                        return;
                    }

                    parsedCosts.TryGetValue(resourceName, out double existing);
                    double total = existing + amount;
                    if (double.IsNaN(total) || double.IsInfinity(total))
                    {
                        CTShared.LogError("Part \"" + partName
                            + "\" has construction costs that overflow for resource "
                            + resourceName + "; construction will be blocked.",
                            "ChitakTechPart/OnLoad");
                        return;
                    }
                    parsedCosts[resourceName] = total;
                }
            }

            List<ChitakTechISRURecipe.ResourceInputMaterial> parsedMaterialCosts =
                new List<ChitakTechISRURecipe.ResourceInputMaterial>();
            foreach (ConfigNode materialNode in node.GetNodes("MATERIAL_COST"))
            {
                if (!ChitakTechConstructionMaterials.TryParseCost(materialNode, partName,
                        out ChitakTechISRURecipe.ResourceInputMaterial materialCost))
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
                CTShared.LogError("Part construction-cost configuration is invalid!",
                    "ChitakTechPart/BuyCheck");
                return tmp;
            }

            if (!ChitakTechResourceCheckManager.TryCheckCosts(ResourceDict, MaterialCosts,
                    out string error))
            {
                tmp.Add(error);
                tmp.Add("0");
                return tmp;
            }

            tmp.Add("");
            tmp.Add("1");
            CTShared.Log("BuyCheck passed for part.", "ChitakTechPart/BuyCheck");
            return tmp;
        }

        // Deducts resources after a successful BuyCheck.
        public void Buy()
        {
            if (!EnsureCostConfiguration(out _)) return;
            if (!ChitakTechResourceCheckManager.TryCommitCosts(ResourceDict, MaterialCosts,
                    out string error))
                CTShared.LogError("Construction balances changed before Buy; nothing was "
                    + "deducted. " + error, "ChitakTechPart/Buy");
        }
    }

}

