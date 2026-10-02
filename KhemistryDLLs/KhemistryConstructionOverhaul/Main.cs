using CustomPreLaunchChecks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Khemistry;
using System;
using System.Globalization;

namespace KhemistryConstructionOverhaul
{
    public class KhemistryGeneratorPart : PartModule
    {
        // Maximum amount of a resource sendable per activation
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Send stored resources to the KSC", active = true,
         groupName = "resourcesending", groupDisplayName = "Resource Sender", groupStartCollapsed = false)]
        public void SendResources()
        {
            var shared = KShared.Instance;
            if (shared?.ResourceDict == null)
            {
                Debug.LogError("KhemistryConstructionOverhaul: The shared construction-resource ledger is unavailable in SendResources!");
                return;
            }

            KShared.Log("SendResources triggered.", "KhemistryGeneratorPart/SendResources");

            if (!HighLogic.LoadedSceneIsFlight)
            {
                KShared.Log("Attempt to send resources while not in flight.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("This does not work right now.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel?.mainBody == null || part?.vessel != activeVessel)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "This resource sender must be on the active vessel.", 5.0f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (activeVessel.mainBody.name != FlightGlobals.GetHomeBodyName()
                || !activeVessel.LandedOrSplashed)
            {
                KShared.Log("Must send resources while on the home world.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("You can only send resources to the KSC while landed on the body it is on.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            // Gather all resources present on the vessel with amount > 0, deduplicated by name
            if (float.IsNaN(ResourceMaxAmount) || float.IsInfinity(ResourceMaxAmount)
                || ResourceMaxAmount <= 0f)
            {
                KShared.LogError("ResourceMaxAmount must be a finite positive value.",
                    "KhemistryGeneratorPart/SendResources");
                return;
            }

            var availableResources = activeVessel.parts
                .SelectMany(p => p.Resources.Cast<PartResource>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.resourceName)
                    && !double.IsNaN(r.amount) && !double.IsInfinity(r.amount)
                    && r.amount > 0)
                .GroupBy(r => r.resourceName)
                .Select(g => g.Key)
                .ToList();

            if (availableResources.Count == 0)
            {
                KShared.Log("No resources available on vessel.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("No resources available to send.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowResourceSelector(availableResources, TransferResource);
        }

        // Called when the player picks a resource from the selector window
        private void TransferResource(string resourceName)
        {
            var shared = KShared.Instance;
            if (shared?.ResourceDict == null)
            {
                Debug.LogError("KhemistryConstructionOverhaul: The shared construction-resource ledger is unavailable in TransferResource!");
                return;
            }
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (!HighLogic.LoadedSceneIsFlight || activeVessel?.mainBody == null
                || activeVessel.mainBody.name != FlightGlobals.GetHomeBodyName()
                || !activeVessel.LandedOrSplashed || part?.vessel != activeVessel)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "The vessel must remain landed on the home world.", 5.0f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            resourceName = resourceName?.Trim();
            PartResourceLibrary resourceLibrary = PartResourceLibrary.Instance;
            var def = string.IsNullOrEmpty(resourceName) || resourceLibrary == null
                ? null
                : resourceLibrary.GetDefinition(resourceName);
            if (def == null)
            {
                KShared.LogError("Could not find resource definition for: " + resourceName, "KhemistryGeneratorPart/TransferResource");
                ScreenMessages.PostScreenMessage(new ScreenMessage("Unknown resource: " + resourceName, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ResourceDict.TryGetValue(resourceName, out double currentAmount);
            if (double.IsNaN(currentAmount) || double.IsInfinity(currentAmount) || currentAmount < 0d)
            {
                KShared.LogError("The stored KSC balance for " + resourceName
                    + " is invalid; refusing the transfer.", "KhemistryGeneratorPart/TransferResource");
                return;
            }

            // Avoid draining a vessel resource if the KSC ledger cannot represent
            // the resulting balance.
            double remainingLedgerCapacity = double.MaxValue - currentAmount;
            double requestAmount = Math.Min(ResourceMaxAmount, remainingLedgerCapacity);
            if (requestAmount <= 0d)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "The KSC storage balance for " + resourceName + " is full.", 5.0f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            // Drain up to ResourceMaxAmount from the whole vessel.
            double taken = part.RequestResource(def.id, requestAmount, ResourceFlowMode.ALL_VESSEL);

            if (double.IsNaN(taken) || double.IsInfinity(taken) || taken <= 0d)
            {
                KShared.Log("No " + resourceName + " could be drained from the vessel.", "KhemistryGeneratorPart/TransferResource");
                ScreenMessages.PostScreenMessage(new ScreenMessage("No " + resourceName + " could be transferred.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            double newAmount = currentAmount + taken;
            if (double.IsNaN(newAmount) || double.IsInfinity(newAmount)
                || newAmount <= currentAmount)
            {
                // Return the resource rather than losing it if this balance is too large for
                // even double precision to represent the increment.
                part.RequestResource(def.id, -taken, ResourceFlowMode.ALL_VESSEL);
                KShared.LogError("Transferred amount exceeded the KSC ledger capacity.",
                    "KhemistryGeneratorPart/TransferResource");
                return;
            }
            shared.ResourceDict[resourceName] = newAmount;

            KShared.Log(taken + " of " + resourceName + " transferred to the KSC.", "KhemistryGeneratorPart/TransferResource");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                string.Format("Transferred {0:F2} units of {1} to the KSC.", taken, resourceName),
                5.0f, ScreenMessageStyle.UPPER_CENTER));
        }
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KhemistryConstructionResourcesScenario : ScenarioModule
    {
        public static KhemistryConstructionResourcesScenario Instance { get; private set; }
        public bool LedgerReady => !_loadedResourcesPending && !_loadedMaterialsPending;
        public void OnDestroy() { if (Instance == this) Instance = null; }
        private readonly Dictionary<string, double> _loadedResources =
            new Dictionary<string, double>();
        private readonly List<ConfigNode> _loadedMaterialNodes = new List<ConfigNode>();
        private readonly List<ConfigNode> _opaqueMaterialNodes = new List<ConfigNode>();
        private bool _loadedResourcesPending;
        private bool _loadedMaterialsPending;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _loadedResources.Clear();
            Instance = this;
            _loadedMaterialNodes.Clear();
            _opaqueMaterialNodes.Clear();
            _loadedResourcesPending = false;
            _loadedMaterialsPending = false;

            bool initialized = false;
            if (node != null)
                bool.TryParse(node.GetValue("initialized"), out initialized);

            // KShared persists across saves, so an absent marker must explicitly replace any
            // previous save's ledger with the starter balances.
            if (!initialized)
            {
                foreach (KeyValuePair<string, double> resource in
                    KShared.CreateStartingConstructionResourceLedger())
                    _loadedResources[resource.Key] = resource.Value;
                _loadedResourcesPending = true;
                _loadedMaterialsPending = true;
                TryApplyLoadedLedger();
                return;
            }

            foreach (ConfigNode resourceNode in node.GetNodes("RESOURCE"))
            {
                string resourceName = resourceNode.GetValue("name")?.Trim();
                string amountText = resourceNode.GetValue("amount");
                if (string.IsNullOrEmpty(resourceName)
                    || !double.TryParse(amountText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)
                    || double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
                {
                    KShared.LogError("Ignored an invalid saved construction-resource balance.",
                        "KhemistryConstructionResourcesScenario/OnLoad");
                    continue;
                }

                _loadedResources.TryGetValue(resourceName, out double existing);
                double total = existing + amount;
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    KShared.LogError("Ignored an overflowing saved balance for " + resourceName + ".",
                        "KhemistryConstructionResourcesScenario/OnLoad");
                    continue;
                }
                _loadedResources[resourceName] = total;
            }

            foreach (ConfigNode materialNode in node.GetNodes("STORED_MATERIAL"))
            {
                ConfigNode copy = new ConfigNode("STORED_MATERIAL");
                materialNode.CopyTo(copy);
                _loadedMaterialNodes.Add(copy);
            }

            _loadedResourcesPending = true;
            _loadedMaterialsPending = true;
            TryApplyLoadedLedger();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            IDictionary<string, double> resources = _loadedResourcesPending
                ? _loadedResources : KShared.Instance?.ResourceDict;
            if (resources == null)
            {
                KShared.LogError("Could not save construction resources because KShared is unavailable.",
                    "KhemistryConstructionResourcesScenario/OnSave");
                return;
            }

            while (node.HasValue("initialized")) node.RemoveValue("initialized");
            while (node.HasNode("RESOURCE")) node.RemoveNode("RESOURCE");
            while (node.HasNode("STORED_MATERIAL")) node.RemoveNode("STORED_MATERIAL");
            node.AddValue("initialized", true);
            foreach (KeyValuePair<string, double> resource in resources.OrderBy(kvp => kvp.Key))
            {
                if (string.IsNullOrWhiteSpace(resource.Key) || double.IsNaN(resource.Value)
                    || double.IsInfinity(resource.Value) || resource.Value < 0d)
                {
                    KShared.LogError("Skipped an invalid construction-resource balance while saving.",
                        "KhemistryConstructionResourcesScenario/OnSave");
                    continue;
                }

                ConfigNode resourceNode = node.AddNode("RESOURCE");
                resourceNode.AddValue("name", resource.Key.Trim());
                resourceNode.AddValue("amount", resource.Value.ToString("R",
                    CultureInfo.InvariantCulture));
            }

            if (_loadedMaterialsPending)
            {
                foreach (ConfigNode materialNode in _loadedMaterialNodes)
                    AddMaterialNodeCopy(node, materialNode);
                return;
            }

            foreach (ConfigNode materialNode in _opaqueMaterialNodes)
                AddMaterialNodeCopy(node, materialNode);
            foreach (KhemistryMaterialInstance material in
                KShared.Instance?.KSCMaterialContents ?? new List<KhemistryMaterialInstance>())
            {
                if (!KhemistryConstructionMaterials.IsValidInstance(material))
                {
                    KShared.LogError("Skipped an invalid KSC material while saving.",
                        "KhemistryConstructionResourcesScenario/OnSave");
                    continue;
                }
                node.AddNode(material.ToConfigNode("STORED_MATERIAL"));
            }
        }

        public void Update()
        {
            if (_loadedResourcesPending || _loadedMaterialsPending)
                TryApplyLoadedLedger();
        }

        private void TryApplyLoadedLedger()
        {
            KShared shared = KShared.Instance;
            if (shared == null) return;

            if (_loadedResourcesPending && shared.ResourceDict != null)
            {
                shared.ResourceDict.Clear();
                foreach (KeyValuePair<string, double> resource in _loadedResources)
                    shared.ResourceDict[resource.Key] = resource.Value;
                _loadedResourcesPending = false;
            }

            if (!_loadedMaterialsPending) return;
            if (_loadedMaterialNodes.Count > 0
                && (shared.materialList == null || shared.materialList.Count == 0))
                return;

            shared.KSCMaterialContents.Clear();
            _opaqueMaterialNodes.Clear();
            foreach (ConfigNode savedNode in _loadedMaterialNodes)
            {
                if (KhemistryMaterialInstance.TryFromConfigNode(savedNode,
                        out KhemistryMaterialInstance material,
                        "KhemistryConstructionResourcesScenario/TryApplyLoadedLedger")
                    && KhemistryConstructionMaterials.AddNormal(
                        shared.KSCMaterialContents, material))
                    continue;

                ConfigNode copy = new ConfigNode("STORED_MATERIAL");
                savedNode.CopyTo(copy);
                _opaqueMaterialNodes.Add(copy);
            }
            _loadedMaterialNodes.Clear();
            _loadedMaterialsPending = false;
        }

        private static void AddMaterialNodeCopy(ConfigNode parent, ConfigNode materialNode)
        {
            ConfigNode copy = new ConfigNode("STORED_MATERIAL");
            materialNode.CopyTo(copy);
            parent.AddNode(copy);
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryCPLCChecksRegistrar : MonoBehaviour
    {
        private static KhemistryCPLCChecksRegistrar _instance;
        private static bool _registered;
        private bool _rolloutEventRegistered;

        public void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            KerbalKSCBridge.SendMaterial = AcceptKerbalMaterial;
            DontDestroyOnLoad(gameObject);

            if (!_registered)
            {
                KShared.Log("Registering pre-launch check.", "KhemistryCPLCChecksRegistrar/Awake");
                CPLC.RegisterCheck(KhemistryResourceCheckManager.GetKhemistryTest);
                _registered = true;
            }

            GameEvents.OnVesselRollout.Add(OnVesselRollout);
            _rolloutEventRegistered = true;
        }

        public void OnDestroy()
        {
            if (_rolloutEventRegistered)
            {
                GameEvents.OnVesselRollout.Remove(OnVesselRollout);
                _rolloutEventRegistered = false;
            }
            if (_instance == this)
            {
                KerbalKSCBridge.SendMaterial = null;
                _instance = null;
            }
        }

        private static bool AcceptKerbalMaterial(KhemistryMaterialInstance material)
        {
            var ledger = KShared.Instance?.KSCMaterialContents;
            if (ledger == null || !KhemistryConstructionMaterials.IsValidInstance(material)) return false;
            // Work on copies first: a rejected send must not mutate the live ledger.
            var next = ledger.Select(item => new KhemistryMaterialInstance(item)).ToList();
            if (!KhemistryConstructionMaterials.AddNormal(next, new KhemistryMaterialInstance(material))) return false;
            ledger.Clear();
            ledger.AddRange(next);
            return true;
        }

        private void OnVesselRollout(ShipConstruct ship)
        {
            if (KhemistryResourceCheckManager.TryCommitRolloutCost(ship, out string error))
                return;

            KShared.LogError("Could not deduct construction resources during rollout: " + error,
                "KhemistryCPLCChecksRegistrar/OnVesselRollout");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Khemistry construction resources were not deducted: " + error, 8f,
                ScreenMessageStyle.UPPER_CENTER));
        }
    }

    public class KhemistryResourceCheckManager : PreFlightTests.IPreFlightTest
    {
        public string errorMessage = "UNKNOWN ERROR";

        public bool Test()
        {
            KShared.Log("Test() fired!", "KhemistryResourceCheckManager/Test");

            ShipConstruct ship = EditorLogic.fetch?.ship;
            if (!TryGetShipCost(ship, out Dictionary<string, double> totalCost,
                    out List<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
                    out errorMessage))
            {
                KShared.LogError("Launch blocked: " + errorMessage,
                    "KhemistryResourceCheckManager/Test");
                return false;
            }

            if (!TryCheckCosts(totalCost, materialCosts, out errorMessage))
            {
                KShared.LogWarning("Launch blocked: " + errorMessage,
                    "KhemistryResourceCheckManager/Test");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        internal static bool TryCommitRolloutCost(ShipConstruct ship, out string error)
        {
            if (!TryGetShipCost(ship, out Dictionary<string, double> totalCost,
                    out List<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
                    out error))
                return false;
            return TryCommitCosts(totalCost, materialCosts, out error);
        }

        private static bool TryGetShipCost(ShipConstruct ship,
            out Dictionary<string, double> totalCost,
            out List<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
            out string error)
        {
            totalCost = new Dictionary<string, double>();
            materialCosts = new List<KhemistryISRURecipe.ResourceInputMaterial>();
            if (ship?.parts == null)
            {
                error = "No editor ship is available.";
                return false;
            }

            foreach (Part part in ship.parts)
            {
                List<KhemistryPart> modules = part?.FindModulesImplementing<KhemistryPart>();
                if (modules == null || modules.Count == 0)
                    modules = part?.partInfo?.partPrefab?.FindModulesImplementing<KhemistryPart>();
                if (modules == null) continue;

                foreach (KhemistryPart module in modules)
                {
                    if (module == null)
                    {
                        error = "Part \"" + (part?.partInfo?.title ?? part?.name ?? "<unknown>")
                            + "\" has a missing KhemistryPart module.";
                        return false;
                    }
                    if (!module.EnsureCostConfiguration(out error)) return false;

                    foreach (KeyValuePair<string, double> cost in module.ResourceDict)
                    {
                        string resourceName = cost.Key?.Trim();
                        if (string.IsNullOrEmpty(resourceName) || double.IsNaN(cost.Value)
                            || double.IsInfinity(cost.Value) || cost.Value < 0d)
                        {
                            error = "A part has an invalid Khemistry construction cost.";
                            return false;
                        }
                        if (PartResourceLibrary.Instance?.GetDefinition(resourceName) == null)
                        {
                            error = "A part references the unknown construction resource "
                                + resourceName + ".";
                            return false;
                        }
                        if (cost.Value == 0d) continue;

                        totalCost.TryGetValue(resourceName, out double existing);
                        double aggregate = existing + cost.Value;
                        if (double.IsNaN(aggregate) || double.IsInfinity(aggregate))
                        {
                            error = "The total construction cost for " + resourceName
                                + " is too large.";
                            return false;
                        }
                        totalCost[resourceName] = aggregate;
                    }

                    foreach (KhemistryISRURecipe.ResourceInputMaterial materialCost in
                        module.MaterialCosts)
                    {
                        if (!KhemistryConstructionMaterials.TryValidateRequirement(
                                materialCost, out error))
                            return false;
                        materialCosts.Add(
                            KhemistryConstructionMaterials.CopyRequirement(materialCost));
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        internal static bool TryCheckCosts(Dictionary<string, double> resourceCosts,
            IEnumerable<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
            out string error)
        {
            return TryPrepareCosts(resourceCosts, materialCosts, out _, out _, out _, out error);
        }

        internal static bool TryCommitCosts(Dictionary<string, double> resourceCosts,
            IEnumerable<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
            out string error)
        {
            if (!TryPrepareCosts(resourceCosts, materialCosts,
                    out Dictionary<string, double> updatedBalances,
                    out List<KhemistryMaterialInstance> remainingMaterials,
                    out bool commitMaterials, out error))
                return false;

            KShared shared = KShared.Instance;
            foreach (KeyValuePair<string, double> balance in updatedBalances)
            {
                shared.ResourceDict[balance.Key] = balance.Value;
                KShared.Log("Deducted construction cost for " + balance.Key + ".",
                    "KhemistryResourceCheckManager/TryCommitCosts");
            }
            if (commitMaterials)
            {
                KhemistryConstructionMaterials.ReplaceContents(
                    shared.KSCMaterialContents, remainingMaterials);
                KShared.Log("Deducted material construction costs.",
                    "KhemistryResourceCheckManager/TryCommitCosts");
            }

            error = string.Empty;
            return true;
        }

        private static bool TryPrepareCosts(Dictionary<string, double> resourceCosts,
            IEnumerable<KhemistryISRURecipe.ResourceInputMaterial> materialCosts,
            out Dictionary<string, double> updatedBalances,
            out List<KhemistryMaterialInstance> remainingMaterials,
            out bool commitMaterials, out string error)
        {
            updatedBalances = new Dictionary<string, double>();
            remainingMaterials = null;
            commitMaterials = false;
            KShared shared = KShared.Instance;
            Dictionary<string, double> balances = shared?.ResourceDict;
            if (balances == null || shared.KSCMaterialContents == null)
            {
                error = "Khemistry construction resources and materials are not available.";
                return false;
            }
            if (resourceCosts == null)
            {
                error = "A part has an invalid Khemistry construction-cost configuration.";
                return false;
            }

            foreach (KeyValuePair<string, double> cost in resourceCosts)
            {
                string resourceName = cost.Key?.Trim();
                if (string.IsNullOrEmpty(resourceName) || double.IsNaN(cost.Value)
                    || double.IsInfinity(cost.Value) || cost.Value < 0d)
                {
                    error = "A part has an invalid Khemistry construction cost.";
                    return false;
                }
                if (PartResourceLibrary.Instance?.GetDefinition(resourceName) == null)
                {
                    error = "A part references the unknown construction resource "
                        + resourceName + ".";
                    return false;
                }
                if (!balances.TryGetValue(cost.Key, out double available)
                    || double.IsNaN(available) || double.IsInfinity(available) || available < 0d)
                {
                    error = "No valid " + cost.Key + " balance is available.";
                    return false;
                }
                if (available < cost.Value)
                {
                    double shortfall = cost.Value - available;
                    error = "Not enough " + cost.Key + "! You need "
                        + shortfall.ToString("G6", CultureInfo.InvariantCulture) + " more.";
                    return false;
                }

                double updated = available - cost.Value;
                if (double.IsNaN(updated) || double.IsInfinity(updated) || updated < 0d)
                {
                    error = "The " + cost.Key + " balance changed before construction.";
                    return false;
                }
                updatedBalances[cost.Key] = updated;
            }

            List<KhemistryISRURecipe.ResourceInputMaterial> materialCostList =
                (materialCosts ?? Enumerable.Empty<KhemistryISRURecipe.ResourceInputMaterial>())
                .Select(KhemistryConstructionMaterials.CopyRequirement).ToList();
            commitMaterials = materialCostList.Count > 0;
            if (commitMaterials
                && !KhemistryConstructionMaterials.TryConsumeRequirements(
                    shared.KSCMaterialContents, materialCostList,
                    out remainingMaterials, out error))
                return false;
            if (!commitMaterials)
                remainingMaterials = new List<KhemistryMaterialInstance>();

            error = string.Empty;
            return true;
        }

        public string GetWarningTitle() => "Khemistry Resource Check";
        public string GetWarningDescription() => errorMessage;
        public string GetProceedOption() => null;
        public string GetAbortOption() => "Abort launch";

        public KhemistryResourceCheckManager(string launchSiteName)
        {
            KShared.Log(
                "Constructor fired for site: " + launchSiteName,
                "KhemistryResourceCheckManager/Constructor");
        }

        public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
        {
            return new KhemistryResourceCheckManager(launchSiteName);
        }
    }
}
