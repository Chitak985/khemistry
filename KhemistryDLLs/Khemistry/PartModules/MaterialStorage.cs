using System.Collections.Generic;

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
        /// <returns>Whether the material was added. This can only be false if there wasn't enough space.</returns>
        public bool AddMaterial(KhemistryMaterialInstance mat)
        {
            if (ComputeCurrentVolume(mat.volume) >= volume)
                return false;

            foreach (KhemistryMaterialInstance m in contents)
                if (m.Merge(mat))
                    return true;

            contents.Add(mat);
            return true;
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
            KhemistryMaterialInstance toRemove = null;
            foreach (KhemistryMaterialInstance m in contents)  // Check every material stored
            {
                // Make sure name, shape, and size match
                // Also check amount here, since no point in using the material if it isn't enough
                // This does create problems of the amount being spread across multiple materials however...
                // !TODO: Do something about it
                if (m.material.name == name && m.shape == shape && m.size == size && m.amount >= amount)
                {
                    bool success = true;
                    foreach (string param in paramConditions.Keys)  // Check every parameter of the material
                    {
                        // Check if the parameter exists in the material
                        if (!m.parameters.ContainsKey(param))
                            KShared.LogError(
                                "RemoveMaterial has a parameter condition for a parameter that does not exist! Error information:   " +
                                $"Material: Material {name}, shape {shape}, size {size}, and volume to subtract {volume}.   " +
                                $"MaterialStorage: Maximum volume is {volume}, contents display is \"{contentsDisplay}\", supported names [{KShared.ListToString(supportedNames)}], and supported shapes [{KShared.ListToString(supportedShapes)}].",
                                "KhemistryMaterialStorage/RemoveMaterial");

                        // Evaluate parameter comparison and skip material if failes
                        if (!KShared.EvaluateParamComparison(m.parameters[param], paramConditions[param]))
                        {
                            success = false;
                            break;
                        }
                    }
                    if (success)
                    {
                        toRemove = m;  // reference
                        break;
                    }
                }
            }

            if (toRemove != null)
            {
                if (toRemove.amount == amount)
                    contents.Remove(toRemove);
                else
                    toRemove.amount -= amount;  // since it is a reference, this should work

                return true;
            }

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
                usedVolume += m.volume;
            return usedVolume;
        }

        private void UpdateUI()
        {
            List<string> contentsDisplayNames = new List<string>();
            foreach (KhemistryMaterialInstance m in contents)
                if (m.volume > 0)
                    contentsDisplayNames.Add(m.material.name + " as " + m.shape + " (" + KShared.DictToString(m.parameters) + ")");
            contentsDisplay = string.Join("\n", contentsDisplayNames);
            volumeDisplay = $"{ComputeCurrentVolume():F10} / {volume:F10}";
        }
    }
}
