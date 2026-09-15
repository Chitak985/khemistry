// Engine doubles: the three production files are compiled directly into this test harness.
using System;
using System.Collections.Generic;
using System.Linq;
public enum ResourceFlowMode { NO_FLOW, ALL_VESSEL, STAGE_PRIORITY_FLOW, NULL }
public enum ModifierChangeWhen { CONSTANTLY }
public enum ModifierStagingSituation { CURRENT }
public interface IPartMassModifier { float GetModuleMass(float mass, ModifierStagingSituation situation); ModifierChangeWhen GetModuleMassChangeWhen(); }
public interface IPartCostModifier { float GetModuleCost(float cost, ModifierStagingSituation situation); ModifierChangeWhen GetModuleCostChangeWhen(); }
public class ConfigNode
{
    public ConfigNode(string name = "") { }
    private Dictionary<string, string> values = new Dictionary<string, string>();
    private List<(string, ConfigNode)> nodes = new List<(string, ConfigNode)>();
    public string GetValue(string key) => values.TryGetValue(key, out string v) ? v : null;
    public void AddValue(string key, object value) => values[key] = value.ToString();
    public ConfigNode AddNode(string name) { var n = new ConfigNode(name); nodes.Add((name, n)); return n; }
    public void AddNode(ConfigNode node) => nodes.Add(("STORED_RESOURCE", node));
    public ConfigNode[] GetNodes(string name) => nodes.Where(n => n.Item1 == name).Select(n => n.Item2).ToArray();
    public void RemoveNodes(string name) => nodes.RemoveAll(n => n.Item1 == name);
    public void CopyTo(ConfigNode node) { node.values = new Dictionary<string, string>(values); node.nodes = new List<(string, ConfigNode)>(nodes); }
}
public class PartModule
{
    public Part part;
    public virtual void OnLoad(ConfigNode node) { }
    public virtual void OnSave(ConfigNode node) { }
}
public class PartResource { public string resourceName; public double amount, maxAmount; public bool flowState = true; }
public class PartResourceDefinition { public string name; public int id; public double density = 0.5, unitCost = 2; public ResourceFlowMode resourceFlowMode = ResourceFlowMode.ALL_VESSEL; }
public class PartResourceLibrary
{
    public static PartResourceLibrary Instance = new PartResourceLibrary();
    public readonly List<PartResourceDefinition> definitions = new List<PartResourceDefinition>();
    public PartResourceDefinition GetDefinition(string name) => definitions.FirstOrDefault(d => d.name == name);
    public PartResourceDefinition GetDefinition(int id) => definitions.FirstOrDefault(d => d.id == id);
}
public class Part
{
    public Vessel vessel;
    public List<PartResource> Resources = new List<PartResource>();
    public List<PartModule> Modules = new List<PartModule>();
    public bool RemoveResource(PartResource resource) => Resources.Remove(resource);
    public bool crossfeed = true;
    public int GetResourcePriority() => 0;
    public bool CanCrossfeed(Part target, int id, ResourceFlowMode flow) => target == this || crossfeed && target.crossfeed;
    public double RequestResource(string name, double amount, ResourceFlowMode flow)
    {
        double moved = 0;
        foreach (Part p in vessel?.parts ?? new List<Part> { this })
        {
            if (p != this && (flow == ResourceFlowMode.NO_FLOW || !CanCrossfeed(p, 0, flow))) continue;
            foreach (PartResource r in p.Resources.Where(r => r.resourceName == name && r.flowState))
            {
                double step = amount > 0 ? Math.Min(amount - moved, r.amount)
                    : -Math.Min(-amount + moved, r.maxAmount - r.amount);
                r.amount -= step;
                moved += step;
            }
        }
        return moved;
    }
}
public class Vessel { public bool loaded = true; public List<Part> parts = new List<Part>(); }
public static class FlightGlobals { public static List<Vessel> VesselsLoaded = new List<Vessel>(); }
public static class TimeWarp { public static float fixedDeltaTime = 1; }
public class KSPAddon : Attribute { public enum Startup { Flight } public KSPAddon(Startup startup, bool once) { } }
namespace UnityEngine
{
    public class MonoBehaviour { }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public static class Time { public static float fixedTime; }
}
public struct ResourceRatio { public string ResourceName; }
public interface IResourceBroker
{
    double AmountAvailable(Part p, string n, double dt, ResourceFlowMode f);
    double AmountAvailable(Part p, int n, double dt, ResourceFlowMode f);
    double StorageAvailable(Part p, string n, double dt, ResourceFlowMode f, double fill);
    double StorageAvailable(Part p, int n, double dt, ResourceFlowMode f, double fill);
    double RequestResource(Part p, string n, double a, double dt, ResourceFlowMode f);
    double RequestResource(Part p, int n, double a, double dt, ResourceFlowMode f);
    double StoreResource(Part p, string n, double a, double dt, ResourceFlowMode f);
    double StoreResource(Part p, int n, double a, double dt, ResourceFlowMode f);
}
public class ResourceBroker : IResourceBroker
{
    private string Name(int id) => PartResourceLibrary.Instance.GetDefinition(id).name;
    public double AmountAvailable(Part p, string n, double dt, ResourceFlowMode f) => p.vessel.parts.SelectMany(x => x.Resources).Where(r => r.resourceName == n && r.flowState).Sum(r => r.amount);
    public double StorageAvailable(Part p, string n, double dt, ResourceFlowMode f, double fill) => p.vessel.parts.SelectMany(x => x.Resources).Where(r => r.resourceName == n && r.flowState).Sum(r => Math.Max(0, r.maxAmount * fill - r.amount));
    public double RequestResource(Part p, string n, double a, double dt, ResourceFlowMode f) => p.RequestResource(n, a, f);
    public double StoreResource(Part p, string n, double a, double dt, ResourceFlowMode f) => p.RequestResource(n, -a, f);
    public double AmountAvailable(Part p, int n, double dt, ResourceFlowMode f) => AmountAvailable(p, Name(n), dt, f);
    public double StorageAvailable(Part p, int n, double dt, ResourceFlowMode f, double fill) => StorageAvailable(p, Name(n), dt, f, fill);
    public double RequestResource(Part p, int n, double a, double dt, ResourceFlowMode f) => RequestResource(p, Name(n), a, dt, f);
    public double StoreResource(Part p, int n, double a, double dt, ResourceFlowMode f) => StoreResource(p, Name(n), a, dt, f);
}
public class ResourceConverter
{
    private IResourceBroker _broker;
    public ResourceConverter(IResourceBroker broker) { _broker = broker; }
    public IResourceBroker Broker => _broker;
}
public class BaseConverter : PartModule
{
    protected IResourceBroker _resBroker;
    protected ResourceConverter _resConverter;
    public IResourceBroker ResBroker => _resBroker ?? (_resBroker = new ResourceBroker());
    public ResourceConverter ResConverter => _resConverter ?? (_resConverter = new ResourceConverter(ResBroker));
    public List<ResourceRatio> inputList = new List<ResourceRatio>(), outputList = new List<ResourceRatio>();
}
namespace Khemistry
{
    public static class KShared
    {
        public enum ChargablePartState { Off, On, Charging }
        public static bool IsFinite(double value) => !double.IsInfinity(value) && !double.IsNaN(value);
        public static void LogError(string message, string context) => throw new Exception(context + ": " + message);
    }
    public partial class KhemistryAdvancedStorage : PartModule
    {
        private List<string> _supportedResources = new List<string>();
        private bool _fatalConfigError = false;
        private const string SavedFlowStateNodeName = "KHEMISTRY_ORIGINAL_FLOW_STATE";
        public string storageType = "multiShared", activeResource = "A";
        public float maximumResources = 10, maxInputRate = -1, maxOutputRate = -1;
        public KShared.ChargablePartState state = KShared.ChargablePartState.On;
        public void Ready() { _storageReady = true; _supportedResources.AddRange(new[] { "A", "B" }); }
        public void Migrate() => MigrateLegacyResources();
    }
    public class KhemistryFluidCell : PartModule
    {
        public float ResourceMaxAmount = 10;
        public HashSet<string> SupportedResources = new HashSet<string>();
        public Dictionary<string, double> GetStoredResources() => new Dictionary<string, double>();
        public double GetStoredAmount(string name) => 0;
        public double GetStoredTotal() => 0;
        public bool CanAddResource(string name, IEnumerable<string> stored) => SupportedResources.Contains(name);
        public double RequestStoredResource(string name, double amount) => 0;
    }
}
