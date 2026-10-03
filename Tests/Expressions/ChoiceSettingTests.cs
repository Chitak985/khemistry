using System;
using System.Collections.Generic;
using System.Linq;
using Khemistry;

internal static class ChoiceSettingTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message + ": " + string.Join("; ", KShared.Errors)); }
        ConfigNode Node(string field, string value, string kind = "OUTPUT_RESOURCE")
        {
            var root = new ConfigNode("RECIPE"); root.AddValue("name", "Choices"); root.AddValue("recipeTime", "2*(SETTING:count)");
            var setting = new ConfigNode("SETTING"); root.AddNode(setting);
            setting.AddValue("name", "Resource"); setting.AddValue("var", "resource");
            var options = new ConfigNode("OPTIONS"); setting.AddNode(options);
            options.AddValue("option", "Water"); options.AddValue("option", "Sand");
            setting = new ConfigNode("SETTING"); root.AddNode(setting);
            setting.AddValue("name", "Count"); setting.AddValue("var", "count");
            setting.AddValue("min", 1); setting.AddValue("max", 250); setting.AddValue("default", 2);
            var output = new ConfigNode(kind); root.AddNode(output);
            output.AddValue("name", field == "name" ? value : "Water");
            output.AddValue("amount", field == "amount" ? value : "2*(SETTING:count)");
            if (field != "name" && field != "amount") output.AddValue(field, value);
            return root;
        }
        var node = Node("name", "Prefix(SETTING:resource)Suffix");
        var recipe = new KhemistryISRURecipe(node, "Test");
        Check(recipe.IsValid, "choice recipe valid");
        Check(recipe._outputs[0].resourceName == "PrefixWaterSuffix", "default option inserted in string");
        Check(recipe._outputs[0].amount == 4 && recipe._recipeTime == 4, "bare numeric expression");
        Check(recipe._settings[0].IsChoice && recipe._settings[0].SelectedOption(99) == "Water", "bad choice resets to first");
        Check(recipe._settings[1].max == 250, "configured max honored");
        var selected = recipe.ScaledCopy(3).WithSettingValues(new Dictionary<string,double> { ["resource"] = 1, ["count"] = 5 });
        Check(selected.IsValid && selected._outputs[0].resourceName == "PrefixSandSuffix", "selected option updates parsed fields");
        Check(selected._outputs[0].amount == 30 && selected._recipeTime == 10, "rebuilding retains scale without scaling time");
        Check(recipe._outputs[0].resourceName == "PrefixWaterSuffix" && node.GetNode("OUTPUT_RESOURCE").GetValue("name").Contains("(SETTING:"), "template and shared recipe not mutated");
        selected = selected.WithSettingValues(new Dictionary<string,double> { ["resource"] = 0, ["count"] = 2 });
        Check(selected._outputs[0].amount == 12, "repeated rebuild does not compound scale");
        foreach (string field in new[] { "min", "max", "mul1", "mul2", "default", "step" })
            node.GetNodes("SETTING")[0].AddValue(field, "invalid");
        int errors = KShared.Errors.Count;
        Check(new KhemistryISRURecipe(node, "Test").IsValid && KShared.Errors.Count >= errors + 6, "numeric choice fields log errors and are ignored");
        foreach (var bad in new[] { Node("amount", "(SETTING:resource)"), Node("name", "Prefix(SETTING:count)Count"),
            Node("name", "(SETTING:count)"), Node("amount", "(SETTING:missing)") })
            Check(!new KhemistryISRURecipe(bad, "Test").IsValid, "type/unknown reference rejected");
        var numericText = Node("amount", "(SETTING:resource)");
        numericText.GetNodes("SETTING")[0].GetNode("OPTIONS").values[0].value = "123";
        Check(!new KhemistryISRURecipe(numericText, "Test").IsValid, "numeric-looking option remains a string");
        foreach (string key in new[] { "recipeType", "recipeSubtype", "recipeSubsubtype" })
        {
            var forbidden = Node("name", "(SETTING:resource)"); forbidden.AddValue(key, "(SETTING:resource)");
            Check(!new KhemistryISRURecipe(forbidden, "Test").IsValid, "recipe selection cannot be dynamic");
        }
        var module = new ConfigNode("MODULE"); module.AddValue("ConverterName", "(SETTING:resource)");
        Check(!KhemistryISRURecipe.ValidateModuleSettingScope(module), "module naming cannot be dynamic");
        module = new ConfigNode("MODULE"); var names = new ConfigNode("RECIPE_NAMES"); module.AddNode(names); names.AddValue("name", "(SETTING:resource)");
        Check(!KhemistryISRURecipe.ValidateModuleSettingScope(module), "recipe lists cannot be dynamic");
        var empty = Node("name", "(SETTING:resource)"); empty.GetNodes("SETTING")[0].GetNode("OPTIONS").values.Clear();
        Check(!new KhemistryISRURecipe(empty, "Test").IsValid, "empty options rejected");
        var material = Node("name", "(SETTING:resource)", "OUTPUT_MATERIAL");
        var outputMat = material.GetNode("OUTPUT_MATERIAL");
        outputMat.AddValue("shape", "Fluid"); outputMat.AddValue("size", "none");
        outputMat.AddValue("outVolume", "(SETTING:count)*2");
        var parameters = new ConfigNode("PARAMS"); outputMat.AddNode(parameters);
        parameters.AddValue("type", "Raw(SETTING:resource)");
        parameters.AddValue("length", "(SETTING:count)*3");
        var matRecipe = new KhemistryISRURecipe(material, "Test");
        Check(matRecipe.IsValid && matRecipe._outputMaterials[0].name == "Water", "material string fields and bare expressions resolve");
        Check(matRecipe._outputMaterials[0].parameters["type"] == "RawWater", "text parameter assignment");
        Check(matRecipe._outputMaterials[0].parameters["length"].Contains("3"), "numeric parameter expression retained");
        parameters.values[0].value = "2*(SETTING:resource)";
        Check(!new KhemistryISRURecipe(material, "Test").IsValid, "text option cannot enter parameter math");
        return count;
    }
}
