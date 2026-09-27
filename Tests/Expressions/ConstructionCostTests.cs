using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Khemistry;
using KhemistryConstructionOverhaul;

internal static class ConstructionCostTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string error) { count++; if (!ok) throw new Exception(error); }
        var part = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "WoodenWashingBasin.cfg")).GetNode("PART");
        var node = part.GetNodes("MODULE").Single(n => n.GetValue("name") == "KhemistryPart").GetNode("MATERIAL_COST");
        Check(KhemistryConstructionMaterials.TryParseCost(node, "WoodenWashingBasin", out var cost), "Basin cost parses");
        Check(cost.name == "Wood" && cost.shape == "HalfHollowLog" && cost.size == "none" && cost.amount == 2, "Basin cost identity");
        Check(cost.parameters.Count() == 4, "Preserve all four bounds");
        Check(cost.parameters.Count(p => p.Key == "height") == 2 && cost.parameters.Count(p => p.Key == "radius") == 2, "Both bounds retained per parameter");
        var copy = KhemistryConstructionMaterials.CopyRequirement(cost);
        Check(copy.parameters.SequenceEqual(cost.parameters), "Copies retain all bounds");
        bool Matches(double height, double radius) => copy.parameters.All(p => KShared.EvaluateParamComparison(
            (p.Key == "height" ? height : radius).ToString("R", CultureInfo.InvariantCulture), p.Value));
        Check(Matches(5, 0.9), "Matching logs accepted");
        Check(Matches(4.9, 0.8) && Matches(5.1, 1.0), "Inclusive boundaries accepted");
        Check(!Matches(4.89, 0.9), "Short logs rejected");
        Check(!Matches(5.11, 0.9), "Tall logs rejected");
        Check(!Matches(5, 0.79), "Thin logs rejected");
        Check(!Matches(5, 1.01), "Wide logs rejected");
        return count;
    }
}
