using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ChitakTech
{
    /// <summary>
    /// Persists procedural deposit positions per save. CTShared lives across save changes, so
    /// keeping this data only in that singleton caused one save to inherit another save's
    /// deposits and caused locations to change after restarting the game.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER,
        GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KhemistryDepositsScenario : ScenarioModule
    {
        private readonly List<ChitakTechUDeposit> _loadedUnderground =
            new List<ChitakTechUDeposit>();
        private readonly List<ChitakTechGDeposit> _loadedSurface =
            new List<ChitakTechGDeposit>();
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
            List<ChitakTechUDeposit> undergroundBySavedIndex =
                new List<ChitakTechUDeposit>();
            foreach (ConfigNode depositNode in node.GetNodes("UNDERGROUND_DEPOSIT"))
            {
                if (TryLoadUnderground(depositNode, out ChitakTechUDeposit deposit))
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
                if (!TryLoadSurface(depositNode, out ChitakTechGDeposit deposit,
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
                // Rendering is definition metadata, not procedural save data. Reapply it
                // from the current config so existing saves immediately pick up model changes.
                KhemistryDepositRenderingConfig.ApplyCurrentConfig(deposit);
                _loadedSurface.Add(deposit);
            }

            if (invalidNodes > 0)
                CTShared.LogError("Ignored " + invalidNodes
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
                CTShared.LogError("Could not save deposits because their per-save generation has not completed yet.",
                    "KhemistryDepositsScenario/OnSave");
                return;
            }

            IList<ChitakTechUDeposit> underground;
            IList<ChitakTechGDeposit> surface;
            if (_loadedDepositsPending)
            {
                underground = _loadedUnderground;
                surface = _loadedSurface;
            }
            else
            {
                CTShared shared = CTShared.Instance;
                if (shared == null)
                {
                    CTShared.LogError("Could not save deposits because CTShared is unavailable.",
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

            Dictionary<ChitakTechUDeposit, int> undergroundIndexes =
                new Dictionary<ChitakTechUDeposit, int>();
            int savedUndergroundCount = 0;
            foreach (ChitakTechUDeposit deposit in underground)
            {
                if (!IsValid(deposit))
                {
                    CTShared.LogError("Skipped an invalid underground deposit while saving.",
                        "KhemistryDepositsScenario/OnSave");
                    continue;
                }

                undergroundIndexes[deposit] = savedUndergroundCount++;
                ConfigNode savedNode = node.AddNode("UNDERGROUND_DEPOSIT");
                SaveCommon(savedNode, deposit);
                savedNode.AddValue("depthStart", Format(deposit.DepthStart));
            }

            foreach (ChitakTechGDeposit deposit in surface)
            {
                if (!IsValid(deposit))
                {
                    CTShared.LogError("Skipped an invalid surface deposit while saving.",
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
                    CTShared.LogError("A surface deposit's underground pair was not saved; preserving the surface deposit without the link.",
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
            CTShared shared = CTShared.Instance;
            if (shared == null) return;

            if (_newSaveResetPending)
            {
                if (!CTSharedMainMenu.GenerateConfiguredDeposits(shared)) return;
                _newSaveResetPending = false;
            }

            if (!_loadedDepositsPending) return;
            shared.undergroundDeposits.Clear();
            foreach (ChitakTechUDeposit deposit in _loadedUnderground)
                shared.undergroundDeposits.Add(deposit);
            shared.surfaceDeposits.Clear();
            foreach (ChitakTechGDeposit deposit in _loadedSurface)
                shared.surfaceDeposits.Add(deposit);
            _loadedDepositsPending = false;
        }

        private static bool TryLoadUnderground(ConfigNode node,
            out ChitakTechUDeposit deposit)
        {
            deposit = null;
            if (!TryLoadCommon(node, out string planet, out string resource,
                    out Vector2 position, out float radius, out float depth)
                || !CTShared.TryReadFloat(node, "depthStart", out float depthStart)
                || depthStart < 0f || !CTShared.IsFinite(depthStart + depth))
                return false;

            deposit = new ChitakTechUDeposit
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

        private static bool TryLoadSurface(ConfigNode node, out ChitakTechGDeposit deposit,
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

            deposit = new ChitakTechGDeposit
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
                || !CTShared.IsKnownBody(planet)
                || !CTShared.TryReadFloat(node, "latitude", out float latitude)
                || !CTShared.TryReadFloat(node, "longitude", out float longitude)
                || !CTShared.TryReadFloat(node, "radius", out radius) || radius <= 0f
                || !CTShared.TryReadFloat(node, "depth", out depth) || depth <= 0f
                || latitude < -90f || latitude > 90f
                || longitude < -180f || longitude > 180f)
                return false;
            position = new Vector2(latitude, longitude);
            return true;
        }

        private static bool IsValid(ChitakTechDeposit deposit)
        {
            if (deposit == null || string.IsNullOrWhiteSpace(deposit.Planet)
                || string.IsNullOrWhiteSpace(deposit.Resource) || !CTShared.IsKnownBody(deposit.Planet)
                || !CTShared.IsFinite(deposit.Position.x) || deposit.Position.x < -90f
                || deposit.Position.x > 90f || !CTShared.IsFinite(deposit.Position.y)
                || deposit.Position.y < -180f || deposit.Position.y > 180f
                || !CTShared.IsFinite(deposit.Radius) || deposit.Radius <= 0f
                || !CTShared.IsFinite(deposit.Depth) || deposit.Depth <= 0f)
                return false;
            return !(deposit is ChitakTechUDeposit underground) || (CTShared.IsFinite(underground.DepthStart)
                && underground.DepthStart >= 0f
                && CTShared.IsFinite(underground.DepthStart + underground.Depth));
        }

        private static bool HasMatchingFootprint(ChitakTechGDeposit surface,
            ChitakTechUDeposit underground)
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

        private static void SaveCommon(ConfigNode node, ChitakTechDeposit deposit)
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
