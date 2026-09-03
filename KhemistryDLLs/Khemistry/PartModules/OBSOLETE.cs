using System;
using System.Collections.Generic;
using System.Globalization;

namespace Khemistry
{
    /// <summary>
    /// An obsolete <see cref="PartModule"/> that simulates a battery that degrades over time.
    /// Should be ported over to the <see cref="KhemistryFluidCell"/> and <see cref="KhemistryAdvancedStorage"/> modules.
    /// </summary>
    public class KhemistryDegradingBattery : PartModule
    {
        [KSPField(isPersistant = false)]
        public string ResourceName = "ElectricCharge";

        [KSPField(isPersistant = false)]
        public double DegradeTime = -1.0;

        [KSPField(isPersistant = true)]
        public double OriginalMaxAmount = -1.0;

        [KSPField(isPersistant = true)]
        public double StartTime = -1.0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
        public string HealthDisplay = "Battery Life: 100%";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Time Remaining",
         groupName = "batterydeg")]
        public string HealthTimeDisplay = "Time until 0% battery life: Battery cannot degrade.";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            ResourceName = ResourceName?.Trim();
            if (string.IsNullOrEmpty(ResourceName))
            {
                KShared.LogError("Part \"" + part.name
                    + "\" has KhemistryDegradingBattery with an empty ResourceName.",
                    "KhemistryDegradingBattery/OnStart");
                enabled = false;
                return;
            }
            if (double.IsNaN(DegradeTime) || double.IsInfinity(DegradeTime))
            {
                KShared.LogError("Part \"" + part.name
                    + "\" has a non-finite DegradeTime; degradation was disabled.",
                    "KhemistryDegradingBattery/OnStart");
                DegradeTime = -1.0;
            }

            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryDegradingBattery but no resource node for " + ResourceName,
                    "KhemistryDegradingBattery/OnStart");
                return;
            }

            if (OriginalMaxAmount < 0.0 || double.IsNaN(OriginalMaxAmount)
                || double.IsInfinity(OriginalMaxAmount))
                OriginalMaxAmount = Math.Max(0.0, resource.maxAmount);
            if (StartTime < 0.0 || double.IsNaN(StartTime) || double.IsInfinity(StartTime))
                StartTime = Planetarium.GetUniversalTime();

            ApplyDegradation(resource);
        }

        public override void OnUpdate()
        {
            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null) return;
            ApplyDegradation(resource);
        }

        private void ApplyDegradation(PartResource resource)
        {
            if (resource == null || DegradeTime <= 0.0
                || double.IsNaN(DegradeTime) || double.IsInfinity(DegradeTime)
                || double.IsNaN(OriginalMaxAmount) || double.IsInfinity(OriginalMaxAmount)
                || OriginalMaxAmount < 0.0
                || double.IsNaN(StartTime) || double.IsInfinity(StartTime)) return;

            double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
            double degradeSeconds = DegradeTime * 60.0;
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                || double.IsNaN(degradeSeconds) || double.IsInfinity(degradeSeconds)
                || degradeSeconds <= 0.0) return;
            double fraction = Math.Min(1.0, Math.Max(0.0,
                1.0 - (elapsedSeconds / degradeSeconds)));
            double newMax = OriginalMaxAmount * fraction;

            resource.maxAmount = newMax;
            resource.amount = Math.Max(0.0, Math.Min(resource.amount, resource.maxAmount));

            HealthDisplay = string.Format("Battery Life: {0:F1}%", fraction * 100.0);
            double remaining = Math.Max(0, degradeSeconds - elapsedSeconds);
            HealthTimeDisplay = string.Format("Time until 0%: {0:F0}s", remaining);
        }
    }

    /// <summary>
    /// An ISRU that is both an EVA ISRU and a <see cref="KhemistryFluidCell"/>.
    /// This is used in EVA parts that must work from a kerbal's inventory.
    /// !TODO: Merge into KhemistryISRU
    /// </summary>
    public class KhemistryEVACombinedProcessor : PartModule
    {
        [KSPField(isPersistant = true)]
        public string storedResourcesData = "";

        [KSPField(isPersistant = true)]
        public bool isRunning = false;

        [KSPField(isPersistant = true)]
        public string activeConverterName = "";

        [KSPField(isPersistant = false)]
        public float maxTotalStorage = 200f;

        [KSPField(isPersistant = false)]
        public float transferDistance = 10f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryprocessor",
                  groupDisplayName = "Processor", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Converter", groupName = "khemistryprocessor")]
        public string converterDisplay = "Stopped";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Capacity", groupName = "khemistryprocessor")]
        public string capacityDisplay = "0 / 0";

        public struct ProcessorConverter
        {
            public string name;
            public List<(string resourceName, double ratio)> inputs;
            public List<(string resourceName, double ratio)> outputs;
        }

        private readonly List<string> _supportedResources = new List<string>();
        private readonly List<ProcessorConverter> _converters = new List<ProcessorConverter>();
        private bool _fatalConfigError = false;

        public bool IsConfigLoaded => !_fatalConfigError;
        public List<string> SupportedResources => _supportedResources;
        public List<ProcessorConverter> Converters => _converters;
        public float MaxTotalStorage => maxTotalStorage;
        public float TransferDistance => transferDistance;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // A vessel/inventory save node contains only persistent fields. Keep the parsed
            // prefab configuration in that case instead of clearing it on every reload.
            if (node == null || (!node.HasNode("SUPPORTED_RESOURCES")
                && !node.HasNode("CONVERTER"))) return;

            _supportedResources.Clear();
            _converters.Clear();
            _fatalConfigError = false;

            if (float.TryParse(node.GetValue("maxTotalStorage"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float tmp)) maxTotalStorage = tmp;
            if (float.TryParse(node.GetValue("transferDistance"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out tmp)) transferDistance = tmp;
            if (float.IsNaN(maxTotalStorage) || float.IsInfinity(maxTotalStorage) || maxTotalStorage <= 0f
                || float.IsNaN(transferDistance) || float.IsInfinity(transferDistance) || transferDistance < 0f)
            {
                KShared.LogError("Part \"" + part.name
                    + "\" has invalid processor storage or transfer-distance settings.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }

            if (!node.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryEVACombinedProcessor but no SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
            {
                string resourceName = n.Trim();
                if (string.IsNullOrEmpty(resourceName) || _supportedResources.Contains(resourceName))
                    continue;
                if (PartResourceLibrary.Instance != null
                    && PartResourceLibrary.Instance.GetDefinition(resourceName) == null)
                {
                    KShared.LogError("Unknown processor resource \"" + resourceName + "\" was ignored.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }
                _supportedResources.Add(resourceName);
            }

            if (_supportedResources.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has an empty SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }

            foreach (ConfigNode convNode in node.GetNodes("CONVERTER"))
            {
                string convName = convNode.GetValue("ConverterName")?.Trim();
                if (string.IsNullOrEmpty(convName))
                {
                    KShared.LogError("A CONVERTER node is missing ConverterName, skipping.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }
                if (_converters.Exists(existing => existing.name == convName))
                {
                    KShared.LogError("Duplicate processor ConverterName \"" + convName
                        + "\"; the later converter was skipped.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }

                var conv = new ProcessorConverter
                {
                    name = convName,
                    inputs = new List<(string, double)>(),
                    outputs = new List<(string, double)>()
                };

                bool converterValid = true;
                foreach (ConfigNode inputNode in convNode.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("ResourceName")?.Trim();
                    if (!TryLoadRatio(inputNode, convName, "input", out double ratio)
                        || !_supportedResources.Contains(resName))
                    {
                        if (!string.IsNullOrEmpty(resName) && !_supportedResources.Contains(resName))
                            KShared.LogError("Processor converter \"" + convName
                                + "\" uses unsupported input resource \"" + resName + "\".",
                                "KhemistryEVACombinedProcessor/OnLoad");
                        converterValid = false;
                        continue;
                    }
                    conv.inputs.Add((resName, ratio));
                }

                foreach (ConfigNode outputNode in convNode.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("ResourceName")?.Trim();
                    if (!TryLoadRatio(outputNode, convName, "output", out double ratio)
                        || !_supportedResources.Contains(resName))
                    {
                        if (!string.IsNullOrEmpty(resName) && !_supportedResources.Contains(resName))
                            KShared.LogError("Processor converter \"" + convName
                                + "\" uses unsupported output resource \"" + resName + "\".",
                                "KhemistryEVACombinedProcessor/OnLoad");
                        converterValid = false;
                        continue;
                    }
                    conv.outputs.Add((resName, ratio));
                }

                if (!converterValid || conv.inputs.Count == 0 || conv.outputs.Count == 0)
                {
                    KShared.LogError("Processor converter \"" + convName
                        + "\" is incomplete or invalid and was skipped.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }
                _converters.Add(conv);
            }

            KShared.Log(
                string.Format("OnLoad: {0} supported resources, {1} converters, maxStorage={2}, transferDist={3}",
                    _supportedResources.Count, _converters.Count, maxTotalStorage, transferDistance),
                "KhemistryEVACombinedProcessor/OnLoad");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (_supportedResources.Count == 0)
            {
                KhemistryEVACombinedProcessor prefab = part.partInfo?.partPrefab
                    ?.FindModuleImplementing<KhemistryEVACombinedProcessor>();
                if (prefab != null && prefab != this && prefab._supportedResources.Count > 0)
                {
                    _supportedResources.AddRange(prefab._supportedResources);
                    _converters.AddRange(prefab._converters);
                    _fatalConfigError = prefab._fatalConfigError;
                }
            }

            if (_fatalConfigError)
            {
                contentsDisplay = "ERROR: see log";
                converterDisplay = "ERROR";
                return;
            }

            Dictionary<string, double> restored = Deserialize(storedResourcesData);
            storedResourcesData = Serialize(restored);
            if (isRunning && !_converters.Exists(converter => converter.name == activeConverterName))
            {
                isRunning = false;
                activeConverterName = "";
            }
            UpdateDisplay(restored);
        }

        public static Dictionary<string, double> Deserialize(string data)
        {
            var dict = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(data)) return dict;
            foreach (string entry in data.Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int sep = entry.IndexOf(':');
                if (sep < 1) continue;
                string name = entry.Substring(0, sep).Trim();
                if (double.TryParse(entry.Substring(sep + 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)
                    && amount > 0.0 && !double.IsNaN(amount) && !double.IsInfinity(amount))
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    dict.TryGetValue(name, out double existing);
                    double combined = existing + amount;
                    if (!double.IsNaN(combined) && !double.IsInfinity(combined))
                        dict[name] = combined;
                }
            }
            return dict;
        }

        public static string Serialize(Dictionary<string, double> dict)
        {
            var parts = new List<string>();
            foreach (var kvp in dict)
                if (kvp.Value > 0.0)
                    if (!double.IsNaN(kvp.Value) && !double.IsInfinity(kvp.Value))
                        parts.Add(kvp.Key + ":" + kvp.Value.ToString("R", CultureInfo.InvariantCulture));
            return string.Join("|", parts.ToArray());
        }

        public static double GetTotal(Dictionary<string, double> dict)
        {
            if (dict == null) return 0.0;
            double total = 0.0;
            foreach (var kvp in dict)
            {
                total += kvp.Value;
                if (double.IsNaN(total) || double.IsInfinity(total)) return total;
            }
            return total;
        }

        public bool RunConversionCycle(Dictionary<string, double> resources,
            string converterName, double dt)
        {
            try
            {
                if (resources == null || double.IsNaN(dt) || double.IsInfinity(dt)) return false;
                if (dt <= 0.0) return true;
                foreach (KeyValuePair<string, double> resource in resources)
                    if (!_supportedResources.Contains(resource.Key)
                        || resource.Value < 0.0 || double.IsNaN(resource.Value)
                        || double.IsInfinity(resource.Value))
                        return false;

                ProcessorConverter? found = null;
                foreach (var conv in _converters)
                    if (conv.name == converterName) { found = conv; break; }

                if (found == null) return false;
                var c = found.Value;

                Dictionary<string, double> inputs = AggregateRatios(c.inputs, dt);
                Dictionary<string, double> outputs = AggregateRatios(c.outputs, dt);
                Dictionary<string, double> candidate = new Dictionary<string, double>(resources);

                foreach (KeyValuePair<string, double> input in inputs)
                {
                    if (!candidate.TryGetValue(input.Key, out double available)
                        || available + 1e-12 < input.Value)
                        return false;
                }

                foreach (KeyValuePair<string, double> input in inputs)
                {
                    double remaining = candidate[input.Key] - input.Value;
                    if (remaining < 1e-12) candidate.Remove(input.Key);
                    else candidate[input.Key] = remaining;
                }

                foreach (KeyValuePair<string, double> output in outputs)
                {
                    candidate.TryGetValue(output.Key, out double existing);
                    double combined = existing + output.Value;
                    if (double.IsNaN(combined) || double.IsInfinity(combined)) return false;
                    candidate[output.Key] = combined;
                }

                double finalTotal = GetTotal(candidate);
                if (double.IsNaN(finalTotal) || double.IsInfinity(finalTotal)
                    || finalTotal > maxTotalStorage + 1e-9)
                    return false;

                resources.Clear();
                foreach (KeyValuePair<string, double> value in candidate)
                    resources[value.Key] = value.Value;

                return true;
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured, returning cycle failure. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryEVACombinedProcessor/RunConversionCycle");
                return false;
            }
        }

        private static bool TryLoadRatio(ConfigNode node, string converterName, string kind,
            out double ratio)
        {
            string resourceName = node.GetValue("ResourceName")?.Trim();
            string rawRatio = node.GetValue("Ratio");
            if (!string.IsNullOrEmpty(resourceName)
                && double.TryParse(rawRatio, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio)
                && ratio > 0.0 && !double.IsNaN(ratio) && !double.IsInfinity(ratio))
                return true;

            KShared.LogError("Processor converter \"" + converterName + "\" has an invalid "
                + kind + " resource or ratio (resource=\"" + (resourceName ?? "")
                + "\", ratio=\"" + (rawRatio ?? "") + "\").",
                "KhemistryEVACombinedProcessor/OnLoad");
            ratio = 0.0;
            return false;
        }

        private static Dictionary<string, double> AggregateRatios(
            List<(string resourceName, double ratio)> entries, double dt)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (var entry in entries)
            {
                result.TryGetValue(entry.resourceName, out double current);
                result[entry.resourceName] = current + entry.ratio * dt;
            }
            return result;
        }

        public void UpdateDisplay(Dictionary<string, double> resources)
        {
            double total = GetTotal(resources);

            if (resources.Count == 0)
                contentsDisplay = "Empty";
            else
            {
                var parts = new List<string>();
                foreach (var kvp in resources)
                    parts.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                contentsDisplay = string.Join(", ", parts.ToArray());
            }

            capacityDisplay = string.Format("{0:F2} / {1:F2}", total, maxTotalStorage);

            converterDisplay = (isRunning && !string.IsNullOrEmpty(activeConverterName))
                ? "Running: " + activeConverterName
                : "Stopped";
        }
    }
}
