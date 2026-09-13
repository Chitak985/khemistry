using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        private KhemistryMaterialInstance CreateConsumedInputMaterialSnapshot(
            IList<KhemistryMaterialInstance> consumed)
        {
            if (consumed == null || consumed.Count == 0 || consumed[0] == null)
                return null;

            KhemistryMaterialInstance combined =
                new KhemistryMaterialInstance(consumed[0]);
            bool couldCombineEveryPiece = true;
            for (int index = 1; index < consumed.Count; index++)
            {
                if (consumed[index] == null) continue;
                KhemistryMaterialInstance piece =
                    new KhemistryMaterialInstance(consumed[index]);
                if (combined.Merge(piece) || combined.ContaminatedMerge(piece)) continue;
                couldCombineEveryPiece = false;
                break;
            }
            if (couldCombineEveryPiece) return combined;

            // A requirement can span distinct stacks. Normally their material merge equations
            // produce one representative input value. If the material explicitly refuses that
            // merge, keep the first stack's values but still expose the total consumed amount.
            long totalAmount = consumed.Where(value => value != null)
                .Sum(value => (long)Math.Max(0, value.amount));
            combined = new KhemistryMaterialInstance(consumed[0])
            {
                amount = totalAmount >= int.MaxValue ? int.MaxValue : (int)totalAmount
            };
            combined.UpdateParams("KhemistryISRU/CreateConsumedInputMaterialSnapshot");
            KShared.LogError("Converter \"" + ConverterName
                + "\": an INPUT_MATERIAL id consumed stacks that could not be merged; "
                + "INMAT uses the first stack's shape, size, and parameters with the total consumed amount.",
                "KhemistryISRU/CreateConsumedInputMaterialSnapshot");
            return combined;
        }

        private bool TryResolveInputMaterialOutputs(
            IEnumerable<KhemistryISRURecipe.ResourceOutputMaterial> templates,
            IDictionary<string, KhemistryMaterialInstance> inputs,
            out List<KhemistryISRURecipe.ResourceOutputMaterial> resolved)
        {
            resolved = new List<KhemistryISRURecipe.ResourceOutputMaterial>();
            foreach (KhemistryISRURecipe.ResourceOutputMaterial template in
                     templates ?? Enumerable.Empty<KhemistryISRURecipe.ResourceOutputMaterial>())
            {
                if (!KhemistryISRURecipe.MaterialOutputUsesInputMaterialValues(template))
                {
                    resolved.Add(template);
                    continue;
                }

                KhemistryISRURecipe.ResourceOutputMaterial output = template;
                if (!TryResolveInputMaterialReferences(template.shape, inputs, false,
                        template.name + " shape", out output.shape)
                    || !TryResolveInputMaterialReferences(template.size, inputs, false,
                        template.name + " size", out output.size)
                    || !TryResolveInputMaterialReferences(template.outVolume, inputs, true,
                        template.name + " outVolume", out output.outVolume))
                    return false;

                if (!string.IsNullOrEmpty(template.amountExpression))
                {
                    string amountError = null;
                    double amount = 0.0;
                    if (!TryResolveInputMaterialReferences(template.amountExpression, inputs,
                            true, template.name + " amount", out string amountExpression)
                        || !KMathExpr.TryEvaluate(amountExpression, out amount,
                            out amountError)
                        || double.IsNaN(amount) || double.IsInfinity(amount)
                        || amount <= 0.0
                        || double.IsNaN(template.amountScale)
                        || double.IsInfinity(template.amountScale)
                        || template.amountScale <= 0.0
                        || double.IsNaN(amount * template.amountScale)
                        || double.IsInfinity(amount * template.amountScale))
                    {
                        KShared.LogError("Converter \"" + ConverterName
                            + "\": OUTPUT_MATERIAL \"" + template.name
                            + "\" amount expression could not be evaluated: "
                            + (amountError ?? "the result was not finite and positive") + ".",
                            "KhemistryISRU/TryResolveInputMaterialOutputs");
                        return false;
                    }
                    output.amount = amount * template.amountScale;
                    output.amountExpression = null;
                }

                output.parameters = new Dictionary<string, string>();
                output.parameterAssignments = new List<KeyValuePair<string, string>>();
                IEnumerable<KeyValuePair<string, string>> assignments =
                    (IEnumerable<KeyValuePair<string, string>>)template.parameterAssignments
                    ?? (template.parameters ?? new Dictionary<string, string>())
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> assignment in assignments)
                {
                    string value = assignment.Value;
                    if (KhemistryISRURecipe.ContainsInputMaterialValue(value))
                    {
                        string expressionError = null;
                        double evaluated = 0.0;
                        if (!TryResolveInputMaterialReferences(value, inputs, true,
                                template.name + " parameter " + assignment.Key,
                                out string expression)
                            || !KMathExpr.TryEvaluate(expression, out evaluated,
                                out expressionError, output.parameters))
                        {
                            KShared.LogError("Converter \"" + ConverterName
                                + "\": OUTPUT_MATERIAL \"" + template.name
                                + "\" parameter \"" + assignment.Key
                                + "\" expression could not be evaluated: "
                                + (expressionError ?? "invalid input-material reference") + ".",
                                "KhemistryISRU/TryResolveInputMaterialOutputs");
                            return false;
                        }
                        value = evaluated.ToString("R", CultureInfo.InvariantCulture);
                    }
                    output.parameters[assignment.Key] = value;
                    output.parameterAssignments.Add(new KeyValuePair<string, string>(
                        assignment.Key, value));
                }

                KhemistryMaterial definition = KShared.Instance?.materialList
                    .FirstOrDefault(material => material.name == output.name);
                if (definition == null || !definition.shapes.Contains(output.shape))
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": OUTPUT_MATERIAL \"" + output.name
                        + "\" resolved to unsupported shape \"" + output.shape + "\".",
                        "KhemistryISRU/TryResolveInputMaterialOutputs");
                    return false;
                }

                output.usesParams = output.parameters.Count > 0;
                output.inputMaterialResolved = true;
                resolved.Add(output);
            }
            return true;
        }

        private bool TryResolveInputMaterialReferences(string value,
            IDictionary<string, KhemistryMaterialInstance> inputs, bool numeric,
            string outputLocation, out string resolved)
        {
            resolved = value;
            if (!_activeRecipe.TryGetInputMaterialValueReferences(value,
                    out List<KhemistryISRURecipe.InputMaterialValueReference> references,
                    out string referenceError))
            {
                KShared.LogError("Converter \"" + ConverterName + "\": "
                    + referenceError, "KhemistryISRU/TryResolveInputMaterialReferences");
                return false;
            }

            for (int index = references.Count - 1; index >= 0; index--)
            {
                KhemistryISRURecipe.InputMaterialValueReference reference = references[index];
                string replacement = GetInputMaterialValue(reference, inputs,
                    outputLocation);
                if (numeric && (!double.TryParse(replacement, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number)))
                {
                    KShared.LogError("Converter \"" + ConverterName + "\": (INMAT:"
                        + reference.input.id + ":" + reference.field + ") resolved to \""
                        + replacement + "\", which is not numeric in " + outputLocation
                        + "; using 0.",
                        "KhemistryISRU/TryResolveInputMaterialReferences");
                    replacement = "0";
                }

                resolved = resolved.Remove(reference.start, reference.length)
                    .Insert(reference.start, replacement);
            }
            return true;
        }

        private string GetInputMaterialValue(
            KhemistryISRURecipe.InputMaterialValueReference reference,
            IDictionary<string, KhemistryMaterialInstance> inputs,
            string outputLocation)
        {
            if (inputs == null || !inputs.TryGetValue(reference.input.id,
                    out KhemistryMaterialInstance input) || input == null)
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": no consumed material is available for INPUT_MATERIAL id \""
                    + reference.input.id + "\" while resolving " + outputLocation + ".",
                    "KhemistryISRU/GetInputMaterialValue");
                return "0";
            }

            if (string.Equals(reference.field, "shape", StringComparison.OrdinalIgnoreCase))
                return input.shape ?? "";
            if (string.Equals(reference.field, "size", StringComparison.OrdinalIgnoreCase))
                return input.size ?? "";
            if (string.Equals(reference.field, "amount", StringComparison.OrdinalIgnoreCase))
                return input.amount.ToString(CultureInfo.InvariantCulture);

            foreach (KeyValuePair<string, string> parameter in input.parameters)
                if (string.Equals(parameter.Key, reference.field,
                        StringComparison.OrdinalIgnoreCase))
                    return parameter.Value ?? "";

            KShared.LogError("Converter \"" + ConverterName + "\": INPUT_MATERIAL id \""
                + reference.input.id + "\" has no parameter \"" + reference.field
                + "\" while resolving " + outputLocation + "; using 0.",
                "KhemistryISRU/GetInputMaterialValue");
            return "0";
        }

        private static bool IsSavedInputMaterialRealization(
            KhemistryISRURecipe.ResourceOutputMaterial saved,
            KhemistryISRURecipe.ResourceOutputMaterial template)
        {
            if (!saved.inputMaterialResolved
                || !KhemistryISRURecipe.MaterialOutputUsesInputMaterialValues(template)
                || saved.name != template.name
                || !InputMaterialTemplateStringMatches(template.shape, saved.shape)
                || !InputMaterialTemplateStringMatches(template.size, saved.size)
                || saved.parameters == null || template.parameters == null
                || saved.parameters.Count != template.parameters.Count)
                return false;

            KhemistryMaterial definition = KShared.Instance?.materialList
                .FirstOrDefault(material => material.name == saved.name);
            if (definition == null || !definition.shapes.Contains(saved.shape))
                return false;

            if (KhemistryISRURecipe.ContainsInputMaterialValue(template.outVolume))
            {
                if (KhemistryISRURecipe.ContainsInputMaterialValue(saved.outVolume)
                    || string.IsNullOrWhiteSpace(saved.outVolume))
                    return false;
            }
            else if (!ParallaxTemplateValueMatches(template.outVolume, saved.outVolume))
                return false;

            foreach (KeyValuePair<string, string> parameter in template.parameters)
            {
                if (!saved.parameters.TryGetValue(parameter.Key, out string savedValue))
                    return false;
                if (KhemistryISRURecipe.ContainsInputMaterialValue(parameter.Value))
                {
                    if (!double.TryParse(savedValue, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number))
                        return false;
                }
                else if (!ParallaxTemplateValueMatches(parameter.Value, savedValue))
                    return false;
            }

            if (template.parameterAssignments == null
                || saved.parameterAssignments == null)
                return true;
            if (template.parameterAssignments.Count != saved.parameterAssignments.Count)
                return false;
            for (int index = 0; index < template.parameterAssignments.Count; index++)
            {
                KeyValuePair<string, string> expected = template.parameterAssignments[index];
                KeyValuePair<string, string> actual = saved.parameterAssignments[index];
                if (!string.Equals(expected.Key, actual.Key, StringComparison.Ordinal))
                    return false;
                if (KhemistryISRURecipe.ContainsInputMaterialValue(expected.Value))
                {
                    if (!double.TryParse(actual.Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number))
                        return false;
                }
                else if (!ParallaxTemplateValueMatches(expected.Value, actual.Value))
                    return false;
            }
            return true;
        }

        private static bool InputMaterialTemplateStringMatches(string template,
            string actual)
        {
            if (!KhemistryISRURecipe.ContainsInputMaterialValue(template))
                return ParallaxTemplateValueMatches(template, actual);
            return !string.IsNullOrEmpty(actual)
                && !KhemistryISRURecipe.ContainsInputMaterialValue(actual);
        }
    }
}
