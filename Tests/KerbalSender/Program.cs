using System;
using System.Collections.Generic;
using Khemistry;
public class KSPField : Attribute { }
public class KSPEvent : Attribute { public bool guiActive, guiActiveEditor, active, groupStartCollapsed; public string guiName, groupName, groupDisplayName; }
public class Vessel { public bool isEVA=true, LandedOrSplashed=true; public Body mainBody=new Body(); }
public class Body { public string name="Kerbin"; }
public static class HighLogic { public static bool LoadedSceneIsFlight=true; }
public static class FlightGlobals { public static Vessel ActiveVessel; public static string GetHomeBodyName()=>"Kerbin"; }
public class PartResourceLibrary { public static PartResourceLibrary Instance=new PartResourceLibrary(); public object GetDefinition(string name)=>name=="Water"?new object():null; }
public enum ScreenMessageStyle { UPPER_CENTER }
public class ScreenMessage { public ScreenMessage(string m,float d,ScreenMessageStyle s){} }
public static class ScreenMessages { public static void PostScreenMessage(ScreenMessage m){} }
namespace Khemistry
{
    public class KShared
    {
        public static KShared Instance=new KShared();
        public Dictionary<string,double> ResourceDict=new Dictionary<string,double>();
        public Action<string> choose;
        public List<string> options;
        public static bool IsFinite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public void ShowSelector(string title,List<string> labels,Action<string> callback){options=labels;choose=callback;}
        public void ShowResourceSelector(List<string> names,Action<string> callback)=>ShowSelector("",names,callback);
    }
    public class Material { public string name="Wood"; }
    public class KhemistryMaterialInstance
    {
        public Material material=new Material(); public int amount=10; public float volume=0.25f; public string shape="Log",size="Small";
        public double TotalVolume=>(double)amount*volume;
        public KhemistryMaterialInstance(){}
        public KhemistryMaterialInstance(KhemistryMaterialInstance m){material=m.material;amount=m.amount;volume=m.volume;shape=m.shape;size=m.size;}
        public void UpdateParams(string context){}
    }
    public partial class KhemistryKerbal
    {
        private bool _disabledDuplicate=false;
        public Vessel vessel;
        public bool HasFluidSuitCell=true,HasMaterialSuitCell=true;
        public Dictionary<string,double> suit=new Dictionary<string,double>();
        public List<KhemistryMaterialInstance> materialSuitCellContents=new List<KhemistryMaterialInstance>();
        public class Event { public bool active; }
        public Dictionary<string,Event> Events=new Dictionary<string,Event>{{"SendSuitResourcesToKSC",new Event()},{"SendSuitMaterialsToKSC",new Event()}};
        private Dictionary<string,double> GetSuitCellDict()=>new Dictionary<string,double>(suit);
        private void SetSuitCellFromDict(Dictionary<string,double> d)=>suit=d;
        public void Refresh()=>UpdateKSCSenderControls();
    }
}
class Program
{
    static int count;
    static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
    static void Pick()=>KShared.Instance.choose(KShared.Instance.options[0]);
    static int Main()
    {
        try
        {
            var k=new KhemistryKerbal { vessel=new Vessel() }; FlightGlobals.ActiveVessel=k.vessel;
            k.Refresh();Check(!k.Events["SendSuitResourcesToKSC"].active&&!k.Events["SendSuitMaterialsToKSC"].active,"hidden without overhaul");
            var received=new List<KhemistryMaterialInstance>();
            KerbalKSCBridge.SendMaterial=m=>{received.Add(m);return true;};
            k.Refresh();Check(k.Events["SendSuitResourcesToKSC"].active&&k.Events["SendSuitMaterialsToKSC"].active,"overhaul controls");
            k.suit["Water"]=150;k.SendSuitResourcesToKSC();Pick();
            Check(k.suit["Water"]==50&&KShared.Instance.ResourceDict["Water"]==100,"resource cap and dictionary transfer");
            k.SendSuitResourcesToKSC();Pick();Check(!k.suit.ContainsKey("Water")&&KShared.Instance.ResourceDict["Water"]==150,"empty entry removed");
            k.suit["Water"]=10;k.SendSuitResourcesToKSC();k.vessel.LandedOrSplashed=false;Pick();
            Check(k.suit["Water"]==10,"landing rechecked");
            k.vessel.LandedOrSplashed=true;k.SendSuitResourcesToKSC();FlightGlobals.ActiveVessel=new Vessel();Pick();
            Check(k.suit["Water"]==10,"active vessel rechecked");
            FlightGlobals.ActiveVessel=k.vessel;k.SendSuitResourcesToKSC();k.vessel.mainBody.name="Mun";Pick();Check(k.suit["Water"]==10,"home body rechecked");
            k.vessel.mainBody.name="Kerbin";
            KShared.Instance.ResourceDict["Water"]=double.MaxValue;k.SendSuitResourcesToKSC();Pick();Check(k.suit["Water"]==10,"full ledger preserves source");
            KShared.Instance.ResourceDict["Water"]=0;k.SendSuitResourcesToKSC();KerbalKSCBridge.SendMaterial=null;Pick();Check(k.suit["Water"]==10,"dependency rechecked");
            KerbalKSCBridge.SendMaterial=m=>{received.Add(m);return true;};
            var material=new KhemistryMaterialInstance();k.materialSuitCellContents.Add(material);
            k.SendSuitMaterialsToKSC();Pick();Check(material.amount==6&&received[0].amount==4&&received[0].TotalVolume==1,"material volume cap");
            Check(!ReferenceEquals(material,received[0]),"destination owns copy");
            KerbalKSCBridge.SendMaterial=m=>false;k.SendSuitMaterialsToKSC();Pick();Check(material.amount==6,"failed ledger preserves source");
            KerbalKSCBridge.SendMaterial=m=>{received.Add(m);return true;};
            k.SendSuitMaterialsToKSC();k.materialSuitCellContents.Clear();Pick();Check(received.Count==1,"stale selection rejected");
            k.materialSuitCellContents.Add(material);material.volume=2;k.SendSuitMaterialsToKSC();Pick();Check(material.amount==6,"oversized unit not destroyed");
            material.volume=0.25f;k.KSCMaterialMaxVolume=10;k.SendSuitMaterialsToKSC();Pick();Check(k.materialSuitCellContents.Count==0&&received.Count==2,"whole stack sent");
            k.HasFluidSuitCell=false;k.HasMaterialSuitCell=false;k.Refresh();Check(!k.Events["SendSuitResourcesToKSC"].active&&!k.Events["SendSuitMaterialsToKSC"].active,"missing cells hidden");
            Console.WriteLine(count+" kerbal sender checks passed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
