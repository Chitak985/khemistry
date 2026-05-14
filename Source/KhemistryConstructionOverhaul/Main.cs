using CustomPreLaunchChecks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KhemistryConstructionOverhaul
{
    public class KhemistryLogger
    {
        private KhemistryLogger() { }
        private static KhemistryLogger _instance;
        private static readonly object _lock = new object();
        public static KhemistryLogger GetInstance()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new KhemistryLogger();
                    }
                }
            }
            return _instance;
        }

        public void Log(string n, string func=null)
        {
            if (func != null)
            {
                Debug.LogError("KhemistryConstructionOverhaul ("+func+") : " + n);
            }
            else
            {
                Debug.LogError("KhemistryConstructionOverhaul: " + n);
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class KhemistryConstructionResourceManager : MonoBehaviour
    {
        // Logger
        public KhemistryLogger logging = KhemistryLogger.GetInstance();
        // Stores resources available
        public Dictionary<string, float> ResourceDict = new Dictionary<string, float>();

        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain;

        public void Start()
        {
            if (this.gameObject.name != "KhemistryConstructionResourceManager GameObject")
            {
                gameObjectMain = new GameObject();
                DontDestroyOnLoad(gameObjectMain);
                gameObjectMain.AddComponent<KhemistryConstructionResourceManager>();
                gameObjectMain.name = "KhemistryConstructionResourceManager GameObject";

                if(!gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.ContainsKey("H2O"))
                    gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.Add("H2O", 5.0f);  // Add temporary resources

                logging.Log("Game object created!", "KhemistryConstructionResourceManager/Start");
            }
        }
    }

    public class KhemistryGeneratorPart : PartModule
    {
        // Logger
        public KhemistryLogger logging = KhemistryLogger.GetInstance();
        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain = GameObject.Find("KhemistryConstructionResourceManager GameObject");

        // Maximum resources sendable
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Send stored resources to the KSC", active = true,
         groupName = "resourcesending", groupDisplayName = "Resource Sender", groupStartCollapsed = false)]
        public void SendResources()
        {
            logging.Log("Sending resources to the KSC");
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (FlightGlobals.ActiveVessel.mainBody.name == FlightGlobals.GetHomeBodyName())
                {
                    double result = part.RequestResource(PartResourceLibrary.Instance.GetDefinition("H2O").id, ResourceMaxAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
                    if (gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.ContainsKey("H2O"))
                    {
                        gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict["H2O"] += (float)result;
                        logging.Log(result.ToString() + " of H2O added to the dict.", "KhemistryGeneratorPart/SendResources");
                    }
                    else
                    {
                        gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.Add("H2O", (float)result);
                        logging.Log(result.ToString() + " of H2O added to the dict as a new resource.", "KhemistryGeneratorPart/SendResources");
                    }
                }
                else
                {
                    logging.Log("Must send resources while on the home world, error printed.", "KhemistryGeneratorPart/SendResources");
                    ScreenMessages.PostScreenMessage(new ScreenMessage("You can only send resources to the KSC while landed on the body it is on.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                }
            }
            else
            {
                logging.Log("Attempt to send resources while in the VAB, error printed.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("This does not work right now.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
            }
        }
    }

    public class KhemistryPart : PartModule
    {
        // Logger
        public KhemistryLogger logging = KhemistryLogger.GetInstance();
        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain = GameObject.Find("KhemistryConstructionResourceManager GameObject");
        KhemistryConstructionResourceManager componentMain = GameObject.Find("KhemistryConstructionResourceManager GameObject").GetComponent<KhemistryConstructionResourceManager>();

        public Dictionary<string, float> ResourceDict;

        public override void OnLoad(ConfigNode node)
        {
            logging.Log("OnLoad triggered", "KhemistryPart/OnLoad");
            base.OnLoad(node);
            // Set up ResourceDict
            string[] names = node.GetNode("RESOURCE_COST_NAMES").GetValues("name");
            string[] amountsStr = node.GetNode("RESOURCE_COST_AMOUNTS").GetValues("amount");
            List<float> amounts = amountsStr.Select(float.Parse).ToList();
            ResourceDict = names.Zip(amounts, (k, v) => new { k, v })
                                   .ToDictionary(x => x.k, x => x.v);
        }

        public List<string> BuyCheck()
        {
            List<string> tmp = new List<string>();

            if (componentMain.ResourceDict == null)
            {
                tmp.Add("A null reference error occured! Info: componentMain.ResourceDict is null.");
                tmp.Add("0");
                logging.Log("componentMain.ResourceDict is null!", "KhemistryPart/BuyCheck");
                return tmp;
            }
            if (ResourceDict == null)
            {
                tmp.Add("A null reference error occured! Info: ResourceDict is null.");
                tmp.Add("0");
                logging.Log("ResourceDict is null!", "KhemistryPart/BuyCheck");
                return tmp;
            }

            foreach (string resourceName in ResourceDict.Keys)
            {
                if (componentMain.ResourceDict.ContainsKey(resourceName))
                {
                    if (componentMain.ResourceDict[resourceName] < ResourceDict[resourceName])
                    {
                        tmp.Add("Not enough " + resourceName + "!, you need " + (ResourceDict[resourceName] - componentMain.ResourceDict[resourceName]).ToString() + " more!");
                        tmp.Add("0");
                        logging.Log("Not enough of resource!", "KhemistryPart/BuyCheck");
                        return tmp;
                    }
                }
                else
                {
                    tmp.Add("You have never obtained " + resourceName + "!");
                    tmp.Add("0");
                    logging.Log("Never obtained resource!", "KhemistryPart/BuyCheck");
                    return tmp;
                }
            }
            tmp.Add("");
            tmp.Add("1");
            logging.Log("Part creation success!", "KhemistryPart/BuyCheck");
            return tmp;
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryCPLCChecksRegistrar : MonoBehaviour
    {
        // Logger
        public KhemistryLogger logging = KhemistryLogger.GetInstance();
        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain = GameObject.Find("KhemistryConstructionResourceManager GameObject");
        public void Awake()
        {
            logging.Log("Registering check and adding resource!");
            if (!gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.ContainsKey("H2O"))
                gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.Add("H2O", 5.0f);  // Add temporary resources
            CPLC.RegisterCheck(KhemistryResourceCheckManager.GetKhemistryTest);
        }
    }

    public class KhemistryResourceCheckManager : PreFlightTests.IPreFlightTest
    {
        // Logger
        public KhemistryLogger logging = KhemistryLogger.GetInstance();
        public static int funds { get; set; } = 0; // funds needed to launch
        public string errorMessage = "UNKNOWN ERROR";

        public bool Test()
        {
            logging.Log("Test() fired!");
            foreach (Part part in EditorLogic.fetch.ship.parts)
            {
                KhemistryPart module = part.partInfo.partPrefab.FindModuleImplementing<KhemistryPart>();
                if (module == null)
                {
                    continue;
                }
                List<string> tmp = module.BuyCheck();
                if (tmp == null)
                {
                    errorMessage = "A null reference error occured! Info: BuyCheck result for " + part.name+" is null.";
                    return false;
                }
                if (tmp[1] == "0")
                {
                    errorMessage = tmp[0];
                    return false;
                }
            }
            return true;
        }

        public string GetWarningTitle()
        {
            logging.Log("Getting warning title", "KhemistryResourceCheckManager/GetWarningTitle");
            return "Khemistry Resource Check";
        }

        public string GetWarningDescription() => errorMessage;
        public string GetProceedOption() => null;
        public string GetAbortOption() => "Abort launch";

        public KhemistryResourceCheckManager(string launchSiteName)
        {
            logging.Log("Constructor fired!", "KhemistryResourceCheckManager/GetWarningTitle");
        }

        public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
        {
            // i hate C# (no ref to logging since this is called from a different class)
            //logging.Log("GetKhemistryTest("+ launchSiteName+ ") fired!", "KhemistryResourceCheckManager/GetWarningTitle");
            return new KhemistryResourceCheckManager(launchSiteName);
        }
    }
}
