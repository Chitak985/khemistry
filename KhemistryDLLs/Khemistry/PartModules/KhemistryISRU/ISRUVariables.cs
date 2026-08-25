using System.Collections.Generic;
using UnityEngine;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        /// <summary> Is the converter currently running </summary>
        [KSPField(isPersistant = true)]
        public bool isRunning = false;

        /// <summary> Does the converter need maintenance </summary>
        [KSPField(isPersistant = true)]
        public bool needsMaintenance = false;

        /// <summary> Status display of the ISRU </summary>
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Status", groupName = "khemistryisru",
                  groupDisplayName = "Khemistry ISRU", groupStartCollapsed = false)]
        public string statusDisplay = "Stopped";

        /// <summary> Charge display of the ISRU </summary>
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryisru")]
        public string chargeDisplay = "N/A";

        /// <summary> Progress display of the ISRU </summary>
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Progress", groupName = "khemistryisru")]
        public string progressDisplay = "Off";

        /// <summary> State display of the ISRU </summary>
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryisru")]
        public string stateDisplay = "Off";

        /// <summary> Override of the active animation's name </summary>
        [KSPField(isPersistant = false)]
        public string activeAnimationNameOverride = "";

        /// <summary> The active animation reference </summary>
        private Animation _activeAnim;

        /// <summary> Name of the active animation </summary>
        private string _activeAnimationName;

        /// <summary> Is the animation playing </summary>
        private bool _animationPlaying = false;

        /// <summary> The current state of this converter, see <see cref="KShared.ChargablePartState"/> for options. </summary>
        [KSPField(isPersistant = true)]
        public KShared.ChargablePartState state = KShared.ChargablePartState.Off;

        [KSPField(isPersistant = false)] public string ConverterName = "Converter";
        [KSPField(isPersistant = false)] public string StartActionName = "Start working";
        [KSPField(isPersistant = false)] public string StopActionName = "Stop working";

        /// <summary>
        /// The moduleType loaded from the MODULE node. "normal" (default) behaves as before;
        /// "kerbalEVA" is EVA-suit-cell-routed ISRU meant to live on a kerbal part; "partEVA" is
        /// reserved for future use and is not currently implemented.
        /// </summary>
        [KSPField(isPersistant = false)] public string moduleType = "normal";

        /// <summary>
        /// The KhemistryKerbal this converter routes resources/materials through when
        /// moduleType == "kerbalEVA". Only set (and required) in that mode.
        /// </summary>
        protected KhemistryKerbal _kerbalHost = null;

        /// <summary> Does the converter require charging </summary>
        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        /// <summary> Charge rate in decimal percent </summary>
        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;

        /// <summary> Charge decay rate in decimal percent </summary>
        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;

        protected List<string> _chargeNames = new List<string>();
        protected List<float> _chargeAmounts = new List<float>();
        protected bool _moduleChargingRequired = false;
        protected float _moduleChargeRate = 0f;
        protected float _moduleChargeDecayRate = 0f;
        protected readonly List<string> _moduleChargeNames = new List<string>();
        protected readonly List<float> _moduleChargeAmounts = new List<float>();

        /// <summary> Percentage of current charge </summary>
        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        protected bool _controlsShowPAW = true;
        protected bool _controlsShowEVA = false;

        /// <summary> Runtime data reference </summary>
        protected KhemistryRuntimeData _runtimeData = null;

        // The actual values, multiplied by a multiplier
        protected float _maxInteractionDistance = 7f;
        protected float _maxDisplayDistance = 10f;

        // The values loaded from the config
        protected float _configMaxInteractionDistance = 7f;
        protected float _configMaxDisplayDistance = 10f;

        /// <summary> List of recipes used by the ISRU </summary>
        protected List<KhemistryISRURecipe> recipes = new List<KhemistryISRURecipe>();

        /// <summary> Whether a config error occured and the ISRU cannot run </summary>
        protected bool _fatalConfigError = false;

        ///// Recipe importing /////
        [KSPField(isPersistant = false)] public string recipeType = null;
        [KSPField(isPersistant = false)] public string recipeSubtype = null;
        [KSPField(isPersistant = false)] public string recipeSubsubtype = null;

        [KSPField(isPersistant = false)] public float recipeMultiplier = 1f;

        [KSPField(isPersistant = false)] public bool workersCrewSamePart = false;

        protected readonly List<string> _recipeNames = new List<string>();
        protected readonly List<float> _recipeMultipliers = new List<float>();

        ///// Active recipe /////
        [KSPField(isPersistant = true)] public string activeRecipeName = null;
        [KSPField(isPersistant = true)] public double batchProgress = 0.0;

        protected KhemistryISRURecipe _activeRecipe = null;

        // Parallel to _activeRecipe._passiveInputs; serialized through PASSIVE_INPUT_STATE.
        protected readonly List<double> _passiveTimers = new List<double>();

        // Cumulative amount actually withdrawn per passive input since the last time
        // batchProgress was reset to 0 — needed so STOP can refund exactly what was taken
        // during the in-progress batch, while VOID/MAINT discard it instead.
        protected readonly List<double> _passiveConsumedThisBatch = new List<double>();
        protected readonly List<double> _loadedPassiveTimers = new List<double>();
        protected readonly List<double> _loadedPassiveConsumed = new List<double>();

        protected readonly Dictionary<KhemistryISRURecipe.ResourceOutputMaterial, double> _materialOutputAmount =
            new Dictionary<KhemistryISRURecipe.ResourceOutputMaterial, double>();
        protected readonly List<ConfigNode> _pendingMaterialOutputNodes = new List<ConfigNode>();

        private static readonly System.Text.RegularExpressions.Regex _randfPattern =
           new System.Text.RegularExpressions.Regex(
               @"^randf\(\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)\s*,\s*([+-]?[0-9]+)\s*\)$",
               System.Text.RegularExpressions.RegexOptions.Compiled);
    }
}
