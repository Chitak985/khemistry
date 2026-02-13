using System;
using System.Linq;
using System.Collections.Generic;
using CustomPreLaunchChecks;

namespace KhemistryConstructionOverhaul
{
  public class Module : PartModule
  {
    // Resource to use when spawning the part
    [KSPField(isPersistant = false)]
    public string ResourceCostName1 = "H2O";

    // Resource amount to use when spawning the part
    [KSPField(isPersistant = false)]
    public float ResourceCostAmount1 = 1.0f;

    public bool BuyCheck()
    {
      if(KhemistryConstructionResourceManager.ResourceDict.ContainsKey(ResourceCostName1))
      {
        if(KhemistryConstructionResourceManager.ResourceDict[ResourceCostName1] >= ResourceCostAmount1)
        {
          return ["",true];
        } else {
          return ["Not enough "+ResourceCostName1+"!",false];
        }
      } else {
        return ["You have never obtained "+ResourceCostName1+"!",false];
      }
    }
  }

  
  [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
  public class KhemistryConstructionResourceManager : MonoBehaviour
  {
    // Stores resources available
    public Dictionary<string, float> ResourceDict = new Dictionary<string, float>();
    
    // Add starting resources
    ResourceDict.Add("H2O", 5.0f);
  }

  [KSPAddon(KSPAddon.Startup.MainMenu, true)]
  public class KhemistryCPLCChecksRegistrar : MonoBehaviour
  {
    public void Awake()
    {
      CPLC.RegisterCheck(KhemistryResourceCheckManager.KhemistryResourceTest);
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

    public static PreFlightTests.IPreFlightTest GetKhemistryTest(string launchSiteName)
    {
      return new KhemistryResourceTest(launchSiteName);
    }

    public KhemistryResourceTest(string launchSiteName)
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
  }
}