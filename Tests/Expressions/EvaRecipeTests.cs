using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Khemistry;

internal static class EvaRecipeTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string error) { count++; if (!ok) throw new Exception(error); }
        var materials = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "materials.cfg"))
            .GetNodes("KHEMISTRY_MATERIAL").Select(n => new KhemistryMaterial
            {
                name = n.GetValue("name"), shapes = n.GetNode("SHAPES").GetValues("name").ToList(),
                parameters = n.GetNode("PARAMS").values.ToDictionary(v => v.name, v => v.value)
            }).ToList();
        var nodes = ConfigNode.ReadFixture(Path.Combine(AppContext.BaseDirectory, "kerbalEva.cfg"))
            .GetNodes("KHEMISTRYISRU_RECIPE");
        foreach (var node in nodes)
        {
            KShared.Errors.Clear();
            var recipe = new KhemistryISRURecipe(node, "Kerbal");
            Check(recipe.IsValid, recipe._name + ": " + string.Join("; ", KShared.Errors));
            Check(recipe.ValidateReferences(materials, "test"), recipe._name + ": " + string.Join("; ", KShared.Errors));
            foreach (var input in recipe._inputMaterials)
            {
                var raw = node.GetNodes("INPUT_MATERIAL").First(n => n.GetValue("id") == input.id);
                Check(input.parameters.Count() == (raw.GetNode("PARAMS")?.values.Count ?? 0), "All duplicate conditions retained");
            }
            var copy = recipe.ScaledCopy(2);
            Check(copy._inputMaterials.SelectMany(m => m.parameters).SequenceEqual(recipe._inputMaterials.SelectMany(m => m.parameters)),
                "Scaled copies must preserve repeated conditions");
            var overridden = new KhemistryISRURecipe(KhemistryISRURecipe.ApplyModuleOverrides(new ConfigNode("MODULE"), node), "Kerbal");
            Check(overridden.IsValid && overridden._inputMaterials.SelectMany(m => m.parameters)
                .SequenceEqual(recipe._inputMaterials.SelectMany(m => m.parameters)), "Module copies must preserve all conditions");
            if (recipe._settings.Count == 0) continue;
            var inputMaterial = recipe._inputMaterials.Single();
            Check(inputMaterial.parameters.Count() == 4, "Expected both bounds for radius and height");
            var settings = new Dictionary<string, double> { ["radius"] = 0.6, ["height"] = 35 };
            bool Matches(double radius, double height)
            {
                var values = new Dictionary<string, string>
                { ["radius"] = radius.ToString("R", CultureInfo.InvariantCulture), ["height"] = height.ToString("R", CultureInfo.InvariantCulture) };
                return inputMaterial.parameters.All(condition =>
                    MaterialParameterCondition.TryResolve(condition.Value, v => settings.TryGetValue(v, out var value) ? value : (double?)null,
                        out var resolved, out _) && KShared.EvaluateParamComparison(values[condition.Key], resolved));
            }
            Check(Matches(0.6, 35), "Selected material should match");
            Check(!Matches(0.49, 35) && !Matches(0.71, 35), "Both radius bounds must apply");
            Check(!Matches(0.6, 34.89) && !Matches(0.6, 35.11), "Both height bounds must apply");
            settings["radius"] = 0.8; settings["height"] = 60;
            Check(!Matches(0.6, 35) && Matches(0.8, 60), "Changing settings changes eligible material");
            double Duration(double radius, double height)
            {
                string expression = recipe._recipeTimeExpression
                    .Replace("(INMAT:main:radius)", radius.ToString("R", CultureInfo.InvariantCulture))
                    .Replace("(INMAT:main:height)", height.ToString("R", CultureInfo.InvariantCulture));
                Check(KMathExpr.TryEvaluateNumericExpression(expression, out var time, out var error), error);
                return time;
            }
            double small = Duration(0.5, 30), tall = Duration(0.5, 60), wide = Duration(0.8, 30);
            Check(Math.Abs(tall / small - 2) < 1e-10, "Height doubles duration");
            double expectedRadiusScale = recipe._name.StartsWith("Hollow") ? 2.56 : 1.6;
            Check(Math.Abs(wide / small - expectedRadiusScale) < 1e-10, "Actual input radius scales duration");

            // A bad repeated entry must not be silently discarded during recipe validation.
            var invalid = new ConfigNode(); node.CopyTo(invalid);
            invalid.GetNode("INPUT_MATERIAL").GetNode("PARAMS").AddValue("radius", "EMunknownVariable");
            Check(!new KhemistryISRURecipe(invalid, "Kerbal").IsValid, "Invalid repeated condition must reject recipe");
        }
        Check(KMathExpr.TryEvaluateNumericExpression("[2*3]", out double bracketed, out _) && bracketed == 6,
            "Bracketed recipeTime remains supported");
        Check(KMathExpr.TryEvaluateNumericExpression("2*3", out double bare, out _) && bare == 6,
            "Bare recipeTime is supported");
        Check(!KMathExpr.TryEvaluateNumericExpression("radius*3", out _, out _), "Bare material variables not allowed in recipeTime");
        Check(!KMathExpr.TryEvaluateNumericExpression("1/0", out _, out _), "Invalid recipeTime rejected");
        Console.WriteLine("Loaded and validated " + nodes.Length + " unchanged EVA recipes (simulated KSP host).");
        return count;
    }
}
