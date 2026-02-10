using System;
using System.Linq;
using System.Collections.Generic;

// Everything here is currently taken from FFT, this will be changed ASAP

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
}