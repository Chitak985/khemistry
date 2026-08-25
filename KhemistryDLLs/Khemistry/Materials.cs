using System.Collections.Generic;

namespace Khemistry
{
    /// <summary>
    /// A material usually loaded from configs. It defines its name, allowed shapes, and allowed parameters.
    /// An instance of this material used as a resource is <see cref="KhemistryMaterialInstance"/>.
    /// </summary>
    public class KhemistryMaterial
    {
        public string name = "LOADFAIL";
        public List<string> shapes = new List<string>();
        public Dictionary<string, string> parameters = new Dictionary<string, string>();  // name: default
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
        public float volume = 0f;  // how much one material is in cubic meters
        public int amount = 1;  // how many materials are combined into this one
        public Dictionary<string, string> parameters = new Dictionary<string, string>();

        /// <summary>
        /// Create a material instance using parameters
        /// </summary>
        public KhemistryMaterialInstance(KhemistryMaterial material, string shape, string size, float volume, Dictionary<string, string> parameters)
        {
            // Assign values
            this.material = material;
            this.shape = shape;
            this.size = size;
            this.volume = volume;

            // Apply default parameters
            this.parameters = new Dictionary<string, string>(material.parameters);  // Dict constructor makes a copy instead of a reference

            // Set keys that exist to the passed parameters values
            foreach (string key in parameters.Keys)
                if (this.parameters.ContainsKey(key))
                    this.parameters[key] = parameters[key];
                else
                    KShared.LogError("Material instance of material " + material.name + " has an invalid parameter " + key + " with value " + material.parameters[key] + "!", "KhemistryMaterialInstance/constructor");

            // Check shape validity
            if (!material.shapes.Contains(shape))
                KShared.LogError("Material instance of material " + material.name + " has an invalid shape " + shape + "!", "KhemistryMaterialInstance/constructor");
        }

        /// <summary>
        /// Create a copy of an existing material instance
        /// </summary>
        public KhemistryMaterialInstance(KhemistryMaterialInstance matInst)
        {
            this.material = matInst.material;
            this.shape = matInst.shape;
            this.size = matInst.size;
            this.volume = matInst.volume;
            this.amount = matInst.amount;
            this.parameters = matInst.parameters;
        }

        /// <summary>
        /// Checks if it is possible to merge the volumes of another <see cref="KhemistryMaterialInstance"/> into this one.
        /// For this to be true, they must be exactly the same except for the volume.
        /// </summary>
        /// <param name="other">The other <see cref="KhemistryMaterialInstance"/> to test merging for.</param>
        /// <returns>If possible to merge the two <see cref="KhemistryMaterialInstance"/>.</returns>
        public bool CanMerge(KhemistryMaterialInstance other)
        {
            if (shape == other.shape && size == other.size && volume == other.volume && material.name == other.material.name)
            {
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
        /// Split off some amount of materials off of this one.
        /// Silently errors if the amount is too large or is equal to main material amount
        /// </summary>
        /// <param name="amount">Amount to split off.</param>
        /// <returns>The split-off material.</returns>
        public KhemistryMaterialInstance SplitOff(int amount)
        {
            // Error checking
            if (this.amount == amount)
                KShared.LogError($"Splitting off material would be the same as keeping the main material! (amount equal to this.amount, {amount})", "KhemistryMaterialInstance/SplitOff");
            else if (this.amount < amount)
                KShared.LogError($"Attempting to split off more than possible! (amount greater than this.amount, {this.amount} < {amount})", "KhemistryMaterialInstance/SplitOff");

            // Create the new material
            KhemistryMaterialInstance splitMat = new KhemistryMaterialInstance(this);

            // Set the amount to the split-off
            splitMat.amount = amount;

            // Subtract the amount from the new material
            this.amount -= amount;

            return splitMat;
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
