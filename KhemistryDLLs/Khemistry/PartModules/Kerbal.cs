using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// A <see cref="PartModule"/> applied to kerbals, it handles all EVA-side logic and rendering.
    /// </summary>
    public class KhemistryKerbal : PartModule
    {
        ///// Occupation System /////

        // Current occupation of the kerbal, null if none
        public string occupation = null;  // Apparently it gets set to "" if i don't do this
        // Can the kerbal get occupied
        public bool canBeOccupied = true;
        // Is the kerbal frozen (cannot move)
        public bool kerbalFrozen = false;
        // String to show the kerbal's current occupation
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Current occupation")]
        public string OccupationString = "Free";

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Leave current occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void LeaveOccupation() => occupation = null;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void DisableOccupation()
        {
            occupation = null;
            canBeOccupied = false;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EnableOccupation() => canBeOccupied = true;


        // Serialized as "ResA:1.5000|ResB:2.0000" — same format as KhemistryEVACombinedProcessor
        [KSPField(isPersistant = true)]
        public string suitCellResourcesData = "";

        private float _suitCellMaxAmount = 0f;
        private float _suitCellTransferDistance = 10f;
        private readonly HashSet<string> _suitCellAllowedResources = new HashSet<string>();

        ///// Material suit cell (behaves like SUIT_CELL, but stores KhemistryMaterialInstance /////
        ///// via KhemistryMaterialStorage-style logic instead of fluid resources)             /////
        private float _materialSuitCellVolume = 0f;
        private float _materialSuitCellTransferDistance = 2f;
        private readonly List<KhemistryAllowedMaterial> _materialSuitCellAllowed = new List<KhemistryAllowedMaterial>();

        // Not persisted across saves — matches KhemistryMaterialStorage.contents, which is
        // likewise runtime-only.
        public readonly List<KhemistryMaterialInstance> materialSuitCellContents = new List<KhemistryMaterialInstance>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Material Cell")]
        public string MaterialCellContentsDisplay = "No material cell";

        public bool HasMaterialSuitCell => _materialSuitCellVolume > 0f;
        public float MaterialSuitCellTransferDistance => _materialSuitCellTransferDistance;

        /// <summary>
        /// Claim slot for the single allowed kerbalEVA-type KhemistryISRU on this kerbal.
        /// Set by whichever such module's OnStart runs first; used to detect and disable
        /// duplicates regardless of instance/config duplication order.
        /// </summary>
        public KhemistryISRU kerbalEVAISRU = null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is clearly used elsewhere in the code and shouldn't be readonly")]
        private HashSet<string> FluidCellPartNames = new HashSet<string>();
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is clearly used elsewhere in the code and shouldn't be readonly")]
        private HashSet<string> _evaISRUPartNames = new HashSet<string>();

        private ModuleInventoryPart _inventory;
        private KerbalEVA eva;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells available";

        private struct FluidCellRef
        {
            public bool isSuit;
            public StoredPart stored;
        }

        private Dictionary<string, double> GetSuitCellDict()
    => KhemistryEVACombinedProcessor.Deserialize(suitCellResourcesData);

        private void SetSuitCellFromDict(Dictionary<string, double> dict)
    => suitCellResourcesData = KhemistryEVACombinedProcessor.Serialize(dict);

        public bool HasFluidSuitCell => _suitCellMaxAmount > 0f;

        /// <summary>
        /// Requests (positive amount) or produces (negative amount) a resource directly against
        /// this kerbal's fluid suit cell, for use by a kerbalEVA-mode KhemistryISRU. Same
        /// amount/return contract as Part.RequestResource: returns the amount actually removed
        /// (consume) or the negative of the amount actually added (produce). Respects
        /// ALLOWED_RESOURCES and available suit cell capacity.
        /// </summary>
        public double RequestSuitCellResource(string name, double amount)
        {
            if (!HasFluidSuitCell) return 0.0;
            if (_suitCellAllowedResources.Count > 0 && !_suitCellAllowedResources.Contains(name)) return 0.0;

            var dict = GetSuitCellDict();
            dict.TryGetValue(name, out double current);

            if (amount > 0.0)  // Consume
            {
                double take = Math.Min(amount, current);
                if (take <= 0.0) return 0.0;
                double remaining = current - take;
                if (remaining < 1e-9) dict.Remove(name); else dict[name] = remaining;
                SetSuitCellFromDict(dict);
                return take;
            }
            else if (amount < 0.0)  // Produce
            {
                double want = -amount;
                double spaceLeft = _suitCellMaxAmount - KhemistryEVACombinedProcessor.GetTotal(dict);
                double add = Math.Min(want, Math.Max(0.0, spaceLeft));
                if (add <= 0.0) return 0.0;
                dict[name] = current + add;
                SetSuitCellFromDict(dict);
                return -add;
            }

            return 0.0;
        }

        /// <summary>Current volume used in the material suit cell.</summary>
        private float ComputeMaterialSuitCellVolume(float additional = 0f)
        {
            foreach (KhemistryMaterialInstance m in materialSuitCellContents)
                additional += m.volume;
            return additional;
        }

        /// <summary>
        /// Attempts to add a material instance to the material suit cell. Checks that the
        /// material passes at least one ALLOWED_MATERIAL entry (name, shape, PARAM_REQUIREMENTS)
        /// and that there is enough remaining volume, merging with existing contents where
        /// possible, same as <see cref="KhemistryMaterialStorage.AddMaterial"/>.
        /// </summary>
        public bool TryAddMaterialToSuitCell(KhemistryMaterialInstance mat)
        {
            if (!HasMaterialSuitCell) return false;
            if (mat == null) return false;

            bool allowed = false;
            foreach (KhemistryAllowedMaterial a in _materialSuitCellAllowed)
            {
                if (a.Matches(mat)) { allowed = true; break; }
            }
            if (!allowed) return false;

            if (ComputeMaterialSuitCellVolume(mat.volume) >= _materialSuitCellVolume) return false;

            foreach (KhemistryMaterialInstance existing in materialSuitCellContents)
                if (existing.Merge(mat))
                    return true;

            materialSuitCellContents.Add(mat);
            return true;
        }

        private void UpdateMaterialSuitCellDisplay()
        {
            if (!HasMaterialSuitCell) { MaterialCellContentsDisplay = "No material cell"; return; }

            var parts = new List<string>();
            foreach (KhemistryMaterialInstance m in materialSuitCellContents)
                if (m.volume > 0)
                    parts.Add(m.material.name + " as " + m.shape);

            string contentsStr = parts.Count > 0 ? string.Join(", ", parts) : "Empty";
            MaterialCellContentsDisplay = string.Format("{0} ({1:F2}/{2:F2})",
                contentsStr, ComputeMaterialSuitCellVolume(), _materialSuitCellVolume);
        }

        private void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryKerbal/LoadConfigFromPartInfo");
            FluidCellPartNames.Clear();
            _evaISRUPartNames.Clear();
            _suitCellMaxAmount = 0f;
            _suitCellTransferDistance = 10f;
            _suitCellAllowedResources.Clear();
            _materialSuitCellVolume = 0f;
            _materialSuitCellTransferDistance = 2f;
            _materialSuitCellAllowed.Clear();

            ConfigNode moduleNode = null;

            if (part.partInfo?.partConfig != null)
            {
                foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
                {
                    if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                }
            }

            if (moduleNode == null)
            {
                string targetPartName = part.partInfo?.name ?? part.name;
                foreach (ConfigNode partNode in GameDatabase.Instance.GetConfigNodes("PART"))
                {
                    string nodeName = partNode.GetValue("name") ?? "";
                    int slash = nodeName.LastIndexOf('/');
                    if (slash >= 0) nodeName = nodeName.Substring(slash + 1);
                    if (!nodeName.Equals(targetPartName, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (ConfigNode n in partNode.GetNodes("MODULE"))
                    {
                        if (n.GetValue("name") == "KhemistryKerbal") { moduleNode = n; break; }
                    }
                    if (moduleNode != null) break;
                }
            }

            if (moduleNode == null)
            {
                KShared.LogError(
                    "Could not find KhemistryKerbal MODULE node for part \"" + part.name + "\".",
                    "KhemistryKerbal/LoadConfigFromPartInfo");
                return;
            }

            if (moduleNode.HasNode("FLUID_CELL_PARTS"))
                foreach (string name in moduleNode.GetNode("FLUID_CELL_PARTS").GetValues("name"))
                    FluidCellPartNames.Add(name.Trim());

            if (moduleNode.HasNode("EVA_ISRU_PARTS"))
                foreach (string name in moduleNode.GetNode("EVA_ISRU_PARTS").GetValues("name"))
                    _evaISRUPartNames.Add(name.Trim());

            if (moduleNode.HasNode("SUIT_CELL"))
            {
                ConfigNode suitNode = moduleNode.GetNode("SUIT_CELL");
                if (float.TryParse(suitNode.GetValue("maxAmount"), out float tmp))
                    _suitCellMaxAmount = tmp;
                if (float.TryParse(suitNode.GetValue("transferDistance"), out tmp))
                    _suitCellTransferDistance = tmp;
                if (suitNode.HasNode("ALLOWED_RESOURCES"))
                    foreach (string n in suitNode.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                        _suitCellAllowedResources.Add(n.Trim());
            }

            if (moduleNode.HasNode("MATERIAL_SUIT_CELL"))
            {
                ConfigNode matSuitNode = moduleNode.GetNode("MATERIAL_SUIT_CELL");
                _materialSuitCellVolume = KShared.GetFloatValueFromCFG(matSuitNode, "volume", 0f);
                _materialSuitCellTransferDistance = KShared.GetFloatValueFromCFG(matSuitNode, "transferDistance", 2f);

                foreach (ConfigNode allowedNode in matSuitNode.GetNodes("ALLOWED_MATERIAL"))
                {
                    var allowed = new KhemistryAllowedMaterial(allowedNode);
                    if (string.IsNullOrEmpty(allowed.name))
                    {
                        KShared.LogError(
                            "Part \"" + part.name + "\" has a MATERIAL_SUIT_CELL ALLOWED_MATERIAL with no name, skipping.",
                            "KhemistryKerbal/LoadConfigFromPartInfo");
                        continue;
                    }
                    _materialSuitCellAllowed.Add(allowed);
                }

                if (_materialSuitCellVolume > 0f && _materialSuitCellAllowed.Count == 0)
                    KShared.LogError(
                        "Part \"" + part.name + "\" has a MATERIAL_SUIT_CELL with no valid ALLOWED_MATERIAL entries — nothing will ever be accepted.",
                        "KhemistryKerbal/LoadConfigFromPartInfo");
            }

            KShared.Log(
                string.Format("Loaded {0} fluid cell part names, {1} EVA ISRU part names, suitCell={2}.",
                    FluidCellPartNames.Count, _evaISRUPartNames.Count, _suitCellMaxAmount > 0f),
                "KhemistryKerbal/LoadConfigFromPartInfo");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            eva = part.FindModuleImplementing<KerbalEVA>();

            var allHandlers = part.FindModulesImplementing<KhemistryKerbal>();
            if (allHandlers.Count > 1 && allHandlers[0] != this)
            {
                KShared.Log("Duplicate handler found, removing self.", "KhemistryKerbal/OnStart");
                return;
            }

            LoadConfigFromPartInfo();

            _inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (_inventory == null)
                KShared.LogError("No ModuleInventoryPart on Kerbal.", "KhemistryKerbal/OnStart");
            else
                KShared.Log("Inventory found.", "KhemistryKerbal/OnStart");

            KShared.Log("OnStart complete!", "KhemistryKerbal/OnStart");
        }

        public override void OnUpdate()
        {
            if (!string.IsNullOrEmpty(occupation))  // If not free
            {
                Events["LeaveOccupation"].active = true;
                OccupationString = occupation;  // Show occupation
                if (!kerbalFrozen && eva != null)  // If not frozen and have reference
                {
                    eva.strafeSpeed /= 100;  // Freeze kerbal
                    eva.walkSpeed /= 100;
                    eva.runSpeed /= 100;
                    eva.swimSpeed /= 100;
                    eva.ladderClimbSpeed /= 100;
                    kerbalFrozen = true;
                }
            }
            else
            {
                Events["LeaveOccupation"].active = false;
                OccupationString = "Free";
                if (kerbalFrozen && eva != null)
                {
                    eva.strafeSpeed *= 100;
                    eva.walkSpeed *= 100;
                    eva.runSpeed *= 100;
                    eva.swimSpeed *= 100;
                    eva.ladderClimbSpeed *= 100;
                    kerbalFrozen = false;
                }
            }

            UpdateFluidCellDisplay();
            UpdateMaterialSuitCellDisplay();
            Fields["MaterialCellContentsDisplay"].guiActive = HasMaterialSuitCell;

            Events["EnableOccupation"].active = !canBeOccupied;
            Events["DisableOccupation"].active = canBeOccupied;
        }

        private List<FluidCellRef> GetAllCellRefs()
        {
            var result = new List<FluidCellRef>();
            if (_suitCellMaxAmount > 0f)
                result.Add(new FluidCellRef { isSuit = true });
            foreach (StoredPart stored in GetHeldCellSnapshots())
                result.Add(new FluidCellRef { isSuit = false, stored = stored });
            return result;
        }

        private string GetCellLabel(FluidCellRef cell, int index)
            => cell.isSuit ? "Cell 0 (suit)" : string.Format("Cell {0}", index);

        private string ReadCellResourceName(FluidCellRef cell)
        {
            if (cell.isSuit)
            {
                var dict = GetSuitCellDict();
                if (dict.Count == 0) return "";
                var names = new List<string>();
                foreach (var kvp in dict) names.Add(kvp.Key);
                return string.Join(", ", names.ToArray());
            }
            return ReadResourceName(cell.stored);
        }

        private float ReadCellResourceAmount(FluidCellRef cell)
        {
            if (cell.isSuit)
                return (float)KhemistryEVACombinedProcessor.GetTotal(GetSuitCellDict());
            return ReadResourceAmount(cell.stored);
        }

        private float ReadCellMaxAmount(FluidCellRef cell)
            => cell.isSuit ? _suitCellMaxAmount : ReadMaxAmount(cell.stored.partName);

        private void UpdateFluidCellDisplay()
        {
            var cells = GetAllCellRefs();
            if (cells.Count == 0) { CellContentsDisplay = "No cells available"; return; }
            var parts = new List<string>();
            for (int i = 0; i < cells.Count; i++)
            {
                string label = GetCellLabel(cells[i], i);
                if (cells[i].isSuit)
                {
                    var dict = GetSuitCellDict();
                    double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                    if (dict.Count == 0)
                        parts.Add(string.Format("{0}: Empty (0/{1:F2})", label, _suitCellMaxAmount));
                    else
                    {
                        var cp = new List<string>();
                        foreach (var kvp in dict)
                            cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                        parts.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})",
                            label, string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                    }
                }
                else
                {
                    string resName = ReadResourceName(cells[i].stored);
                    float resAmount = ReadResourceAmount(cells[i].stored);
                    float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                    parts.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", label)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", label, resName, resAmount, maxAmount));
                }
            }
            CellContentsDisplay = string.Join("  |  ", parts.ToArray());
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;

            double dt = TimeWarp.fixedDeltaTime;

            foreach (StoredPart stored in GetProcessorSnapshots())
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                if (prefab == null || !prefab.IsConfigLoaded) continue;

                bool running = ReadProcessorBool(stored, "isRunning");
                string converterName = ReadProcessorField(stored, "activeConverterName");
                if (!running || string.IsNullOrEmpty(converterName)) continue;

                var resources = DeserializeProcessorResources(stored);
                bool cycled = prefab.RunConversionCycle(resources, converterName, dt);
                WriteProcessorResources(stored, resources);

                if (!cycled)
                {
                    WriteProcessorField(stored, "isRunning", "False");
                    KShared.Log(
                        "Processor converter \"" + converterName + "\" stopped: insufficient inputs.",
                        "KhemistryKerbal/FixedUpdate");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + converterName + "\" stopped: insufficient inputs.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                }
            }
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
                if (moduleSnap.moduleName == "KhemistryFluidCell") return moduleSnap;
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
            KShared.Log("Called with range " + range.ToString(), "KhemistryKerbal/GetPartsInRange");
            var result = new List<Part>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
                foreach (Part p in v.parts)
                {
                    if (p == this.part) continue;
                    if (Vector3.Distance(this.part.transform.position, p.transform.position) <= range)
                        result.Add(p);
                }
            KShared.Log("Acquired " + result.Count.ToString() + " parts.", "KhemistryKerbal/GetPartsInRange");
            return result;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from cell to nearby part",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVASendResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { Debug.LogError("Khemistry: KShared null in EVASendResources!"); return; }
            KShared.Log("Called! (Transfer from ... to nearby part button)", "KhemistryKerbal/EVASendResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForSend(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    if (cells[i].isSuit)
                    {
                        var dict = GetSuitCellDict();
                        double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                        if (dict.Count == 0)
                            labels.Add(string.Format("{0}: Empty (0/{1:F2})", cellLabel, _suitCellMaxAmount));
                        else
                        {
                            var cp = new List<string>();
                            foreach (var kvp in dict)
                                cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                            labels.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})", cellLabel,
                                string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                        }
                    }
                    else
                    {
                        string resName = ReadResourceName(cells[i].stored);
                        float resAmount = ReadResourceAmount(cells[i].stored);
                        float maxAmount = ReadMaxAmount(cells[i].stored.partName);
                        labels.Add(string.IsNullOrEmpty(resName)
                            ? string.Format("{0}: Empty", cellLabel)
                            : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                    }
                }
                shared.ShowSelector("Which cell to send from?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForSend(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForSend(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForSend(); return; }

            string resourceName = ReadResourceName(cell.stored);
            float resourceAmount = ReadResourceAmount(cell.stored);
            float range = ReadTransferDistance(cell.stored.partName);

            if (string.IsNullOrEmpty(resourceName) || resourceAmount <= 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is empty.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var targetParts = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!targetParts.ContainsKey(lbl)) targetParts.Add(lbl, p);
                    break;
                }

            if (targetParts.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                targetParts.Keys.ToList(), label =>
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
                    if (newAmount <= 0.001f) { WriteResourceName(cell.stored, ""); WriteResourceAmount(cell.stored, 0f); }
                    else WriteResourceAmount(cell.stored, newAmount);
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
            KShared.Log("Called! (Transfer from ... to cell button)", "KhemistryKerbal/EVATakeResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForTake(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    string resName = ReadCellResourceName(cells[i]);
                    float resAmount = ReadCellResourceAmount(cells[i]);
                    float maxAmount = ReadCellMaxAmount(cells[i]);
                    labels.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", cellLabel)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} kg", cellLabel, resName, resAmount, maxAmount));
                }
                shared.ShowSelector("Which cell to fill?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForTake(cells[index]);
                });
            }
        }

        private void ShowPartSelectorForTake(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForTake(); return; }
            KShared.Log("Called!", "KhemistryKerbal/ShowPartSelectorForTake");

            string currentResource = ReadResourceName(cell.stored);
            float currentAmount = ReadResourceAmount(cell.stored);
            float maxAmount = ReadMaxAmount(cell.stored.partName);
            float range = ReadTransferDistance(cell.stored.partName);
            HashSet<string> allowed = ReadAllowedResources(cell.stored.partName);

            if (currentAmount >= maxAmount)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is full.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
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
                    if (string.IsNullOrEmpty(currentResource) && allowed.Count > 0
                        && !allowed.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F1} kg)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!optionParts.ContainsKey(lbl))
                    {
                        optionParts.Add(lbl, p);
                        optionResources.Add(lbl, pr.resourceName);
                    }
                }

            if (optionParts.Count == 0)
            {
                KShared.Log("No nearby parts with resource " + currentResource + " were detected.", "KhemistryKerbal/ShowPartSelectorForTake");
                string msg = string.IsNullOrEmpty(currentResource)
                    ? "No allowed resources found within range."
                    : "No nearby parts have " + currentResource + ".";
                ScreenMessages.PostScreenMessage(new ScreenMessage(msg, 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowPartSelectorForTake");
            KShared.Instance.ShowSelector("Take resources from...", optionParts.Keys.ToList(), label =>
            {
                Part source = optionParts[label];
                string resourceName = optionResources[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                if (sourceResource == null) return;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min(amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        WriteResourceName(cell.stored, resourceName);
                        WriteResourceAmount(cell.stored, currentAmount + (float)taken);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} kg of {1}.", taken, resourceName),
                            5.0f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        private void ShowSuitCellPartSelectorForTake()
        {
            KShared.Log("Called!", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            var dict = GetSuitCellDict();
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(dict);
            double spaceRemaining = _suitCellMaxAmount - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                KShared.Log("Unable to take resources: suit cell is full.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, PartResource resource)>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.amount <= 0.0) continue;
                    if (_suitCellAllowedResources.Count > 0
                        && !_suitCellAllowedResources.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F2})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(lbl))
                        options.Add(lbl, (p, pr));
                }

            if (options.Count == 0)
            {
                KShared.Log("No nearby parts have any of the allowed resources.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have allowed resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            KShared.Instance.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, sourceResource) = options[label];
                string resourceName = sourceResource.resourceName;
                float maxTake = (float)Math.Min(sourceResource.amount, spaceRemaining);

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        double taken = Math.Min((double)amount, maxTake);
                        if (taken <= 0.0) return;
                        sourceResource.amount -= taken;
                        var d = GetSuitCellDict();
                        d.TryGetValue(resourceName, out double existing);
                        d[resourceName] = existing + taken;
                        SetSuitCellFromDict(d);
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} of {1}.", taken, resourceName),
                            5f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }

        private List<StoredPart> GetProcessorSnapshots()
        {
            var result = new List<StoredPart>();
            if (_inventory == null) return result;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                if (ap?.partPrefab.FindModuleImplementing<KhemistryEVACombinedProcessor>() != null)
                    result.Add(stored);
            }
            return result;
        }

        private KhemistryEVACombinedProcessor GetPrefabProcessor(StoredPart stored)
            => PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                .FindModuleImplementing<KhemistryEVACombinedProcessor>();

        private ProtoPartModuleSnapshot GetProcessorSnapshot(StoredPart stored)
        {
            if (stored.snapshot == null) return null;
            foreach (ProtoPartModuleSnapshot snap in stored.snapshot.modules)
                if (snap.moduleName == "KhemistryEVACombinedProcessor") return snap;
            return null;
        }

        private string ReadProcessorField(StoredPart stored, string key)
            => GetProcessorSnapshot(stored)?.moduleValues.GetValue(key) ?? "";

        private void WriteProcessorField(StoredPart stored, string key, string value)
            => GetProcessorSnapshot(stored)?.moduleValues.SetValue(key, value);

        private bool ReadProcessorBool(StoredPart stored, string key)
        {
            return bool.TryParse(ReadProcessorField(stored, key), out bool result) && result;
        }

        private Dictionary<string, double> DeserializeProcessorResources(StoredPart stored)
            => KhemistryEVACombinedProcessor.Deserialize(ReadProcessorField(stored, "storedResourcesData"));

        private void WriteProcessorResources(StoredPart stored, Dictionary<string, double> resources)
            => WriteProcessorField(stored, "storedResourcesData",
                KhemistryEVACombinedProcessor.Serialize(resources));

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Use Held Processor",
                  groupName = "processoreva", groupDisplayName = "Processors", groupStartCollapsed = false,
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EVAUseProcessor()
        {
            KShared.Log("Called! (Use Held Processor button)", "KhemistryKerbal/EVAUseProcessor");
            var shared = KShared.Instance;
            if (shared == null) return;

            var processors = GetProcessorSnapshots();
            if (processors.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No processors in inventory.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (processors.Count == 1)
            {
                ShowProcessorActionMenu(processors[0]);
                return;
            }

            var labels = new List<string>();
            foreach (StoredPart stored in processors)
            {
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                string name = prefab != null ? stored.partName : stored.partName;
                bool running = ReadProcessorBool(stored, "isRunning");
                string conv = ReadProcessorField(stored, "activeConverterName");
                string suffix = running ? " [" + conv + "]" : " [Stopped]";
                labels.Add(name + suffix);
            }

            shared.ShowSelector("Select processor", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowProcessorActionMenu(processors[idx]);
            });
        }

        private void ShowProcessorActionMenu(StoredPart stored)
        {
            var shared = KShared.Instance;
            KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
            if (prefab == null || !prefab.IsConfigLoaded) return;

            bool running = ReadProcessorBool(stored, "isRunning");
            var actions = new List<string>();

            if (prefab.Converters.Count > 0)
            {
                if (!running) actions.Add("Start Converter");
                else actions.Add("Stop Converter");
            }

            actions.Add("Transfer In (from nearby)");

            var resources = DeserializeProcessorResources(stored);
            if (KhemistryEVACombinedProcessor.GetTotal(resources) > 0.0)
                actions.Add("Transfer Out (to nearby)");

            if (actions.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No actions available.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Processor: " + stored.partName, actions,
                action => ExecuteProcessorAction(stored, prefab, action));
        }

        private void ExecuteProcessorAction(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string action)
        {
            var shared = KShared.Instance;
            if (shared == null) return;

            switch (action)
            {
                case "Start Converter":
                    {
                        if (prefab.Converters.Count == 1)
                        {
                            WriteProcessorField(stored, "activeConverterName", prefab.Converters[0].name);
                            WriteProcessorField(stored, "isRunning", "True");
                            ScreenMessages.PostScreenMessage(new ScreenMessage(
                                "Converter \"" + prefab.Converters[0].name + "\" started.",
                                4f, ScreenMessageStyle.UPPER_CENTER));
                        }
                        else
                        {
                            var names = new List<string>();
                            foreach (var conv in prefab.Converters) names.Add(conv.name);
                            shared.ShowSelector("Select converter to start", names, name =>
                            {
                                WriteProcessorField(stored, "activeConverterName", name);
                                WriteProcessorField(stored, "isRunning", "True");
                                ScreenMessages.PostScreenMessage(new ScreenMessage(
                                    "Converter \"" + name + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                            });
                        }
                        break;
                    }
                case "Stop Converter":
                    WriteProcessorField(stored, "isRunning", "False");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter stopped.", 4f, ScreenMessageStyle.UPPER_CENTER));
                    break;

                case "Transfer In (from nearby)":
                    ShowProcessorTransferInMenu(stored, prefab);
                    break;

                case "Transfer Out (to nearby)":
                    ShowProcessorTransferOutMenu(stored, prefab);
                    break;
            }
        }

        private void ShowProcessorTransferInMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            KShared.Log("Called!", "KhemistryKerbal/ShowProcessorTransferInMenu");
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);
            double currentTotal = KhemistryEVACombinedProcessor.GetTotal(resources);
            double spaceRemaining = prefab.MaxTotalStorage - currentTotal;

            if (spaceRemaining <= 0.0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is full.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var options = new Dictionary<string, (Part part, string resourceName)>();
            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (!prefab.SupportedResources.Contains(pr.resourceName)) continue;
                    if (pr.amount <= 0.0) continue;
                    string label = string.Format("{0} / {1}  ({2}: {3:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, (p, pr.resourceName));
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have supported resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                var (sourcePart, resourceName) = options[label];
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = sourcePart.Resources.Get(def.id);
                if (sourceResource == null) return;

                double taken = Math.Min(sourceResource.amount, spaceRemaining);
                sourceResource.amount -= taken;

                var res = DeserializeProcessorResources(stored);
                res.TryGetValue(resourceName, out double existing);
                res[resourceName] = existing + taken;
                WriteProcessorResources(stored, res);

                KShared.Log(
                    string.Format("Processor received {0:F4} of {1} from {2}.",
                        taken, resourceName, sourcePart.partInfo.title),
                    "KhemistryKerbal/ProcessorTransferIn");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    string.Format("Received {0:F2} of {1}.", taken, resourceName),
                    5f, ScreenMessageStyle.UPPER_CENTER));
            });
        }

        private void ShowProcessorTransferOutMenu(StoredPart stored,
            KhemistryEVACombinedProcessor prefab)
        {
            var shared = KShared.Instance;
            var resources = DeserializeProcessorResources(stored);

            if (resources.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Processor is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (resources.Count == 1)
            {
                string only = ""; double onlyAmount = 0.0;
                foreach (var kvp in resources) { only = kvp.Key; onlyAmount = kvp.Value; }
                ShowProcessorTransferOutTargets(stored, prefab, only, onlyAmount);
                return;
            }

            var resLabels = new List<string>();
            var resKeys = new List<string>();
            foreach (var kvp in resources)
            {
                resLabels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                resKeys.Add(kvp.Key);
            }

            shared.ShowSelector("Which resource to send?", resLabels, label =>
            {
                int idx = resLabels.IndexOf(label);
                if (idx >= 0)
                    ShowProcessorTransferOutTargets(stored, prefab, resKeys[idx], resources[resKeys[idx]]);
            });
        }

        private void ShowProcessorTransferOutTargets(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string resourceName, double resourceAmount)
        {
            var shared = KShared.Instance;
            var options = new Dictionary<string, Part>();

            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string label = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(label))
                        options.Add(label, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;

                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;

                    var res = DeserializeProcessorResources(stored);
                    double remaining = resourceAmount - pushed;
                    if (remaining < 1e-9) res.Remove(resourceName);
                    else res[resourceName] = remaining;
                    WriteProcessorResources(stored, res);

                    KShared.Log(
                        string.Format("Processor sent {0:F4} of {1} to {2}.",
                            pushed, resourceName, target.partInfo.title),
                        "KhemistryKerbal/ProcessorTransferOut");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
        private void ShowSuitCellPartSelectorForSend()
        {
            var dict = GetSuitCellDict();
            if (dict.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Suit cell is empty.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (dict.Count == 1)
            {
                foreach (var kvp in dict) { ShowSuitCellSendTargets(kvp.Key, kvp.Value); return; }
            }

            var labels = new List<string>();
            var keys = new List<string>();
            var amounts = new List<double>();
            foreach (var kvp in dict)
            {
                labels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                keys.Add(kvp.Key);
                amounts.Add(kvp.Value);
            }

            KShared.Instance.ShowSelector("Which resource to send?", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowSuitCellSendTargets(keys[idx], amounts[idx]);
            });
        }

        private void ShowSuitCellSendTargets(string resourceName, double resourceAmount)
        {
            var options = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (pr.amount >= pr.maxAmount) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    if (!options.ContainsKey(lbl)) options.Add(lbl, p);
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts can accept " + resourceName + ".", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Instance.ShowSelector("Send " + resourceName + " to...",
                new List<string>(options.Keys), label =>
                {
                    Part target = options[label];
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (targetResource == null) return;
                    double space = targetResource.maxAmount - targetResource.amount;
                    double pushed = Math.Min(resourceAmount, space);
                    targetResource.amount += pushed;
                    var d = GetSuitCellDict();
                    d.TryGetValue(resourceName, out double existing);
                    double remaining = existing - pushed;
                    if (remaining < 1e-9) d.Remove(resourceName);
                    else d[resourceName] = remaining;
                    SetSuitCellFromDict(d);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
    }
}
