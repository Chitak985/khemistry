using System;
using System.Collections.Generic;

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

namespace Khemistry
{
    /// <summary>
    /// A versatile storage system that can be configured to store multiple resources, require charging, and have passive consumption.
    /// See the comment above source for a sample config.
    /// </summary>
    public class KhemistryAdvancedStorage : PartModule
    {
        [KSPField(isPersistant = false)]
        public string storageType = "single";

        [KSPField(isPersistant = false)]
        public float maximumResources = 1000f;

        [KSPField(isPersistant = false)]
        public bool chargingRequired = false;

        [KSPField(isPersistant = false)]
        public bool passiveConsumption = false;

        [KSPField(isPersistant = false)]
        public float maxInputRate = -1f;

        [KSPField(isPersistant = false)]
        public float maxOutputRate = -1f;

        [KSPField(isPersistant = false)]
        public float chargeRate = 0f;

        [KSPField(isPersistant = false)]
        public float chargeDecayRate = 0f;

        [KSPField(isPersistant = true)]
        public float chargePercent = 0f;

        [KSPField(isPersistant = true)]
        public KShared.ChargablePartState state = KShared.ChargablePartState.Off;

        [KSPField(isPersistant = true)]
        public string activeResource = "";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Contents", groupName = "khemistryadvstorage",
                  groupDisplayName = "Khemistry Container", groupStartCollapsed = false)]
        public string contentsDisplay = "Empty";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true,
                  guiName = "Volume Used", groupName = "khemistryadvstorage")]
        public string volumeDisplay = "0 / 0";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Charge", groupName = "khemistryadvstorage")]
        public string chargeDisplay = "N/A";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "State", groupName = "khemistryadvstorage")]
        public string stateDisplay = "Off";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false,
                  guiName = "Active Resource", groupName = "khemistryadvstorage")]
        public string activeResourceDisplay = "(none)";

        private readonly List<string> _supportedResources = new List<string>();

        private readonly List<string> _passiveNames = new List<string>();
        private readonly List<float> _passiveAmounts = new List<float>();

        private List<string> _chargeNames = new List<string>();
        private List<float> _chargeAmounts = new List<float>();

        private enum ConsequenceType { Off, Void, Destroy, Boiloff }

        private struct ConsequenceConfig
        {
            public ConsequenceType type;
            public float value;
        }

        private ConsequenceConfig _passiveUnsatisfiedResult;
        private ConsequenceConfig _filledUnpoweredResult;

        private readonly Dictionary<string, double> _frozenAmounts = new Dictionary<string, double>();

        private bool _passiveUnsatisfiedFired = false;
        private double _filledUnpoweredAccum = 0.0;

        private bool _fatalConfigError = false;

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryadvstorage")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == KShared.ChargablePartState.On) return;
            state = KShared.ChargablePartState.Charging;
            KShared.Log("Charging enabled.", "KhemistryAdvancedStorage/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryadvstorage", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != KShared.ChargablePartState.Charging) return;
            state = KShared.ChargablePartState.Off;
            KShared.Log("Charging disabled.", "KhemistryAdvancedStorage/DisableCharging");
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
            state = KShared.ChargablePartState.On;
            _passiveUnsatisfiedFired = false;
            KShared.Log("Container turned ON.", "KhemistryAdvancedStorage/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off container",
                  groupName = "khemistryadvstorage", active = false)]
        public void TurnOffContainer()
        {
            state = KShared.ChargablePartState.Off;
            KShared.Log("Container turned OFF.", "KhemistryAdvancedStorage/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Select resource",
                  groupName = "khemistryadvstorage", active = false)]
        public void SelectResource()
        {
            if (storageType != "multi") return;

            if (!string.IsNullOrEmpty(activeResource))
            {
                PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(activeResource);
                if (def != null)
                {
                    PartResource pr = part.Resources.Get(def.id);
                    if (pr != null && pr.amount >= 1.0)
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Container must be nearly empty to switch resource. (less than 1 unit)", 5f, ScreenMessageStyle.UPPER_CENTER));
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

            KShared shared = KShared.Instance;
            if (shared == null)
            {
                KShared.LogError("KShared is null!", "KhemistryAdvancedStorage/SelectResource");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "An internal null reference error occured! A vital component (KShared) of the mod is missing, please restart the game.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            shared.ShowSelector("Select active resource", new List<string>(_supportedResources), label =>
            {
                activeResource = label;
                KShared.Log("Active resource set to " + activeResource,
                    "KhemistryAdvancedStorage/SelectResource");
                ZeroNonActiveResources();
            });
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

            EnsureResourcesExistOnPart();
            SnapshotFrozenAmounts();
            _passiveUnsatisfiedFired = false;
            _filledUnpoweredAccum = 0.0;
            UpdateUI();
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

        private void LoadConfigFromPartInfo()
        {
            if (part.partInfo?.partConfig == null)
            {
                KShared.LogError("partInfo.partConfig is null!",
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
                KShared.LogError("Could not find MODULE node in partConfig!",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            _supportedResources.Clear();
            if (!moduleNode.HasNode("SUPPORTED_RESOURCES"))
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage but no SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            foreach (string n in moduleNode.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
                _supportedResources.Add(n.Trim());
            if (_supportedResources.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage with an empty SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            storageType = moduleNode.GetValue("storageType") ?? moduleNode.GetValue("type") ?? "single";

            maximumResources = KShared.GetFloatValueFromCFG(moduleNode, "maximumResources", maximumResources);
            maxInputRate = KShared.GetFloatValueFromCFG(moduleNode, "maxInputRate", maxInputRate);
            maxOutputRate = KShared.GetFloatValueFromCFG(moduleNode, "maxOutputRate", maxOutputRate);

            chargeRate = KShared.GetFloatValueFromCFG(moduleNode, "chargeRate", chargeRate);
            chargeDecayRate = KShared.GetFloatValueFromCFG(moduleNode, "chargeDecayRate", chargeDecayRate);

            if (bool.TryParse(moduleNode.GetValue("chargingRequired"), out bool tmpB)) chargingRequired = tmpB;
            if (bool.TryParse(moduleNode.GetValue("passiveConsumption"), out tmpB)) passiveConsumption = tmpB;

            _passiveUnsatisfiedResult = ParseConsequence(
                moduleNode.GetValue("passiveUnsatisfiedResult"), allowBoiloff: false,
                "passiveUnsatisfiedResult", "off");

            _filledUnpoweredResult = ParseConsequence(
                moduleNode.GetValue("filledUnpoweredResult"), allowBoiloff: true,
                "filledUnpoweredResult", "off");

            _passiveNames.Clear();
            _passiveAmounts.Clear();
            if (passiveConsumption)
            {
                if (moduleNode.HasNode("PASSIVE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("PASSIVE_CON_NAMES").GetValues("name"))
                        _passiveNames.Add(n.Trim());
                if (moduleNode.HasNode("PASSIVE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("PASSIVE_CON_AMOUNTS").GetValues("amount"))
                        if (float.TryParse(a, out float tmp))
                            _passiveAmounts.Add(tmp);
                if (_passiveNames.Count != _passiveAmounts.Count)
                    KShared.LogError("PASSIVE_CON_NAMES and PASSIVE_CON_AMOUNTS length mismatch.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
            }

            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
                _chargeNames = KShared.GetChargingFromCFG(moduleNode, out _chargeAmounts);

            if ((storageType == "single" || storageType == "multi") && string.IsNullOrEmpty(activeResource))
                if (_supportedResources.Count > 0) activeResource = _supportedResources[0];

            if (storageType == "single" && _supportedResources.Count > 1)
            {
                KShared.LogError(
                    "storageType=single but multiple SUPPORTED_RESOURCES defined; only first will be used.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                string keep = _supportedResources[0];
                _supportedResources.Clear();
                _supportedResources.Add(keep);
                activeResource = keep;
            }

            KShared.Log(
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
            string src = string.IsNullOrEmpty(raw) ? fallback : raw.Trim().Trim('"').Trim().ToLower();

            if (src == "off") return new ConsequenceConfig { type = ConsequenceType.Off };
            if (src == "void") return new ConsequenceConfig { type = ConsequenceType.Void };

            if (src.StartsWith("destroy,"))
            {
                if (float.TryParse(src.Substring(8), out float v))
                    return new ConsequenceConfig { type = ConsequenceType.Destroy, value = v };
                KShared.LogError("Could not parse destroy power in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            if (allowBoiloff && src.StartsWith("boiloff,"))
            {
                if (float.TryParse(src.Substring(8), out float v))
                    return new ConsequenceConfig { type = ConsequenceType.Boiloff, value = v };
                KShared.LogError("Could not parse boiloff rate in " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                    "KhemistryAdvancedStorage/ParseConsequence");
                return new ConsequenceConfig { type = ConsequenceType.Off };
            }

            KShared.LogError("Unknown consequence value " + fieldName + "=\"" + raw + "\". Defaulting to off.",
                "KhemistryAdvancedStorage/ParseConsequence");
            return new ConsequenceConfig { type = ConsequenceType.Off };
        }

        private void EnsureResourcesExistOnPart()
        {
            foreach (string resName in _supportedResources)
            {
                PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(resName);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + resName + "\" in SUPPORTED_RESOURCES.",
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
                if (!_supportedResources.Contains(pr.resourceName))
                    continue;
                if (!string.IsNullOrEmpty(activeResource) && pr.resourceName != activeResource)
                    pr.amount = 0.0;
            }
        }

        private void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            if (state == KShared.ChargablePartState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
                return;
            }

            if (state != KShared.ChargablePartState.Charging) return;

            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = KShared.ChargablePartState.On;
                KShared.Log("Container fully charged, now ON.",
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
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
                    if (chargePercent < 0f) chargePercent = 0f;
                }
            }
        }

        private void HandlePassiveConsumption(double dt)
        {
            if (!passiveConsumption) return;

            if (state == KShared.ChargablePartState.On)
            {
                bool satisfied = ConsumeVesselResources(_passiveNames, _passiveAmounts, dt);
                if (satisfied)
                {
                    _passiveUnsatisfiedFired = false;
                    return;
                }
            }

            if (!HasAnyStoredResources()) return;

            if (!_passiveUnsatisfiedFired)
            {
                _passiveUnsatisfiedFired = true;
                ApplyConsequence(_passiveUnsatisfiedResult, "passiveUnsatisfiedResult");
            }
        }

        private void HandleFilledUnpowered(double dt)
        {
            if (state == KShared.ChargablePartState.On) return;

            if (!HasAnyStoredResources()) return;

            _filledUnpoweredAccum += dt;

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
                    break;

                case ConsequenceType.Void:
                    KShared.Log("Voiding all stored resources (" + source + ").",
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    foreach (PartResource pr in part.Resources)
                    {
                        if (_supportedResources.Contains(pr.resourceName))
                            pr.amount = 0.0;
                    }
                    break;

                case ConsequenceType.Destroy:
                    KShared.Log(
                        string.Format("Destroying part with power {0:F1} ({1}).", cfg.value, source),
                        "KhemistryAdvancedStorage/ApplyConsequence");
                    KShared.TriggerExplosionWithHeat(part, (float)cfg.value, (float)(cfg.value*5)+100);
                    break;

                case ConsequenceType.Boiloff:
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
            List<PartResource> filled = new List<PartResource>();
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
                _frozenAmounts[pr.resourceName] = pr.amount;
            }

            KShared.Log(
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

            List<double> pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = part.RequestResource(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        part.RequestResource(names[i], -pulled[i]);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the container has any stored resources.
        /// </summary>
        private bool HasAnyStoredResources()
        {
            foreach (PartResource pr in part.Resources)
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 0.0)
                    return true;
            return false;
        }

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
                (chargingRequired && state != KShared.ChargablePartState.On) ||
                (!chargingRequired && state == KShared.ChargablePartState.Off);

            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;

                if (!shouldFreeze)
                    _frozenAmounts[pr.resourceName] = pr.amount;
                else
                {
                    if (_frozenAmounts.TryGetValue(pr.resourceName, out double frozen))
                        pr.amount = frozen;
                    else
                        _frozenAmounts[pr.resourceName] = pr.amount;
                }
            }
        }

        private void EnforceCapacity()
        {
            foreach (PartResource pr in part.Resources)
                if (pr.amount < 0.0) pr.amount = 0.0;

            if (storageType == "multiShared")
            {
                double total = 0.0;
                List<PartResource> list = new List<PartResource>();
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    list.Add(pr);
                    total += pr.amount;
                }

                if (total > maximumResources && total > 0.0)
                {
                    double scale = maximumResources / total;
                    foreach (PartResource pr in list)
                        pr.amount *= scale;
                }

                foreach (PartResource pr in list)
                    pr.maxAmount = maximumResources;
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

        private void UpdateUI()
        {
            double total = 0.0;
            List<string> parts = new List<string>();

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
                ? string.Format("{0:F1}%", chargePercent)
                : "N/A";

            stateDisplay = state.ToString();

            activeResourceDisplay = (storageType == "single" || storageType == "multi")
                ? (string.IsNullOrEmpty(activeResource) ? "(none)" : activeResource)
                : "(multiShared)";

            Events["EnableCharging"].active = chargingRequired && state != KShared.ChargablePartState.Charging && state != KShared.ChargablePartState.On;
            Events["DisableCharging"].active = chargingRequired && state == KShared.ChargablePartState.Charging;
            Events["TurnOnContainer"].active = state != KShared.ChargablePartState.On;
            Events["TurnOffContainer"].active = state == KShared.ChargablePartState.On;
            Events["SelectResource"].active = storageType == "multi";
        }
    }
}
