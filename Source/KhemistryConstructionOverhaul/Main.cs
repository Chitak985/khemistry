using CustomPreLaunchChecks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KhemistryConstructionOverhaul
{
    public class KhemistryLogger
    {
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
        public KhemistryLogger logging = new KhemistryLogger();
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
        public KhemistryLogger logging = new KhemistryLogger();
        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain = GameObject.Find("KhemistryConstructionResourceManager GameObject");

        // Maximum resources sendable
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Send stored resources to the KSC", active = true,
         groupName = "fusionreactor", groupDisplayName = "Send stored resources to the KSCDisplayName", groupStartCollapsed = false)]
        public void SendResources()
        {
            logging.Log("Sending resources to the KSC");
            if (HighLogic.LoadedSceneIsFlight)
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
                logging.Log("Attempt to send resources while in the VAB, error printed.", "KhemistryGeneratorPart/SendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage("This does not work right now.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
            }
        }
    }

    public class KhemistryPart : PartModule
    {
        // Logger
        public KhemistryLogger logging = new KhemistryLogger();
        // GameObject pointing to the KhemistryConstructionResourceManager
        GameObject gameObjectMain = GameObject.Find("KhemistryConstructionResourceManager GameObject");

        // Resource to use when spawning the part
        [KSPField(isPersistant = false)]
        public string ResourceCostName1 = "H2O";

        // Resource amount to use when spawning the part
        [KSPField(isPersistant = false)]
        public float ResourceCostAmount1 = 1.0f;

        public List<string> BuyCheck()
        {
            string dict = "{";
            foreach (string str in gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.Keys)
            {
                dict += str+": "+gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict[str].ToString()+", ";
            }
            dict += "}";
            logging.Log("BuyCheck! Resource is "+ResourceCostName1+" with amount "+ResourceCostAmount1.ToString()+". Dictionary: " + dict, "KhemistryPart/BuyCheck");
            List<string> tmp = new List<string>();
            if (gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict.ContainsKey(ResourceCostName1))
            {
                if (gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict[ResourceCostName1] >= ResourceCostAmount1)
                {
                    tmp.Add("");
                    tmp.Add("1");
                    logging.Log("Part creation success!", "KhemistryPart/BuyCheck");
                }
                else
                {
                    tmp.Add("Not enough " + ResourceCostName1 + "!, you need " + (ResourceCostAmount1 - gameObjectMain.GetComponent<KhemistryConstructionResourceManager>().ResourceDict[ResourceCostName1]).ToString() + " more!");
                    tmp.Add("0");
                    logging.Log("Not enough of resource!", "KhemistryPart/BuyCheck");
                }
            }
            else
            {
                tmp.Add("You have never obtained " + ResourceCostName1 + "!");
                tmp.Add("0");
                logging.Log("Never obtained resource!", "KhemistryPart/BuyCheck");
            }
            return tmp;
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryCPLCChecksRegistrar : MonoBehaviour
    {
        // Logger
        public KhemistryLogger logging = new KhemistryLogger();
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
        public KhemistryLogger logging = new KhemistryLogger();
        public static int funds { get; set; } = 0; // funds needed to launch
        public string errorMessage = "UNKNOWN ERROR";

        public bool Test()
        {
            logging.Log("Test() fired!");
            foreach (Part part in EditorLogic.fetch.ship.parts)
            {
                KhemistryPart module = part.FindModuleImplementing<KhemistryPart>();
                if (module == null)
                {
                    continue;
                }
                List<string> tmp = module.BuyCheck();
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
        public string GetAbortOption() => "Abort launch.";

        public KhemistryResourceCheckManager(string launchSiteName)
        {
            logging.Log("Constructor fired!", "KhemistryResourceCheckManager/GetWarningTitle");
        }

        public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
        {
            // i hate C#
            //logging.Log("GetKhemistryTest("+ launchSiteName+ ") fired!", "KhemistryResourceCheckManager/GetWarningTitle");
            return new KhemistryResourceCheckManager(launchSiteName);
        }
    }
}
