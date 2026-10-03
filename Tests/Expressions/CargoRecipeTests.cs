using System;
using Khemistry;

internal static class CargoRecipeTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
        ConfigNode Recipe(string kind, string expression)
        {
            var node = new ConfigNode("RECIPE");
            node.AddValue("name", "Cargo"); node.AddValue("recipeTime", "2");
            var setting = new ConfigNode("SETTING"); node.AddNode(setting);
            setting.AddValue("name", "Quantity"); setting.AddValue("var", "quantity");
            setting.AddValue("min", "1"); setting.AddValue("max", "10");
            var cargo = new ConfigNode(kind); node.AddNode(cargo);
            cargo.AddValue("name", "evaRepairKit"); cargo.AddValue("amount", expression);
            return node;
        }
        foreach (string kind in new[] { "INPUT_CARGOPART", "OUTPUT_CARGOPART", "OUPUT_CARGOPART" })
        {
            var node = Recipe(kind, "2*(SETTING:quantity)");
            var recipe = new KhemistryISRURecipe(node, "Test");
            Check(recipe.IsValid, kind + " only recipe loads: " + string.Join("; ", KShared.Errors));
            var entries = kind == "INPUT_CARGOPART" ? recipe._inputCargoParts : recipe._outputCargoParts;
            Check(entries.Count == 1 && entries[0].name == "evaRepairKit", "name retained");
            var copy = recipe.ScaledCopy(3);
            Check((kind == "INPUT_CARGOPART" ? copy._inputCargoParts : copy._outputCargoParts)[0].scale == 3, "scaled cargo");
            node.GetNode(kind).AddValue("id", "notSupported");
            Check(!new KhemistryISRURecipe(node, "Test").IsValid, "cargo IDs rejected");
        }
        foreach (string bad in new[] { "[2+2]", "unknown+1", "(SETTING:missing)", "(INMAT:missing:amount)", "2+" })
            Check(!new KhemistryISRURecipe(Recipe("OUTPUT_CARGOPART", bad), "Test").IsValid, "invalid expression: " + bad);
        var references = Recipe("OUTPUT_CARGOPART", "(INMAT:source:amount)+(OUTMAT:result:amount)");
        var input = new ConfigNode("INPUT_MATERIAL"); references.AddNode(input);
        input.AddValue("name", "Wood"); input.AddValue("id", "source");
        input.AddValue("shape", "Log"); input.AddValue("size", "none"); input.AddValue("amount", "1");
        var output = new ConfigNode("OUTPUT_MATERIAL"); references.AddNode(output);
        output.AddValue("name", "Wood"); output.AddValue("id", "result");
        output.AddValue("shape", "Log"); output.AddValue("size", "none");
        output.AddValue("amount", "1"); output.AddValue("outVolume", "1");
        Check(new KhemistryISRURecipe(references, "Test").IsValid, "cargo accepts declared INMAT/OUTMAT references");
        return count;
    }
}
