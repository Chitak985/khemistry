// Minimal KSP host for exercising the real recipe loader. Biome and maintenance
// behavior have separate suites; the expression/material recipe paths are production code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

public sealed class ConfigNode
{
    public string name;
    public sealed class Value { public string name, value; }
    public sealed class Values : List<Value>
    { public IEnumerable<string> DistinctNames() => this.Select(v => v.name).Distinct(); }
    public sealed class Nodes : List<ConfigNode>
    { public IEnumerable<string> DistinctNames() => this.Select(n => n.name).Distinct(); }
    public Values values = new Values();
    public Nodes nodes = new Nodes();
    public ConfigNode(string name = "") { this.name = name; }
    public string GetValue(string key) => values.FirstOrDefault(v => v.name == key)?.value;
    public string[] GetValues(string key) => values.Where(v => v.name == key).Select(v => v.value).ToArray();
    public bool HasValue(string key) => values.Any(v => v.name == key);
    public void AddValue(string key, object value) => values.Add(new Value
        { name = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) });
    public void RemoveValue(string key) => values.Remove(values.First(v => v.name == key));
    public ConfigNode GetNode(string key) => nodes.FirstOrDefault(n => n.name == key);
    public ConfigNode[] GetNodes(string key) => nodes.Where(n => n.name == key).ToArray();
    public bool HasNode(string key) => GetNode(key) != null;
    public void AddNode(ConfigNode node) => nodes.Add(node);
    public void RemoveNode(string key) => nodes.Remove(GetNode(key));
    public void CopyTo(ConfigNode target)
    {
        target.name = name;
        foreach (var value in values) target.AddValue(value.name, value.value);
        foreach (var node in nodes) { var copy = new ConfigNode(); node.CopyTo(copy); target.AddNode(copy); }
    }
    // The checked-in fixture uses line-separated braces; retain repeated values exactly.
    // ModuleManager NEEDS suffixes are stripped as if dependencies are installed.
    public static ConfigNode ReadFixture(string path)
    {
        var root = new ConfigNode();
        var stack = new Stack<ConfigNode>(); stack.Push(root);
        string pending = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0) continue;
            if (line == "{") { var next = new ConfigNode(pending.Split(':')[0]); stack.Peek().AddNode(next); stack.Push(next); pending = null; }
            else if (line == "}") stack.Pop();
            else if (line.Contains("="))
            { int split = line.IndexOf('='); stack.Peek().AddValue(line.Substring(0, split).Trim(), line.Substring(split + 1).Trim()); }
            else pending = line;
        }
        if (stack.Count != 1) throw new FormatException("Unbalanced fixture nodes.");
        return root;
    }
}
public enum ResourceFlowMode { STAGE_PRIORITY_FLOW }
public class PartResourceLibrary
{
    public static PartResourceLibrary Instance => null; // Resource availability is game-owned.
    public object GetDefinition(string name) => null;
}
namespace Khemistry
{
    public partial class KShared
    {
        public static readonly List<string> Errors = new List<string>();
        public static void LogError(string message, string context) => Errors.Add(message);
        public static void Log(string message, string context) => Errors.Add(message);
        public static void LogWarning(string message, string context) { }
        public static void LogNoValueInNode(string node, string key, string message, string context) => Errors.Add(message);
        public static string GetStrValueFromCFG(ConfigNode node, string key, string fallback) => node.GetValue(key) ?? fallback;
        public static double GetDoubleValueFromCFG(ConfigNode node, string key, double fallback) =>
            double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        public static float GetFloatValueFromCFG(ConfigNode node, string key, float fallback) => (float)GetDoubleValueFromCFG(node, key, fallback);
        public static int GetIntValueFromCFG(ConfigNode node, string key, int fallback) => (int)GetDoubleValueFromCFG(node, key, fallback);
        public static void ParseShowRule(string value, out bool paw, out bool eva, string context, string name) { paw = true; eva = true; }
    }
    public sealed class KhemistryMaterial
    {
        public string name;
        public List<string> shapes = new List<string>();
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        public List<string> parameterOrder => parameters.Keys.ToList();
        public bool IsDerivedParameter(string name) => parameters.TryGetValue(name, out var value) && value.StartsWith("DER");
    }
    public sealed class KhemistryISRUBiomeConfig
    {
        public bool IsValid => true;
        public string biomeName;
        public bool hasResourceConfigurationError => false;
        public List<KhemistryISRURecipe.ResourceOutput> outputs = new List<KhemistryISRURecipe.ResourceOutput>();
        public KhemistryISRUBiomeConfig(ConfigNode node, string context = null, string converter = null) { biomeName = node.GetValue("name"); }
    }
    public partial class KhemistryISRURecipe
    {
        public sealed class TestMaintenance { public List<TestStage> stages = new List<TestStage>(); }
        public sealed class TestStage
        { public Dictionary<string, double> resources = new Dictionary<string, double>(), tools = new Dictionary<string, double>(); }
        public List<TestMaintenance> maintenance = new List<TestMaintenance>();
        private bool LoadMaintenance(ConfigNode node) => !node.HasNode("MAINTENANCE");
        private bool TryParsePassiveInput(ConfigNode node, string context, out PassiveResourceInput input)
        { input = new PassiveResourceInput(); return false; }
    }
}
