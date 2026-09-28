using System;
using System.Collections.Generic;
using System.Linq;
public partial class ConfigNode
{
    public bool HasNode(string name) => GetNode(name) != null;
    public void RemoveValue(string name) { values.Remove(name); repeatedValues.RemoveAll(v => v.Item1 == name); }
}
public static class HighLogic { public static bool LoadedSceneIsFlight = true; }
public class CelestialBody { public string name = "Kerbin"; public string biome = "Grasslands"; }
public static class ScienceUtil { public static string GetExperimentBiome(CelestialBody body, double a, double b) => body.biome; }
public partial class Vessel
{
    public CelestialBody mainBody = new CelestialBody();
    public double altitude, geeForce = 1, externalTemperature = 293.15, staticPressurekPa = 101.325, latitude, longitude;
}
public class AvailablePart { public string title = "Test storage"; }
public partial class Part
{
    public AvailablePart partInfo = new AvailablePart();
    public string name = "storage";
    public int explosions;
    public void explode() { explosions++; }
}
namespace Khemistry
{
    public partial class KShared
    {
        public static KShared Instance = new KShared();
        public enum SituationCondition { Landed, FlyingLow, SplashedDown }
        public static SituationCondition GetVesselSituation(Vessel vessel) => SituationCondition.Landed;
        public List<string> SurfaceDepositsAtPoint(float a, float b, string planet, int c) => new List<string>();
        public List<string> UndergroundDepositsBelowPoint(float a, float b, string planet) => new List<string>();
        public static string GetStrValueFromCFG(ConfigNode n, string key, string fallback) => n.GetValue(key) ?? fallback;
        public static double GetDoubleTemperatureValueFromCFG(ConfigNode n, string key, double fallback)
            => GetDoubleValueFromCFG(n, key, fallback);
    }
    public class MaintenanceStage { public Dictionary<string, double> multipliers = new Dictionary<string, double>(); }
    public partial class KhemistryISRURecipe
    {
        public struct ResourceInput { }
        public struct ResourceOutput { }
        public static bool TryParseResourceInput(ConfigNode n, string c, out ResourceInput input) { input = default; return true; }
        public static bool TryParseResourceOutput(ConfigNode n, string c, out ResourceOutput output) { output = default; return true; }
    }
    public partial class KhemistryAdvancedStorage
    {
        public float chargeRate = 2, chargeDecayRate = 3;
        public bool ConfigureEnvironment(ConfigNode n) => _environment.Load(n, true);
        public bool RefreshEnvironment() => CheckStorageEnvironment();
        public double ChargeRateForTest => EffectiveChargeRate;
        public double DecayRateForTest => EffectiveChargeDecayRate;
        public double PassiveMultiplierForTest => EffectivePassiveMultiplier;
    }
    // Test the production MaterialStorage environment binding without Unity's PartModule host.
    public partial class KhemistryMaterialStorage
    {
        public Part part;
        public float volume = 10;
        private bool _fatalConfigError = false;
        public List<double> contents = new List<double>();
        private List<ConfigNode> _pendingSavedContents = new List<ConfigNode>();
        private double ComputeCurrentVolume() => contents.Sum();
        public bool Configure(ConfigNode n) { _restoringContents = false; return _environment.Load(n, false); }
    }
}
