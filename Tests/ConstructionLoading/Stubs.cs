using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
// Minimal host for the production construction-cost loader; no KSP runtime needed.
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

public class PartModule { public Part part; public virtual void OnLoad(ConfigNode node) { } }
public class Part { public string name = "WoodenBasin"; public PartInfo partInfo; public List<PartModule> Modules = new List<PartModule>(); }
public class PartInfo { public string name = "WoodenBasin", title = "Wooden Basin"; public ConfigNode partConfig; public Part partPrefab; }
namespace Khemistry
{
    public static class KShared
    {
        public static void Log(string message, string context) { }
        public static void LogError(string message, string context) { }
        public static ConfigNode FindModuleConfigNode(PartModule module, string name)
        {
            int index = module.part.Modules.OfType<KhemistryConstructionOverhaul.KhemistryPart>().ToList().IndexOf((KhemistryConstructionOverhaul.KhemistryPart)module);
            return module.part.partInfo?.partConfig?.GetNodes("MODULE").Where(n => n.GetValue("name") == name).ElementAtOrDefault(index);
        }
    }
    public class KhemistryISRURecipe
    {
        public struct ResourceInputMaterial
        {
            public string id, name, shape, size;
            public bool usesParams;
            public int amount;
            public IEnumerable<KeyValuePair<string, string>> parameters;
        }
    }
}
namespace KhemistryConstructionOverhaul
{
    public static class KhemistryResourceCheckManager
    {
        public static bool TryCheckCosts(Dictionary<string, double> resources, List<Khemistry.KhemistryISRURecipe.ResourceInputMaterial> materials, out string error)
        { error = null; return true; }
        public static bool TryCommitCosts(Dictionary<string, double> resources, List<Khemistry.KhemistryISRURecipe.ResourceInputMaterial> materials, out string error)
        { error = null; return true; }
    }
}
