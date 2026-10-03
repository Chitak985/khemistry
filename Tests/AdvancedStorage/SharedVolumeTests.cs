using System;
using Khemistry;

static partial class Program
{
    static ConfigNode SharedVolumeConfig(params string[] amounts)
    {
        var config = new ConfigNode();
        var names = config.AddNode("SUPPORTED_RESOURCES");
        names.AddValue("name", "A"); names.AddValue("name", "B");
        var multipliers = config.AddNode("SUPPORTED_RESOURCE_VOL_MUL");
        foreach (var amount in amounts) multipliers.AddValue("amount", amount);
        return config;
    }

    static void SharedVolumeTests()
    {
        var (part, storage, converter) = Setup();
        var config = SharedVolumeConfig("1", "2");
        Equal(1, storage.ConfigureSupported(config) ? 1 : 0, "weighted config accepted");
        Equal(10, storage.GetResourceCapacity("A"), "A standalone capacity");
        Equal(5, storage.GetResourceCapacity("B"), "B standalone capacity");
        Equal(4, storage.PutResource("B", 4), "4 B occupies 8");
        Equal(2, storage.PutResource("A", 20), "2 A fills remaining 2");
        Equal(0, storage.PutResource("B", 1), "weighted full");
        Equal(1, storage.TakeResource("B", 1), "withdraw returns resource units");
        Equal(2, storage.GetAvailableSpace("A"), "B withdrawal frees two capacity units");
        storage.UndoTransfer("B", 1);
        Equal(0, storage.GetAvailableSpace("A"), "rollback restores weighted occupancy");
        var save = new ConfigNode(); storage.OnSave(save);
        storage.OnLoad(save); storage.Ready(); storage.ConfigureSupported(config);
        Equal(4, storage.GetStoredAmount("B"), "persist raw resource units");
        Equal(0, storage.GetAvailableSpace("B"), "restore weighted capacity from config");
        storage.TakeResource("B", 4); storage.TakeResource("A", 2);
        Equal(2.5, storage.GetAvailableSpace("B", .5), "fractional fill quote uses weighted units");
        storage.maxInputRate = 1;
        UnityEngine.Time.fixedTime++;
        Equal(1, storage.PutResource("B", 10), "rate remains resource units per second");
        storage.maxInputRate = -1;
        storage.TakeResource("B", 1);
        converter.outputList.Add(new ResourceRatio { ResourceName = "B" });
        var broker = new KhemistryStockResourceBroker(new ResourceBroker(), converter);
        Equal(5, broker.StorageAvailable(part, "B", 1, ResourceFlowMode.ALL_VESSEL, 1), "stock broker weighted quote");
        Equal(-5, broker.StoreResource(part, "B", 10, 1, ResourceFlowMode.ALL_VESSEL), "stock broker weighted insertion");
        storage.TakeResource("B", 5);
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "volumeMul", "0.5"));
        storage.RefreshEnvironment();
        Equal(2.5, storage.PutResource("B", 10), "biome multiplier preserves weighted pool");
        Equal(2.5, storage.GetStoredAmount("B"), "weighted full not incorrectly voided");
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "volumeMul", "0.4"));
        storage.RefreshEnvironment();
        Equal(0, storage.GetStoredAmount("B"), "environment overfill uses weighted occupancy");
        storage.ConfigureEnvironment(new ConfigNode()); storage.RefreshEnvironment();
        foreach (var invalid in new[] { new string[0], new[] { "1" }, new[] { "1", "2", "3" },
            new[] { "1", "0" }, new[] { "1", "-1" }, new[] { "1", "NaN" },
            new[] { "1", "Infinity" }, new[] { "1", "bad" } })
        {
            int warnings = KShared.Warnings.Count;
            Equal(1, storage.ConfigureSupported(SharedVolumeConfig(invalid)) ? 1 : 0, "bad multipliers not fatal");
            Equal(10, storage.GetResourceCapacity("B"), "bad multipliers fall back atomically");
            Equal(1, KShared.Warnings.Count > warnings ? 1 : 0, "fallback logged");
        }
        config = SharedVolumeConfig("1", "2");
        config.AddNode("SUPPORTED_RESOURCE_VOL_MUL").AddValue("amount", "1");
        storage.ConfigureSupported(config);
        Equal(10, storage.GetResourceCapacity("B"), "duplicate multiplier node ignored");
        config = SharedVolumeConfig("1", "2");
        config.GetNode("SUPPORTED_RESOURCES").AddValue("name", "A");
        config.GetNode("SUPPORTED_RESOURCE_VOL_MUL").AddValue("amount", "3");
        storage.ConfigureSupported(config);
        Equal(10, storage.GetResourceCapacity("B"), "duplicate resource list falls back");
        storage.ConfigureSupported(SharedVolumeConfig("0.5", "2"));
        Equal(20, storage.GetResourceCapacity("A"), "fractional multiplier accepted");
        var plain = new ConfigNode();
        plain.AddNode("SUPPORTED_RESOURCES").AddValue("name", "A");
        storage.ConfigureSupported(plain);
        Equal(10, storage.GetResourceCapacity("A"), "missing node clears old multipliers");
        storage.storageType = "multi";
        storage.ConfigureSupported(SharedVolumeConfig("1", "2"));
        Equal(10, storage.GetResourceCapacity("B"), "other storage types unaffected");
    }
}
