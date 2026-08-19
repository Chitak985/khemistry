using System.Linq;
using UnityEngine;

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
            kinst = KShared.Instance;
            if (kinst == null)
            {
                Debug.Log("Khemistry (KSharedMainMenu/Awake): No KShared.Instance and Khemistry is about to have a bad time");
            }

            if (_instance != null)
            {
                KShared.LogError("Another instance of KSharedMainMenu was found, self destructing...", "KSharedMainMenu/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Celestial body list
            kinst.celestialBodies = FlightGlobals.Bodies.Select(b => b.bodyName).ToList();

            // Resource deposits
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_RESOURCE_DEPOSIT"))
            {
                if (!node.HasValue("resource"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT does not define a resource it contains and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("type"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a type and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (!node.HasValue("body"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not define a body to be placed on and was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }
                if (node.GetValue("type") == "surface" && !node.HasValue("resource2"))
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" is a surface type deposit without a resource2 value. It was not loaded.", "KSharedMainMenu/Awake");
                    continue;
                }

                if (node.GetValue("type") != "underground" && node.GetValue("render") == "true")
                {
                    KShared.LogWarning("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" attempts to render but that is not implemented yet.", "KSharedMainMenu/Awake");
                }

                int maxAmount = KShared.GetIntValueFromCFG(node, "maxAmount", 10) + 1;
                int minAmount = KShared.GetIntValueFromCFG(node, "minAmount", 5);
                int maxRadius = KShared.GetIntValueFromCFG(node, "maxRadius", 20) + 1;
                int minRadius = KShared.GetIntValueFromCFG(node, "minRadius", 10);
                string body = node.GetValue("body");
                string resource = node.GetValue("resource");
                string biome = KShared.GetStrValueFromCFG(node, "biome", null);
                float depthUnderground = KShared.GetFloatValueFromCFG(node, "depthUnderground", 50);

                if (node.GetValue("type") == "surface")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, node.GetValue("resource2"), KShared.GetFloatValueFromCFG(node, "depthUndergroundStart", 100)));
                }
                else if (node.GetValue("type") == "surfaceOnly")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.surfaceDeposits.Add(new KhemistryGDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthSurface", 10), resource, minRadius, maxRadius, null, 0));
                }
                else if (node.GetValue("type") == "underground")
                {
                    for (int i = 0; i < kinst.rand.Next(minAmount, maxAmount); i++)
                        kinst.undergroundDeposits.Add(new KhemistryUDeposit(kinst, body, biome, KShared.GetFloatValueFromCFG(node, "depthUndergroundStart", 100), depthUnderground, resource, minRadius, maxRadius));
                }
                else
                {
                    KShared.LogError("A KHEMISTRY_RESOURCE_DEPOSIT with resource \"" + node.GetValue("resource") + "\" does not have a valid type and was not loaded. The type was \"" + node.GetValue("type") + "\".", "KSharedMainMenu/Awake");
                }
            }
            KShared.Log("Created " + kinst.undergroundDeposits.Count().ToString() + " underground deposits.", "KSharedMainMenu/Awake");
            KShared.Log("Created " + kinst.surfaceDeposits.Count().ToString() + " surface deposits.", "KSharedMainMenu/Awake");

            // KhemistryISRU recipes
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRYISRU_RECIPE"))
            {
                if (!node.HasValue("name"))
                {
                    KShared.LogError("A KHEMISTRYISRU_RECIPE has no name!", "KSharedMainMenu/Awake");
                    continue;
                }
                kinst.batchRecipeList.Add(new KhemistryISRURecipe(node, node.GetValue("name")));
            }
            KShared.Log("Created " + kinst.batchRecipeList.Count.ToString() + " KhemistryISRU recipes.", "KSharedMainMenu/Awake");

            // Material definitions
            int materialCount = 0;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KHEMISTRY_MATERIAL"))
            {
                KhemistryMaterial tmp = new KhemistryMaterial(node);
                if (tmp != null)
                {
                    kinst.materialList.Add(tmp);
                    materialCount++;
                }
            }
            KShared.Log("Created " + materialCount.ToString() + " material definitions.", "KSharedMainMenu/Awake");
        }
    }
}
