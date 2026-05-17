using CustomPreLaunchChecks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KhemistryConstructionOverhaul
{
    // Single shared singleton: handles logging and resource storage.
    // KSPAddon with once=true means KSP creates this once at boot and
    // DontDestroyOnLoad keeps it alive across all scene transitions.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KCOShared : MonoBehaviour
    {
        private static KCOShared _instance;
        public static KCOShared Instance => _instance;

        public Dictionary<string, float> ResourceDict = new Dictionary<string, float>();

        public void Awake()
        {
            // If a duplicate somehow exists, destroy it and keep the first
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Log("KCOShared initialized.", "KCOShared/Awake");
        }

        public void Log(string message, string func = null)
        {
            if (func != null)
                Debug.Log("KhemistryConstructionOverhaul (" + func + "): " + message);
            else
                Debug.Log("KhemistryConstructionOverhaul: " + message);
        }

        public void LogError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("KhemistryConstructionOverhaul (" + func + "): " + message);
            else
                Debug.LogError("KhemistryConstructionOverhaul: " + message);
        }
    }

    public class KhemistryGeneratorPart : PartModule
    {
        // Maximum resources sendable per activation
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Send stored resources to the KSC", active = true,
         groupName = "resourcesending", groupDisplayName = "Resource Sender", groupStartCollapsed = false)]
        public void SendResources()
        {
            var shared = KCOShared.Instance;
            if (shared == null)
            {
                Debug.LogError("KhemistryConstructionOverhaul: Shared instance is null in SendResources!");
                return;
            }

            shared.Log("Sending resources to the KSC", "KhemistryGeneratorPart/SendResources");

            if (!HighLogic.LoadedSceneIsFlight)
            {
                shared.Log("Attempt to send resources while not in flight, error printed.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("This does not work right now.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (FlightGlobals.ActiveVessel.mainBody.name != FlightGlobals.GetHomeBodyName())
            {
                shared.Log("Must send resources while on the home world.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("You can only send resources to the KSC while landed on the body it is on.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            double result = part.RequestResource(PartResourceLibrary.Instance.GetDefinition("H2O").id, ResourceMaxAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW);

            if (shared.ResourceDict.ContainsKey("H2O"))
                shared.ResourceDict["H2O"] += (float)result;
            else
                shared.ResourceDict.Add("H2O", (float)result);

            shared.Log(result + " of H2O added to the ResourceDict.", "KhemistryGeneratorPart/SendResources");
        }
    }

    public class KhemistryPart : PartModule
    {
        // Resource costs for this part, populated from the part config
        public Dictionary<string, float> ResourceDict;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            var shared = KCOShared.Instance;
            shared?.Log("OnLoad triggered", "KhemistryPart/OnLoad");

            if (!node.HasNode("RESOURCE_COST_NAMES") || !node.HasNode("RESOURCE_COST_AMOUNTS"))
            {
                shared?.LogError(
                    "Part \"" + part.name + "\" has a KhemistryPart module but is missing " +
                    "RESOURCE_COST_NAMES and/or RESOURCE_COST_AMOUNTS in its config. " +
                    "This part will be treated as free.",
                    "KhemistryPart/OnLoad");
                return;
            }

            string[] names = node.GetNode("RESOURCE_COST_NAMES").GetValues("name");
            string[] amountsStr = node.GetNode("RESOURCE_COST_AMOUNTS").GetValues("amount");
            List<float> amounts = amountsStr.Select(float.Parse).ToList();

            ResourceDict = names.Zip(amounts, (k, v) => new { k, v })
                                .ToDictionary(x => x.k, x => x.v);
        }

        // Returns ("", "1") on success, or (errorMessage, "0") on failure.
        public List<string> BuyCheck()
        {
            var shared = KCOShared.Instance;
            var tmp = new List<string>();

            if (shared == null)
            {
                tmp.Add("A null reference error occurred! Info: Shared instance is null.");
                tmp.Add("0");
                Debug.LogError("KhemistryConstructionOverhaul: Shared instance is null in BuyCheck!");
                return tmp;
            }
            if (shared.ResourceDict == null)
            {
                tmp.Add("A null reference error occurred! Info: shared.ResourceDict is null.");
                tmp.Add("0");
                shared.LogError("shared.ResourceDict is null!", "KhemistryPart/BuyCheck");
                return tmp;
            }
            if (ResourceDict == null)
            {
                tmp.Add("A null reference error occurred! Info: part ResourceDict is null.");
                tmp.Add("0");
                shared.LogError("ResourceDict is null!", "KhemistryPart/BuyCheck");
                return tmp;
            }

            foreach (string resourceName in ResourceDict.Keys)
            {
                if (!shared.ResourceDict.ContainsKey(resourceName))
                {
                    tmp.Add("You have never obtained " + resourceName + "!");
                    tmp.Add("0");
                    shared.Log("Never obtained resource: " + resourceName, "KhemistryPart/BuyCheck");
                    return tmp;
                }
                if (shared.ResourceDict[resourceName] < ResourceDict[resourceName])
                {
                    float shortfall = ResourceDict[resourceName] - shared.ResourceDict[resourceName];
                    tmp.Add("Not enough " + resourceName + "! You need " + shortfall + " more.");
                    tmp.Add("0");
                    shared.Log("Not enough of resource: " + resourceName, "KhemistryPart/BuyCheck");
                    return tmp;
                }
            }

            tmp.Add("");
            tmp.Add("1");
            shared.Log("BuyCheck passed for part.", "KhemistryPart/BuyCheck");
            return tmp;
        }

        // Deducts resources after a successful BuyCheck.
        public void Buy()
        {
            var shared = KCOShared.Instance;
            if (shared == null || ResourceDict == null) return;

            foreach (var kvp in ResourceDict)
            {
                if (shared.ResourceDict.ContainsKey(kvp.Key))
                {
                    shared.ResourceDict[kvp.Key] -= kvp.Value;
                    shared.Log("Deducted " + kvp.Value + " of " + kvp.Key, "KhemistryPart/Buy");
                }
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryCPLCChecksRegistrar : MonoBehaviour
    {
        public void Awake()
        {
            var shared = KCOShared.Instance;
            shared?.Log("Registering pre-launch check.", "KhemistryCPLCChecksRegistrar/Awake");

            // Add temporary H2O for testing if not already present
            if (shared != null && !shared.ResourceDict.ContainsKey("H2O"))
                shared.ResourceDict.Add("H2O", 5.0f);

            CPLC.RegisterCheck(KhemistryResourceCheckManager.GetKhemistryTest);
        }
    }

    public class KhemistryResourceCheckManager : PreFlightTests.IPreFlightTest
    {
        public string errorMessage = "UNKNOWN ERROR";

        public bool Test()
        {
            var shared = KCOShared.Instance;
            shared?.Log("Test() fired!", "KhemistryResourceCheckManager/Test");

            foreach (Part part in EditorLogic.fetch.ship.parts)
            {
                KhemistryPart module = part.partInfo.partPrefab.FindModuleImplementing<KhemistryPart>();
                if (module == null) continue;

                List<string> tmp = module.BuyCheck();
                if (tmp == null)
                {
                    errorMessage = "A null reference error occurred! BuyCheck returned null for part: " + part.name;
                    return false;
                }
                if (tmp[1] == "0")
                {
                    errorMessage = tmp[0];
                    return false;
                }
            }

            // All checks passed — deduct resources
            foreach (Part part in EditorLogic.fetch.ship.parts)
            {
                KhemistryPart module = part.partInfo.partPrefab.FindModuleImplementing<KhemistryPart>();
                module?.Buy();
            }

            return true;
        }

        public string GetWarningTitle() => "Khemistry Resource Check";
        public string GetWarningDescription() => errorMessage;
        public string GetProceedOption() => null;
        public string GetAbortOption() => "Abort launch";

        public KhemistryResourceCheckManager(string launchSiteName)
        {
            KCOShared.Instance?.Log(
                "Constructor fired for site: " + launchSiteName,
                "KhemistryResourceCheckManager/Constructor");
        }

        public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
        {
            return new KhemistryResourceCheckManager(launchSiteName);
        }
    }
}