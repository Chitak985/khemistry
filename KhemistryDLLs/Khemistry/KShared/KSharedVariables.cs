using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    public partial class KShared
    {
        // Instance
        private static KShared _instance;
        public static KShared Instance => _instance;

        // Selector GUI
        private bool _selectorVisible = false;
        private Vector2 _selectorScroll = Vector2.zero;
        private string _selectorTitle = "";
        private List<string> _selectorOptions;
        private Action<string> _selectorCallback;
        private Rect _windowRect = new Rect(0, 0, 320, 300);
        private int _windowId;

        // Amount selector GUI
        private bool _amountVisible = false;
        private string _amountTitle = "";
        private float _amountValue = 0f;
        private float _amountMin = 0f;
        private float _amountMax = 1f;
        private Action<float> _amountCallback;
        private Rect _amountRect = new Rect(0, 0, 320, 130);
        private int _amountWindowId;

        // Recipe settings selector GUI
        private bool _recipeSettingsVisible = false;
        private Vector2 _recipeSettingsScroll = Vector2.zero;
        private string _recipeSettingsTitle = "";
        private List<KhemistryISRURecipe.RecipeSetting> _recipeSettings =
            new List<KhemistryISRURecipe.RecipeSetting>();
        private Dictionary<string, double> _recipeSettingWorkingValues =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private Action<Dictionary<string, double>> _recipeSettingsCallback;
        private Rect _recipeSettingsRect = new Rect(0, 0, 820, 430);
        private int _recipeSettingsWindowId;

        // Nearby deposits toolbar GUI
        private bool _depositsVisible = false;
        private Rect _depositsRect = new Rect(0, 0, 380, 420);
        private int _depositsWindowId;
        private Vector2 _depositsScroll = Vector2.zero;
        private ApplicationLauncherButton _depositsToolbarButton;
        private Texture2D _depositsButtonTexture;

        // Deposit data for the active save (persisted by KhemistryDepositsScenario)
        public List<KhemistryUDeposit> undergroundDeposits = new List<KhemistryUDeposit>();
        public List<KhemistryGDeposit> surfaceDeposits = new List<KhemistryGDeposit>();

        // Loaded BatchISRU recipe data
        public List<KhemistryISRURecipe> batchRecipeList = new List<KhemistryISRURecipe>();

        // Loaded material data
        public List<KhemistryMaterial> materialList = new List<KhemistryMaterial>();

        // Currently loaded celestial bodies
        public List<string> celestialBodies = new List<string>();

        // Resource dictionary for KhemistryConstructionOverhaul
        // Construction-resource balances can reach hundreds of millions of units while
        // transfers may be only a few units. Double precision keeps those small transfers
        // representable; float precision loses them at that scale.
        public Dictionary<string, double> ResourceDict = new Dictionary<string, double>();

        // Material instances delivered to the KSC by KhemistryConstructionOverhaul. This is an
        // unbounded logical ledger, not a PartModule container: the construction add-on persists
        // it per save and only performs exact (non-contaminating) material merges.
        public readonly List<KhemistryMaterialInstance> KSCMaterialContents =
            new List<KhemistryMaterialInstance>();

        // KhemistryConstructionOverhaul GUI (public to access from it)
        public List<string> _selectorResources;
        public bool _kcoSelectorVisible = false;

        // Random number generator
        public static System.Random rand = new System.Random();

        // Enumerators
        public enum SituationCondition
        {
            Any,
            Landed,
            Splashed,
            // KSP calls this Vessel.Situations.SPLASHED, but the public recipe syntax has
            // always documented the more descriptive name SplashedDown. Keep both spellings
            // as aliases so existing configs using Splashed continue to work.
            SplashedDown = Splashed,
            FlyingLow,
            FlyingHigh,
            SpaceLow,
            SpaceHigh,
            SubOrbital
        }

        /// <summary>
        /// The state of a chargable part.
        /// <list type="bullet">Off: The part is currently turned off and is not working.</list>
        /// <list type="bullet">Charging: Same as Off but the part is charging.</list>
        /// <list type="bullet">On: The part is currently turned on and is working.</list>
        /// </summary>
        public enum ChargablePartState { Off, Charging, On }
    }
}
