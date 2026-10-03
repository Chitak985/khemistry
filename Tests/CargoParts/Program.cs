using System;
using System.Linq;
using Khemistry;
internal static class Program
{
    static int checks;
    static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    static CargoInventoryTransaction Plan(ModuleInventoryPart inv, StoredPart protect = null) => new CargoInventoryTransaction(inv, protect);
    static void Main()
    {
        try { Run(); Console.WriteLine(checks + " cargo assertions passed."); }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        var prefab = new Part { name = "Tool", mass = 2, cargo = new ModuleCargoPart { packedVolume = 3, stackableQuantity = 2 } };
        PartLoader.Parts.Add("Tool", new AvailablePart { partPrefab = prefab });
        var inv = new ModuleInventoryPart();
        var plan = Plan(inv);
        Check(plan.Produce("Tool", 3, out _), "stacked outputs use two slots");
        Check(plan.HasCapacity(out _), "aggregate volume and mass fit");
        Check(inv.storedParts.Count == 0, "planning has no side effects");
        plan.Apply();
        Check(inv.storedParts.Count == 2 && inv.storedParts[0].quantity == 2, "whole parts stored");
        Check(!ReferenceEquals(inv.storedParts[0].snapshot, inv.storedParts[1].snapshot), "independent stack snapshots");
        plan.Publish();
        Check(inv.cacheResets == 1 && GameEvents.onModuleInventoryChanged.notifications == 1, "stock cache and event updated");
        var original = inv.storedParts[0];
        plan = Plan(inv);
        Check(plan.Consume("Tool", 3, out _) && plan.Produce("Tool", 3, out _) && plan.HasCapacity(out _), "inputs free capacity");
        plan.Apply(); plan.Rollback();
        Check(ReferenceEquals(original, inv.storedParts[0]) && original.quantity == 2, "rollback restores identity and quantity");
        plan = Plan(inv);
        Check(!plan.Consume("Tool", 4, out _), "missing inputs rejected");
        Check(inv.storedParts[0].quantity == 2, "failed plan preserves inputs");
        plan = Plan(inv);
        Check(plan.Produce("Tool", 1, out _) && !plan.HasCapacity(out _), "combined output volume blocks");
        inv.packedVolumeLimit = 100; inv.massLimit = 5;
        Check(!Plan(inv).HasCapacity(out _), "combined output mass blocks");
        inv.massLimit = 100;
        plan = Plan(inv, original);
        Check(!plan.Consume("Tool", 2, out _), "running processor protected");
        plan = Plan(inv);
        Check(!plan.Produce("Tool", int.MaxValue, out _), "huge output safely limited by slots");
        Check(!Plan(inv).Produce("Unknown", 1, out _), "unknown output rejected");
        inv.InventorySlots = 2;
        inv.storedParts[1].snapshot.state = "used";
        Check(!Plan(inv).Produce("Tool", 1, out _), "new parts cannot inherit used stack state");
        inv.InventorySlots = 3;
        inv.storedParts[0].snapshot.resources.Add(new ProtoPartResourceSnapshot { amount = 50, definition = new ResourceDefinition { density = 1 } });
        Check(!Plan(inv).HasCapacity(out _), "stored resource mass counted");
        foreach (string mode in new[] { "normal", "kerbalEVA", "partEVA" })
        {
            var correct = new ModuleInventoryPart();
            var wrong = new ModuleInventoryPart { InventorySlots = 0 };
            var isru = new KhemistryISRU { moduleType = mode, part = new Part { inventory = mode == "partEVA" ? wrong : correct }, host = new Part { inventory = correct } };
            isru._activeRecipe._outputCargoParts.Add(new KhemistryISRURecipe.CargoPartEntry { name = "Tool", amountExpression = "(SETTING:quantity)", scale = 1 });
            Check(isru.Plan(out plan), mode + " cargo batch planned");
            plan.Apply();
            Check(correct.storedParts.Values.Sum(p => p.quantity) == 2 && wrong.storedParts.Count == 0, mode + " uses context inventory");
            foreach (string bad in new[] { "0.5", "-1", "1/0", "2147483648", "unknown", "[2]" })
            {
                isru._activeRecipe._outputCargoParts[0] = new KhemistryISRURecipe.CargoPartEntry { name = "Tool", amountExpression = bad, scale = 1 };
                Check(!isru.Plan(out _), mode + " rejects invalid count " + bad);
            }
        }
    }
}
