using System;
using System.Linq;
using Khemistry;

static partial class Program
{
    static ConfigNode CapacityConfig(string amount = "100")
    {
        var module = new ConfigNode();
        var resource = module.AddNode("SUPPORTED_RESOURCE");
        resource.AddValue("name", "A");
        if (amount != null) resource.AddValue("amount", amount);
        return module;
    }

    static void CapacityTests()
    {
        var (part, storage, converter) = Setup();
        storage.storageType = "multi";
        var config = CapacityConfig();
        var second = config.AddNode("SUPPORTED_RESOURCE");
        second.AddValue("name", "B"); second.AddValue("amount", "25");
        Equal(1, storage.ConfigureSupported(config) ? 1 : 0, "singular-only multi config accepted");
        Equal(100, storage.GetResourceCapacity("A"), "custom A capacity");
        Equal(25, storage.GetResourceCapacity("B"), "custom B capacity");
        Equal(0, storage.GetResourceCapacity("unknown"), "unsupported has no capacity");
        Equal(100, storage.PutResource("A", 200), "custom capacity used instead of main maximum");
        Equal(0, storage.PutResource("B", 1), "inactive resource remains blocked");
        Equal(100, storage.TakeResource("A", 100), "drain before switching");
        storage.activeResource = "B";
        Equal(12.5, storage.GetAvailableSpace("B", .5), "fill fraction uses selected capacity");
        Equal(25, storage.PutResource("B", 100), "selected B limit");
        storage.TakeResource("B", 25);
        converter.outputList.Add(new ResourceRatio { ResourceName = "B" });
        var broker = new KhemistryStockResourceBroker(new ResourceBroker(), converter);
        Equal(25, broker.StorageAvailable(part, "B", 1, ResourceFlowMode.ALL_VESSEL, 1), "stock broker quotes custom capacity");
        Equal(-25, broker.StoreResource(part, "B", 50, 1, ResourceFlowMode.ALL_VESSEL), "stock output obeys custom limit");
        Equal(1, storage.ConfigureSupported(CapacityConfig("2")) ? 1 : 0, "reload configuration");
        Equal(25, storage.GetStoredAmount("B"), "removed resource retained after config change");
        Equal(0, storage.GetResourceCapacity("B"), "old capacity cleared on reload");
        storage.TakeResource("B", 25);
        storage.activeResource = "A";
        Equal(2, storage.PutResource("A", 9), "smaller custom maximum");
        storage.ConfigureSupported(CapacityConfig("1"));
        Equal(2, storage.GetStoredAmount("A"), "overfilled saved contents never voided");
        Equal(0, storage.GetAvailableSpace("A"), "overfilled blocks additions");
        Equal(2, storage.TakeResource("A", 2), "overfilled contents recoverable");

        int warnings = KShared.Warnings.Count;
        Equal(1, storage.ConfigureSupported(CapacityConfig(null)) ? 1 : 0, "missing amount accepted");
        Equal(storage.maximumResources, storage.GetResourceCapacity("A"), "missing amount uses main maximum");
        Equal(1, KShared.Warnings.Count > warnings && KShared.Warnings.Last().Contains("SUPPORTED_RESOURCES") ? 1 : 0,
            "missing amount recommends plural node");
        config = CapacityConfig("40");
        var plural = config.AddNode("SUPPORTED_RESOURCES");
        plural.AddValue("name", "A"); plural.AddValue("name", "B");
        storage.ConfigureSupported(config);
        Equal(2, storage.SupportedResources.Count(), "combined forms do not duplicate selection");
        Equal(40, storage.GetResourceCapacity("A"), "singular overrides plural capacity");
        Equal(10, storage.GetResourceCapacity("B"), "plural uses main capacity");
        storage.maxInputRate = 3;
        UnityEngine.Time.fixedTime++;
        Equal(3, storage.PutResource("A", 40), "custom capacity still honors aggregate rate");
        storage.maxInputRate = -1;
        var transfers = new System.Collections.Generic.List<KhemistryResourceNetwork.Transfer>();
        Equal(-37, KhemistryResourceNetwork.Request(part, "A", -100, ResourceFlowMode.ALL_VESSEL, transfers),
            "routed transfer caps at custom limit");
        KhemistryResourceNetwork.Rollback(transfers);
        Equal(3, storage.GetStoredAmount("A"), "rollback preserves custom-capacity contents");
        var saved = new ConfigNode(); storage.OnSave(saved);
        storage.OnLoad(saved); storage.Ready(); storage.ConfigureSupported(config);
        Equal(3, storage.GetStoredAmount("A"), "contents persist with custom limits");
        Equal(40, storage.GetResourceCapacity("A"), "custom limit reapplied from config after load");
        foreach (string type in new[] { "single", "multiShared" })
        {
            storage.storageType = type;
            int errors = KShared.Errors.Count;
            Equal(1, storage.ConfigureSupported(config) ? 1 : 0, "plural valid on " + type);
            Equal(10, storage.GetResourceCapacity("A"), "custom ignored on " + type);
            Equal(1, KShared.Errors.Count > errors ? 1 : 0, "wrong type logged");
            Equal(0, storage.ConfigureSupported(CapacityConfig()) ? 1 : 0, "singular alone invalid on " + type);
        }
        storage.storageType = "multi";
        foreach (string amount in new[] { "0", "-1", "NaN", "Infinity", "bad", "" })
            Equal(0, storage.ConfigureSupported(CapacityConfig(amount)) ? 1 : 0, "invalid capacity rejected: " + amount);
        config = CapacityConfig();
        config.AddNode(CapacityConfig("200").GetNode("SUPPORTED_RESOURCE"));
        Equal(0, storage.ConfigureSupported(config) ? 1 : 0, "duplicate individual limits rejected");
        config = CapacityConfig();
        config.GetNode("SUPPORTED_RESOURCE").AddValue("name", "unknown");
        Equal(0, storage.ConfigureSupported(config) ? 1 : 0, "unknown individual resource rejected");
    }
}
