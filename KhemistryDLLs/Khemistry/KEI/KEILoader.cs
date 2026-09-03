using System;
using System.Collections.Generic;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// Loads the data for the Resource and Recipe Library from the <see cref="GameDatabase"/>.
    /// The Resource Library is unusable until this finishes loading.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KEILoader : MonoBehaviour
    {
        private static KEILoader _instance;

        public static List<KhemistryResourceInfo> Resources { get; private set; }
        public static List<KhemistryRecipeInfo> Recipes { get; private set; }
        public static bool IsLoaded { get; private set; } = false;

        public void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadData();
        }

        public void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void LoadData()
        {
            KShared.Log("Loading resource and recipe library...", "KhemistryLibraryLoader/LoadData");

            // KSP resource names are case-sensitive (for example Co/CO, Hf/HF, No/NO and
            // Cn/CN are distinct elemental/molecular resources in this mod).
            var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string resName = node.GetValue("name");
                string desc = node.GetValue("khemistryDescription");
                if (!string.IsNullOrEmpty(resName) && !string.IsNullOrEmpty(desc))
                    descriptions[resName] = desc;
            }

            Resources = new List<KhemistryResourceInfo>();
            foreach (PartResourceDefinition def in PartResourceLibrary.Instance.resourceDefinitions)
            {
                descriptions.TryGetValue(def.name, out string description);
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
            Resources.Sort((a, b) =>
                string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

            Recipes = new List<KhemistryRecipeInfo>();
            foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
            {
                string partTitle = partNode.GetValue("title") ?? partNode.GetValue("name") ?? "Unknown Part";
                foreach (ConfigNode moduleNode in partNode.GetNodes("MODULE"))
                {
                    if (moduleNode.GetValue("name") != "ModuleResourceConverter") continue;

                    var recipe = new KhemistryRecipeInfo
                    {
                        converterName = moduleNode.GetValue("ConverterName") ?? "Unnamed Converter",
                        generatesHeat = string.Equals(moduleNode.GetValue("GeneratesHeat"), "true",
                                            StringComparison.OrdinalIgnoreCase),
                        partTitle = partTitle
                    };

                    foreach (ConfigNode inputNode in moduleNode.GetNodes("INPUT_RESOURCE"))
                    {
                        string resName = inputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        if (!TryReadRatio(inputNode, recipe.converterName, out double ratio)) continue;
                        recipe.inputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }
                    foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
                    {
                        string resName = outputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        if (!TryReadRatio(outputNode, recipe.converterName, out double ratio)) continue;
                        recipe.outputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }

                    Recipes.Add(recipe);
                }
            }

            IsLoaded = true;
            KShared.Log(
                string.Format("Library loaded: {0} resources, {1} recipes.", Resources.Count, Recipes.Count),
                "KhemistryLibraryLoader/LoadData");
        }

        private static bool TryReadRatio(ConfigNode node, string converterName, out double ratio)
        {
            string raw = node.GetValue("Ratio");
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ratio)
                && !double.IsNaN(ratio) && !double.IsInfinity(ratio) && ratio >= 0.0)
                return true;

            KShared.LogWarning("Recipe library skipped invalid ratio \"" + raw
                + "\" in converter \"" + converterName + "\".", "KhemistryLibraryLoader/LoadData");
            ratio = 0.0;
            return false;
        }
    }
}
