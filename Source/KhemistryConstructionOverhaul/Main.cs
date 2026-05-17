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
                LogError("Another instance of KCOShared was found, self destructing...", "KCOShared/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            // Starting resources
            ResourceDict.Add("CuWiring", 10.0f);       // Copper wires
            ResourceDict.Add("Sn60Pb40Alloy", 10.0f);  // Soldering
            ResourceDict.Add("Aluminium6061", 10.0f);  // Simple construction material
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
        // Maximum amount of a resource sendable per activation
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

            shared.Log("SendResources triggered.", "KhemistryGeneratorPart/SendResources");

            if (!HighLogic.LoadedSceneIsFlight)
            {
                shared.Log("Attempt to send resources while not in flight.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("This does not work right now.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (FlightGlobals.ActiveVessel.mainBody.name != FlightGlobals.GetHomeBodyName())
            {
                shared.Log("Must send resources while on the home world.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("You can only send resources to the KSC while landed on the body it is on.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            // Gather all resources present on the vessel with amount > 0, deduplicated by name
            var availableResources = FlightGlobals.ActiveVessel.parts
                .SelectMany(p => p.Resources.Cast<PartResource>())
                .Where(r => r.amount > 0)
                .GroupBy(r => r.resourceName)
                .Select(g => g.Key)
                .ToList();

            if (availableResources.Count == 0)
            {
                shared.Log("No resources available on vessel.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("No resources available to send.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            // Build one button per resource inside a scroll list so the dialog
            // never grows taller than 150px regardless of how many resources exist.
            // Cancel sits outside the scroll list so it is always visible.
            // Each button needs an explicit height so the scroll list can calculate
            // the true content height -- without this it snaps back to the top when scrolling.
            const float buttonHeight = 30f;
            var buttons = new List<DialogGUIBase>();
            foreach (string resourceName in availableResources)
            {
                string captured = resourceName; // capture loop variable for the lambda
                buttons.Add(new DialogGUIButton(
                    captured,
                    () => TransferResource(captured),
                    240f,         // explicit width
                    buttonHeight, // explicit height so scroll list can measure content correctly
                    true          // dismiss dialog on click
                ));
            }

            // Total content height must be passed explicitly so the scroll list
            // knows where the content ends and doesn't snap back to the top.
            // We set size as a property instead of using the extended constructor
            // to avoid requiring UnityEngine.TextRenderingModule in the project.
            var layout = new DialogGUIVerticalLayout(
                buttons.ToArray()
            );

            var scrollList = new DialogGUIScrollList(
                new Vector2(250f, 150f),
                false,
                true,
                layout
            );

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KCOResourceSelector",
                    "Select a resource to send up to " + ResourceMaxAmount + " units of to the KSC:",
                    "Send Resources",
                    HighLogic.UISkin,
                    new Rect(0.5f, 0.5f, 300f, 0f), // 0.5/0.5 = centered, 300px wide, auto height
                    scrollList,
                    new DialogGUIButton("Cancel", () => { }, true)
                ),
                false,
                HighLogic.UISkin
            );
        }

        // Called when the player picks a resource from the popup
        private void TransferResource(string resourceName)
        {
            var shared = KCOShared.Instance;
            if (shared == null)
            {
                Debug.LogError("KhemistryConstructionOverhaul: Shared instance is null in TransferResource!");
                return;
            }

            var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
            if (def == null)
            {
                shared.LogError("Could not find resource definition for: " + resourceName, "KhemistryGeneratorPart/TransferResource");
                ScreenMessages.PostScreenMessage(new ScreenMessage("Unknown resource: " + resourceName, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            // Drain up to ResourceMaxAmount from the whole vessel
            double taken = part.RequestResource(def.id, ResourceMaxAmount, ResourceFlowMode.ALL_VESSEL);

            if (taken <= 0)
            {
                shared.Log("No " + resourceName + " could be drained from the vessel.", "KhemistryGeneratorPart/TransferResource");
                ScreenMessages.PostScreenMessage(new ScreenMessage("No " + resourceName + " could be transferred.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (shared.ResourceDict.ContainsKey(resourceName))
                shared.ResourceDict[resourceName] += (float)taken;
            else
                shared.ResourceDict.Add(resourceName, (float)taken);

            shared.Log(taken + " of " + resourceName + " transferred to the KSC.", "KhemistryGeneratorPart/TransferResource");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                string.Format("Transferred {0:F2} units of {1} to the KSC.", taken, resourceName),
                5.0f, ScreenMessageStyle.UPPER_CENTER));
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

            // All checks passed -- deduct resources
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