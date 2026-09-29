using System;
using System.Collections.Generic;
using System.Linq;
using Khemistry;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage
    {
        public bool ConfigureDegradation(ConfigNode n) => LoadDegradation(n);
        public void TickDegradation(double dt) => ProcessDegradation(dt);
        public double DegradationValue(string name) => _degradationValues[name];
        public bool Fatal => _fatalConfigError;
        public void SetContentsForTest(double amount) => _resources["A"] = amount;
    }
}

static partial class Program
{
    static ConfigNode Degradation(params string[] pairs)
    {
        var module = new ConfigNode("MODULE");
        var node = module.AddNode("DEGRADATION");
        for (int i = 0; i < pairs.Length; i += 2) node.AddValue(pairs[i], pairs[i + 1]);
        return module;
    }
    static void DegradationTests()
    {
        var (part, storage, _) = Setup();
        storage.storageType = "single"; storage.maximumResources = 100;
        var config = Degradation("age", "age+dt", "damage", "damage+0.01*dt",
            "currentCapacity", "maxCapacity*(1-damage)", "after", "currentCapacity");
        Equal(1, storage.ConfigureDegradation(config) ? 1 : 0, "degradation loads");
        Equal(0, storage.DegradationValue("age"), "custom default zero");
        Equal(100, storage.GetResourceCapacity("A"), "initial capacity");
        storage.PutResource("A", 100);
        int messages = ScreenMessages.Count, warnings = KShared.Warnings.Count;
        storage.TickDegradation(10);
        Equal(10, storage.DegradationValue("age"), "dt age");
        Equal(0.1, storage.DegradationValue("damage"), "damage integrated");
        Equal(90, storage.DegradationValue("after"), "same-evaluation ordering");
        Equal(90, storage.GetStoredAmount("A"), "only excess discarded");
        Equal(messages, ScreenMessages.Count, "degradation silent screen");
        Equal(warnings, KShared.Warnings.Count, "degradation silent log");
        Equal(100, storage.maximumResources, "configured capacity unchanged");
        storage.TickDegradation(5);
        Equal(15, storage.DegradationValue("age"), "self reference persists");
        Equal(85, storage.GetResourceCapacity("A"), "repeat degradation");
        var saved = new ConfigNode();
        storage.OnSave(saved);
        Equal(0, saved.GetNodes("DEGRADATION").Length, "expressions not saved");
        var (_, reloaded, _) = Setup(); reloaded.storageType = "single"; reloaded.maximumResources = 100;
        reloaded.OnLoad(saved);
        var immediateSave = new ConfigNode(); reloaded.OnSave(immediateSave);
        Equal(1, immediateSave.GetNodes("DEGRADATION_STATE").Length, "save before start preserves pending state");
        reloaded.Ready(); reloaded.ConfigureDegradation(config);
        Equal(85, reloaded.GetResourceCapacity("A"), "capacity restored before evaluation");
        reloaded.TickDegradation(1000);
        Equal(1015, reloaded.DegradationValue("age"), "elapsed catchup interval");
        Equal(0, reloaded.GetResourceCapacity("A"), "lower clamp");
        Equal(0, reloaded.GetStoredAmount("A"), "zero capacity safely empty");
        Equal(0, reloaded.PutResource("A", 1), "zero capacity rejects fill");

        (_, storage, _) = Setup(); storage.storageType = "single";
        config = Degradation("input", "throughputIn", "output", "throughputOut",
            "inFlag", "isInputting", "outFlag", "isOutputting",
            "fill", "fillPercentage", "before", "storedAmount", "currentCapacity", "maxCapacity");
        storage.ConfigureDegradation(config);
        storage.PutResource("A", 20); // Only ten fit.
        storage.TakeResource("A", 3); storage.UndoTransfer("A", 1);
        storage.UndoTransfer("A", -2);
        var throughputSave = new ConfigNode(); storage.OnSave(throughputSave);
        (_, reloaded, _) = Setup(); reloaded.storageType = "single";
        reloaded.OnLoad(throughputSave); reloaded.Ready(); reloaded.ConfigureDegradation(config);
        UnityEngine.Time.fixedTime += 10;
        reloaded.TickDegradation(10);
        Equal(8, reloaded.DegradationValue("input"), "accepted net input persists independently of tick budgets");
        Equal(2, reloaded.DegradationValue("output"), "net output rollback");
        Equal(1, reloaded.DegradationValue("inFlag"), "input flag");
        Equal(1, reloaded.DegradationValue("outFlag"), "output flag");
        Equal(6, reloaded.DegradationValue("before"), "stored amount snapshot");
        Equal(0.6, reloaded.DegradationValue("fill"), "fill fraction");
        reloaded.TickDegradation(1);
        Equal(0, reloaded.DegradationValue("input"), "input accumulator reset");
        Equal(0, reloaded.DegradationValue("output"), "output accumulator reset");
        Equal(0, reloaded.DegradationValue("inFlag"), "input flag reset");
        Equal(0, reloaded.DegradationValue("outFlag"), "output flag reset");

        (part, storage, _) = Setup(); storage.storageType = "single";
        part.vessel.externalTemperature = 250; part.vessel.staticPressurekPa = 12;
        part.vessel.geeForce = 3; part.vessel.altitude = 2000;
        config = Degradation("env", "temp+pressure+accelG+altitude",
            "oldCapacity", "currentCapacity", "currentCapacity", "currentCapacity-dt");
        storage.ConfigureDegradation(config); storage.TickDegradation(2);
        Equal(2265, storage.DegradationValue("env"), "same runtime environment units");
        Equal(10, storage.DegradationValue("oldCapacity"), "read previous capacity");
        Equal(8, storage.GetResourceCapacity("A"), "capacity recurrence");
        storage.state = KShared.ChargablePartState.Off; storage.TickDegradation(1);
        Equal(7, storage.GetResourceCapacity("A"), "degrades while off");

        foreach (var bad in new[] {
            Degradation("damage","1"),
            Degradation("dt","1","currentCapacity","1"),
            Degradation("MAXcapacity","1","currentCapacity","1"),
            Degradation("age","1","AGE","2","currentCapacity","1"),
            Degradation("bad-name","1","currentCapacity","1"),
            Degradation("currentCapacity","missingVariable"),
            Degradation("currentCapacity","Pow(1,)"),
            Degradation("currentCapacity","1 trailing")
        })
        {
            (_, storage, _) = Setup(); storage.storageType = "single";
            Equal(0, storage.ConfigureDegradation(bad) ? 1 : 0, "bad config rejected");
            Equal(1, storage.Fatal ? 1 : 0, "bad config fatal");
        }
        foreach (string type in new[] { "multi", "multiShared" })
        {
            (_, storage, _) = Setup(); storage.storageType = type;
            Equal(0, storage.ConfigureDegradation(Degradation("currentCapacity","1")) ? 1 : 0, "type restricted");
        }
        foreach (string expr in new[] { "1/0", "Sqrt(-1)", "1e308*1e308" })
        {
            (_, storage, _) = Setup(); storage.storageType = "single";
            storage.ConfigureDegradation(Degradation("age","age+dt","currentCapacity",expr));
            storage.TickDegradation(1);
            Equal(1, storage.Fatal ? 1 : 0, "nonfinite result fatal");
            Equal(0, storage.DegradationValue("age"), "failed interval not partially committed");
            Equal(0, storage.PutResource("A",1), "runtime fatal disables transfers");
        }
        (_, storage, _) = Setup(); storage.storageType = "single";
        Equal(1, storage.ConfigureDegradation(Degradation("currentCapacity","maxCapacity/dt")) ? 1 : 0,
            "syntax validation does not evaluate with dt zero");
        storage.TickDegradation(0.5); Equal(10, storage.GetResourceCapacity("A"), "upper clamp");
        storage.ConfigureDegradation(Degradation("fill","fillPercentage","currentCapacity","0"));
        storage.TickDegradation(1); storage.TickDegradation(1);
        Equal(0, storage.DegradationValue("fill"), "empty zero-capacity fraction");
        storage.SetContentsForTest(1); storage.TickDegradation(1);
        Equal(1, storage.DegradationValue("fill"), "overfull zero-capacity fraction");
        Equal(0, storage.GetStoredAmount("A"), "zero-capacity excess discarded");

        (part, storage, _) = Setup(); storage.storageType = "single";
        var environment = new ConfigNode(); var planet = environment.AddNode("PLANET_CONFIG");
        planet.AddValue("name","ALL"); var biome = planet.AddNode("BIOME_CONFIG");
        biome.AddValue("name","ALL"); biome.AddValue("volumeMul","2");
        storage.ConfigureEnvironment(environment); storage.RefreshEnvironment();
        storage.ConfigureDegradation(Degradation("currentCapacity","maxCapacity/2"));
        storage.PutResource("A",20); storage.TickDegradation(1);
        Equal(10, storage.GetStoredAmount("A"), "biome-scaled degradation trim");
        Equal(10, storage.GetResourceCapacity("A"), "biome multiplier retained");
        messages = ScreenMessages.Count; storage.RefreshEnvironment();
        Equal(messages, ScreenMessages.Count, "environment doesn't void after degradation trim");
        Equal(10, storage.GetStoredAmount("A"), "contents retained after environment check");
        biome.RemoveValue("volumeMul"); biome.AddValue("volumeMul","1");
        storage.ConfigureEnvironment(environment); UnityEngine.Time.fixedTime++; storage.RefreshEnvironment();
        Equal(0, storage.GetStoredAmount("A"), "real environment capacity loss still voids");
        Equal(messages+1, ScreenMessages.Count, "environment loss still notifies");
    }
}
