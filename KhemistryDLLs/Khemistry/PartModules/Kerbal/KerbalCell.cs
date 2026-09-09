using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryKerbal
    {
        private HashSet<string> GetSuitCellAddableResources(
            IEnumerable<string> storedResources)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (_suitCellSupportedResourceGroups.Count == 0) return result;
            var stored = new HashSet<string>(StringComparer.Ordinal);
            if (storedResources != null)
                foreach (string value in storedResources)
                    if (!string.IsNullOrWhiteSpace(value)) stored.Add(value.Trim());
            foreach (HashSet<string> group in _suitCellSupportedResourceGroups)
                if (group.IsSupersetOf(stored)) result.UnionWith(group);
            return result;
        }

        private bool CanAddToSuitCell(string resourceName,
            IEnumerable<string> storedResources)
            => !string.IsNullOrWhiteSpace(resourceName)
                && (_suitCellSupportedResourceGroups.Count == 0
                    || GetSuitCellAddableResources(storedResources)
                        .Contains(resourceName.Trim()));

        private Dictionary<string, double> GetSuitCellDict()
            => DeserializeResourceDictionary(suitCellResourcesData);

        private void SetSuitCellFromDict(Dictionary<string, double> dict)
            => suitCellResourcesData = SerializeResourceDictionary(dict);

        public bool HasFluidSuitCell => _suitCellMaxAmount > 0f;

        /// <summary>
        /// Requests (positive amount) or produces (negative amount) a resource directly against
        /// this kerbal's fluid suit cell, for use by a kerbalEVA-mode KhemistryISRU. Same
        /// amount/return contract as Part.RequestResource: returns the amount actually removed
        /// (consume) or the negative of the amount actually added (produce). Respects the
        /// compatible SUPPORTED_RESOURCES groups and available suit cell capacity.
        /// </summary>
        public double RequestSuitCellResource(string name, double amount)
        {
            if (!HasFluidSuitCell || string.IsNullOrWhiteSpace(name)
                || double.IsNaN(amount) || double.IsInfinity(amount)) return 0.0;
            var dict = GetSuitCellDict();
            name = name.Trim();
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
                if (!CanAddToSuitCell(name, dict.Keys)) return 0.0;
                double want = -amount;
                double total = GetResourceDictionaryTotal(dict);
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
            return string.Join(", ", ReadResourceNames(cell.stored).ToArray());
        }

        private float ReadCellResourceAmount(FluidCellRef cell)
        {
            if (cell.isSuit)
                return (float)GetResourceDictionaryTotal(GetSuitCellDict());
            return ReadResourceAmount(cell.stored);
        }

        private float ReadCellMaxAmount(FluidCellRef cell)
        {
            if (cell.isSuit) return _suitCellMaxAmount;
            return ReadMaxAmount(cell.stored);
        }

        private string DescribeStoredCell(StoredPart stored)
        {
            List<string> names = ReadResourceNames(stored);
            if (names.Count == 0) return "Empty";
            var contents = new List<string>();
            foreach (string name in names)
                contents.Add(string.Format("{0}: {1:F2}", name,
                    ReadResourceAmountValue(stored, name)));
            return string.Format("{0} ({1:F2}/{2:F2})",
                string.Join(", ", contents.ToArray()), ReadResourceAmount(stored),
                ReadMaxAmount(stored));
        }

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
                    double total = GetResourceDictionaryTotal(dict);
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
                    parts.Add(string.Format("{0}: {1}", label,
                        DescribeStoredCell(cells[i].stored)));
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
                if (GetCellModuleSnapshot(stored) != null)
                    result.Add(stored);
            }
            return result;
        }

        private ProtoPartModuleSnapshot GetCellModuleSnapshot(StoredPart stored)
        {
            if (stored?.snapshot?.modules == null) return null;
            foreach (ProtoPartModuleSnapshot moduleSnap in stored.snapshot.modules)
                if (moduleSnap.moduleName == "KhemistryFluidCell") return moduleSnap;
            return null;
        }

        private Dictionary<string, double> ReadCellResourceDictionary(StoredPart stored)
        {
            ProtoPartModuleSnapshot module = GetCellModuleSnapshot(stored);
            if (module?.moduleValues == null)
                return new Dictionary<string, double>(StringComparer.Ordinal);

            string savedData = module.moduleValues.GetValue("StoredResourcesData") ?? "";
            Dictionary<string, double> resources =
                KhemistryFluidCell.DeserializeResources(savedData);
            string normalized = KhemistryFluidCell.SerializeResources(resources);
            bool changed = normalized != savedData;

            // Migrate the obsolete single-resource module fields into the dictionary. Real
            // ProtoPartResourceSnapshots are intentionally ignored: they are separate tanks.
            string legacyName = module.moduleValues.GetValue("ResourceName")?.Trim();
            if (!string.IsNullOrEmpty(legacyName)
                && double.TryParse(module.moduleValues.GetValue("ResourceAmount"),
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double legacyAmount)
                && KShared.IsFinite(legacyAmount) && legacyAmount > 1e-9)
            {
                KhemistryFluidCell prefab = ReadFluidCellPrefab(stored.partName);
                if (prefab != null && prefab.CanAddResource(legacyName, resources.Keys))
                {
                    double free = Math.Max(0.0,
                        prefab.ResourceMaxAmount
                        - KhemistryFluidCell.GetResourceTotal(resources));
                    double moved = Math.Min(legacyAmount, free);
                    if (moved > 0.0)
                    {
                        resources.TryGetValue(legacyName, out double current);
                        resources[legacyName] = current + moved;
                        double remainder = legacyAmount - moved;
                        module.moduleValues.SetValue("ResourceAmount",
                            remainder.ToString("R", CultureInfo.InvariantCulture), true);
                        if (remainder <= 1e-9)
                            module.moduleValues.SetValue("ResourceName", "", true);
                        normalized = KhemistryFluidCell.SerializeResources(resources);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                module.moduleValues.SetValue("StoredResourcesData", normalized, true);
                NotifyInventoryChanged();
            }
            return resources;
        }

        private double ReadCellTotalFreeSpace(StoredPart stored)
        {
            KhemistryFluidCell cell = ReadFluidCellPrefab(stored?.partName);
            if (cell == null || !KShared.IsFinite(cell.ResourceMaxAmount)
                || cell.ResourceMaxAmount <= 0f) return 0.0;
            return Math.Max(0.0, cell.ResourceMaxAmount - ReadResourceAmount(stored));
        }

        private KhemistryFluidCell ReadFluidCellPrefab(string partName)
            => PartLoader.getPartInfoByName(partName)?.partPrefab
                .FindModuleImplementing<KhemistryFluidCell>();

        private void ShowSuitCellPartSelectorForTake()
        {
            KShared.Log("Called!", "KhemistryKerbal/ShowSuitCellPartSelectorForTake");
            var dict = GetSuitCellDict();
            double currentTotal = GetResourceDictionaryTotal(dict);
            double spaceRemaining = _suitCellMaxAmount - currentTotal;
            HashSet<string> addable = GetSuitCellAddableResources(dict.Keys);
            bool restrictResources = _suitCellSupportedResourceGroups.Count > 0;

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
                    if (restrictResources && !addable.Contains(pr.resourceName)) continue;
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
                    || !HasUsableAmount(sourceResource) || !KShared.IsFinite(spaceRemaining)
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
                            _suitCellMaxAmount - GetResourceDictionaryTotal(liveDict));
                        var def = PartResourceLibrary.Instance.GetDefinition(resourceName);
                        PartResource liveSource = def == null ? null : sourcePart.Resources.Get(def.id);
                        if (!HasUsableAmount(liveSource) || float.IsNaN(amount)
                            || float.IsInfinity(amount) || amount <= 0f
                            || !CanAddToSuitCell(resourceName, liveDict.Keys)) return;
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
