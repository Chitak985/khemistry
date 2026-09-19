// Only the game host is mocked. Production maintenance parsing, timers, repairs,
// multiplier composition, and persistence are compiled directly into this harness.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khemistry;

public sealed class ConfigNode
{
    private readonly string name;
    private readonly List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
    private readonly List<ConfigNode> nodes = new List<ConfigNode>();
    public ConfigNode(string name = "") { this.name = name; }
    public void AddValue(string key, object value) => values.Add(new KeyValuePair<string, string>(key,
        Convert.ToString(value, CultureInfo.InvariantCulture)));
    public void SetValue(string key, object value) { values.RemoveAll(v => v.Key == key); AddValue(key, value); }
    public string GetValue(string key) => values.FirstOrDefault(v => v.Key == key).Value;
    public string[] GetValues(string key) => values.Where(v => v.Key == key).Select(v => v.Value).ToArray();
    public bool HasValue(string key) => values.Any(v => v.Key == key);
    public ConfigNode AddNode(string key) { var node = new ConfigNode(key); nodes.Add(node); return node; }
    public ConfigNode[] GetNodes(string key) => nodes.Where(n => n.name == key).ToArray();
    public bool HasNode(string key) => nodes.Any(n => n.name == key);
    public void RemoveNode(string key) => nodes.Remove(nodes.First(n => n.name == key));
}
public class KSPField : Attribute
{
    public bool isPersistant, guiActive;
    public string guiName, groupName;
}
public class KSPEvent : Attribute
{
    public bool guiActive, guiActiveEditor, guiActiveUnfocused;
    public float unfocusedRange;
    public string guiName, groupName;
}
public sealed class UIEntry
{
    public bool guiActive, guiActiveUnfocused;
    public float guiUnfocusedRange, unfocusedRange;
    public string guiName;
}
public sealed class UIEntries : Dictionary<string, UIEntry>
{
    public new UIEntry this[string key]
    {
        get { if (!ContainsKey(key)) base[key] = new UIEntry(); return base[key]; }
    }
}
public static class Planetarium { public static double now; public static double GetUniversalTime() => now; }
public static class ScreenMessages { public static void PostScreenMessage(string text, float t, ScreenMessageStyle style) { } }
public enum ScreenMessageStyle { UPPER_CENTER }
public enum ResourceFlowMode { STAGE_PRIORITY_FLOW }
public class ProtoCrewMember { public string trait; }
public sealed class Transform { public double position; }
public static class Vector3d { public static double Distance(double a, double b) => Math.Abs(a - b); }
public class Part
{
    public readonly List<ProtoCrewMember> protoModuleCrew = new List<ProtoCrewMember>();
    public readonly List<KhemistryResourceNetwork.Endpoint> resources = new List<KhemistryResourceNetwork.Endpoint>();
    public Transform transform = new Transform();
    public bool CanCrossfeed(Part p, int id, ResourceFlowMode flow) => true;
}
public class Vessel
{
    public bool isEVA, loaded = true;
    public Transform transform = new Transform();
    public List<Part> parts = new List<Part>();
    public List<ProtoCrewMember> GetVesselCrew() => parts.SelectMany(p => p.protoModuleCrew).ToList();
}
public static class FlightGlobals { public static readonly List<Vessel> Vessels = new List<Vessel>(); }
public sealed class PartResourceDefinition { public int id; }
public sealed class PartResourceLibrary
{
    public static PartResourceLibrary Instance = new PartResourceLibrary();
    public PartResourceDefinition GetDefinition(string name) => new PartResourceDefinition();
}
namespace UnityEngine { public static class Random { public static float value => 0; } }
namespace Khemistry
{
    public static class KShared
    {
        public enum SituationCondition { Landed, Splashed }
        public static void LogError(string message, string context) { }
        public static string GetStrValueFromCFG(ConfigNode node, string key, string fallback) => node.GetValue(key) ?? fallback;
        public static double GetDoubleValueFromCFG(ConfigNode node, string key, double fallback)
            => double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        public static double GetDoubleTemperatureValueFromCFG(ConfigNode node, string key, double fallback)
            => GetDoubleValueFromCFG(node, key, fallback);
    }
    public static class KhemistryResourceNetwork
    {
        public sealed class Endpoint { public string resourceName; public double amount; public object cell; }
        public static IEnumerable<Endpoint> GetEndpoints(Part part) => part.resources;
    }
    public sealed class KhemistryKerbal
    {
        public double GetProcessorResourceAmount(object stored, string name, bool suit) => 0;
    }
    public partial class KhemistryISRURecipe
    {
        private string _name = "Test recipe";
        public struct ResourceInput { }
        public struct ResourceOutput { }
        public static bool TryParseResourceInput(ConfigNode node, string context, out ResourceInput input) { input = default; return true; }
        public static bool TryParseResourceOutput(ConfigNode node, string context, out ResourceOutput output) { output = default; return true; }
        public bool ParseMaintenance(ConfigNode node) => LoadMaintenance(node);
        public KhemistryISRUBiomeConfig biome = new KhemistryISRUBiomeConfig(new ConfigNode());
        public KhemistryISRUBiomeConfig GetBiomeConfig(string planet, string biomeName) => biome;
    }
    public partial class KhemistryISRU
    {
        public KhemistryISRURecipe _activeRecipe;
        public sealed class Runtime { public string planet, biome; }
        private Runtime _runtimeData = new Runtime();
        private UIEntries Fields = new UIEntries(), Events = new UIEntries();
        private bool _controlsShowPAW = true, _controlsShowEVA = true, useSuitCell = false;
        private float _maxDisplayDistance = 10, _maxInteractionDistance = 7;
        private string moduleType = "normal";
        private KhemistryKerbal _kerbalHost = null;
        private object _inventoryStoredPart = null;
        public Part part = new Part();
        public Vessel vessel;
        public KhemistryISRU() { vessel = new Vessel(); vessel.parts.Add(part); }
        private Part GetPowerfailContextPart() => part;
        private Vessel GetProcessingVessel() => vessel;
        private bool IsEVAModuleType() => false;
        private void ClearParallaxTargetCache() { }
        private static void ApplyShowRule(UIEntry entry, bool paw, bool eva) { entry.guiActive = paw; entry.guiActiveUnfocused = eva; }
        private bool ConsumeVesselResources(IList<string> names, IList<double> amounts, double dt)
        {
            var all = vessel.parts.SelectMany(KhemistryResourceNetwork.GetEndpoints).ToList();
            for (int i = 0; i < names.Count; i++)
                if (all.Where(r => r.resourceName == names[i]).Sum(r => r.amount) < amounts[i]) return false;
            for (int i = 0; i < names.Count; i++)
            {
                double remaining = amounts[i];
                foreach (var r in all.Where(r => r.resourceName == names[i]))
                { double take = Math.Min(remaining, r.amount); r.amount -= take; remaining -= take; }
            }
            return true;
        }
        public MaintenanceState State(MaintenanceDefinition definition) => MaintenanceFor(definition);
        public void Tick(double dt, double runtime = 0) { Planetarium.now += dt; _maintenanceRuntime = runtime; TickMaintenance(dt); }
        public void SaveState(ConfigNode node) { node.AddValue("autoMaintenanceFixing", autoMaintenanceFixing); SaveMaintenanceState(node); }
        public void LoadState(ConfigNode node) => LoadMaintenanceState(node);
        public KhemistryISRUBiomeConfig Effective() => GetEffectiveBiomeConfig();
    }
}
