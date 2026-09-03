using System.Collections.Generic;

using System;

namespace Khemistry
{
    /// <summary>
    /// A part that can hold some resources and be carried by a kerbal to transfer resources between vessels.
    /// </summary>
    public class KhemistryFluidCell : PartModule
    {
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        public HashSet<string> AllowedResources = new HashSet<string>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            AllowedResources.Clear();

            if (node.HasNode("ALLOWED_RESOURCES"))
            {
                foreach (string name in node.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                {
                    string trimmed = name?.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) AllowedResources.Add(trimmed);
                }
                KShared.Log(
                    "Loaded " + AllowedResources.Count + " allowed resources.",
                    "KhemistryFluidCell/OnLoad");
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (AllowedResources.Count == 0)
            {
                KhemistryFluidCell prefab = part.partInfo?.partPrefab
                    ?.FindModuleImplementing<KhemistryFluidCell>();
                if (prefab != null && prefab != this)
                    foreach (string resourceName in prefab.AllowedResources)
                        AllowedResources.Add(resourceName);
            }
            ResourceName = ResourceName?.Trim() ?? "";

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

            // Older versions stored contents in these module fields rather than the real
            // PartResource tank. Move everything that fits into the canonical tank and keep
            // any remainder in the legacy fields so no saved resource is silently discarded.
            if (!string.IsNullOrWhiteSpace(ResourceName) && ResourceAmount > 0f
                && !float.IsNaN(ResourceAmount) && !float.IsInfinity(ResourceAmount)
                && (AllowedResources.Count == 0 || AllowedResources.Contains(ResourceName)))
            {
                PartResourceDefinition definition = PartResourceLibrary.Instance
                    ?.GetDefinition(ResourceName);
                PartResource tank = definition == null ? null : part.Resources.Get(definition.id);
                bool validTank = tank != null && !double.IsNaN(tank.amount)
                    && !double.IsInfinity(tank.amount) && tank.amount >= 0.0
                    && !double.IsNaN(tank.maxAmount) && !double.IsInfinity(tank.maxAmount)
                    && tank.maxAmount >= 0.0;
                if (validTank)
                {
                    double freeSpace = Math.Max(0.0, tank.maxAmount - tank.amount);
                    double moved = Math.Min(ResourceAmount, freeSpace);
                    tank.amount += moved;
                    double remainder = ResourceAmount - moved;
                    if (remainder <= 0.0)
                    {
                        ResourceAmount = 0f;
                        ResourceName = "";
                    }
                    else
                    {
                        ResourceAmount = remainder >= float.MaxValue
                            ? float.MaxValue : (float)remainder;
                        KShared.LogWarning("Only part of legacy fluid-cell resource \""
                            + ResourceName + "\" fit in the canonical tank; preserving the remainder.",
                            "KhemistryFluidCell/OnStart");
                    }
                }
                else if (tank == null)
                {
                    KShared.LogError("Could not migrate legacy fluid-cell resource \"" + ResourceName
                        + "\" because this part has no matching PartResource tank; preserving the legacy value.",
                        "KhemistryFluidCell/OnStart");
                }
                else if (!validTank)
                {
                    KShared.LogError("Could not migrate legacy fluid-cell resource \"" + ResourceName
                        + "\" because the canonical tank has invalid amount or capacity values; preserving the legacy value.",
                        "KhemistryFluidCell/OnStart");
                }
            }
        }

        public override void OnUpdate()
        {
            // The real PartResource tanks are canonical. Keeping a second amount in this
            // module caused deployed and inventoried copies of a cell to disagree and could
            // duplicate resources when the part changed state.
            var displayed = new List<string>();
            foreach (PartResource resource in part.Resources)
            {
                if (AllowedResources.Count > 0 && !AllowedResources.Contains(resource.resourceName))
                    continue;
                if (double.IsNaN(resource.amount) || double.IsInfinity(resource.amount)
                    || double.IsNaN(resource.maxAmount) || double.IsInfinity(resource.maxAmount)
                    || resource.amount <= 1e-9 || resource.maxAmount < 0.0) continue;
                displayed.Add(string.Format("{0}: {1:F2} / {2:F2}", resource.resourceName,
                    resource.amount, resource.maxAmount));
            }
            if (!string.IsNullOrWhiteSpace(ResourceName) && ResourceAmount > 1e-6f
                && !float.IsNaN(ResourceAmount) && !float.IsInfinity(ResourceAmount))
                displayed.Add(string.Format("{0}: {1:F2} (preserved legacy remainder)",
                    ResourceName.Trim(), ResourceAmount));
            ContentsDisplay = displayed.Count == 0 ? "Empty" : string.Join(", ", displayed.ToArray());
        }
    }
}
