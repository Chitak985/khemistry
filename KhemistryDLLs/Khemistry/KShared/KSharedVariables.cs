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

        // Nearby deposits toolbar GUI
        private bool _depositsVisible = false;
        private Rect _depositsRect = new Rect(0, 0, 380, 420);
        private int _depositsWindowId;
        private Vector2 _depositsScroll = Vector2.zero;
        private ApplicationLauncherButton _depositsToolbarButton;
        private Texture2D _depositsButtonTexture;

        // Loaded deposit data (!TODO: will need to be per save file later)
        public List<KhemistryUDeposit> undergroundDeposits = new List<KhemistryUDeposit>();
        public List<KhemistryGDeposit> surfaceDeposits = new List<KhemistryGDeposit>();

        // Loaded BatchISRU recipe data
        public List<KhemistryISRURecipe> batchRecipeList = new List<KhemistryISRURecipe>();

        // Loaded material data
        public List<KhemistryMaterial> materialList = new List<KhemistryMaterial>();

        // Currently loaded celestial bodies
        public List<string> celestialBodies = new List<string>();

        // Resource dictionary for KhemistryConstructionOverhaul
        public Dictionary<string, float> ResourceDict = new Dictionary<string, float>();

        // KhemistryConstructionOverhaul GUI (public to access from it)
        public List<string> _selectorResources;
        public bool _kcoSelectorVisible = false;

        // Other
        public System.Random rand = new System.Random();

        // Enumerators
        public enum SituationCondition
        {
            Any, Landed, Splashed, FlyingLow, FlyingHigh, SpaceLow, SpaceHigh, SubOrbital
        }
    }
}
