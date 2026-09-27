using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khemistry;

namespace KhemistryConstructionOverhaul
{
    internal static partial class KhemistryConstructionMaterials
    {
        internal static KhemistryISRURecipe.ResourceInputMaterial CopyRequirement(
            KhemistryISRURecipe.ResourceInputMaterial source)
        {
            return new KhemistryISRURecipe.ResourceInputMaterial
            {
                id = null,
                name = source.name,
                shape = source.shape,
                size = source.size,
                usesParams = source.usesParams,
                parameters = source.parameters == null
                    ? new List<KeyValuePair<string, string>>()
                    : source.parameters.ToList(),
                amount = source.amount
            };
        }

        internal static bool TryParseCost(ConfigNode node, string partName,
            out KhemistryISRURecipe.ResourceInputMaterial requirement)
        {
            requirement = default;
            string context = "KhemistryPart/OnLoad";
            if (node == null)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a null MATERIAL_COST node; construction will be blocked.", context);
                return false;
            }

            if (node.HasValue("id"))
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a MATERIAL_COST with an id. MATERIAL_COST does not support ids; "
                    + "construction will be blocked.", context);
                return false;
            }

            string name = node.GetValue("name")?.Trim();
            string shape = node.GetValue("shape")?.Trim();
            string size = node.GetValue("size")?.Trim();
            int amount = 1;
            bool validAmount = !node.HasValue("amount")
                || int.TryParse(node.GetValue("amount"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out amount);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(shape)
                || string.IsNullOrEmpty(size) || !validAmount || amount <= 0)
            {
                KShared.LogError("Part \"" + partName
                    + "\" has a MATERIAL_COST that requires non-empty name/shape/size and "
                    + "an integer amount greater than zero; construction will be blocked.", context);
                return false;
            }

            bool hasParameterRequirements = node.HasNode("PARAM_REQUIREMENTS");
            bool hasParamsAlias = node.HasNode("PARAMS");
            if (hasParameterRequirements && hasParamsAlias)
            {
                KShared.LogError("Part \"" + partName + "\": MATERIAL_COST \"" + name
                    + "\" cannot contain both PARAM_REQUIREMENTS and PARAMS; construction "
                    + "will be blocked.", context);
                return false;
            }

            ConfigNode parameterNode = hasParameterRequirements
                ? node.GetNode("PARAM_REQUIREMENTS") : node.GetNode("PARAMS");
            var parameters = new List<KeyValuePair<string, string>>();
            if (parameterNode != null)
                foreach (ConfigNode.Value value in parameterNode.values)
                    parameters.Add(new KeyValuePair<string, string>(value.name, value.value));

            requirement = new KhemistryISRURecipe.ResourceInputMaterial
            {
                id = null,
                name = name,
                shape = shape,
                size = size,
                usesParams = parameterNode != null,
                parameters = parameters,
                amount = amount
            };
            return true;
        }
    }
}
