using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Khemistry
{
    /// <summary>Per-save record of Parallax scatter instances harvested by Khemistry.</summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public sealed class KhemistryParallaxScenario : ScenarioModule
    {
        private const string HarvestedNodeName = "HARVESTED_PARALLAX_SCATTER";
        private readonly HashSet<KhemistryParallaxScatterIdentity> _harvested =
            new HashSet<KhemistryParallaxScatterIdentity>();
        private float _nextSuppressionPass;

        public static KhemistryParallaxScenario Instance { get; private set; }
        internal bool IsReady { get; private set; }

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            IsReady = false;
            _harvested.Clear();
            KhemistryParallaxIntegration.ClearRuntimeCache();
            int invalid = 0;
            if (node != null)
            {
                foreach (ConfigNode harvestedNode in node.GetNodes(HarvestedNodeName))
                {
                    if (!TryReadIdentity(harvestedNode,
                            out KhemistryParallaxScatterIdentity identity)
                        || !_harvested.Add(identity))
                        invalid++;
                }
            }
            if (invalid > 0)
                KShared.LogError("Ignored " + invalid
                    + " invalid or duplicate harvested Parallax scatter record(s).",
                    "KhemistryParallaxScenario/OnLoad");
            IsReady = true;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;
            while (node.HasNode(HarvestedNodeName)) node.RemoveNode(HarvestedNodeName);
            foreach (KhemistryParallaxScatterIdentity identity in _harvested)
            {
                ConfigNode harvestedNode = new ConfigNode(HarvestedNodeName);
                harvestedNode.AddValue("body", identity.bodyName);
                harvestedNode.AddValue("scatter", identity.scatterName);
                harvestedNode.AddValue("quadPath", identity.quadPath);
                harvestedNode.AddValue("index",
                    identity.positionIndex.ToString(CultureInfo.InvariantCulture));
                harvestedNode.AddValue("localX", Format(identity.localX));
                harvestedNode.AddValue("localY", Format(identity.localY));
                harvestedNode.AddValue("localZ", Format(identity.localZ));
                node.AddNode(harvestedNode);
            }
        }

        public void Update()
        {
            if (!IsReady || !HighLogic.LoadedSceneIsFlight
                || Time.realtimeSinceStartup < _nextSuppressionPass)
                return;
            _nextSuppressionPass = Time.realtimeSinceStartup + 0.5f;
            KhemistryParallaxIntegration.ReapplyHarvestedScatters();
        }

        public void OnDestroy()
        {
            IsReady = false;
            if (ReferenceEquals(Instance, this)) Instance = null;
            KhemistryParallaxIntegration.ClearRuntimeCache();
        }

        internal bool IsHarvested(KhemistryParallaxScatterIdentity identity)
            => _harvested.Contains(identity);

        internal bool HasHarvestedScatters => _harvested.Count > 0;

        internal bool HasHarvestedScatter(string bodyName, string scatterName)
        {
            foreach (KhemistryParallaxScatterIdentity identity in _harvested)
                if (string.Equals(identity.bodyName, bodyName, StringComparison.Ordinal)
                    && string.Equals(identity.scatterName, scatterName,
                        StringComparison.Ordinal))
                    return true;
            return false;
        }

        internal bool TryMarkHarvested(KhemistryParallaxScatterIdentity identity)
            => IsReady && _harvested.Add(identity);

        internal void UnmarkHarvested(KhemistryParallaxScatterIdentity identity)
            => _harvested.Remove(identity);

        private static bool TryReadIdentity(ConfigNode node,
            out KhemistryParallaxScatterIdentity identity)
        {
            identity = default(KhemistryParallaxScatterIdentity);
            string bodyName = node?.GetValue("body")?.Trim();
            string scatterName = node?.GetValue("scatter")?.Trim();
            string quadPath = node?.GetValue("quadPath")?.Trim();
            if (string.IsNullOrEmpty(bodyName) || string.IsNullOrEmpty(scatterName)
                || !IsValidQuadPath(quadPath)
                || !uint.TryParse(node.GetValue("index"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out uint positionIndex)
                || !TryReadFloat(node, "localX", out float localX)
                || !TryReadFloat(node, "localY", out float localY)
                || !TryReadFloat(node, "localZ", out float localZ))
                return false;
            identity = new KhemistryParallaxScatterIdentity(bodyName, scatterName,
                quadPath, positionIndex, localX, localY, localZ);
            return true;
        }

        private static bool IsValidQuadPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (string component in value.Split('/'))
            {
                string[] values = component.Split(':');
                if (values.Length != 2
                    || !int.TryParse(values[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _)
                    || !int.TryParse(values[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _))
                    return false;
            }
            return true;
        }

        private static bool TryReadFloat(ConfigNode node, string name, out float value)
        {
            return float.TryParse(node?.GetValue(name), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string Format(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
