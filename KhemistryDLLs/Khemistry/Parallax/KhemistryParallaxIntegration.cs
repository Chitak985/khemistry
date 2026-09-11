using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parallax;
using UnityEngine;

namespace Khemistry
{
    internal struct KhemistryParallaxScatterIdentity : IEquatable<KhemistryParallaxScatterIdentity>
    {
        internal readonly string bodyName;
        internal readonly string scatterName;
        internal readonly string quadPath;
        internal readonly uint positionIndex;
        internal readonly float localX;
        internal readonly float localY;
        internal readonly float localZ;

        internal KhemistryParallaxScatterIdentity(string bodyName, string scatterName,
            string quadPath, uint positionIndex, float localX, float localY, float localZ)
        {
            this.bodyName = bodyName;
            this.scatterName = scatterName;
            this.quadPath = quadPath;
            this.positionIndex = positionIndex;
            this.localX = localX;
            this.localY = localY;
            this.localZ = localZ;
        }

        public bool Equals(KhemistryParallaxScatterIdentity other)
        {
            return string.Equals(bodyName, other.bodyName, StringComparison.Ordinal)
                && string.Equals(scatterName, other.scatterName, StringComparison.Ordinal)
                && string.Equals(quadPath, other.quadPath, StringComparison.Ordinal)
                && positionIndex == other.positionIndex
                && localX.Equals(other.localX)
                && localY.Equals(other.localY)
                && localZ.Equals(other.localZ);
        }

        public override bool Equals(object obj)
            => obj is KhemistryParallaxScatterIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = bodyName?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (scatterName?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (quadPath?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (int)positionIndex;
                hash = (hash * 397) ^ localX.GetHashCode();
                hash = (hash * 397) ^ localY.GetHashCode();
                hash = (hash * 397) ^ localZ.GetHashCode();
                return hash;
            }
        }
    }

    internal sealed class KhemistryParallaxTarget
    {
        internal KhemistryParallaxScatterIdentity identity;
        internal PositionDataQuadID transformData;
        internal ScatterSystemQuadData quadData;
        internal GameObject colliderObject;
        internal double distance;
        internal double radius;
        internal double height;
        internal double volume;
    }

    /// <summary>
    /// Isolates the Parallax Continued API from the converter. Parallax renders scatters from
    /// GPU buffers and creates separate collider objects, so a harvested instance must be
    /// suppressed in both places and suppressed again whenever its PQS quad is regenerated.
    /// </summary>
    internal static class KhemistryParallaxIntegration
    {
        internal const string NoSuitableTreeMessage = "No suitable tree nearby";

        private sealed class TreeDefinition
        {
            internal string bodyName;
            internal string scatterName;
            internal double trunkRadius;
            internal double trunkHeight;
        }

        private sealed class BufferChange
        {
            internal ComputeBuffer buffer;
            internal PositionData[] positions;
            internal int position;
            internal Vector3 originalScale;
        }

        private sealed class SuppressionCacheEntry
        {
            internal WeakReference quad;
            internal float nextVerification;
        }

        // Parallax can rebuild a scatter buffer without replacing its owning PQS quad. A short
        // cache avoids a GPU readback every polling pass, while this periodic verification makes
        // an in-place regeneration self-healing too.
        private const float SuppressionVerificationInterval = 5f;

        private static readonly Dictionary<string, TreeDefinition> TreeDefinitions =
            new Dictionary<string, TreeDefinition>(StringComparer.Ordinal);
        private static readonly HashSet<string> MissingDefinitionWarnings =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<KhemistryParallaxScatterIdentity,
            SuppressionCacheEntry>
            SuppressedQuadCache =
                new Dictionary<KhemistryParallaxScatterIdentity,
                    SuppressionCacheEntry>();
        private static bool _definitionsLoaded;

        internal static void ClearRuntimeCache()
        {
            SuppressedQuadCache.Clear();
        }

        internal static bool TryFindClosest(string bodyName, Vector3 interactionPosition,
            IEnumerable<KhemistryISRURecipe.ParallaxScatterRequirement> requirements,
            double maximumDistance, out KhemistryParallaxTarget target)
        {
            target = null;
            KhemistryParallaxScenario scenario = KhemistryParallaxScenario.Instance;
            if (scenario == null || !scenario.IsReady || string.IsNullOrEmpty(bodyName)
                || !IsFinite(interactionPosition) || double.IsNaN(maximumDistance)
                || double.IsInfinity(maximumDistance) || maximumDistance < 0.0)
                return false;

            EnsureDefinitionsLoaded();
            HashSet<string> accepted = new HashSet<string>(
                (requirements ?? Enumerable.Empty<KhemistryISRURecipe.ParallaxScatterRequirement>())
                    .Select(value => value.scatterName)
                    .Where(value => !string.IsNullOrEmpty(value)),
                StringComparer.Ordinal);
            if (accepted.Count == 0) return false;

            Scatter[] scatters = Parallax.CollisionManager.collideableScatters;
            Dictionary<PositionDataQuadID, GameObject>[] active =
                Parallax.CollisionManager.activeObjects;
            if (scatters == null || active == null) return false;

            int count = Math.Min(scatters.Length, active.Length);
            for (int scatterIndex = 0; scatterIndex < count; scatterIndex++)
            {
                Scatter scatter = scatters[scatterIndex];
                string scatterName = scatter?.scatterName;
                if (string.IsNullOrEmpty(scatterName) || !accepted.Contains(scatterName))
                    continue;
                if (!TryGetTreeDefinition(bodyName, scatterName, out TreeDefinition definition))
                    continue;

                Dictionary<PositionDataQuadID, GameObject> instances = active[scatterIndex];
                if (instances == null) continue;
                try
                {
                    foreach (KeyValuePair<PositionDataQuadID, GameObject> instance in instances)
                    {
                        GameObject colliderObject = instance.Value;
                        if (colliderObject == null || !colliderObject.activeInHierarchy)
                            continue;
                        MeshCollider collider = colliderObject.GetComponent<MeshCollider>();
                        if (collider == null || !collider.enabled) continue;
                        if (!TryBuildTarget(bodyName, scatterName, instance.Key, colliderObject,
                                out KhemistryParallaxTarget candidate))
                            continue;
                        if (scenario.IsHarvested(candidate.identity)) continue;

                        Vector3 nearest;
                        try
                        {
                            nearest = collider.ClosestPoint(interactionPosition);
                        }
                        catch (Exception)
                        {
                            // The collider can disappear while Parallax is recycling a quad.
                            continue;
                        }
                        double distance = Vector3.Distance(interactionPosition, nearest);
                        if (double.IsNaN(distance) || double.IsInfinity(distance)
                            || distance > maximumDistance)
                            continue;

                        CalculateTreeDimensions(definition.trunkRadius,
                            definition.trunkHeight,
                            new Vector3(candidate.transformData.localScale.x,
                                candidate.transformData.localScale.y,
                                candidate.transformData.localScale.z),
                            out double radius, out double height, out double volume);
                        if (!IsFinitePositive(radius) || !IsFinitePositive(height)
                            || !IsFinitePositive(volume) || volume > float.MaxValue)
                            continue;

                        candidate.distance = distance;
                        candidate.radius = radius;
                        candidate.height = height;
                        candidate.volume = volume;
                        if (target == null || candidate.distance < target.distance)
                            target = candidate;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Parallax rebuilt this dictionary during the scan. The converter will try
                    // again on its next update rather than using a partially observed instance.
                    target = null;
                    return false;
                }
            }
            return target != null;
        }

        internal static bool IsTargetUsable(KhemistryParallaxTarget target,
            Vector3 interactionPosition, double maximumDistance)
        {
            if (target?.colliderObject == null || !target.colliderObject.activeInHierarchy
                || !IsFinite(interactionPosition) || double.IsNaN(maximumDistance)
                || double.IsInfinity(maximumDistance) || maximumDistance < 0.0)
                return false;
            KhemistryParallaxScenario scenario = KhemistryParallaxScenario.Instance;
            if (scenario == null || !scenario.IsReady || scenario.IsHarvested(target.identity))
                return false;
            try
            {
                MeshCollider collider = target.colliderObject.GetComponent<MeshCollider>();
                if (collider == null || !collider.enabled) return false;
                double distance = Vector3.Distance(interactionPosition,
                    collider.ClosestPoint(interactionPosition));
                return !double.IsNaN(distance) && !double.IsInfinity(distance)
                    && distance <= maximumDistance;
            }
            catch (Exception)
            {
                // A cached collider may be destroyed between Parallax update phases.
                return false;
            }
        }

        internal static bool TryHarvest(KhemistryParallaxTarget target)
        {
            KhemistryParallaxScenario scenario = KhemistryParallaxScenario.Instance;
            if (target == null || scenario == null || !scenario.IsReady
                || scenario.IsHarvested(target.identity))
                return false;

            if (!TryPrepareSuppression(target, out List<BufferChange> changes,
                    out bool allRelatedBuffersReady) || changes.Count == 0)
                return false;
            if (!scenario.TryMarkHarvested(target.identity)) return false;

            bool colliderWasActive = target.colliderObject != null
                && target.colliderObject.activeSelf;
            List<BufferChange> applied = new List<BufferChange>();
            try
            {
                foreach (BufferChange change in changes)
                {
                    change.positions[change.position].localScale = Vector3.zero;
                    change.buffer.SetData(change.positions, change.position,
                        change.position, 1);
                    applied.Add(change);
                }
                if (target.colliderObject != null) target.colliderObject.SetActive(false);
                if (allRelatedBuffersReady)
                    CacheSuppressedQuad(target);
                return true;
            }
            catch (Exception ex)
            {
                RestoreBufferChanges(applied);
                if (target.colliderObject != null)
                    target.colliderObject.SetActive(colliderWasActive);
                scenario.UnmarkHarvested(target.identity);
                KShared.LogError("Could not suppress harvested Parallax scatter \""
                    + target.identity.scatterName + "\": " + ex.Message,
                    "KhemistryParallaxIntegration/TryHarvest");
                return false;
            }
        }

        internal static void ReapplyHarvestedScatters()
        {
            KhemistryParallaxScenario scenario = KhemistryParallaxScenario.Instance;
            if (scenario == null || !scenario.IsReady
                || !scenario.HasHarvestedScatters)
                return;
            string bodyName = FlightGlobals.currentMainBody?.bodyName;
            if (string.IsNullOrEmpty(bodyName)) return;

            Scatter[] scatters = Parallax.CollisionManager.collideableScatters;
            Dictionary<PositionDataQuadID, GameObject>[] active =
                Parallax.CollisionManager.activeObjects;
            if (scatters == null || active == null) return;

            int count = Math.Min(scatters.Length, active.Length);
            float now = Time.realtimeSinceStartup;
            for (int scatterIndex = 0; scatterIndex < count; scatterIndex++)
            {
                string scatterName = scatters[scatterIndex]?.scatterName;
                Dictionary<PositionDataQuadID, GameObject> instances = active[scatterIndex];
                if (string.IsNullOrEmpty(scatterName) || instances == null
                    || !scenario.HasHarvestedScatter(bodyName, scatterName))
                    continue;
                try
                {
                    foreach (KeyValuePair<PositionDataQuadID, GameObject> instance in instances)
                    {
                        if (!TryBuildTarget(bodyName, scatterName, instance.Key, instance.Value,
                                out KhemistryParallaxTarget target)
                            || !scenario.IsHarvested(target.identity))
                            continue;

                        bool recentlyVerified = SuppressedQuadCache.TryGetValue(
                                target.identity, out SuppressionCacheEntry cached)
                            && ReferenceEquals(cached.quad.Target, target.quadData)
                            && now < cached.nextVerification;
                        if (recentlyVerified && (target.colliderObject == null
                                || !target.colliderObject.activeSelf))
                            continue;

                        if (TryPrepareSuppression(target, out List<BufferChange> changes,
                                out bool allRelatedBuffersReady) && changes.Count > 0)
                        {
                            bool applied = true;
                            try
                            {
                                foreach (BufferChange change in changes)
                                {
                                    change.positions[change.position].localScale = Vector3.zero;
                                    change.buffer.SetData(change.positions,
                                        change.position, change.position, 1);
                                }
                                if (target.colliderObject != null)
                                    target.colliderObject.SetActive(false);
                            }
                            catch (Exception ex)
                            {
                                applied = false;
                                KShared.LogError("Could not reapply harvested Parallax scatter suppression: "
                                    + ex.Message,
                                    "KhemistryParallaxIntegration/ReapplyHarvestedScatters");
                            }
                            if (applied && allRelatedBuffersReady)
                                CacheSuppressedQuad(target);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The regenerated collection will be observed in the next polling pass.
                }
            }
        }

        internal static void CalculateTreeDimensions(double baseRadius, double baseHeight,
            Vector3 localScale, out double radius, out double height, out double volume)
        {
            radius = baseRadius * Math.Sqrt(Math.Abs((double)localScale.x * localScale.z));
            height = baseHeight * Math.Abs((double)localScale.y);
            volume = Math.PI * radius * radius * height;
        }

        private static void CacheSuppressedQuad(KhemistryParallaxTarget target)
        {
            SuppressedQuadCache[target.identity] = new SuppressionCacheEntry
            {
                quad = new WeakReference(target.quadData),
                nextVerification = Time.realtimeSinceStartup
                    + SuppressionVerificationInterval
            };
        }

        private static bool TryBuildTarget(string bodyName, string scatterName,
            PositionDataQuadID transformData, GameObject colliderObject,
            out KhemistryParallaxTarget target)
        {
            target = null;
            if (!IsFinite(new Vector3(transformData.localPos.x,
                    transformData.localPos.y, transformData.localPos.z))
                || !IsFinite(new Vector3(transformData.localScale.x,
                    transformData.localScale.y, transformData.localScale.z))
                || float.IsNaN(transformData.rotation)
                || float.IsInfinity(transformData.rotation)
                || !TryGetQuadData(transformData, out ScatterSystemQuadData quadData)
                || quadData?.quad == null)
                return false;
            string quadPath = BuildQuadPath(quadData.quad);
            if (string.IsNullOrEmpty(quadPath)) return false;

            target = new KhemistryParallaxTarget
            {
                identity = new KhemistryParallaxScatterIdentity(bodyName, scatterName,
                    quadPath, transformData.index, transformData.localPos.x,
                    transformData.localPos.y, transformData.localPos.z),
                transformData = transformData,
                quadData = quadData,
                colliderObject = colliderObject
            };
            return true;
        }

        private static bool TryGetQuadData(PositionDataQuadID transformData,
            out ScatterSystemQuadData quadData)
        {
            quadData = null;
            try
            {
                int quadId = transformData.quadID;
                if (quadId < 0 || quadId >= Parallax.CollisionManager.collisionData.Length)
                    return false;
                ScatterColliderData colliderData =
                    Parallax.CollisionManager.collisionData[quadId];
                quadData = colliderData?.scatterSystemQuad;
                return quadData != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string BuildQuadPath(PQ quad)
        {
            List<string> path = new List<string>();
            int guard = 0;
            for (PQ current = quad; current != null && guard++ < 64; current = current.parent)
                path.Add(current.subdivision.ToString(CultureInfo.InvariantCulture) + ":"
                    + current.quadIndex.ToString(CultureInfo.InvariantCulture));
            path.Reverse();
            return string.Join("/", path.ToArray());
        }

        private static bool TryPrepareSuppression(KhemistryParallaxTarget target,
            out List<BufferChange> changes, out bool allRelatedBuffersReady)
        {
            changes = new List<BufferChange>();
            allRelatedBuffersReady = true;
            if (target?.quadData?.quadScatters == null) return false;

            List<ScatterData> baseData = new List<ScatterData>();
            List<ScatterData> sharedData = new List<ScatterData>();
            foreach (ScatterData scatterData in target.quadData.quadScatters)
            {
                Scatter scatter = scatterData?.scatter;
                if (scatter == null) continue;
                if (string.Equals(scatter.scatterName, target.identity.scatterName,
                        StringComparison.Ordinal))
                    baseData.Add(scatterData);
                else if (scatter is SharedScatter shared && shared.parent != null
                    && string.Equals(shared.parent.scatterName,
                        target.identity.scatterName, StringComparison.Ordinal))
                    sharedData.Add(scatterData);
            }
            if (baseData.Count == 0) return false;

            int baseBufferPosition = -1;
            HashSet<ComputeBuffer> visited = new HashSet<ComputeBuffer>();
            foreach (ScatterData scatterData in baseData.Concat(sharedData))
            {
                ComputeBuffer buffer = scatterData.outputScatterDataBuffer;
                if (buffer == null || !visited.Add(buffer))
                {
                    if (buffer == null) allRelatedBuffersReady = false;
                    continue;
                }

                try
                {
                    if (buffer.count <= 0)
                    {
                        allRelatedBuffersReady = false;
                        continue;
                    }
                    PositionData[] positions = new PositionData[buffer.count];
                    buffer.GetData(positions);
                    int position = FindPosition(positions, target.transformData,
                        baseBufferPosition);
                    if (position < 0)
                    {
                        allRelatedBuffersReady = false;
                        continue;
                    }
                    if (baseBufferPosition < 0 && baseData.Contains(scatterData))
                        baseBufferPosition = position;
                    changes.Add(new BufferChange
                    {
                        buffer = buffer,
                        positions = positions,
                        position = position,
                        originalScale = positions[position].localScale
                    });
                }
                catch (Exception)
                {
                    allRelatedBuffersReady = false;
                }
            }

            return baseBufferPosition >= 0 && changes.Any(change =>
                baseData.Any(data => ReferenceEquals(data.outputScatterDataBuffer,
                    change.buffer)));
        }

        private static int FindPosition(PositionData[] positions,
            PositionDataQuadID transformData, int fallbackPosition)
        {
            for (int index = 0; index < positions.Length; index++)
            {
                PositionData position = positions[index];
                if (position.index == transformData.index
                    && NearlyEqual(position.localPos.x, transformData.localPos.x)
                    && NearlyEqual(position.localPos.y, transformData.localPos.y)
                    && NearlyEqual(position.localPos.z, transformData.localPos.z)
                    && NearlyEqual(position.rotation, transformData.rotation))
                    return index;
            }
            if (fallbackPosition >= 0 && fallbackPosition < positions.Length
                && positions[fallbackPosition].index == transformData.index)
                return fallbackPosition;
            return -1;
        }

        private static void RestoreBufferChanges(IEnumerable<BufferChange> changes)
        {
            foreach (BufferChange change in changes.Reverse())
            {
                try
                {
                    change.positions[change.position].localScale = change.originalScale;
                    change.buffer.SetData(change.positions, change.position,
                        change.position, 1);
                }
                catch (Exception ex)
                {
                    KShared.LogError("Could not roll back Parallax scatter suppression: "
                        + ex.Message,
                        "KhemistryParallaxIntegration/RestoreBufferChanges");
                }
            }
        }

        private static void EnsureDefinitionsLoaded()
        {
            if (_definitionsLoaded || GameDatabase.Instance == null) return;
            _definitionsLoaded = true;
            TreeDefinitions.Clear();
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(
                         "KHEMISTRY_PARALLAX_TREE"))
            {
                string bodyName = node.GetValue("body")?.Trim();
                string scatterName = node.GetValue("scatter")?.Trim();
                string heightText = node.GetValue("trunkHeight")
                    ?? node.GetValue("usableTrunkLength");
                if (string.IsNullOrEmpty(bodyName) || string.IsNullOrEmpty(scatterName)
                    || !TryReadPositiveDouble(node.GetValue("trunkRadius"),
                        out double radius)
                    || !TryReadPositiveDouble(heightText, out double height))
                {
                    KShared.LogError("Ignored invalid KHEMISTRY_PARALLAX_TREE definition.",
                        "KhemistryParallaxIntegration/EnsureDefinitionsLoaded");
                    continue;
                }
                string key = MakeDefinitionKey(bodyName, scatterName);
                if (TreeDefinitions.ContainsKey(key))
                {
                    KShared.LogError("Ignored duplicate KHEMISTRY_PARALLAX_TREE definition for "
                        + bodyName + "/" + scatterName + ".",
                        "KhemistryParallaxIntegration/EnsureDefinitionsLoaded");
                    continue;
                }
                TreeDefinitions.Add(key, new TreeDefinition
                {
                    bodyName = bodyName,
                    scatterName = scatterName,
                    trunkRadius = radius,
                    trunkHeight = height
                });
            }
        }

        private static bool TryGetTreeDefinition(string bodyName, string scatterName,
            out TreeDefinition definition)
        {
            string key = MakeDefinitionKey(bodyName, scatterName);
            if (TreeDefinitions.TryGetValue(key, out definition)) return true;
            if (MissingDefinitionWarnings.Add(key))
                KShared.LogError("No KHEMISTRY_PARALLAX_TREE definition exists for "
                    + bodyName + "/" + scatterName + ".",
                    "KhemistryParallaxIntegration/TryGetTreeDefinition");
            return false;
        }

        private static string MakeDefinitionKey(string bodyName, string scatterName)
            => bodyName + "\0" + scatterName;

        private static bool TryReadPositiveDouble(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out value)
                && IsFinitePositive(value);
        }

        private static bool IsFinitePositive(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;

        private static bool IsFinite(Vector3 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool NearlyEqual(float left, float right)
        {
            float scale = Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= scale * 1e-5f;
        }
    }
}
