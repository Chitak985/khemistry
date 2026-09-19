using System;
using System.Linq;
using Khemistry;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string why)
    { assertions++; if (!condition) throw new Exception(why); }
    private static MaintenanceDefinition Definition(params MaintenanceStage[] stages)
    {
        var definition = new MaintenanceDefinition { name = "Screws", statusOptimal = "Like New" };
        definition.stages.AddRange(stages.OrderByDescending(s => s.order));
        return definition;
    }
    private static MaintenanceStage Stage(int order, double time, bool parallel = false, bool runtime = false)
        => new MaintenanceStage { order = order, time = time, ignoreOrder = parallel, timeIsRuntime = runtime, status = "Stage " + order };
    private static ConfigNode Config(out ConfigNode stage)
    {
        var root = new ConfigNode();
        var maintenance = root.AddNode("MAINTENANCE");
        maintenance.AddValue("name", "Screws"); maintenance.AddValue("statusOptimal", "Like New");
        stage = maintenance.AddNode("STAGE");
        stage.AddValue("order", 1); stage.AddValue("status", "Loose"); stage.AddValue("time", 10);
        return root;
    }
    private static void Timers()
    {
        var definition = Definition(Stage(1, 10), Stage(3, 10), Stage(2, 10));
        var state = new MaintenanceState();
        state.Advance(definition, 9, 0, () => 0);
        Check(state.activeOrder == null, "initial state lasts the full first interval");
        state.Advance(definition, 12, 0, () => 0);
        Check(state.activeOrder == 2 && Math.Abs(state.elapsed[1] - 1) < 1e-8, "large ticks preserve sequential elapsed time");
        state.Repair(definition); Check(state.activeOrder == 3, "repair returns to preceding stage");
        state.Repair(definition); Check(state.activeOrder == null, "repair first stage returns to optimal");
        definition = Definition(Stage(3, 100), Stage(2, 100), Stage(1, 5, true));
        state = new MaintenanceState(); state.Advance(definition, 5, 0, () => 0);
        Check(state.activeOrder == 1, "parallel stage jumps ahead");
        state.Repair(definition); Check(state.activeOrder == 2, "parallel repair activates its predecessor even when skipped");
        state.Advance(definition, 4, 0, () => 0); Check(state.activeOrder == 2, "repaired stage timer restarts");
        state.Advance(definition, 1, 0, () => 0); Check(state.activeOrder == 1, "parallel issue can recur");
        definition = Definition(Stage(1, 10, false, true));
        state = new MaintenanceState(); state.Advance(definition, 100, 0, () => 0);
        Check(state.activeOrder == null, "runtime timers do not age while stopped");
        state.Advance(definition, 100, 9, () => 0); Check(state.activeOrder == null, "runtime uses actual work time");
        state.Advance(definition, 1, 1, () => 0); Check(state.activeOrder == 1, "runtime threshold triggers");
        definition.stages[0].canFix = false; state.Repair(definition);
        Check(state.activeOrder == 1, "unfixable stages cannot be repaired");
        definition = Definition(Stage(1, 10)); definition.stages[0].chance = .25;
        state = new MaintenanceState(); state.Advance(definition, 10, 0, () => .75);
        Check(state.activeOrder == null && state.elapsed[1] == 0, "failed chance roll resets timer");
        state.Advance(definition, 10, 0, () => .1); Check(state.activeOrder == 1, "next chance roll can trigger");
        definition.stages[0].chance = 0; state = new MaintenanceState();
        state.Advance(definition, 1000, 0, () => throw new Exception("zero chance should not roll"));
        Check(state.activeOrder == null, "chance zero never activates");
    }
    private static void ParsingAndMultipliers()
    {
        var root = Config(out var stage); var recipe = new KhemistryISRURecipe();
        Check(recipe.ParseMaintenance(root), "minimal documented config parses");
        var parsed = recipe.maintenance.Single().stages.Single();
        Check(parsed.canFix && !parsed.autoFix && !parsed.timeIsRuntime && !parsed.ignoreOrder
            && parsed.chance == 1 && parsed.evaFixers.All(n => n == 0), "defaults match config contract");
        root = Config(out stage); stage.AddValue("chance", 1.1);
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "invalid chance rejected");
        root = Config(out stage); stage.SetValue("time", 0);
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "zero time cannot cause a timer loop");
        root = Config(out stage); stage.AddValue("canFix", false); stage.AddValue("fixTime", "invalid");
        stage.AddNode("RESOURCE_TO_FIX");
        Check(new KhemistryISRURecipe().ParseMaintenance(root), "canFix false ignores all fixing fields");
        root = Config(out stage); stage.AddValue("fixersEngineers", 1.5);
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "fractional fixer count rejected");
        root = Config(out stage); stage.AddValue("speedMul", 0);
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "zero duration multiplier rejected");
        var biomeNode = new ConfigNode(); var a = Stage(1, 10); var b = Stage(1, 10);
        foreach (string key in KhemistryISRUBiomeConfig.MultiplierFields.Keys)
        { biomeNode.AddValue(key, 2); a.multipliers[key] = 3; b.multipliers[key] = 4; }
        var biome = new KhemistryISRUBiomeConfig(biomeNode);
        var effective = biome.WithMaintenance(new[] { a, b });
        Check(KhemistryISRUBiomeConfig.MultiplierFields.Count == 13, "all thirteen biome multipliers included");
        foreach (var mapping in KhemistryISRUBiomeConfig.MultiplierFields)
        {
            var field = typeof(KhemistryISRUBiomeConfig).GetField(mapping.Value);
            Check((double)field.GetValue(effective) == 24, mapping.Key + " combines biome and maintenance types");
            Check((double)field.GetValue(biome) == 2, mapping.Key + " leaves shared config unchanged");
        }
    }
    private static void RepairsAndPersistence()
    {
        var stage = Stage(1, 10); stage.fixTime = 3;
        stage.resources["Tape"] = 2; stage.tools["Wrench"] = 1; stage.evaFixers[0] = 1;
        stage.crewFixers[1] = 1; stage.fixersCrewSamePart = true;
        var definition = Definition(stage); var recipe = new KhemistryISRURecipe(); recipe.maintenance.Add(definition);
        var converter = new KhemistryISRU { _activeRecipe = recipe };
        var state = converter.State(definition); state.Activate(stage);
        converter.FixMaintenance(); converter.Tick(1);
        Check(state.repairProgress == 0 && state.repairStatus == "missing fixers", "manual repair waits for both fixer groups");
        var eva = new Vessel { isEVA = true }; var evaPart = new Part(); eva.parts.Add(evaPart);
        evaPart.protoModuleCrew.Add(new ProtoCrewMember { trait = "Engineer" }); FlightGlobals.Vessels.Add(eva);
        var other = new Part(); other.protoModuleCrew.Add(new ProtoCrewMember { trait = "Pilot" }); converter.vessel.parts.Add(other);
        converter.Tick(1); Check(state.repairProgress == 0, "same-part restriction excludes crew on another part");
        converter.part.protoModuleCrew.Add(new ProtoCrewMember { trait = "Pilot" });
        converter.Tick(1); Check(state.repairStatus == "missing Tape", "missing supplies pause repairs");
        var tape = new KhemistryResourceNetwork.Endpoint { resourceName = "Tape", amount = 5 };
        var wrench = new KhemistryResourceNetwork.Endpoint { resourceName = "Wrench", amount = 1 };
        converter.part.resources.Add(tape); converter.part.resources.Add(wrench);
        converter.Tick(1); Check(state.repairProgress == 1 && tape.amount == 5, "supplies consumed only at completion");
        var saved = new ConfigNode(); converter.ToggleAutoMaintenanceFixing(); converter.SaveState(saved);
        var restored = new KhemistryISRU { _activeRecipe = recipe, part = converter.part, vessel = converter.vessel };
        restored.LoadState(saved); var restoredState = restored.State(definition);
        Check(restored.autoMaintenanceFixing && restoredState.repairProgress == 1 && restoredState.manualRepair
            && restoredState.activeOrder == 1, "save/load retains repairs, active order and auto toggle");
        wrench.amount = 0; restored.Tick(10);
        Check(restoredState.repairProgress == 1 && tape.amount == 5, "losing a tool pauses without consuming supplies");
        wrench.amount = 1; restored.Tick(2);
        Check(restoredState.activeOrder == null && tape.amount == 3 && wrench.amount == 1, "completed repair consumes supplies and retains tools");
        restoredState.Activate(stage); restored.Tick(5);
        Check(restoredState.activeOrder == 1 && tape.amount == 3, "auto toggle does not repair a stage with autoFix false");
        stage.autoFix = true; restored.Tick(3);
        Check(restoredState.activeOrder == null && tape.amount == 1, "autoFix and enabled toggle repair together");
        restoredState.Activate(stage); restored.ToggleAutoMaintenanceFixing(); tape.amount = 10; restored.Tick(3);
        Check(restoredState.activeOrder == 1, "toggle off prevents automatic fixing");
        stage.tools["Tape"] = 9; restored.FixMaintenance(); restored.Tick(3);
        Check(restoredState.activeOrder == 1 && tape.amount == 10, "supply also required as tool must remain after consumption");
        stage.tools.Remove("Tape"); stage.canFix = false; restored.Tick(3);
        Check(restoredState.activeOrder == 1 && tape.amount == 10, "canFix false blocks manual and auto repairs");
        restored.LoadState(new ConfigNode());
        Check(!restored.autoMaintenanceFixing && restored.State(definition).activeOrder == null,
            "inventory prefab state cannot leak between different parts");
        var clock = restored.State(definition); stage.canFix = true; stage.autoFix = false;
        Planetarium.now += 100;
        clock.lastUniversalTime = Planetarium.now - 100; restored.Tick(1);
        Check(clock.activeOrder == 1, "wall timers catch up after unloading");
    }
    private static void HistoryConditions()
    {
        var root = Config(out var node);
        var type = root.GetNodes("MAINTENANCE").Single();
        var duplicate = type.AddNode("STAGE");
        duplicate.AddValue("order", 1); duplicate.AddValue("status", "Duplicate"); duplicate.AddValue("time", 5);
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "duplicate stage orders invalidate maintenance");

        root = Config(out node); type = root.GetNodes("MAINTENANCE").Single();
        foreach (int order in new[] { 10, 42, 26, 50, 60, 70 })
        {
            var additional = type.AddNode("STAGE");
            additional.AddValue("order", order); additional.AddValue("status", "Other"); additional.AddValue("time", 5);
        }
        node.AddValue("requiredOrdersAND", "10, 42");
        node.AddValue("requiredOrdersAND", "26");
        node.AddValue("requiredOrdersOR", "10,50");
        node.AddValue("requiredOrdersOR", "60,70");
        node.AddValue("requiredOrderNOT", "50");
        node.AddValue("requiredOrderNOT", "1");
        var recipe = new KhemistryISRURecipe();
        Check(recipe.ParseMaintenance(root), "repeated AND/OR/NOT conditions parse with whitespace");
        var stage = recipe.maintenance.Single().stages.Single(s => s.order == 1);
        var state = new MaintenanceState();
        state.occurredOrders.UnionWith(new[] { 10, 42, 60 });
        Check(!stage.RequirementsMet(state.occurredOrders), "every repeated AND list is required");
        state.occurredOrders.Add(26); state.occurredOrders.Remove(60);
        Check(!stage.RequirementsMet(state.occurredOrders), "matching one OR list does not satisfy the other");
        state.occurredOrders.Add(70);
        Check(stage.RequirementsMet(state.occurredOrders), "any match within each OR list suffices");
        state.occurredOrders.Add(50);
        Check(!stage.RequirementsMet(state.occurredOrders), "first NOT condition blocks the stage");
        state.occurredOrders.Remove(50); state.occurredOrders.Add(1);
        Check(!stage.RequirementsMet(state.occurredOrders), "repeated NOT condition blocks recurrence");
        foreach (var bad in new[] { "", "10,", "10,,42", "1.5", "2147483648", "unknown" })
        {
            root = Config(out node); node.AddValue("requiredOrdersAND", bad);
            Check(!new KhemistryISRURecipe().ParseMaintenance(root), "malformed prerequisite is rejected: " + bad);
        }
        root = Config(out node); node.AddValue("requiredOrderNOT", "1,2");
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "NOT accepts only a single order per value");
        root = Config(out node); node.AddValue("requiredOrdersOR", "999");
        Check(!new KhemistryISRURecipe().ParseMaintenance(root), "undefined referenced order is rejected");

        var first = Stage(3, 5); var gated = Stage(2, 10, true); var final = Stage(1, 20, true);
        gated.requiredOrdersAND.Add(3);
        var definition = Definition(first, gated, final);
        state = new MaintenanceState();
        state.Advance(definition, 4, 0, () => 0);
        Check(!state.elapsed.ContainsKey(2), "gated parallel timer never starts early");
        state.Advance(definition, 4, 0, () => 0);
        Check(state.occurredOrders.SetEquals(new[] { 3 }) && state.elapsed[2] == 3,
            "gate opens at the activation boundary, without accruing time before it");
        state.Advance(definition, 7, 0, () => 0);
        Check(state.activeOrder == 2 && state.occurredOrders.SetEquals(new[] { 3, 2 }), "successful stages accumulate history");
        state.Repair(definition); state.Repair(definition);
        Check(state.activeOrder == null && state.occurredOrders.SetEquals(new[] { 3, 2 }), "repairs do not erase occurrence history");
        first.chance = 0; state = new MaintenanceState();
        state.Advance(definition, 10, 0, () => 0);
        Check(!state.occurredOrders.Contains(3) && !state.elapsed.ContainsKey(2), "failed chance rolls do not unlock dependents");

        first = Stage(3, 5); gated = Stage(2, 10, true); gated.requiredOrderNOT.Add(3);
        definition = Definition(first, gated); state = new MaintenanceState();
        state.Advance(definition, 50, 0, () => 0);
        Check(state.activeOrder == 3 && state.elapsed[2] == 5, "NOT stops an already-started parallel timer");
        state.Repair(definition); state.Advance(definition, 50, 0, () => 0);
        Check(!state.occurredOrders.Contains(2), "repairing the blocker never removes a historical NOT block");

        definition = Definition(Stage(3, 100), Stage(2, 100), Stage(1, 5, true));
        state = new MaintenanceState(); state.Advance(definition, 5, 0, () => 0);
        Check(state.occurredOrders.SetEquals(new[] { 1 }), "skipped stages are not invented in history");
        state.Repair(definition);
        Check(state.occurredOrders.SetEquals(new[] { 1, 2 }), "a predecessor activated by repair is recorded");

        recipe = new KhemistryISRURecipe(); recipe.maintenance.Add(definition);
        var converter = new KhemistryISRU { _activeRecipe = recipe };
        converter.State(definition).occurredOrders.UnionWith(new[] { 1, 2 });
        var save = new ConfigNode(); converter.SaveState(save);
        var restored = new KhemistryISRU { _activeRecipe = recipe }; restored.LoadState(save);
        Check(restored.State(definition).occurredOrders.SetEquals(new[] { 1, 2 }), "full history persists even after returning to optimal");
        var otherDefinition = Definition(Stage(1, 10)); otherDefinition.name = "Other type";
        Check(restored.State(otherDefinition).occurredOrders.Count == 0, "history is isolated between maintenance types");
        restored.LoadState(new ConfigNode());
        Check(restored.State(definition).occurredOrders.Count == 0, "loading another inventory part clears history");
        save = new ConfigNode(); var oldState = save.AddNode("MAINTENANCE_STATE");
        oldState.AddValue("name", definition.name); oldState.AddValue("activeOrder", 1);
        restored.LoadState(save);
        Check(restored.State(definition).occurredOrders.SetEquals(new[] { 1 }), "legacy saves recover only the known active stage");
    }

    private static int Main()
    {
        try { Timers(); ParsingAndMultipliers(); RepairsAndPersistence(); HistoryConditions(); Console.WriteLine(assertions + " assertions passed."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
