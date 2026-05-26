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
                    "Part \"" + part.name + "\" has KhemistryFluidCell but no ALLOWED_RESOURCES node.",
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
}