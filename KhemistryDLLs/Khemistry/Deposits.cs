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
                KShared.Log(
                string.Format("An error occured, returning 0 meters. Message: {0}. Stack trace: {1}. ",
                    ex.Message, ex.StackTrace),
                "KhemistryDeposit/DistanceFromDeposit");
                return 0f;
            }
        }
        public bool IsInsideDeposit(float lat, float lon) => DistanceFromDeposit(lat, lon) <= Radius;
    }

    /// <summary>
    /// A deposit where resources can be extracted from.
    /// This is the underground version of a deposit, see <see cref="KhemistryGDeposit"/> for its aboveground counterpart.
    /// </summary>
    public class KhemistryUDeposit : KhemistryDeposit
    {
        public float DepthStart { get; set; }

        public bool IsDepthInsideDeposit(float depth2) => depth2 > DepthStart && depth2 < DepthStart + Depth;

        public KhemistryUDeposit(KShared kinst, string planet, string requiredBiome, float depthStart, float depth, string resource, float minRadius, float maxRadius, float latOverride = -12345, float lonOverride = -12345)
        {
            try
            {
                Planet = planet;
                DepthStart = depthStart;
                Depth = depth;
                Resource = resource;

                if (minRadius == maxRadius)
                    Radius = minRadius;
                else
                {
                    // Keep rolling a radius until it clears minRadius, up to a sane attempt cap —
                    // a misconfigured minRadius >= maxRadius (or a negative value reaching this
                    // constructor via a mislabeled call) would otherwise spin forever.
                    const int maxRadiusAttempts = 10000;
                    float tmp = -1.0f;
                    int radiusAttempts = 0;
                    while (!(minRadius > tmp))
                    {
                        radiusAttempts++;
                        if (radiusAttempts >= maxRadiusAttempts)
                        {
                            KShared.LogError(
                                "Could not roll a radius above minRadius " + minRadius + " with maxRadius " + maxRadius +
                                " after " + maxRadiusAttempts + " attempts (minRadius >= maxRadius?). Using maxRadius instead.",
                                "KhemistryUDeposit/constructor");
                            tmp = maxRadius;
                            break;
                        }
                        tmp = (float)(kinst.rand.NextDouble() * maxRadius);
                    }
                    Radius = tmp;
                }

                // Generate position
                if ((int)latOverride == -12345 || (int)lonOverride == -12345)  // If either of them are not set, calculate as normal
                {
                    Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
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
                            Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
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

        /// <summary>
        /// Helper function to see if a depth is inside the deposit.
        /// Uses -1 in the comparison to make sure 0 works as well.
        /// </summary>
        /// <param name="depth2">Depth of the point in meters.</param>
        /// <returns>Whether the depth is inside the deposit.</returns>
        public bool IsDepthInsideDeposit(float depth2) => depth2 > -1 && depth2 < Depth;

        public KhemistryGDeposit(KShared kinst, string planet, string requiredBiome, float depth, string resource, float minRadius, float maxRadius, string resource2, float underDepth)
        {
            try
            {
                // Set values to make sure everything works
                Planet = planet;
                Depth = depth;
                Resource = resource;

                // if it works, it works — keep rolling a radius until it clears minRadius, up to a
                // sane attempt cap; a misconfigured minRadius >= maxRadius would otherwise spin forever.
                const int maxRadiusAttempts = 10000;
                float tmp = -1.0f;
                int radiusAttempts = 0;
                while (!(minRadius > tmp))
                {
                    radiusAttempts++;
                    if (radiusAttempts >= maxRadiusAttempts)
                    {
                        KShared.LogError(
                            "Could not roll a radius above minRadius " + minRadius + " with maxRadius " + maxRadius +
                            " after " + maxRadiusAttempts + " attempts (minRadius >= maxRadius?). Using maxRadius instead.",
                            "KhemistryGDeposit/constructor");
                        tmp = maxRadius;
                        break;
                    }
                    tmp = (float)(kinst.rand.NextDouble() * maxRadius);
                }
                Radius = tmp;

                // Generate position
                Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
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
                        Position = new Vector2((float)(kinst.rand.NextDouble() * 180) - 90, (float)(kinst.rand.NextDouble() * 360) - 180);
                    }
                }

                // Create the underground pair of the surface deposit, giving it the counterpart resource and overriding the position to the surface deposit's position
                // The biome is not passed here because the override will ignore it anyway
                // If resource2 is null, the deposit is considered "surfaceOnly" and the underground deposit won't be created
                if (resource2 != null)
                    PairGDeposit = new KhemistryUDeposit(kinst, planet, null, depth, underDepth, resource2, minRadius, maxRadius, latOverride: Position[0], lonOverride: Position[1]);
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
