using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// A <see cref="PartModule"/> applied to kerbals, it handles all EVA-side logic and rendering.
    /// </summary>
    public partial class KhemistryKerbal : PartModule
    {
        ///// Occupation System /////

        // Current occupation of the kerbal, null if none
        public string occupation = null;  // Apparently it gets set to "" if i don't do this
        // Can the kerbal get occupied
        [KSPField(isPersistant = true)]
        public bool canBeOccupied = true;
        // Is the kerbal frozen (cannot move)
        public bool kerbalFrozen = false;
        // String to show the kerbal's current occupation
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Current occupation")]
        public string OccupationString = "Free";

        // Serialized as "ResA:1.5000|ResB:2.0000".
        [KSPField(isPersistant = true)]
        public string suitCellResourcesData = "";

        private float _suitCellMaxAmount = 0f;
        private float _suitCellTransferDistance = 10f;
        private readonly List<HashSet<string>> _suitCellSupportedResourceGroups
            = new List<HashSet<string>>();

        ///// Material suit cell (behaves like SUIT_CELL, but stores KhemistryMaterialInstance /////
        ///// via KhemistryMaterialStorage-style logic instead of fluid resources)             /////
        private float _materialSuitCellVolume = 0f;
        private float _materialSuitCellTransferDistance = 2f;
        private readonly List<KhemistryAllowedMaterial> _materialSuitCellAllowed = new List<KhemistryAllowedMaterial>();

        public readonly List<KhemistryMaterialInstance> materialSuitCellContents = new List<KhemistryMaterialInstance>();
        private readonly List<ConfigNode> _pendingMaterialSuitContents = new List<ConfigNode>();

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

        private ModuleInventoryPart _inventory;
        private KerbalEVA eva;
        private bool _disabledDuplicate;
        private uint _crewPersistentId;
        private string _crewName = "";
        private bool _boardingEventRegistered;
        private bool _suitPersistenceRestoreChecked;
        private bool _loadedAuthoritativeSuitState;
        private int _inventoryChangeBatchDepth;
        private bool _inventoryChangePending;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Held Cells")]
        public string CellContentsDisplay = "No cells available";

        private struct FluidCellRef
        {
            public bool isSuit;
            public StoredPart stored;
        }

        private static string AddUniqueOption<T>(Dictionary<string, T> options,
            string baseLabel, T value)
        {
            string label = baseLabel;
            int suffix = 2;
            while (options.ContainsKey(label))
                label = baseLabel + " (" + suffix++ + ")";
            options.Add(label, value);
            return label;
        }

        private static string MakeUniqueLabel(ICollection<string> labels, string baseLabel)
        {
            string label = baseLabel;
            int suffix = 2;
            while (labels.Contains(label)) label = baseLabel + " (" + suffix++ + ")";
            return label;
        }

        private static bool HasUsableAmount(PartResource resource)
            => resource != null && KShared.IsFinite(resource.amount) && resource.amount > 0.0;

        private static bool CanAcceptResource(PartResource resource)
            => resource != null && KShared.IsFinite(resource.amount) && resource.amount >= 0.0
                && KShared.IsFinite(resource.maxAmount) && resource.maxAmount >= 0.0
                && resource.amount < resource.maxAmount;

        private bool IsStoredPartCurrent(StoredPart stored)
        {
            if (_inventory == null || stored == null) return false;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
                if (ReferenceEquals(_inventory.storedParts.At(i), stored))
                    return true;
            return false;
        }

        private bool IsPartCurrentAndInRange(Part candidate, double range)
        {
            if (candidate == null || part == null || candidate == part
                || !KShared.IsFinite(range) || range < 0.0 || candidate.vessel == null
                || !FlightGlobals.VesselsLoaded.Contains(candidate.vessel)
                || !candidate.vessel.parts.Contains(candidate))
                return false;
            return Vector3.Distance(part.transform.position, candidate.transform.position) <= range;
        }

        private void NotifyInventoryChanged()
        {
            if (_inventoryChangeBatchDepth > 0)
            {
                _inventoryChangePending = true;
                return;
            }
            if (_inventory != null)
                GameEvents.onModuleInventoryChanged.Fire(_inventory);
        }

        internal void NotifyInventoryProcessorChanged() => NotifyInventoryChanged();

        internal void BeginInventoryProcessorChanges()
            => _inventoryChangeBatchDepth++;

        internal void EndInventoryProcessorChanges()
        {
            if (_inventoryChangeBatchDepth <= 0) return;
            _inventoryChangeBatchDepth--;
            if (_inventoryChangeBatchDepth != 0 || !_inventoryChangePending) return;
            _inventoryChangePending = false;
            if (_inventory != null)
                GameEvents.onModuleInventoryChanged.Fire(_inventory);
        }

        internal static Dictionary<string, double> DeserializeResourceDictionary(string data)
            => KhemistryFluidCell.DeserializeResources(data);

        internal static string SerializeResourceDictionary(
            IDictionary<string, double> resources)
            => KhemistryFluidCell.SerializeResources(resources);

        internal static double GetResourceDictionaryTotal(
            IDictionary<string, double> resources)
            => KhemistryFluidCell.GetResourceTotal(resources);

        /// <summary>
        /// Routes a held partEVA converter request through the fluid cell on that same held
        /// part, then through the suit cell when the converter explicitly enables it.
        /// </summary>
        internal double RequestInventoryProcessorResource(StoredPart stored, string name,
            double amount, bool allowSuitCell)
        {
            if (!IsStoredPartCurrent(stored) || string.IsNullOrWhiteSpace(name)
                || !KShared.IsFinite(amount) || amount == 0.0)
                return 0.0;

            name = name.Trim();
            bool hasPartCell = GetCellModuleSnapshot(stored) != null;
            double moved = 0.0;

            if (hasPartCell && amount > 0.0)
            {
                double available = ReadResourceAmountValue(stored, name);
                double take = Math.Min(amount, available);
                if (take > 0.0
                    && WriteResourceAmount(stored, name, available - take))
                    moved += take;
            }
            else if (hasPartCell && amount < 0.0
                && IsResourceAllowedForAddition(stored, name))
            {
                double current = ReadResourceAmountValue(stored, name);
                KhemistryFluidCell cell = ReadFluidCellPrefab(stored.partName);
                double totalSpace = cell == null || !KShared.IsFinite(cell.ResourceMaxAmount)
                    || cell.ResourceMaxAmount <= 0f
                    ? 0.0
                    : Math.Max(0.0, cell.ResourceMaxAmount - ReadResourceAmount(stored));
                double add = Math.Min(-amount, totalSpace);
                if (add > 0.0
                    && WriteResourceAmount(stored, name, current + add))
                    moved += add;
            }

            double remaining = Math.Abs(amount) - moved;
            if (allowSuitCell && remaining > 0.0)
            {
                double suitMoved = RequestSuitCellResource(name,
                    amount > 0.0 ? remaining : -remaining);
                if (KShared.IsFinite(suitMoved)) moved += Math.Abs(suitMoved);
            }

            return amount > 0.0 ? moved : -moved;
        }

        private void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryKerbal/LoadConfigFromPartInfo");
            _suitCellMaxAmount = 0f;
            _suitCellTransferDistance = 10f;
            _suitCellSupportedResourceGroups.Clear();
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

            if (moduleNode.HasNode("SUIT_CELL"))
            {
                ConfigNode suitNode = moduleNode.GetNode("SUIT_CELL");
                _suitCellMaxAmount = KShared.GetFloatValueFromCFG(suitNode, "maxAmount", 0f);
                _suitCellTransferDistance = KShared.GetFloatValueFromCFG(suitNode, "transferDistance", 10f);
                if (float.IsNaN(_suitCellMaxAmount) || float.IsInfinity(_suitCellMaxAmount)
                    || _suitCellMaxAmount <= 0f)
                {
                    KShared.LogError("Part \"" + part.name
                        + "\" has a SUIT_CELL with invalid maxAmount; the suit cell was disabled.",
                        "KhemistryKerbal/LoadConfigFromPartInfo");
                    _suitCellMaxAmount = 0f;
                }
                if (float.IsNaN(_suitCellTransferDistance) || float.IsInfinity(_suitCellTransferDistance)
                    || _suitCellTransferDistance < 0f)
                {
                    KShared.LogError("Part \"" + part.name
                        + "\" has a negative SUIT_CELL transferDistance; using 10.",
                        "KhemistryKerbal/LoadConfigFromPartInfo");
                    _suitCellTransferDistance = 10f;
                }
                foreach (ConfigNode supportedNode in suitNode.GetNodes("SUPPORTED_RESOURCES"))
                {
                    var group = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string n in supportedNode.GetValues("name"))
                    {
                        string trimmed = n?.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) group.Add(trimmed);
                    }
                    if (group.Count > 0)
                        _suitCellSupportedResourceGroups.Add(group);
                }
            }

            if (moduleNode.HasNode("MATERIAL_SUIT_CELL"))
            {
                ConfigNode matSuitNode = moduleNode.GetNode("MATERIAL_SUIT_CELL");
                _materialSuitCellVolume = KShared.GetFloatValueFromCFG(matSuitNode, "volume", 0f);
                _materialSuitCellTransferDistance = KShared.GetFloatValueFromCFG(matSuitNode, "transferDistance", 2f);
                if (float.IsNaN(_materialSuitCellVolume) || float.IsInfinity(_materialSuitCellVolume)
                    || _materialSuitCellVolume <= 0f)
                {
                    KShared.LogError("Part \"" + part.name
                        + "\" has a MATERIAL_SUIT_CELL with invalid volume; the material cell was disabled.",
                        "KhemistryKerbal/LoadConfigFromPartInfo");
                    _materialSuitCellVolume = 0f;
                }
                if (float.IsNaN(_materialSuitCellTransferDistance)
                    || float.IsInfinity(_materialSuitCellTransferDistance)
                    || _materialSuitCellTransferDistance < 0f)
                {
                    KShared.LogError("Part \"" + part.name
                        + "\" has a negative MATERIAL_SUIT_CELL transferDistance; using 2.",
                        "KhemistryKerbal/LoadConfigFromPartInfo");
                    _materialSuitCellTransferDistance = 2f;
                }

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

            KShared.Log("Loaded kerbal resource storage; suitCell="
                + (_suitCellMaxAmount > 0f) + ".",
                "KhemistryKerbal/LoadConfigFromPartInfo");
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _loadedAuthoritativeSuitState = node != null
                && (node.HasValue("suitCellResourcesData")
                    || node.HasValue("canBeOccupied")
                    || node.HasNode("SUIT_STORED_MATERIAL"));
            materialSuitCellContents.Clear();
            _pendingMaterialSuitContents.Clear();
            foreach (ConfigNode savedNode in node.GetNodes("SUIT_STORED_MATERIAL"))
            {
                ConfigNode copy = new ConfigNode("SUIT_STORED_MATERIAL");
                savedNode.CopyTo(copy);
                _pendingMaterialSuitContents.Add(copy);
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            while (node.HasNode("SUIT_STORED_MATERIAL"))
                node.RemoveNode("SUIT_STORED_MATERIAL");
            foreach (ConfigNode pendingNode in _pendingMaterialSuitContents)
            {
                ConfigNode copy = new ConfigNode("SUIT_STORED_MATERIAL");
                pendingNode.CopyTo(copy);
                node.AddNode(copy);
            }
            foreach (KhemistryMaterialInstance material in materialSuitCellContents)
                if (material != null && material.amount > 0)
                    node.AddNode(material.ToConfigNode("SUIT_STORED_MATERIAL"));
        }

        internal bool HasMeaningfulSuitPersistenceState()
            => _loadedAuthoritativeSuitState || GetSuitCellDict().Count > 0
                || materialSuitCellContents.Count > 0
                || _pendingMaterialSuitContents.Count > 0 || !canBeOccupied;

        internal ConfigNode ExportSuitPersistenceSnapshot()
        {
            if (!HasMeaningfulSuitPersistenceState()) return null;

            ConfigNode snapshot = new ConfigNode("KERBAL_SUIT");
            snapshot.AddValue("suitCellResourcesData",
                SerializeResourceDictionary(GetSuitCellDict()));
            snapshot.AddValue("canBeOccupied", canBeOccupied);
            foreach (ConfigNode pendingNode in _pendingMaterialSuitContents)
            {
                ConfigNode copy = new ConfigNode("SUIT_STORED_MATERIAL");
                pendingNode.CopyTo(copy);
                snapshot.AddNode(copy);
            }
            foreach (KhemistryMaterialInstance material in materialSuitCellContents)
                if (material != null && material.amount > 0)
                    snapshot.AddNode(material.ToConfigNode("SUIT_STORED_MATERIAL"));
            return snapshot;
        }

        internal bool ImportSuitPersistenceSnapshot(ConfigNode snapshot)
        {
            if (snapshot == null || HasMeaningfulSuitPersistenceState()) return false;

            suitCellResourcesData = SerializeResourceDictionary(
                DeserializeResourceDictionary(
                    snapshot.GetValue("suitCellResourcesData")));
            if (bool.TryParse(snapshot.GetValue("canBeOccupied"), out bool savedPreference))
                canBeOccupied = savedPreference;

            foreach (ConfigNode savedNode in snapshot.GetNodes("SUIT_STORED_MATERIAL"))
            {
                ConfigNode copy = new ConfigNode("SUIT_STORED_MATERIAL");
                savedNode.CopyTo(copy);
                _pendingMaterialSuitContents.Add(copy);
            }
            return true;
        }

        private void CacheCrewIdentity()
        {
            ProtoCrewMember crew = part?.protoModuleCrew?.FirstOrDefault();
            if (crew == null) return;
            _crewPersistentId = crew.persistentID;
            _crewName = crew.name ?? "";
        }

        private bool TryRestoreBoardedSuitState()
        {
            if (_suitPersistenceRestoreChecked) return false;
            CacheCrewIdentity();
            if (_crewPersistentId == 0 && string.IsNullOrWhiteSpace(_crewName))
                return false;
            KhemistryKerbalSuitScenario scenario = KhemistryKerbalSuitScenario.Instance;
            if (scenario == null || !scenario.IsReady) return false;

            bool restored = scenario.TryRestoreBoardingSnapshot(this,
                _crewPersistentId, _crewName);
            _suitPersistenceRestoreChecked = true;
            return restored;
        }

        private void OnCrewBoardVessel(GameEvents.FromToAction<Part, Part> action)
        {
            if (_disabledDuplicate || action.from != part) return;
            CacheCrewIdentity();
            if (_crewPersistentId == 0 && string.IsNullOrWhiteSpace(_crewName))
            {
                KShared.LogError("Could not preserve suit-cell contents while boarding because the kerbal identity is unavailable.",
                    "KhemistryKerbal/OnCrewBoardVessel");
                return;
            }
            KhemistryKerbalSuitScenario scenario = KhemistryKerbalSuitScenario.Instance;
            if (scenario == null || !scenario.IsReady)
            {
                KShared.LogError("Could not preserve suit-cell contents while boarding because the per-save suit scenario is unavailable.",
                    "KhemistryKerbal/OnCrewBoardVessel");
                return;
            }
            scenario.StoreBoardingSnapshot(this, _crewPersistentId, _crewName);
            // An occupation describes an active EVA task, not a lasting crew preference.
            occupation = null;
        }

        public void OnDestroy()
        {
            if (!_boardingEventRegistered) return;
            GameEvents.onCrewBoardVessel.Remove(OnCrewBoardVessel);
            _boardingEventRegistered = false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            eva = part.FindModuleImplementing<KerbalEVA>();

            var allHandlers = part.FindModulesImplementing<KhemistryKerbal>();
            if (allHandlers.Count > 1 && allHandlers[0] != this)
            {
                KShared.LogError("Duplicate handler found; disabling this copy.", "KhemistryKerbal/OnStart");
                _disabledDuplicate = true;
                enabled = false;
                foreach (BaseEvent moduleEvent in Events) moduleEvent.active = false;
                return;
            }

            LoadConfigFromPartInfo();
            CacheCrewIdentity();
            TryRestoreBoardedSuitState();
            RestoreMaterialSuitContents();

            GameEvents.onCrewBoardVessel.Add(OnCrewBoardVessel);
            _boardingEventRegistered = true;

            _inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (_inventory == null)
                KShared.LogError("No ModuleInventoryPart on Kerbal.", "KhemistryKerbal/OnStart");
            else
                KShared.Log("Inventory found.", "KhemistryKerbal/OnStart");

            KShared.Log("OnStart complete!", "KhemistryKerbal/OnStart");
        }

        public override void OnUpdate()
        {
            if (_disabledDuplicate) return;
            if (!_suitPersistenceRestoreChecked && TryRestoreBoardedSuitState())
                RestoreMaterialSuitContents();
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

        public void FixedUpdate()
        {
            if (_disabledDuplicate) return;
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;

            double dt = TimeWarp.fixedDeltaTime;

            foreach (StoredPart storedCell in GetHeldCellSnapshots())
                ApplyHeldBatteryDegradation(storedCell);

            foreach (HeldPartEVAProcessor processor in GetHeldPartEVAProcessors())
                if (IsStoredPartCurrent(processor.stored))
                    processor.prefab.RunInventoryCycle(this, processor.stored,
                        processor.snapshot, processor.config, dt);
        }

        // Used by modules such as KhemistryDegradingBattery that genuinely operate on a
        // KSP PartResource. Fluid-cell contents never use this collection.
        private ProtoPartResourceSnapshot FindStoredPartResource(StoredPart stored,
            string resourceName)
        {
            if (stored?.snapshot?.resources == null) return null;
            foreach (ProtoPartResourceSnapshot resource in stored.snapshot.resources)
            {
                if (resource == null) continue;
                if (resource.resourceName == resourceName) return resource;
            }
            return null;
        }

        private ProtoPartModuleSnapshot GetModuleSnapshot(StoredPart stored, string moduleName)
        {
            if (stored?.snapshot?.modules == null) return null;
            foreach (ProtoPartModuleSnapshot module in stored.snapshot.modules)
                if (module.moduleName == moduleName) return module;
            return null;
        }

        private void ApplyHeldBatteryDegradation(StoredPart stored)
        {
            KhemistryDegradingBattery prefab = PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                .FindModuleImplementing<KhemistryDegradingBattery>();
            ProtoPartModuleSnapshot snapshot = GetModuleSnapshot(stored, "KhemistryDegradingBattery");
            if (prefab == null || snapshot == null || !KShared.IsFinite(prefab.DegradeTime)
                || prefab.DegradeTime <= 0.0) return;

            ProtoPartResourceSnapshot resource = FindStoredPartResource(stored,
                prefab.ResourceName);
            if (resource == null || !KShared.IsFinite(resource.amount) || resource.amount < 0.0
                || !KShared.IsFinite(resource.maxAmount) || resource.maxAmount < 0.0) return;

            double now = Planetarium.GetUniversalTime();
            if (!KShared.IsFinite(now) || now < 0.0) return;
            bool changed = false;
            if (!double.TryParse(snapshot.moduleValues.GetValue("OriginalMaxAmount"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double originalMax)
                || originalMax < 0.0 || double.IsNaN(originalMax) || double.IsInfinity(originalMax))
            {
                originalMax = resource.maxAmount;
                snapshot.moduleValues.SetValue("OriginalMaxAmount",
                    originalMax.ToString("R", CultureInfo.InvariantCulture), true);
                changed = true;
            }
            if (!double.TryParse(snapshot.moduleValues.GetValue("StartTime"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double startTime)
                || startTime < 0.0 || double.IsNaN(startTime) || double.IsInfinity(startTime))
            {
                startTime = now;
                snapshot.moduleValues.SetValue("StartTime",
                    startTime.ToString("R", CultureInfo.InvariantCulture), true);
                changed = true;
            }

            double degradeSeconds = prefab.DegradeTime * 60.0;
            double fraction = Math.Min(1.0, Math.Max(0.0,
                1.0 - ((now - startTime) / degradeSeconds)));
            double newMaxAmount = originalMax * fraction;
            double newAmount = Math.Max(0.0, Math.Min(resource.amount, newMaxAmount));
            if (resource.maxAmount != newMaxAmount)
            {
                resource.maxAmount = newMaxAmount;
                changed = true;
            }
            if (resource.amount != newAmount)
            {
                resource.amount = newAmount;
                changed = true;
            }
            if (changed) NotifyInventoryChanged();
        }

        private List<string> ReadResourceNames(StoredPart stored)
            => ReadCellResourceDictionary(stored).Keys
                .OrderBy(name => name, StringComparer.Ordinal).ToList();

        private float ReadResourceAmount(StoredPart stored)
        {
            double total = KhemistryFluidCell.GetResourceTotal(
                ReadCellResourceDictionary(stored));
            return total >= float.MaxValue ? float.MaxValue : (float)total;
        }

        private double ReadResourceAmountValue(StoredPart stored, string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return 0.0;
            ReadCellResourceDictionary(stored).TryGetValue(resourceName.Trim(),
                out double amount);
            return KShared.IsFinite(amount) && amount > 0.0 ? amount : 0.0;
        }

        private float ReadMaxAmount(StoredPart stored)
        {
            double capacity = ReadFluidCellPrefab(stored?.partName)
                ?.ResourceMaxAmount ?? 0f;
            if (!KShared.IsFinite(capacity) || capacity <= 0.0) return 0f;
            return capacity >= float.MaxValue ? float.MaxValue : (float)capacity;
        }

        private float ReadTransferDistance(string partName)
            => ReadFluidCellPrefab(partName)?.TransferDistance ?? 10f;

        private HashSet<string> ReadAddableResources(StoredPart stored)
        {
            KhemistryFluidCell prefab = ReadFluidCellPrefab(stored?.partName);
            if (prefab == null) return new HashSet<string>();
            return prefab.GetAddableResources(ReadResourceNames(stored));
        }

        private bool IsResourceAllowedForAddition(StoredPart stored, string resourceName)
        {
            KhemistryFluidCell prefab = ReadFluidCellPrefab(stored?.partName);
            return prefab != null
                && prefab.CanAddResource(resourceName, ReadResourceNames(stored));
        }

        private bool WriteResourceAmount(StoredPart stored, string resourceName,
            double amount)
        {
            if (!IsStoredPartCurrent(stored) || string.IsNullOrWhiteSpace(resourceName)
                || !KShared.IsFinite(amount) || amount < 0.0)
                return false;

            ProtoPartModuleSnapshot module = GetCellModuleSnapshot(stored);
            KhemistryFluidCell prefab = ReadFluidCellPrefab(stored.partName);
            if (module?.moduleValues == null || prefab == null) return false;

            resourceName = resourceName.Trim();
            Dictionary<string, double> resources = ReadCellResourceDictionary(stored);
            resources.TryGetValue(resourceName, out double current);
            double increase = amount - current;
            const double epsilon = 1e-9;
            if (increase > epsilon)
            {
                if (!prefab.CanAddResource(resourceName, resources.Keys)) return false;
                double total = KhemistryFluidCell.GetResourceTotal(resources);
                if (!KShared.IsFinite(total)
                    || increase > Math.Max(0.0,
                        prefab.ResourceMaxAmount - total) + epsilon)
                    return false;
            }

            if (amount <= epsilon) resources.Remove(resourceName);
            else resources[resourceName] = amount;
            module.moduleValues.SetValue("StoredResourcesData",
                KhemistryFluidCell.SerializeResources(resources), true);
            NotifyInventoryChanged();
            return true;
        }

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

        private void ShowPartSelectorForSend(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForSend(); return; }
            if (!IsStoredPartCurrent(cell.stored)) return;

            List<string> resourceNames = ReadResourceNames(cell.stored);
            if (resourceNames.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is empty.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (resourceNames.Count == 1)
            {
                ShowPartSelectorForSend(cell, resourceNames[0]);
                return;
            }

            var labels = new List<string>();
            var resourcesByLabel = new Dictionary<string, string>();
            foreach (string resourceName in resourceNames)
            {
                string label = string.Format("{0}: {1:F2} units", resourceName,
                    ReadResourceAmountValue(cell.stored, resourceName));
                labels.Add(label);
                resourcesByLabel.Add(label, resourceName);
            }
            KShared.Instance.ShowSelector("Which resource to send?", labels, label =>
            {
                if (resourcesByLabel.TryGetValue(label, out string resourceName))
                    ShowPartSelectorForSend(cell, resourceName);
            });
        }

        private void ShowPartSelectorForSend(FluidCellRef cell, string resourceName)
        {
            if (!IsStoredPartCurrent(cell.stored)
                || ReadResourceAmountValue(cell.stored, resourceName) <= 0.0) return;
            float range = ReadTransferDistance(cell.stored.partName);

            var targetParts = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (!CanAcceptResource(pr)) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1} units)",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    AddUniqueOption(targetParts, lbl, p);
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
                    if (!targetParts.TryGetValue(label, out Part target)
                        || !IsStoredPartCurrent(cell.stored)
                        || !IsPartCurrentAndInRange(target, range)) return;
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (!CanAcceptResource(targetResource)) return;

                    // Re-read both sides when the player finally clicks. The selector may have
                    // remained open while a converter or another transfer changed either tank.
                    double available = ReadResourceAmountValue(cell.stored, resourceName);
                    double space = Math.Max(0.0, targetResource.maxAmount - targetResource.amount);
                    double pushed = Math.Min(available, space);
                    if (pushed <= 1e-9) return;
                    if (!WriteResourceAmount(cell.stored, resourceName, available - pushed)) return;
                    targetResource.amount += pushed;
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} units of {1}.", pushed, resourceName),
                        5.0f, ScreenMessageStyle.UPPER_CENTER));
                });
        }

        private void ShowPartSelectorForTake(FluidCellRef cell)
        {
            if (cell.isSuit) { ShowSuitCellPartSelectorForTake(); return; }
            if (!IsStoredPartCurrent(cell.stored)) return;
            KShared.Log("Called!", "KhemistryKerbal/ShowPartSelectorForTake");

            float range = ReadTransferDistance(cell.stored.partName);
            KhemistryFluidCell prefab = ReadFluidCellPrefab(cell.stored.partName);
            HashSet<string> addable = ReadAddableResources(cell.stored);
            bool restrictResources = prefab != null && prefab.HasSupportedResourceGroups;
            if (ReadCellTotalFreeSpace(cell.stored) <= 1e-9)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That fluid cell is full.", 5.0f,
                    ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var optionParts = new Dictionary<string, Part>();
            var optionResources = new Dictionary<string, string>();

            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (!HasUsableAmount(pr)) continue;
                    if (restrictResources && !addable.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F1} units)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    string uniqueLabel = AddUniqueOption(optionParts, lbl, p);
                    optionResources.Add(uniqueLabel, pr.resourceName);
                }

            if (optionParts.Count == 0)
            {
                KShared.Log("No compatible nearby resources with available cell capacity were detected.",
                    "KhemistryKerbal/ShowPartSelectorForTake");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No compatible resources with available cell capacity found within range.",
                    5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            KShared.Log("Calling ShowSelector to take resources from a part.", "KhemistryKerbal/ShowPartSelectorForTake");
            KShared.Instance.ShowSelector("Take resources from...", optionParts.Keys.ToList(), label =>
            {
                if (!optionParts.TryGetValue(label, out Part source)
                    || !optionResources.TryGetValue(label, out string resourceName)
                    || !IsStoredPartCurrent(cell.stored)
                    || !IsPartCurrentAndInRange(source, range)) return;
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                if (!HasUsableAmount(sourceResource)
                    || !IsResourceAllowedForAddition(cell.stored, resourceName)) return;
                double liveSpace = ReadCellTotalFreeSpace(cell.stored);
                double maxTakeValue = Math.Min(sourceResource.amount, liveSpace);
                float maxTake = maxTakeValue >= float.MaxValue
                    ? float.MaxValue : (float)maxTakeValue;
                if (maxTake <= 0f) return;

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        if (!IsStoredPartCurrent(cell.stored)
                            || !IsPartCurrentAndInRange(source, range)) return;
                        PartResource liveSource = source.Resources.Get(def.id);
                        if (!HasUsableAmount(liveSource)
                            || !IsResourceAllowedForAddition(cell.stored, resourceName)
                            || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;
                        double liveCellSpace = ReadCellTotalFreeSpace(cell.stored);
                        double taken = Math.Min(amount, Math.Min(liveSource.amount, liveCellSpace));
                        if (taken <= 1e-9) return;
                        double currentLogicalAmount = ReadResourceAmountValue(
                            cell.stored, resourceName);
                        if (!WriteResourceAmount(cell.stored, resourceName,
                                currentLogicalAmount + taken)) return;
                        liveSource.amount -= taken;
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} units of {1}.", taken, resourceName),
                            5.0f, ScreenMessageStyle.UPPER_CENTER));
                    });
            });
        }
    }
}
