using KSP.UI.Screens;
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
            _windowId = GUIUtility.GetControlID(FocusType.Passive);
            _amountWindowId = GUIUtility.GetControlID(FocusType.Passive);
            _depositsWindowId = GUIUtility.GetControlID(FocusType.Passive);

            _depositsButtonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color depositIconColor = new Color(0.85f, 0.55f, 0.15f, 1f);
            Color[] depositPixels = new Color[38 * 38];
            for (int i = 0; i < depositPixels.Length; i++) depositPixels[i] = depositIconColor;
            _depositsButtonTexture.SetPixels(depositPixels);
            _depositsButtonTexture.Apply();

            GameEvents.onGUIApplicationLauncherReady.Add(OnDepositsLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnDepositsLauncherDestroyed);

            // Starting resources
            ResourceDict.Add("CuWiring", 10.0f);       // Copper wires
            ResourceDict.Add("Sn60Pb40Alloy", 10.0f);  // Soldering
            ResourceDict.Add("Aluminium6061", 10.0f);   // Simple construction material

            KShared.Log("KShared initialized.", "KShared/Awake");
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnDepositsLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnDepositsLauncherDestroyed);
            if (_depositsToolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_depositsToolbarButton);
        }
    }
}
