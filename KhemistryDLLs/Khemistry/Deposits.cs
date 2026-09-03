using System;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// Shared data for both deposit types, <see cref="KhemistryUDeposit"/> and <see cref="KhemistryGDeposit"/>.
    /// </summary>
    public class KhemistryDeposit
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
                return (float)KShared.LatLonDistanceMeters(Position[0], Position[1], lat, lon, Planet);
            }
            catch (Exception ex)
            {
                KShared.LogError(
                string.Format("Could not calculate deposit distance; treating the deposit as out of range. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryDeposit/DistanceFromDeposit");
                return float.PositiveInfinity;
            }
        }
        public bool IsInsideDeposit(float lat, float lon) => DistanceFromDeposit(lat, lon) <= Radius;

        protected static float RollRadius(KShared kinst, float minRadius, float maxRadius,
            string logContext)
        {
            if (kinst?.rand == null || float.IsNaN(minRadius) || float.IsInfinity(minRadius)
                || float.IsNaN(maxRadius) || float.IsInfinity(maxRadius)
                || minRadius < 0f || maxRadius < minRadius)
            {
                KShared.LogError(
                    "Invalid deposit radius range [" + minRadius + ", " + maxRadius + "]. Using 0.",
                    logContext);
                return 0f;
            }

            if (minRadius.Equals(maxRadius)) return minRadius;
            return minRadius + (float)kinst.rand.NextDouble() * (maxRadius - minRadius);
        }

        protected static Vector2 RollPosition(KShared kinst, string logContext)
        {
            if (kinst?.rand == null)
            {
                KShared.LogError("Could not generate a deposit position because the random generator is unavailable.",
                    logContext);
                return Vector2.zero;
            }

            // Uniform latitude values over-represent the polar regions. Sampling the sine of
            // latitude uniformly instead gives every equal-area patch of the body the same
            // chance of receiving a deposit.
            double sinLatitude = (kinst.rand.NextDouble() * 2.0) - 1.0;
            float latitude = (float)(Math.Asin(sinLatitude) * 180.0 / Math.PI);
            float longitude = (float)(kinst.rand.NextDouble() * 360.0) - 180f;
            return new Vector2(latitude, longitude);
        }
    }

    /// <summary>
    /// A deposit where resources can be extracted from.
    /// This is the underground version of a deposit, see <see cref="KhemistryGDeposit"/> for its aboveground counterpart.
    /// </summary>
    public class KhemistryUDeposit : KhemistryDeposit
    {
        public float DepthStart { get; set; }

        internal KhemistryUDeposit() { }

        public bool IsDepthInsideDeposit(float depth2)
            => depth2 >= DepthStart && depth2 <= DepthStart + Depth;

        public KhemistryUDeposit(KShared kinst, string planet, string requiredBiome, float depthStart, float depth, string resource, float minRadius, float maxRadius, float latOverride = -12345, float lonOverride = -12345)
        {
            try
            {
                Planet = planet;
                DepthStart = depthStart;
                Depth = depth;
                Resource = resource;

                Radius = RollRadius(kinst, minRadius, maxRadius,
                    "KhemistryUDeposit/constructor");

                // Generate position
                if ((int)latOverride == -12345 || (int)lonOverride == -12345)  // If either of them are not set, calculate as normal
                {
                    Position = RollPosition(kinst, "KhemistryUDeposit/constructor");
                    if (requiredBiome != null)  // If it is null, any biome is supported
                    {
                        // Just keep randomizing the deposit until it hits the right biome, up to a
                        // sane attempt cap — an unmatched/misspelled biome name would otherwise spin
                        // forever since GetBiomeNameFromLatLon always returns *some* biome name.
                        const int maxBiomeAttempts = 10000;
                        int attempts = 0;
                        while (KShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                        {
                            attempts++;
                            if (attempts >= maxBiomeAttempts)
                            {
                                KShared.LogError(
                                    "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                    "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                    "Available biomes for the planet: " + KShared.ListToString(KShared.GetBiomeNames(planet)),
                                    "KhemistryUDeposit/constructor");
                                break;
                            }
                            Position = RollPosition(kinst, "KhemistryUDeposit/constructor");
                        }
                    }
                }
                else  // If both are set, ignore requiredBiome and override the position
                    Position = new Vector2(latOverride, lonOverride);
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryUDeposit/constructor");
            }
        }
    }

    /// <summary>
    /// A deposit where resources can be extracted from. Can have a pair underground deposit.
    /// This is the aboveground version of a deposit, see <see cref="KhemistryUDeposit"/> for its underground counterpart.
    /// </summary>
    public class KhemistryGDeposit : KhemistryDeposit
    {
        public KhemistryUDeposit PairGDeposit { get; set; }

        internal KhemistryGDeposit() { }

        /// <summary>
        /// Helper function to see if a depth is inside the deposit.
        /// Includes both configured boundaries; negative depths are not surface deposits.
        /// </summary>
        /// <param name="depth2">Depth of the point in meters.</param>
        /// <returns>Whether the depth is inside the deposit.</returns>
        public bool IsDepthInsideDeposit(float depth2) => depth2 >= 0f && depth2 <= Depth;

        public KhemistryGDeposit(KShared kinst, string planet, string requiredBiome, float depth,
            string resource, float minRadius, float maxRadius, string resource2,
            float undergroundDepthStart, float undergroundDepth)
        {
            try
            {
                // Set values to make sure everything works
                Planet = planet;
                Depth = depth;
                Resource = resource;

                Radius = RollRadius(kinst, minRadius, maxRadius,
                    "KhemistryGDeposit/constructor");

                // Generate position
                Position = RollPosition(kinst, "KhemistryGDeposit/constructor");
                if (requiredBiome != null)
                {
                    // Just keep randomizing the deposit until it hits the right biome, up to a sane
                    // attempt cap — an unmatched/misspelled biome name would otherwise spin forever
                    // since GetBiomeNameFromLatLon always returns *some* biome name.
                    const int maxBiomeAttempts = 10000;
                    int attempts = 0;
                    while (KShared.GetBiomeNameFromLatLon(planet, Position) != requiredBiome)
                    {
                        attempts++;
                        if (attempts >= maxBiomeAttempts)
                        {
                            KShared.LogError(
                                "Could not find a position on \"" + planet + "\" matching biome \"" + requiredBiome +
                                "\" after " + maxBiomeAttempts + " attempts (bad body/biome name?), using last random position instead. " +
                                "Available biomes for the planet: " + KShared.ListToString(KShared.GetBiomeNames(planet)),
                                "KhemistryGDeposit/constructor");
                            break;
                        }
                        Position = RollPosition(kinst, "KhemistryGDeposit/constructor");
                    }
                }

                // Create the underground pair of the surface deposit, giving it the counterpart resource and overriding the position to the surface deposit's position
                // The biome is not passed here because the override will ignore it anyway
                // If resource2 is null, the deposit is considered "surfaceOnly" and the underground deposit won't be created
                if (resource2 != null)
                {
                    PairGDeposit = new KhemistryUDeposit(kinst, planet, null,
                        undergroundDepthStart, undergroundDepth, resource2, minRadius, maxRadius,
                        latOverride: Position[0], lonOverride: Position[1]);
                    // A paired deposit describes the same horizontal body of ore at a
                    // different depth. It must not independently reroll its footprint.
                    PairGDeposit.Radius = Radius;
                }
            }
            catch (Exception ex)
            {
                KShared.Log(
                string.Format("An error occured. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryGDeposit/constructor");
            }
        }
    }

}
