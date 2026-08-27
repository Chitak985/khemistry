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
            foreach (ConfigNode savedNode in _pendingSavedContents)
            {
                if (!KhemistryMaterialInstance.TryFromConfigNode(savedNode, out KhemistryMaterialInstance material,
                        "KhemistryMaterialStorage/RestoreSavedContents"))
                    continue;

                if (!AddMaterial(material))
                    KShared.LogError(
                        "Saved material \"" + material.material.name + "\" no longer fits or is no longer supported by part \"" + part.name + "\".",
                        "KhemistryMaterialStorage/RestoreSavedContents");
            }
            _pendingSavedContents.Clear();
        }

        public void FixedUpdate() => UpdateUI();

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.LogError("partInfo.partConfig is null!",
                    "KhemistryMaterialStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            ConfigNode moduleNode = null;
            foreach (ConfigNode n in part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (n.GetValue("name") == "KhemistryMaterialStorage") { moduleNode = n; break; }
            }

            if (moduleNode == null)
            {
                KShared.LogError("Could not find MODULE node in partConfig!",
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
                supportedNames.Add(n.Trim());
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
                supportedShapes.Add(n.Trim());
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

            if (ComputeCurrentVolume(mat.TotalVolume) > volume + 1e-6f)
                return false;

            foreach (KhemistryMaterialInstance m in contents)
                if (m.Merge(mat))
                    return true;

            contents.Add(mat);
            return true;
        }

        private bool AcceptsMaterial(KhemistryMaterialInstance mat)
        {
            if (mat?.material == null || mat.amount <= 0
                || float.IsNaN(mat.volume) || float.IsInfinity(mat.volume) || mat.volume < 0f)
                return false;

            if (!supportedNames.Contains(mat.material.name) || !supportedShapes.Contains(mat.shape))
                return false;

            foreach (KeyValuePair<string, string> requirement in paramRequirements)
            {
                mat.UpdateParams("KhemistryMaterialStorage/AcceptsMaterial");
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

            foreach (KeyValuePair<string, string> condition in
                     paramConditions ?? new Dictionary<string, string>())
            {
                material.UpdateParams("KhemistryMaterialStorage/MatchesMaterial");
                if (!material.parameters.TryGetValue(condition.Key, out string value)
                    || !KShared.EvaluateParamComparison(value, condition.Value))
                    return false;
            }
            return true;
        }

        public int GetMatchingMaterialAmount(string name, string shape, string size,
            Dictionary<string, string> paramConditions)
        {
            int total = 0;
            foreach (KhemistryMaterialInstance material in contents)
                if (MatchesMaterial(material, name, shape, size, paramConditions))
                    total += material.amount;
            return total;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name">Required material name</param>
        /// <param name="shape">Required material shape</param>
        /// <param name="size">Required material size</param>
        /// <param name="paramConditions">Parameter conditions</param>
        /// <param name="amount">Amount to remove</param>
        /// <returns>If the material was removed successfully</returns>
        public bool RemoveMaterial(string name, string shape, string size, Dictionary<string, string> paramConditions, int amount)
        {
            return TryRemoveMaterial(name, shape, size, paramConditions, amount, out _);
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
                removed.Add(piece);

                if (take == stored.amount) contents.Remove(stored);
                else stored.amount -= take;

                remaining -= take;
                if (remaining == 0) return true;
            }

            foreach (KhemistryMaterialInstance piece in removed) AddMaterial(piece);
            removed.Clear();
            return false;
        }

        /// <summary>
        /// Compute the current volume taken up by the contents of this storage.
        /// Can accept a value to add to the total volume, usually used to check if adding a new material would exceed capacity.
        /// </summary>
        /// <param name="usedVolume">An additional amount to add to the volume being used.</param>
        /// <returns>How much volume is used.</returns>
        private float ComputeCurrentVolume(float usedVolume = 0f)
        {
            foreach (KhemistryMaterialInstance m in contents)
                usedVolume += m.TotalVolume;
            return usedVolume;
        }

        private void UpdateUI()
        {
            List<string> contentsDisplayNames = new List<string>();
            foreach (KhemistryMaterialInstance m in contents)
                if (m.volume > 0)
                    contentsDisplayNames.Add(m.amount + "× " + m.material.name + " as " + m.shape + " (" + KShared.DictToString(m.parameters) + ")");
            contentsDisplay = string.Join("\n", contentsDisplayNames);
            volumeDisplay = $"{ComputeCurrentVolume():F10} / {volume:F10}";
        }
    }
}
