using System;
using System.Collections.Generic;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// Reads the optional visual model attached to a surface-deposit definition.
    /// The configured value deliberately retains its .mu suffix for clarity while
    /// GameDatabase's model lookup uses the corresponding extensionless URL.
    /// </summary>
    internal static class KhemistryDepositRenderingConfig
    {
        internal static bool TryRead(ConfigNode node, string resource, string type,
            bool logErrors, out bool render, out string model)
        {
            render = false;
            model = null;

            if (node != null && node.HasValue("render")
                && !bool.TryParse(node.GetValue("render"), out render))
            {
                if (logErrors)
                    KShared.LogError("Deposit \"" + resource
                        + "\" has a malformed render setting and was not loaded.",
                        "KhemistryDepositRenderingConfig/TryRead");
                return false;
            }

            if (!render) return true;

            if (string.Equals(type, "underground", StringComparison.Ordinal))
            {
                if (logErrors)
                    KShared.LogWarning("Underground deposit \"" + resource
                        + "\" cannot be rendered on the surface; rendering was disabled.",
                        "KhemistryDepositRenderingConfig/TryRead");
                render = false;
                return true;
            }

            string configuredModel = node?.GetValue("model")?.Trim();
            if (!TryNormalizeModelPath(configuredModel, out model))
            {
                if (logErrors)
                    KShared.LogError("Deposit \"" + resource
                        + "\" enables rendering but its model must be a safe GameData-relative path ending in .mu; rendering was disabled.",
                        "KhemistryDepositRenderingConfig/TryRead");
                render = false;
                model = null;
                return true;
            }

            string databasePath = GetDatabaseModelPath(model);
            if (GameDatabase.Instance == null || !GameDatabase.Instance.ExistsModel(databasePath))
            {
                if (logErrors)
                    KShared.LogError("Deposit \"" + resource + "\" model \"" + model
                        + "\" was not found in GameData; rendering was disabled.",
                        "KhemistryDepositRenderingConfig/TryRead");
                render = false;
                model = null;
            }
            return true;
        }

        internal static void ApplyCurrentConfig(KhemistryGDeposit deposit)
        {
            if (deposit == null) return;
            deposit.Render = false;
            deposit.Model = null;
            if (GameDatabase.Instance == null) return;

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(
                "KHEMISTRY_RESOURCE_DEPOSIT");
            foreach (ConfigNode node in nodes)
            {
                string type = node.GetValue("type")?.Trim();
                if (type != "surface" && type != "surfaceOnly") continue;
                if (!string.Equals(node.GetValue("body")?.Trim(), deposit.Planet,
                        StringComparison.Ordinal)
                    || !string.Equals(node.GetValue("resource")?.Trim(), deposit.Resource,
                        StringComparison.Ordinal))
                    continue;

                if (TryRead(node, deposit.Resource, type, false, out bool render,
                        out string model))
                {
                    deposit.Render = render;
                    deposit.Model = model;
                }
                return;
            }
        }

        internal static string GetDatabaseModelPath(string model)
            => model.Substring(0, model.Length - 3);

        private static bool TryNormalizeModelPath(string value, out string normalized)
        {
            normalized = value?.Replace('\\', '/').Trim();
            if (string.IsNullOrEmpty(normalized)
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.IndexOf(':') >= 0
                || !normalized.EndsWith(".mu", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    return false;
            }
            return true;
        }
    }

    public partial class KShared
    {
        private sealed class DepositRenderInstance
        {
            internal KhemistryGDeposit Deposit;
            internal CelestialBody Body;
            internal GameObject GameObject;
            internal double TerrainAltitude;
        }

        private readonly List<DepositRenderInstance> _depositRenderInstances =
            new List<DepositRenderInstance>();
        private readonly Dictionary<string, GameObject> _depositModelPrefabs =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _depositModelErrors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CelestialBody _depositRenderBody;
        private float _nextDepositRenderRefresh;

        public void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.currentMainBody == null)
            {
                if (_depositRenderInstances.Count > 0) DestroyDepositRenderers();
                return;
            }

            CelestialBody body = FlightGlobals.currentMainBody;
            if (Time.realtimeSinceStartup >= _nextDepositRenderRefresh)
            {
                _nextDepositRenderRefresh = Time.realtimeSinceStartup + 0.5f;
                RefreshDepositRenderers(body);
            }

            for (int i = 0; i < _depositRenderInstances.Count; i++)
                PositionDepositRenderer(_depositRenderInstances[i]);
        }

        private void RefreshDepositRenderers(CelestialBody body)
        {
            List<KhemistryGDeposit> desired = new List<KhemistryGDeposit>();
            for (int i = 0; i < surfaceDeposits.Count; i++)
            {
                KhemistryGDeposit deposit = surfaceDeposits[i];
                if (deposit != null && deposit.Render && !string.IsNullOrWhiteSpace(deposit.Model)
                    && string.Equals(deposit.Planet, body.bodyName, StringComparison.Ordinal))
                    desired.Add(deposit);
            }

            bool rebuild = _depositRenderBody != body
                || desired.Count != _depositRenderInstances.Count;
            if (!rebuild)
            {
                for (int i = 0; i < desired.Count; i++)
                {
                    if (!ReferenceEquals(desired[i], _depositRenderInstances[i].Deposit))
                    {
                        rebuild = true;
                        break;
                    }
                }
            }
            if (!rebuild) return;

            DestroyDepositRenderers();
            _depositRenderBody = body;
            for (int i = 0; i < desired.Count; i++)
            {
                try
                {
                    CreateDepositRenderer(desired[i], body, i);
                }
                catch (Exception ex)
                {
                    string errorKey = "create:" + desired[i].Model;
                    if (_depositModelErrors.Add(errorKey))
                        LogError("Could not create deposit model \"" + desired[i].Model
                            + "\". Message: " + ex.Message,
                            "KShared/RefreshDepositRenderers");
                }
            }
        }

        private void CreateDepositRenderer(KhemistryGDeposit deposit, CelestialBody body,
            int index)
        {
            // Keep one record per desired deposit even when the model or terrain cannot be
            // loaded. That prevents one bad definition from forcing every good model on the
            // body to be destroyed and recreated twice per second.
            DepositRenderInstance rendered = new DepositRenderInstance
            {
                Deposit = deposit,
                Body = body
            };
            _depositRenderInstances.Add(rendered);

            string databasePath = KhemistryDepositRenderingConfig.GetDatabaseModelPath(
                deposit.Model);
            if (!_depositModelPrefabs.TryGetValue(databasePath, out GameObject prefab)
                || prefab == null)
            {
                prefab = GameDatabase.Instance?.GetModelPrefab(databasePath);
                if (prefab == null)
                {
                    if (_depositModelErrors.Add(deposit.Model))
                        LogError("Could not load deposit model \"" + deposit.Model + "\".",
                            "KShared/CreateDepositRenderer");
                    return;
                }
                _depositModelPrefabs[databasePath] = prefab;
            }

            GameObject instance = Instantiate(prefab);
            if (instance == null) return;
            instance.name = "KhemistryDeposit_" + deposit.Resource + "_" + index;
            instance.hideFlags = HideFlags.DontSave;

            // Deposit visuals are intentionally cosmetic. A collider or rigidbody accidentally
            // included in an exported model must not create a planet-sized physics obstacle.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody rigidbody in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }

            instance.transform.localScale = Vector3.one * deposit.Radius;
            instance.SetActive(true);

            double terrainAltitude;
            try
            {
                terrainAltitude = body.TerrainAltitude(deposit.Position.x,
                    deposit.Position.y, true);
            }
            catch (Exception ex)
            {
                Destroy(instance);
                LogError("Could not determine terrain height for a \"" + deposit.Resource
                    + "\" deposit. Message: " + ex.Message,
                    "KShared/CreateDepositRenderer");
                return;
            }

            rendered.GameObject = instance;
            rendered.TerrainAltitude = terrainAltitude;
            PositionDepositRenderer(rendered);
        }

        private static void PositionDepositRenderer(DepositRenderInstance rendered)
        {
            if (rendered?.GameObject == null || rendered.Body == null
                || rendered.Deposit == null) return;

            Vector3d worldPosition = rendered.Body.GetWorldSurfacePosition(
                rendered.Deposit.Position.x, rendered.Deposit.Position.y,
                rendered.TerrainAltitude);
            Vector3d outward = worldPosition - rendered.Body.position;
            Vector3 up = new Vector3((float)outward.x, (float)outward.y,
                (float)outward.z).normalized;

            Vector3 north = Vector3.ProjectOnPlane(rendered.Body.transform.up, up).normalized;
            if (north.sqrMagnitude < 1e-6f)
                north = Vector3.ProjectOnPlane(rendered.Body.transform.forward, up).normalized;

            rendered.GameObject.transform.position = new Vector3((float)worldPosition.x,
                (float)worldPosition.y, (float)worldPosition.z);
            rendered.GameObject.transform.rotation = Quaternion.LookRotation(north, up);
        }

        private void DestroyDepositRenderers()
        {
            for (int i = 0; i < _depositRenderInstances.Count; i++)
            {
                GameObject instance = _depositRenderInstances[i].GameObject;
                if (instance != null) Destroy(instance);
            }
            _depositRenderInstances.Clear();
            _depositRenderBody = null;
            _nextDepositRenderRefresh = 0f;
        }
    }
}
