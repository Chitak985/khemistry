using System.IO
using UnityEngine

namespace KhemistryResourceGen
{
  [KSPAddon(KSPAddon.Startup.Instantly, false)]
  public class ResourceGenMain : MonoBehaviour
  {
    public void Start()
    {
      
      Application.Quit();
    }
  }
}
