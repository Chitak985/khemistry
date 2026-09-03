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
    public class KhemistryKerbal : PartModule
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is clearly used elsewhere in the code and shouldn't be readonly")]
        private HashSet<string> _evaISRUPartNames = new HashSet<string>();

        private ModuleInventoryPart _inventory;
        private KerbalEVA eva;
        private bool _disabledDuplicate;
        private uint _crewPersistentId;
        private string _crewName = "";
        private bool _boardingEventRegistered;
        private bool _suitPersistenceRestoreChecked;
        private bool _loadedAuthoritativeSuitState;

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

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool HasUsableAmount(PartResource resource)
            => resource != null && IsFinite(resource.amount) && resource.amount > 0.0;

        private static bool HasUsableAmount(ProtoPartResourceSnapshot resource)
            => resource != null && IsFinite(resource.amount) && resource.amount > 0.0;

        private static bool CanAcceptResource(PartResource resource)
            => resource != null && IsFinite(resource.amount) && resource.amount >= 0.0
                && IsFinite(resource.maxAmount) && resource.maxAmount >= 0.0
                && resource.amount < resource.maxAmount;

        private static bool CanAcceptResource(ProtoPartResourceSnapshot resource)
            => resource != null && IsFinite(resource.amount) && resource.amount >= 0.0
                && IsFinite(resource.maxAmount) && resource.maxAmount >= 0.0
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
                || !IsFinite(range) || range < 0.0 || candidate.vessel == null
                || !FlightGlobals.VesselsLoaded.Contains(candidate.vessel)
                || !candidate.vessel.parts.Contains(candidate))
                return false;
            return Vector3.Distance(part.transform.position, candidate.transform.position) <= range;
        }

        private void NotifyInventoryChanged()
        {
            if (_inventory != null)
                GameEvents.onModuleInventoryChanged.Fire(_inventory);
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
            if (!HasFluidSuitCell || string.IsNullOrWhiteSpace(name)
                || double.IsNaN(amount) || double.IsInfinity(amount)) return 0.0;
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
                double total = KhemistryEVACombinedProcessor.GetTotal(dict);
                if (double.IsNaN(total) || double.IsInfinity(total)) return 0.0;
                double spaceLeft = _suitCellMaxAmount - total;
                double add = Math.Min(want, Math.Max(0.0, spaceLeft));
                if (add <= 0.0) return 0.0;
                double combined = current + add;
                if (double.IsNaN(combined) || double.IsInfinity(combined)) return 0.0;
                dict[name] = combined;
                SetSuitCellFromDict(dict);
                return -add;
            }

            return 0.0;
        }

        /// <summary>Current volume used in the material suit cell.</summary>
        private double ComputeMaterialSuitCellVolume(double additional = 0.0)
        {
            foreach (KhemistryMaterialInstance m in materialSuitCellContents)
                if (m != null)
                    additional += m.TotalVolume;
            foreach (ConfigNode pending in _pendingMaterialSuitContents)
            {
                if (!KhemistryMaterialInstance.TryGetSerializedTotalVolume(pending,
                        out double pendingVolume))
                    return double.PositiveInfinity;
                additional += pendingVolume;
                if (double.IsNaN(additional) || double.IsInfinity(additional))
                    return double.PositiveInfinity;
            }
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

            if (mat.amount <= 0 || mat.volume <= 0f || float.IsNaN(mat.volume) || float.IsInfinity(mat.volume))
                return false;

            double capacityTolerance = Math.Max(1e-12, Math.Abs(_materialSuitCellVolume) * 1e-6);
            if (ComputeMaterialSuitCellVolume(mat.TotalVolume)
                > _materialSuitCellVolume + capacityTolerance) return false;

            foreach (KhemistryMaterialInstance existing in materialSuitCellContents)
                if (existing.Merge(mat))
                    return true;

            // Only contaminate after checking every stack for an exact match. Custom material
            // merge equations need not be associative, so contaminating the first compatible
            // stack could otherwise produce a different result solely from inventory order.
            foreach (KhemistryMaterialInstance existing in materialSuitCellContents)
                if (existing.ContaminatedMerge(mat))
                    return true;

            materialSuitCellContents.Add(mat);
            return true;
        }

        internal bool RestoreRemovedMaterialToSuitCell(KhemistryMaterialInstance mat)
        {
            if (mat?.material == null || mat.amount <= 0 || mat.volume <= 0f
                || float.IsNaN(mat.volume) || float.IsInfinity(mat.volume))
                return false;
            foreach (KhemistryMaterialInstance existing in materialSuitCellContents)
                if (existing != null && existing.Merge(mat))
                    return true;
            materialSuitCellContents.Add(mat);
            return true;
        }

        public int GetSuitCellMatchingMaterialAmount(string name, string shape, string size,
            Dictionary<string, string> paramConditions)
        {
            long total = 0;
            foreach (KhemistryMaterialInstance material in materialSuitCellContents)
                if (KhemistryMaterialStorage.MatchesMaterial(material, name, shape, size, paramConditions))
                {
                    total += material.amount;
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            return (int)total;
        }

        public bool TryRemoveMaterialFromSuitCell(string name, string shape, string size,
            Dictionary<string, string> paramConditions, int amount,
            out List<KhemistryMaterialInstance> removed)
        {
            removed = new List<KhemistryMaterialInstance>();
            if (amount <= 0 || GetSuitCellMatchingMaterialAmount(name, shape, size, paramConditions) < amount)
                return false;

            int remaining = amount;
            foreach (KhemistryMaterialInstance stored in materialSuitCellContents.ToList())
            {
                if (!KhemistryMaterialStorage.MatchesMaterial(stored, name, shape, size, paramConditions)) continue;

                int take = Math.Min(remaining, stored.amount);
                KhemistryMaterialInstance piece = new KhemistryMaterialInstance(stored) { amount = take };
                piece.UpdateParams("KhemistryKerbal/TryRemoveMaterialFromSuitCell");
                removed.Add(piece);
                if (take == stored.amount) materialSuitCellContents.Remove(stored);
                else
                {
                    stored.amount -= take;
                    stored.UpdateParams("KhemistryKerbal/TryRemoveMaterialFromSuitCell");
                }

                remaining -= take;
                if (remaining == 0) return true;
            }

            foreach (KhemistryMaterialInstance piece in removed)
                RestoreRemovedMaterialToSuitCell(piece);
            removed.Clear();
            return false;
        }

        private void UpdateMaterialSuitCellDisplay()
        {
            if (!HasMaterialSuitCell) { MaterialCellContentsDisplay = "No material cell"; return; }

            var parts = new List<string>();
            foreach (KhemistryMaterialInstance m in materialSuitCellContents)
                if (m?.material != null && m.amount > 0)
                    parts.Add(m.amount + "× " + m.material.name + " as " + m.shape);

            string contentsStr = parts.Count > 0 ? string.Join(", ", parts) : "Empty";
            double usedVolume = ComputeMaterialSuitCellVolume();
            MaterialCellContentsDisplay = double.IsNaN(usedVolume) || double.IsInfinity(usedVolume)
                ? contentsStr + " (preserved saved material)"
                : string.Format("{0} ({1:F2}/{2:F2})", contentsStr, usedVolume,
                    _materialSuitCellVolume);
        }

        private void LoadConfigFromPartInfo()
        {
            KShared.Log("Called!", "KhemistryKerbal/LoadConfigFromPartInfo");
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

            if (moduleNode.HasNode("EVA_ISRU_PARTS"))
                foreach (string name in moduleNode.GetNode("EVA_ISRU_PARTS").GetValues("name"))
                {
                    string trimmed = name?.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) _evaISRUPartNames.Add(trimmed);
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
                if (suitNode.HasNode("ALLOWED_RESOURCES"))
                    foreach (string n in suitNode.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                    {
                        string trimmed = n?.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) _suitCellAllowedResources.Add(trimmed);
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

            KShared.Log(
                string.Format("Loaded {0} EVA ISRU part names, suitCell={1}.",
                    _evaISRUPartNames.Count, _suitCellMaxAmount > 0f),
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
                KhemistryEVACombinedProcessor.Serialize(GetSuitCellDict()));
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

            suitCellResourcesData = KhemistryEVACombinedProcessor.Serialize(
                KhemistryEVACombinedProcessor.Deserialize(
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

        private void RestoreMaterialSuitContents()
        {
            List<ConfigNode> savedContents = new List<ConfigNode>(_pendingMaterialSuitContents);
            _pendingMaterialSuitContents.Clear();
            foreach (ConfigNode savedNode in savedContents)
            {
                if (!KhemistryMaterialInstance.TryFromConfigNode(savedNode, out KhemistryMaterialInstance material,
                        "KhemistryKerbal/RestoreMaterialSuitContents"))
                {
                    _pendingMaterialSuitContents.Add(savedNode);
                    continue;
                }

                if (!TryAddMaterialToSuitCell(material))
                {
                    KShared.LogError(
                        "Saved suit-cell material \"" + material.material.name + "\" no longer fits or is no longer allowed.",
                        "KhemistryKerbal/RestoreMaterialSuitContents");
                    _pendingMaterialSuitContents.Add(savedNode);
                }
            }
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
            => cell.isSuit ? _suitCellMaxAmount : ReadMaxAmount(cell.stored, ReadResourceName(cell.stored));

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
                    float maxAmount = ReadMaxAmount(cells[i].stored, resName);
                    parts.Add(string.IsNullOrEmpty(resName)
                        ? string.Format("{0}: Empty", label)
                        : string.Format("{0}: {1} {2:F1}/{3:F1} units", label, resName, resAmount, maxAmount));
                }
            }
            CellContentsDisplay = string.Join("  |  ", parts.ToArray());
        }

        public void FixedUpdate()
        {
            if (_disabledDuplicate) return;
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;

            double dt = TimeWarp.fixedDeltaTime;

            foreach (StoredPart storedCell in GetHeldCellSnapshots())
                ApplyHeldBatteryDegradation(storedCell);

            foreach (StoredPart stored in GetProcessorSnapshots())
            {
                if (!IsStoredPartCurrent(stored)) continue;
                KhemistryEVACombinedProcessor prefab = GetPrefabProcessor(stored);
                if (prefab == null || !prefab.IsConfigLoaded) continue;

                bool running = ReadProcessorBool(stored, "isRunning");
                string converterName = ReadProcessorField(stored, "activeConverterName");
                if (!running || string.IsNullOrEmpty(converterName)) continue;

                var resources = DeserializeProcessorResources(stored);
                bool cycled = prefab.RunConversionCycle(resources, converterName, dt);
                if (!WriteProcessorResources(stored, resources)) continue;

                if (!cycled)
                {
                    WriteProcessorField(stored, "isRunning", "False");
                    KShared.Log(
                    "Processor converter \"" + converterName + "\" stopped: insufficient inputs or storage space.",
                        "KhemistryKerbal/FixedUpdate");
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + converterName + "\" stopped: insufficient inputs or storage space.",
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
                if (GetCellModuleSnapshot(stored) != null)
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

        private ProtoPartResourceSnapshot FindCellResource(StoredPart stored, string resourceName = null)
        {
            if (stored?.snapshot?.resources == null) return null;
            HashSet<string> allowed = ReadAllowedResources(stored.partName);
            foreach (ProtoPartResourceSnapshot resource in stored.snapshot.resources)
            {
                if (resource == null) continue;
                if (resourceName != null && resource.resourceName != resourceName) continue;
                if (allowed.Count > 0 && !allowed.Contains(resource.resourceName)) continue;
                return resource;
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
            if (prefab == null || snapshot == null || !IsFinite(prefab.DegradeTime)
                || prefab.DegradeTime <= 0.0) return;

            ProtoPartResourceSnapshot resource = FindCellResource(stored, prefab.ResourceName);
            if (resource == null || !IsFinite(resource.amount) || resource.amount < 0.0
                || !IsFinite(resource.maxAmount) || resource.maxAmount < 0.0) return;

            double now = Planetarium.GetUniversalTime();
            if (!IsFinite(now) || now < 0.0) return;
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

        private string ReadResourceName(StoredPart stored)
        {
            if (TryReadLegacyCellResource(stored, out string legacyName, out _))
                return legacyName;
            if (stored?.snapshot?.resources == null) return "";
            HashSet<string> allowed = ReadAllowedResources(stored.partName);
            foreach (ProtoPartResourceSnapshot resource in stored.snapshot.resources)
                if (resource != null && IsFinite(resource.amount) && resource.amount > 1e-9
                    && (allowed.Count == 0 || allowed.Contains(resource.resourceName)))
                    return resource.resourceName;
            return "";
        }

        private float ReadResourceAmount(StoredPart stored)
        {
            double total = ReadResourceAmountValue(stored);
            return total >= float.MaxValue ? float.MaxValue : (float)total;
        }

        private double ReadResourceAmountValue(StoredPart stored)
        {
            string resourceName = ReadResourceName(stored);
            if (string.IsNullOrEmpty(resourceName)) return 0.0;

            double total = 0.0;
            if (TryReadLegacyCellResource(stored, out string legacyName,
                    out double legacyAmount) && legacyName == resourceName)
                total += legacyAmount;
            ProtoPartResourceSnapshot resource = FindCellResource(stored, resourceName);
            if (HasUsableAmount(resource)) total += resource.amount;
            return IsFinite(total) && total > 0.0 ? total : 0.0;
        }

        private float ReadMaxAmount(StoredPart stored, string resourceName = null)
        {
            if (string.IsNullOrEmpty(resourceName)) resourceName = ReadResourceName(stored);
            ProtoPartResourceSnapshot resource = FindCellResource(stored, resourceName);
            double capacity = resource != null && IsFinite(resource.maxAmount)
                && resource.maxAmount > 0.0 ? resource.maxAmount : 0.0;
            if (TryReadLegacyCellResource(stored, out string legacyName,
                    out double legacyAmount) && legacyName == resourceName)
            {
                // The legacy value is an intentionally preserved overflow remainder. New
                // resource still goes only into the real PartResource tank, so the reachable
                // logical maximum is the remainder plus that tank's capacity.
                capacity += legacyAmount;
            }
            if (capacity <= 0.0 && string.IsNullOrEmpty(resourceName))
                capacity = PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                    .FindModuleImplementing<KhemistryFluidCell>()?.ResourceMaxAmount ?? 0f;
            if (!IsFinite(capacity) || capacity <= 0.0) return 0f;
            return capacity >= float.MaxValue ? float.MaxValue : (float)capacity;
        }

        private float ReadTransferDistance(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.TransferDistance ?? 10f;

        private HashSet<string> ReadAllowedResources(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>()?.AllowedResources
                ?? new HashSet<string>();

        private bool TryReadLegacyCellResource(StoredPart stored,
            out string resourceName, out double amount)
        {
            resourceName = "";
            amount = 0.0;
            ProtoPartModuleSnapshot module = GetCellModuleSnapshot(stored);
            if (module?.moduleValues == null) return false;

            string savedName = module.moduleValues.GetValue("ResourceName")?.Trim();
            if (string.IsNullOrEmpty(savedName)
                || !double.TryParse(module.moduleValues.GetValue("ResourceAmount"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double savedAmount)
                || !IsFinite(savedAmount) || savedAmount <= 1e-9)
                return false;

            HashSet<string> allowed = ReadAllowedResources(stored.partName);
            if (allowed.Count > 0 && !allowed.Contains(savedName)) return false;
            resourceName = savedName;
            amount = savedAmount;
            return true;
        }

        private bool WriteResourceAmount(StoredPart stored, string resourceName, double amount)
        {
            if (!IsStoredPartCurrent(stored) || string.IsNullOrWhiteSpace(resourceName)
                || !IsFinite(amount) || amount < 0.0)
                return false;

            resourceName = resourceName.Trim();
            HashSet<string> allowed = ReadAllowedResources(stored.partName);
            if (allowed.Count > 0 && !allowed.Contains(resourceName)) return false;

            ProtoPartResourceSnapshot resource = FindCellResource(stored, resourceName);
            if (resource != null && (!IsFinite(resource.amount) || resource.amount < 0.0
                || !IsFinite(resource.maxAmount) || resource.maxAmount < 0.0))
                return false;

            ProtoPartModuleSnapshot module = GetCellModuleSnapshot(stored);
            bool hasLegacy = TryReadLegacyCellResource(stored, out string legacyName,
                out double legacyAmount);
            if (hasLegacy && legacyName != resourceName)
                return false;

            double canonicalAmount = resource?.amount ?? 0.0;
            double currentTotal = canonicalAmount + (hasLegacy ? legacyAmount : 0.0);
            if (!IsFinite(currentTotal)) return false;

            const double epsilon = 1e-9;
            double requestedIncrease = amount - currentTotal;
            double newCanonical = canonicalAmount;
            double newLegacy = hasLegacy ? legacyAmount : 0.0;
            if (requestedIncrease > epsilon)
            {
                // Never add to the obsolete parallel field. It exists only so an old save's
                // overflow can be drained without data loss.
                if (resource == null) return false;
                double freeSpace = Math.Max(0.0, resource.maxAmount - canonicalAmount);
                if (requestedIncrease > freeSpace + epsilon) return false;
                newCanonical = canonicalAmount + requestedIncrease;
            }
            else
            {
                double reduction = Math.Max(0.0, currentTotal - amount);
                double legacyReduction = Math.Min(newLegacy, reduction);
                newLegacy -= legacyReduction;
                reduction -= legacyReduction;
                if (reduction > newCanonical + epsilon) return false;
                newCanonical = Math.Max(0.0, newCanonical - reduction);
            }

            bool changed = false;
            if (resource != null && Math.Abs(resource.amount - newCanonical) > epsilon)
            {
                resource.amount = newCanonical;
                changed = true;
            }
            if (hasLegacy && module?.moduleValues != null
                && Math.Abs(legacyAmount - newLegacy) > epsilon)
            {
                module.moduleValues.SetValue("ResourceAmount",
                    newLegacy.ToString("R", CultureInfo.InvariantCulture), true);
                if (newLegacy <= epsilon)
                    module.moduleValues.SetValue("ResourceName", "", true);
                changed = true;
            }
            if (changed) NotifyInventoryChanged();
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
                        float maxAmount = ReadMaxAmount(cells[i].stored, resName);
                        labels.Add(string.IsNullOrEmpty(resName)
                            ? string.Format("{0}: Empty", cellLabel)
                            : string.Format("{0}: {1} {2:F1}/{3:F1} units", cellLabel, resName, resAmount, maxAmount));
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
            if (!IsStoredPartCurrent(cell.stored)) return;

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
                    if (!CanAcceptResource(targetResource)
                        || ReadResourceName(cell.stored) != resourceName) return;

                    // Re-read both sides when the player finally clicks. The selector may have
                    // remained open while a converter or another transfer changed either tank.
                    double available = ReadResourceAmountValue(cell.stored);
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
                        : string.Format("{0}: {1} {2:F1}/{3:F1} units", cellLabel, resName, resAmount, maxAmount));
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
            if (!IsStoredPartCurrent(cell.stored)) return;
            KShared.Log("Called!", "KhemistryKerbal/ShowPartSelectorForTake");

            string currentResource = ReadResourceName(cell.stored);
            float currentAmount = ReadResourceAmount(cell.stored);
            float range = ReadTransferDistance(cell.stored.partName);
            HashSet<string> allowed = ReadAllowedResources(cell.stored.partName);

            if (!string.IsNullOrEmpty(currentResource)
                && currentAmount >= ReadMaxAmount(cell.stored, currentResource))
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "That cell is full.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var optionParts = new Dictionary<string, Part>();
            var optionResources = new Dictionary<string, string>();

            foreach (Part p in GetPartsInRange(range))
                foreach (PartResource pr in p.Resources)
                {
                    if (!HasUsableAmount(pr)) continue;
                    if (!string.IsNullOrEmpty(currentResource) && pr.resourceName != currentResource) continue;
                    if (string.IsNullOrEmpty(currentResource) && allowed.Count > 0
                        && !allowed.Contains(pr.resourceName)) continue;
                    ProtoPartResourceSnapshot cellResource = FindCellResource(cell.stored, pr.resourceName);
                    if (!CanAcceptResource(cellResource)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F1} units)",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    string uniqueLabel = AddUniqueOption(optionParts, lbl, p);
                    optionResources.Add(uniqueLabel, pr.resourceName);
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
                if (!optionParts.TryGetValue(label, out Part source)
                    || !optionResources.TryGetValue(label, out string resourceName)
                    || !IsStoredPartCurrent(cell.stored)
                    || !IsPartCurrentAndInRange(source, range)) return;
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = source.Resources.Get(def.id);
                ProtoPartResourceSnapshot cellResource = FindCellResource(cell.stored, resourceName);
                if (!HasUsableAmount(sourceResource) || !CanAcceptResource(cellResource)) return;
                double liveSpace = Math.Max(0.0, cellResource.maxAmount - cellResource.amount);
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
                        ProtoPartResourceSnapshot liveCell = FindCellResource(cell.stored, resourceName);
                        if (!HasUsableAmount(liveSource) || !CanAcceptResource(liveCell)
                            || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;
                        double liveCellSpace = Math.Max(0.0, liveCell.maxAmount - liveCell.amount);
                        double taken = Math.Min(amount, Math.Min(liveSource.amount, liveCellSpace));
                        if (taken <= 1e-9) return;
                        double currentLogicalAmount = ReadResourceAmountValue(cell.stored);
                        string liveResourceName = ReadResourceName(cell.stored);
                        if ((!string.IsNullOrEmpty(liveResourceName)
                                && liveResourceName != resourceName)
                            || !WriteResourceAmount(cell.stored, resourceName,
                                currentLogicalAmount + taken)) return;
                        liveSource.amount -= taken;
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            string.Format("Received {0:F2} units of {1}.", taken, resourceName),
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
                    if (!HasUsableAmount(pr)) continue;
                    if (_suitCellAllowedResources.Count > 0
                        && !_suitCellAllowedResources.Contains(pr.resourceName)) continue;
                    string lbl = string.Format("{0} / {1}  ({2}: {3:F2})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    AddUniqueOption(options, lbl, (p, pr));
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
                if (!options.TryGetValue(label, out var selection)) return;
                var (sourcePart, sourceResource) = selection;
                string resourceName = sourceResource.resourceName;
                if (!IsPartCurrentAndInRange(sourcePart, _suitCellTransferDistance)
                    || !HasUsableAmount(sourceResource) || !IsFinite(spaceRemaining)
                    || spaceRemaining <= 0.0) return;
                double maxTakeValue = Math.Min(sourceResource.amount, spaceRemaining);
                float maxTake = maxTakeValue >= float.MaxValue
                    ? float.MaxValue : (float)maxTakeValue;

                KShared.Log("Calling ShowAmountSelector to get exact amount.", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
                KShared.Instance.ShowAmountSelector(
                    string.Format("How much {0} to take?", resourceName),
                    0f, maxTake, maxTake, amount =>
                    {
                        if (!IsPartCurrentAndInRange(sourcePart,
                                _suitCellTransferDistance)) return;
                        var liveDict = GetSuitCellDict();
                        double liveSpace = Math.Max(0.0,
                            _suitCellMaxAmount - KhemistryEVACombinedProcessor.GetTotal(liveDict));
                        var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                        PartResource liveSource = def == null ? null : sourcePart.Resources.Get(def.id);
                        if (!HasUsableAmount(liveSource) || float.IsNaN(amount)
                            || float.IsInfinity(amount) || amount <= 0f) return;
                        double taken = Math.Min((double)amount,
                            Math.Min(liveSource.amount, liveSpace));
                        if (taken <= 1e-9) return;
                        liveDict.TryGetValue(resourceName, out double existing);
                        liveDict[resourceName] = existing + taken;
                        SetSuitCellFromDict(liveDict);
                        liveSource.amount -= taken;
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
            bool migratedSnapshot = false;
            for (int i = 0; i < _inventory.storedParts.Count; i++)
            {
                StoredPart stored = _inventory.storedParts.At(i);
                AvailablePart ap = PartLoader.getPartInfoByName(stored.partName);
                KhemistryEVACombinedProcessor prefab = ap?.partPrefab
                    .FindModuleImplementing<KhemistryEVACombinedProcessor>();
                if (prefab == null || stored?.snapshot?.modules == null) continue;
                if (GetProcessorSnapshot(stored) == null)
                {
                    // Some older inventory snapshots predate the module. Seed a normal module
                    // snapshot from the current prefab before exposing the item as operable.
                    stored.snapshot.modules.Add(new ProtoPartModuleSnapshot(prefab));
                    migratedSnapshot = true;
                }
                result.Add(stored);
            }
            if (migratedSnapshot) NotifyInventoryChanged();
            return result;
        }

        private KhemistryEVACombinedProcessor GetPrefabProcessor(StoredPart stored)
            => PartLoader.getPartInfoByName(stored.partName)?.partPrefab
                .FindModuleImplementing<KhemistryEVACombinedProcessor>();

        private ProtoPartModuleSnapshot GetProcessorSnapshot(StoredPart stored)
        {
            if (stored?.snapshot?.modules == null) return null;
            foreach (ProtoPartModuleSnapshot snap in stored.snapshot.modules)
                if (snap.moduleName == "KhemistryEVACombinedProcessor") return snap;
            return null;
        }

        private string ReadProcessorField(StoredPart stored, string key)
            => GetProcessorSnapshot(stored)?.moduleValues.GetValue(key) ?? "";

        private bool WriteProcessorField(StoredPart stored, string key, string value)
        {
            if (!IsStoredPartCurrent(stored) || string.IsNullOrEmpty(key)) return false;
            ProtoPartModuleSnapshot snapshot = GetProcessorSnapshot(stored);
            if (snapshot?.moduleValues == null) return false;
            value = value ?? "";
            if (snapshot.moduleValues.GetValue(key) == value) return true;
            snapshot.moduleValues.SetValue(key, value, true);
            NotifyInventoryChanged();
            return true;
        }

        private bool ReadProcessorBool(StoredPart stored, string key)
        {
            return bool.TryParse(ReadProcessorField(stored, key), out bool result) && result;
        }

        private Dictionary<string, double> DeserializeProcessorResources(StoredPart stored)
            => KhemistryEVACombinedProcessor.Deserialize(ReadProcessorField(stored, "storedResourcesData"));

        private bool WriteProcessorResources(StoredPart stored, Dictionary<string, double> resources)
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
                labels.Add(MakeUniqueLabel(labels, name + suffix));
            }

            shared.ShowSelector("Select processor", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0 && IsStoredPartCurrent(processors[idx]))
                    ShowProcessorActionMenu(processors[idx]);
            });
        }

        private void ShowProcessorActionMenu(StoredPart stored)
        {
            if (!IsStoredPartCurrent(stored)) return;
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
                action =>
                {
                    if (IsStoredPartCurrent(stored))
                        ExecuteProcessorAction(stored, prefab, action);
                });
        }

        private void ExecuteProcessorAction(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string action)
        {
            var shared = KShared.Instance;
            if (shared == null || !IsStoredPartCurrent(stored)) return;

            switch (action)
            {
                case "Start Converter":
                    {
                        if (prefab.Converters.Count == 1)
                        {
                            if (!WriteProcessorField(stored, "activeConverterName",
                                    prefab.Converters[0].name)
                                || !WriteProcessorField(stored, "isRunning", "True")) return;
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
                                if (!IsStoredPartCurrent(stored) || !names.Contains(name)) return;
                                if (!WriteProcessorField(stored, "activeConverterName", name)
                                    || !WriteProcessorField(stored, "isRunning", "True")) return;
                                ScreenMessages.PostScreenMessage(new ScreenMessage(
                                    "Converter \"" + name + "\" started.", 4f, ScreenMessageStyle.UPPER_CENTER));
                            });
                        }
                        break;
                    }
                case "Stop Converter":
                    if (!WriteProcessorField(stored, "isRunning", "False")) return;
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
            if (!IsStoredPartCurrent(stored)) return;
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
                    if (!HasUsableAmount(pr)) continue;
                    string label = string.Format("{0} / {1}  ({2}: {3:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.resourceName, pr.amount);
                    AddUniqueOption(options, label, (p, pr.resourceName));
                }

            if (options.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No nearby parts have supported resources.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Take from...", new List<string>(options.Keys), label =>
            {
                if (!options.TryGetValue(label, out var selection)
                    || !IsStoredPartCurrent(stored)) return;
                var (sourcePart, resourceName) = selection;
                if (!IsPartCurrentAndInRange(sourcePart, prefab.TransferDistance)) return;
                var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (def == null) return;
                PartResource sourceResource = sourcePart.Resources.Get(def.id);
                if (!HasUsableAmount(sourceResource)) return;

                var res = DeserializeProcessorResources(stored);
                double liveSpace = Math.Max(0.0, prefab.MaxTotalStorage
                    - KhemistryEVACombinedProcessor.GetTotal(res));
                double taken = Math.Min(sourceResource.amount, liveSpace);
                if (taken <= 1e-9) return;
                res.TryGetValue(resourceName, out double existing);
                res[resourceName] = existing + taken;
                if (!WriteProcessorResources(stored, res)) return;
                sourceResource.amount -= taken;

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
            if (!IsStoredPartCurrent(stored)) return;
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
                string only = "";
                foreach (var kvp in resources) { only = kvp.Key; }
                ShowProcessorTransferOutTargets(stored, prefab, only);
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
                if (idx >= 0 && IsStoredPartCurrent(stored))
                    ShowProcessorTransferOutTargets(stored, prefab, resKeys[idx]);
            });
        }

        private void ShowProcessorTransferOutTargets(StoredPart stored,
            KhemistryEVACombinedProcessor prefab, string resourceName)
        {
            if (!IsStoredPartCurrent(stored)) return;
            var shared = KShared.Instance;
            var options = new Dictionary<string, Part>();

            foreach (Part p in GetPartsInRange(prefab.TransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (!CanAcceptResource(pr)) continue;
                    string label = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    AddUniqueOption(options, label, p);
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
                    if (!options.TryGetValue(label, out Part target)
                        || !IsStoredPartCurrent(stored)
                        || !IsPartCurrentAndInRange(target, prefab.TransferDistance)) return;
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (!CanAcceptResource(targetResource)) return;

                    var res = DeserializeProcessorResources(stored);
                    res.TryGetValue(resourceName, out double available);
                    double space = Math.Max(0.0, targetResource.maxAmount - targetResource.amount);
                    double pushed = Math.Min(available, space);
                    if (pushed <= 1e-9) return;

                    double remaining = available - pushed;
                    if (remaining < 1e-9) res.Remove(resourceName);
                    else res[resourceName] = remaining;
                    if (!WriteProcessorResources(stored, res)) return;
                    targetResource.amount += pushed;

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
                foreach (var kvp in dict) { ShowSuitCellSendTargets(kvp.Key); return; }
            }

            var labels = new List<string>();
            var keys = new List<string>();
            foreach (var kvp in dict)
            {
                labels.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                keys.Add(kvp.Key);
            }

            KShared.Instance.ShowSelector("Which resource to send?", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx >= 0) ShowSuitCellSendTargets(keys[idx]);
            });
        }

        private void ShowSuitCellSendTargets(string resourceName)
        {
            var options = new Dictionary<string, Part>();
            foreach (Part p in GetPartsInRange(_suitCellTransferDistance))
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName != resourceName) continue;
                    if (!CanAcceptResource(pr)) continue;
                    string lbl = string.Format("{0} / {1}  (space: {2:F1})",
                        p.vessel.vesselName, p.partInfo.title, pr.maxAmount - pr.amount);
                    AddUniqueOption(options, lbl, p);
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
                    if (!options.TryGetValue(label, out Part target)
                        || !IsPartCurrentAndInRange(target,
                            _suitCellTransferDistance)) return;
                    var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                    if (def == null) return;
                    PartResource targetResource = target.Resources.Get(def.id);
                    if (!CanAcceptResource(targetResource)) return;
                    var d = GetSuitCellDict();
                    d.TryGetValue(resourceName, out double existing);
                    double space = Math.Max(0.0, targetResource.maxAmount - targetResource.amount);
                    double pushed = Math.Min(existing, space);
                    if (pushed <= 1e-9) return;
                    double remaining = existing - pushed;
                    if (remaining < 1e-9) d.Remove(resourceName);
                    else d[resourceName] = remaining;
                    SetSuitCellFromDict(d);
                    targetResource.amount += pushed;
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        string.Format("Transferred {0:F2} of {1}.", pushed, resourceName),
                        5f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
    }
}
