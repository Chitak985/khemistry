using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        [KSPField(isPersistant = true)] public bool autoMaintenanceFixing = false;
        [KSPField(guiActive = false, guiName = "Maintenance", groupName = "khemistryisru")]
        public string maintenanceDisplay = "";
        private readonly Dictionary<string, MaintenanceState> _maintenanceStates =
            new Dictionary<string, MaintenanceState>(StringComparer.Ordinal);
        private double _maintenanceRuntime;

        private MaintenanceState MaintenanceFor(MaintenanceDefinition definition)
        {
            if (!_maintenanceStates.TryGetValue(definition.name, out MaintenanceState saved))
            {
                saved = new MaintenanceState { lastUniversalTime = Planetarium.GetUniversalTime() };
                _maintenanceStates.Add(definition.name, saved);
            }
            return saved;
        }

        private IEnumerable<MaintenanceDefinition> ActiveMaintenance
            => _activeRecipe?.maintenance ?? Enumerable.Empty<MaintenanceDefinition>();

        private KhemistryISRUBiomeConfig GetEffectiveBiomeConfig()
        {
            if (_activeRecipe == null || _runtimeData == null) return null;
            var biome = _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome);
            if (biome == null || _activeRecipe.maintenance.Count == 0) return biome;
            return biome.WithMaintenance(ActiveMaintenance.Select(d => MaintenanceFor(d).Active(d))
                .Where(s => s != null));
        }

        private bool HasRepairableMaintenance => ActiveMaintenance.Any(d =>
            MaintenanceFor(d).Active(d)?.canFix == true);

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Fix maintenance",
            groupName = "khemistryisru", guiActiveUnfocused = false, unfocusedRange = 7f)]
        public void FixMaintenance()
        {
            if (!BeginMaintenanceRepairs())
                ScreenMessages.PostScreenMessage("No fixable maintenance issue is active.", 4f,
                    ScreenMessageStyle.UPPER_CENTER);
            UpdateMaintenanceDisplay();
        }

        private bool BeginMaintenanceRepairs()
        {
            bool any = false;
            foreach (var definition in ActiveMaintenance)
            {
                var saved = MaintenanceFor(definition);
                if (saved.Active(definition)?.canFix != true) continue;
                saved.manualRepair = true;
                any = true;
            }
            return any;
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false,
            guiName = "Auto maintenance fixing: Off", groupName = "khemistryisru",
            guiActiveUnfocused = false, unfocusedRange = 7f)]
        public void ToggleAutoMaintenanceFixing()
        {
            autoMaintenanceFixing = !autoMaintenanceFixing;
            UpdateMaintenanceDisplay();
        }

        private void UpdateMaintenanceDisplay()
        {
            var statuses = new List<string>();
            foreach (var definition in ActiveMaintenance)
            {
                var saved = MaintenanceFor(definition);
                var stage = saved.Active(definition);
                string status = stage?.status ?? definition.statusOptimal;
                if (stage != null && !stage.canFix) status += " (cannot be fixed)";
                else if (stage != null && (saved.manualRepair || autoMaintenanceFixing && stage.autoFix))
                    status += " (" + saved.repairProgress.ToString("F1") + "/"
                        + stage.fixTime.ToString("F1") + " s; " + saved.repairStatus + ")";
                statuses.Add(definition.name + ": " + status);
            }
            maintenanceDisplay = string.Join("; ", statuses);
            bool visible = statuses.Count > 0;
            Fields[nameof(maintenanceDisplay)].guiActive = visible && _controlsShowPAW;
            Fields[nameof(maintenanceDisplay)].guiActiveUnfocused = visible && _controlsShowEVA;
            Fields[nameof(maintenanceDisplay)].guiUnfocusedRange = _maxDisplayDistance;
            ApplyShowRule(Events[nameof(FixMaintenance)], HasRepairableMaintenance && _controlsShowPAW,
                HasRepairableMaintenance && _controlsShowEVA);
            Events[nameof(ToggleAutoMaintenanceFixing)].guiName = "Auto maintenance fixing: "
                + (autoMaintenanceFixing ? "On" : "Off");
            ApplyShowRule(Events[nameof(ToggleAutoMaintenanceFixing)], visible && _controlsShowPAW,
                visible && _controlsShowEVA);
            Events[nameof(FixMaintenance)].unfocusedRange = _maxInteractionDistance;
            Events[nameof(ToggleAutoMaintenanceFixing)].unfocusedRange = _maxInteractionDistance;
        }

        private void TickMaintenance(double dt)
        {
            double now = Planetarium.GetUniversalTime();
            foreach (var definition in ActiveMaintenance)
            {
                var saved = MaintenanceFor(definition);
                double elapsed = saved.lastUniversalTime < 0 ? dt
                    : Math.Max(0, now - saved.lastUniversalTime);
                saved.lastUniversalTime = now;
                var before = saved.Active(definition);
                saved.Advance(definition, elapsed, _maintenanceRuntime, () => UnityEngine.Random.value);
                var stage = saved.Active(definition);
                if (stage != before) ClearParallaxTargetCache();
                if (stage == null || !stage.canFix
                    || !(saved.manualRepair || autoMaintenanceFixing && stage.autoFix)) continue;
                if (!MaintenanceFixersPresent(stage))
                {
                    saved.repairStatus = "missing fixers";
                    continue;
                }
                if (!MaintenanceResourcesPresent(stage, out string missing))
                {
                    saved.repairStatus = "missing " + missing;
                    continue;
                }
                saved.repairStatus = "repairing";
                // Never retroactively repair while unloaded. Supplies/crew must be checked live.
                // A newly activated problem begins repair on the next tick.
                if (stage != before) continue;
                saved.repairProgress = Math.Min(stage.fixTime, saved.repairProgress + dt);
                if (saved.repairProgress < stage.fixTime) continue;
                var names = stage.resources.Keys.ToList();
                if (!ConsumeVesselResources(names, names.Select(n => stage.resources[n]).ToList(), 1))
                {
                    saved.repairStatus = "resources unavailable";
                    continue;
                }
                saved.Repair(definition);
                ClearParallaxTargetCache();
            }
            _maintenanceRuntime = 0;
            UpdateMaintenanceDisplay();
        }

        private bool MaintenanceFixersPresent(MaintenanceStage stage)
        {
            var context = GetPowerfailContextPart();
            var craft = GetProcessingVessel();
            if (context == null || craft == null) return false;
            int[] eva = new int[3], crew = new int[3];
            Action<IEnumerable<ProtoCrewMember>, int[]> count = (people, counts) =>
            {
                foreach (var person in people)
                {
                    int i = person.trait == "Engineer" ? 0 : person.trait == "Pilot" ? 1
                        : person.trait == "Scientist" ? 2 : -1;
                    if (i >= 0) counts[i]++;
                }
            };
            if (!craft.isEVA)
                count(stage.fixersCrewSamePart ? (IEnumerable<ProtoCrewMember>)context.protoModuleCrew
                    : craft.GetVesselCrew(), crew);
            // EVA converters have unlimited PAW range, but that must not count distant Kerbals.
            double range = IsEVAModuleType() ? 7 : _maxInteractionDistance;
            foreach (var candidate in FlightGlobals.Vessels)
                if (candidate != null && candidate.loaded && candidate.isEVA
                    && Vector3d.Distance(candidate.transform.position, context.transform.position) <= range)
                    count(candidate.GetVesselCrew(), eva);
            return Enumerable.Range(0, 3).All(i => eva[i] >= stage.evaFixers[i]
                && crew[i] >= stage.crewFixers[i]);
        }

        private double MaintenanceAvailable(string name, bool vesselWide)
        {
            double cells = IsEVAModuleType() && _kerbalHost != null
                ? _kerbalHost.GetProcessorResourceAmount(_inventoryStoredPart, name,
                    moduleType == "kerbalEVA" || useSuitCell) : 0;
            if (!vesselWide && IsEVAModuleType()) return cells;
            var craft = GetProcessingVessel();
            var origin = GetPowerfailContextPart();
            var definition = PartResourceLibrary.Instance?.GetDefinition(name);
            if (craft == null || origin == null || definition == null) return cells;
            return cells + craft.parts.Where(p => p != null && (vesselWide || p == origin
                    || origin.CanCrossfeed(p, definition.id, ResourceFlowMode.STAGE_PRIORITY_FLOW)))
                .SelectMany(KhemistryResourceNetwork.GetEndpoints)
                .Where(e => e.resourceName == name && (vesselWide || e.cell == null))
                .Sum(e => e.amount);
        }

        private bool MaintenanceResourcesPresent(MaintenanceStage stage, out string missing)
        {
            foreach (var required in stage.resources)
                if (MaintenanceAvailable(required.Key, false) + 1e-9 < required.Value)
                { missing = required.Key; return false; }
            foreach (var tool in stage.tools)
            {
                // If a resource is both supply and tool, the tool quantity must remain afterward.
                double needed = tool.Value + (stage.resources.TryGetValue(tool.Key, out double used) ? used : 0);
                if (MaintenanceAvailable(tool.Key, true) + 1e-9 < needed)
                { missing = tool.Key; return false; }
            }
            missing = null;
            return true;
        }

        private void LoadMaintenanceState(ConfigNode node)
        {
            _maintenanceStates.Clear();
            _maintenanceRuntime = 0;
            // Explicit reset is necessary when the same prefab serves different inventory parts.
            autoMaintenanceFixing = bool.TryParse(node?.GetValue(nameof(autoMaintenanceFixing)), out bool enabled) && enabled;
            if (node == null) return;
            foreach (ConfigNode entry in node.GetNodes("MAINTENANCE_STATE"))
            {
                string name = entry.GetValue("name");
                if (string.IsNullOrEmpty(name)) continue;
                var saved = new MaintenanceState
                {
                    activeOrder = int.TryParse(entry.GetValue("activeOrder"), out int order) ? (int?)order : null,
                    repairProgress = ReadMaintenanceStateNumber(entry, "repairProgress", 0),
                    lastUniversalTime = ReadMaintenanceStateNumber(entry, "lastUniversalTime", -1),
                    manualRepair = bool.TryParse(entry.GetValue("manualRepair"), out bool repair) && repair
                };
                foreach (ConfigNode timer in entry.GetNodes("TIMER"))
                    if (int.TryParse(timer.GetValue("order"), out int timerOrder))
                        saved.elapsed[timerOrder] = ReadMaintenanceStateNumber(timer, "elapsed", 0);
                foreach (string value in entry.GetValues("occurredOrder"))
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int occurred))
                        saved.occurredOrders.Add(occurred);
                // Older saves have no history. Only the current stage is known to have
                // occurred: earlier stages may have been skipped, so do not invent them.
                if (saved.activeOrder.HasValue) saved.occurredOrders.Add(saved.activeOrder.Value);
                _maintenanceStates[name] = saved;
            }
        }

        private static double ReadMaintenanceStateNumber(ConfigNode node, string key, double fallback)
            => double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && MaintenanceState.Finite(value) && value >= 0 ? value : fallback;

        private void SaveMaintenanceState(ConfigNode node)
        {
            while (node.HasNode("MAINTENANCE_STATE")) node.RemoveNode("MAINTENANCE_STATE");
            foreach (var item in _maintenanceStates)
            {
                var entry = node.AddNode("MAINTENANCE_STATE");
                entry.AddValue("name", item.Key);
                if (item.Value.activeOrder.HasValue) entry.AddValue("activeOrder", item.Value.activeOrder.Value);
                foreach (int occurred in item.Value.occurredOrders.OrderByDescending(order => order))
                    entry.AddValue("occurredOrder", occurred.ToString(CultureInfo.InvariantCulture));
                entry.AddValue("repairProgress", item.Value.repairProgress.ToString("R", CultureInfo.InvariantCulture));
                entry.AddValue("manualRepair", item.Value.manualRepair);
                entry.AddValue("lastUniversalTime", item.Value.lastUniversalTime.ToString("R", CultureInfo.InvariantCulture));
                foreach (var timer in item.Value.elapsed)
                {
                    var timerNode = entry.AddNode("TIMER");
                    timerNode.AddValue("order", timer.Key);
                    timerNode.AddValue("elapsed", timer.Value.ToString("R", CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
