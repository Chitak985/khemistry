using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    // One condition parser (the ISRU biome config) and one runtime condition evaluator.
    // Storage-specific multipliers are kept separate from recipe multipliers.
    internal static class KhemistryEnvironmentConditions
    {
        internal static bool Destructive(KhemistryISRUBiomeConfig c, KhemistryRuntimeData d)
            => c.situationDestructive.Contains(d.sitCon)
                || d.alt < c.minAltitude || d.alt > c.maxAltitude
                || d.g < c.minG || d.g > c.maxG
                || d.temperature < c.minTemperature || d.temperature > c.maxTemperature
                || d.pressure < c.minPressure || d.pressure > c.maxPressure;

        internal static string OperatingFailure(KhemistryISRUBiomeConfig c, KhemistryRuntimeData d)
        {
            if (c.disabled) return "Disabled in this biome";
            if (c.situationOperating.Count > 0 && !c.situationOperating.Contains(d.sitCon))
                return "Wrong situation (" + d.sitCon + ")";
            if (d.alt < c.minOperatingAltitude || d.alt > c.maxOperatingAltitude) return "Out of operating altitude range";
            if (d.g < c.minOperatingG || d.g > c.maxOperatingG) return "Out of operating G range";
            if (d.temperature < c.minOperatingTemperature || d.temperature > c.maxOperatingTemperature) return "Out of operating temperature range";
            if (d.pressure < c.minOperatingPressure || d.pressure > c.maxOperatingPressure) return "Out of operating pressure range";
            return null;
        }
    }

    internal sealed class StorageMultipliers
    {
        internal static readonly string[] RateNames = { "maxInputRateMul", "maxOutputRateMul",
            "chargeRateMul", "chargeDecayRateMul", "passiveConsumptionRateMul" };
        private readonly Dictionary<string, double> values = new Dictionary<string, double>();
        internal double this[string name] => values.TryGetValue(name, out double value) ? value : 1.0;

        internal bool Load(ConfigNode node, bool rates, bool volume, string context)
        {
            values.Clear();
            foreach (string name in RateNames.Concat(new[] { "volumeMul" }))
            {
                if (!node.HasValue(name)) continue;
                if (!(name == "volumeMul" ? volume : rates))
                {
                    KShared.LogError(name + " is not supported here.", context);
                    return false;
                }
                if (!double.TryParse(node.GetValue(name), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double value) || !KShared.IsFinite(value) || value < 0.0)
                {
                    KShared.LogError(name + " must be finite and nonnegative.", context);
                    return false;
                }
                values[name] = value;
            }
            return true;
        }
    }

    internal sealed class StorageEnvironment
    {
        private sealed class Entry
        {
            internal KhemistryISRUBiomeConfig conditions;
            internal StorageMultipliers multipliers;
        }
        private readonly Dictionary<string, Dictionary<string, Entry>> planets = new Dictionary<string, Dictionary<string, Entry>>();
        private Entry active;
        internal bool Configured { get; private set; }
        internal bool Operational { get; private set; } = true;
        internal string Reason { get; private set; }
        private bool destroyed;
        private double checkedTick = double.NaN;
        internal double Multiplier(string name) => active?.multipliers[name] ?? 1.0;

        internal bool Load(ConfigNode module, bool advanced)
        {
            planets.Clear(); active = null; checkedTick = double.NaN; destroyed = false;
            Configured = module.HasNode("PLANET_CONFIG"); Operational = true; Reason = null;
            foreach (ConfigNode planet in module.GetNodes("PLANET_CONFIG"))
            {
                string planetName = (planet.GetValue("name") ?? "ALL").Trim();
                if (planetName.Length == 0) planetName = "ALL";
                if (!planets.TryGetValue(planetName, out var biomes))
                    planets.Add(planetName, biomes = new Dictionary<string, Entry>());
                ConfigNode[] configs = planet.GetNodes("BIOME_CONFIG");
                if (configs.Length == 0) configs = new[] { new ConfigNode("BIOME_CONFIG") };
                foreach (ConfigNode config in configs)
                {
                    var multipliers = new StorageMultipliers();
                    if (!multipliers.Load(config, advanced, true, "StorageEnvironment/Load")) return false;
                    ConfigNode conditions = new ConfigNode(); config.CopyTo(conditions);
                    foreach (string key in KhemistryISRUBiomeConfig.MultiplierFields.Keys)
                    {
                        if (conditions.HasValue(key) && key != "chargeRateMul")
                        {
                            KShared.LogError("ISRU multiplier " + key + " is not supported by storage.", "StorageEnvironment/Load");
                            return false;
                        }
                        while (conditions.HasValue(key)) conditions.RemoveValue(key);
                    }
                    // Storage has no conversion batches; biome resource recipes have no meaning here.
                    if (config.HasNode("INPUT_RESOURCE") || config.HasNode("OUTPUT_RESOURCE"))
                    {
                        KShared.LogError("Storage BIOME_CONFIG cannot contain converter INPUT_RESOURCE/OUTPUT_RESOURCE nodes.", "StorageEnvironment/Load");
                        return false;
                    }
                    var parsed = new KhemistryISRUBiomeConfig(conditions, "Storage", "Storage");
                    if (biomes.ContainsKey(parsed.biomeName))
                    {
                        KShared.LogError("Duplicate storage biome " + planetName + "/" + parsed.biomeName, "StorageEnvironment/Load");
                        return false;
                    }
                    biomes.Add(parsed.biomeName, new Entry { conditions = parsed, multipliers = multipliers });
                }
            }
            return true;
        }

        private Entry Find(string planet, string biome)
        {
            foreach (string name in new[] { planet, "ALL" })
                if (planets.TryGetValue(name, out var biomes))
                {
                    if (biomes.TryGetValue(biome, out var exact)) return exact;
                    if (biomes.TryGetValue("ALL", out var fallback)) return fallback;
                }
            return null;
        }

        internal void Update(Part part)
        {
            if (!Configured || !HighLogic.LoadedSceneIsFlight || part?.vessel == null) return;
            double tick = UnityEngine.Time.fixedTime;
            if (checkedTick == tick) return;
            checkedTick = tick;
            var data = new KhemistryRuntimeData(part.vessel);
            active = Find(data.planet, data.biome);
            Reason = active == null ? "No configuration for this biome"
                : KhemistryEnvironmentConditions.OperatingFailure(active.conditions, data);
            if (active != null && KhemistryEnvironmentConditions.Destructive(active.conditions, data))
            {
                Reason = "Outside storage survival limits";
                if (!destroyed) { destroyed = true; part.explode(); }
            }
            if (Reason == null && active.conditions.depositConditions.Count > 0)
            {
                KShared shared = KShared.Instance;
                Vessel vessel = part.vessel;
                List<string> here = shared?.SurfaceDepositsAtPoint(
                    (float)vessel.latitude,
                    (float)vessel.longitude,
                    data.planet,
                    0
                );
                here?.AddRange(shared?.UndergroundDepositsBelowPoint(
                    (float)vessel.latitude,
                    (float)vessel.longitude,
                    data.planet
                ));
                if (here == null || !active.conditions.depositConditions.Any(here.Contains))
                    Reason = "Not at a required deposit";
            }
            Operational = Reason == null && !destroyed;
        }

        internal static void NotifyVoid(Part part, string reason)
        {
            string message = (part.partInfo?.title ?? part.name) + ": storage contents voided — " + reason + ".";
            ScreenMessages.PostScreenMessage(message, 6f, ScreenMessageStyle.UPPER_CENTER);
            KShared.LogWarning(message, "StorageEnvironment");
        }
    }
}
