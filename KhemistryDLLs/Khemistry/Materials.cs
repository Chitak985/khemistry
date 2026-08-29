using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    /// <summary>
    /// A material usually loaded from configs. It defines its name, allowed shapes, and allowed parameters.
    /// An instance of this material used as a resource is <see cref="KhemistryMaterialInstance"/>.
    /// Warning: Derived parameters are always calculated using the formula in their default value. Overwriting these won't do anything.
    /// </summary>
    public class KhemistryMaterial
    {
        /// <summary>Name of the material, must be unique.</summary>
        public string name = "LOADFAIL";
        
        /// <summary>Shapes allowed by the material.</summary>
        public List<string> shapes = new List<string>();
        
        /// <summary>
        /// List of parameters of the material.
        /// The value is either a default value or the equation to derive it from.
        /// The derivation equation must start with DER and it is a KMathExpr.
        /// </summary>
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        
        /// <summary>
        /// List of equations to merge parameters of the material.
        /// If one isn't loaded for a numeric parameter, it defaults to a weighted average by amount.
        /// The merge equation is a KMathExpr and can include the current variables and the other
        /// material's variables with an O suffix.
        /// </summary>
        public Dictionary<string, string> parameterMergers = new Dictionary<string, string>();

        /// <summary>
        /// Construct the material from a ConfigNode.
        /// The node must be named KHEMISTRY_MATERIAL.
        /// </summary>
        public KhemistryMaterial(ConfigNode configNode)
        {
            // Check if the config node is valid
            if (configNode.name != "KHEMISTRY_MATERIAL")
            {
                KShared.LogError("KhemistryMaterial loading failed because the node isn't named KHEMISTRY_MATERIAL!", "KhemistryMaterial/constructor");
                return;
            }
            if (!configNode.HasNode("SHAPES") || !configNode.HasValue("name"))
            {
                KShared.LogError("KhemistryMaterial loading failed because one of the nodes isn't present!", "KhemistryMaterial/constructor");
                return;
            }

            // Set material name from the config
            name = configNode.GetValue("name");

            // Set shapes from the config
            foreach (string shape in configNode.GetNode("SHAPES").GetValues("name"))
                shapes.Add(shape);

            // Set parameters (if there are any) from the config
            if (configNode.HasNode("PARAMS"))
                foreach (string key in configNode.GetNode("PARAMS").values.DistinctNames())
                    parameters.Add(key, configNode.GetNode("PARAMS").GetValue(key));

            // Set parameter merge expressions (if there are any) from the config.
            if (configNode.HasNode("PARAM_MERGING"))
            {
                ConfigNode mergingNode = configNode.GetNode("PARAM_MERGING");
                foreach (string key in mergingNode.values.DistinctNames())
                {
                    if (!parameters.ContainsKey(key))
                    {
                        KShared.LogError(
                            "Material \"" + name + "\" has a PARAM_MERGING equation for unknown parameter \""
                            + key + "\"; the equation was ignored.",
                            "KhemistryMaterial/constructor");
                        continue;
                    }
                    if (IsDerivedParameter(key))
                    {
                        KShared.LogError(
                            "Material \"" + name + "\" has a PARAM_MERGING equation for derived parameter \""
                            + key + "\"; derived parameters are recalculated after a merge, so the equation was ignored.",
                            "KhemistryMaterial/constructor");
                        continue;
                    }

                    string expression = mergingNode.GetValue(key);
                    if (!string.IsNullOrWhiteSpace(expression))
                        parameterMergers[key] = expression.Trim();
                }
            }

            // Default non-derived merge expressions to a weighted average.
            foreach (string param in parameters.Keys)
                if (!IsDerivedParameter(param) && !parameterMergers.ContainsKey(param))
                    parameterMergers.Add(param, $"(({param}*amount)+({param}O*amountO))/(amount+amountO)");
        }

        public bool IsDerivedParameter(string parameterName)
        {
            return parameters.TryGetValue(parameterName, out string configuredValue)
                && configuredValue?.TrimStart().StartsWith("DER", StringComparison.OrdinalIgnoreCase) == true;
        }

        public string GetDerivationExpression(string parameterName)
        {
            if (!IsDerivedParameter(parameterName)) return null;
            string configuredValue = parameters[parameterName].TrimStart();
            return configuredValue.Substring(3);
        }
    }
    /// <summary>
    /// Instance of a <see cref="KhemistryMaterial"/> with its own shape, size, volume, and parameters.
    /// </summary>
    public class KhemistryMaterialInstance
    {
        public KhemistryMaterial material;
        public string shape = "null";
        public string size = "null";
        public float volume = 0f;  // cubic meters occupied by one unit of this material
        public int amount = 1;  // how many material units are combined into this instance
        /// <summary>
        /// Contains all of the parameters of this KhemistryMaterialInstance.
        /// PLEASE call <c>.UpdateParams("class/function");</c> every time this is accessed.
        /// Not using get and set because this is a dictionary and I don't want to make my own class for it.
        /// </summary>
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        private string _lastContaminatedMergeError;
        private string _lastDerivedParameterError;

        /// <summary>
        /// Update all derivable parameters.
        /// Should be called before accessing parameters to make sure they are up to date.
        /// </summary>
        public void UpdateParams(string location="KhemistryMaterialInstance/constructor(matInst)")
        {
            if (TryCalculateDerivedParameters(parameters, amount,
                    out Dictionary<string, string> updated, out string error))
            {
                ReplaceParameters(updated);
                _lastDerivedParameterError = null;
            }
            else if (!string.Equals(_lastDerivedParameterError, error, StringComparison.Ordinal))
            {
                KShared.LogError("Not all derived parameters could be updated: " + error, location);
                _lastDerivedParameterError = error;
            }
        }

        /// <summary>
        /// Total volume of the material instance.
        /// This is just the base volume multiplied by material amount.
        /// </summary>
        public float TotalVolume => volume * amount;

        /// <summary>
        /// Create a material instance using parameters.
        /// </summary>
        public KhemistryMaterialInstance(KhemistryMaterial material, string shape, string size, float volume, Dictionary<string, string> parameters)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            // Assign values
            this.material = material;
            this.shape = shape;
            this.size = size;
            this.volume = volume;

            // Apply default parameters
            this.parameters = new Dictionary<string, string>(material.parameters);  // Dict constructor makes a copy instead of a reference

            // Check shape validity
            if (!material.shapes.Contains(shape))
                KShared.LogError("Material instance of material " + material.name + " has an invalid shape " + shape + "!", "KhemistryMaterialInstance/constructor");

            // Set keys that exist to the passed parameters values
            foreach (string key in (parameters ?? new Dictionary<string, string>()).Keys)
                if (this.parameters.ContainsKey(key))
                    this.parameters[key] = parameters[key];
                else
                    KShared.LogError("Material instance of material " + material.name + " has an invalid parameter " + key + " with value " + parameters[key] + "!", "KhemistryMaterialInstance/constructor");

            // Derive derivable parameters
            UpdateParams("KhemistryMaterialInstance/constructor");
        }

        /// <summary>
        /// Create a copy of an existing material instance.
        /// </summary>
        public KhemistryMaterialInstance(KhemistryMaterialInstance matInst)
        {
            this.material = matInst.material;
            this.shape = matInst.shape;
            this.size = matInst.size;
            this.volume = matInst.volume;
            this.amount = matInst.amount;
            this.parameters = new Dictionary<string, string>(matInst.parameters);

            UpdateParams("KhemistryMaterialInstance/constructor(matInst)");
        }

        /// <summary>
        /// Gets the material-related variable list to use in <see cref="KMathExpr"/> expressions.
        /// This includes:
        /// <list type="bullet">Parameters</list>
        /// <list type="bullet">Amount</list>
        /// <list type="bullet">Volume</list>
        /// <list type="bullet">Any additional ones provided in the argument</list>
        /// </summary>
        /// <param name="additional"></param>
        /// <returns>The list of variables to use in <see cref="KMathExpr.TryEvaluate(string, out double, out string, Dictionary{string, string})"/>.</returns>
        public Dictionary<string, string> GetVariableList(Dictionary<string, string> additional = null)
        {
            return BuildVariableList(parameters, amount, additional);
        }

        private Dictionary<string, string> BuildVariableList(Dictionary<string, string> sourceParameters,
            int sourceAmount, Dictionary<string, string> additional = null)
        {
            Dictionary<string, string> variables = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in sourceParameters)
                variables[pair.Key] = pair.Value;

            // These names are reserved for instance properties and intentionally override params.
            variables["amount"] = sourceAmount.ToString(CultureInfo.InvariantCulture);
            variables["volume"] = volume.ToString("R", CultureInfo.InvariantCulture);

            if (additional != null)
                foreach (KeyValuePair<string, string> pair in additional)
                    variables[pair.Key] = pair.Value;

            return variables;
        }

        private static void ReplaceDictionary(Dictionary<string, string> destination,
            Dictionary<string, string> source)
        {
            destination.Clear();
            foreach (KeyValuePair<string, string> pair in source)
                destination[pair.Key] = pair.Value;
        }

        private void ReplaceParameters(Dictionary<string, string> replacement)
        {
            ReplaceDictionary(parameters, replacement);
        }

        private bool TryCalculateDerivedParameters(Dictionary<string, string> sourceParameters,
            int sourceAmount, out Dictionary<string, string> updated, out string error)
        {
            updated = new Dictionary<string, string>(sourceParameters);
            error = null;

            List<string> pending = material.parameters.Keys
                .Where(material.IsDerivedParameter)
                .ToList();
            foreach (string derivedParameter in pending)
                updated.Remove(derivedParameter);

            Dictionary<string, string> latestErrors = new Dictionary<string, string>();
            while (pending.Count > 0)
            {
                bool madeProgress = false;
                foreach (string parameterName in pending.ToList())
                {
                    string expression = material.GetDerivationExpression(parameterName);
                    if (!KMathExpr.TryEvaluate(expression, out double value, out string evaluationError,
                            BuildVariableList(updated, sourceAmount)))
                    {
                        latestErrors[parameterName] = evaluationError;
                        continue;
                    }

                    updated[parameterName] = value.ToString("R", CultureInfo.InvariantCulture);
                    latestErrors.Remove(parameterName);
                    pending.Remove(parameterName);
                    madeProgress = true;
                }

                if (madeProgress) continue;

                error = string.Join("; ", pending.Select(parameterName =>
                    parameterName + ": " + (latestErrors.TryGetValue(parameterName, out string value)
                        ? value : "unresolved dependency")));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Derive all parameters on the material.
        /// The derive formula is taken from the default material.
        /// </summary>
        /// <returns>Was the operation successful or not.</returns>
        public bool DeriveAllParameters()
        {
            if (!TryCalculateDerivedParameters(parameters, amount,
                    out Dictionary<string, string> updated, out string error))
            {
                if (!string.Equals(_lastDerivedParameterError, error, StringComparison.Ordinal))
                {
                    KShared.LogError("Could not derive material parameters: " + error,
                        "KhemistryMaterialInstance/DeriveAllParameters");
                    _lastDerivedParameterError = error;
                }
                return false;
            }

            ReplaceParameters(updated);
            _lastDerivedParameterError = null;
            return true;
        }

        private static bool ParameterValuesEqual(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal)) return true;
            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double leftNumber)
                || !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double rightNumber))
                return false;
            if (leftNumber.Equals(rightNumber)) return true;
            if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber)
                || double.IsInfinity(leftNumber) || double.IsInfinity(rightNumber))
                return false;

            double scale = Math.Max(1.0, Math.Max(Math.Abs(leftNumber), Math.Abs(rightNumber)));
            return Math.Abs(leftNumber - rightNumber) <= scale * 1e-12;
        }

        /// <summary>
        /// Checks if it is possible to merge the amount of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// For this to be true, they must be exactly the same except for the amount.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to test merging for.</param>
        /// <returns>If possible to merge the two <see cref="KhemistryMaterialInstance"/>.</returns>
        public bool CanMerge(KhemistryMaterialInstance other)
        {
            if (other == null || material == null || other.material == null
                || amount <= 0 || other.amount <= 0 || (long)amount + other.amount > int.MaxValue)
                return false;

            if (shape == other.shape
                && size == other.size
                && Math.Abs(volume - other.volume) <= 1e-7f
                && material.name == other.material.name
                && parameters.Count == other.parameters.Count)
            {
                if (!TryCalculateDerivedParameters(parameters, amount,
                        out Dictionary<string, string> currentParameters, out _)
                    || !other.TryCalculateDerivedParameters(other.parameters, other.amount,
                        out Dictionary<string, string> otherParameters, out _))
                    return false;

                foreach (string key in material.parameters.Keys)
                {
                    if (!currentParameters.ContainsKey(key) || !otherParameters.ContainsKey(key))
                        return false;
                    if (material.IsDerivedParameter(key)) continue;
                    if (!ParameterValuesEqual(otherParameters[key], currentParameters[key]))
                        return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Merge the amount of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to merge.</param>
        /// <returns>If merging succeeded or not.</returns>
        public bool Merge(KhemistryMaterialInstance other)
        {
            if (CanMerge(other))
            {
                int mergedAmount = amount + other.amount;
                if (!TryCalculateDerivedParameters(parameters, mergedAmount,
                        out Dictionary<string, string> mergedParameters, out string error))
                {
                    KShared.LogError("Could not update derived parameters after merging: " + error,
                        "KhemistryMaterialInstance/Merge");
                    return false;
                }

                ReplaceParameters(mergedParameters);
                amount = mergedAmount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if it is possible to merge another <see cref="KhemistryMaterialInstance"/> into this one with contamination.
        /// This is usually forced and passes all the time.
        /// For this to be true, they must be exactly the same except for the parameters.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to test contaminated merging for.</param>
        /// <returns>If possible to contaminated merge the two <see cref="KhemistryMaterialInstance"/>.</returns>
        public bool CanContaminatedMerge(KhemistryMaterialInstance other)
        {
            if (other == null || material == null || other.material == null
                || amount <= 0 || other.amount <= 0 || (long)amount + other.amount > int.MaxValue)
                return false;

            if (shape != other.shape || size != other.size
                || Math.Abs(volume - other.volume) > 1e-7f
                || material.name != other.material.name
                || parameters.Count != material.parameters.Count
                || other.parameters.Count != material.parameters.Count)
                return false;

            foreach (string parameterName in material.parameters.Keys)
            {
                if (!parameters.ContainsKey(parameterName) || !other.parameters.ContainsKey(parameterName))
                    return false;
                if (material.IsDerivedParameter(parameterName)) continue;
                if (!material.parameterMergers.ContainsKey(parameterName)) return false;

                string currentValue = parameters[parameterName];
                string otherValue = other.parameters[parameterName];
                bool currentNumeric = double.TryParse(currentValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
                bool otherNumeric = double.TryParse(otherValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
                if (currentNumeric != otherNumeric) return false;
                if (!currentNumeric && !string.Equals(currentValue, otherValue, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Merge another <see cref="KhemistryMaterialInstance"/> into this one with contamination.
        /// This means that parameters will be combined together using an equation.
        /// Variables available in the equation:
        /// <list type="bullet">Parameters (this material)</list>
        /// <list type="bullet">Amount (this material)</list>
        /// <list type="bullet">Volume (this material)</list>
        /// <list type="bullet">Parameters (other material, end with O)</list>
        /// <list type="bullet">Amount (other material, amountO)</list>
        /// <list type="bullet">Volume (other material, volumeO)</list>
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to try merging.</param>
        /// <returns>If the <see cref="KhemistryMaterialInstance"/> was merged into this one.</returns>
        public bool ContaminatedMerge(KhemistryMaterialInstance other)
        {
            if (!CanContaminatedMerge(other)) return false;

            if (!TryCalculateDerivedParameters(parameters, amount,
                    out Dictionary<string, string> currentSnapshot, out string currentDerivationError))
                return FailContaminatedMerge("Could not derive the receiving material: " + currentDerivationError);
            if (!other.TryCalculateDerivedParameters(other.parameters, other.amount,
                    out Dictionary<string, string> otherSnapshot, out string otherDerivationError))
                return FailContaminatedMerge("Could not derive the incoming material: " + otherDerivationError);

            Dictionary<string, string> otherVariables = other.BuildVariableList(otherSnapshot, other.amount)
                .ToDictionary(pair => pair.Key + "O", pair => pair.Value);
            Dictionary<string, string> mergeVariables = BuildVariableList(
                currentSnapshot, amount, otherVariables);
            Dictionary<string, string> mergedParameters = new Dictionary<string, string>(currentSnapshot);
            int mergedAmount = amount + other.amount;

            foreach (string parameterName in material.parameters.Keys)
            {
                if (material.IsDerivedParameter(parameterName)) continue;

                string currentValue = currentSnapshot[parameterName];
                string otherValue = otherSnapshot[parameterName];
                bool currentNumeric = double.TryParse(currentValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
                bool otherNumeric = double.TryParse(otherValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
                if (!currentNumeric && !otherNumeric)
                {
                    // Equal non-numeric values carry through unchanged; differing ones were
                    // rejected by CanContaminatedMerge because KMathExpr is numeric-only.
                    mergedParameters[parameterName] = currentValue;
                    continue;
                }

                string expression = material.parameterMergers[parameterName];
                if (!KMathExpr.TryEvaluate(expression, out double mergedValue, out string mergeError,
                        mergeVariables))
                    return FailContaminatedMerge("Parameter \"" + parameterName + "\": " + mergeError);
                if (double.IsNaN(mergedValue) || double.IsInfinity(mergedValue))
                    return FailContaminatedMerge("Parameter \"" + parameterName
                        + "\" produced a non-finite value.");

                mergedParameters[parameterName] = mergedValue.ToString("R", CultureInfo.InvariantCulture);
            }

            if (!TryCalculateDerivedParameters(mergedParameters, mergedAmount,
                    out Dictionary<string, string> finalParameters, out string derivationError))
                return FailContaminatedMerge("Could not derive the merged material: " + derivationError);

            ReplaceParameters(finalParameters);
            amount = mergedAmount;
            _lastContaminatedMergeError = null;
            return true;
        }

        private bool FailContaminatedMerge(string error)
        {
            if (!string.Equals(_lastContaminatedMergeError, error, StringComparison.Ordinal))
            {
                KShared.LogError(error, "KhemistryMaterialInstance/ContaminatedMerge");
                _lastContaminatedMergeError = error;
            }
            return false;
        }

        /// <summary>
        /// Serializes this material instance for PartModule save data.
        /// </summary>
        public ConfigNode ToConfigNode(string nodeName = "STORED_MATERIAL")
        {
            // Update parameters before saving them
            UpdateParams("KhemistryMaterialInstance/ToConfigNode");
            
            ConfigNode node = new ConfigNode(nodeName);
            node.AddValue("name", material?.name ?? "");
            node.AddValue("shape", shape ?? "");
            node.AddValue("size", size ?? "");
            node.AddValue("volume", volume.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("amount", amount.ToString(CultureInfo.InvariantCulture));

            ConfigNode paramsNode = new ConfigNode("PARAMS");
            foreach (KeyValuePair<string, string> parameter in parameters)
                paramsNode.AddValue(parameter.Key, parameter.Value);
            node.AddNode(paramsNode);
            return node;
        }

        /// <summary>Restores an instance from PartModule save data.</summary>
        public static bool TryFromConfigNode(ConfigNode node, out KhemistryMaterialInstance instance,
            string logContext = "KhemistryMaterialInstance/TryFromConfigNode")
        {
            instance = null;
            if (node == null) return false;

            string materialName = node.GetValue("name");
            KhemistryMaterial definition = KShared.Instance?.materialList
                .FirstOrDefault(m => m.name == materialName);
            if (definition == null)
            {
                KShared.LogError("Saved material \"" + materialName + "\" has no loaded KHEMISTRY_MATERIAL definition.", logContext);
                return false;
            }

            if (!float.TryParse(node.GetValue("volume"), NumberStyles.Float, CultureInfo.InvariantCulture, out float volume)
                || float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0f)
            {
                KShared.LogError("Saved material \"" + materialName + "\" has an invalid per-unit volume.", logContext);
                return false;
            }

            if (!int.TryParse(node.GetValue("amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount)
                || amount <= 0)
            {
                KShared.LogError("Saved material \"" + materialName + "\" has an invalid amount.", logContext);
                return false;
            }

            Dictionary<string, string> savedParameters = node.HasNode("PARAMS")
                ? KShared.NodeToDictionary(node.GetNode("PARAMS"))
                : new Dictionary<string, string>();

            instance = new KhemistryMaterialInstance(
                definition, node.GetValue("shape"), node.GetValue("size"), volume, savedParameters)
            {
                amount = amount
            };
            // Derived expressions may use amount, so recalculate after restoring the saved amount.
            instance.UpdateParams(logContext);
            return true;
        }
    }

    /// <summary>
    /// A material name allowed into a container (e.g. MATERIAL_SUIT_CELL's ALLOWED_MATERIAL),
    /// restricted to a set of supported shapes and, optionally, PARAM_REQUIREMENTS that a
    /// candidate material instance's parameters must satisfy.
    /// </summary>
    public class KhemistryAllowedMaterial
    {
        public string name = null;
        public List<string> supportedShapes = new List<string>();

        // paramName -> list of comparison expressions (OR'd together). Different param names
        // are AND'd against each other.
        public Dictionary<string, List<string>> paramRequirements = new Dictionary<string, List<string>>();

        public KhemistryAllowedMaterial(ConfigNode node)
        {
            name = KShared.GetStrValueFromCFG(node, "name", null);

            if (node.HasNode("SUPPORTED_SHAPES"))
                foreach (string s in node.GetNode("SUPPORTED_SHAPES").GetValues("name"))
                    supportedShapes.Add(s.Trim());

            if (node.HasNode("PARAM_REQUIREMENTS"))
            {
                foreach (ConfigNode.Value v in node.GetNode("PARAM_REQUIREMENTS").values)
                {
                    if (!paramRequirements.TryGetValue(v.name, out List<string> comparisons))
                    {
                        comparisons = new List<string>();
                        paramRequirements[v.name] = comparisons;
                    }
                    comparisons.Add(v.value);
                }
            }
        }

        /// <summary>
        /// Whether the given material instance is accepted: its material name and shape (if
        /// SUPPORTED_SHAPES is non-empty) must match, and every PARAM_REQUIREMENTS param name
        /// must have a value in the instance that satisfies at least one of that name's
        /// comparison expressions.
        /// </summary>
        public bool Matches(KhemistryMaterialInstance instance)
        {
            if (instance?.material == null || instance.material.name != name) return false;
            if (supportedShapes.Count > 0 && !supportedShapes.Contains(instance.shape)) return false;

            instance.UpdateParams("KhemistryAllowedMaterial/Matches");
            foreach (var kv in paramRequirements)
            {
                if (!instance.parameters.TryGetValue(kv.Key, out string paramValue)) return false;

                bool anyPassed = false;
                foreach (string comparison in kv.Value)
                {
                    if (KShared.EvaluateParamComparison(paramValue, comparison))
                    {
                        anyPassed = true;
                        break;
                    }
                }
                if (!anyPassed) return false;
            }

            return true;
        }
    }
}
