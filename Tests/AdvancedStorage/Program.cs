using System;
using System.Collections.Generic;
using System.Linq;
using Khemistry;

static partial class Program
{
    static int assertions;
    static void Equal(double expected, double actual, string message)
    {
        assertions++;
        if (Math.Abs(expected - actual) > 1e-8 || double.IsNaN(actual))
            throw new Exception(message + ": expected " + expected + ", got " + actual);
    }
    static (Part, KhemistryAdvancedStorage, BaseConverter) Setup()
    {
        UnityEngine.Time.fixedTime++;
        var vessel = new Vessel();
        var part = new Part { vessel = vessel };
        vessel.parts.Add(part);
        var storage = new KhemistryAdvancedStorage { part = part };
        storage.Ready();
        var converter = new BaseConverter { part = part };
        part.Modules.Add(storage); part.Modules.Add(converter);
        return (part, storage, converter);
    }
    static void Main()
    {
        // Report failures through the console without an unhandled-exception dialog.
        try { Run(); }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }
    static void Run()
    {
        PartResourceLibrary.Instance.definitions.Add(new PartResourceDefinition { name = "A", id = 1 });
        PartResourceLibrary.Instance.definitions.Add(new PartResourceDefinition { name = "B", id = 2 });
        var (part, storage, converter) = Setup();
        Equal(7, storage.PutResource("A", 7), "fill A");
        Equal(3, storage.PutResource("B", 7), "shared capacity");
        Equal(0, storage.PutResource("A", 1), "no overfill");
        storage.state = KShared.ChargablePartState.Off;
        Equal(0, storage.TakeResource("A", 4), "off blocks withdrawal");
        Equal(0, storage.PutResource("B", 4), "off blocks filling");
        Equal(7, storage.GetStoredAmount("A"), "off preserves contents");
        storage.state = KShared.ChargablePartState.On;
        Equal(0, storage.PutResource("unknown", 1), "unsupported filling");
        Equal(0, storage.PutResource("A", double.NaN), "NaN rejection");
        Equal(0, part.Resources.Count, "no stock tanks created");
        Equal(5, storage.GetModuleMass(0, ModifierStagingSituation.CURRENT), "contents mass");
        Equal(20, storage.GetModuleCost(0, ModifierStagingSituation.CURRENT), "contents cost");
        var saved = new ConfigNode();
        storage.OnSave(saved);
        var restored = new KhemistryAdvancedStorage();
        restored.OnLoad(saved); restored.Ready();
        Equal(7, restored.GetStoredAmount("A"), "save/load A");
        Equal(3, restored.GetStoredAmount("B"), "save/load B");
        var snapshot = restored.GetStoredResources(); snapshot["A"] = 999;
        Equal(7, restored.GetStoredAmount("A"), "snapshot cannot mutate storage");

        restored.storageType = "multi";
        Equal(0, restored.PutResource("A", 1), "mixed legacy contents block filling single-resource storage");
        Equal(3, restored.TakeResource("B", 3), "inactive legacy contents remain recoverable");
        Equal(3, restored.PutResource("A", 3), "filling resumes after conflicting contents removed");
        var malformed = new ConfigNode();
        var invalid = malformed.AddNode("STORED_RESOURCE");
        invalid.AddValue("name", "B"); invalid.AddValue("amount", "invalid");
        restored.OnLoad(malformed); restored.Ready();
        Equal(0, restored.PutResource("A", 1), "unreadable save blocks unsafe filling");
        var preserved = new ConfigNode(); restored.OnSave(preserved);
        Equal(1, preserved.GetNodes("STORED_RESOURCE").Length, "unreadable contents preserved on save");

        (part, storage, converter) = Setup();
        part.Resources.Add(new PartResource { resourceName = "A", amount = 12, maxAmount = 12 });
        part.Resources.Add(new PartResource { resourceName = "unrelated", amount = 5, maxAmount = 5 });
        storage.Migrate(); storage.Migrate();
        Equal(12, storage.GetStoredAmount("A"), "legacy migration once, preserve overfill");
        Equal(1, part.Resources.Count, "only owned legacy tank removed");
        Equal(0, storage.PutResource("B", 1), "overfilled save blocks input");
        Equal(12, storage.TakeResource("A", 12), "legacy excess withdrawable");

        storage.maxInputRate = 4; storage.maxOutputRate = 0;
        Equal(3, storage.PutResource("A", 3), "rate first fill");
        Equal(1, storage.PutResource("B", 3), "rate shared across calls and resources");
        Equal(0, storage.TakeResource("A", 1), "zero output rate");
        UnityEngine.Time.fixedTime++;
        Equal(4, storage.PutResource("A", 4), "next tick replenishes budget");
        var ledger = new List<KhemistryResourceNetwork.Transfer>();
        storage.maxInputRate = -1; storage.maxOutputRate = -1;
        Equal(7, KhemistryResourceNetwork.Request(part, "A", 7,
            ResourceFlowMode.ALL_VESSEL, ledger), "dictionary network withdrawal");
        storage.state = KShared.ChargablePartState.Off;
        KhemistryResourceNetwork.Rollback(ledger);
        Equal(7, storage.GetStoredAmount("A"), "rollback bypasses changed state");
        Equal(0, ledger.Count, "rollback is single-use");
        storage.state = KShared.ChargablePartState.On;
        KhemistryResourceNetwork.Request(part, "A", 5, ResourceFlowMode.ALL_VESSEL, ledger);
        KhemistryResourceNetwork.RollbackAmount(ledger, 2);
        Equal(4, storage.GetStoredAmount("A"), "partial passive rollback");
        KhemistryResourceNetwork.Rollback(ledger);
        Equal(7, storage.GetStoredAmount("A"), "remaining rollback");

        (part, storage, converter) = Setup();
        storage.PutResource("A", 3);
        part.Resources.Add(new PartResource { resourceName = "A", amount = 4, maxAmount = 10 });
        Equal(7, KhemistryResourceNetwork.Request(part, "A", 9,
            ResourceFlowMode.ALL_VESSEL, ledger), "mixed source partial draw");
        KhemistryResourceNetwork.Rollback(ledger);
        Equal(3, storage.GetStoredAmount("A"), "internal refund to exact source");
        Equal(4, part.Resources[0].amount, "stock refund to exact source");
        var internalEndpoint = KhemistryResourceNetwork.GetEndpoints(part).First(e => e.storage == storage);
        storage.state = KShared.ChargablePartState.Off;
        Equal(0, internalEndpoint.Request(1), "selector rechecks storage state");
        storage.state = KShared.ChargablePartState.On;
        var stockEndpoint = KhemistryResourceNetwork.GetEndpoints(part).First(e => e.stock != null);
        part.Resources[0].flowState = false;
        Equal(0, stockEndpoint.Request(1), "ordinary tank lock respected");
        part.Resources[0].flowState = true;
        part.Modules.Remove(storage);
        Equal(0, internalEndpoint.Request(1), "stale storage selector rejected");
        part.Modules.Add(storage);
        var remote = new Part { vessel = part.vessel };
        part.vessel.parts.Add(remote);
        Equal(0, KhemistryResourceNetwork.Request(remote, "A", 1,
            ResourceFlowMode.NO_FLOW), "NO_FLOW");
        remote.crossfeed = false;
        Equal(0, KhemistryResourceNetwork.Request(remote, "A", 1,
            ResourceFlowMode.STAGE_PRIORITY_FLOW), "crossfeed barrier");

        (part, storage, converter) = Setup();
        converter.inputList.Add(new ResourceRatio { ResourceName = "A" });
        converter.outputList.Add(new ResourceRatio { ResourceName = "B" });
        storage.PutResource("A", 4);
        var broker = new KhemistryStockResourceBroker(new ResourceBroker(), converter);
        Equal(4, broker.AmountAvailable(part, 1, 1, ResourceFlowMode.ALL_VESSEL), "stock sees internal input");
        Equal(4, broker.RequestResource(part, 1, 4, 1, ResourceFlowMode.ALL_VESSEL), "stock consumes internal input");
        Equal(10, broker.StorageAvailable(part, 2, 1, ResourceFlowMode.ALL_VESSEL, 1), "stock sees internal capacity");
        Equal(-6, broker.StoreResource(part, 2, 6, 1, ResourceFlowMode.ALL_VESSEL), "stock produces internal output, signed return");
        Equal(6, storage.GetStoredAmount("B"), "stock output retained");
        Equal(0, part.Resources.Count, "stock bridge creates no tanks");

        (part, storage, converter) = Setup();
        converter.outputList.Add(new ResourceRatio { ResourceName = "A" });
        converter.outputList.Add(new ResourceRatio { ResourceName = "B" });
        part.Resources.Add(new PartResource { resourceName = "A", amount = 0, maxAmount = 100 });
        broker = new KhemistryStockResourceBroker(new ResourceBroker(), converter);
        Equal(105, broker.StorageAvailable(part, "A", 1, ResourceFlowMode.ALL_VESSEL, 1), "combined stock/shared quota");
        Equal(5, broker.StorageAvailable(part, "B", 1, ResourceFlowMode.ALL_VESSEL, 1), "other shared quota");
        Equal(-50, broker.StoreResource(part, "A", 50, 1, ResourceFlowMode.ALL_VESSEL), "A cannot steal B quota");
        Equal(-5, broker.StoreResource(part, "B", 5, 1, ResourceFlowMode.ALL_VESSEL), "B retained after A");
        Equal(10, storage.GetStoredResources().Values.Sum(), "shared quota capacity");
        Equal(45, part.Resources[0].amount, "remaining output to stock");
        FlightGlobals.VesselsLoaded = new List<Vessel> { part.vessel };
        new KhemistryStockStorageBridge().FixedUpdate();
        Equal(1, converter.ResBroker is KhemistryStockResourceBroker ? 1 : 0, "bridge installs broker");
        Equal(1, ReferenceEquals(converter.ResBroker, converter.ResConverter.Broker) ? 1 : 0, "processor bound to same broker");
        new KhemistryStockStorageBridge().FixedUpdate();
        Equal(1, converter.ResBroker is KhemistryStockResourceBroker ? 1 : 0, "bridge idempotent");
        PassiveTests();
        CapacityTests();
        Console.WriteLine(assertions + " assertions passed.");
    }
}
