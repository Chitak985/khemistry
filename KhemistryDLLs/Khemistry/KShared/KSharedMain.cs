using KSP.UI.Screens;
using System.Collections.Generic;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// The shared data for many Khemistry classes.
    /// Contains various methods and variables, used for GUI and as helpers. Handles all logging.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public partial class KShared : MonoBehaviour
    {
        public void Awake()
        {
            if (_instance != null)
            {
                KShared.LogError("Another instance of KShared was found, self destructing...", "KShared/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            // GUIUtility.GetControlID is intended to be called while Unity is processing
            // OnGUI.  These windows need stable IDs long before they are first drawn, so use
            // process-local hashes instead of touching the GUI state from Awake.
            string windowIdSeed = GetInstanceID().ToString();
            _windowId = ("Khemistry.Selector." + windowIdSeed).GetHashCode();
            _amountWindowId = ("Khemistry.Amount." + windowIdSeed).GetHashCode();
            _depositsWindowId = ("Khemistry.Deposits." + windowIdSeed).GetHashCode();

            _depositsButtonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color depositIconColor = new Color(0.85f, 0.55f, 0.15f, 1f);
            Color[] depositPixels = new Color[38 * 38];
            for (int i = 0; i < depositPixels.Length; i++) depositPixels[i] = depositIconColor;
            _depositsButtonTexture.SetPixels(depositPixels);
            _depositsButtonTexture.Apply();

            GameEvents.onGUIApplicationLauncherReady.Add(OnDepositsLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnDepositsLauncherDestroyed);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);

            ResetConstructionResourcesToDefaults();

            KShared.Log("KShared initialized.", "KShared/Awake");
        }

        public static Dictionary<string, double> CreateStartingConstructionResourceLedger()
        {
            return new Dictionary<string, double>
            {
                { "CuWiring", 10.0 },       // Copper wires
                { "Sn60Pb40Alloy", 10.0 },  // Soldering
                { "Aluminium6061", 10.0 }   // Simple construction material
            };
        }

        public void ResetConstructionResourcesToDefaults()
        {
            ResourceDict.Clear();
            foreach (KeyValuePair<string, double> resource in
                CreateStartingConstructionResourceLedger())
                ResourceDict[resource.Key] = resource.Value;
        }

        public void OnDestroy()
        {
            if (_instance != this) return;

            GameEvents.onGUIApplicationLauncherReady.Remove(OnDepositsLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnDepositsLauncherDestroyed);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            if (_depositsToolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_depositsToolbarButton);
            CloseTransientWindows();
            if (_depositsButtonTexture != null)
                Destroy(_depositsButtonTexture);
            _depositsToolbarButton = null;
            _depositsButtonTexture = null;
            _instance = null;
        }
    }
}
