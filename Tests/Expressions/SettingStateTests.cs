using System;
using System.Collections.Generic;
using System.Linq;
using Khemistry;

public class ScreenMessage { public ScreenMessage(string text, float duration, ScreenMessageStyle style) { } }
public enum ScreenMessageStyle { UPPER_CENTER }
public static class ScreenMessages { public static void PostScreenMessage(ScreenMessage message) { } }
namespace Khemistry
{
    public partial class KShared
    {
        public static KShared Instance = new KShared();
        public List<KhemistryMaterial> materialList = new List<KhemistryMaterial>();
        public Dictionary<string,double> dialogValues;
        public Action<Dictionary<string,double>> done;
        public void ShowRecipeSettings(string title, IEnumerable<KhemistryISRURecipe.RecipeSetting> settings,
            IDictionary<string,double> values, Action<Dictionary<string,double>> callback)
        { dialogValues = new Dictionary<string,double>(values); done = callback; }
    }
    // Tests the real setting lifecycle, using a tiny host in place of Unity/PartModule.
    public partial class KhemistryISRU
    {
        private readonly Dictionary<string,Dictionary<string,double>> _recipeSettingValues =
            new Dictionary<string,Dictionary<string,double>>();
        public List<KhemistryISRURecipe> recipes = new List<KhemistryISRURecipe>();
        public KhemistryISRURecipe _activeRecipe;
        public bool isRunning;
        public string ConverterName = "Test";
        public int refunds, applies;
        private void RefundPassiveConsumption() => refunds++;
        private void ApplyRecipe(KhemistryISRURecipe recipe) { _activeRecipe = recipe; applies++; }
        private void UpdateUI() { }
        private void UpdateEventVisibility() { }
        public void Select(KhemistryISRURecipe recipe)
        { recipes.Add(recipe); _activeRecipe = recipe.WithSettingValues(EnsureRecipeSettingValues(recipe)); }
        public void Open() => ShowRecipeSettings(_activeRecipe);
        public void LoadSettings(ConfigNode node) => LoadRecipeSettingValues(node);
        public void SaveSettings(ConfigNode node) => SaveRecipeSettingValues(node);
        public Dictionary<string,double> Values() => GetRecipeSettingValuesSnapshot(_activeRecipe);
    }
}
internal static class SettingStateTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        ConfigNode Config(bool reversed = false)
        {
            var node = new ConfigNode("RECIPE"); node.AddValue("name", "Persistent"); node.AddValue("recipeTime", "10/(SETTING:number)");
            var setting = new ConfigNode("SETTING"); node.AddNode(setting);
            setting.AddValue("name", "Resource"); setting.AddValue("var", "resource");
            var options = new ConfigNode("OPTIONS"); setting.AddNode(options);
            options.AddValue("option", reversed ? "Sand" : "Water"); options.AddValue("option", reversed ? "Water" : "Sand");
            setting = new ConfigNode("SETTING"); node.AddNode(setting);
            setting.AddValue("name", "Number"); setting.AddValue("var", "number");
            setting.AddValue("default", 1);
            var output = new ConfigNode("OUTPUT_RESOURCE"); node.AddNode(output);
            output.AddValue("name", "(SETTING:resource)"); output.AddValue("amount", "(SETTING:number)");
            return node;
        }
        var converter = new KhemistryISRU(); converter.Select(new KhemistryISRURecipe(Config(), "Test"));
        converter.Open();
        KShared.Instance.dialogValues["resource"] = 1;
        Check(converter.Values()["resource"] == 0, "editing/Cancel does not mutate saved choice");
        KShared.Instance.done(KShared.Instance.dialogValues);
        Check(converter.Values()["resource"] == 1 && converter._activeRecipe._outputs[0].resourceName == "Sand", "Done applies choice to actual recipe");
        Check(converter.refunds == 1 && converter.applies == 1, "settings change resets/refunds batch through ApplyRecipe");
        var save = new ConfigNode(); converter.SaveSettings(save);
        var savedSetting = save.GetNode("RECIPE_SETTING_VALUES").GetNode("RECIPE").GetNodes("SETTING").Single(n => n.GetValue("var") == "resource");
        Check(savedSetting.GetValue("option") == "Sand", "option text persisted");
        var restored = new KhemistryISRU(); restored.LoadSettings(save); restored.Select(new KhemistryISRURecipe(Config(true), "Test"));
        Check(restored.Values()["resource"] == 0 && restored._activeRecipe._outputs[0].resourceName == "Sand", "option survives reordering on reload");
        restored.isRunning = true; restored.Open(); KShared.Instance.dialogValues["resource"] = 1; KShared.Instance.done(KShared.Instance.dialogValues);
        Check(restored.Values()["resource"] == 0, "running converter rejects changes");
        restored.isRunning = false; restored.Open(); KShared.Instance.dialogValues["number"] = 0; KShared.Instance.done(KShared.Instance.dialogValues);
        Check(restored.Values()["number"] == 1 && restored._activeRecipe.IsValid, "invalid expression keeps prior settings");
        // An inactive recipe must retain its saved option even if it has not been selected
        // (and therefore normalized to the new index) before the next save.
        restored = new KhemistryISRU(); restored.LoadSettings(save); restored.recipes.Add(new KhemistryISRURecipe(Config(true), "Test"));
        var inactiveSave = new ConfigNode(); restored.SaveSettings(inactiveSave);
        Check(inactiveSave.GetNode("RECIPE_SETTING_VALUES").GetNode("RECIPE").GetNodes("SETTING")
            .Single(n => n.GetValue("var") == "resource").GetValue("option") == "Sand", "inactive choice not changed by reordered config");
        return checks;
    }
}
