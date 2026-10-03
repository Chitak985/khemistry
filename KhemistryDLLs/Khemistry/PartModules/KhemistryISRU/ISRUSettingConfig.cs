using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Khemistry
{
    public partial class KhemistryISRURecipe
    {
        internal double _settingRecipeScale = 1;
        private static readonly HashSet<string> NumericSettingFields = new HashSet<string>(
            ("amount recipeTime outVolume time fixTime order chance chargeRate chargeDecayRate chargeDecay "
            + "chargeDecayRateInactive chargeDecayRateActive chargeThreshold period peirod "
            + "workersEngineers workersPilots workersScientists fixersEngineers fixersPilots fixersScientists "
            + "fixersCrewEngineers fixersCrewPilots fixersCrewScientists powerfailExplosionRadius "
            + "powerfailExplosionTemperature").Split(' '), StringComparer.Ordinal);

        private bool SettingError(string message)
        {
            KShared.LogError("Recipe \"" + _name + "\": " + message, "KhemistryISRU/SETTING");
            return false;
        }

        private bool LoadSettingDefinitions(ConfigNode node)
        {
            if (node == null) return false;
            bool valid = true;
            var variables = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode entry in node.GetNodes("SETTING"))
            {
                string name = entry.GetValue("name")?.Trim(), variable = entry.GetValue("var")?.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(variable)
                    || !Regex.IsMatch(variable, "^[A-Za-z0-9]+$") || !variables.Add(variable)
                    || ContainsSettingValue(name))
                { valid = SettingError("SETTING needs a literal name and unique alphanumeric var."); continue; }
                if (entry.HasNode("OPTIONS"))
                {
                    foreach (string field in new[] { "min", "max", "mul1", "mul2", "default", "step" })
                        if (entry.HasValue(field)) SettingError("SETTING " + variable + ": " + field
                            + " is not allowed with OPTIONS and was ignored.");
                    var options = entry.GetNode("OPTIONS").GetValues("option").ToList();
                    if (entry.GetNodes("OPTIONS").Length != 1 || options.Count == 0
                        || options.Any(string.IsNullOrEmpty) || options.Any(ContainsSettingValue))
                    { valid = SettingError("SETTING " + variable + " needs one OPTIONS node with nonempty literal option values."); continue; }
                    _settings.Add(new RecipeSetting { name = name, variable = variable, options = options,
                        min = 0, max = options.Count - 1, defaultValue = 0 });
                    continue;
                }
                bool numbers = TryReadOptionalDouble(entry, "min", 0, out double min);
                numbers &= TryReadOptionalDouble(entry, "max", 100, out double max);
                numbers &= TryReadOptionalDouble(entry, "mul1", 10, out double mul1);
                numbers &= TryReadOptionalDouble(entry, "mul2", 100, out double mul2);
                numbers &= TryReadOptionalDouble(entry, "step", 1, out double step);
                numbers &= TryReadOptionalDouble(entry, "default", min, out double initial);
                if (!numbers || min > max || initial < min || initial > max || step <= 0 || mul1 <= 0 || mul2 <= 0)
                { valid = SettingError("SETTING " + variable + " has invalid numeric bounds/default/steps."); continue; }
                _settings.Add(new RecipeSetting { name = name, variable = variable, min = min, max = max,
                    multiplier1 = mul1, multiplier2 = mul2, step = step, defaultValue = initial });
            }
            return valid;
        }

        internal static bool ValidateModuleSettingScope(ConfigNode module)
        {
            bool valid = true;
            foreach (ConfigNode.Value value in module.values)
                if (_moduleOnlyValueKeys.Contains(value.name) && ContainsSettingValue(value.value))
                {
                    KShared.LogError("SETTING references cannot change module/recipe selection field " + value.name,
                        "KhemistryISRU/SETTING");
                    valid = false;
                }
            foreach (string kind in new[] { "RECIPE_NAMES", "RECIPE_MULTIPLIERS" })
                foreach (ConfigNode child in module.GetNodes(kind))
                    foreach (ConfigNode.Value value in child.values)
                        if (ContainsSettingValue(value.value))
                        {
                            KShared.LogError("SETTING references are forbidden inside " + kind,
                                "KhemistryISRU/SETTING"); valid = false;
                        }
            return valid;
        }

        // Resolve on a private config copy. Keep mainNode as the original template so a
        // selection change can rebuild every parsed string (including nested biome rules).
        private bool ResolveSettingConfig(ConfigNode node, IDictionary<string, double> values, bool root = false,
            string parent = null)
        {
            bool valid = true;
            foreach (ConfigNode.Value value in node.values)
            {
                string text = value.value;
                if (!ContainsSettingValue(text)) continue;
                // Legacy whole-field math remains readable; new settings need no brackets.
                if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal)
                    && text.IndexOf(']') == text.Length - 1) text = text.Substring(1, text.Length - 2);
                string location = node.name + "/" + value.name;
                if ((root && (value.name == "name" || value.name.StartsWith("recipe", StringComparison.Ordinal)
                        && value.name != "recipeTime")) || value.name == "id"
                    || node.name == "RECIPE_NAMES" || node.name == "RECIPE_MULTIPLIERS")
                { valid = SettingError("SETTING references cannot change recipe identity/selection or ids: " + location); continue; }
                if (text.IndexOf('[') >= 0 || text.IndexOf(']') >= 0)
                { valid = SettingError(location + ": use direct SETTING insertion, not bracket interpolation."); continue; }
                if (!TryGetSettingValueReferences(text, out var refs, out string error))
                { valid = SettingError(location + ": " + error); continue; }

                bool parameter = node.name == "PARAMS" || node.name == "PARAM_REQUIREMENTS";
                bool condition = node.name == "PARAM_REQUIREMENTS" || (node.name == "PARAMS" && parent == "INPUT_MATERIAL");
                int prefix = condition && (text.StartsWith("EM", StringComparison.Ordinal) || text.StartsWith("EL", StringComparison.Ordinal)) ? 2
                    : condition && (text.StartsWith("M", StringComparison.Ordinal) || text.StartsWith("L", StringComparison.Ordinal)) ? 1 : 0;
                bool numeric = NumericSettingFields.Contains(value.name) || value.name.EndsWith("Mul", StringComparison.Ordinal)
                    || value.name.StartsWith("min", StringComparison.Ordinal) || value.name.StartsWith("max", StringComparison.Ordinal);
                if (parameter)
                {
                    // Parameter assignments have no declared scalar type. A bare number/expression
                    // is numeric; concatenation with literal text is a string.
                    string probe = text.Substring(prefix);
                    probe = Regex.Replace(probe, @"\((?:SETTING|INMAT|OUTMAT):[^)]*\)", "1", RegexOptions.IgnoreCase);
                    var parameterVariables = new Dictionary<string, KMathExpr.ValueRange>(StringComparer.OrdinalIgnoreCase);
                    foreach (ConfigNode.Value parameterValue in node.values)
                        parameterVariables[parameterValue.name] = new KMathExpr.ValueRange(1, 1);
                    foreach (string builtin in new[] { "amount", "size", "volume" })
                        parameterVariables[builtin] = new KMathExpr.ValueRange(1, 1);
                    numeric = prefix > 0 || KMathExpr.TryEvaluateRange(probe, out _, out _, parameterVariables);
                    if (!numeric && refs.All(r => !r.setting.IsChoice))
                        numeric = refs.All(r => (r.start == 0 || !char.IsLetterOrDigit(text[r.start - 1]))
                            && (r.start + r.length == text.Length || !char.IsLetterOrDigit(text[r.start + r.length])));
                }
                if (refs.Any(reference => reference.setting.IsChoice == numeric))
                { valid = SettingError(location + ": text options are only valid in strings; numeric settings only in expressions."); continue; }
                for (int i = refs.Count - 1; i >= 0; i--)
                {
                    var reference = refs[i];
                    double selected = values != null && values.TryGetValue(reference.setting.variable, out double supplied)
                        ? reference.setting.Clamp(supplied) : reference.setting.defaultValue;
                    string replacement = reference.setting.IsChoice ? reference.setting.SelectedOption(selected)
                        : "(" + selected.ToString("R", CultureInfo.InvariantCulture) + ")";
                    text = text.Remove(reference.start, reference.length).Insert(reference.start, replacement);
                }
                if (numeric)
                {
                    if (parameter && !condition)
                        text = "[" + text + "]"; // Adapter for ordered material assignment evaluator.
                    else if (condition && prefix > 0) { } // Numeric inequality evaluator handles bare math.
                    else if (condition)
                    {
                        if (!KMathExpr.TryEvaluate(text, out double number, out error) || !KShared.IsFinite(number))
                        { valid = SettingError(location + ": " + error); continue; }
                        text = MaterialParameterCondition.LiteralPrefix + number.ToString("R", CultureInfo.InvariantCulture);
                    }
                    else if (value.name == "outVolume" || node.name == "OUTPUT_MATERIAL" && value.name == "amount")
                        text = "[" + text + "]";
                    else if (value.name == "recipeTime"
                        || node.name.EndsWith("CARGOPART", StringComparison.Ordinal)) { }
                    else if (!ContainsInputMaterialValue(text) && !ContainsOutputMaterialValue(text)
                        && !ContainsParallaxValue(text))
                    {
                        if (!KMathExpr.TryEvaluate(text, out double number, out error) || !KShared.IsFinite(number))
                        { valid = SettingError(location + ": " + error); continue; }
                        text = number.ToString("R", CultureInfo.InvariantCulture);
                    }
                    else if (node.name == "OUTPUT_MATERIAL" && value.name == "amount")
                        text = "[" + text + "]";
                }
                else if (condition) text = MaterialParameterCondition.LiteralPrefix + text;
                value.value = text;
            }
            foreach (ConfigNode child in node.nodes)
            {
                if (child.name == "SETTING") continue;
                if (!ResolveSettingConfig(child, values, false, node.name)) valid = false;
            }
            return valid;
        }

        internal KhemistryISRURecipe WithSettingValues(IDictionary<string, double> values)
            => new KhemistryISRURecipe(mainNode, _name, values).ScaledCopy(_settingRecipeScale);
    }
}
