using System;
using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    /// <summary>
    /// A PartModule that stores <see cref="KhemistryMaterialInstance"/> and merges them as needed.
    /// Uses a completely different resource system than stock KSP.
    /// </summary>
    public class KhemistryMaterialStorage : PartModule
    {
        [KSPField(isPersistant = false)]
        public float volume = 1f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistrymatstorage",
                  groupDisplayName = "Khemistry Material Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistrymatstorage")]
        public string volumeDisplay = "0 / 0";

        public List<string> supportedNames = new List<string>();
        public List<string> supportedShapes = new List<string>();
        public Dictionary<string, string> paramRequirements = new Dictionary<string, string>();

        public List<KhemistryMaterialInstance> contents = new List<KhemistryMaterialInstance>();
        private bool _fatalConfigError = false;
        private readonly List<ConfigNode> _pendingSavedContents = new List<ConfigNode>();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            contents.Clear();
            _pendingSavedContents.Clear();
            foreach (ConfigNode savedNode in node.GetNodes("STORED_MATERIAL"))
            {
                ConfigNode copy = new ConfigNode("STORED_MATERIAL");
                savedNode.CopyTo(copy);
                _pendingSavedContents.Add(copy);
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            while (node.HasNode("STORED_MATERIAL")) node.RemoveNode("STORED_MATERIAL");

            // OnSave can run before OnStart (and a bad future config can prevent a
            // saved stack from being restored). Keep those opaque nodes verbatim so
            // a temporary configuration problem never erases a player's materials.
            foreach (ConfigNode pendingNode in _pendingSavedContents)
            {
                ConfigNode copy = new ConfigNode("STORED_MATERIAL");
                pendingNode.CopyTo(copy);
                node.AddNode(copy);
            }

            foreach (KhemistryMaterialInstance material in contents)
                if (material != null && material.amount > 0)
                    node.AddNode(material.ToConfigNode());
        }

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

            RestoreSavedContents();
        }

        private void RestoreSavedContents()
        {
            List<ConfigNode> savedContents = new List<ConfigNode>(_pendingSavedContents);
            _pendingSavedContents.Clear();
            foreach (ConfigNode savedNode in savedContents)
            {
                if (!KhemistryMaterialInstance.TryFromConfigNode(savedNode, out KhemistryMaterialInstance material,
                        "KhemistryMaterialStorage/RestoreSavedContents"))
                {
                    _pendingSavedContents.Add(savedNode);
                    continue;
                }

                if (!AddMaterial(material))
                {
                    KShared.LogError(
                        "Saved material \"" + material.material.name + "\" no longer fits or is no longer supported by part \"" + part.name + "\".",
                        "KhemistryMaterialStorage/RestoreSavedContents");
                    _pendingSavedContents.Add(savedNode);
                }
            }
        }

        public void FixedUpdate()
        {
            // Apply tank-caused contamination to all materials on the part
            // !TODO

            contents.RemoveAll(material => material == null || material.amount <= 0);

            // Complete a separate exact pass before applying any contamination formula. A
            // one-pass Merge || ContaminatedMerge loop could contaminate the first stack before
            // reaching a later exact match, making non-associative custom formulas list-order
            // dependent even when exact consolidation was possible.
            for (int i = 0; i < contents.Count; i++)
            {
                for (int j = i + 1; j < contents.Count;)
                {
                    if (contents[i].Merge(contents[j]))
                        contents.RemoveAt(j);
                    else
                        j++;
                }
            }

            // Contaminate only after every original exact stack has been consolidated. Try an
            // exact merge again because an earlier contamination result can legitimately become
            // identical to a later stack.
            for (int i = 0; i < contents.Count; i++)
            {
                for (int j = i + 1; j < contents.Count;)
                {
                    KhemistryMaterialInstance receiver = contents[i];
                    KhemistryMaterialInstance incoming = contents[j];
                    bool exactMerge = receiver.Merge(incoming);
                    if (exactMerge || receiver.ContaminatedMerge(incoming))
                    {
                        if (!exactMerge)
                            KShared.Log(
                                $"Instance of {incoming.material.name} was contamination-merged into {receiver.material.name}.",
                                "KhemistryMaterialStorage/FixedUpdate/contaminatedMergeLogic"
                            );

                        contents.RemoveAt(j);
                    }
                    else j++;
                }
            }

            // Reflect merges in the same physics update instead of displaying stale contents.
            UpdateUI();
        }

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.LogError("partInfo.partConfig is null!",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = KShared.FindModuleConfigNode(this,
                "KhemistryMaterialStorage");

            if (moduleNode == null)
            {
                KShared.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume <= 0f)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has an invalid KhemistryMaterialStorage volume. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            supportedNames.Clear();
            if (!moduleNode.HasNode("SUPPORTED_NAMES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage but no SUPPORTED_NAMES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_NAMES").GetValues("name"))
            {
                string trimmed = n?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !supportedNames.Contains(trimmed))
                    supportedNames.Add(trimmed);
            }
            if (supportedNames.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty SUPPORTED_NAMES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            supportedShapes.Clear();
            if (!moduleNode.HasNode("SUPPORTED_SHAPES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage but no SUPPORTED_SHAPES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_SHAPES").GetValues("name"))
            {
                string trimmed = n?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !supportedShapes.Contains(trimmed))
                    supportedShapes.Add(trimmed);
            }
            if (supportedShapes.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty SUPPORTED_SHAPES node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            paramRequirements.Clear();
            if (!moduleNode.HasNode("PARAM_REQUIREMENTS")) return;

            paramRequirements = KShared.NodeToDictionary(moduleNode.GetNode("PARAM_REQUIREMENTS"));
            if (paramRequirements.Keys.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryMaterialStorage with an empty PARAM_REQUIREMENTS node. This module will not load.",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
        }

        /// <summary>
        /// Add a material instance to storage.
        /// If the material is already present, it will be merged with the existing one.
        /// </summary>
        /// <param name="mat">The material instance to add to storage.</param>
        /// <returns>Whether the material met the storage restrictions and there was enough space.</returns>
        public bool AddMaterial(KhemistryMaterialInstance mat)
        {
            if (_fatalConfigError || !AcceptsMaterial(mat))
                return false;

            double incomingVolume = mat.TotalVolume;
            double capacityTolerance = Math.Max(1e-12, Math.Abs(volume) * 1e-6);
            if (double.IsNaN(incomingVolume) || double.IsInfinity(incomingVolume)
                || ComputeCurrentVolume(incomingVolume) > volume + capacityTolerance)
                return false;

            foreach (KhemistryMaterialInstance m in contents)
                if (m.Merge(mat))
                    return true;

            contents.Add(mat);
            return true;
        }

        /// <summary>
        /// Restores a piece removed by an in-progress transaction. The piece was already accepted
        /// and occupied capacity in this exact container, so reapplying current restrictions could
        /// lose it when an amount-dependent derived parameter changed during the split.
        /// </summary>
        internal bool RestoreRemovedMaterial(KhemistryMaterialInstance mat)
        {
            if (mat?.material == null || mat.amount <= 0 || mat.volume <= 0f
                || float.IsNaN(mat.volume) || float.IsInfinity(mat.volume))
                return false;
            foreach (KhemistryMaterialInstance existing in contents)
                if (existing != null && existing.Merge(mat))
                    return true;
            contents.Add(mat);
            return true;
        }

        private bool AcceptsMaterial(KhemistryMaterialInstance mat)
        {
            if (mat?.material == null || mat.amount <= 0
                || float.IsNaN(mat.volume) || float.IsInfinity(mat.volume) || mat.volume <= 0f)
                return false;

            if (!supportedNames.Contains(mat.material.name) || !supportedShapes.Contains(mat.shape))
                return false;

            mat.UpdateParams("KhemistryMaterialStorage/AcceptsMaterial");
            foreach (KeyValuePair<string, string> requirement in paramRequirements)
            {
                if (!mat.parameters.TryGetValue(requirement.Key, out string value)
                    || !KShared.EvaluateParamComparison(value, requirement.Value))
                    return false;
            }
            return true;
        }

        public static bool MatchesMaterial(KhemistryMaterialInstance material, string name, string shape,
            string size, Dictionary<string, string> paramConditions)
        {
            if (material?.material == null || material.material.name != name
                || material.shape != shape || material.size != size)
                return false;

            material.UpdateParams("KhemistryMaterialStorage/MatchesMaterial");
            foreach (KeyValuePair<string, string> condition in
                     paramConditions ?? new Dictionary<string, string>())
            {
                if (!material.parameters.TryGetValue(condition.Key, out string value)
                    || !KShared.EvaluateParamComparison(value, condition.Value))
                    return false;
            }
            return true;
        }

        public int GetMatchingMaterialAmount(string name, string shape, string size,
            Dictionary<string, string> paramConditions)
        {
            long total = 0;
            foreach (KhemistryMaterialInstance material in contents)
                if (MatchesMaterial(material, name, shape, size, paramConditions))
                {
                    total += material.amount;
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            return (int)total;
        }

        /// <summary>
        /// Removes a requirement atomically, combining matching stacks within this storage.
        /// The exact removed instances are returned so a caller can roll the transaction back.
        /// </summary>
        public bool TryRemoveMaterial(string name, string shape, string size,
            Dictionary<string, string> paramConditions, int amount,
            out List<KhemistryMaterialInstance> removed)
        {
            removed = new List<KhemistryMaterialInstance>();
            if (amount <= 0 || GetMatchingMaterialAmount(name, shape, size, paramConditions) < amount)
                return false;

            int remaining = amount;
            foreach (KhemistryMaterialInstance stored in contents.ToList())
            {
                if (!MatchesMaterial(stored, name, shape, size, paramConditions)) continue;

                int take = Math.Min(remaining, stored.amount);
                KhemistryMaterialInstance piece = new KhemistryMaterialInstance(stored) { amount = take };
                piece.UpdateParams("KhemistryMaterialStorage/TryRemoveMaterial");
                removed.Add(piece);

                if (take == stored.amount) contents.Remove(stored);
                else
                {
                    stored.amount -= take;
                    stored.UpdateParams("KhemistryMaterialStorage/TryRemoveMaterial");
                }

                remaining -= take;
                if (remaining == 0) return true;
            }

            foreach (KhemistryMaterialInstance piece in removed) RestoreRemovedMaterial(piece);
            removed.Clear();
            return false;
        }

        /// <summary>
        /// Compute the current volume taken up by the contents of this storage.
        /// Can accept a value to add to the total volume, usually used to check if adding a new material would exceed capacity.
        /// </summary>
        /// <param name="usedVolume">An additional amount to add to the volume being used.</param>
        /// <returns>How much volume is used.</returns>
        private double ComputeCurrentVolume(double usedVolume = 0.0)
        {
            foreach (KhemistryMaterialInstance m in contents)
                if (m != null)
                    usedVolume += m.TotalVolume;
            foreach (ConfigNode pending in _pendingSavedContents)
            {
                if (!KhemistryMaterialInstance.TryGetSerializedTotalVolume(pending,
                        out double pendingVolume))
                    return double.PositiveInfinity;
                usedVolume += pendingVolume;
                if (double.IsNaN(usedVolume) || double.IsInfinity(usedVolume))
                    return double.PositiveInfinity;
            }
            return usedVolume;
        }

        private void UpdateUI()
        {
            List<string> contentsDisplayNames = new List<string>();
            foreach (KhemistryMaterialInstance m in contents)
            {
                if (m?.material == null || m.amount <= 0) continue;
                m.UpdateParams("KhemistryMaterialStorage/UpdateUI");
                contentsDisplayNames.Add(m.amount + "× " + m.material.name + " as " + m.shape + " (" + KShared.DictToString(m.parameters) + ")");
            }
            contentsDisplay = contentsDisplayNames.Count == 0 ? "Empty" : string.Join("\n", contentsDisplayNames);
            double usedVolume = ComputeCurrentVolume();
            volumeDisplay = double.IsNaN(usedVolume) || double.IsInfinity(usedVolume)
                ? $"Preserved saved material / {volume:F10}"
                : $"{usedVolume:F10} / {volume:F10}";
        }
    }
}
