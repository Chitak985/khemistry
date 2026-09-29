using System;
using System.Collections.Generic;
using System.Linq;
public class KSPField : Attribute { public bool isPersistant; }
public class KSPEvent : Attribute { public bool guiActive, guiActiveEditor; public string guiName; }
public class PartModule
{
    public enum StartState { None }
    public Part part = new Part();
    public virtual void OnLoad(ConfigNode n) { }
    public virtual void OnStart(StartState s) { }
}
public class Part
{
    public string name = "Test";
    public PartInfo partInfo;
    public T FindModuleImplementing<T>() where T : class => null;
}
public class PartInfo { public Part partPrefab; }
public class ConfigNode
{
    readonly Dictionary<string,string> values = new Dictionary<string,string>();
    public ConfigNode[] GetNodes(string name) => Array.Empty<ConfigNode>();
    public string[] GetValues(string name) => Array.Empty<string>();
    public void SetValue(string name, string value, bool create) => values[name] = value;
    public string GetValue(string name) => values.TryGetValue(name, out var v) ? v : "";
}
public class StoredPart { public string partName = "Cell"; public bool current = true; public ConfigNode moduleValues = new ConfigNode(); }
public static class TimeWarp { public static double fixedDeltaTime = 1; }
namespace UnityEngine { public static class Time { public static float fixedTime; } }
public enum ScreenMessageStyle { UPPER_CENTER }
public class ScreenMessage { public string text; public ScreenMessage(string t, float d, ScreenMessageStyle s) { text = t; } }
public static class ScreenMessages { public static List<string> messages = new List<string>(); public static void PostScreenMessage(ScreenMessage s) => messages.Add(s.text); }
namespace Khemistry
{
    public class KShared
    {
        public static KShared Instance;
        public void ShowResourceContents(string title, Func<Dictionary<string,double>> read) { }
        public static bool IsFinite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public static void Log(string a, string b) { }
        public static void LogWarning(string a, string b) { }
        public static void LogError(string a, string b) { }
    }
    public class KhemistryResourceNetwork
    {
        public class Transfer { internal double amount; internal Action<double> undo; }
        public static void Rollback(List<Transfer> transfers)
        { for(int i=transfers.Count-1;i>=0;i--) transfers[i].undo(transfers[i].amount); }
        public class Endpoint
        {
            public bool IsCurrent = true;
            public Part part = new Part();
            public string resourceName = "Water";
            public double amount, capacity = 100, acceptance = double.PositiveInfinity;
            public double space => capacity - amount;
            public double Request(double request)
            {
                double moved = request > 0 ? Math.Min(request, amount) : -Math.Min(-request, space);
                moved = Math.Sign(moved) * Math.Min(Math.Abs(moved), acceptance);
                amount -= moved; return moved;
            }
        }
    }
    public partial class KhemistryKerbal
    {
        public KhemistryFluidCell prefab = new KhemistryFluidCell();
        public bool inRange = true;
        public bool HasFluidSuitCell => true;
        private double _suitCellMaxAmount = 100;
        private static double GetResourceDictionaryTotal(Dictionary<string,double> d) => d.Values.Sum();
        private bool CanAddToSuitCell(string n, IEnumerable<string> keys) => true;
        public class Event { public bool active; }
        public Dictionary<string,Event> Events = new Dictionary<string,Event> { ["CancelCellTransfers"] = new Event() };
        private bool IsStoredPartCurrent(StoredPart s) => s != null && s.current;
        private StoredPart GetCellModuleSnapshot(StoredPart s) => s;
        private KhemistryFluidCell ReadFluidCellPrefab(string name) => prefab;
        private Dictionary<string,double> ReadCellResourceDictionary(StoredPart s)
            => KhemistryFluidCell.DeserializeResources(s.moduleValues.GetValue("StoredResourcesData"));
        private void NotifyInventoryChanged() { }
        private double ReadResourceAmountValue(StoredPart s, string name) => ReadCellResourceDictionary(s).TryGetValue(name,out var v) ? v : 0;
        private double ReadResourceAmount(StoredPart s) => ReadCellResourceDictionary(s).Values.Sum();
        private bool IsResourceAllowedForAddition(StoredPart s, string name) => prefab.CanAddResource(name, ReadCellResourceDictionary(s).Keys);
        private double ReadCellTotalFreeSpace(StoredPart s) => prefab.ResourceMaxAmount - ReadResourceAmount(s);
        private bool IsPartCurrentAndInRange(Part p, float range) => inRange;
        private bool WriteResourceAmount(StoredPart s, string name, double amount)
        {
            var d = ReadCellResourceDictionary(s);
            if (amount <= 0) d.Remove(name); else d[name] = amount;
            s.moduleValues.SetValue("StoredResourcesData", KhemistryFluidCell.SerializeResources(d), true); return true;
        }
        Dictionary<string,double> suit = new Dictionary<string,double>();
        private Dictionary<string,double> GetSuitCellDict() => new Dictionary<string,double>(suit);
        private void SetSuitCellFromDict(Dictionary<string,double> d) => suit = d;
        private double RequestSuitCellResource(string name, double amount)
        {
            suit.TryGetValue(name, out var current);
            var moved = amount > 0 ? Math.Min(amount, current) : amount;
            suit[name] = current - moved; return moved;
        }
        public void Start(StoredPart s, KhemistryResourceNetwork.Endpoint e, bool input, double amount) => StartCellTransfer(s,e,input,amount,10);
        public void Tick() { UnityEngine.Time.fixedTime++; TickCellTransfers(); }
        public int Jobs => cellTransfers.Count;
        public double Amount(StoredPart s, string name = "Water") => ReadResourceAmountValue(s,name);
    }
}
