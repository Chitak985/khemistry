using System;
using System.Collections.Generic;
using System.Linq;
using Data = KhemistryConstructionOverhaul.KSCResourceListData;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static Data.MaterialRow Material(string name, int amount, string shape, string size,
        params string[] parameters)
    {
        var row = new Data.MaterialRow
        {
            name = name, amount = amount, shape = shape, size = size,
            parameters = new Dictionary<string, string>()
        };
        for (int i = 0; i < parameters.Length; i += 2)
            row.parameters.Add(parameters[i], parameters[i + 1]);
        return row;
    }
    static void Main()
    {
        try { Run(); Console.WriteLine(checks + " assertions passed."); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        var rows = new List<Data.MaterialRow>
        {
            Material("Wood", 3, "Log", "5m", "grade", "A", "radius", "2"),
            Material("Wood", 4, "Log", "10m", "radius", "2", "grade", "A"),
            Material("Wood", 2, "Plank", "5m", "grade", "A", "radius", "2"),
            Material("Wood", 9, "Log", "5m", "grade", "B", "radius", "2"),
            Material("Iron", 2, "Rod", "1m"),
        };
        var names = Data.Groups(rows, r => r.name, r => r.name);
        Check(names.Count == 2, "group materials by name");
        var wood = names.Single(g => g.label == "Wood");
        Check(wood.amount == 18, "material total includes all variants");
        var parameters = Data.Groups(wood.rows, Data.ParameterKey, r => "");
        Check(parameters.Count == 2, "group by complete parameter combinations");
        var gradeA = parameters.Single(g => g.rows[0].parameters["grade"] == "A");
        Check(gradeA.amount == 9, "parameter key ignores dictionary insertion order");
        var shapes = Data.Groups(gradeA.rows, r => r.shape, r => r.shape);
        Check(shapes.Count == 2, "shape grouping");
        Check(shapes.Single(g => g.label == "Log").amount == 7, "shape totals");
        var sizes = Data.Groups(shapes.Single(g => g.label == "Log").rows, r => r.size, r => r.size);
        Check(sizes.Count == 2 && sizes.Sum(g => g.amount) == 7, "size grouping");
        Check(Data.ParameterNames(rows).SequenceEqual(new[] { "grade", "radius" }), "parameter headers sorted");
        Check(rows[0].amount == 3 && rows[0].parameters["radius"] == "2", "grouping does not mutate ledger rows");
        Check(Data.ParameterKey(Material("X", 1, "", "", "a", "b:c")) !=
            Data.ParameterKey(Material("X", 1, "", "", "a:b", "c")), "parameter keys avoid separator collisions");
        Check(Data.ParameterKey(Material("X", 1, "", "", "a", "")) !=
            Data.ParameterKey(Material("X", 1, "", "")), "missing and empty parameters remain distinct");
        var huge = Data.Groups(new[] { Material("X", int.MaxValue, "", ""), Material("X", int.MaxValue, "", "") },
            r => r.name, r => r.name);
        Check(huge[0].amount == 2L * int.MaxValue, "totals exceed Int32 without overflow");

        var sort = new Data.Sort("Total Amount");
        string[] values = { "2", "10", "z", "a" };
        Check(Data.Sorted(values, sort, s => s, s => s).SequenceEqual(new[] { "a", "z", "10", "2" }),
            "text ordering, letters before digits, lexical numbers");
        sort.Select("Total Amount");
        Check(sort.descending, "same header toggles descending");
        Check(Data.Sorted(values, sort, s => s, s => s).SequenceEqual(new[] { "2", "10", "z", "a" }),
            "descending text ordering");
        sort.Select("Total Amount");
        Check(!sort.descending, "same header toggles back ascending");
        sort.Select("Name");
        Check(sort.column == "Name" && !sort.descending, "new header starts ascending");
        Check(Data.Sorted(names, new Data.Sort("Total Amount"), g => g.amount.ToString(), g => g.key)[0] == wood,
            "material tables default to textual Total Amount order");
        Check(Data.AlphaComparer.Instance.Compare("A", "a") == 0, "case insensitive primary order");
        Check(Data.Sorted(new[] { "a", "A" }, sort, s => s, s => s).SequenceEqual(new[] { "A", "a" }),
            "deterministic tie breaking");

        Check(Data.Density(.005, "ton") == .005, "tons are definition density");
        Check(Data.Density(.005, "kg") == 5, "kg multiplier");
        Check(Data.Density(.005, "g") == 5000, "g multiplier");
        var ledger = new Dictionary<string, double> { ["Ore"] = 10 };
        Check(Data.TrySale(ledger, "Ore", "2.5", true, 4, out double left, out double funds, out _)
            && left == 7.5 && funds == 10, "career sale price and balance");
        Check(ledger["Ore"] == 10, "validation does not spend resources");
        Check(Data.TrySale(ledger, "Ore", "10", false, 4, out left, out funds, out _)
            && left == 0 && funds == 0, "science/sandbox disposal no payment");
        foreach (string amount in new[] { "0", "-1", "NaN", "Infinity", "1e999", "11", "oops", "" })
            Check(!Data.TrySale(ledger, "Ore", amount, true, 4, out _, out _, out _), "reject invalid amount " + amount);
        Check(!Data.TrySale(ledger, "Missing", "1", true, 4, out _, out _, out _), "missing resource rejected");
        Check(!Data.TrySale(ledger, null, "1", true, 4, out _, out _, out _), "closed popup null resource safe");
        Check(!Data.TrySale(ledger, "Ore", "2", true, double.MaxValue, out _, out _, out _), "fund overflow rejected");
        Check(!Data.TrySale(ledger, "Ore", "2", true, -1, out _, out _, out _), "negative sale price rejected");
        Check(Data.TrySale(ledger, "Ore", "2", false, double.NaN, out _, out funds, out _) && funds == 0,
            "noncareer disposal does not depend on resource price");
        ledger["Ore"] = 1;
        Check(!Data.TrySale(ledger, "Ore", "2.5", true, 4, out _, out _, out _), "live balance change revalidated");
        ledger["Ore"] = double.NaN;
        Check(!Data.TrySale(ledger, "Ore", "1", true, 4, out _, out _, out _), "invalid saved balance rejected");
        ledger["Ore"] = 1e30;
        Check(!Data.TrySale(ledger, "Ore", "1", true, 4, out _, out _, out _), "unrepresentable debit cannot mint funds");
    }
}
