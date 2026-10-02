using System;
using System.IO;
using System.Linq;
using KhemistryConstructionOverhaul;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    static KhemistryPart Instance(ConfigNode config)
    {
        var part = new Part { partInfo = new PartInfo { partConfig = config } };
        var module = new KhemistryPart { part = part };
        part.Modules.Add(module);
        return module;
    }
    static int Main()
    {
        try
        {
            var config = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "WoodenBasin.cfg")).GetNode("PART");
            var prefab = Instance(config);
            var moduleConfig = config.GetNodes("MODULE").Single(n => n.GetValue("name") == "KhemistryPart");
            prefab.OnLoad(moduleConfig);
            Check(prefab.CostConfigurationValid, "actual WoodenBasin costs parse");
            Check(prefab.MaterialCosts.Count == 1 && prefab.MaterialCosts[0].shape == "HalfHollowLog", "material cost retained");
            Check(prefab.MaterialCosts[0].parameters.Count() == 4, "duplicate height/radius bounds retained");
            var clone = Instance(config);
            clone.part.partInfo.partPrefab = prefab.part;
            Check(!clone.CostConfigurationValid, "editor clone starts without loaded costs");
            Check(clone.EnsureCostConfiguration(out _), "editor clone rehydrates before launch");
            Check(clone.MaterialCosts.Count == 1, "rehydration must not allow free construction");
            Check(Instance(config).BuyCheck()[1] == "1", "BuyCheck also reloads costs");
            var nullCollections = Instance(config);
            nullCollections.ResourceDict = null;
            nullCollections.MaterialCosts = null;
            Check(nullCollections.EnsureCostConfiguration(out _), "missing clone collections restored");
            var sharedCollections = Instance(config);
            sharedCollections.ResourceDict = prefab.ResourceDict;
            sharedCollections.MaterialCosts = prefab.MaterialCosts;
            Check(sharedCollections.EnsureCostConfiguration(out _), "shared collections restored");
            Check(prefab.MaterialCosts.Count == 1, "loading clone does not clear prefab");
            var missing = Instance(null);
            Check(!missing.EnsureCostConfiguration(out var error) && error.Contains("Wooden Basin") && error.Contains("WoodenBasin"), "failure identifies offending part");
            var invalid = new ConfigNode();
            config.CopyTo(invalid);
            var cost = invalid.GetNodes("MODULE").Single(n => n.GetValue("name") == "KhemistryPart").GetNode("MATERIAL_COST");
            cost.RemoveValue("amount"); cost.AddValue("amount", 0);
            var bad = Instance(invalid);
            bad.part.partInfo.partPrefab = prefab.part;
            Check(!bad.EnsureCostConfiguration(out _), "invalid explicit costs are not bypassed by valid prefab");
            Console.WriteLine(checks + " assertions passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
