using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// Persists procedural deposit positions per save. KShared lives across save changes, so
    /// keeping this data only in that singleton caused one save to inherit another save's
    /// deposits and caused locations to change after restarting the game.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KhemistryDepositsScenario : ScenarioModule
    {
        private readonly List<KhemistryUDeposit> _loadedUnderground =
            new List<KhemistryUDeposit>();
        private readonly List<KhemistryGDeposit> _loadedSurface =
            new List<KhemistryGDeposit>();
        private bool _loadedDepositsPending;
        private bool _newSaveResetPending;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _loadedUnderground.Clear();
            _loadedSurface.Clear();
            _loadedDepositsPending = false;
            _newSaveResetPending = false;

            bool initialized = false;
            if (node != null)
                bool.TryParse(node.GetValue("initialized"), out initialized);

            if (!initialized)
            {
                // Do not inherit a previously loaded save's singleton lists. Roll a fresh set
                // from configuration for this save, then persist it on first save.
                _newSaveResetPending = true;
                TryApplyLoadedDeposits();
                return;
            }

            int invalidNodes = 0;
            List<KhemistryUDeposit> undergroundBySavedIndex =
                new List<KhemistryUDeposit>();
            foreach (ConfigNode depositNode in node.GetNodes("UNDERGROUND_DEPOSIT"))
            {
                if (TryLoadUnderground(depositNode, out KhemistryUDeposit deposit))
                {
                    undergroundBySavedIndex.Add(deposit);
                    _loadedUnderground.Add(deposit);
                }
                else
                {
                    undergroundBySavedIndex.Add(null);
                    invalidNodes++;
                }
            }

            foreach (ConfigNode depositNode in node.GetNodes("SURFACE_DEPOSIT"))
            {
                if (!TryLoadSurface(depositNode, out KhemistryGDeposit deposit,
                        out int pairIndex))
                {
                    invalidNodes++;
                    continue;
                }

                if (pairIndex >= 0)
                {
                    if (pairIndex >= undergroundBySavedIndex.Count
                        || undergroundBySavedIndex[pairIndex] == null
                        || !HasMatchingFootprint(deposit,
                            undergroundBySavedIndex[pairIndex]))
                    {
                        invalidNodes++;
                        // The link is corrupt, but the surface deposit itself is still valid and
                        // useful. Preserve it without a pair instead of discarding save data.
                        pairIndex = -1;
                    }
                    if (pairIndex >= 0)
                        deposit.PairGDeposit = undergroundBySavedIndex[pairIndex];
                }
                _loadedSurface.Add(deposit);
            }

            if (invalidNodes > 0)
                KShared.LogError("Ignored " + invalidNodes
                    + " invalid saved deposit nodes while restoring the save.",
                    "KhemistryDepositsScenario/OnLoad");

            _loadedDepositsPending = true;
            TryApplyLoadedDeposits();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            TryApplyLoadedDeposits();
            if (_newSaveResetPending)
            {
                KShared.LogError("Could not save deposits because their per-save generation has not completed yet.",
                    "KhemistryDepositsScenario/OnSave");
                return;
            }

            IList<KhemistryUDeposit> underground;
            IList<KhemistryGDeposit> surface;
            if (_loadedDepositsPending)
            {
                underground = _loadedUnderground;
                surface = _loadedSurface;
            }
            else
            {
                KShared shared = KShared.Instance;
                if (shared == null)
                {
                    KShared.LogError("Could not save deposits because KShared is unavailable.",
                        "KhemistryDepositsScenario/OnSave");
                    return;
                }
                underground = shared.undergroundDeposits;
                surface = shared.surfaceDeposits;
            }

            while (node.HasValue("initialized")) node.RemoveValue("initialized");
            while (node.HasNode("UNDERGROUND_DEPOSIT")) node.RemoveNode("UNDERGROUND_DEPOSIT");
            while (node.HasNode("SURFACE_DEPOSIT")) node.RemoveNode("SURFACE_DEPOSIT");
            node.AddValue("initialized", true);

            Dictionary<KhemistryUDeposit, int> undergroundIndexes =
                new Dictionary<KhemistryUDeposit, int>();
            int savedUndergroundCount = 0;
            foreach (KhemistryUDeposit deposit in underground)
            {
                if (!IsValid(deposit))
                {
                    KShared.LogError("Skipped an invalid underground deposit while saving.",
                        "KhemistryDepositsScenario/OnSave");
                    continue;
                }

                undergroundIndexes[deposit] = savedUndergroundCount++;
                ConfigNode savedNode = node.AddNode("UNDERGROUND_DEPOSIT");
                SaveCommon(savedNode, deposit);
                savedNode.AddValue("depthStart", Format(deposit.DepthStart));
            }

            foreach (KhemistryGDeposit deposit in surface)
            {
                if (!IsValid(deposit))
                {
                    KShared.LogError("Skipped an invalid surface deposit while saving.",
                        "KhemistryDepositsScenario/OnSave");
                    continue;
                }

                ConfigNode savedNode = node.AddNode("SURFACE_DEPOSIT");
                SaveCommon(savedNode, deposit);
                int pairIndex = -1;
                if (deposit.PairGDeposit != null
                    && !undergroundIndexes.TryGetValue(deposit.PairGDeposit, out pairIndex))
                {
                    pairIndex = -1;
                    KShared.LogError("A surface deposit's underground pair was not saved; preserving the surface deposit without the link.",
                        "KhemistryDepositsScenario/OnSave");
                }
                savedNode.AddValue("pairIndex", pairIndex.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void Update()
        {
            if (_loadedDepositsPending || _newSaveResetPending)
                TryApplyLoadedDeposits();
        }

        private void TryApplyLoadedDeposits()
        {
            KShared shared = KShared.Instance;
            if (shared == null) return;

            if (_newSaveResetPending)
            {
                if (!KSharedMainMenu.GenerateConfiguredDeposits(shared)) return;
                _newSaveResetPending = false;
            }

            if (!_loadedDepositsPending) return;
            shared.undergroundDeposits.Clear();
            foreach (KhemistryUDeposit deposit in _loadedUnderground)
                shared.undergroundDeposits.Add(deposit);
            shared.surfaceDeposits.Clear();
            foreach (KhemistryGDeposit deposit in _loadedSurface)
                shared.surfaceDeposits.Add(deposit);
            _loadedDepositsPending = false;
        }

        private static bool TryLoadUnderground(ConfigNode node,
            out KhemistryUDeposit deposit)
        {
            deposit = null;
            if (!TryLoadCommon(node, out string planet, out string resource,
                    out Vector2 position, out float radius, out float depth)
                || !TryReadFloat(node, "depthStart", out float depthStart)
                || depthStart < 0f || !IsFinite(depthStart + depth))
                return false;

            deposit = new KhemistryUDeposit
            {
                Planet = planet,
                Resource = resource,
                Position = position,
                Radius = radius,
                DepthStart = depthStart,
                Depth = depth
            };
            return true;
        }

        private static bool TryLoadSurface(ConfigNode node, out KhemistryGDeposit deposit,
            out int pairIndex)
        {
            deposit = null;
            pairIndex = -1;
            if (!TryLoadCommon(node, out string planet, out string resource,
                    out Vector2 position, out float radius, out float depth))
                return false;

            string pairText = node.GetValue("pairIndex");
            if (!string.IsNullOrEmpty(pairText)
                && (!int.TryParse(pairText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out pairIndex) || pairIndex < -1))
                return false;

            deposit = new KhemistryGDeposit
            {
                Planet = planet,
                Resource = resource,
                Position = position,
                Radius = radius,
                Depth = depth
            };
            return true;
        }

        private static bool TryLoadCommon(ConfigNode node, out string planet,
            out string resource, out Vector2 position, out float radius, out float depth)
        {
            planet = node?.GetValue("planet")?.Trim();
            resource = node?.GetValue("resource")?.Trim();
            position = Vector2.zero;
            radius = 0f;
            depth = 0f;
            if (string.IsNullOrEmpty(planet) || string.IsNullOrEmpty(resource)
                || !IsKnownBody(planet)
                || !TryReadFloat(node, "latitude", out float latitude)
                || !TryReadFloat(node, "longitude", out float longitude)
                || !TryReadFloat(node, "radius", out radius) || radius <= 0f
                || !TryReadFloat(node, "depth", out depth) || depth <= 0f
                || latitude < -90f || latitude > 90f
                || longitude < -180f || longitude > 180f)
                return false;
            position = new Vector2(latitude, longitude);
            return true;
        }

        private static bool TryReadFloat(ConfigNode node, string key, out float value)
        {
            value = 0f;
            return node != null && float.TryParse(node.GetValue(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && IsFinite(value);
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsKnownBody(string bodyName)
        {
            try { return FlightGlobals.GetBodyByName(bodyName) != null; }
            catch { return false; }
        }

        private static bool IsValid(KhemistryDeposit deposit)
        {
            if (deposit == null || string.IsNullOrWhiteSpace(deposit.Planet)
                || string.IsNullOrWhiteSpace(deposit.Resource) || !IsKnownBody(deposit.Planet)
                || !IsFinite(deposit.Position.x) || deposit.Position.x < -90f
                || deposit.Position.x > 90f || !IsFinite(deposit.Position.y)
                || deposit.Position.y < -180f || deposit.Position.y > 180f
                || !IsFinite(deposit.Radius) || deposit.Radius <= 0f
                || !IsFinite(deposit.Depth) || deposit.Depth <= 0f)
                return false;
            KhemistryUDeposit underground = deposit as KhemistryUDeposit;
            return underground == null || (IsFinite(underground.DepthStart)
                && underground.DepthStart >= 0f
                && IsFinite(underground.DepthStart + underground.Depth));
        }

        private static bool HasMatchingFootprint(KhemistryGDeposit surface,
            KhemistryUDeposit underground)
        {
            if (surface == null || underground == null
                || !string.Equals(surface.Planet, underground.Planet,
                    StringComparison.Ordinal))
                return false;
            const float tolerance = 1e-5f;
            return Math.Abs(surface.Position.x - underground.Position.x) <= tolerance
                && Math.Abs(surface.Position.y - underground.Position.y) <= tolerance
                && Math.Abs(surface.Radius - underground.Radius)
                    <= Math.Max(tolerance, Math.Abs(surface.Radius) * 1e-6f);
        }

        private static void SaveCommon(ConfigNode node, KhemistryDeposit deposit)
        {
            node.AddValue("planet", deposit.Planet.Trim());
            node.AddValue("resource", deposit.Resource.Trim());
            node.AddValue("latitude", Format(deposit.Position.x));
            node.AddValue("longitude", Format(deposit.Position.y));
            node.AddValue("radius", Format(deposit.Radius));
            node.AddValue("depth", Format(deposit.Depth));
        }

        private static string Format(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
