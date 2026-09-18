using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        private Dictionary<string, double> EnsureRecipeSettingValues(
            KhemistryISRURecipe recipe)
        {
            if (recipe == null)
                return new Dictionary<string, double>(StringComparer.Ordinal);
            if (!_recipeSettingValues.TryGetValue(recipe._name,
                    out Dictionary<string, double> values))
            {
                values = new Dictionary<string, double>(StringComparer.Ordinal);
                _recipeSettingValues[recipe._name] = values;
            }
            foreach (KhemistryISRURecipe.RecipeSetting setting in recipe._settings)
            {
                if (!values.TryGetValue(setting.variable, out double value))
                    value = setting.defaultValue;
                values[setting.variable] = setting.Clamp(value);
            }
            return values;
        }

        private Dictionary<string, double> GetRecipeSettingValuesSnapshot(
            KhemistryISRURecipe recipe)
        {
            Dictionary<string, double> source = EnsureRecipeSettingValues(recipe);
            var snapshot = new Dictionary<string, double>(StringComparer.Ordinal);
            if (recipe == null) return snapshot;
            foreach (KhemistryISRURecipe.RecipeSetting setting in recipe._settings)
                snapshot[setting.variable] = source[setting.variable];
            return snapshot;
        }

        private double GetActiveRecipeSettingValue(
            KhemistryISRURecipe.RecipeSetting setting)
        {
            Dictionary<string, double> values = EnsureRecipeSettingValues(_activeRecipe);
            return values.TryGetValue(setting.variable, out double value)
                ? setting.Clamp(value) : setting.defaultValue;
        }

        private bool ApplyRecipeSettingValues(KhemistryISRURecipe recipe,
            IDictionary<string, double> proposed)
        {
            if (recipe == null || recipe != _activeRecipe || isRunning) return false;
            Dictionary<string, double> values = EnsureRecipeSettingValues(recipe);
            bool changed = false;
            foreach (KhemistryISRURecipe.RecipeSetting setting in recipe._settings)
            {
                double next = setting.defaultValue;
                if (proposed != null && proposed.TryGetValue(setting.variable,
                        out double supplied))
                    next = supplied;
                next = setting.Clamp(next);
                if (!values.TryGetValue(setting.variable, out double previous)
                    || !previous.Equals(next))
                    changed = true;
                values[setting.variable] = next;
            }
            if (!changed) return true;

            // A stopped converter may retain partial timed/passive progress. A parameter change
            // begins a fresh batch so one batch can never mix two sets of values.
            RefundPassiveConsumption();
            ResetPassiveTimers();
            batchProgress = 0.0;
            resolvedRecipeTime = 0.0;
            return true;
        }

        private void ShowRecipeSettings(KhemistryISRURecipe recipe)
        {
            if (recipe == null || recipe._settings.Count == 0) return;
            KShared shared = KShared.Instance;
            if (shared == null) return;
            Dictionary<string, double> values = GetRecipeSettingValuesSnapshot(recipe);
            shared.ShowRecipeSettings(recipe._name + " Parameters", recipe._settings,
                values, proposed =>
                {
                    if (ApplyRecipeSettingValues(recipe, proposed))
                    {
                        UpdateUI();
                        UpdateEventVisibility();
                        return;
                    }
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Stop the converter before changing recipe parameters.", 5f,
                        ScreenMessageStyle.UPPER_CENTER));
                });
        }

        private void LoadRecipeSettingValues(ConfigNode node)
        {
            _recipeSettingValues.Clear();
            ConfigNode root = node?.GetNode("RECIPE_SETTING_VALUES");
            if (root == null) return;
            foreach (ConfigNode recipeNode in root.GetNodes("RECIPE"))
            {
                string recipeName = recipeNode.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(recipeName)) continue;
                if (!_recipeSettingValues.TryGetValue(recipeName,
                        out Dictionary<string, double> values))
                {
                    values = new Dictionary<string, double>(StringComparer.Ordinal);
                    _recipeSettingValues[recipeName] = values;
                }
                foreach (ConfigNode settingNode in recipeNode.GetNodes("SETTING"))
                {
                    string variable = settingNode.GetValue("var")?.Trim();
                    if (string.IsNullOrEmpty(variable)
                        || !double.TryParse(settingNode.GetValue("value"),
                            NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double value)
                        || double.IsNaN(value) || double.IsInfinity(value))
                        continue;
                    values[variable] = value;
                }
            }
        }

        private void SaveRecipeSettingValues(ConfigNode node)
        {
            if (node == null) return;
            while (node.HasNode("RECIPE_SETTING_VALUES"))
                node.RemoveNode("RECIPE_SETTING_VALUES");
            if (_recipeSettingValues.Count == 0) return;

            ConfigNode root = new ConfigNode("RECIPE_SETTING_VALUES");
            foreach (KeyValuePair<string, Dictionary<string, double>> recipe in
                     _recipeSettingValues.OrderBy(pair => pair.Key,
                         StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(recipe.Key) || recipe.Value == null
                    || recipe.Value.Count == 0) continue;
                ConfigNode recipeNode = new ConfigNode("RECIPE");
                recipeNode.AddValue("name", recipe.Key);
                foreach (KeyValuePair<string, double> setting in recipe.Value.OrderBy(
                             pair => pair.Key, StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(setting.Key) || double.IsNaN(setting.Value)
                        || double.IsInfinity(setting.Value)) continue;
                    ConfigNode settingNode = new ConfigNode("SETTING");
                    settingNode.AddValue("var", setting.Key);
                    settingNode.AddValue("value", setting.Value.ToString("R",
                        CultureInfo.InvariantCulture));
                    recipeNode.AddNode(settingNode);
                }
                if (recipeNode.nodes.Count > 0) root.AddNode(recipeNode);
            }
            if (root.nodes.Count > 0) node.AddNode(root);
        }
    }
}
