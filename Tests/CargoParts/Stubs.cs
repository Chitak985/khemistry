using System;
using System.Collections.Generic;
using System.Linq;
public class ConfigNode
{
    private readonly SortedDictionary<string, string> values = new SortedDictionary<string, string>();
    public ConfigNode(string name) { }
    public void AddValue(string key, object value) => values[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    public void RemoveValues(string key) => values.Remove(key);
    public override string ToString() => string.Join(";", values.Select(p => p.Key + "=" + p.Value));
}
public class Part
{
    public string name;
    public float mass;
    public ModuleCargoPart cargo;
    public ModuleInventoryPart inventory;
    public T FindModuleImplementing<T>() where T : class => (typeof(T) == typeof(ModuleCargoPart) ? (object)cargo : inventory) as T;
}
public class AvailablePart { public Part partPrefab; }
public static class PartLoader
{
    public static readonly Dictionary<string, AvailablePart> Parts = new Dictionary<string, AvailablePart>();
    public static AvailablePart getPartInfoByName(string name) => Parts.TryGetValue(name, out var p) ? p : null;
}
public class ModuleCargoPart { public float packedVolume; public int stackableQuantity; }
public class ProtoVessel { }
public class Vessel { public ProtoVessel protoVessel = new ProtoVessel(); }
public class ResourceDefinition { public double density; }
public class ProtoPartResourceSnapshot { public double amount; public ResourceDefinition definition; }
public class ProtoPartSnapshot
{
    public Part partPrefab;
    public float mass;
    public string moduleVariantName = "", state = "";
    public int moduleCargoStackableQuantity;
    public List<ProtoPartResourceSnapshot> resources = new List<ProtoPartResourceSnapshot>();
    public ProtoPartSnapshot(Part part, ProtoVessel vessel)
    { partPrefab = part; mass = part.mass; moduleCargoStackableQuantity = part.cargo.stackableQuantity; }
    public void Save(ConfigNode node)
    { node.AddValue("name", partPrefab.name); node.AddValue("mass", mass); node.AddValue("state", state); node.AddValue("variant", moduleVariantName); node.AddValue("resources", resources.Sum(r => r.amount)); }
}
public class StoredPart
{
    public int slotIndex, quantity, stackCapacity;
    public string partName, variantName;
    public ProtoPartSnapshot snapshot;
    public StoredPart(string name, int slot) { partName = name; slotIndex = slot; }
}
public class StoredParts : Dictionary<int, StoredPart> { public StoredPart At(int i) => Values.ElementAt(i); }
public class ModuleInventoryPart
{
    public StoredParts storedParts = new StoredParts();
    public int InventorySlots = 3, cacheResets;
    public float packedVolumeLimit = 10, massLimit = 10;
    public bool HasPackedVolumeLimit => packedVolumeLimit > 0;
    public bool HasMassLimit => massLimit > 0;
    public Vessel vessel = new Vessel();
    private void ResetInventoryPartsByName() => cacheResets++;
    private void UpdateModuleUI() { }
}
public static class GameEvents
{
    public sealed class Slots { public void Fire(ModuleInventoryPart inventory, int slot) { } }
    public sealed class Inventory { public int notifications; public void Fire(ModuleInventoryPart inventory) => notifications++; }
    public static readonly Slots onModuleInventorySlotChanged = new Slots();
    public static readonly Inventory onModuleInventoryChanged = new Inventory();
}
namespace Khemistry
{
    public static class KShared
    {
        public static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static bool ContainsInterpolation(string s) => s?.Contains("[") == true;
        public static void LogError(string message, string context) { }
        public static double RandomDouble(double a, double b) => (a + b) / 2;
        public static void LogFatalError(string message, string context) => throw new ArgumentException(message);
    }
    public class KhemistryMaterialInstance { }
    public class KhemistryISRUBiomeConfig { public double inputMultiplier = 1, outputMultiplier = 1; }
    public class KhemistryISRURecipe
    {
        public struct ResourceOutputMaterial { }
        public struct CargoPartEntry { public string name, amountExpression; public double scale; }
        public readonly List<CargoPartEntry> _inputCargoParts = new List<CargoPartEntry>(), _outputCargoParts = new List<CargoPartEntry>();
    }
    public partial class KhemistryISRU
    {
        public KhemistryISRURecipe _activeRecipe = new KhemistryISRURecipe();
        public Part part, host;
        public string moduleType, _lastBatchFailureStatus;
        public StoredPart _inventoryStoredPart;
        private Part GetPowerfailContextPart() => moduleType == "partEVA" ? host : part;
        private bool TryResolveMaterialReferences(string value, IDictionary<string,KhemistryMaterialInstance> inputs,
            IDictionary<string,KhemistryISRURecipe.ResourceOutputMaterial> outputs, bool numeric, string location, out string resolved)
        { resolved = value.Replace("(SETTING:quantity)", "2"); return true; }
        internal bool Plan(out CargoInventoryTransaction transaction) => TryPlanCargo(new KhemistryISRUBiomeConfig(), null, null, out transaction);
    }
}
