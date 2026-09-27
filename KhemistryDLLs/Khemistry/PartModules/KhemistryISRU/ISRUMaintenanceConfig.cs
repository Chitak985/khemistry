using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRURecipe
    {
        /// <summary>List of maintenance definitions used by the recipe.</summary>
        public readonly List<MaintenanceDefinition> maintenance = new List<MaintenanceDefinition>();

        /// <summary>Load all MAINTENANCE nodes in a <see cref="ConfigNode"/></summary>
        private bool LoadMaintenance(ConfigNode node)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode entry in node.GetNodes("MAINTENANCE"))
            {
                try  // Ignore exceptions here, they're all caught later
                {
                    // Get name and optimal status text
                    MaintenanceDefinition definition = new MaintenanceDefinition
                    {
                        name = RequiredMaintenanceText(entry, "name"),
                        statusOptimal = RequiredMaintenanceText(entry, "statusOptimal")
                    };
                    if (!names.Add(definition.name))
                        throw new FormatException("Duplicate maintenance name");

                    // Get maintenance stages
                    HashSet<int> orders = new HashSet<int>();
                    foreach (ConfigNode stageNode in entry.GetNodes("STAGE"))
                    {
                        // Get stage order
                        double order = MaintenanceNumber(stageNode, "order", null);
                        if (order != Math.Truncate(order) || order < int.MinValue || order > int.MaxValue)
                            throw new FormatException("STAGE order must be an integer");
                        if (!orders.Add((int)order))
                            throw new FormatException($"Duplicate STAGE order {(int)order}; every stage within a MAINTENANCE type must have a unique order");

                        // Load the maintenance stage
                        MaintenanceStage stage = new MaintenanceStage
                        {
                            order = (int)order,
                            status = RequiredMaintenanceText(stageNode, "status"),
                            time = MaintenanceNumber(stageNode, "time", null, double.Epsilon),
                            chance = MaintenanceNumber(stageNode, "chance", 1, 0, 1),
                            timeIsRuntime = MaintenanceBool(stageNode, "timeIsRuntime", false),
                            ignoreOrder = MaintenanceBool(stageNode, "ignoreOrder", false),
                            canFix = MaintenanceBool(stageNode, "canFix", true)
                        };

                        // Load stage order conditions
                        foreach (string value in stageNode.GetValues("requiredOrdersAND"))
                            stage.requiredOrdersAND.UnionWith(ReadRequiredOrders(value, "requiredOrdersAND"));
                        foreach (string value in stageNode.GetValues("requiredOrdersOR"))
                            stage.requiredOrdersOR.Add(ReadRequiredOrders(value, "requiredOrdersOR"));
                        foreach (string value in stageNode.GetValues("requiredOrderNOT"))
                            stage.requiredOrderNOT.Add(ReadRequiredOrder(value, "requiredOrderNOT"));
                        foreach (string key in KhemistryISRUBiomeConfig.MultiplierFields.Keys)
                            stage.multipliers[key] = MaintenanceNumber(stageNode, key, 1,
                                key == "speedMul" || key == "passivePeriodMul" ? double.Epsilon : 0);

                        // Load stage fixing data
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

                        // Add maintenance stage
                        definition.stages.Add(stage);
                    }

                    // Ensure at least one stage loaded
                    if (definition.stages.Count == 0)
                        throw new FormatException("MAINTENANCE needs at least one STAGE");

                    // Verify stage orders in order conditions
                    foreach (MaintenanceStage stage in definition.stages)
                        foreach (int required in stage.requiredOrdersAND
                            .Concat(stage.requiredOrdersOR.SelectMany(group => group))
                            .Concat(stage.requiredOrderNOT))
                            if (!orders.Contains(required))
                                throw new FormatException($"STAGE {stage.order} refers to an undefined order {required} in this MAINTENANCE type");

                    // Finish
                    definition.stages.Sort((a, b) => b.order.CompareTo(a.order));
                    maintenance.Add(definition);
                }
                catch (FormatException ex)
                {
                    KShared.LogError($"Recipe \"{_name}\": Config format error in MAINTENANCE node \"" +
                                     entry.GetValue("name") +
                                     "\": {ex.Message}!", "KhemistryISRURecipe/LoadMaintenance");
                    return false;
                }
                catch (Exception ex)  // Just in case
                {
                    KShared.LogError($"Recipe \"{_name}\": Unknown error in MAINTENANCE node \"" +
                                     entry.GetValue("name") +
                                     "\": {ex.Message}!", "KhemistryISRURecipe/LoadMaintenance");
                    return false;
                }
            }
            return true;
        }

        /// <summary>Get a string required for maintenance.</summary>
        private static string RequiredMaintenanceText(ConfigNode node, string key)
        {
            string value = node.GetValue(key)?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new FormatException(key + " is required");  // Ignore, caught in LoadMaintenance
            return value;
        }

        /// <summary>Get a required maintenance order.</summary>
        private static int ReadRequiredOrder(string value, string key)
        {
            if (!int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
                throw new FormatException($"{key} requires a single integer stage order; got \"{value}\" instead");  // Ignore, caught in LoadMaintenance
            return order;
        }

        /// <summary>Get all required maintenance orders.</summary>
        private static int[] ReadRequiredOrders(string value, string key)
            => (value ?? "").Split(',').Select(token => ReadRequiredOrder(token, key)).ToArray();

        /// <summary>Get a double required for maintenance.</summary>
        private static double MaintenanceNumber(ConfigNode node, string key, double? fallback,
            double min = double.MinValue, double max = double.MaxValue)
        {
            if (!node.HasValue(key) && fallback.HasValue) return fallback.Value;
            if (!double.TryParse(
                    node.GetValue(key),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value) ||
                !MaintenanceState.Finite(value) ||
                value < min || value > max)
                throw new FormatException($"{key} must be a finite number in interval ({min}, {max})");  // Ignore, caught in LoadMaintenance
            return value;
        }

        /// <summary>Get a boolean required for maintenance.</summary>
        private static bool MaintenanceBool(ConfigNode node, string key, bool fallback)
        {
            if (!node.HasValue(key)) return fallback;
            if (!bool.TryParse(node.GetValue(key), out bool value))
                throw new FormatException(key + " must be true or false");  // Ignore, caught in LoadMaintenance
            return value;
        }

        /// <summary>Get an integer required for maintenance.</summary>
        private static int MaintenanceCount(ConfigNode node, string key)
        {
            double count = MaintenanceNumber(node, key, 0, 0, int.MaxValue);
            if (count != Math.Truncate(count))
                throw new FormatException(key + " must be an integer");  // Ignore, caught in LoadMaintenance
            return (int)count;
        }

        /// <summary>Get repair resources required for maintenance.</summary>
        private static void ReadRepairResources(ConfigNode node, string kind, Dictionary<string, double> target)
        {
            foreach (ConfigNode resource in node.GetNodes(kind))
            {
                string name = RequiredMaintenanceText(resource, "name");
                double amount = MaintenanceNumber(resource, "amount", null, double.Epsilon);
                target[name] = amount + (target.TryGetValue(name, out double previous) ? previous : 0);
                if (!MaintenanceState.Finite(target[name]))
                    throw new FormatException(kind + " amount overflows");  // Ignore, caught in LoadMaintenance
            }
        }
    }
}
