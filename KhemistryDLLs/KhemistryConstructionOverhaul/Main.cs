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

    public class KhemistryPart : PartModule
    {
        // Resource costs for this part, populated from the part config
        public Dictionary<string, double> ResourceDict = new Dictionary<string, double>();
        public bool CostConfigurationValid { get; private set; }

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

            if (node == null || !node.HasNode("RESOURCE_COST_NAMES")
                || !node.HasNode("RESOURCE_COST_AMOUNTS"))
            {
                // Craft/vessel persistence nodes contain only KSPField values. Preserve the
                // matching prefab module's cost table rather than mistaking persistence data
                // for a malformed part config. The index matters if a part has several modules.
                if (CostConfigurationValid) return;
                KhemistryPart prefabModule = FindCorrespondingPrefabModule();
                if (prefabModule != null && prefabModule.ResourceDict != null)
                {
                    ResourceDict = new Dictionary<string, double>(prefabModule.ResourceDict);
                    CostConfigurationValid = prefabModule.CostConfigurationValid;
                    if (CostConfigurationValid) return;
                }
                KShared.LogError(
                    "Part \"" + partName + "\" has a KhemistryPart module but is missing " +
                    "RESOURCE_COST_NAMES and/or RESOURCE_COST_AMOUNTS in its config. " +
                    "Construction of this part will be blocked.",
                    "KhemistryPart/OnLoad");
                return;
            }

            ResourceDict.Clear();
            CostConfigurationValid = false;
            string[] names = node.GetNode("RESOURCE_COST_NAMES").GetValues("name");
            string[] amountsStr = node.GetNode("RESOURCE_COST_AMOUNTS").GetValues("amount");
            if (names.Length == 0 || names.Length != amountsStr.Length)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has empty or mismatched RESOURCE_COST_NAMES/RESOURCE_COST_AMOUNTS; construction will be blocked.",
                    "KhemistryPart/OnLoad");
                return;
            }

            Dictionary<string, double> parsedCosts = new Dictionary<string, double>();
            for (int i = 0; i < names.Length; i++)
            {
                string resourceName = names[i]?.Trim();
                if (string.IsNullOrEmpty(resourceName)
                    || !double.TryParse(amountsStr[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double amount)
                    || double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
                {
                    KShared.LogError("Part \"" + partName + "\" has invalid construction cost at index "
                        + i + "; construction will be blocked.", "KhemistryPart/OnLoad");
                    ResourceDict.Clear();
                    return;
                }

                parsedCosts.TryGetValue(resourceName, out double existing);
                double total = existing + amount;
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    KShared.LogError("Part \"" + partName + "\" has construction costs that "
                        + "overflow for resource " + resourceName + "; construction will be blocked.",
                        "KhemistryPart/OnLoad");
                    ResourceDict.Clear();
                    return;
                }
                parsedCosts[resourceName] = total;
            }
            ResourceDict = parsedCosts;
            CostConfigurationValid = true;
        }

        // Returns ("", "1") on success, or (errorMessage, "0") on failure.
        public List<string> BuyCheck()
        {
            var shared = KShared.Instance;
            var tmp = new List<string>();

            if (shared == null)
            {
                tmp.Add("A null reference error occurred! Info: KShared instance is null.");
                tmp.Add("0");
                Debug.LogError("KhemistryConstructionOverhaul: KShared instance is null in BuyCheck!");
                return tmp;
            }
            if (shared.ResourceDict == null)
            {
                tmp.Add("A null reference error occurred! Info: shared.ResourceDict is null.");
                tmp.Add("0");
                KShared.LogError("shared.ResourceDict is null!", "KhemistryPart/BuyCheck");
                return tmp;
            }
            if (ResourceDict == null || !CostConfigurationValid)
            {
                tmp.Add("This part has an invalid Khemistry construction-cost configuration.");
                tmp.Add("0");
                KShared.LogError("Part construction-cost configuration is invalid!",
                    "KhemistryPart/BuyCheck");
                return tmp;
            }

            foreach (KeyValuePair<string, double> cost in ResourceDict)
            {
                string resourceName = cost.Key;
                if (string.IsNullOrWhiteSpace(resourceName) || double.IsNaN(cost.Value)
                    || double.IsInfinity(cost.Value) || cost.Value < 0d)
                {
                    tmp.Add("This part has an invalid Khemistry construction cost.");
                    tmp.Add("0");
                    return tmp;
                }
                if (!shared.ResourceDict.ContainsKey(resourceName))
                {
                    tmp.Add("You have never obtained " + resourceName + "!");
                    tmp.Add("0");
                    KShared.Log("Never obtained resource: " + resourceName, "KhemistryPart/BuyCheck");
                    return tmp;
                }
                double available = shared.ResourceDict[resourceName];
                if (double.IsNaN(available) || double.IsInfinity(available) || available < 0d)
                {
                    tmp.Add("The stored " + resourceName + " balance is invalid.");
                    tmp.Add("0");
                    return tmp;
                }
                if (available < cost.Value)
                {
                    double shortfall = cost.Value - available;
                    tmp.Add("Not enough " + resourceName + "! You need " + shortfall + " more.");
                    tmp.Add("0");
                    KShared.Log("Not enough of resource: " + resourceName, "KhemistryPart/BuyCheck");
                    return tmp;
                }
            }

            tmp.Add("");
            tmp.Add("1");
            KShared.Log("BuyCheck passed for part.", "KhemistryPart/BuyCheck");
            return tmp;
        }

        // Deducts resources after a successful BuyCheck.
        public void Buy()
        {
            var shared = KShared.Instance;
            if (shared?.ResourceDict == null || ResourceDict == null
                || !CostConfigurationValid) return;

            foreach (KeyValuePair<string, double> cost in ResourceDict)
                if (string.IsNullOrWhiteSpace(cost.Key) || double.IsNaN(cost.Value)
                    || double.IsInfinity(cost.Value) || cost.Value < 0d
                    || !shared.ResourceDict.TryGetValue(cost.Key, out double available)
                    || double.IsNaN(available) || double.IsInfinity(available)
                    || available < cost.Value)
                {
                    KShared.LogError("Construction-resource balance changed before Buy; no resources were deducted.",
                        "KhemistryPart/Buy");
                    return;
                }

            foreach (var kvp in ResourceDict)
            {
                shared.ResourceDict[kvp.Key] -= kvp.Value;
                KShared.Log("Deducted " + kvp.Value + " of " + kvp.Key, "KhemistryPart/Buy");
            }
        }
    }

    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KhemistryConstructionResourcesScenario : ScenarioModule
    {
        private readonly Dictionary<string, double> _loadedResources =
            new Dictionary<string, double>();
        private bool _loadedLedgerPending;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _loadedResources.Clear();
            _loadedLedgerPending = false;

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
                _loadedLedgerPending = true;
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

            _loadedLedgerPending = true;
            TryApplyLoadedLedger();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            IDictionary<string, double> resources = _loadedLedgerPending
                ? _loadedResources : KShared.Instance?.ResourceDict;
            if (resources == null)
            {
                KShared.LogError("Could not save construction resources because KShared is unavailable.",
                    "KhemistryConstructionResourcesScenario/OnSave");
                return;
            }

            while (node.HasValue("initialized")) node.RemoveValue("initialized");
            while (node.HasNode("RESOURCE")) node.RemoveNode("RESOURCE");
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
        }

        public void Update()
        {
            if (_loadedLedgerPending)
                TryApplyLoadedLedger();
        }

        private void TryApplyLoadedLedger()
        {
            KShared shared = KShared.Instance;
            if (!_loadedLedgerPending || shared?.ResourceDict == null) return;

            shared.ResourceDict.Clear();
            foreach (KeyValuePair<string, double> resource in _loadedResources)
                shared.ResourceDict[resource.Key] = resource.Value;
            _loadedLedgerPending = false;
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
            if (_instance == this) _instance = null;
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
                out errorMessage))
                return false;

            if (!TryCheckBalances(totalCost, out errorMessage))
                return false;

            errorMessage = string.Empty;
            return true;
        }

        internal static bool TryCommitRolloutCost(ShipConstruct ship, out string error)
        {
            if (!TryGetShipCost(ship, out Dictionary<string, double> totalCost, out error))
                return false;
            if (!TryCheckBalances(totalCost, out error))
                return false;

            Dictionary<string, double> balances = KShared.Instance?.ResourceDict;
            if (balances == null)
            {
                error = "Khemistry construction resources are not available.";
                return false;
            }
            Dictionary<string, double> updatedBalances = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> cost in totalCost)
            {
                if (!balances.TryGetValue(cost.Key, out double current))
                {
                    error = "The " + cost.Key + " balance disappeared before rollout.";
                    return false;
                }

                double updated = current - cost.Value;
                if (double.IsNaN(updated) || double.IsInfinity(updated) || updated < 0d)
                {
                    error = "The " + cost.Key + " balance changed before rollout.";
                    return false;
                }
                updatedBalances[cost.Key] = updated;
            }

            // Commit only after every result has been calculated, keeping the ledger unchanged if
            // any resource fails validation.
            foreach (KeyValuePair<string, double> balance in updatedBalances)
            {
                balances[balance.Key] = balance.Value;
                KShared.Log("Deducted rollout construction cost for " + balance.Key + ".",
                    "KhemistryResourceCheckManager/TryCommitRolloutCost");
            }

            error = string.Empty;
            return true;
        }

        private static bool TryGetShipCost(ShipConstruct ship,
            out Dictionary<string, double> totalCost, out string error)
        {
            totalCost = new Dictionary<string, double>();
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
                    if (module == null || !module.CostConfigurationValid
                        || module.ResourceDict == null)
                    {
                        error = "A part has an invalid Khemistry construction-cost configuration.";
                        return false;
                    }

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
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryCheckBalances(Dictionary<string, double> totalCost,
            out string error)
        {
            Dictionary<string, double> balances = KShared.Instance?.ResourceDict;
            if (balances == null)
            {
                error = "Khemistry construction resources are not available.";
                return false;
            }

            foreach (KeyValuePair<string, double> cost in totalCost)
            {
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
            }

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
