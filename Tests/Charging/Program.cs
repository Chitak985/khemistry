using System;
using System.Collections.Generic;
using Khemistry;

namespace Khemistry
{
    public static class KShared { public enum ChargablePartState { Off, On, Charging } }
    public class KhemistryISRUBiomeConfig
    {
        public double chargeThresholdMultiplier = 1, chargeRateMultiplier = 1,
            chargeConsumptionMultiplier = 1, chargeDecayMultiplier = 1,
            chargeDecayRateInactiveMultiplier = 1;
    }
    public partial class KhemistryISRU
    {
        public bool chargingRequired = true, chargeWhileRunning, resourcesAvailable = true;
        public float chargeThreshold = 90, chargePercent, chargeRate = 10, chargeDecayRate = 2,
            chargeDecayRateInactive, chargeDecayRateActive;
        public string statusDisplay;
        public KShared.ChargablePartState state = KShared.ChargablePartState.On;
        private object _activeRecipe = new object(), _runtimeData = new object();
        private List<string> _chargeNames = new List<string> { "Power" };
        private List<float> _chargeAmounts = new List<float> { 1 };
        public KhemistryISRUBiomeConfig biome = new KhemistryISRUBiomeConfig();
        private KhemistryISRUBiomeConfig GetEffectiveBiomeConfig() => biome;
        public double consumed;
        private bool ConsumeVesselResources(List<string> names, List<double> amounts, double dt)
        { if (resourcesAvailable) consumed += amounts[0] * dt; return resourcesAvailable; }
        public bool Ready => CanOperate;
        public void Tick(double dt, bool working = false)
        {
            HandleCharging(dt);
            ApplyChargeDecay(dt, working && CanOperate ? RecipeTimeAvailable(dt) : 0);
        }
    }
}
class Program
{
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.001, message + ": " + actual);
    static int Main()
    {
        try
        {
            var c = new KhemistryISRU { chargePercent = 90 };
            Check(c.Ready, "Threshold is inclusive");
            c.chargePercent = 89; Check(!c.Ready, "Below threshold blocks work");
            c.Tick(1); Near(c.chargePercent, 87, "Uncharged normal decay");
            c.chargePercent = 95; c.Tick(1); Near(c.chargePercent, 95, "Idle default zero");
            c.chargeDecayRateInactive = 1; c.Tick(1); Near(c.chargePercent, 94, "Idle decay");
            c.chargeDecayRateActive = 3; c.Tick(1, true); Near(c.chargePercent, 91, "Active decay");
            c.state = KShared.ChargablePartState.Off; c.Tick(1, true);
            Near(c.chargePercent, 89, "Off normal decay"); Check(!c.Ready, "Off never ready");
            c.state = KShared.ChargablePartState.Charging; c.chargePercent = 50; c.Tick(1, true);
            Near(c.chargePercent, 60, "Below threshold charging without decay");
            Check(!c.Ready, "Below threshold cannot work");
            c.chargePercent = 90; Check(!c.Ready, "Charging excludes work by default");
            c.chargeWhileRunning = true; Check(c.Ready, "Concurrent work allowed");
            c.chargeRate = 1; c.Tick(1, true); Near(c.chargePercent, 91, "No active decay during charging");
            c.Tick(1); Near(c.chargePercent, 91, "Charging idle loses inactive rate");
            c.resourcesAvailable = false; c.Tick(1, true); Near(c.chargePercent, 88, "Failed charge active decay");
            c.Tick(1, true); Near(c.chargePercent, 86, "Failed charge below threshold normal decay");
            c.resourcesAvailable = true; c.chargeRate = 0; c.Tick(1);
            Near(c.chargePercent, 84, "Unavailable charging still decays");
            c.state = KShared.ChargablePartState.On; c.biome.chargeThresholdMultiplier = 0.5;
            Check(c.Ready, "Biome threshold multiplier");
            c.biome.chargeDecayRateInactiveMultiplier = 2; c.Tick(1);
            Near(c.chargePercent, 82, "Biome inactive multiplier");
            c.biome.chargeThresholdMultiplier = 3; c.chargePercent = 100;
            Check(c.Ready, "Effective threshold clamped to 100");
            c = new KhemistryISRU { chargePercent = 99, state = KShared.ChargablePartState.Charging,
                chargeDecayRateActive = 2 };
            c.Tick(1, true);
            Near(c.consumed, 0.1, "Charge consumption limited to time needed");
            Near(c.chargePercent, 98.2, "Active decay only after charging ends");
            c = new KhemistryISRU { chargePercent = 50, chargingRequired = false };
            c.Tick(1, true); Near(c.chargePercent, 50, "Noncharging converter unchanged");
            c = new KhemistryISRU { chargePercent = 89, chargeRate = 2,
                state = KShared.ChargablePartState.Charging, chargeDecayRateInactive = 1 };
            c.Tick(1); Near(c.chargePercent, 90.5, "Idle decay starts at threshold, not before");
            c = new KhemistryISRU { chargePercent = 95, chargeRate = 2,
                state = KShared.ChargablePartState.Charging, chargeDecayRateActive = 5 };
            c.Tick(1, true); Near(c.chargePercent, 97, "Exclusive charging does not run recipe");
            c = new KhemistryISRU { chargePercent = 50, chargingRequired = false, chargeWhileRunning = true };
            c.Tick(1, true); Check(c.Ready, "Concurrent flag does not block noncharging converter");
            Console.WriteLine("Passed " + assertions + " charging assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
