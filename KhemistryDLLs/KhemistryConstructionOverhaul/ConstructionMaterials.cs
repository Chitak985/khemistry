using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khemistry;
using UnityEngine;

namespace KhemistryConstructionOverhaul
{
    /// <summary>
    /// Operations for the KSC's unbounded material ledger. These deliberately use only
    /// KhemistryMaterialInstance.Merge; materials with different parameters remain separate and
    /// are never contamination-merged.
    /// </summary>
    internal static class KhemistryConstructionMaterials
    {
        internal static KhemistryISRURecipe.ResourceInputMaterial CopyRequirement(
            KhemistryISRURecipe.ResourceInputMaterial source)
        {
            return new KhemistryISRURecipe.ResourceInputMaterial
            {
                id = null,
                name = source.name,
                shape = source.shape,
                size = source.size,
                usesParams = source.usesParams,
                parameters = source.parameters == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(source.parameters),
                amount = source.amount
            };
        }

        internal static bool TryParseCost(ConfigNode node, string partName,
            out KhemistryISRURecipe.ResourceInputMaterial requirement)
        {
            requirement = default;
            string context = "KhemistryPart/OnLoad";
            if (node == null)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a null MATERIAL_COST node; construction will be blocked.", context);
                return false;
            }

            if (node.HasValue("id"))
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a MATERIAL_COST with an id. MATERIAL_COST does not support ids; "
                    + "construction will be blocked.", context);
                return false;
            }

            string name = node.GetValue("name")?.Trim();
            string shape = node.GetValue("shape")?.Trim();
            string size = node.GetValue("size")?.Trim();
            int amount = 1;
            bool validAmount = !node.HasValue("amount")
                || int.TryParse(node.GetValue("amount"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out amount);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(shape)
                || string.IsNullOrEmpty(size) || !validAmount || amount <= 0)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a MATERIAL_COST that requires non-empty name/shape/size and "
                    + "an integer amount greater than zero; construction will be blocked.", context);
                return false;
            }

            bool hasParameterRequirements = node.HasNode("PARAM_REQUIREMENTS");
            bool hasParamsAlias = node.HasNode("PARAMS");
            if (hasParameterRequirements && hasParamsAlias)
            {
                KShared.LogError("Part \"" + partName + "\": MATERIAL_COST \"" + name
                    + "\" cannot contain both PARAM_REQUIREMENTS and PARAMS; construction "
                    + "will be blocked.", context);
                return false;
            }

            ConfigNode parameterNode = hasParameterRequirements
                ? node.GetNode("PARAM_REQUIREMENTS") : node.GetNode("PARAMS");
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            if (parameterNode != null)
                foreach (string key in parameterNode.values.DistinctNames())
                    parameters.Add(key, parameterNode.GetValue(key));

            requirement = new KhemistryISRURecipe.ResourceInputMaterial
            {
                id = null,
                name = name,
                shape = shape,
                size = size,
                usesParams = parameterNode != null,
                parameters = parameters,
                amount = amount
            };
            return true;
        }

        internal static bool TryValidateRequirement(
            KhemistryISRURecipe.ResourceInputMaterial requirement, out string error)
        {
            if (string.IsNullOrWhiteSpace(requirement.name)
                || string.IsNullOrWhiteSpace(requirement.shape)
                || string.IsNullOrWhiteSpace(requirement.size) || requirement.amount <= 0)
            {
                error = "A part has an invalid Khemistry material construction cost.";
                return false;
            }

            KhemistryMaterial definition = KShared.Instance?.materialList?.FirstOrDefault(
                material => material != null && material.name == requirement.name);
            if (definition == null)
            {
                error = "A part references the unknown construction material "
                    + requirement.name + ".";
                return false;
            }
            if (!definition.shapes.Contains(requirement.shape))
            {
                error = "A part uses the unsupported shape " + requirement.shape
                    + " for construction material " + requirement.name + ".";
                return false;
            }
            foreach (string parameterName in
                (requirement.parameters ?? new Dictionary<string, string>()).Keys)
                if (!definition.parameters.ContainsKey(parameterName))
                {
                    error = "A part references the unknown parameter " + parameterName
                        + " on construction material " + requirement.name + ".";
                    return false;
                }

            error = string.Empty;
            return true;
        }

        internal static bool IsValidInstance(KhemistryMaterialInstance material)
        {
            return material?.material != null && !string.IsNullOrWhiteSpace(material.material.name)
                && !string.IsNullOrWhiteSpace(material.shape)
                && material.material.shapes.Contains(material.shape)
                && !string.IsNullOrWhiteSpace(material.size) && material.amount > 0
                && !float.IsNaN(material.volume) && !float.IsInfinity(material.volume)
                && material.volume > 0f;
        }

        internal static bool AddNormal(List<KhemistryMaterialInstance> contents,
            KhemistryMaterialInstance material)
        {
            if (contents == null || !IsValidInstance(material)) return false;
            material.UpdateParams("KhemistryConstructionMaterials/AddNormal");
            foreach (KhemistryMaterialInstance existing in contents)
                if (existing != null && existing.Merge(material))
                    return true;
            contents.Add(material);
            return true;
        }

        internal static List<KhemistryMaterialInstance> CloneContents(
            IEnumerable<KhemistryMaterialInstance> contents)
        {
            List<KhemistryMaterialInstance> result = new List<KhemistryMaterialInstance>();
            if (contents == null) return result;
            foreach (KhemistryMaterialInstance material in contents)
                if (IsValidInstance(material))
                    AddNormal(result, new KhemistryMaterialInstance(material));
            return result;
        }

        internal static bool TryConsumeRequirements(
            IEnumerable<KhemistryMaterialInstance> source,
            IEnumerable<KhemistryISRURecipe.ResourceInputMaterial> requirements,
            out List<KhemistryMaterialInstance> remaining, out string error)
        {
            remaining = CloneContents(source);
            foreach (KhemistryISRURecipe.ResourceInputMaterial requirement in
                requirements ?? Enumerable.Empty<KhemistryISRURecipe.ResourceInputMaterial>())
            {
                if (!TryValidateRequirement(requirement, out error)) return false;

                int available = GetMatchingAmount(remaining, requirement);
                if (available < requirement.amount)
                {
                    int missing = requirement.amount - available;
                    error = "Missing " + missing.ToString(CultureInfo.InvariantCulture)
                        + " of " + requirement.name + " with shape " + requirement.shape + "!";
                    return false;
                }

                RemoveMatching(remaining, requirement, requirement.amount);
            }

            error = string.Empty;
            return true;
        }

        private static int GetMatchingAmount(List<KhemistryMaterialInstance> contents,
            KhemistryISRURecipe.ResourceInputMaterial requirement)
        {
            long total = 0;
            foreach (KhemistryMaterialInstance material in contents)
                if (KhemistryMaterialStorage.MatchesMaterial(material, requirement.name,
                        requirement.shape, requirement.size, requirement.parameters))
                {
                    total += material.amount;
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            return (int)total;
        }

        private static void RemoveMatching(List<KhemistryMaterialInstance> contents,
            KhemistryISRURecipe.ResourceInputMaterial requirement, int amount)
        {
            int remaining = amount;
            for (int index = contents.Count - 1; index >= 0 && remaining > 0; index--)
            {
                KhemistryMaterialInstance stored = contents[index];
                if (!KhemistryMaterialStorage.MatchesMaterial(stored, requirement.name,
                        requirement.shape, requirement.size, requirement.parameters))
                    continue;

                int take = Math.Min(remaining, stored.amount);
                if (take == stored.amount)
                    contents.RemoveAt(index);
                else
                {
                    stored.amount -= take;
                    stored.UpdateParams("KhemistryConstructionMaterials/RemoveMatching");
                }
                remaining -= take;
            }
        }

        internal static void ReplaceContents(List<KhemistryMaterialInstance> destination,
            IEnumerable<KhemistryMaterialInstance> replacement)
        {
            destination.Clear();
            foreach (KhemistryMaterialInstance material in replacement)
                AddNormal(destination, material);
        }
    }

    /// <summary>
    /// Material counterpart to KhemistryGeneratorPart. It sends whole material units from
    /// KhemistryMaterialStorage modules on the active vessel to the unbounded KSC ledger.
    /// </summary>
    public class KhemistryMaterialGeneratorPart : PartModule
    {
        [KSPField(isPersistant = false)]
        public float MaterialMaxVolume = 1.0f;

        private sealed class MaterialSource
        {
            internal KhemistryMaterialStorage storage;
            internal KhemistryMaterialInstance material;
        }

        private sealed class MaterialSelection
        {
            internal KhemistryMaterialInstance representative;
            internal readonly List<MaterialSource> sources = new List<MaterialSource>();
            internal long totalAmount;
        }

        private sealed class RemovedMaterial
        {
            internal KhemistryMaterialStorage storage;
            internal KhemistryMaterialInstance material;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false,
            guiName = "Send stored materials to the KSC", active = true,
            groupName = "materialsending", groupDisplayName = "Material Sender",
            groupStartCollapsed = false)]
        public void SendMaterials()
        {
            if (!TryGetEligibleVessel(out Vessel vessel)) return;
            if (float.IsNaN(MaterialMaxVolume) || float.IsInfinity(MaterialMaxVolume)
                || MaterialMaxVolume <= 0f)
            {
                KShared.LogError("MaterialMaxVolume must be a finite positive value.",
                    "KhemistryMaterialGeneratorPart/SendMaterials");
                return;
            }

            List<MaterialSelection> selections = BuildSelections(vessel);
            if (selections.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No materials are available to send.", 5f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            Dictionary<string, MaterialSelection> options =
                new Dictionary<string, MaterialSelection>();
            foreach (MaterialSelection selection in selections)
            {
                KhemistryMaterialInstance material = selection.representative;
                string baseLabel = material.material.name + " | " + material.shape + " | "
                    + material.size + " | "
                    + selection.totalAmount.ToString(CultureInfo.InvariantCulture)
                    + " available | "
                    + ((double)material.volume * selection.totalAmount).ToString("G6",
                        CultureInfo.InvariantCulture) + " m³";
                string label = baseLabel;
                int suffix = 2;
                while (options.ContainsKey(label))
                    label = baseLabel + " (variant " + suffix++ + ")";
                options.Add(label, selection);
            }

            KShared.Instance.ShowSelector("Send materials to the KSC", options.Keys.ToList(),
                selectedLabel =>
                {
                    if (options.TryGetValue(selectedLabel, out MaterialSelection selection))
                        TransferSelection(selection);
                });
        }

        private bool TryGetEligibleVessel(out Vessel vessel)
        {
            vessel = FlightGlobals.ActiveVessel;
            if (KShared.Instance?.KSCMaterialContents == null)
            {
                KShared.LogError("The shared KSC material ledger is unavailable.",
                    "KhemistryMaterialGeneratorPart/TryGetEligibleVessel");
                return false;
            }
            if (!HighLogic.LoadedSceneIsFlight)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "This does not work right now.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return false;
            }
            if (vessel?.mainBody == null || part?.vessel != vessel)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "This material sender must be on the active vessel.", 5f,
                    ScreenMessageStyle.UPPER_CENTER));
                return false;
            }
            if (vessel.mainBody.name != FlightGlobals.GetHomeBodyName()
                || !vessel.LandedOrSplashed)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "You can only send materials to the KSC while landed on the body it is on.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return false;
            }
            return true;
        }

        private static List<MaterialSelection> BuildSelections(Vessel vessel)
        {
            List<MaterialSelection> selections = new List<MaterialSelection>();
            foreach (Part vesselPart in vessel.parts ?? new List<Part>())
            {
                List<KhemistryMaterialStorage> storages =
                    vesselPart?.FindModulesImplementing<KhemistryMaterialStorage>();
                if (storages == null) continue;
                foreach (KhemistryMaterialStorage storage in storages)
                    foreach (KhemistryMaterialInstance material in
                        storage?.contents ?? new List<KhemistryMaterialInstance>())
                    {
                        if (!KhemistryConstructionMaterials.IsValidInstance(material)) continue;
                        MaterialSelection selection = selections.FirstOrDefault(existing =>
                            SameNormalMaterial(existing.representative, material));
                        if (selection == null)
                        {
                            selection = new MaterialSelection
                            {
                                representative = new KhemistryMaterialInstance(material),
                                totalAmount = 0
                            };
                            selections.Add(selection);
                        }
                        selection.sources.Add(new MaterialSource
                        {
                            storage = storage,
                            material = material
                        });
                        selection.totalAmount = Math.Min(int.MaxValue,
                            selection.totalAmount + material.amount);
                    }
            }
            return selections;
        }

        private static bool SameNormalMaterial(KhemistryMaterialInstance left,
            KhemistryMaterialInstance right)
        {
            if (!KhemistryConstructionMaterials.IsValidInstance(left)
                || !KhemistryConstructionMaterials.IsValidInstance(right)) return false;
            KhemistryMaterialInstance leftUnit = new KhemistryMaterialInstance(left) { amount = 1 };
            KhemistryMaterialInstance rightUnit = new KhemistryMaterialInstance(right) { amount = 1 };
            leftUnit.UpdateParams("KhemistryMaterialGeneratorPart/SameNormalMaterial");
            rightUnit.UpdateParams("KhemistryMaterialGeneratorPart/SameNormalMaterial");
            return leftUnit.CanMerge(rightUnit);
        }

        private void TransferSelection(MaterialSelection selection)
        {
            if (!TryGetEligibleVessel(out Vessel vessel) || selection?.representative == null)
                return;

            double perUnitVolume = selection.representative.volume;
            if (double.IsNaN(perUnitVolume) || double.IsInfinity(perUnitVolume)
                || perUnitVolume <= 0d) return;
            double volumeLimitedRaw = Math.Floor(MaterialMaxVolume / perUnitVolume + 1e-9);
            int volumeLimitedAmount = volumeLimitedRaw >= int.MaxValue
                ? int.MaxValue : (volumeLimitedRaw > 0d ? (int)volumeLimitedRaw : 0);
            if (volumeLimitedAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "One unit of this material is larger than the sender's volume limit.", 5f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            List<MaterialSource> validSources = selection.sources.Where(source =>
                source?.storage?.part?.vessel == vessel
                && source.storage.contents.Contains(source.material)
                && SameNormalMaterial(selection.representative, source.material)).ToList();
            long available = validSources.Sum(source => (long)source.material.amount);
            int transferAmount = (int)Math.Min(available, volumeLimitedAmount);
            if (transferAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That material is no longer available.", 5f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KhemistryMaterialInstance transferred =
                new KhemistryMaterialInstance(selection.representative)
                {
                    amount = transferAmount
                };
            transferred.UpdateParams("KhemistryMaterialGeneratorPart/TransferSelection");
            if (!KhemistryConstructionMaterials.IsValidInstance(transferred)) return;

            int remaining = transferAmount;
            List<RemovedMaterial> removed = new List<RemovedMaterial>();
            foreach (MaterialSource source in validSources)
            {
                int take = Math.Min(remaining, source.material.amount);
                KhemistryMaterialInstance piece = new KhemistryMaterialInstance(source.material)
                {
                    amount = take
                };
                piece.UpdateParams("KhemistryMaterialGeneratorPart/TransferSelection");
                removed.Add(new RemovedMaterial { storage = source.storage, material = piece });
                if (take == source.material.amount)
                    source.storage.contents.Remove(source.material);
                else
                {
                    source.material.amount -= take;
                    source.material.UpdateParams(
                        "KhemistryMaterialGeneratorPart/TransferSelection");
                }
                remaining -= take;
                if (remaining == 0) break;
            }

            if (remaining != 0 || !KhemistryConstructionMaterials.AddNormal(
                    KShared.Instance.KSCMaterialContents, transferred))
            {
                // Restore each exact piece to its source without invoking a storage restriction
                // or contaminated merge.
                foreach (RemovedMaterial item in removed)
                    if (item?.storage != null)
                        KhemistryConstructionMaterials.AddNormal(item.storage.contents,
                            item.material);
                KShared.LogError("Material transfer failed and was restored to the vessel.",
                    "KhemistryMaterialGeneratorPart/TransferSelection");
                return;
            }

            double sentVolume = transferred.TotalVolume;
            KShared.Log("Transferred " + transferAmount.ToString(CultureInfo.InvariantCulture)
                + " of " + transferred.material.name + " ("
                + sentVolume.ToString("G6", CultureInfo.InvariantCulture)
                + " m³) to the KSC.", "KhemistryMaterialGeneratorPart/TransferSelection");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Transferred " + transferAmount.ToString(CultureInfo.InvariantCulture)
                + " of " + transferred.material.name + " ("
                + sentVolume.ToString("G6", CultureInfo.InvariantCulture) + " m³) to the KSC.",
                5f, ScreenMessageStyle.UPPER_CENTER));
        }
    }
}
