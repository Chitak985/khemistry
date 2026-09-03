namespace Khemistry
{
    /// <summary>
    /// Runtime data used by <see cref="KhemistryISRU"/>.
    /// This is checked by <see cref="KhemistryISRUBiomeConfig"/> to see if a recipe can run.
    /// </summary>
    public class KhemistryRuntimeData
    {
        // While vessel is null this tries to mimick Kerbin
        public double alt = 0;
        public double g = 0;
        public double temperature = 293.15;
        public double pressure = 104;
        public KShared.SituationCondition sitCon = new KShared.SituationCondition();
        public string planet = "Kerbin";
        public string biome = "Grasslands";

        public KhemistryRuntimeData(Vessel vessel)
        {
            // If vessel is null just don't update
            if (vessel != null)
                Update(vessel);
        }
        public void Update(Vessel vessel)
        {
            // If vessel is null just don't update
            if (vessel != null)
            {
                alt = vessel.altitude;  // meters
                g = vessel.geeForce;  // Gs
                temperature = vessel.externalTemperature;  // Kelvin
                pressure = vessel.staticPressurekPa;  // kPa
                sitCon = KShared.GetVesselSituation(vessel);
                CelestialBody body = vessel.mainBody;
                planet = body?.name ?? "";
                biome = body == null
                    ? ""
                    : (ScienceUtil.GetExperimentBiome(body, vessel.latitude, vessel.longitude) ?? "");
            }
        }
    }
}
