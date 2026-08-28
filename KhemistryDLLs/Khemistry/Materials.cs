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
        /// If one isn't loaded for a parameter, it will default to (current+new)/2.
        /// The merge equation is a KMathExpr and can include the current parameter and the new parameter with N at the end.
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

            // Set parameter merge expressions (if there are any) from the config
            if (configNode.HasNode("PARAM_MERGING"))
                foreach (string key in configNode.GetNode("PARAMS").values.DistinctNames())
                    parameterMergers.Add(key, configNode.GetNode("PARAMS").GetValue(key));

            // Default merge expressions to averaging
            foreach (string param in parameters.Keys)
                if (!parameterMergers.ContainsKey(param))
                    parameterMergers.Add(param, $"({param}+{param}N)/2");
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

        /// <summary>
        /// Update all derivable parameters.
        /// Should be called before accessing parameters to make sure they are up to date.
        /// </summary>
        public void UpdateParams(string location="KhemistryMaterialInstance/constructor(matInst)")
        {
            if (!DeriveAllParameters())
                KShared.LogError("DeriveAllParamters() has failed with an error and not all parameters were derived!", location);
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
        /// Derive all parameters on the material.
        /// The derive formula is taken from the default material.
        /// </summary>
        /// <returns>Was the operation successful or not.</returns>
        public bool DeriveAllParameters()
        {
            foreach (string param in material.parameters.Keys)
                if (parameters[param].StartsWith("DER"))
                    if (KMathExpr.TryEvaluate(parameters[param].Remove(0, 3), out double val, out string error, parameters))
                        parameters[param] = val.ToString();
                    else
                    {
                        KShared.LogError("An error occured while evaulating parameter " + param + ": " + error, "KhemistryMaterialInstance/DeriveAllParameters");
                        return false;
                    }
            return true;
        }

        /// <summary>
        /// Checks if it is possible to merge the volumes of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// For this to be true, they must be exactly the same except for the volume.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to test merging for.</param>
        /// <returns>If possible to merge the two <see cref="KhemistryMaterialInstance"/>.</returns>
        public bool CanMerge(KhemistryMaterialInstance other)
        {
            if (other == null || material == null || other.material == null) return false;

            if (shape == other.shape && size == other.size
                && Math.Abs(volume - other.volume) <= 1e-7f
                && material.name == other.material.name
                && parameters.Count == other.parameters.Count)
            {
                // Update parameters before checking them
                UpdateParams("KhemistryMaterialInstance/CanMerge");
                other.UpdateParams("KhemistryMaterialInstance/CanMerge, other");
                
                foreach (string key in parameters.Keys)
                {
                    if (!other.parameters.ContainsKey(key))
                        return false;
                    if (other.parameters[key] != parameters[key])
                        return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Merge the volumes of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to merge.</param>
        /// <returns>If merging succeeded or not.</returns>
        public bool Merge(KhemistryMaterialInstance other)
        {
            if (CanMerge(other))
            {
                amount += other.amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Serializes this material instance for PartModule save data.
        /// </summary>
        public ConfigNode ToConfigNode(string nodeName = "STORED_MATERIAL")
        {
            // Update parameters before saving them
            UpdateParams("KhemistryMaterialInstance/CanMerge");
            
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

            // No need to update parameters here as they are saved as already derived

            instance = new KhemistryMaterialInstance(
                definition, node.GetValue("shape"), node.GetValue("size"), volume, savedParameters)
            {
                amount = amount
            };
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

            foreach (var kv in paramRequirements)
            {
                instance.UpdateParams("KhemistryAllowedMaterial/Matches");
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
