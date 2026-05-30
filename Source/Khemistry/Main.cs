using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    // Single shared singleton: handles logging and the selector GUI window.
    // KSPAddon with once=true means KSP creates this once at boot and
    // DontDestroyOnLoad keeps it alive across all scene transitions.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KShared : MonoBehaviour
    {
        private static KShared _instance;
        public static KShared Instance => _instance;

        // Selector window state
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

        // Opens the selector window centered on screen.
        // title    - window title bar text
        // options  - list of strings shown as buttons
        // onSelect - called with the chosen string when the player picks one
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
        // Maximum amount of a resource the fluid cell can hold, in kilograms
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        // How far, in meters, the fluid cell can reach when transferring
        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        // Persistent storage for whatever is currently held in the cell.
        // ResourceName == "" means the cell is empty.
        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        // Resources this cell is allowed to hold, loaded from ALLOWED_RESOURCES
        // in the part config. Not a KSPField since it comes from the part definition.
        public HashSet<string> AllowedResources = new HashSet<string>();

        // Read-only display shown in the part GUI
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

    // Patched onto kerbalEVA and kerbalEVAfemale.
    // Reads and writes fluid cell state directly from the inventory snapshot
    // so it works correctly with stored (not instantiated) inventory parts.
    public class KhemistryEVAFluidCellHandler : PartModule
    {
        private static readonly HashSet<string> FluidCellPartNames = new HashSet<string>
        {
            "NameConverterRadial"
        };

        private ModuleInventoryPart _inventory;

        // Display field showing contents of all held cells, updated every frame
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells in inventory";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // If another instance of this handler is already on the part (caused by KSP
            // loading both kerbalEVA and kerbalEVAfemale modules onto the same kerbal),
            // remove this one so only a single handler is ever active.
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
            // Build a summary string: one entry per cell
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

        // Returns StoredParts for all fluid cell items currently in the inventory
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

        // Reads the KhemistryFluidCell module snapshot from a StoredPart
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
        {
            return GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceName") ?? "";
        }

        private float ReadResourceAmount(StoredPart stored)
        {
            string val = GetCellModuleSnapshot(stored)?.moduleValues.GetValue("ResourceAmount");
            return val != null ? float.Parse(val) : 0f;
        }

        // Reads non-persistent fields from the part prefab since they aren't in the snapshot
        private float ReadMaxAmount(string partName)
        {
            return PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.ResourceMaxAmount ?? 100f;
        }

        private float ReadTransferDistance(string partName)
        {
            return PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.TransferDistance ?? 10f;
        }

        private HashSet<string> ReadAllowedResources(string partName)
        {
            return PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.AllowedResources
                ?? new HashSet<string>();
        }

        private void WriteResourceName(StoredPart stored, string name)
        {
            GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceName", name);
        }

        private void WriteResourceAmount(StoredPart stored, float amount)
        {
            GetCellModuleSnapshot(stored)?.moduleValues.SetValue("ResourceAmount", amount.ToString("F4"));
        }

        // Returns all parts within range of the Kerbal across all loaded vessels,
        // excluding the Kerbal's own part
        private List<Part> GetPartsInRange(float range)
        {
            var result = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                foreach (Part p in v.parts)
                {
                    if (p == this.part) continue;
                    if (Vector3.Distance(this.part.transform.position, p.transform.position) <= range)
                        result.Add(p);
                }
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
            {
                ShowPartSelectorForSend(cells[0]);
            }
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
            {
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
                if (newAmount <= 0.001f)
                {
                    WriteResourceName(stored, "");
                    WriteResourceAmount(stored, 0f);
                }
                else
                {
                    WriteResourceAmount(stored, newAmount);
                }

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
            {
                ShowPartSelectorForTake(cells[0]);
            }
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
            {
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0) continue;
                    // If cell already holds something, only accept more of the same resource.
                    // Otherwise filter by the allowed resources list from the config.
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
    // Degrades the maximum capacity of a resource on this part linearly over time.
    // The resource node must already exist on the part in the config — this module
    // only overrides maxAmount at runtime. Timewarp-safe: uses universal time to
    // calculate elapsed time so degradation continues correctly at any warp rate.
    //
    // Sample MODULE node:
    // MODULE
    // {
    //     name = KhemistryDegradingBattery
    //     ResourceName = ElectricCharge
    //     DegradeTime = 60.0
    // }
    public class KhemistryDegradingBattery : PartModule
    {
        // The resource whose maxAmount will degrade over time
        [KSPField(isPersistant = false)]
        public string ResourceName = "ElectricCharge";

        // How long, in minutes, until maxAmount reaches zero
        [KSPField(isPersistant = false)]
        public double DegradeTime = -1.0;

        // The original maxAmount read from the resource node on first load.
        // Persisted so the linear calculation stays correct after save/load.
        [KSPField(isPersistant = true)]
        public double OriginalMaxAmount = -1.0;

        // Universal time (seconds) when this part first came to life.
        // Persisted so elapsed time survives save/load and scene changes.
        [KSPField(isPersistant = true)]
        public double StartTime = -1.0;

        // Read-only display showing current capacity percentage and time until 0%
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
        public string HealthDisplay = "Battery Life: 100%";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Battery Health",
         groupName = "batterydeg", groupDisplayName = "Battery Health", groupStartCollapsed = false)]
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

            // First time this part has ever been started — record the baseline values
            if (OriginalMaxAmount < 0)
                OriginalMaxAmount = resource.maxAmount;
            if (StartTime < 0)
                StartTime = Planetarium.GetUniversalTime();

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
            if (DegradeTime > 0)
            {
                double elapsedSeconds = Planetarium.GetUniversalTime() - StartTime;
                double degradeSeconds = DegradeTime * 60.0;

                // Linear interpolation: capacity goes from OriginalMaxAmount to 0
                // over degradeSeconds. Clamped so it never goes below 0.
                double fraction = Math.Max(0.0, 1.0 - (elapsedSeconds / degradeSeconds));
                double newMax = OriginalMaxAmount * fraction;

                resource.maxAmount = newMax;

                // Clamp current amount so it never exceeds the shrinking max
                if (resource.amount > resource.maxAmount)
                    resource.amount = resource.maxAmount;

                HealthDisplay = "Battery Life: " + (fraction * 100.0).ToString() + "%";
                HealthTimeDisplay = "Time until 0 % battery life: " + (DegradeTime - elapsedSeconds).ToString() + " seconds";
            }
        }
    }


    // ── Data classes ──────────────────────────────────────────────────────────

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

    // ── Data Loader ───────────────────────────────────────────────────────────
    // Runs once at the main menu, by which point ModuleManager has finished
    // patching and both GameDatabase and PartResourceLibrary are fully populated.

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

            // Build a description lookup from every RESOURCE_DEFINITION node in GameDatabase.
            // PartResourceDefinition doesn't parse custom values like khemistryDescription,
            // so we read them directly from the raw config nodes.
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string resName = node.GetValue("name");
                string desc = node.GetValue("khemistryDescription");
                if (!string.IsNullOrEmpty(resName) && !string.IsNullOrEmpty(desc))
                    descriptions[resName] = desc;
            }

            // Load every resource from PartResourceLibrary and sort alphanumerically by displayName
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

            // Scan every PART node in GameDatabase for ModuleResourceConverter modules.
            // GameDatabase gives us the post-MM-patched config, so we see all patched recipes.
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

    // ── GUI ───────────────────────────────────────────────────────────────────
    // Three independent draggable windows:
    //   Main   — searchable resource list, opened by the toolbar button
    //   Detail — per-resource details, opens when a resource is clicked
    //   Recipe — filtered recipe list, opens from the detail window buttons

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KhemistryLibraryGUI : MonoBehaviour
    {
        // Use plain integers to avoid calling GUIUtility.GetControlID outside OnGUI
        private const int MainWindowId = 856201;
        private const int DetailWindowId = 856202;
        private const int RecipeWindowId = 856203;

        // Visibility flags
        private bool _mainVisible = false;
        private bool _detailVisible = false;
        private bool _recipeVisible = false;

        // Window rects — initialized in Awake once screen dimensions are known
        private Rect _mainRect;
        private Rect _detailRect;
        private Rect _recipeRect;

        // Main window state
        private string _searchText = "";
        private Vector2 _mainScroll = Vector2.zero;

        // Detail window state
        private KhemistryResourceInfo _selectedResource;
        private Vector2 _detailScroll = Vector2.zero;

        // Recipe window state
        private List<KhemistryRecipeInfo> _filteredRecipes;
        private string _recipeTitle = "";
        private Vector2 _recipeScroll = Vector2.zero;

        // Toolbar button
        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _buttonTexture;

        // GUIStyles — created inside OnGUI on first call so HighLogic.Skin is ready
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

            // Create a simple flat blue toolbar icon without needing an external texture file
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

        private void OnLauncherDestroyed()
        {
            _toolbarButton = null;
        }

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

        // ── Main Window ───────────────────────────────────────────────────────

        private void DrawMainWindow(int id)
        {
            // X button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _mainVisible = false;
            GUILayout.EndHorizontal();

            // Full-width search bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", HighLogic.Skin.label, GUILayout.Width(55));
            _searchText = GUILayout.TextField(_searchText, HighLogic.Skin.textField);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Column headers
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

        // ── Detail Window ─────────────────────────────────────────────────────

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

            // Title row: wrapping displayName + X button
            GUILayout.BeginHorizontal();
            GUILayout.Label(res.displayName, _boldLabel, GUILayout.Width(labelW - 35f));
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
            {
                _detailVisible = false;
                _recipeVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Centered description
            string desc = string.IsNullOrEmpty(res.description)
                ? "No description available."
                : res.description;
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

            // Density / volume description
            string densityLine;
            if (Approx(res.density, 0.001f) && Approx(res.volume, 1f))
                densityLine = "1 unit = 1 kilogram";
            else if (Approx(res.density, 1f) && Approx(res.volume, 1f))
                densityLine = "1 unit = 1 ton";
            else if (Approx(res.density, 0.000001f) && Approx(res.volume, 1f))
                densityLine = "1 unit = 1 gram";
            else
                densityLine = string.Format(
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

        // Approximate float equality with a 1% relative tolerance plus epsilon
        private static bool Approx(float a, float b)
            => Math.Abs(a - b) < Math.Abs(b) * 0.01f + 1e-9f;

        // ── Recipe Window ─────────────────────────────────────────────────────

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
            // X button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", HighLogic.Skin.button, GUILayout.Width(28)))
                _recipeVisible = false;
            GUILayout.EndHorizontal();

            // Column headers
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

                    // Name + part title stacked vertically in one column
                    GUILayout.BeginVertical(GUILayout.Width(200));
                    GUILayout.Label(recipe.converterName, _boldLabel);
                    GUILayout.Label("(" + recipe.partTitle + ")", _wrapLabel);
                    GUILayout.EndVertical();

                    // Generates heat
                    GUILayout.Label(recipe.generatesHeat ? "Yes" : "No",
                        HighLogic.Skin.label, GUILayout.Width(100));

                    // Inputs — each on its own line, each a clickable button
                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.inputs.Count == 0)
                    {
                        GUILayout.Label("-", _wrapLabel);
                    }
                    else
                    {
                        foreach (KhemistryRecipeIO input in recipe.inputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", input.ratio, input.resourceName);
                            KhemistryResourceInfo inputRes = FindResource(input.resourceName);
                            if (inputRes != null)
                            {
                                if (GUILayout.Button(btnLabel, HighLogic.Skin.button))
                                    OpenDetailWindow(inputRes);
                            }
                            else
                            {
                                GUILayout.Label(btnLabel, _wrapLabel);
                            }
                        }
                    }
                    GUILayout.EndVertical();

                    // Outputs — same pattern as inputs
                    GUILayout.BeginVertical(GUILayout.Width(270));
                    if (recipe.outputs.Count == 0)
                    {
                        GUILayout.Label("-", _wrapLabel);
                    }
                    else
                    {
                        foreach (KhemistryRecipeIO output in recipe.outputs)
                        {
                            string btnLabel = string.Format("{0:G4}x {1}/sec", output.ratio, output.resourceName);
                            KhemistryResourceInfo outputRes = FindResource(output.resourceName);
                            if (outputRes != null)
                            {
                                if (GUILayout.Button(btnLabel, HighLogic.Skin.button))
                                    OpenDetailWindow(outputRes);
                            }
                            else
                            {
                                GUILayout.Label(btnLabel, _wrapLabel);
                            }
                        }
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();

                    // Divider between recipes
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
}