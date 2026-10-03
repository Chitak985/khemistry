using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryAdvancedStorage
    {
        private readonly StorageEnvironment _environment = new StorageEnvironment();
        private readonly Dictionary<string, StorageMultipliers> _resourceMultipliers = new Dictionary<string, StorageMultipliers>();
        private double ResourceMultiplier(string name) => storageType == "multi"
            && activeResource != null && _resourceMultipliers.TryGetValue(activeResource, out var multipliers) ? multipliers[name] : 1.0;
        private double EffectiveMultiplier(string name) => KShared.Multiply(_environment.Multiplier(name), ResourceMultiplier(name));
        private double EffectiveInputRate => maxInputRate < 0 ? (EffectiveMultiplier("maxInputRateMul") == 0 ? 0 : -1)
            : KShared.Multiply(maxInputRate, EffectiveMultiplier("maxInputRateMul"));
        private double EffectiveOutputRate => maxOutputRate < 0 ? (EffectiveMultiplier("maxOutputRateMul") == 0 ? 0 : -1)
            : KShared.Multiply(maxOutputRate, EffectiveMultiplier("maxOutputRateMul"));
        private double EffectiveChargeRate => KShared.Multiply(chargeRate, EffectiveMultiplier("chargeRateMul"));
        private double EffectiveChargeDecayRate => KShared.Multiply(chargeDecayRate, EffectiveMultiplier("chargeDecayRateMul"));
        private double EffectivePassiveMultiplier => EffectiveMultiplier("passiveConsumptionRateMul");
        private double EffectiveCapacity => GetResourceCapacity(storageType == "multiShared"
            ? _supportedResources.FirstOrDefault() : activeResource);

        private bool CheckStorageEnvironment()
        {
            _environment.Update(part);
            if (!_environment.Configured || !HighLogic.LoadedSceneIsFlight || part?.vessel == null) return true;
            double capacity = EffectiveCapacity;
            bool overfilled = _resources.Values.Sum() > capacity;
            if ((!_environment.Operational || overfilled) && HasAnyStoredResources())
            {
                _resources.Clear(); _unreadableContents.Clear();
                foreach (var input in _storagePassiveInputs) input.elapsed = input.consumed = 0;
                _passivePaused = false;
                StorageEnvironment.NotifyVoid(part, _environment.Reason ?? "biome capacity exceeded");
            }
            return _environment.Operational;
        }
    }
}
