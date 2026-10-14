using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChitakTech
{
    /// <summary>
    /// Loads the data for the Resource and Recipe Library from the <see cref="GameDatabase"/>.
    /// The Resource Library is unusable until this finishes loading.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KEILoader : MonoBehaviour
    {
        /// <summary> Singleton instance of this class </summary>
        private static KEILoader _instance;

        /// <summary> List of loaded resources, <see cref="KhemistryResourceInfo"/> </summary>
        public static List<KhemistryResourceInfo> Resources { get; private set; }
        /// <summary> List of loaded recipes, <see cref="KhemistryRecipeInfo"/> </summary>
        public static List<KhemistryRecipeInfo> Recipes { get; private set; }
        /// <summary> Is loading finished </summary>
        public static bool IsLoaded { get; private set; } = false;

        public void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public void Awake()
        {
            // Check for duplicate
            if (_instance != null && _instance != this)
            {
                CTShared.Log("Existing instance detected, self-destructing...", "KhemistryLibraryLoader/Awake");
                Destroy(gameObject);
                return;
            }
            // Set up singleton
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Load all of the data
            CTShared.Log("Loading resource and recipe library...", "KhemistryLibraryLoader/Awake");

            // Load all descriptions from config nodes
            Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                // Get the values
                string resName = node.GetValue("name");
                string desc = node.GetValue("khemistryDescription");

                // Verify name
                if (string.IsNullOrEmpty(resName))
                {
                    CTShared.LogError("RESOURCE_DEFINITION has an empty or null name and was not loaded!", "KhemistryLibraryLoader/Awake");
                    continue;
                }

                // Get the description if there is one
                if (!string.IsNullOrEmpty(desc))
                    descriptions[resName] = desc;
            }

            // Load resources
            Resources = new List<KhemistryResourceInfo>();
            foreach (PartResourceDefinition def in PartResourceLibrary.Instance.resourceDefinitions)
            {
                // Get the corresponding description
                descriptions.TryGetValue(def.name, out string description);
                
                // Add resource to resource list
                Resources.Add(new KhemistryResourceInfo
                {
                    name = def.name,
                    displayName = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName,
                    abbreviation = def.abbreviation,
                    unitCost = def.unitCost,
                    density = def.density,
                    volume = def.volume,
                    flowMode = def.resourceFlowMode.ToString(),
                    transfer = def.resourceTransferMode.ToString(),
                    isTweakable = def.isTweakable,
                    isVisible = def.isVisible,
                    description = description
                });
            }

            // Sort resources alphabetically
            Resources.Sort((a, b) =>
                string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

            // Load recipes
            Recipes = new List<KhemistryRecipeInfo>();
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                // Load all <see cref="PartModule"/>s on the part
                foreach (ConfigNode moduleNode in partNode.GetNodes("MODULE"))
                {
                    // Skip anything that isn't a converter
                    if (moduleNode.GetValue("name") != "ModuleResourceConverter") continue;

                    // Load the recipe
                    KhemistryRecipeInfo recipe = new KhemistryRecipeInfo
                    {
                        converterName = moduleNode.GetValue("ConverterName") ?? "Unnamed Converter",
                        generatesHeat = string.Equals(moduleNode.GetValue("GeneratesHeat"), "true",
                                            StringComparison.OrdinalIgnoreCase),
                        partTitle = partNode.GetValue("title") ?? partNode.GetValue("name") ?? "Unknown Part"
                    };

                    // Add recipe inputs
                    foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
                        if (LoadIONode(inputNode, recipe, out KhemistryRecipeIO inres))
                            recipe.inputs.Add(inres);
                        
                     // Add recipe outputs
                    foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
                        if (LoadIONode(outputNode, recipe, out KhemistryRecipeIO outres))
                            recipe.outputs.Add(outres);

                    // Add recipe to main recipe list
                    Recipes.Add(recipe);
                }
            }

            // Finish loading and log results
            IsLoaded = true;
            CTShared.Log(
                string.Format($"Library loaded: {Resources.Count} resources and {Recipes.Count} recipes."),
                "KhemistryLibraryLoader/Awake");
        }

        /// <summary>
        /// Load the provided IO node of a recipe.
        /// This can be either INPUT_RESOURCE or OUTPUT_RESOURCE.
        /// </summary>
        private static bool LoadIONode(ConfigNode node, KhemistryRecipeInfo recipe, out KhemistryRecipeIO result)
        {
            // Default return value
            result = new KhemistryRecipeIO { resourceName = "LOAD ERROR", ratio = 0 };
            
            // Get the resource name and verify it
            string resName = node.GetValue("ResourceName");
            if (string.IsNullOrEmpty(resName))
            {
                CTShared.LogError($"Skipped IO node with empty ResourceName in converter \"{recipe.converterName}\"!",
                    "KhemistryLibraryLoader/LoadIONode");
                return false;
            }

            // Get the ratio value
            string raw = node.GetValue("Ratio");

            // Verify the ratio
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double ratio))
            {
                // Check if ratio is finite
                if (!CTShared.IsFinite(ratio))
                {
                    CTShared.LogError($"Failed to load NaN or Infinity ratio \"{raw}\" in converter \"{recipe.converterName}\"!",
                        "KhemistryLibraryLoader/LoadIONode");
                    return false;
                }
                // Check if ratio is non-negative
                if (ratio < 0.0)
                {
                    CTShared.LogError($"Failed to load negative ratio \"{raw}\" in converter \"{recipe.converterName}\"!",
                        "KhemistryLibraryLoader/LoadIONode");
                    return false;
                }

                // Load ratio
                result = new KhemistryRecipeIO { resourceName = resName, ratio = ratio };
                return true;
            }

            // Parsing error
            CTShared.LogError($"Failed to parse ratio \"{raw}\" in converter \"{recipe.converterName}\"!",
                "KhemistryLibraryLoader/LoadIONode");
            return false;
        }
    }
}
