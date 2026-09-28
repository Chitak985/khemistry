using System;
using Khemistry;
static partial class Program
{
    static ConfigNode EnvironmentConfig(string biome, params string[] settings)
    {
        var module = new ConfigNode();
        var planet = module.AddNode("PLANET_CONFIG"); planet.AddValue("name", "Kerbin");
        var config = planet.AddNode("BIOME_CONFIG"); config.AddValue("name", biome);
        for (int i = 0; i < settings.Length; i += 2) config.AddValue(settings[i], settings[i + 1]);
        return module;
    }
    static void EnvironmentTests()
    {
        var (part, storage, converter) = Setup();
        storage.storageType = "multi";
        var module = CapacityConfig("100");
        var resource = module.GetNode("SUPPORTED_RESOURCE");
        foreach (string key in StorageMultipliers.RateNames) resource.AddValue(key, "2");
        Equal(1, storage.ConfigureSupported(module) ? 1 : 0, "rate config loads");
        Equal(1, storage.ConfigureEnvironment(EnvironmentConfig("ALL", "volumeMul", "0.5", "maxInputRateMul", "3",
            "maxOutputRateMul", "4", "chargeRateMul", "5", "chargeDecayRateMul", "6", "passiveConsumptionRateMul", "7")) ? 1 : 0, "biome config loads");
        storage.maxInputRate = 1; storage.maxOutputRate = 1;
        storage.RefreshEnvironment();
        Equal(50, storage.GetResourceCapacity("A"), "biome capacity");
        Equal(6 * TimeWarp.fixedDeltaTime, storage.PutResource("A", 100), "input resource and biome multipliers compose");
        Equal(20, storage.ChargeRateForTest, "charging composition");
        Equal(36, storage.DecayRateForTest, "decay composition");
        Equal(14, storage.PassiveMultiplierForTest, "passive composition");
        UnityEngine.Time.fixedTime++;
        Equal(Math.Min(8 * TimeWarp.fixedDeltaTime, storage.GetStoredAmount("A")), storage.TakeResource("A", 100), "output composition");
        storage.maxInputRate = -1; UnityEngine.Time.fixedTime++;
        storage.PutResource("A", 40);
        int messages = ScreenMessages.Count;
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "volumeMul", "0.1"));
        storage.RefreshEnvironment();
        Equal(0, storage.GetStoredAmount("A"), "shrink voids all contents");
        Equal(messages + 1, ScreenMessages.Count, "void message once");
        storage.RefreshEnvironment(); UnityEngine.Time.fixedTime++; storage.RefreshEnvironment();
        Equal(messages + 1, ScreenMessages.Count, "empty storage does not spam");
        storage.PutResource("A", 5);
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "disabled", "true"));
        Equal(0, storage.TakeResource("A", 1), "disabled blocks transfers");
        Equal(0, storage.GetStoredAmount("A"), "disabled voids contents");

        var mat = new KhemistryMaterialStorage { part = part };
        mat.contents.Add(8);
        Equal(1, mat.Configure(EnvironmentConfig("ALL", "volumeMul", "0.5")) ? 1 : 0, "material biome accepted");
        Equal(1, mat.TransfersEnabled ? 1 : 0, "material can operate after void");
        Equal(5, mat.EffectiveVolume, "material volume multiplier");
        Equal(0, mat.contents.Count, "material shrink voids contents");
        Equal(0, mat.Configure(EnvironmentConfig("ALL", "chargeRateMul", "2")) ? 1 : 0, "material rejects rate multiplier");
        Equal(0, mat.Configure(EnvironmentConfig("ALL", "volumeMul", "NaN")) ? 1 : 0, "reject NaN multiplier");
        Equal(0, storage.ConfigureEnvironment(EnvironmentConfig("ALL", "maxInputRateMul", "-1")) ? 1 : 0, "reject negative multiplier");

        var env = new StorageEnvironment();
        module = EnvironmentConfig("Grasslands", "volumeMul", "2");
        var fallback = module.AddNode("PLANET_CONFIG"); fallback.AddValue("name", "ALL");
        fallback.AddNode("BIOME_CONFIG").AddValue("volumeMul", "3");
        env.Load(module, true); env.Update(part); Equal(2, env.Multiplier("volumeMul"), "exact biome precedence");
        part.vessel.mainBody.biome = "Desert"; UnityEngine.Time.fixedTime++; env.Update(part);
        Equal(3, env.Multiplier("volumeMul"), "global fallback");
        env.Load(EnvironmentConfig("ALL", "maxOperatingAltitude", "10"), true);
        part.vessel.altitude = 11; env.Update(part); Equal(0, env.Operational ? 1 : 0, "operating altitude");
        env.Load(EnvironmentConfig("ALL", "maxAltitude", "10"), true); env.Update(part);
        Equal(1, part.explosions, "survival limit explodes");
        UnityEngine.Time.fixedTime++; env.Update(part); Equal(1, part.explosions, "explode once");
        part.vessel.altitude = 0;
        env.Load(EnvironmentConfig("ALL", "depositCondition", "Absent"), true); env.Update(part);
        Equal(0, env.Operational ? 1 : 0, "deposit requirement enforced");

        foreach (var condition in new[] { new[] { "minOperatingG", "2" },
            new[] { "maxOperatingPressure", "100" }, new[] { "maxOperatingTemperature", "290" },
            new[] { "situationOperating", "FlyingLow" } })
        {
            env.Load(EnvironmentConfig("ALL", condition), true); env.Update(part);
            Equal(0, env.Operational ? 1 : 0, "environment condition " + condition[0]);
        }
        env.Load(EnvironmentConfig("Grasslands"), true); env.Update(part);
        Equal(0, env.Operational ? 1 : 0, "no matching config blocks");
        HighLogic.LoadedSceneIsFlight = false;
        env.Load(EnvironmentConfig("ALL", "disabled", "true"), true); env.Update(part);
        Equal(1, env.Operational ? 1 : 0, "editor does not void or restrict");
        HighLogic.LoadedSceneIsFlight = true;

        (part, storage, converter) = Setup(); storage.storageType = "multi";
        module = CapacityConfig("100"); resource = module.GetNode("SUPPORTED_RESOURCE");
        resource.AddValue("passiveConsumptionRateMul", "2");
        storage.ConfigureSupported(module);
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "passiveConsumptionRateMul", "3")); storage.RefreshEnvironment();
        var passive = new ConfigNode(); var pin = passive.AddNode("PINPUT_RESOURCE");
        pin.AddValue("name", "B"); pin.AddValue("amount", "1"); pin.AddValue("period", "1");
        storage.ConfigurePassive(passive);
        part.Resources.Add(new PartResource { resourceName = "B", amount = 20, maxAmount = 20 });
        storage.TickPassive(1);
        Equal(14, part.Resources[0].amount, "passive amount actually multiplied by resource and biome");
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "passiveConsumptionRateMul", "0")); storage.RefreshEnvironment();
        storage.TickPassive(1);
        Equal(14, part.Resources[0].amount, "zero passive multiplier consumes nothing");
        Equal(0, storage.Paused ? 1 : 0, "zero upkeep does not powerfail");
        storage.ConfigureEnvironment(EnvironmentConfig("ALL", "maxInputRateMul", "0")); storage.RefreshEnvironment();
        Equal(0, storage.PutResource("A", 1), "zero multiplier blocks even unlimited input rate");
        storage.ConfigureEnvironment(new ConfigNode()); storage.RefreshEnvironment();
        Equal(100, storage.GetResourceCapacity("A"), "unconfigured environment keeps capacity");
        storage.activeResource = "B";
        Equal(1, storage.PassiveMultiplierForTest, "unlisted selected resource defaults to one");
    }
}
