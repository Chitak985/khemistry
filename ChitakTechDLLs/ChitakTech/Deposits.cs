using System;
using UnityEngine;

namespace ChitakTech
{
    /// <summary>
    /// Shared data for both deposit types, <see cref="ChitakTechUDeposit"/> and <see cref="ChitakTechGDeposit"/>.
    /// </summary>
    public class ChitakTechDeposit
    {
        // Shared variables
        public Vector2 Position { get; set; }  // In latitude, longitude format
        public float Depth { get; set; }  // In meters
        public string Resource { get; set; }  // Internal name of a resource
        public string Planet { get; set; }  // Planet the resource is on
        public float Radius { get; set; }  // Radius of the deposit in meters

        // Deposit distance logic
        public float DistanceFromDeposit(float lat, float lon)
        {
            try
            {
                return (float)CTShared.LatLonDistanceMeters(Position[0], Position[1], lat, lon, Planet);
            }
            catch (Exception ex)
            {
                CTShared.LogError(
                string.Format("Could not calculate deposit distance; treating the deposit as out of range. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "ChitakTechDeposit/DistanceFromDeposit");
                return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// Check if a latitude and longitude are inside the deposit.
        /// </summary>
        public bool IsInsideDeposit(float lat, float lon)
            => DistanceFromDeposit(lat, lon) <= Radius;

        /// <summary>
        /// Check if a latitude and longitude are inside the deposit.
        /// This overload also checks the planet.
        /// </summary>
        public bool IsInsideDeposit(float lat, float lon, string body)
            => body == Planet && IsInsideDeposit(lat, lon);

        protected static float RollRadius(float minRadius, float maxRadius,
            string logContext)
        {
            if (CTShared.rand == null || float.IsNaN(minRadius) || float.IsInfinity(minRadius)
                || float.IsNaN(maxRadius) || float.IsInfinity(maxRadius)
                || minRadius < 0f || maxRadius < minRadius)
            {
                CTShared.LogError(
                    "Invalid deposit radius range [" + minRadius + ", " + maxRadius + "]. Using 0.",
                    logContext);
                return 0f;
            }

            if (minRadius.Equals(maxRadius)) return minRadius;
            return minRadius + (float)CTShared.rand.NextDouble() * (maxRadius - minRadius);
        }

        protected static Vector2 RollPosition(string logContext)
        {
            if (CTShared.rand == null)
            {
                CTShared.LogError("Could not generate a deposit position because the random generator is unavailable.",
                    logContext);
                return Vector2.zero;
            }

            // Uniform latitude values over-represent the polar regions. Sampling the sine of
            // latitude uniformly instead gives every equal-area patch of the body the same
            // chance of receiving a deposit.
            double sinLatitude = (CTShared.rand.NextDouble() * 2.0) - 1.0;
            float latitude = (float)(Math.Asin(sinLatitude) * 180.0 / Math.PI);
            float longitude = (float)(CTShared.rand.NextDouble() * 360.0) - 180f;
            return new Vector2(latitude, longitude);
        }
    }

    /// <summary>
    /// A deposit where resources can be extracted from.
    /// This is the underground version of a deposit, see <see cref="ChitakTechGDeposit"/> for its aboveground counterpart.
    /// </summary>
    public class ChitakTechUDeposit : ChitakTechDeposit
    {
        public float DepthStart { get; set; }

        internal ChitakTechUDeposit() { }

        public bool IsDepthInsideDeposit(float depth2)
            => depth2 >= DepthStart && depth2 <= DepthStart + Depth;

        /// <summary>
        /// Check if a latitude and longitude are inside the deposit.
        /// This overload also checks the planet and depth.
        /// </summary>
        public bool IsInsideDeposit(float lat, float lon, string body, float depth)
            => IsInsideDeposit(lat, lon, body) && IsDepthInsideDeposit(depth);

        public ChitakTechUDeposit(string planet, string requiredBiome, float depthStart, float depth, string resource, float minRadius, float maxRadius, float latOverride = -12345, float lonOverride = -12345)
        {
            try
            {
                Planet = planet;
                DepthStart = depthStart;
                Depth = depth;
                Resource = resource;

                Radius = RollRadius(minRadius, maxRadius, "ChitakTechUDeposit/constructor");

                // Generate position
                if ((int)latOverride == -12345 || (int)lonOverride == -12345)  // If either of them are not set, calculate as normal
                {
                    Position = RollPosition("ChitakTechUDeposit/constructor");
                    if (requiredBiome != null)  // If it is null, any biome is supported
                    {
                        // Just keep randomizing the deposit until it hits the right biome, up to a
                        // sane attempt cap — an unmatched/misspelled biome name would otherwise spin
                        // forever since GetBiomeNameFromLatLon always returns *some* biome name.
                        const int maxBiomeAttempts = 10000;
                        int attempts = 0;
                        while (CTShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                        {
                            attempts++;
                            if (attempts >= maxBiomeAttempts)
                            {
                                CTShared.LogError(
                                    "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                    "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                    "Available biomes for the planet: " + CTShared.ListToString(CTShared.GetBiomeNames(planet)),
                                    "ChitakTechUDeposit/constructor");
                                break;
                            }
                            Position = RollPosition("ChitakTechUDeposit/constructor");
                        }
                    }
                }
                else  // If both are set, ignore requiredBiome and override the position
                    Position = new Vector2(latOverride, lonOverride);
            }
            catch (Exception ex)
            {
                CTShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "ChitakTechUDeposit/constructor");
            }
        }
    }

    /// <summary>
    /// A deposit where resources can be extracted from. Can have a pair underground deposit.
    /// This is the aboveground version of a deposit, see <see cref="ChitakTechUDeposit"/> for its underground counterpart.
    /// </summary>
    public class ChitakTechGDeposit : ChitakTechDeposit
    {
        public ChitakTechUDeposit PairGDeposit { get; set; }
        public bool Render { get; set; }
        public string Model { get; set; }

        internal ChitakTechGDeposit() { }

        /// <summary>
        /// Helper function to see if a depth is inside the deposit.
        /// Includes both configured boundaries; negative depths are not surface deposits.
        /// </summary>
        /// <param name="depth2">Depth of the point in meters.</param>
        /// <returns>Whether the depth is inside the deposit.</returns>
        public bool IsDepthInsideDeposit(float depth2) => depth2 >= 0f && depth2 <= Depth;

        /// <summary>
        /// Check if a latitude and longitude are inside the deposit.
        /// This overload also checks the planet and depth.
        /// </summary>
        public bool IsInsideDeposit(float lat, float lon, string body, float depth)
            => IsInsideDeposit(lat, lon, body) && IsDepthInsideDeposit(depth);

        public ChitakTechGDeposit(string planet, string requiredBiome, float depth,
            string resource, float minRadius, float maxRadius, string resource2,
            float undergroundDepthStart, float undergroundDepth, bool render = false,
            string model = null)
        {
            try
            {
                // Set values to make sure everything works
                Planet = planet;
                Depth = depth;
                Resource = resource;
                Render = render;
                Model = model;

                Radius = RollRadius(minRadius, maxRadius,
                    "ChitakTechGDeposit/constructor");

                // Generate position
                Position = RollPosition("ChitakTechGDeposit/constructor");
                if (requiredBiome != null)
                {
                    // Just keep randomizing the deposit until it hits the right biome, up to a sane
                    // attempt cap — an unmatched/misspelled biome name would otherwise spin forever
                    // since GetBiomeNameFromLatLon always returns *some* biome name.
                    const int maxBiomeAttempts = 10000;
                    int attempts = 0;
                    while (CTShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                    {
                        attempts++;
                        if (attempts >= maxBiomeAttempts)
                        {
                            CTShared.LogError(
                                "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                "Available biomes for the planet: " + CTShared.ListToString(CTShared.GetBiomeNames(planet)),
                                "ChitakTechGDeposit/constructor");
                            break;
                        }
                        Position = RollPosition("ChitakTechGDeposit/constructor");
                    }
                }

                // Create the underground pair of the surface deposit, giving it the counterpart resource and overriding the position to the surface deposit's position
                // The biome is not passed here because the override will ignore it anyway
                // If resource2 is null, the deposit is considered "surfaceOnly" and the underground deposit won't be created
                if (resource2 != null)
                {
                    PairGDeposit = new ChitakTechUDeposit(planet, null,
                        undergroundDepthStart, undergroundDepth, resource2, minRadius, maxRadius,
                        latOverride: Position[0], lonOverride: Position[1])
                    {
                        // A paired deposit describes the same horizontal body of ore at a
                        // different depth. It must not independently reroll its footprint.
                        Radius = Radius
                    };
                }
            }
            catch (Exception ex)
            {
                CTShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "ChitakTechGDeposit/constructor");
            }
        }
    }

}
