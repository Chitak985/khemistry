using System;
using System.Collections.Generic;
using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechISRURecipe
    {
        public struct CargoPartEntry
        {
            public string name, amountExpression;
            public double scale;
        }
        public readonly List<CargoPartEntry> _inputCargoParts = new List<CargoPartEntry>();
        public readonly List<CargoPartEntry> _outputCargoParts = new List<CargoPartEntry>();

        private bool LoadCargoParts(ConfigNode node)
        {
            bool valid = true;
            foreach (string kind in new[] { "INPUT_CARGOPART", "OUTPUT_CARGOPART", "OUPUT_CARGOPART" })
                foreach (ConfigNode entry in node.GetNodes(kind))
                {
                    string name = entry.GetValue("name")?.Trim();
                    string expression = entry.GetValue("amount")?.Trim();
                    string error = null;
                    bool parsed = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(expression)
                        && !entry.HasValue("id") && expression.IndexOf('[') < 0 && expression.IndexOf(']') < 0
                        && TryReplaceMaterialValuesForValidation(expression, out string validation, out error)
                        && CTMathExpr.TryEvaluate(validation, out double ignored, out error);
                    if (!parsed)
                    {
                        valid = false;
                        CTShared.LogError("Recipe \"" + _name + "\": " + kind + " \"" + name
                            + "\" requires a name and a bare amount expression, without id or interpolation. "
                            + error, "ChitakTechISRURecipe/LoadCargoParts");
                        continue;
                    }
                    // Part prefabs are resolved at execution, after the part database is ready.
                    (kind == "INPUT_CARGOPART" ? _inputCargoParts : _outputCargoParts)
                        .Add(new CargoPartEntry { name = name, amountExpression = expression, scale = 1 });
                }
            return valid;
        }

        private void CopyCargoPartsTo(ChitakTechISRURecipe copy, double multiplier)
        {
            copy._inputCargoParts.AddRange(_inputCargoParts.Select(entry => new CargoPartEntry
                { name = entry.name, amountExpression = entry.amountExpression, scale = entry.scale * multiplier }));
            copy._outputCargoParts.AddRange(_outputCargoParts.Select(entry => new CargoPartEntry
                { name = entry.name, amountExpression = entry.amountExpression, scale = entry.scale * multiplier }));
        }
    }
}
