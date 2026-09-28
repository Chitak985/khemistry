namespace Khemistry
{
    public partial class KhemistryMaterialStorage
    {
        private readonly StorageEnvironment _environment = new StorageEnvironment();
        private bool _restoringContents;
        public double EffectiveVolume => StorageMultipliers.Multiply(volume, _environment.Multiplier("volumeMul"));
        public bool TransfersEnabled => !_fatalConfigError && CheckStorageEnvironment();
        private bool CheckStorageEnvironment()
        {
            if (_restoringContents) return true;
            _environment.Update(part);
            if (!_environment.Configured || !HighLogic.LoadedSceneIsFlight || part?.vessel == null) return true;
            double used = ComputeCurrentVolume();
            if ((!_environment.Operational || used > EffectiveVolume + System.Math.Max(1e-12, EffectiveVolume * 1e-6))
                && (contents.Count > 0 || _pendingSavedContents.Count > 0))
            {
                contents.Clear(); _pendingSavedContents.Clear();
                StorageEnvironment.NotifyVoid(part, _environment.Reason ?? "biome capacity exceeded");
            }
            return _environment.Operational;
        }
    }
}
