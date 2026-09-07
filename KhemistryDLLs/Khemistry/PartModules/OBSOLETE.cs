using System;

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

}
