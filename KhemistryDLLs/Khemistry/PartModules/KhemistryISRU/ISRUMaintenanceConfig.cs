using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRURecipe
    {
        public readonly List<MaintenanceDefinition> maintenance = new List<MaintenanceDefinition>();

        private bool LoadMaintenance(ConfigNode node)
        {
            bool valid = true;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode entry in node.GetNodes("MAINTENANCE"))
            {
                try
                {
                    var definition = new MaintenanceDefinition
                    {
                        name = RequiredMaintenanceText(entry, "name"),
                        statusOptimal = RequiredMaintenanceText(entry, "statusOptimal")
                    };
                    if (!names.Add(definition.name)) throw new FormatException("duplicate maintenance name");
                    var orders = new HashSet<int>();
                    foreach (ConfigNode stageNode in entry.GetNodes("STAGE"))
                    {
                        double order = MaintenanceNumber(stageNode, "order", null);
                        if (order != Math.Truncate(order) || order < int.MinValue || order > int.MaxValue)
                            throw new FormatException("STAGE order must be an integer");
                        if (!orders.Add((int)order))
                            throw new FormatException("duplicate STAGE order " + (int)order
                                + "; every stage within a MAINTENANCE type must have a unique order");
                        var stage = new MaintenanceStage
                        {
                            order = (int)order,
                            status = RequiredMaintenanceText(stageNode, "status"),
                            time = MaintenanceNumber(stageNode, "time", null, double.Epsilon),
                            chance = MaintenanceNumber(stageNode, "chance", 1, 0, 1),
                            timeIsRuntime = MaintenanceBool(stageNode, "timeIsRuntime", false),
                            ignoreOrder = MaintenanceBool(stageNode, "ignoreOrder", false),
                            canFix = MaintenanceBool(stageNode, "canFix", true)
                        };
                        foreach (string value in stageNode.GetValues("requiredOrdersAND"))
                            stage.requiredOrdersAND.UnionWith(ReadRequiredOrders(value, "requiredOrdersAND"));
                        foreach (string value in stageNode.GetValues("requiredOrdersOR"))
                            stage.requiredOrdersOR.Add(ReadRequiredOrders(value, "requiredOrdersOR"));
                        foreach (string value in stageNode.GetValues("requiredOrderNOT"))
                            stage.requiredOrderNOT.Add(ReadRequiredOrder(value, "requiredOrderNOT"));
                        foreach (string key in KhemistryISRUBiomeConfig.MultiplierFields.Keys)
                            stage.multipliers[key] = MaintenanceNumber(stageNode, key, 1,
                                key == "speedMul" || key == "passivePeriodMul" ? double.Epsilon : 0);
                        if (stage.canFix)
                        {
                            stage.fixTime = MaintenanceNumber(stageNode, "fixTime", 0, 0);
                            stage.autoFix = MaintenanceBool(stageNode, "autoFix", false);
                            stage.fixersCrewSamePart = MaintenanceBool(stageNode, "fixersCrewSamePart", false);
                            string[] traits = { "Engineers", "Pilots", "Scientists" };
                            for (int i = 0; i < traits.Length; i++)
                            {
                                stage.evaFixers[i] = MaintenanceCount(stageNode, "fixers" + traits[i]);
                                stage.crewFixers[i] = MaintenanceCount(stageNode, "fixersCrew" + traits[i]);
                            }
                            ReadRepairResources(stageNode, "RESOURCE_TO_FIX", stage.resources);
                            ReadRepairResources(stageNode, "RESOURCE_TO_FIX_NC", stage.tools);
                        }
                        definition.stages.Add(stage);
                    }
                    if (definition.stages.Count == 0) throw new FormatException("MAINTENANCE needs at least one STAGE");
                    foreach (MaintenanceStage stage in definition.stages)
                        foreach (int required in stage.requiredOrdersAND
                            .Concat(stage.requiredOrdersOR.SelectMany(group => group))
                            .Concat(stage.requiredOrderNOT))
                            if (!orders.Contains(required))
                                throw new FormatException("STAGE " + stage.order
                                    + " refers to undefined order " + required + " in this MAINTENANCE type");
                    definition.stages.Sort((a, b) => b.order.CompareTo(a.order));
                    maintenance.Add(definition);
                }
                catch (FormatException ex)
                {
                    valid = false;
                    KShared.LogError("Recipe \"" + _name + "\": invalid MAINTENANCE \""
                        + entry.GetValue("name") + "\": " + ex.Message, "KhemistryISRURecipe/LoadMaintenance");
                }
            }
            return valid;
        }

        private static string RequiredMaintenanceText(ConfigNode node, string key)
        {
            string value = node.GetValue(key)?.Trim();
            if (string.IsNullOrEmpty(value)) throw new FormatException(key + " is required");
            return value;
        }

        private static int ReadRequiredOrder(string value, string key)
        {
            if (!int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
                throw new FormatException(key + " requires a single integer stage order; got \"" + value + "\"");
            return order;
        }

        private static int[] ReadRequiredOrders(string value, string key)
            => (value ?? "").Split(',').Select(token => ReadRequiredOrder(token, key)).ToArray();

        private static double MaintenanceNumber(ConfigNode node, string key, double? fallback,
            double min = double.MinValue, double max = double.MaxValue)
        {
            if (!node.HasValue(key) && fallback.HasValue) return fallback.Value;
            if (!double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double value) || !MaintenanceState.Finite(value) || value < min || value > max)
                throw new FormatException(key + " must be a finite number in [" + min + ", " + max + "]");
            return value;
        }

        private static bool MaintenanceBool(ConfigNode node, string key, bool fallback)
        {
            if (!node.HasValue(key)) return fallback;
            if (!bool.TryParse(node.GetValue(key), out bool value)) throw new FormatException(key + " must be true or false");
            return value;
        }

        private static int MaintenanceCount(ConfigNode node, string key)
        {
            double count = MaintenanceNumber(node, key, 0, 0, int.MaxValue);
            if (count != Math.Truncate(count)) throw new FormatException(key + " must be an integer");
            return (int)count;
        }

        private static void ReadRepairResources(ConfigNode node, string kind, Dictionary<string, double> target)
        {
            foreach (ConfigNode resource in node.GetNodes(kind))
            {
                string name = RequiredMaintenanceText(resource, "name");
                double amount = MaintenanceNumber(resource, "amount", null, double.Epsilon);
                target[name] = amount + (target.TryGetValue(name, out double previous) ? previous : 0);
                if (!MaintenanceState.Finite(target[name])) throw new FormatException(kind + " amount overflows");
            }
        }
    }
}
