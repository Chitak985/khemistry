using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage : IPartMassModifier, IPartCostModifier
    {
        private readonly Dictionary<string, double> _resources =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<ConfigNode> _unreadableContents = new List<ConfigNode>();
        private bool _storageReady;
        private double _rateTick = double.NaN;
        private double _inputThisTick;
        private double _outputThisTick;

        private void MigrateLegacyResources()
        {
            foreach (PartResource resource in part.Resources.ToList())
            {
                if (!_supportedResources.Contains(resource.resourceName)) continue;
                double amount = resource.amount;
                if (!KShared.IsFinite(amount) || amount < 0.0) amount = 0.0;
                double total = GetStoredAmount(resource.resourceName) + amount;
                if (!KShared.IsFinite(total)) continue;
                _resources[resource.resourceName] = total;
                part.RemoveResource(resource);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            LoadStoragePassiveState(node);
            _storageReady = false;
            _resources.Clear();
            _unreadableContents.Clear();
            _rateTick = double.NaN;
            foreach (ConfigNode saved in node.GetNodes("STORED_RESOURCE"))
            {
                string name = saved.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(name)
                    || !double.TryParse(saved.GetValue("amount"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)
                    || !KShared.IsFinite(amount) || amount < 0.0
                    || !KShared.IsFinite(GetStoredAmount(name) + amount))
                {
                    ConfigNode copy = new ConfigNode("STORED_RESOURCE");
                    saved.CopyTo(copy);
                    _unreadableContents.Add(copy);
                    continue;
                }
                _resources[name] = GetStoredAmount(name) + amount;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;
            SaveStoragePassiveState(node);
            node.RemoveNodes("STORED_RESOURCE");
            node.RemoveNodes(SavedFlowStateNodeName);
            foreach (var resource in _resources)
            {
                ConfigNode saved = node.AddNode("STORED_RESOURCE");
                saved.AddValue("name", resource.Key);
                saved.AddValue("amount", resource.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            foreach (ConfigNode saved in _unreadableContents)
            {
                ConfigNode copy = new ConfigNode("STORED_RESOURCE");
                saved.CopyTo(copy);
                node.AddNode(copy);
            }
        }

        public Dictionary<string, double> GetStoredResources()
            => new Dictionary<string, double>(_resources, StringComparer.Ordinal);
        public IEnumerable<string> SupportedResources => _supportedResources.ToArray();
        public double GetStoredAmount(string name)
            => name != null && _resources.TryGetValue(name, out double amount) ? amount : 0.0;
        public bool TransfersEnabled => _storageReady && !_fatalConfigError
            && state == KShared.ChargablePartState.On
            && !_passiveNeedsMaintenance && (!_passivePaused || _processingPassive);

        private void RefreshRateBudget()
        {
            double tick = UnityEngine.Time.fixedTime;
            if (tick == _rateTick) return;
            _rateTick = tick;
            _inputThisTick = _outputThisTick = 0.0;
        }

        public double GetAvailableAmount(string name)
        {
            if (!TransfersEnabled) return 0.0;
            RefreshRateBudget();
            double budget = maxOutputRate < 0f ? double.PositiveInfinity
                : Math.Max(0.0, maxOutputRate * TimeWarp.fixedDeltaTime - _outputThisTick);
            return Math.Min(GetStoredAmount(name), budget);
        }

        public double GetAvailableSpace(string name, double fillAmount = 1.0)
        {
            if (!TransfersEnabled || !_supportedResources.Contains(name)
                || !KShared.IsFinite(fillAmount) || fillAmount <= 0.0
                || _unreadableContents.Count > 0) return 0.0;
            if (storageType != "multiShared" && name != activeResource) return 0.0;
            if (storageType != "multiShared"
                && _resources.Any(pair => pair.Key != name && pair.Value > 0.0)) return 0.0;
            RefreshRateBudget();
            double capacity = GetResourceCapacity(name) * Math.Min(1.0, fillAmount);
            double space = Math.Max(0.0, capacity - _resources.Values.Sum());
            double budget = maxInputRate < 0f ? double.PositiveInfinity
                : Math.Max(0.0, maxInputRate * TimeWarp.fixedDeltaTime - _inputThisTick);
            return Math.Min(space, budget);
        }

        /// <summary>Positive consumes, negative produces. Returns the signed amount moved.</summary>
        public double RequestStoredResource(string name, double amount)
        {
            if (string.IsNullOrEmpty(name) || !KShared.IsFinite(amount) || amount == 0.0)
                return 0.0;
            double moved = amount > 0.0 ? Math.Min(amount, GetAvailableAmount(name))
                : -Math.Min(-amount, GetAvailableSpace(name));
            if (moved == 0.0) return 0.0;
            double remaining = GetStoredAmount(name) - moved;
            if (remaining == 0.0) _resources.Remove(name);
            else _resources[name] = remaining;
            if (moved > 0.0) _outputThisTick += moved;
            else _inputThisTick -= moved;
            return moved;
        }

        public double TakeResource(string name, double amount)
            => amount > 0.0 ? RequestStoredResource(name, amount) : 0.0;
        public double PutResource(string name, double amount)
            => amount > 0.0 ? -RequestStoredResource(name, -amount) : 0.0;

        // Exact synchronous rollback only. Returning an accepted transfer must not be
        // prevented by a rate limit, full tank, or the opposite direction being blocked.
        internal void UndoTransfer(string name, double moved)
        {
            _resources[name] = GetStoredAmount(name) + moved;
            if (_resources[name] == 0.0) _resources.Remove(name);
            if (moved > 0.0) _outputThisTick = Math.Max(0.0, _outputThisTick - moved);
            else _inputThisTick = Math.Max(0.0, _inputThisTick + moved);
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation)
            => (float)_resources.Sum(pair => pair.Value
                * (PartResourceLibrary.Instance?.GetDefinition(pair.Key)?.density ?? 0.0));
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;
        public float GetModuleCost(float defaultCost, ModifierStagingSituation situation)
            => (float)_resources.Sum(pair => pair.Value
                * (PartResourceLibrary.Instance?.GetDefinition(pair.Key)?.unitCost ?? 0.0));
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.CONSTANTLY;
    }
}
