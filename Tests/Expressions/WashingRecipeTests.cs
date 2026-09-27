using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Khemistry;

internal static class WashingRecipeTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string error) { count++; if (!ok) throw new Exception(error); }
        var node = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "washing.cfg"))
            .GetNodes("KHEMISTRYISRU_RECIPE").Single();
        KShared.Errors.Clear();
        var recipe = new KhemistryISRURecipe(node, "Washing Basin");
        Check(recipe.IsValid, string.Join("; ", KShared.Errors));
        var waterNode = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "materials.cfg"))
            .GetNodes("KHEMISTRY_MATERIAL").Single(n => n.GetValue("name") == "Water");
        var definition = new KhemistryMaterial
        {
            name = "Water", shapes = waterNode.GetNode("SHAPES").GetValues("name").ToList(),
            parameters = waterNode.GetNode("PARAMS").values.ToDictionary(v => v.name, v => v.value)
        };
        Check(recipe.ValidateReferences(new[] { definition }, "test"), string.Join("; ", KShared.Errors));
        var input = recipe._inputMaterials.Single();
        var output = recipe._outputMaterials.Single();
        Check(input.amount == 50000 && output.amount == 50000, "Expected 50,000 cubic-centimeter units");
        Check(KMathExpr.TryInterpolateNumber(output.outVolume, out double unitVolume, out _), "Per-unit volume parses");
        Check(Math.Abs(output.amount * unitVolume - 0.05) < 1e-12, "Output must total 50 liters");
        var water = definition.parameters.Where(p => !p.Value.StartsWith("DER")).ToDictionary(p => p.Key, p => p.Value);
        var variables = new Dictionary<string, string> { ["amount"] = "50000", ["volume"] = "0.000001" };
        Check(KMathExpr.TryInterpolateNumber(definition.parameters["m3"].Substring(3), out double m3, out _, variables), "Water m3 derives");
        water["m3"] = m3.ToString("R", CultureInfo.InvariantCulture);
        foreach (var condition in input.parameters)
            Check(MaterialParameterCondition.TryResolve(condition.Value, null, out var resolved, out _)
                && KShared.EvaluateParamComparison(water[condition.Key], resolved), "Clean 50-liter batch passes " + condition.Key);
        var produced = new Dictionary<string, string>();
        foreach (var assignment in output.parameterAssignments)
        {
            string expression = Regex.Replace(assignment.Value, @"\(INMAT:water:([^)]*)\)", m => water[m.Groups[1].Value]);
            Check(KMathExpr.TryInterpolate(expression, out string value, out string error, produced), assignment.Key + ": " + error);
            produced[assignment.Key] = value;
        }
        Check(double.Parse(produced["tts"], CultureInfo.InvariantCulture) == 19000, "950g/0.05m3 must yield 19,000 mg/L");
        return count;
    }
}
