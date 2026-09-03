using System.Linq;
using UnityEngine;
using System.Globalization;

namespace Khemistry
{
    /// <summary>
    /// A version of <see cref="KShared"/> that loads during the MainMenu scene.
    /// Mainly loads many top-level configs.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KSharedMainMenu : MonoBehaviour
    {
        private static KSharedMainMenu _instance;
        public static KSharedMainMenu Instance => _instance;

        public KShared kinst;

        public void Awake()
        {
            if (_instance != null)
            {
                KShared.LogError("Another instance of KSharedMainMenu was found, self destructing...", "KSharedMainMenu/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            kinst = KShared.Instance;
            if (kinst == null)
            {
                Debug.LogError("Khemistry (KSharedMainMenu/Awake): KShared.Instance is not available; configuration loading was aborted.");
                return;
            }

            // KShared persists across scenes. Always reload into fresh collections if the main
            // menu is entered again, rather than accumulating duplicate data.
            kinst.surfaceDeposits.Clear();
            kinst.undergroundDeposits.Clear();
            kinst.batchRecipeList.Clear();
            kinst.materialList.Clear();

            // Celestial body list
            kinst.celestialBodies = FlightGlobals.Bodies.Select(b => b.bodyName).ToList();

            GenerateConfiguredDeposits(kinst);

            // KhemistryISRU recipes
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRYISRU_RECIPE"))
            {
                if (!node.HasValue("name"))
                {
                    KShared.LogError("A KHEMISTRYISRU_RECIPE has no name!", "KSharedMainMenu/Awake");
                    continue;
                }
                string recipeName = node.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(recipeName))
                {
                    KShared.LogError("A KHEMISTRYISRU_RECIPE has an empty name!", "KSharedMainMenu/Awake");
                    continue;
                }
                if (kinst.batchRecipeList.Any(existingRecipe => existingRecipe._name == recipeName))
                {
                    KShared.LogError("Duplicate KhemistryISRU recipe name \"" + recipeName
                        + "\"; the later definition was ignored.", "KSharedMainMenu/Awake");
                    continue;
                }
                KhemistryISRURecipe loadedRecipe = new KhemistryISRURecipe(node, recipeName);
                if (loadedRecipe.IsValid)
                    kinst.batchRecipeList.Add(loadedRecipe);
                else
                    KShared.LogError("Invalid KhemistryISRU recipe \"" + recipeName
                        + "\" was ignored.", "KSharedMainMenu/Awake");
            }
            // Material definitions
            int materialCount = 0;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_MATERIAL"))
            {
                KhemistryMaterial tmp = new KhemistryMaterial(node);
                if (tmp.IsValid && !kinst.materialList.Any(material => material.name == tmp.name))
                {
                    kinst.materialList.Add(tmp);
                    materialCount++;
                }
                else if (tmp.IsValid)
                    KShared.LogError("Duplicate Khemistry material name \"" + tmp.name
                        + "\"; the later definition was ignored.", "KSharedMainMenu/Awake");
            }
            KShared.Log("Created " + materialCount.ToString() + " material definitions.", "KSharedMainMenu/Awake");

            // Recipe resource/material references can only be checked after all Khemistry
            // material definitions have loaded. Remove invalid recipes so a misspelled input
            // cannot accidentally turn into a free conversion.
            kinst.batchRecipeList.RemoveAll(recipe =>
                !recipe.ValidateReferences(kinst.materialList, "KSharedMainMenu/Awake"));
            KShared.Log("Created " + kinst.batchRecipeList.Count.ToString()
                + " valid KhemistryISRU recipes.", "KSharedMainMenu/Awake");
        }

        public void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Rebuilds the procedural deposit set from configuration. This is intentionally
        /// reusable by the per-save scenario so two new saves created during one KSP process
        /// receive independent random locations instead of clones of the main-menu roll.
        /// </summary>
        internal static bool GenerateConfiguredDeposits(KShared shared)
        {
            if (shared == null || shared.rand == null || GameDatabase.Instance == null
                || PartResourceLibrary.Instance == null || FlightGlobals.Bodies == null
                || FlightGlobals.Bodies.Count == 0)
                return false;

            shared.surfaceDeposits.Clear();
            shared.undergroundDeposits.Clear();

            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RESOURCE_DEPOSIT"))
            {
                string resource = node.GetValue("resource")?.Trim();
                string type = node.GetValue("type")?.Trim();
                string body = node.GetValue("body")?.Trim();
                if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(type)
                    || string.IsNullOrEmpty(body))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT is missing resource, type, or body and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }

                if (type != "surface" && type != "surfaceOnly" && type != "underground")
                {
                    KShared.LogError("Deposit \"" + resource + "\" has invalid type \"" + type
                        + "\" and was not loaded.", "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }

                bool render = false;
                if (node.HasValue("render") && !bool.TryParse(node.GetValue("render"), out render))
                {
                    KShared.LogError("Deposit \"" + resource
                        + "\" has a malformed render setting and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (type != "underground" && render)
                    KShared.LogWarning("Deposit \"" + resource
                        + "\" requests rendering, which is not implemented yet.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");

                if (!TryReadInt(node, "minAmount", 5, out int minAmount)
                    || !TryReadInt(node, "maxAmount", 10, out int maxAmount)
                    || !TryReadFloat(node, "minRadius", 10f, out float minRadius)
                    || !TryReadFloat(node, "maxRadius", 20f, out float maxRadius)
                    || !TryReadFloat(node, "depthUnderground", 50f, out float depthUnderground))
                {
                    KShared.LogError("Deposit \"" + resource
                        + "\" has a malformed numeric setting and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                string biome = KShared.GetStrValueFromCFG(node, "biome", null);
                biome = string.IsNullOrWhiteSpace(biome) ? null : biome.Trim();

                if (minAmount < 0 || maxAmount < minAmount)
                {
                    KShared.LogError("Deposit \"" + resource + "\" has invalid amount range ["
                        + minAmount + ", " + maxAmount + "] and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (!IsFinitePositive(minRadius) || !IsFinitePositive(maxRadius)
                    || maxRadius < minRadius)
                {
                    KShared.LogError("Deposit \"" + resource + "\" has invalid radius range ["
                        + minRadius + ", " + maxRadius + "] and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (FlightGlobals.GetBodyByName(body) == null)
                {
                    KShared.LogError("Deposit \"" + resource + "\" refers to unknown body \""
                        + body + "\" and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (PartResourceLibrary.Instance.GetDefinition(resource) == null)
                {
                    KShared.LogError("Deposit refers to unknown resource \"" + resource
                        + "\" and was not loaded.", "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (biome != null && !KShared.GetBiomeNames(body).Contains(biome))
                {
                    KShared.LogError("Deposit \"" + resource + "\" refers to unknown biome \""
                        + biome + "\" on \"" + body + "\" and was not loaded.",
                        "KSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }

                int amount = NextInclusive(shared.rand, minAmount, maxAmount);
                if (type == "surface")
                {
                    string resource2 = node.GetValue("resource2")?.Trim();
                    float surfaceDepth = 0f;
                    float undergroundStart = 0f;
                    bool validDepths = TryReadFloat(node, "depthSurface", 10f,
                            out surfaceDepth)
                        && TryReadFloat(node, "depthUndergroundStart", 100f,
                            out undergroundStart);
                    if (!validDepths || string.IsNullOrEmpty(resource2)
                        || !IsFinitePositive(surfaceDepth)
                        || !IsFiniteNonNegative(undergroundStart)
                        || !IsFinitePositive(depthUnderground)
                        || !IsFiniteNonNegative(undergroundStart + depthUnderground)
                        || PartResourceLibrary.Instance.GetDefinition(resource2) == null)
                    {
                        KShared.LogError("Surface deposit \"" + resource
                            + "\" has invalid resource2 or depth values and was not loaded.",
                            "KSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }

                    for (int i = 0; i < amount; i++)
                    {
                        KhemistryGDeposit deposit = new KhemistryGDeposit(shared, body, biome,
                            surfaceDepth, resource, minRadius, maxRadius, resource2,
                            undergroundStart, depthUnderground);
                        shared.surfaceDeposits.Add(deposit);
                        if (deposit.PairGDeposit != null)
                            shared.undergroundDeposits.Add(deposit.PairGDeposit);
                    }
                }
                else if (type == "surfaceOnly")
                {
                    if (!TryReadFloat(node, "depthSurface", 10f, out float surfaceDepth)
                        || !IsFinitePositive(surfaceDepth))
                    {
                        KShared.LogError("Surface-only deposit \"" + resource
                            + "\" has invalid depthSurface and was not loaded.",
                            "KSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }
                    for (int i = 0; i < amount; i++)
                        shared.surfaceDeposits.Add(new KhemistryGDeposit(shared, body, biome,
                            surfaceDepth, resource, minRadius, maxRadius, null, 0f, 0f));
                }
                else
                {
                    if (!TryReadFloat(node, "depthUndergroundStart", 100f,
                            out float undergroundStart)
                        || !IsFiniteNonNegative(undergroundStart)
                        || !IsFinitePositive(depthUnderground)
                        || !IsFiniteNonNegative(undergroundStart + depthUnderground))
                    {
                        KShared.LogError("Underground deposit \"" + resource
                            + "\" has invalid depth values and was not loaded.",
                            "KSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }
                    for (int i = 0; i < amount; i++)
                        shared.undergroundDeposits.Add(new KhemistryUDeposit(shared, body, biome,
                            undergroundStart, depthUnderground, resource, minRadius, maxRadius));
                }
            }

            KShared.Log("Created " + shared.undergroundDeposits.Count
                + " underground deposits.", "KSharedMainMenu/GenerateConfiguredDeposits");
            KShared.Log("Created " + shared.surfaceDeposits.Count
                + " surface deposits.", "KSharedMainMenu/GenerateConfiguredDeposits");
            return true;
        }

        private static bool IsFiniteNonNegative(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        private static bool IsFinitePositive(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        private static int NextInclusive(System.Random random, int min, int max)
        {
            if (min == max) return min;
            long range = (long)max - min + 1L;
            long offset = (long)(random.NextDouble() * range);
            return (int)((long)min + offset);
        }

        private static bool TryReadInt(ConfigNode node, string key, int defaultValue,
            out int value)
        {
            value = defaultValue;
            return node != null && (!node.HasValue(key)
                || int.TryParse(node.GetValue(key), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value));
        }

        private static bool TryReadFloat(ConfigNode node, string key, float defaultValue,
            out float value)
        {
            value = defaultValue;
            if (node == null) return false;
            if (!node.HasValue(key)) return true;
            return float.TryParse(node.GetValue(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
