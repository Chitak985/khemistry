using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    public partial class KShared
    {
        /// <summary>
        /// Returns a biome name using a latitude-longitude position on a CelestialBody.
        /// This function will cause a NullReferenceException if the planet does not exist.
        /// </summary>
        /// <param name="planet">The planet used to find the biome.</param>
        /// <param name="pos">The latitude-longitude Vector2 position to find the biome at.</param>
        /// <returns>The name of the biome.</returns>
        public static string GetBiomeNameFromLatLon(string planet, Vector2 pos) => FlightGlobals.GetBodyByName(planet).BiomeMap.GetAtt(pos[0] * Mathf.Deg2Rad, pos[1] * Mathf.Deg2Rad).name;

        /// <summary>
        /// Gets a list of biome names for a planet.
        /// Will return an empty list if the planet does not exist or has no biomes.
        /// </summary>
        /// <param name="planet">The internal name of the planet to fetch biomes for.</param>
        /// <returns>List of biomes for the planet.</returns>
        public static List<string> GetBiomeNames(string planet)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(planet);
            if (body == null || body.BiomeMap == null)
                return new List<string>();

            return body.BiomeMap.Attributes
                .Where(b => b != null)
                .Select(b => b.name)
                .ToList();
        }

        /// <summary>
        /// Evaluates an OUTPUT_MATERIAL outVolume expression: plain numbers, +, -, *, /,
        /// parentheses, the constant PI, the function Pow(a,b), and [name] tokens referring to
        /// either "size" or a defined material parameter (substituted with their numeric value
        /// before evaluation). Logs a specific error and returns false on any failure — a
        /// reserved "size" parameter name, an unknown [name], a non-numeric substituted value,
        /// or a malformed expression.
        /// </summary>
        public static bool TryEvaluateOutVolumeExpression(string rawExpr, string sizeValue,
            Dictionary<string, string> parameters, string logContext, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(rawExpr)) return false;

            if (parameters != null && parameters.ContainsKey("size"))
            {
                LogError("outVolume expression \"" + rawExpr
                    + "\": this material defines a parameter named \"size\", which is reserved for the material's size — rename it.",
                    logContext);
                return false;
            }

            string substituted = rawExpr;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(rawExpr, @"\[([A-Za-z_][A-Za-z0-9_]*)\]"))
            {
                string name = m.Groups[1].Value;
                if (!substituted.Contains("[" + name + "]")) continue;  // already substituted

                string raw = name == "size" ? sizeValue
                    : (parameters != null && parameters.TryGetValue(name, out string pv) ? pv : null);

                if (raw == null)
                {
                    LogError("outVolume expression \"" + rawExpr + "\": \"" + name
                        + "\" is not \"size\" and no parameter with that name is defined.", logContext);
                    return false;
                }

                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
                {
                    LogError("outVolume expression \"" + rawExpr + "\": \"" + name + "\" = \"" + raw
                        + "\" is not a number.", logContext);
                    return false;
                }

                substituted = substituted.Replace("[" + name + "]", numeric.ToString(CultureInfo.InvariantCulture));
            }

            if (!KMathExpr.TryEvaluate(substituted, out result, out string err))
            {
                LogError("outVolume expression \"" + rawExpr + "\" failed to evaluate: " + err, logContext);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Evaluates a single PARAM_REQUIREMENTS comparison expression against a material
        /// parameter's string value.
        /// <list type="bullet">"Mx" — paramValue &gt; x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"Lx" — paramValue &lt; x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"EMx" — paramValue &gt;= x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"ELx" — paramValue &lt;= x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">anything else — exact string match against paramValue</list>
        /// </summary>
        public static bool EvaluateParamComparison(string paramValue, string comparison)
        {
            if (comparison == null) return false;

            bool TryNumeric(string raw, out double parsed) =>
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

            if (comparison.StartsWith("EM", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(2), out double cv)) return false;
                return pv >= cv;
            }
            if (comparison.StartsWith("EL", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(2), out double cv)) return false;
                return pv <= cv;
            }
            if (comparison.StartsWith("M", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(1), out double cv)) return false;
                return pv > cv;
            }
            if (comparison.StartsWith("L", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(1), out double cv)) return false;
                return pv < cv;
            }

            // Exact value match — works for anything, numeric or not.
            return paramValue == comparison;
        }

        public static double DoubleFarenheitToCelsius(double f) => (f - 32.0) * (5.0 / 9.0);
        public static float FloatFarenheitToCelsius(float f) => (f - 32f) * (5f / 9f);

        public static double LatLonDistanceMeters(
            double lat1Deg,
            double lon1Deg,
            double lat2Deg,
            double lon2Deg,
            string body)
        {
            double lat1 = DegreesToRadians(lat1Deg);
            double lon1 = DegreesToRadians(lon1Deg);
            double lat2 = DegreesToRadians(lat2Deg);
            double lon2 = DegreesToRadians(lon2Deg);

            double dLat = lat2 - lat1;
            double dLon = lon2 - lon1;

            double a =
                Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Pow(Math.Sin(dLon / 2), 2);

            double c = 2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

            return FlightGlobals.GetBodyByName(body).Radius * c;
        }

        public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        public List<string> SurfaceDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryGDeposit deposit in surfaceDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
        }
        public List<string> UndergroundDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
        }

        public static void ParseShowRule(string raw, out bool showPAW, out bool showEVA,
            string fieldName, string moduleName = null)
        {
            string val = raw.Trim().Trim('"').ToUpper();
            switch (val)
            {
                case "PAW":
                    showPAW = true; showEVA = false; break;
                case "EVA":
                    showPAW = false; showEVA = true; break;
                case "EVA+PAW":
                case "PAW+EVA":
                    showPAW = true; showEVA = true; break;
                default:
                    if (moduleName == null)
                        KShared.LogError("Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    else
                        KShared.LogError("Converter \"" + moduleName + "\": Unknown " + fieldName + " value \"" + raw + "\" — defaulting to PAW.", "KShared/ParseShowRule");
                    showPAW = true; showEVA = false; break;
            }
        }

        public static Dictionary<string, string> NodeToDictionary(ConfigNode node)
        {
            var dict = new Dictionary<string, string>();
            foreach (ConfigNode.Value value in node.values)
                dict[value.name] = value.value;
            return dict;
        }

        /// <summary>
        /// Converts a list into a readable string.
        /// Example: ["a", "b", "c"] becomes "a, b, c".
        /// </summary>
        /// <param name="list">The List<string> to convert into a string.</param>
        /// <returns>The list as a readable string.</returns>
        public static string ListToString(List<string> list) => string.Join(", ", list);

        /// <summary>
        /// Converts a dictionary into a readable string.
        /// Example: {a: 1, b: 2, c: 3} becomes "a: 1, b: 2, c: 3".
        /// </summary>
        /// <param name="dict">The Dictionary<string, string> to convert into a string.</param>
        /// <returns>The dictionary as a readable string.</returns>
        public static string DictToString(Dictionary<string, string> dict)
        {
            List<string> tmp = new List<string>();
            foreach (string key in dict.Keys)
                tmp.Add(key + " = " + dict[key]);
            return ListToString(tmp);
        }

        /// <summary>Maps a vessel's current KSP Vessel.Situations into a SituationCondition value.</summary>
        public static SituationCondition GetVesselSituation(Vessel v)
        {
            Vessel.Situations sit = v.situation;
            CelestialBody body = v.mainBody;
            double alt = v.altitude;

            switch (sit)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.PRELAUNCH:
                    return SituationCondition.Landed;
                case Vessel.Situations.SPLASHED:
                    return SituationCondition.Splashed;
                case Vessel.Situations.FLYING:
                    return (body != null && alt >= body.scienceValues.flyingAltitudeThreshold)
                        ? SituationCondition.FlyingHigh : SituationCondition.FlyingLow;
                case Vessel.Situations.SUB_ORBITAL:
                    return SituationCondition.SubOrbital;
                case Vessel.Situations.ORBITING:
                case Vessel.Situations.ESCAPING:
                    return (body != null && alt >= body.scienceValues.spaceAltitudeThreshold)
                        ? SituationCondition.SpaceHigh : SituationCondition.SpaceLow;
                default:
                    return SituationCondition.Any;
            }
        }

        /// <summary>
        /// Destroys the given part and applies falling-off thermal damage to every other
        /// loaded part within radiusMeters of it: centerTempCelsius (converted to Kelvin) is
        /// applied at distance 0, linearly fading to no effect at radiusMeters. Any part whose
        /// resulting temperature reaches its max temperature (or skin max temperature) explodes
        /// as a consequence, same as it would from any other overheat in KSP.
        /// </summary>
        public static void TriggerExplosionWithHeat(Part contextPart, float radiusMeters, float centerTempCelsius)
        {
            if (contextPart == null) return;

            if (radiusMeters <= 0f)
            {
                contextPart.explode();
                return;
            }

            double centerTempKelvin = centerTempCelsius + 273.15;
            Vector3 center = contextPart.transform.position;

            var nearbyParts = new List<Part>();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || !v.loaded) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null || p == contextPart) continue;
                    if (Vector3.Distance(p.transform.position, center) <= radiusMeters)
                        nearbyParts.Add(p);
                }
            }

            contextPart.explode();

            foreach (Part p in nearbyParts)
            {
                if (p == null) continue;

                float dist = Vector3.Distance(p.transform.position, center);
                double falloff = Math.Max(0.0, 1.0 - (dist / radiusMeters));
                if (falloff <= 0.0) continue;

                double appliedTempKelvin = p.temperature + (centerTempKelvin - p.temperature) * falloff;
                if (appliedTempKelvin > p.temperature) p.temperature = appliedTempKelvin;
                if (appliedTempKelvin > p.skinTemperature) p.skinTemperature = appliedTempKelvin;

                if (p.temperature >= p.maxTemp || p.skinTemperature >= p.skinMaxTemp)
                    p.explode();
            }
        }
    }
}
