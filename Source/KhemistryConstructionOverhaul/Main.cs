using CustomPreLaunchChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KhemistryConstructionOverhaul
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class KhemistryConstructionResourceManager : MonoBehaviour
    {
        // Stores resources available
        public Dictionary<string, float> ResourceDict = new Dictionary<string, float>();
    }

    public class KhemistryPart : PartModule
    {
        // Resource to use when spawning the part
        [KSPField(isPersistant = false)]
        public string ResourceCostName1 = "H2O";

        // Resource amount to use when spawning the part
        [KSPField(isPersistant = false)]
        public float ResourceCostAmount1 = 1.0f;

        public List<string> BuyCheck()
        {
            List<string> tmp = new List<string>;
            if (KhemistryConstructionResourceManager.ResourceDict.ContainsKey(ResourceCostName1))
            {
                if (KhemistryConstructionResourceManager.ResourceDict[ResourceCostName1] >= ResourceCostAmount1)
                {
                    tmp.Add("");
                    tmp.Add("1");
                }
                else
                {
                    tmp.Add("Not enough " + ResourceCostName1 + "!, you need " + (ResourceCostAmount1 - KhemistryConstructionResourceManager.ResourceDict[ResourceCostName1]).ToString() + " more!");
                    tmp.Add("0");
                }
            }
            else
            {
                tmp.Add("You have never obtained " + ResourceCostName1 + "!");
                tmp.Add("0");
            }
            return tmp;
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryCPLCChecksRegistrar : MonoBehaviour
    {
        public void Awake()
        {
            KhemistryConstructionResourceManager.ResourceDict.Add("H2O", 5.0f);  // Add temporary resources
            CPLC.RegisterCheck(KhemistryResourceCheckManager.GetKhemistryTest);
        }
    }

    public class KhemistryResourceCheckManager : PreFlightTests.IPreFlightTest
    {
        public static int funds { get; set; } = 0; // funds needed to launch
        private string launchSiteName;
        private bool allowLaunch = false;

        public bool Test()
        {
            return true;
        }
        public KhemistryResourceCheckManager(string launchSiteName)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                this.launchSiteName = launchSiteName;
            }
            else
            {
                allowLaunch = true;
            }
        }

        public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
        {
            return new KhemistryResourceCheckManager(launchSiteName);
        }
    }
}
