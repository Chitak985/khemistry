using System;
using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    // Kept independent of Unity so timer/order/chance behavior can be tested directly.
    public sealed class MaintenanceStage
    {
        public int order;
        public string status;
        public double time, fixTime, chance = 1;
        public bool timeIsRuntime, ignoreOrder, autoFix, canFix = true;
        public bool fixersCrewSamePart;
        public readonly HashSet<int> requiredOrdersAND = new HashSet<int>();
        public readonly List<int[]> requiredOrdersOR = new List<int[]>();
        public readonly HashSet<int> requiredOrderNOT = new HashSet<int>();
        public readonly int[] evaFixers = new int[3];
        public readonly int[] crewFixers = new int[3];
        public readonly Dictionary<string, double> resources = new Dictionary<string, double>();
        public readonly Dictionary<string, double> tools = new Dictionary<string, double>();
        public readonly Dictionary<string, double> multipliers = new Dictionary<string, double>();

        public bool RequirementsMet(ISet<int> history)
            => requiredOrdersAND.All(history.Contains)
                && requiredOrdersOR.All(group => group.Any(history.Contains))
                && !requiredOrderNOT.Any(history.Contains);
    }

    public sealed class MaintenanceDefinition
    {
        public string name, statusOptimal;
        // Highest order first; the optimal state precedes index 0.
        public readonly List<MaintenanceStage> stages = new List<MaintenanceStage>();
    }

    public sealed class MaintenanceState
    {
        public int? activeOrder;
        public readonly HashSet<int> occurredOrders = new HashSet<int>();
        public readonly Dictionary<int, double> elapsed = new Dictionary<int, double>();
        public double repairProgress;
        public bool manualRepair;
        public double lastUniversalTime = -1;
        public string repairStatus = "";

        public MaintenanceStage Active(MaintenanceDefinition definition)
            => definition.stages.FirstOrDefault(s => s.order == activeOrder);

        public void Activate(MaintenanceStage stage)
        {
            activeOrder = stage.order;
            occurredOrders.Add(stage.order);
            elapsed[stage.order] = 0;
            repairProgress = 0;
            manualRepair = false;
            repairStatus = "";
        }

        public void Repair(MaintenanceDefinition definition)
        {
            int index = definition.stages.FindIndex(s => s.order == activeOrder);
            if (index < 0 || !definition.stages[index].canFix) return;
            activeOrder = index == 0 ? (int?)null : definition.stages[index - 1].order;
            // Repair may activate a predecessor that a parallel failure originally skipped.
            if (activeOrder.HasValue) occurredOrders.Add(activeOrder.Value);
            // Start a fresh interval toward the repaired stage. Other parallel clocks retain
            // their age, so fixing one problem cannot reset unrelated wear.
            elapsed[definition.stages[index].order] = 0;
            repairProgress = 0;
            manualRepair = false;
            repairStatus = "";
        }

        public void Advance(MaintenanceDefinition definition, double wallSeconds,
            double runtimeSeconds, Func<double> random)
        {
            if (!Finite(wallSeconds) || wallSeconds < 0
                || !Finite(runtimeSeconds) || runtimeSeconds < 0) return;
            int current = definition.stages.FindIndex(s => s.order == activeOrder);
            double fraction = 1;
            // Advance to the next timer boundary, then re-evaluate the eligible stages.
            // This handles sequential and parallel timers together without ordering by frame.
            for (int iteration = 0; fraction > 0 && iteration < 1000; iteration++)
            {
                var eligible = new List<MaintenanceStage>();
                double next = fraction;
                foreach (MaintenanceStage stage in definition.stages.Skip(current + 1))
                {
                    if (!stage.ignoreOrder && definition.stages[current + 1] != stage) continue;
                    if (!stage.RequirementsMet(occurredOrders)) continue;
                    double seconds = stage.timeIsRuntime ? runtimeSeconds : wallSeconds;
                    if (seconds <= 0) continue;
                    eligible.Add(stage);
                    double age = elapsed.TryGetValue(stage.order, out double value) ? value : 0;
                    next = Math.Min(next, Math.Max(0, stage.time - age) / seconds);
                }
                if (eligible.Count == 0) break;
                foreach (MaintenanceStage stage in eligible)
                {
                    double age = elapsed.TryGetValue(stage.order, out double value) ? value : 0;
                    elapsed[stage.order] = age + next * (stage.timeIsRuntime ? runtimeSeconds : wallSeconds);
                }
                fraction -= next;
                // Eligibility at this boundary uses the same history for all simultaneous
                // expiries. Record every successful occurrence; latest deterioration wins.
                MaintenanceStage activated = null;
                foreach (MaintenanceStage stage in eligible)
                {
                    if (elapsed[stage.order] + 1e-9 < stage.time) continue;
                    elapsed[stage.order] = 0;
                    if (stage.chance >= 1 || (stage.chance > 0 && random() < stage.chance))
                    {
                        occurredOrders.Add(stage.order);
                        activated = stage;
                    }
                }
                if (activated != null)
                {
                    Activate(activated);
                    current = definition.stages.IndexOf(activated);
                }
                if (next <= 0 && activated == null
                    && eligible.All(s => elapsed[s.order] > 0)) break;
            }
        }

        internal static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
