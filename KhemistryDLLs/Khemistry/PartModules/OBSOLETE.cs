using System;
using System.Collections.Generic;

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

            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryDegradingBattery but no resource node for " + ResourceName,
                    "KhemistryDegradingBattery/OnStart");
                return;
            }

            if (OriginalMaxAmount < 0) OriginalMaxAmount = resource.maxAmount;
            if (StartTime < 0) StartTime = Planetarium.GetUniversalTime();

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
            if (DegradeTime <= 0) return;

            double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
            double degradeSeconds = DegradeTime * 60.0;
            double fraction = Math.Max(0.0, 1.0 - (elapsedSeconds / degradeSeconds));
            double newMax = OriginalMaxAmount * fraction;

            resource.maxAmount = newMax;
            if (resource.amount > resource.maxAmount)
                resource.amount = resource.maxAmount;

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

            _supportedResources.Clear();
            _converters.Clear();
            _fatalConfigError = false;

            if (float.TryParse(node.GetValue("maxTotalStorage"), out float tmp)) maxTotalStorage = tmp;
            if (float.TryParse(node.GetValue("transferDistance"), out tmp)) transferDistance = tmp;

            if (!node.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryEVACombinedProcessor but no SUPPORTED_RESOURCES node.",
                    "KhemistryEVACombinedProcessor/OnLoad");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in node.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());

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
                string convName = convNode.GetValue("ConverterName");
                if (string.IsNullOrEmpty(convName))
                {
                    KShared.LogError("A CONVERTER node is missing ConverterName, skipping.",
                        "KhemistryEVACombinedProcessor/OnLoad");
                    continue;
                }

                var conv = new ProcessorConverter
                {
                    name = convName,
                    inputs = new List<(string, double)>(),
                    outputs = new List<(string, double)>()
                };

                foreach (ConfigNode inputNode in convNode.GetNodes("INPUT_RESOURCE"))
                {
                    string resName = inputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double.TryParse(inputNode.GetValue("Ratio"), out double ratio);
                    conv.inputs.Add((resName, ratio));
                }

                foreach (ConfigNode outputNode in convNode.GetNodes("OUTPUT_RESOURCE"))
                {
                    string resName = outputNode.GetValue("ResourceName");
                    if (string.IsNullOrEmpty(resName)) continue;
                    double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                    conv.outputs.Add((resName, ratio));
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

            if (_fatalConfigError)
            {
                contentsDisplay = "ERROR: see log";
                converterDisplay = "ERROR";
                return;
            }

            UpdateDisplay(Deserialize(storedResourcesData));
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
                string name = entry.Substring(0, sep);
                if (double.TryParse(entry.Substring(sep + 1), out double amount) && amount > 0.0)
                    dict[name] = amount;
            }
            return dict;
        }

        public static string Serialize(Dictionary<string, double> dict)
        {
            var parts = new List<string>();
            foreach (var kvp in dict)
                if (kvp.Value > 0.0)
                    parts.Add(kvp.Key + ":" + kvp.Value.ToString("F4"));
            return string.Join("|", parts.ToArray());
        }

        public static double GetTotal(Dictionary<string, double> dict)
        {
            double total = 0.0;
            foreach (var kvp in dict) total += kvp.Value;
            return total;
        }

        public bool RunConversionCycle(Dictionary<string, double> resources,
            string converterName, double dt)
        {
            try
            {
                ProcessorConverter? found = null;
                foreach (var conv in _converters)
                    if (conv.name == converterName) { found = conv; break; }

                if (found == null) return false;
                var c = found.Value;

                foreach (var (resourceName, ratio) in c.inputs)
                {
                    double needed = ratio * dt;
                    if (!resources.TryGetValue(resourceName, out double available)
                        || available < needed * 0.999)
                        return false;
                }

                double inputSum = 0.0;
                foreach (var (resourceName, ratio) in c.inputs) inputSum += ratio * dt;
                double outputSum = 0.0;
                foreach (var (resourceName, ratio) in c.outputs) outputSum += ratio * dt;
                double currentTotal = GetTotal(resources);
                if (currentTotal - inputSum + outputSum > maxTotalStorage)
                    return false;

                foreach (var (resourceName, ratio) in c.inputs)
                {
                    double needed = ratio * dt;
                    resources[resourceName] -= needed;
                    if (resources[resourceName] < 1e-9)
                        resources.Remove(resourceName);
                }

                foreach (var (resourceName, ratio) in c.outputs)
                {
                    resources.TryGetValue(resourceName, out double existing);
                    resources[resourceName] = existing + ratio * dt;
                }

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
