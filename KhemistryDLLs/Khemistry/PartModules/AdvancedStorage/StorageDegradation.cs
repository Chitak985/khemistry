using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage
    {
        private const string DegradationStateNode = "DEGRADATION_STATE";
        private static readonly HashSet<string> DegradationReadOnly = new HashSet<string>(
            new[] { "maxCapacity", "temp", "pressure", "accelG", "altitude", "dt",
                "storedAmount", "throughputIn", "throughputOut", "isInputting",
                "isOutputting", "fillPercentage" }, StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> _degradationEntries =
            new List<KeyValuePair<string, string>>();
        private Dictionary<string, double> _degradationValues =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private ConfigNode _pendingDegradationState;
        private bool _degradationConfigured;
        private double _degradationInput, _degradationOutput;

        private double DegradationCapacity => _degradationConfigured
            ? _degradationValues["currentCapacity"] : maximumResources;

        private bool DegradationError(string message)
        {
            KShared.LogError(message + " Storage disabled.",
                "KhemistryAdvancedStorage/Degradation");
            _fatalConfigError = true;
            contentsDisplay = "ERROR: see log";
            foreach (BaseEvent e in Events) e.active = false;
            return false;
        }

        private bool LoadDegradation(ConfigNode module)
        {
            _degradationConfigured = false;
            _degradationEntries.Clear();
            _degradationValues.Clear();
            _degradationInput = _degradationOutput = 0;
            ConfigNode[] nodes = module.GetNodes("DEGRADATION");
            if (nodes.Length == 0) return true;
            if (nodes.Length != 1 || storageType != "single")
                return DegradationError("Exactly one DEGRADATION node is allowed, only on storageType=single.");
            foreach (ConfigNode.Value entry in nodes[0].values)
            {
                string name = entry.name?.Trim();
                if (!KMathExpr.IsVariableName(name) || DegradationReadOnly.Contains(name)
                    || _degradationValues.ContainsKey(name))
                    return DegradationError("Invalid, reserved, or duplicate degradation variable: " + name);
                _degradationEntries.Add(new KeyValuePair<string, string>(name, entry.value));
                _degradationValues.Add(name, string.Equals(name, "currentCapacity",
                    StringComparison.OrdinalIgnoreCase) ? maximumResources : 0);
            }
            if (!_degradationValues.ContainsKey("currentCapacity"))
                return DegradationError("DEGRADATION requires currentCapacity.");
            var names = DegradationReadOnly.Concat(_degradationValues.Keys).ToArray();
            foreach (var entry in _degradationEntries)
                if (!KMathExpr.TryValidate(entry.Value, names, out string error))
                    return DegradationError("Cannot parse " + entry.Key + ": " + error);
            if (_pendingDegradationState != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ConfigNode saved in _pendingDegradationState.GetNodes("VALUE"))
                {
                    string name = saved.GetValue("name")?.Trim();
                    if (name == null || !_degradationValues.ContainsKey(name)) continue;
                    if (!seen.Add(name) || !TryReadDegradationNumber(saved.GetValue("amount"), out double value))
                        return DegradationError("Invalid saved degradation value: " + name);
                    _degradationValues[name] = value;
                }
                if (!ReadSavedThroughput("throughputIn", out _degradationInput)
                    || !ReadSavedThroughput("throughputOut", out _degradationOutput))
                    return DegradationError("Invalid saved degradation throughput.");
            }
            _degradationValues["currentCapacity"] = Math.Max(0,
                Math.Min(maximumResources, _degradationValues["currentCapacity"]));
            _degradationConfigured = true;
            _pendingDegradationState = null;
            return true;
        }

        private static bool TryReadDegradationNumber(string raw, out double value)
            => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && KShared.IsFinite(value);

        private bool ReadSavedThroughput(string key, out double value)
        {
            value = 0;
            string raw = _pendingDegradationState.GetValue(key);
            return raw == null || (TryReadDegradationNumber(raw, out value) && value >= 0);
        }

        private void LoadDegradationState(ConfigNode node)
        {
            _degradationConfigured = false;
            _degradationInput = _degradationOutput = 0;
            _pendingDegradationState = null;
            ConfigNode saved = node.GetNode(DegradationStateNode);
            if (saved == null) return;
            _pendingDegradationState = new ConfigNode(DegradationStateNode);
            saved.CopyTo(_pendingDegradationState);
        }

        private void SaveDegradationState(ConfigNode node)
        {
            node.RemoveNodes(DegradationStateNode);
            if (!_degradationConfigured)
            {
                // Preserve a load/save performed before OnStart/config resolution.
                if (_pendingDegradationState != null)
                {
                    var copy = new ConfigNode(DegradationStateNode);
                    _pendingDegradationState.CopyTo(copy);
                    node.AddNode(copy);
                }
                return;
            }
            ConfigNode stateNode = node.AddNode(DegradationStateNode);
            stateNode.AddValue("throughputIn", _degradationInput.ToString("R", CultureInfo.InvariantCulture));
            stateNode.AddValue("throughputOut", _degradationOutput.ToString("R", CultureInfo.InvariantCulture));
            foreach (var pair in _degradationValues)
            {
                ConfigNode value = stateNode.AddNode("VALUE");
                value.AddValue("name", pair.Key);
                value.AddValue("amount", pair.Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private void ProcessDegradation(double dt)
        {
            if (!_degradationConfigured || _fatalConfigError || dt <= 0 || !KShared.IsFinite(dt)) return;
            var environment = new KhemistryRuntimeData(part?.vessel);
            double stored = _resources.Values.Sum();
            double capacity = DegradationCapacity;
            // Snapshot built-ins once; custom assignments become visible immediately below.
            var variables = _degradationValues.ToDictionary(pair => pair.Key,
                pair => pair.Value.ToString("R", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
            var builtins = new Dictionary<string, double>
            {
                ["maxCapacity"] = maximumResources, ["temp"] = environment.temperature,
                ["pressure"] = environment.pressure, ["accelG"] = environment.g,
                ["altitude"] = environment.alt, ["dt"] = dt, ["storedAmount"] = stored,
                ["throughputIn"] = _degradationInput, ["throughputOut"] = _degradationOutput,
                ["isInputting"] = _degradationInput > 0 ? 1 : 0,
                ["isOutputting"] = _degradationOutput > 0 ? 1 : 0,
                ["fillPercentage"] = capacity > 0 ? Math.Max(0, Math.Min(1, stored / capacity))
                    : (stored > 0 ? 1 : 0)
            };
            foreach (var pair in builtins)
                variables[pair.Key] = pair.Value.ToString("R", CultureInfo.InvariantCulture);
            var next = new Dictionary<string, double>(_degradationValues, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _degradationEntries)
            {
                if (!KMathExpr.TryEvaluate(entry.Value, out double value, out string error, variables))
                {
                    DegradationError("Cannot evaluate " + entry.Key + ": " + error);
                    return; // No partial variable/capacity commit on failure.
                }
                next[entry.Key] = value;
                variables[entry.Key] = value.ToString("R", CultureInfo.InvariantCulture);
            }
            next["currentCapacity"] = Math.Max(0, Math.Min(maximumResources, next["currentCapacity"]));
            double previousCapacity = EffectiveCapacity;
            _degradationValues = next;
            double newCapacity = EffectiveCapacity;
            if (newCapacity < previousCapacity || stored > newCapacity)
            {
                double excess = Math.Max(0, stored - newCapacity);
                foreach (string name in _resources.Keys.ToArray())
                {
                    double discard = Math.Min(excess, _resources[name]);
                    _resources[name] -= discard;
                    excess -= discard;
                    if (_resources[name] <= 0) _resources.Remove(name);
                    if (excess <= 0) break;
                }
            }
            // Capacity loss is not resource throughput.
            _degradationInput = _degradationOutput = 0;
        }

        private void TrackDegradationTransfer(double moved, bool undo = false)
        {
            if (!_degradationConfigured) return;
            double delta = Math.Abs(moved) * (undo ? -1 : 1);
            if (moved > 0) _degradationOutput = Math.Max(0, _degradationOutput + delta);
            else _degradationInput = Math.Max(0, _degradationInput + delta);
        }
    }
}
