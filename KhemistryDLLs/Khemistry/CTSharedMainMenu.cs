using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    /// <summary>
    /// A version of <see cref="CTShared"/> that loads during the MainMenu scene.
    /// Mainly loads many top-level configs.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CTSharedMainMenu : MonoBehaviour
    {
        /// <summary>
        /// The private instance set in MainMenu Awake().
        /// </summary>
        private static CTSharedMainMenu _instance;
        
        /// <summary>
        /// The instance set in MainMenu Awake().
        /// </summary>
        public static CTSharedMainMenu Instance => _instance;

        /// <summary>
        /// The instance of <see cref="CTShared"/> to use everywhere.
        /// This should exist by the time the game loads MainMenu.
        /// </summary>
        public CTShared kinst;

        public void Awake()
        {
            if (_instance != null)
            {
                CTShared.LogError("Another instance of CTSharedMainMenu was found, self destructing...", "CTSharedMainMenu/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            kinst = CTShared.Instance;
            if (kinst == null)
            {
                CTShared.LogFatalError("No CTShared instance and Khemistry is about to have a bad time", "CTSharedMainMenu/Awake");
                return;
            }

            // CTShared persists across scenes. Always reload into fresh collections if the main
            // menu is entered again, rather than accumulating duplicate data.
            kinst.surfaceDeposits.Clear();
            kinst.undergroundDeposits.Clear();
            kinst.batchRecipeList.Clear();
            kinst.materialList.Clear();

            // Set up <see cref="CTShared"/>'s celestial body list
            kinst.celestialBodies = FlightGlobals.Bodies.Select(b => b.bodyName).ToList();

            GenerateConfiguredDeposits(kinst);

            // ChitakTechISRU recipes
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRYISRU_RECIPE"))
            {
                if (!node.HasValue("name"))
                {
                    CTShared.LogError("A KHEMISTRYISRU_RECIPE has no name!", "CTSharedMainMenu/Awake");
                    continue;
                }
                string recipeName = node.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(recipeName))
                {
                    CTShared.LogError("A KHEMISTRYISRU_RECIPE has an empty name!", "CTSharedMainMenu/Awake");
                    continue;
                }
                if (kinst.batchRecipeList.Any(existingRecipe => existingRecipe._name == recipeName))
                {
                    CTShared.LogError("Duplicate ChitakTechISRU recipe name \"" + recipeName
                        + "\"; the later definition was ignored.", "CTSharedMainMenu/Awake");
                    continue;
                }
                KhemistryISRURecipe loadedRecipe = new KhemistryISRURecipe(node, recipeName);
                if (loadedRecipe.IsValid)
                    kinst.batchRecipeList.Add(loadedRecipe);
                else
                    CTShared.LogError("Invalid ChitakTechISRU recipe \"" + recipeName
                        + "\" was ignored.", "CTSharedMainMenu/Awake");
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
                    CTShared.LogError("Duplicate Khemistry material name \"" + tmp.name
                        + "\"; the later definition was ignored.", "CTSharedMainMenu/Awake");
            }
            CTShared.Log("Created " + materialCount.ToString() + " material definitions.", "CTSharedMainMenu/Awake");

            // Recipe resource/material references can only be checked after all Khemistry
            // material definitions have loaded. Remove invalid recipes so a misspelled input
            // cannot accidentally turn into a free conversion.
            kinst.batchRecipeList.RemoveAll(recipe =>
                !recipe.ValidateReferences(kinst.materialList, "CTSharedMainMenu/Awake"));
            CTShared.Log("Created " + kinst.batchRecipeList.Count.ToString()
                + " valid ChitakTechISRU recipes.", "CTSharedMainMenu/Awake");
        }

        public void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Rebuilds the procedural deposit set from configuration. This is intentionally
        /// reusable by the per-save scenario so two new saves created during one KSP process
        /// receive independent random locations instead of clones of the main-menu roll.
        /// Not using the kinst variable as this is a static method and
        /// is called in DepositPersistence.cs/TryApplyLoadedDeposits.
        /// </summary>
        internal static bool GenerateConfiguredDeposits(CTShared shared)
        {
            // Null check everything
            if (shared == null || CTShared.rand == null || GameDatabase.Instance == null
                || PartResourceLibrary.Instance == null || FlightGlobals.Bodies == null
                || FlightGlobals.Bodies.Count == 0)
                return false;

            // Clear all of the old deposits
            shared.surfaceDeposits.Clear();
            shared.undergroundDeposits.Clear();

            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RESOURCE_DEPOSIT"))
            {
                string resource = node.GetValue("resource")?.Trim();
                string type = node.GetValue("type")?.Trim();
                string body = node.GetValue("body")?.Trim();

                // Resource error checking
                string resoError;
                bool resoErr = true;

                if (string.IsNullOrEmpty(resource))
                    resoError = "MISSING";
                else
                {
                    resoError = "Present (" + resource + ")";
                    resoErr = false;
                }

                // Type error checking
                string typeError;
                bool typeErr = true;

                if (string.IsNullOrEmpty(type))
                    typeError = "MISSING";
                else if (type != "surface" && type != "surfaceOnly" && type != "underground")
                    typeError = "INVALID ("+type+")";
                else
                {
                    typeError = "Valid ("+type+")";
                    typeErr = false;
                }


                // Body error checking
                string bodyError;
                bool bodyErr = true;

                if (string.IsNullOrEmpty(body))
                    bodyError = "MISSING";
                else
                    if (shared.celestialBodies == null)
                        bodyError = "Cannot validate, celestialBodies is null (" + body + ")";
                    else if (shared.celestialBodies.Count == 0)
                        bodyError = "Cannot validate, celestialBodies is empty (" + body + ")";
                    else
                        if (shared.celestialBodies.Contains(body))
                        {
                            bodyError = "Valid (" + body + ")";
                            bodyErr = false;
                        }
                        else
                            bodyError = "INVALID (" + body + ")";

                // Print error message if needed
                if (resoErr || typeErr || bodyErr)
                {
                    CTShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT encountered a value-related error: "
                        + $"resource: {resoError}, "
                        + $"type: {typeError}, "
                        + $"body: {bodyError}",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }

                if (!KhemistryDepositRenderingConfig.TryRead(node, resource, type, true,
                        out bool render, out string model))
                    continue;

                if (!CTShared.TryGetIntValueFromCFG(node, "minAmount", 5, out int minAmount)
                    || !CTShared.TryGetIntValueFromCFG(node, "maxAmount", 10, out int maxAmount)
                    || !CTShared.TryGetFloatValueFromCFG(node, "minRadius", 10f, out float minRadius)
                    || !CTShared.TryGetFloatValueFromCFG(node, "maxRadius", 20f, out float maxRadius)
                    || !CTShared.TryGetFloatValueFromCFG(node, "depthUnderground", 50f, out float depthUnderground))
                {
                    CTShared.LogError("Deposit \"" + resource
                        + "\" has a malformed numeric setting and was not loaded.",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                string biome = CTShared.GetStrValueFromCFG(node, "biome", null);
                biome = string.IsNullOrWhiteSpace(biome) ? null : biome.Trim();

                if (minAmount < 0 || maxAmount < minAmount)
                {
                    CTShared.LogError("Deposit \"" + resource + "\" has invalid amount range ["
                        + minAmount + ", " + maxAmount + "] and was not loaded.",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (!CTShared.IsFinitePositive(minRadius) || !CTShared.IsFinitePositive(maxRadius)
                    || maxRadius < minRadius)
                {
                    CTShared.LogError("Deposit \"" + resource + "\" has invalid radius range ["
                        + minRadius + ", " + maxRadius + "] and was not loaded.",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (FlightGlobals.GetBodyByName(body) == null)
                {
                    CTShared.LogError("Deposit \"" + resource + "\" refers to unknown body \""
                        + body + "\" and was not loaded.",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (PartResourceLibrary.Instance.GetDefinition(resource) == null)
                {
                    CTShared.LogError("Deposit refers to unknown resource \"" + resource
                        + "\" and was not loaded.", "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }
                if (biome != null && !CTShared.GetBiomeNames(body).Contains(biome))
                {
                    CTShared.LogError("Deposit \"" + resource + "\" refers to unknown biome \""
                        + biome + "\" on \"" + body + "\" and was not loaded.",
                        "CTSharedMainMenu/GenerateConfiguredDeposits");
                    continue;
                }

                int amount = CTShared.RandomIntInclusive(minAmount, maxAmount);
                if (type == "surface")
                {
                    string resource2 = node.GetValue("resource2")?.Trim();
                    float undergroundStart = 0f;
                    bool validDepths = CTShared.TryGetFloatValueFromCFG(node, "depthSurface", 10f,
                            out float surfaceDepth)
                        && CTShared.TryGetFloatValueFromCFG(node, "depthUndergroundStart", 100f,
                            out undergroundStart);
                    if (!validDepths || string.IsNullOrEmpty(resource2)
                        || !CTShared.IsFinitePositive(surfaceDepth)
                        || !CTShared.IsFiniteNonNegative(undergroundStart)
                        || !CTShared.IsFinitePositive(depthUnderground)
                        || !CTShared.IsFiniteNonNegative(undergroundStart + depthUnderground)
                        || PartResourceLibrary.Instance.GetDefinition(resource2) == null)
                    {
                        CTShared.LogError("Surface deposit \"" + resource
                            + "\" has invalid resource2 or depth values and was not loaded.",
                            "CTSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }

                    for (int i = 0; i < amount; i++)
                    {
                        ChitakTechGDeposit deposit = new ChitakTechGDeposit(body, biome,
                            surfaceDepth, resource, minRadius, maxRadius, resource2,
                            undergroundStart, depthUnderground, render, model);
                        shared.surfaceDeposits.Add(deposit);
                        if (deposit.PairGDeposit != null)
                            shared.undergroundDeposits.Add(deposit.PairGDeposit);
                    }
                }
                else if (type == "surfaceOnly")
                {
                    if (!CTShared.TryGetFloatValueFromCFG(node, "depthSurface", 10f, out float surfaceDepth)
                        || !CTShared.IsFinitePositive(surfaceDepth))
                    {
                        CTShared.LogError("Surface-only deposit \"" + resource
                            + "\" has invalid depthSurface and was not loaded.",
                            "CTSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }
                    for (int i = 0; i < amount; i++)
                        shared.surfaceDeposits.Add(new ChitakTechGDeposit(body, biome,
                            surfaceDepth, resource, minRadius, maxRadius, null, 0f, 0f,
                            render, model));
                }
                else
                {
                    if (!CTShared.TryGetFloatValueFromCFG(node, "depthUndergroundStart", 100f,
                            out float undergroundStart)
                        || !CTShared.IsFiniteNonNegative(undergroundStart)
                        || !CTShared.IsFinitePositive(depthUnderground)
                        || !CTShared.IsFiniteNonNegative(undergroundStart + depthUnderground))
                    {
                        CTShared.LogError("Underground deposit \"" + resource
                            + "\" has invalid depth values and was not loaded.",
                            "CTSharedMainMenu/GenerateConfiguredDeposits");
                        continue;
                    }
                    for (int i = 0; i < amount; i++)
                        shared.undergroundDeposits.Add(new ChitakTechUDeposit(body, biome,
                            undergroundStart, depthUnderground, resource, minRadius, maxRadius));
                }
            }

            CTShared.Log("Created " + shared.undergroundDeposits.Count
                + " underground deposits.", "CTSharedMainMenu/GenerateConfiguredDeposits");
            CTShared.Log("Created " + shared.surfaceDeposits.Count
                + " surface deposits.", "CTSharedMainMenu/GenerateConfiguredDeposits");
            return true;
        }
    }
}
