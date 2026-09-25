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
            return TryResolveInputMaterialOutputs(templates, inputs, out resolved, out _,
                1.0);
        }

        private bool TryResolveInputMaterialOutputs(
            IEnumerable<KhemistryISRURecipe.ResourceOutputMaterial> templates,
            IDictionary<string, KhemistryMaterialInstance> inputs,
            out List<KhemistryISRURecipe.ResourceOutputMaterial> resolved,
            out Dictionary<string, KhemistryISRURecipe.ResourceOutputMaterial> outputValues,
            double outputAmountMultiplier)
        {
            resolved = new List<KhemistryISRURecipe.ResourceOutputMaterial>();
            outputValues =
                new Dictionary<string, KhemistryISRURecipe.ResourceOutputMaterial>(
                    StringComparer.Ordinal);
            List<KhemistryISRURecipe.ResourceOutputMaterial> pending =
                (templates ?? Enumerable.Empty<KhemistryISRURecipe.ResourceOutputMaterial>())
                .ToList();
            List<KhemistryISRURecipe.ResourceOutputMaterial> ordered =
                new List<KhemistryISRURecipe.ResourceOutputMaterial>();
            HashSet<string> availableOutputIds = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    KhemistryISRURecipe.ResourceOutputMaterial candidate = pending[index];
                    if (!AreOutputMaterialReferencesAvailable(candidate,
                            availableOutputIds))
                        continue;
                    ordered.Add(candidate);
                    pending.RemoveAt(index);
                    if (!string.IsNullOrEmpty(candidate.id))
                        availableOutputIds.Add(candidate.id);
                }
                if (pending.Count == before)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": OUTPUT_MATERIAL references contain a cycle or an unresolved id.",
                        "KhemistryISRU/TryResolveInputMaterialOutputs");
                    return false;
                }
            }

            foreach (KhemistryISRURecipe.ResourceOutputMaterial template in ordered)
            {
                KhemistryISRURecipe.ResourceOutputMaterial output = template;
                if (!TryResolveMaterialReferences(template.shape, inputs, outputValues,
                        false, template.name + " shape", out output.shape)
                    || !TryResolveMaterialReferences(template.size, inputs, outputValues,
                        false, template.name + " size", out output.size)
                    || !TryResolveMaterialReferences(template.outVolume, inputs, outputValues,
                        true, template.name + " outVolume", out output.outVolume))
                    return false;

                if (!KMathExpr.TryInterpolate(output.shape, out output.shape,
                        out string shapeError))
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": OUTPUT_MATERIAL \"" + template.name
                        + "\" shape could not be resolved: " + shapeError + ".",
                        "KhemistryISRU/TryResolveInputMaterialOutputs");
                    return false;
                }

                if (!string.IsNullOrEmpty(template.amountExpression))
                {
                    string amountError = null;
                    double amount = 0.0;
                    if (!TryResolveMaterialReferences(template.amountExpression, inputs,
                            outputValues, true, template.name + " amount",
                            out string amountExpression)
                        || !KMathExpr.TryInterpolateNumber(amountExpression, out amount,
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
                    if (!TryResolveMaterialReferences(assignment.Value, inputs,
                            outputValues, false,
                            template.name + " parameter " + assignment.Key,
                            out string value))
                        return false;
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
                output.inputMaterialResolved =
                    KhemistryISRURecipe.MaterialOutputUsesInputMaterialValues(template)
                    || KhemistryISRURecipe.MaterialOutputUsesOutputMaterialValues(template)
                    || KhemistryISRURecipe.MaterialOutputUsesSettingValues(template);
                if (!string.IsNullOrEmpty(output.id))
                {
                    if (!TryCreateOutputMaterialReferenceValue(output, definition,
                            out KhemistryISRURecipe.ResourceOutputMaterial referenceValue))
                        return false;
                    referenceValue.amount *= outputAmountMultiplier;
                    outputValues[output.id] = referenceValue;
                }
                resolved.Add(output);
            }
            return true;
        }

        private bool AreOutputMaterialReferencesAvailable(
            KhemistryISRURecipe.ResourceOutputMaterial output,
            ISet<string> availableIds)
        {
            IEnumerable<string> values = new[] { output.shape, output.size,
                    output.amountExpression, output.outVolume }
                .Concat((output.parameterAssignments
                        ?? new List<KeyValuePair<string, string>>())
                    .Select(pair => pair.Value));
            foreach (string value in values)
            {
                if (!_activeRecipe.TryGetOutputMaterialValueReferences(value,
                        out List<KhemistryISRURecipe.OutputMaterialValueReference> references,
                        out _))
                    return false;
                if (references.Any(reference =>
                        !availableIds.Contains(reference.output.id)))
                    return false;
            }
            return true;
        }

        private bool TryResolveInputMaterialConditions(
            ref KhemistryISRURecipe.ResourceInputMaterial material)
        {
            var resolved = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> condition in
                     material.parameters ?? new Dictionary<string, string>())
            {
                if (!MaterialParameterCondition.TryResolve(condition.Value, variable =>
                        {
                            foreach (KhemistryISRURecipe.RecipeSetting setting in _activeRecipe._settings)
                                if (setting.variable == variable)
                                    return (double?)GetActiveRecipeSettingValue(setting);
                            return null;
                        }, out string value, out string error))
                {
                    KShared.LogError("Converter \"" + ConverterName + "\": INPUT_MATERIAL \""
                        + material.name + "\" parameter \"" + condition.Key
                        + "\" could not be resolved: " + error,
                        "KhemistryISRU/TryResolveInputMaterialConditions");
                    return false;
                }
                resolved.Add(condition.Key, value);
            }
            // ResourceInputMaterial is a struct; only this operation's copy is changed.
            material.parameters = resolved;
            return true;
        }

        private bool TryPreviewVesselMaterials(
            KhemistryISRURecipe.ResourceInputMaterial requirement, int amount,
            out List<KhemistryMaterialInstance> pieces)
        {
            pieces = new List<KhemistryMaterialInstance>();
            if (amount <= 0) return true;
            if (!TryResolveInputMaterialConditions(ref requirement)) return false;
            IEnumerable<KhemistryMaterialInstance> available;
            if ((moduleType == "kerbalEVA"
                    || (moduleType == "partEVA" && useSuitCell))
                && _kerbalHost != null)
                available = _kerbalHost.materialSuitCellContents;
            else if (moduleType == "partEVA" || vessel == null)
                return false;
            else
                available = vessel.parts
                    .SelectMany(vesselPart => vesselPart.Modules
                        .OfType<KhemistryMaterialStorage>())
                    .SelectMany(storage => storage.contents);

            int remaining = amount;
            foreach (KhemistryMaterialInstance stored in available)
            {
                if (!KhemistryMaterialStorage.MatchesMaterial(stored,
                        requirement.name, requirement.shape, requirement.size,
                        requirement.parameters))
                    continue;
                int take = Math.Min(remaining, stored.amount);
                if (take <= 0) continue;
                KhemistryMaterialInstance copy = new KhemistryMaterialInstance(stored)
                {
                    amount = take
                };
                copy.UpdateParams("KhemistryISRU/TryPreviewVesselMaterials");
                pieces.Add(copy);
                remaining -= take;
                if (remaining == 0) return true;
            }
            pieces.Clear();
            return false;
        }

        private bool TryResolveRecipeTime(KhemistryISRUBiomeConfig biomeConfig,
            out double recipeTime)
        {
            recipeTime = 0.0;
            if (_activeRecipe == null) return false;
            string expression = _activeRecipe._recipeTimeExpression;
            if (!TryResolveSettingReferences(expression, "recipeTime", out expression))
                return false;
            if (!KhemistryISRURecipe.ContainsInputMaterialValue(expression)
                && !KhemistryISRURecipe.ContainsOutputMaterialValue(expression))
                return KMathExpr.TryInterpolateNumber(expression, out recipeTime,
                    out _) && recipeTime > 0.0;

            Dictionary<string, KhemistryMaterialInstance> inputValues =
                new Dictionary<string, KhemistryMaterialInstance>(StringComparer.Ordinal);
            foreach (KhemistryISRURecipe.ResourceInputMaterial input in
                     _activeRecipe._inputMaterials)
            {
                int amount = ScaleDiscreteMaterialAmount(input.amount,
                    biomeConfig?.inputMultiplier ?? 1.0);
                if (!TryPreviewVesselMaterials(input, amount,
                        out List<KhemistryMaterialInstance> pieces))
                    return false;
                if (!string.IsNullOrEmpty(input.id))
                {
                    KhemistryMaterialInstance snapshot =
                        CreateConsumedInputMaterialSnapshot(pieces);
                    if (snapshot == null) return false;
                    inputValues[input.id] = snapshot;
                }
            }

            List<KhemistryISRURecipe.ResourceOutputMaterial> templates =
                _activeRecipe._outputMaterials;
            if (_activeRecipe.UsesParallaxScatters)
            {
                if (!TryGetNearbyParallaxTarget(false,
                        out KhemistryParallaxTarget target))
                    return false;
                templates = ResolveParallaxMaterialOutputs(templates, target);
            }
            string error = null;
            if (!TryResolveInputMaterialOutputs(templates, inputValues, out _,
                    out Dictionary<string, KhemistryISRURecipe.ResourceOutputMaterial>
                        outputValues, biomeConfig?.outputMultiplier ?? 1.0)
                || !TryResolveMaterialReferences(expression, inputValues, outputValues,
                    true, "recipeTime", out string resolvedExpression)
                || !KMathExpr.TryInterpolateNumber(resolvedExpression, out recipeTime,
                    out error)
                || double.IsNaN(recipeTime) || double.IsInfinity(recipeTime)
                || recipeTime <= 0.0)
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": recipeTime could not be resolved: "
                    + (error ?? "the result must be finite and positive") + ".",
                    "KhemistryISRU/TryResolveRecipeTime");
                return false;
            }
            return true;
        }

        private bool TryResolveMaterialReferences(string value,
            IDictionary<string, KhemistryMaterialInstance> inputs,
            IDictionary<string, KhemistryISRURecipe.ResourceOutputMaterial> outputs,
            bool numeric, string location, out string resolved)
        {
            return TryResolveInputMaterialReferences(value, inputs, numeric, location,
                    out resolved)
                && TryResolveOutputMaterialReferences(resolved, outputs, numeric, location,
                    out resolved)
                && TryResolveSettingReferences(resolved, location, out resolved);
        }

        private bool TryResolveSettingReferences(string value, string location,
            out string resolved)
        {
            resolved = value;
            if (!_activeRecipe.TryGetSettingValueReferences(value,
                    out List<KhemistryISRURecipe.SettingValueReference> references,
                    out string referenceError))
            {
                KShared.LogError("Converter \"" + ConverterName + "\": "
                    + referenceError, "KhemistryISRU/TryResolveSettingReferences");
                return false;
            }
            for (int index = references.Count - 1; index >= 0; index--)
            {
                KhemistryISRURecipe.SettingValueReference reference = references[index];
                string replacement = GetActiveRecipeSettingValue(reference.setting)
                    .ToString("R", CultureInfo.InvariantCulture);
                resolved = resolved.Remove(reference.start, reference.length)
                    .Insert(reference.start, replacement);
            }
            return true;
        }

        private bool TryResolveOutputMaterialReferences(string value,
            IDictionary<string, KhemistryISRURecipe.ResourceOutputMaterial> outputs,
            bool numeric, string location, out string resolved)
        {
            resolved = value;
            if (!_activeRecipe.TryGetOutputMaterialValueReferences(value,
                    out List<KhemistryISRURecipe.OutputMaterialValueReference> references,
                    out string referenceError))
            {
                KShared.LogError("Converter \"" + ConverterName + "\": "
                    + referenceError,
                    "KhemistryISRU/TryResolveOutputMaterialReferences");
                return false;
            }

            for (int index = references.Count - 1; index >= 0; index--)
            {
                KhemistryISRURecipe.OutputMaterialValueReference reference =
                    references[index];
                string replacement = GetOutputMaterialValue(reference, outputs, location);
                bool mustBeNumeric = numeric || IsInsideInterpolation(value,
                    reference.start);
                if (mustBeNumeric && (!double.TryParse(replacement, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number)))
                {
                    KShared.LogError("Converter \"" + ConverterName + "\": (OUTMAT:"
                        + reference.output.id + ":" + reference.field
                        + ") resolved to \"" + replacement + "\", which is not numeric in "
                        + location + "; using 0.",
                        "KhemistryISRU/TryResolveOutputMaterialReferences");
                    replacement = "0";
                }
                resolved = resolved.Remove(reference.start, reference.length)
                    .Insert(reference.start, replacement);
            }
            return true;
        }

        private string GetOutputMaterialValue(
            KhemistryISRURecipe.OutputMaterialValueReference reference,
            IDictionary<string, KhemistryISRURecipe.ResourceOutputMaterial> outputs,
            string location)
        {
            if (outputs == null || !outputs.TryGetValue(reference.output.id,
                    out KhemistryISRURecipe.ResourceOutputMaterial output))
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": no resolved material is available for OUTPUT_MATERIAL id \""
                    + reference.output.id + "\" while resolving " + location + ".",
                    "KhemistryISRU/GetOutputMaterialValue");
                return "0";
            }
            if (string.Equals(reference.field, "shape", StringComparison.OrdinalIgnoreCase))
                return output.shape ?? "";
            if (string.Equals(reference.field, "size", StringComparison.OrdinalIgnoreCase))
                return output.size ?? "";
            if (string.Equals(reference.field, "amount", StringComparison.OrdinalIgnoreCase))
                return output.amount.ToString("R", CultureInfo.InvariantCulture);
            if (string.Equals(reference.field, "volume", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference.field, "outVolume", StringComparison.OrdinalIgnoreCase))
                return output.outVolume ?? "0";
            foreach (KeyValuePair<string, string> parameter in output.parameters
                     ?? new Dictionary<string, string>())
                if (string.Equals(parameter.Key, reference.field,
                        StringComparison.OrdinalIgnoreCase))
                    return parameter.Value ?? "";
            KShared.LogError("Converter \"" + ConverterName + "\": OUTPUT_MATERIAL id \""
                + reference.output.id + "\" has no parameter \"" + reference.field
                + "\" while resolving " + location + "; using 0.",
                "KhemistryISRU/GetOutputMaterialValue");
            return "0";
        }

        private bool TryCreateOutputMaterialReferenceValue(
            KhemistryISRURecipe.ResourceOutputMaterial output,
            KhemistryMaterial definition,
            out KhemistryISRURecipe.ResourceOutputMaterial value)
        {
            value = output;
            if (!KMathExpr.TryInterpolate(output.size, out string resolvedSize,
                    out string sizeError))
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": OUTPUT_MATERIAL id \"" + output.id
                    + "\" size could not be evaluated for OUTMAT: " + sizeError + ".",
                    "KhemistryISRU/TryCreateOutputMaterialReferenceValue");
                return false;
            }
            KhemistryMaterialInstance instance = new KhemistryMaterialInstance(
                definition, output.shape, resolvedSize, 1f, null) { amount = 1 };
            IEnumerable<KeyValuePair<string, string>> assignments =
                (IEnumerable<KeyValuePair<string, string>>)output.parameterAssignments
                ?? (output.parameters ?? new Dictionary<string, string>())
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal);
            if (!instance.ApplyParameterValuesInOrder(assignments,
                    "KhemistryISRU/TryCreateOutputMaterialReferenceValue"))
                return false;
            if (!KShared.TryEvaluateOutVolumeExpression(output.outVolume, resolvedSize,
                    instance.parameters,
                    "KhemistryISRU/TryCreateOutputMaterialReferenceValue",
                    out double volume))
                return false;
            instance.volume = (float)volume;
            instance.UpdateParams("KhemistryISRU/TryCreateOutputMaterialReferenceValue");
            value.size = resolvedSize;
            value.parameters = new Dictionary<string, string>(instance.parameters);
            value.parameterAssignments = value.parameters.OrderBy(pair => pair.Key,
                StringComparer.Ordinal).ToList();
            value.outVolume = volume.ToString("R", CultureInfo.InvariantCulture);
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
                bool referenceMustBeNumeric = numeric
                    || IsInsideInterpolation(value, reference.start);
                if (referenceMustBeNumeric && (!double.TryParse(replacement, NumberStyles.Float,
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

        private static bool IsInsideInterpolation(string value, int position)
        {
            if (string.IsNullOrEmpty(value) || position < 0 || position > value.Length)
                return false;
            int opening = value.LastIndexOf('[', Math.Max(0, position - 1));
            if (opening < 0) return false;
            int closing = value.IndexOf(']', opening + 1);
            return closing >= position;
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
                || !(KhemistryISRURecipe.MaterialOutputUsesInputMaterialValues(template)
                    || KhemistryISRURecipe.MaterialOutputUsesOutputMaterialValues(template)
                    || KhemistryISRURecipe.MaterialOutputUsesSettingValues(template))
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

            if (ContainsAnyMaterialValue(template.outVolume))
            {
                if (ContainsAnyMaterialValue(saved.outVolume)
                    || string.IsNullOrWhiteSpace(saved.outVolume))
                    return false;
            }
            else if (!ParallaxTemplateValueMatches(template.outVolume, saved.outVolume))
                return false;

            foreach (KeyValuePair<string, string> parameter in template.parameters)
            {
                if (!saved.parameters.TryGetValue(parameter.Key, out string savedValue))
                    return false;
                if (ContainsAnyMaterialValue(parameter.Value))
                {
                    if (string.IsNullOrWhiteSpace(savedValue)
                        || ContainsAnyMaterialValue(savedValue))
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
                if (ContainsAnyMaterialValue(expected.Value))
                {
                    if (string.IsNullOrWhiteSpace(actual.Value)
                        || ContainsAnyMaterialValue(actual.Value))
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
            if (!ContainsAnyMaterialValue(template))
                return ParallaxTemplateValueMatches(template, actual);
            return !string.IsNullOrEmpty(actual)
                && !ContainsAnyMaterialValue(actual);
        }

        private static bool ContainsAnyMaterialValue(string value)
            => KhemistryISRURecipe.ContainsInputMaterialValue(value)
                || KhemistryISRURecipe.ContainsOutputMaterialValue(value)
                || KhemistryISRURecipe.ContainsSettingValue(value);
    }
}
