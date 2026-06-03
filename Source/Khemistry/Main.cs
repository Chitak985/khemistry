using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KShared : MonoBehaviour
    {
        private static KShared _instance;
        public static KShared Instance => _instance;

        private bool _selectorVisible = false;
        private Vector2 _selectorScroll = Vector2.zero;
        private string _selectorTitle = "";
        private List<string> _selectorOptions;
        private Action<string> _selectorCallback;
        private Rect _windowRect = new Rect(0, 0, 320, 300);
        private int _windowId;

        public void Awake()
        {
            if (_instance != null)
            {
                LogError("Another instance of KShared was found, self destructing...", "KShared/Awake");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _windowId = GUIUtility.GetControlID(FocusType.Passive);
            Log("KShared initialized.", "KShared/Awake");
        }

        public void ShowSelector(string title, List<string> options, Action<string> onSelect)
        {
            _selectorTitle = title;
            _selectorOptions = options;
            _selectorCallback = onSelect;
            _selectorScroll = Vector2.zero;
            _windowRect = new Rect(
                (Screen.width - _windowRect.width) / 2f,
                (Screen.height - _windowRect.height) / 2f,
                _windowRect.width,
                _windowRect.height
            );
            _selectorVisible = true;
        }

        private void OnGUI()
        {
            if (!_selectorVisible) return;
            _windowRect = GUILayout.Window(
                _windowId,
                _windowRect,
                DrawSelectorWindow,
                _selectorTitle,
                HighLogic.Skin.window
            );
        }

        private void DrawSelectorWindow(int windowId)
        {
            _selectorScroll = GUILayout.BeginScrollView(
                _selectorScroll,
                HighLogic.Skin.scrollView,
                GUILayout.Height(220f)
            );
            foreach (string option in _selectorOptions)
            {
                if (GUILayout.Button(option, HighLogic.Skin.button))
                {
                    _selectorVisible = false;
                    _selectorCallback(option);
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", HighLogic.Skin.button))
                _selectorVisible = false;

            GUI.DragWindow();
        }

        public void Log(string message, string func = null)
        {
            if (func != null)
                Debug.Log("Khemistry (" + func + "): " + message);
            else
                Debug.Log("Khemistry: " + message);
        }

        public void LogError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("Khemistry (" + func + "): " + message);
            else
                Debug.LogError("Khemistry: " + message);
        }
    }

    public class KhemistryFluidCell : PartModule
    {
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        public HashSet<string> AllowedResources = new HashSet<string>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            AllowedResources.Clear();
            if (node.HasNode("ALLOWED_RESOURCES"))
            {
                foreach (string name in node.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                    AllowedResources.Add(name.Trim());
                KShared.Instance?.Log(
                    "Loaded " + AllowedResources.Count + " allowed resources.",
                    "KhemistryFluidCell/OnLoad");
            }
            else
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryFluidCell but no ALLOWED_RESOURCES node. This part is now capable of storing anything.",
                    "KhemistryFluidCell/OnLoad");
            }
        }

        public override void OnUpdate()
        {
            ContentsDisplay = string.IsNullOrEmpty(ResourceName)
                ? "Empty"
                : string.Format("{0}: {1:F2} / {2:F2} kg", ResourceName, ResourceAmount, ResourceMaxAmount);
        }
    }

    public class KhemistryEVAFluidCellHandler : PartModule
    {
        private static readonly HashSet<string> FluidCellPartNames = new HashSet<string>
        {
            "NameConverterRadial"
        };

        private ModuleInventoryPart _inventory;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells in inventory";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            var allHandlers = part.FindModulesImplementing<KhemistryEVAFluidCellHandler>();
            if (allHandlers.Count > 1 && allHandlers[0] != this)
            {
                KShared.Instance?.Log("Duplicate handler found, removing self.", "KhemistryEVAFluidCellHandler/OnStart");
                part.RemoveModule(this);
                return;
            }

            _inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (_inventory == null)
                KShared.Instance?.LogError("No ModuleInventoryPart on Kerbal.", "KhemistryEVAFluidCellHandler/OnStart");
            else
                KShared.Instance?.Log("OnStart fired, inventory found.", "KhemistryEVAFluidCellHandler/OnStart");
        }

        public override void OnUpdate()
        {
            var cells = GetHeldCellSnapshots();
            if (cells.Count == 0)
            {
                CellContentsDisplay = "No cells in inventory";
                return;
            }
            var parts = new List<string>();
            for (int i = 0; i < cells.Count; i++)
            {
                string resName = ReadResourceName(cells[i]);
                float resAmount = ReadResourceAmount(cells[i]);
                float maxAmount = ReadMaxAmount(cells[i].partName);
                parts.Add(string.IsNullOrEmpty(resName)
                    ? string.Format("Cell {0}: Empty", i + 1)
                    : string.Format("Cell {0}: {1} {2:F1}/{3:F1} kg", i + 1, resName, resAmount, maxAmount));
            }
            CellContentsDisplay = string.Join("  |  ", parts.ToArray());
        }

        private List<StoredPart> GetHeldCellSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                if (FluidCellPartNames.Contains(stored.partName))
                    result.Add(stored);
            }
            return result;
        }

        private ProtoPartModuleSnapshot GetCellModuleSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
            {
                if (moduleSnap.moduleName == "KhemistryFluidCell")
                    return moduleSnap;
            }
            return null;
        }

        private string ReadResourceName(StoredPart stored)
            => GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceName") ?? "";

        private float ReadResourceAmount(StoredPart stored)
        {
            string val = GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceAmount");
            return val != null ? float.Parse(val) : 0f;
        }

        private float ReadMaxAmount(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.ResourceMaxAmount ?? 100f;

        private float ReadTransferDistance(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.TransferDistance ?? 10f;

        private HashSet<string> ReadAllowedResources(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.AllowedResources
                ?? new HashSet<string>();

        private void WriteResourceName(StoredPart stored, string name)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceName", name);

        private void WriteResourceAmount(StoredPart stored, float amount)
            => GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceAmount", amount.ToString("F4"));

        private List<Part> GetPartsInRange(float range)
        {
            var result = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                foreach (Part p in v.parts)
                {
                    if (p == this.part) continue;
                    if (Vector3.Distance(this.part.transform.position, p.transform.position) <= range)
                        result.Add(p);
                }
            return result;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from cell to nearby part",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVASendResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVASendResources!"); return; }

            var cells = GetHeldCellSnapshots();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage("No fluid cells in inventory.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
                ShowPartSelectorForSend(cells[0]);
            else
            {
                var labels = cells.Select((c, i) =>
                {
                    string resName = ReadResourceName(c);
                    float resAmount = ReadResourceAmount(c);
                    float maxAmount = ReadMaxAmount(c.partName);
                    return string.IsNullOrEmpty(resName)
                        ? string.Format("Cell {0}: Empty", i + 1)
                        : string.Format("Cell {0}: {1} {2:F1}/{3:F1} kg", i + 1, resName, resAmount, maxAmount);
                }).ToList();

                shared.ShowSelector("Which cell to send from?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForSend(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForSend(StoredPart stored)
        {
            var shared = KShared.Instance;
            string resourceName = ReadResourceName(stored);
            float resourceAmount = ReadResourceAmount(stored);
            float range = ReadTransferDistance(stored.partName);

            if (string.IsNullOrEmpty(resourceName) || resourceAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage("That cell is empty.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var targetParts = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string label = string.Format("{0} / {1}  (space: {2:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!targetParts.ContainsKey(label))
                        targetParts.Add(label, p);
                    break;
                }

            if (targetParts.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Send " + resourceName + " to...", targetParts.Keys.ToList(), label =>
            {
                Part target = targetParts[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource targetResource = target.Resources.Get(def.id);
                if (targetResource == null) return;

                double space = targetResource.maxAmount - targetResource.amount;
                double pushed = Math.Min(resourceAmount, space);
                targetResource.amount += pushed;

                float newAmount = resourceAmount - (float)pushed;
                if (newAmount <= 0.001f) { WriteResourceName(stored, ""); WriteResourceAmount(stored, 0f); }
                else WriteResourceAmount(stored, newAmount);

                shared.Log(pushed + " of " + resourceName + " pushed into " + target.partInfo.title, "KhemistryEVAFluidCellHandler/EVASendResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    string.Format("Transferred {0:F2} kg of {1}.", pushed, resourceName),
                    5.0f, ScreenMessageStyle.UPPER_CENTER));
            });
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from nearby part to cell",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVATakeResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVATakeResources!"); return; }

            var cells = GetHeldCellSnapshots();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage("No fluid cells in inventory.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
                ShowPartSelectorForTake(cells[0]);
            else
            {
                var labels = cells.Select((c, i) =>
                {
                    string resName = ReadResourceName(c);
                    float resAmount = ReadResourceAmount(c);
                    float maxAmount = ReadMaxAmount(c.partName);
                    return string.IsNullOrEmpty(resName)
                        ? string.Format("Cell {0}: Empty", i + 1)
                        : string.Format("Cell {0}: {1} {2:F1}/{3:F1} kg", i + 1, resName, resAmount, maxAmount);
                }).ToList();

                shared.ShowSelector("Which cell to fill?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForTake(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForTake(StoredPart stored)
        {
            var shared = KShared.Instance;
            string currentResource = ReadResourceName(stored);
            float currentAmount = ReadResourceAmount(stored);
            float maxAmount = ReadMaxAmount(stored.partName);
            float range = ReadTransferDistance(stored.partName);
            HashSet<string> allowed = ReadAllowedResources(stored.partName);

            if (currentAmount >= maxAmount)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage("That cell is full.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            float spaceRemaining = maxAmount - currentAmount;
            var optionParts = new Dictionary<string, Part>();
            var optionResources = new Dictionary<string, string>();

            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0) continue;
                    if (!string.IsNullOrEmpty(currentResource) && pr.resourceName != currentResource) continue;
                    if (string.IsNullOrEmpty(currentResource) && allowed.Count > 0 && !allowed.Contains(pr.resourceName)) continue;

                    string label = string.Format("{0} / {1}  ({2}: {3:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!optionParts.ContainsKey(label))
                    {
                        optionParts.Add(label, p);
                        optionResources.Add(label, pr.resourceName);
                    }
                }

            if (optionParts.Count == 0)
            {
                string msg = string.IsNullOrEmpty(currentResource)
                    ? "No allowed resources found within range."
                    : "No nearby parts have " + currentResource + ".";
                ScreenMessages.PostScreenMessage(new ScreenMessage(msg, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Take resources from...", optionParts.Keys.ToList(), label =>
            {
                Part source = optionParts[label];
                string resourceName = optionResources[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                if (sourceResource == null) return;

                double taken = Math.Min(sourceResource.amount, spaceRemaining);
                sourceResource.amount -= taken;
                WriteResourceName(stored, resourceName);
                WriteResourceAmount(stored, currentAmount + (float)taken);

                shared.Log(taken + " of " + resourceName + " taken from " + source.partInfo.title, "KhemistryEVAFluidCellHandler/EVATakeResources");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    string.Format("Received {0:F2} kg of {1}.", taken, resourceName),
                    5.0f, ScreenMessageStyle.UPPER_CENTER));
            });
        }
    }

    public class KhemistryDegradingBattery : PartModule
    {
        [KSPField(isPersistant = false)]
        public string ResourceName = "ElectricCharge";

        [KSPField(isPersistant = false)]
        public double DegradeTime = -1.0;

        [KSPField(isPersistant = true)]
        public double OriginalMaxAmount = -1.0;

        [KSPField(isPersistant = true)]
        public double StartTime = -1.0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
        public string HealthDisplay = "Battery Life: 100%";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Time Remaining",
         groupName = "batterydeg")]
        public string HealthTimeDisplay = "Time until 0% battery life: Battery cannot degrade.";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null)
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryDegradingBattery but no resource node for " + ResourceName,
                    "KhemistryDegradingBattery/OnStart");
                return;
            }

            if (OriginalMaxAmount < 0) OriginalMaxAmount = resource.maxAmount;
            if (StartTime < 0) StartTime = Planetarium.GetUniversalTime();

            ApplyDegradation(resource);
        }

        public override void OnUpdate()
        {
            PartResource resource = part.Resources.Get(ResourceName);
            if (resource == null) return;
            ApplyDegradation(resource);
        }

        private void ApplyDegradation(PartResource resource)
        {
            if (DegradeTime <= 0) return;

            double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
            double degradeSeconds = DegradeTime * 60.0;
            double fraction = Math.Max(0.0, 1.0 - (elapsedSeconds / degradeSeconds));
            double newMax = OriginalMaxAmount * fraction;

            resource.maxAmount = newMax;
            if (resource.amount > resource.maxAmount)
                resource.amount = resource.maxAmount;

            HealthDisplay = string.Format("Battery Life: {0:F1}%", fraction * 100.0);
            double remaining = Math.Max(0, degradeSeconds - elapsedSeconds);
            HealthTimeDisplay = string.Format("Time until 0%: {0:F0}s", remaining);
        }
    }

    public class KhemistryResourceInfo
    {
        public string name;
        public string displayName;
        public string abbreviation;
        public float unitCost;
        public float density;
        public float volume;
        public string flowMode;
        public string transfer;
        public bool isTweakable;
        public bool isVisible;
        public string description;
    }

    public class KhemistryRecipeIO
    {
        public string resourceName;
        public double ratio;
    }

    public class KhemistryRecipeInfo
    {
        public string converterName;
        public bool generatesHeat;
        public string partTitle;
        public List<KhemistryRecipeIO> inputs = new List<KhemistryRecipeIO>();
        public List<KhemistryRecipeIO> outputs = new List<KhemistryRecipeIO>();
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryLoader : MonoBehaviour
    {
        public static List<KhemistryResourceInfo> Resources { get; private set; }
        public static List<KhemistryRecipeInfo> Recipes { get; private set; }
        public static bool IsLoaded { get; private set; } = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);
            LoadData();
        }

        private void LoadData()
        {
            KShared.Instance?.Log("Loading resource and recipe library...", "KhemistryLibraryLoader/LoadData");

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                        double.TryParse(inputNode.GetValue("Ratio"), out double ratio);
                        recipe.inputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }
                    foreach (ConfigNode outputNode in moduleNode.GetNodes("OUTPUT_RESOURCE"))
                    {
                        string resName = outputNode.GetValue("ResourceName");
                        if (string.IsNullOrEmpty(resName)) continue;
                        double.TryParse(outputNode.GetValue("Ratio"), out double ratio);
                        recipe.outputs.Add(new KhemistryRecipeIO { resourceName = resName, ratio = ratio });
                    }

                    Recipes.Add(recipe);
                }
            }

            IsLoaded = true;
            KShared.Instance?.Log(
                string.Format("Library loaded: {0} resources, {1} recipes.", Resources.Count, Recipes.Count),
                "KhemistryLibraryLoader/LoadData");
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryGUI : MonoBehaviour
    {
        private const int MainWindowId = 856201;
        private const int DetailWindowId = 856202;
        private const int RecipeWindowId = 856203;

        private bool _mainVisible = false;
        private bool _detailVisible = false;
        private bool _recipeVisible = false;

        private Rect _mainRect;
        private Rect _detailRect;
        private Rect _recipeRect;

        private string _searchText = "";
        private Vector2 _mainScroll = Vector2.zero;

        private KhemistryResourceInfo _selectedResource;
        private Vector2 _detailScroll = Vector2.zero;

        private List<KhemistryRecipeInfo> _filteredRecipes;
        private string _recipeTitle = "";
        private Vector2 _recipeScroll = Vector2.zero;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _buttonTexture;

        private GUIStyle _wrapLabel;
        private GUIStyle _centeredLabel;
        private GUIStyle _boldLabel;
        private bool _stylesReady = false;

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);

            float sw = Screen.width;
            float sh = Screen.height;
            float detailW = sw / 3f;
            _mainRect = new Rect(sw * 0.05f, sh * 0.1f, 700f, 500f);
            _detailRect = new Rect(sw * 0.63f, sh * 0.1f, detailW, 560f);
            _recipeRect = new Rect(sw * 0.05f, sh * 0.1f, 900f, 500f);

            _buttonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color icon = new Color(0.25f, 0.60f, 0.90f, 1f);
            Color[] pixels = new Color[38 * 38];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = icon;
            _buttonTexture.SetPixels(pixels);
            _buttonTexture.Apply();

            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;
            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                () => _mainVisible = true,
                () => _mainVisible = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                _buttonTexture
            );
        }

        private void OnLauncherDestroyed() { _toolbarButton = null; }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _wrapLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true };
            _centeredLabel = new GUIStyle(HighLogic.Skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _boldLabel = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_mainVisible)
                _mainRect = GUILayout.Window(MainWindowId, _mainRect, DrawMainWindow, "Khemistry Resource Library", HighLogic.Skin.window);
            if (_detailVisible && _selectedResource != null)
                _detailRect = GUILayout.Window(DetailWindowId, _detailRect, DrawDetailWindow, "", HighLogic.Skin.window);
            if (_recipeVisible && _filteredRecipes != null)
                _recipeRect = GUILayout.Window(RecipeWindowId, _recipeRect, DrawRecipeWindow, _recipeTitle, HighLogic.Skin.window);
        }

        private void DrawMainWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _mainVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", HighLogic.Skin.label, GUILayout.Width(55));
            _searchText = GUILayout.TextField(_searchText, HighLogic.Skin.textField);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(230));
            GUILayout.Label("Abbreviation", _boldLabel, GUILayout.Width(120));
            GUILayout.Label("Cost per KG", _boldLabel, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _mainScroll = GUILayout.BeginScrollView(_mainScroll, HighLogic.Skin.scrollView);

            if (!KhemistryLibraryLoader.IsLoaded)
            {
                GUILayout.Label("Resources and recipes are still loading.", _wrapLabel);
            }
            else
            {
                string filter = _searchText.Trim().ToLower();
                foreach (KhemistryResourceInfo res in KhemistryLibraryLoader.Resources)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        !res.displayName.ToLower().Contains(filter) &&
                        !res.name.ToLower().Contains(filter))
                        continue;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(res.displayName, HighLogic.Skin.button, GUILayout.Width(230)))
                        OpenDetailWindow(res);
                    GUILayout.Label(res.abbreviation ?? "-", HighLogic.Skin.label, GUILayout.Width(120));
                    GUILayout.Label(res.unitCost.ToString("F2"), HighLogic.Skin.label, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void OpenDetailWindow(KhemistryResourceInfo res)
        {
            _selectedResource = res;
            _detailVisible = true;
            _detailScroll = Vector2.zero;
        }

        private void DrawDetailWindow(int id)
        {
            KhemistryResourceInfo res = _selectedResource;
            float labelW = Screen.width / 3f - 60f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(res.displayName, _boldLabel, GUILayout.Width(labelW - 35f));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
            {
                _detailVisible = false;
                _recipeVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string desc = string.IsNullOrEmpty(res.description) ? "No description available." : res.description;
            GUILayout.Label(desc, _centeredLabel, GUILayout.Width(labelW));

            GUILayout.Space(8);

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, HighLogic.Skin.scrollView);

            DrawRow("Internal Name", res.name);
            DrawRow("Abbreviation", res.abbreviation ?? "-");
            DrawRow("Cost per KG", res.unitCost.ToString("F4"));
            DrawRow("Can be adjusted in VAB?", res.isTweakable ? "Yes" : "No");
            DrawRow("Hidden resource?", res.isVisible ? "No" : "Yes");
            DrawRow("Flow mode", res.flowMode ?? "-");
            DrawRow("Transfer method", res.transfer ?? "-");

            GUILayout.Space(6);

            string densityLine;
            if (Approx(res.density, 0.001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 kilogram";
            else if (Approx(res.density, 1f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 ton";
            else if (Approx(res.density, 0.000001f) && Approx(res.volume, 1f)) densityLine = "1 unit = 1 gram";
            else densityLine = string.Format(
                    "This resource has special density and volume parameters. " +
                    "Every unit of this resource weighs {0:F6} kilograms and each internal " +
                    "volume unit is filled by {1} of this resource.",
                    res.density * 1000.0, res.volume);

            GUILayout.Label(densityLine, _wrapLabel, GUILayout.Width(labelW));
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            if (GUILayout.Button("Recipes that use this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: true);
            if (GUILayout.Button("Recipes that produce this resource", HighLogic.Skin.button))
                OpenRecipeWindow(res.name, isInput: false);

            GUI.DragWindow();
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _boldLabel, GUILayout.Width(180));
            GUILayout.Label(value, _wrapLabel);
            GUILayout.EndHorizontal();
        }

        private static bool Approx(float a, float b)
            => Math.Abs(a - b) < Math.Abs(b) * 0.01f + 1e-9f;

        private void OpenRecipeWindow(string resourceName, bool isInput)
        {
            _filteredRecipes = KhemistryLibraryLoader.Recipes.Where(r =>
                isInput
                    ? r.inputs.Any(i => i.resourceName == resourceName)
                    : r.outputs.Any(o => o.resourceName == resourceName)
            ).ToList();

            _recipeTitle = isInput
                ? "Recipes that use " + resourceName
                : "Recipes that produce " + resourceName;
            _recipeScroll = Vector2.zero;
            _recipeVisible = true;
        }

        private void DrawRecipeWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _recipeVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _boldLabel, GUILayout.Width(200));
            GUILayout.Label("Produces heat?", _boldLabel, GUILayout.Width(100));
            GUILayout.Label("Inputs", _boldLabel, GUILayout.Width(270));
            GUILayout.Label("Outputs", _boldLabel, GUILayout.Width(270));
            GUILayout.EndHorizontal();

            _recipeScroll = GUILayout.BeginScrollView(_recipeScroll, HighLogic.Skin.scrollView);

            if (_filteredRecipes == null || _filteredRecipes.Count == 0)
            {
                GUILayout.Label("No recipes found.", _wrapLabel);
            }
            else
            {
                foreach (KhemistryRecipeInfo recipe in _filteredRecipes)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical(GUILayout.Width(200));
                    GUILayout.Label(recipe.converterName, _boldLabel);
                    GUILayout.Label("(" + recipe.partTitle + ")", _wrapLabel);
                    GUILayout.EndVertical();

                    GUILayout.Label(recipe.generatesHeat ? "Yes" : "No", HighLogic.Skin.label, GUILayout.Width(100));

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.inputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO input in recipe.inputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", input.ratio, input.resourceName);
                            KhemistryResourceInfo inputRes = FindResource(input.resourceName);
                            if (inputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(inputRes); }
                            else GUILayout.Label(btnLabel, _wrapLabel);
                        }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.outputs.Count == 0) GUILayout.Label("-", _wrapLabel);
                    else foreach (KhemistryRecipeIO output in recipe.outputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", output.ratio, output.resourceName);
                            KhemistryResourceInfo outputRes = FindResource(output.resourceName);
                            if (outputRes != null) { if (GUILayout.Button(btnLabel, HighLogic.Skin.button)) OpenDetailWindow(outputRes); }
                            else GUILayout.Label(btnLabel, _wrapLabel);
                        }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private KhemistryResourceInfo FindResource(string name)
        {
            if (!KhemistryLibraryLoader.IsLoaded) return null;
            return KhemistryLibraryLoader.Resources.FirstOrDefault(r => r.name == name);
        }
    }

    /* Example config node
MODULE
{
	name = KhemistryAdvancedStorage
    storageType = multiShared  // Can be type or storageType. "single" stores one resource, "multi" stores multiple and can be configured, "multiShared" stores multiple all at the same time
	maximumResources = 1000.0  // Maximum resources it can hold in total. This will be shared for multiShare types but per-resource for others.
	chargingRequired = true    // Does the container need to be charged to be used
	passiveConsumption = true  // Does the container have a passive consumption
	maxInputRate = 10.0        // Maximum transfer rate to the container. Do not include if you want it to be unlimited
	maxOutputRate = 10.0       // Maximum transfer rate from the container. Do not include if you want it to be unlimited
	chargeRate = 50.0          // Percent per second to fill charge (50 = 2 seconds to full). Not required if charging is disabled
	chargeDecayRate = 5.0      // Percent per second to lose charge when storage can no longer charge. Not required if charging is disabled
    filledUnpoweredResult = boiloff,1         // What will happen if the storage is not on but has a resource. Possible options are listed below
    passiveUnsatisfiedResult = destroy,500    // What will happen if the storage cannot consume resources as part of passive consumption. Possible options are listed below
                                              // off = The container will turn off
                                              // void = All resources will be voided
                                              // destroy,50 = The part will blow up with the specified power
                                              // boiloff,1 = All resources will slowly (or not) disappear at the specified amount per second.
                                              // Note that boiloff can only be applied to filledUnpoweredResult because passiveUnsatisfiedResult is only checked once and the container turns off.
                                              // Also the fields can have double quotes (") around them but that is not recommended to do.

	SUPPORTED_RESOURCES        // Resource the container supports and can hold at the same time.
	{                          // More than one entry with single type will error out and remove all after the first one.
		name = LiquidFuel      // If it isn't present, the part will error out and the storage will not show up.
		name = Oxidizer
		name = MonoPropellant
	}

	PASSIVE_CON_NAMES          // Resources used for passive consumption. Not required if passive consumption is disabled
	{
		name = ElectricCharge
	}
	PASSIVE_CON_AMOUNTS        // Amount of each resource used for passive consumption (per second). Not required if passive consumption is disabled
	{
		amount = 0.5
	}

	CHARGE_CON_NAMES           // Resources used for charge consumption. Not required if charging is disabled
	{
		name = ElectricCharge
	}
	CHARGE_CON_AMOUNTS         // Amount of each resource used for charge consumption (per second). Not required if charging is disabled
	{
		amount = 5.0
	}
}
    */
    public class KhemistryAdvancedStorage : PartModule
    {
        // ── Config fields (KSPFields) ────────────────────────────────────────────────

        [KSPField(isPersistant = false)]
        public string storageType = "single";   // "single", "multi", "multiShared"

        [KSPField(isPersistant = false)]
        public float maximumResources = 1000f;

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public bool passiveConsumption = false;

        [KSPField(isPersistant = false)]
        public float maxInputRate = -1f;        // units per second, -1 = unlimited (not enforced yet)

        [KSPField(isPersistant = false)]
        public float maxOutputRate = -1f;       // units per second, -1 = unlimited (not enforced yet)

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;           // percent per second

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;      // percent per second

        // ── Persistent runtime state ────────────────────────────────────────────────

        public enum StorageState { Off, Charging, On }

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public StorageState state = StorageState.Off;

        [KSPField(isPersistant = true)]
        public string activeResource = "";

        // ── Display fields ─────────────────────────────────────────────────────────

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryadvstorage",
                  groupDisplayName = "Khemistry Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistryadvstorage")]
        public string volumeDisplay = "0 / 0";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryadvstorage")]
        public string chargeDisplay = "Charge: N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryadvstorage")]
        public string stateDisplay = "State: Off";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Active Resource", groupName = "khemistryadvstorage")]
        public string activeResourceDisplay = "Active: (none)";

        // ── Internal config data ───────────────────────────────────────────────────

        private readonly List<string> _supportedResources = new List<string>();

        private readonly List<string> _passiveNames = new List<string>();
        private readonly List<float> _passiveAmounts = new List<float>();

        private readonly List<string> _chargeNames = new List<string>();
        private readonly List<float> _chargeAmounts = new List<float>();

        // Parsed consequence configs
        private enum ConsequenceType { Off, Void, Destroy, Boiloff }

        private struct ConsequenceConfig
        {
            public ConsequenceType type;
            public float value;   // explosion power for Destroy; rate/sec for Boiloff
        }

        private ConsequenceConfig _passiveUnsatisfiedResult;  // fires once per unsatisfied run
        private ConsequenceConfig _filledUnpoweredResult;     // fires every 0.1 s when unpowered+filled

        // For blocking transfers while not On
        private readonly Dictionary<string, double> _frozenAmounts = new Dictionary<string, double>();

        // Consequence state
        private bool _passiveUnsatisfiedFired = false;   // re-arms when consumption succeeds
        private double _filledUnpoweredAccum = 0.0;     // time accumulator for 0.1 s ticks

        private bool _fatalConfigError = false;

        // ── UI Events ──────────────────────────────────────────────────────────────

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryadvstorage")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == StorageState.On) return;
            state = StorageState.Charging;
            KShared.Instance?.Log("Charging enabled.", "KhemistryAdvancedStorage/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryadvstorage", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != StorageState.Charging) return;
            state = StorageState.Off;
            KShared.Instance?.Log("Charging disabled.", "KhemistryAdvancedStorage/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn on container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOnContainer()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Container must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = StorageState.On;
            _passiveUnsatisfiedFired = false;
            KShared.Instance?.Log("Container turned ON.", "KhemistryAdvancedStorage/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOffContainer()
        {
            state = StorageState.Off;
            KShared.Instance?.Log("Container turned OFF.", "KhemistryAdvancedStorage/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Select resource",
                  groupName = "khemistryadvstorage", active = false)]
        public void SelectResource()
        {
            if (storageType != "multi") return;

            if (!string.IsNullOrEmpty(activeResource))
            {
                var def = PartResourceLibrary.Instance.GetDefinition(activeResource);
                if (def != null)
                {
                    PartResource pr = part.Resources.Get(def.id);
                    if (pr != null && pr.amount >= 1.0)
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Container must be nearly empty to switch resource.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
                    if (pr != null) pr.amount = 0.0;
                }
            }

            if (_supportedResources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No supported resources configured.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "KShared not available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Select active resource", new List<string>(_supportedResources), label =>
            {
                activeResource = label;
                KShared.Instance?.Log("Active resource set to " + activeResource,
                    "KhemistryAdvancedStorage/SelectResource");
                ZeroNonActiveResources();
            });
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                foreach (BaseEvent e in Events) e.active = false;
                contentsDisplay = "ERROR: see log";
                return;
            }

            EnsureResourcesExistOnPart();
            SnapshotFrozenAmounts();
            _passiveUnsatisfiedFired = false;
            _filledUnpoweredAccum = 0.0;
            UpdateUI();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            double dt = TimeWarp.fixedDeltaTime;

            HandleCharging(dt);
            HandlePassiveConsumption(dt);
            HandleTransferBlocking();
            HandleFilledUnpowered(dt);
            EnforceCapacity();
            UpdateUI();
        }

        public override void OnUpdate()
        {
            if (_fatalConfigError) return;
            UpdateUI();
        }

        // ── Config loading ─────────────────────────────────────────────────────────

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.Instance?.LogError("partInfo.partConfig is null!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = null;
            foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (n.GetValue("name") == "KhemistryAdvancedStorage") { moduleNode = n; break; }
            }

            if (moduleNode == null)
            {
                KShared.Instance?.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            // SUPPORTED_RESOURCES — required
            _supportedResources.Clear();
            if (!moduleNode.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage but no SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());
            if (_supportedResources.Count == 0)
            {
                KShared.Instance?.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage with an empty SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            // Scalar fields
            storageType = moduleNode.GetValue("storageType") ?? moduleNode.GetValue("type") ?? "single";

            float tmp;
            if (float.TryParse(moduleNode.GetValue("maximumResources"), out tmp)) maximumResources = tmp;
            maxInputRate = float.TryParse(moduleNode.GetValue("maxInputRate"), out tmp) ? tmp : -1f;
            maxOutputRate = float.TryParse(moduleNode.GetValue("maxOutputRate"), out tmp) ? tmp : -1f;
            if (float.TryParse(moduleNode.GetValue("chargeRate"), out tmp)) chargeRate = tmp;
            if (float.TryParse(moduleNode.GetValue("chargeDecayRate"), out tmp)) chargeDecayRate = tmp;

            bool tmpB;
            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out tmpB)) chargingRequired = tmpB;
            if (bool.TryParse(moduleNode.GetValue("passiveConsumption"), out tmpB)) passiveConsumption = tmpB;

            // Consequence configs
            _passiveUnsatisfiedResult = ParseConsequence(
                moduleNode.GetValue("passiveUnsatisfiedResult"), allowBoiloff: false,
                "passiveUnsatisfiedResult", "off");

            _filledUnpoweredResult = ParseConsequence(
                moduleNode.GetValue("filledUnpoweredResult"), allowBoiloff: true,
                "filledUnpoweredResult", "off");

            // PASSIVE_CON_NAMES / PASSIVE_CON_AMOUNTS
            _passiveNames.Clear();
            _passiveAmounts.Clear();
            if (passiveConsumption)
            {
                if (moduleNode.HasNode("PASSIVE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("PASSIVE_CON_NAMES").GetValues("name"))
                        _passiveNames.Add(n.Trim());
                if (moduleNode.HasNode("PASSIVE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("PASSIVE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _passiveAmounts.Add(tmp); }
                if (_passiveNames.Count != _passiveAmounts.Count)
                    KShared.Instance?.LogError("PASSIVE_CON_NAMES and PASSIVE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            // CHARGE_CON_NAMES / CHARGE_CON_AMOUNTS
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                if (moduleNode.HasNode("CHARGE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("CHARGE_CON_NAMES").GetValues("name"))
                        _chargeNames.Add(n.Trim());
                if (moduleNode.HasNode("CHARGE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("CHARGE_CON_AMOUNTS").GetValues("amount"))
                    { if (float.TryParse(a, out tmp)) _chargeAmounts.Add(tmp); }
                if (_chargeNames.Count != _chargeAmounts.Count)
                    KShared.Instance?.LogError("CHARGE_CON_NAMES and CHARGE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            // Active resource defaults
            if ((storageType == "single" || storageType == "multi") && string.IsNullOrEmpty(activeResource))
                if (_supportedResources.Count > 0) activeResource = _supportedResources[0];

            if (storageType == "single" && _supportedResources.Count > 1)
            {
                KShared.Instance?.LogError(
                    "storageType=single but multiple SUPPORTED_RESOURCES defined; only first will be used.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                string keep = _supportedResources[0];
                _supportedResources.Clear();
                _supportedResources.Add(keep);
                activeResource = keep;
            }

            KShared.Instance?.Log(
                string.Format("Config loaded. storageType={0}, max={1}, chargingRequired={2}, passiveConsumption={3}, passiveUnsatisfiedResult={4}, filledUnpoweredResult={5}",
                    storageType, maximumResources, chargingRequired, passiveConsumption,
                    _passiveUnsatisfiedResult.type, _filledUnpoweredResult.type),
                "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
        }

        /// <summary>
        /// Parses "off", "void", "destroy,10", "boiloff,1.5" into a ConsequenceConfig.
        /// Falls back to the specified default if the raw value is null/invalid.
        /// </summary>
        private ConsequenceConfig ParseConsequence(string raw, bool allowBoiloff, string fieldName, string fallback)
        {
            // KSP config values may include surrounding quotes when the cfg uses = "value" syntax; strip them.
            string src = string.IsNullOrEmpty(raw) ? fallback : raw.Trim().Trim('"').Trim().ToLower();

            if (src == "off") return new ConsequenceConfig { type = ConsequenceType.Off };
            if (src == "void") return new ConsequenceConfig { type = ConsequenceType.Void };

            if (src.StartsWith("destroy,"))
            {
                float v;
                if (float.TryParse(src.Substring(8), out v))
                    return new ConsequenceConfig { type = ConsequenceType.Destroy, value = v };
                KShared.Instance?.LogError("Could not parse destroy power in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            if (allowBoiloff && src.StartsWith("boiloff,"))
            {
                float v;
                if (float.TryParse(src.Substring(8), out v))
                    return new ConsequenceConfig { type = ConsequenceType.Boiloff, value = v };
                KShared.Instance?.LogError("Could not parse boiloff rate in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            KShared.Instance?.LogError("Unknown consequence value " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                "KhemistryAdvancedStorage/ParseConsequence");
            return new ConsequenceConfig { type = ConsequenceType.Off };
        }

        // ── Resource setup ──────────────────────────────────────────────────────────

        private void EnsureResourcesExistOnPart()
        {
            foreach (string resName in _supportedResources)
            {
                var def = PartResourceLibrary.Instance.GetDefinition(resName);
                if (def == null)
                {
                    KShared.Instance?.LogError("Unknown resource \"" + resName + "\" in SUPPORTED_RESOURCES.",
                        "KhemistryAdvancedStorage/EnsureResourcesExistOnPart");
                    continue;
                }

                PartResource existing = part.Resources.Get(def.id);
                if (existing == null)
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("name", resName);
                    node.AddValue("amount", 0.0);
                    node.AddValue("maxAmount", maximumResources);
                    part.AddResource(node);
                }
                else
                {
                    existing.maxAmount = maximumResources;
                    if (existing.amount < 0.0) existing.amount = 0.0;
                }
            }

            if (storageType == "multi")
                ZeroNonActiveResources();
        }

        private void ZeroNonActiveResources()
        {
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (!string.IsNullOrEmpty(activeResource) && pr.resourceName != activeResource)
                    pr.amount = 0.0;
            }
        }

        // ── Charging logic ─────────────────────────────────────────────────────────

        private void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == StorageState.Off)
            {
                // Container is off — charge decays passively
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != StorageState.Charging) return;

            // Already full — flip to On
            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = StorageState.On;
                KShared.Instance?.Log("Container fully charged, now ON.",
                    "KhemistryAdvancedStorage/HandleCharging");
                return;
            }

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f) chargePercent = 100f;
            }
            else
            {
                // Can't get charge resources — decay if configured
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        // ── Passive consumption ────────────────────────────────────────────────────

        private void HandlePassiveConsumption(double dt)
        {
            if (!passiveConsumption) return;

            // Only attempt to draw resources when the container is actually running.
            // We still check the consequence even after an "off" result has dropped state
            // out of On, so consumption and consequence are handled separately.
            if (state == StorageState.On)
            {
                bool satisfied = ConsumeVesselResources(_passiveNames, _passiveAmounts, dt);
                if (satisfied)
                {
                    // Re-arm so a future failure can trigger the consequence again.
                    _passiveUnsatisfiedFired = false;
                    return;
                }
            }

            // Not On, or consumption failed. Only apply consequence if the container
            // actually has something stored — empty containers don't need protecting.
            if (!HasAnyStoredResources()) return;

            if (!_passiveUnsatisfiedFired)
            {
                _passiveUnsatisfiedFired = true;
                ApplyConsequence(_passiveUnsatisfiedResult, "passiveUnsatisfiedResult");
            }
        }

        // ── Filled-unpowered consequence ───────────────────────────────────────────

        private void HandleFilledUnpowered(double dt)
        {
            // "Unpowered" means the container is not On
            if (state == StorageState.On) return;

            // Only applies when there is actually something stored
            if (!HasAnyStoredResources()) return;

            _filledUnpoweredAccum += dt;

            // Fire every 0.1 seconds
            while (_filledUnpoweredAccum >= 0.1)
            {
                _filledUnpoweredAccum -= 0.1;
                ApplyConsequence(_filledUnpoweredResult, "filledUnpoweredResult", tickDt: 0.1);
            }
        }

        // ── Consequence execution ──────────────────────────────────────────────────

        /// <summary>
        /// Executes a consequence. tickDt is only used by Boiloff (the value in config is per second;
        /// we receive a 0.1 s tick so we apply value * 0.1 per call).
        /// </summary>
        private void ApplyConsequence(ConsequenceConfig cfg, string source, double tickDt = 0.0)
        {
            switch (cfg.type)
            {
                case ConsequenceType.Off:
                    break;  // Do nothing

                case ConsequenceType.Void:
                    KShared.Instance?.Log("Voiding all stored resources (" + source + ").",
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    foreach (PartResource pr in part.Resources)
                    {
                        if (_supportedResources.Contains(pr.resourceName))
                            pr.amount = 0.0;
                    }
                    break;

                case ConsequenceType.Destroy:
                    KShared.Instance?.Log(
                        string.Format("Destroying part with power {0:F1} ({1}).", cfg.value, source),
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    part.explode();
                    break;

                case ConsequenceType.Boiloff:
                    // cfg.value is loss per second; tickDt is 0.1 s, so loss per tick = cfg.value * tickDt
                    ApplyBoiloff(cfg.value * (float)tickDt, source);
                    break;
            }
        }

        /// <summary>
        /// Reduces stored resources by a flat amount per tick, distributed proportionally
        /// across all resources that currently have any amount. Works correctly for all
        /// storage types:
        ///   single / multi   — only the active resource has any amount, so it drains alone.
        ///   multiShared      — all resources drain proportionally to their current fill.
        /// </summary>
        private void ApplyBoiloff(float amountPerTick, string source)
        {
            // Collect resources that currently hold something
            var filled = new List<PartResource>();
            double total = 0.0;
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0) { filled.Add(pr); total += pr.amount; }
            }
            if (filled.Count == 0 || total <= 0.0) return;

            double toDrain = Math.Min(amountPerTick, total);

            foreach (PartResource pr in filled)
            {
                double share = (pr.amount / total) * toDrain;
                pr.amount = Math.Max(0.0, pr.amount - share);
                // Update the freeze snapshot so HandleTransferBlocking doesn't revert the drain next tick.
                _frozenAmounts[pr.resourceName] = pr.amount;
            }

            KShared.Instance?.Log(
                string.Format("Boiloff: drained {0:F4} units ({1}).", toDrain, source),
                "KhemistryAdvancedStorage/ApplyBoiloff");
        }

        // ── Vessel resource consumption ────────────────────────────────────────────

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.Instance?.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                // Allow a small tolerance (0.1% of needed)
                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                // Refund everything that was pulled
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private bool HasAnyStoredResources()
        {
            foreach (PartResource pr in part.Resources)
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 0.0)
                    return true;
            return false;
        }

        // ── Transfer blocking while not On ────────────────────────────────────────

        private void SnapshotFrozenAmounts()
        {
            _frozenAmounts.Clear();
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                _frozenAmounts[pr.resourceName] = pr.amount;
            }
        }

        private void HandleTransferBlocking()
        {
            bool shouldFreeze =
                (chargingRequired && state != StorageState.On) ||
                (!chargingRequired && state == StorageState.Off);

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;

                if (!shouldFreeze)
                {
                    _frozenAmounts[pr.resourceName] = pr.amount;
                }
                else
                {
                    double frozen;
                    if (_frozenAmounts.TryGetValue(pr.resourceName, out frozen))
                        pr.amount = frozen;
                    else
                        _frozenAmounts[pr.resourceName] = pr.amount;
                }
            }
        }

        // ── Capacity enforcement ───────────────────────────────────────────────────

        private void EnforceCapacity()
        {
            // Clamp negatives first
            foreach (PartResource pr in part.Resources)
                if (pr.amount < 0.0) pr.amount = 0.0;

            if (storageType == "multiShared")
            {
                double total = 0.0;
                var list = new List<PartResource>();
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    list.Add(pr);
                    total += pr.amount;
                }

                if (total > maximumResources && total > 0.0)
                {
                    double scale = maximumResources / total;
                    foreach (PartResource pr in list) pr.amount *= scale;
                }

                foreach (PartResource pr in list) pr.maxAmount = maximumResources;
            }
            else
            {
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    pr.amount = Math.Min(pr.amount, maximumResources);
                    pr.amount = Math.Max(pr.amount, 0.0);
                    pr.maxAmount = maximumResources;
                }

                if (storageType == "multi")
                    ZeroNonActiveResources();
            }
        }

        // ── UI updates ─────────────────────────────────────────────────────────────

        private void UpdateUI()
        {
            double total = 0.0;
            var parts = new List<string>();

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (pr.amount > 0.0)
                {
                    parts.Add(string.Format("{0}: {1:F2}", pr.resourceName, pr.amount));
                    total += pr.amount;
                }
            }

            contentsDisplay = parts.Count == 0 ? "Empty" : string.Join(", ", parts.ToArray());
            volumeDisplay = string.Format("{0:F2} / {1:F2}", total, maximumResources);

            chargeDisplay = chargingRequired
                ? string.Format("Charge: {0:F1}%", chargePercent)
                : "Charge: N/A";

            stateDisplay = "State: " + state.ToString();

            activeResourceDisplay = (storageType == "single" || storageType == "multi")
                ? (string.IsNullOrEmpty(activeResource) ? "Active: (none)" : "Active: " + activeResource)
                : "Active: (multiShared)";

            Events["EnableCharging"].active = chargingRequired && state != StorageState.Charging && state != StorageState.On;
            Events["DisableCharging"].active = chargingRequired && state == StorageState.Charging;
            Events["TurnOnContainer"].active = state != StorageState.On;
            Events["TurnOffContainer"].active = state == StorageState.On;
            Events["SelectResource"].active = storageType == "multi";
        }
    }
}