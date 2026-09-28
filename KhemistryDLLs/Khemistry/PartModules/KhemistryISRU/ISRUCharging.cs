using System;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        private double _chargingSeconds;
        private double _secondsBelowThreshold;

        private KhemistryISRUBiomeConfig ChargingBiome =>
            _activeRecipe != null && _runtimeData != null ? GetEffectiveBiomeConfig() : null;

        protected double EffectiveChargeThreshold =>
            Math.Max(0, Math.Min(100, chargeThreshold * (ChargingBiome?.chargeThresholdMultiplier ?? 1)));

        protected bool CanOperate => state != KShared.ChargablePartState.Off
            && (!chargingRequired || (chargePercent >= EffectiveChargeThreshold
                && (state != KShared.ChargablePartState.Charging || chargeWhileRunning)));

        public void HandleCharging(double dt)
        {
            _chargingSeconds = 0;
            _secondsBelowThreshold = chargingRequired && chargePercent < EffectiveChargeThreshold ? dt : 0;
            if (!chargingRequired || double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0) return;
            if (state != KShared.ChargablePartState.Charging) return;
            if (chargePercent >= 100)
            {
                chargePercent = 100;
                state = KShared.ChargablePartState.On;
                return;
            }
            var biome = ChargingBiome;
            double rate = chargeRate * (biome?.chargeRateMultiplier ?? 1);
            double consumption = biome?.chargeConsumptionMultiplier ?? 1;
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0
                || double.IsNaN(consumption) || double.IsInfinity(consumption) || consumption < 0)
            {
                statusDisplay = "Charging unavailable here";
                return;
            }
            double seconds = Math.Min(dt, (100 - chargePercent) / rate);
            if (!ConsumeVesselResources(_chargeNames,
                _chargeAmounts.Select(amount => (double)amount * consumption).ToList(), seconds))
                return;
            _chargingSeconds = seconds;
            _secondsBelowThreshold = Math.Max(0, Math.Min(dt,
                (EffectiveChargeThreshold - chargePercent) / rate));
            chargePercent = (float)Math.Min(100, chargePercent + rate * seconds);
            if (chargePercent >= 100f - 1e-5f)
            {
                chargePercent = 100;
                state = KShared.ChargablePartState.On;
            }
        }

        private double RecipeTimeAvailable(double dt) =>
            Math.Max(0, dt - (chargeWhileRunning ? _secondsBelowThreshold : _chargingSeconds));

        // Called after processing so stalled recipes count as inactive, not active.
        private void ApplyChargeDecay(double dt, double workingSeconds)
        {
            if (!chargingRequired || dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;
            var biome = ChargingBiome;
            double loss;
            if (state == KShared.ChargablePartState.Off || chargePercent < EffectiveChargeThreshold)
                loss = chargeDecayRate * (biome?.chargeDecayMultiplier ?? 1)
                    * Math.Max(0, dt - _chargingSeconds);
            else
            {
                workingSeconds = Math.Max(0, Math.Min(dt, workingSeconds));
                double idleSeconds = Math.Max(0, dt - _secondsBelowThreshold - workingSeconds);
                loss = chargeDecayRateInactive * (biome?.chargeDecayRateInactiveMultiplier ?? 1) * idleSeconds;
                // Charging and work occupy the same interval only when explicitly allowed.
                double unchargedWork = chargeWhileRunning
                    ? Math.Max(0, workingSeconds - Math.Max(0, _chargingSeconds - _secondsBelowThreshold))
                    : workingSeconds;
                loss += chargeDecayRateActive * unchargedWork;
            }
            if (!double.IsNaN(loss) && loss >= 0)
                chargePercent = (float)Math.Max(0, chargePercent - loss);
        }
    }
}
