using System;
using System.Collections.Generic;
using System.Globalization;

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

        // Universal-time checkpoint used to catch up the per-second storage effects
        // after a vessel has been unloaded or the scene has changed. A negative value
        // identifies a legacy/new save that has no checkpoint yet.
        [KSPField(isPersistant = true)]
        public double lastUpdateUniversalTime = -1.0;

        // Preserve the sub-tick boiloff interval as well. Without this, repeatedly
        // loading a vessel can indefinitely postpone a consequence with a small rate.
        [KSPField(isPersistant = true)]
        public double filledUnpoweredElapsed = 0.0;

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

        private readonly Dictionary<string, bool> _savedFlowStates = new Dictionary<string, bool>();
        private bool _flowBlocked;
        private bool _overCapacityLogged;
        private bool _multiConflictLogged;

        private bool _passiveUnsatisfiedFired = false;

        // Persist elapsed catch-up work separately from the universal-time checkpoint. A
        // vessel can be saved again after OnStart but before its first FixedUpdate; keeping
        // this queue only in memory would lose the entire unloaded interval in that case.
        [KSPField(isPersistant = true)]
        public double pendingCatchUpSeconds = 0.0;
        private bool _universalTimeWarningLogged = false;

        private bool _fatalConfigError = false;

        private const string SavedFlowStateNodeName = "KHEMISTRY_ORIGINAL_FLOW_STATE";
        private const double FilledUnpoweredTickSeconds = 0.1;

        // This is only a corrupt-save/clock safety bound. It is deliberately far
        // beyond a practical campaign duration, while keeping every rate*time
        // calculation finite even if a save contains an extreme timestamp.
        private const double MaximumElapsedSeconds = 1.0e12;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            _savedFlowStates.Clear();
            _flowBlocked = false;
            if (node == null) return;

            foreach (ConfigNode savedState in node.GetNodes(SavedFlowStateNodeName))
            {
                string resourceName = savedState.GetValue("name")?.Trim();
                if (string.IsNullOrEmpty(resourceName)
                    || !bool.TryParse(savedState.GetValue("flowState"), out bool flowState))
                    continue;

                _savedFlowStates[resourceName] = flowState;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            // flowState itself is saved by KSP. Keep the player's pre-blocking value
            // alongside it so a container saved while Off can restore that value when
            // it is turned back on after loading.
            while (node.HasNode(SavedFlowStateNodeName))
                node.RemoveNode(SavedFlowStateNodeName);
            foreach (KeyValuePair<string, bool> savedState in _savedFlowStates)
            {
                ConfigNode stateNode = node.AddNode(SavedFlowStateNodeName);
                stateNode.AddValue("name", savedState.Key);
                stateNode.AddValue("flowState", savedState.Value);
            }
        }

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
                    if (pr != null && pr.amount > 1e-9)
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(
                            "Container must be empty before switching resource.", 5f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }
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
                if (_fatalConfigError || part == null) return;
                if (!_supportedResources.Contains(label)) return;
                if (label == activeResource) return;

                // The selector is asynchronous: the tank may have been filled while it
                // was open. Recheck here so changing the selection cannot discard the
                // newly-added contents when inactive tanks are cleared.
                if (!CanSwitchActiveResource(label))
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Container must be empty before switching resource.", 5f, ScreenMessageStyle.UPPER_CENTER));
                    return;
                }

                activeResource = label;
                KShared.Log("Active resource set to " + activeResource,
                    "KhemistryAdvancedStorage/SelectResource");
                EnforceCapacity();
            });
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                RestoreSavedFlowStates();
                foreach (BaseEvent e in Events) e.active = false;
                contentsDisplay = "ERROR: see log";
                return;
            }

            RestoreStaleSavedFlowStates();
            EnsureResourcesExistOnPart();
            EnforceCapacity();
            SanitizePersistentState();
            if (HighLogic.LoadedSceneIsFlight)
                PrepareUniversalTimeCatchUp();
            else
            {
                // Editor craft must not inherit the age of the current save when
                // launched later; their clock begins on the first flight load.
                lastUpdateUniversalTime = -1.0;
                pendingCatchUpSeconds = 0.0;
            }
            UpdateTransferBlocking();
            _passiveUnsatisfiedFired = false;
            UpdateUI();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            // Process saved/off-rails time separately so preservation, decay, and
            // unpowered consequences catch up, while active charging remains an
            // operation performed only while the vessel is actually loaded.
            double catchUpDt = pendingCatchUpSeconds;
            if (catchUpDt > 0.0)
            {
                ProcessElapsedTime(catchUpDt, allowActiveCharging: false);
                pendingCatchUpSeconds = 0.0;
            }

            double dt = GetLiveElapsedTime();
            if (dt > 0.0)
                ProcessElapsedTime(dt, allowActiveCharging: true);
            else
            {
                EnforceCapacity();
                UpdateTransferBlocking();
                UpdateUI();
                return;
            }

            EnforceCapacity();
            UpdateTransferBlocking();
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

            ConfigNode moduleNode = KShared.FindModuleConfigNode(this,
                "KhemistryAdvancedStorage");

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
            bool invalidSupportedResource = false;
            HashSet<string> seenSupportedResources = new HashSet<string>(StringComparer.Ordinal);
            foreach (string n in moduleNode.GetNode("SUPPORTED_RESOURCES").GetValues("name"))
            {
                string resourceName = n?.Trim();
                if (string.IsNullOrEmpty(resourceName))
                {
                    invalidSupportedResource = true;
                    KShared.LogError("SUPPORTED_RESOURCES contains an empty resource name.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    continue;
                }

                if (!seenSupportedResources.Add(resourceName))
                {
                    KShared.LogWarning("Ignoring duplicate supported resource \"" + resourceName + "\".",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    continue;
                }

                if (PartResourceLibrary.Instance == null
                    || PartResourceLibrary.Instance.GetDefinition(resourceName) == null)
                {
                    invalidSupportedResource = true;
                    KShared.LogError("Unknown resource \"" + resourceName + "\" in SUPPORTED_RESOURCES.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    continue;
                }

                _supportedResources.Add(resourceName);
            }
            if (_supportedResources.Count == 0)
            {
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryAdvancedStorage with an empty SUPPORTED_RESOURCES node. This module will not load.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            if (invalidSupportedResource)
                KShared.LogWarning("Invalid SUPPORTED_RESOURCES entries were ignored.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");

            storageType = (moduleNode.GetValue("storageType") ?? moduleNode.GetValue("type") ?? "single").Trim();

            if (!TryReadOptionalFloat(moduleNode, "maximumResources", ref maximumResources)
                || !TryReadOptionalFloat(moduleNode, "maxInputRate", ref maxInputRate)
                || !TryReadOptionalFloat(moduleNode, "maxOutputRate", ref maxOutputRate)
                || !TryReadOptionalFloat(moduleNode, "chargeRate", ref chargeRate)
                || !TryReadOptionalFloat(moduleNode, "chargeDecayRate", ref chargeDecayRate)
                || !TryReadOptionalBool(moduleNode, "chargingRequired", ref chargingRequired)
                || !TryReadOptionalBool(moduleNode, "passiveConsumption", ref passiveConsumption))
            {
                KShared.LogError("Advanced storage has a malformed numeric or Boolean setting.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            if (float.IsNaN(maximumResources) || float.IsInfinity(maximumResources) || maximumResources <= 0f
                || float.IsNaN(chargeRate) || float.IsInfinity(chargeRate) || chargeRate < 0f
                || float.IsNaN(chargeDecayRate) || float.IsInfinity(chargeDecayRate) || chargeDecayRate < 0f)
            {
                KShared.LogError("Advanced storage has invalid capacity or charge-rate settings.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            if (float.IsNaN(maxInputRate) || float.IsInfinity(maxInputRate)
                || float.IsNaN(maxOutputRate) || float.IsInfinity(maxOutputRate))
            {
                KShared.LogError("Advanced storage has invalid transfer-rate settings.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }
            if (maxInputRate >= 0f || maxOutputRate >= 0f)
                KShared.LogWarning("maxInputRate/maxOutputRate are not supported by KSP's stock tank transfer API and will not be enforced.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");

            if (chargingRequired && chargeRate <= 0f)
            {
                KShared.LogError("chargingRequired=true requires a finite positive chargeRate.",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            if (!TryParseConsequence(moduleNode.GetValue("passiveUnsatisfiedResult"),
                    allowBoiloff: false, "passiveUnsatisfiedResult", "off",
                    out _passiveUnsatisfiedResult)
                || !TryParseConsequence(moduleNode.GetValue("filledUnpoweredResult"),
                    allowBoiloff: true, "filledUnpoweredResult", "off",
                    out _filledUnpoweredResult))
            {
                _fatalConfigError = true;
                return;
            }

            _passiveNames.Clear();
            _passiveAmounts.Clear();
            if (passiveConsumption)
            {
                bool invalidPassiveConfig = false;
                if (moduleNode.HasNode("PASSIVE_CON_NAMES"))
                    foreach (string n in moduleNode.GetNode("PASSIVE_CON_NAMES").GetValues("name"))
                    {
                        string resourceName = n?.Trim();
                        if (string.IsNullOrEmpty(resourceName))
                        {
                            invalidPassiveConfig = true;
                            continue;
                        }
                        if (PartResourceLibrary.Instance == null
                            || PartResourceLibrary.Instance.GetDefinition(resourceName) == null)
                        {
                            invalidPassiveConfig = true;
                            KShared.LogError("Unknown passive-consumption resource \"" + resourceName + "\".",
                                "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                            continue;
                        }
                        _passiveNames.Add(resourceName);
                    }
                if (moduleNode.HasNode("PASSIVE_CON_AMOUNTS"))
                    foreach (string a in moduleNode.GetNode("PASSIVE_CON_AMOUNTS").GetValues("amount"))
                        if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float tmp)
                            && !float.IsNaN(tmp) && !float.IsInfinity(tmp) && tmp > 0f)
                            _passiveAmounts.Add(tmp);
                        else
                            invalidPassiveConfig = true;
                if (invalidPassiveConfig || _passiveNames.Count == 0
                    || _passiveNames.Count != _passiveAmounts.Count)
                {
                    KShared.LogError("PASSIVE_CON_NAMES and PASSIVE_CON_AMOUNTS must contain equal numbers of known, non-empty resource names and finite positive amounts.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    _fatalConfigError = true;
                    return;
                }
            }

            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
            {
                _chargeNames = KShared.GetChargingFromCFG(moduleNode, out _chargeAmounts);
                if (_chargeNames.Count == 0 || _chargeNames.Count != _chargeAmounts.Count)
                {
                    KShared.LogError("Charging is required but no valid charging resource list was provided.",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    _fatalConfigError = true;
                    return;
                }

                foreach (string resourceName in _chargeNames)
                {
                    if (PartResourceLibrary.Instance != null
                        && PartResourceLibrary.Instance.GetDefinition(resourceName) != null)
                        continue;

                    KShared.LogError("Unknown charging resource \"" + resourceName + "\".",
                        "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                    _fatalConfigError = true;
                    return;
                }
            }

            if (storageType != "single" && storageType != "multi" && storageType != "multiShared")
            {
                KShared.LogError("Unknown storageType \"" + storageType + "\".",
                    "KhemistryAdvancedStorage/LoadConfigFromPartInfo");
                _fatalConfigError = true;
                return;
            }

            activeResource = activeResource?.Trim() ?? "";
            if ((storageType == "single" || storageType == "multi") && string.IsNullOrEmpty(activeResource))
                if (_supportedResources.Count > 0) activeResource = _supportedResources[0];
            if ((storageType == "single" || storageType == "multi")
                && !_supportedResources.Contains(activeResource))
                activeResource = _supportedResources[0];

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
        private bool TryParseConsequence(string raw, bool allowBoiloff, string fieldName,
            string fallback, out ConsequenceConfig result)
        {
            string src = string.IsNullOrEmpty(raw) ? fallback : raw.Trim().Trim('"').Trim().ToLowerInvariant();

            if (src == "off")
            {
                result = new ConsequenceConfig { type = ConsequenceType.Off };
                return true;
            }
            if (src == "void")
            {
                result = new ConsequenceConfig { type = ConsequenceType.Void };
                return true;
            }

            if (src.StartsWith("destroy,"))
            {
                if (float.TryParse(src.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                    && !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f)
                {
                    result = new ConsequenceConfig { type = ConsequenceType.Destroy, value = v };
                    return true;
                }
                KShared.LogError("Could not parse destroy power in " + fieldName + "=\"" + raw + "\".",
                    "KhemistryAdvancedStorage/ParseConsequence");
                result = default(ConsequenceConfig);
                return false;
            }

            if (allowBoiloff && src.StartsWith("boiloff,"))
            {
                if (float.TryParse(src.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                    && !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f)
                {
                    result = new ConsequenceConfig { type = ConsequenceType.Boiloff, value = v };
                    return true;
                }
                KShared.LogError("Could not parse boiloff rate in " + fieldName + "=\"" + raw + "\".",
                    "KhemistryAdvancedStorage/ParseConsequence");
                result = default(ConsequenceConfig);
                return false;
            }

            KShared.LogError("Unknown consequence value " + fieldName + "=\"" + raw + "\".",
                "KhemistryAdvancedStorage/ParseConsequence");
            result = default(ConsequenceConfig);
            return false;
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
                    if (!IsFinite(existing.amount) || existing.amount < 0.0)
                    {
                        KShared.LogWarning("Resetting invalid stored amount for resource \"" + resName + "\".",
                            "KhemistryAdvancedStorage/EnsureResourcesExistOnPart");
                        existing.amount = 0.0;
                    }
                    existing.maxAmount = Math.Max(existing.amount, maximumResources);
                }
            }

        }

        private void ReconcileMultiResourceState()
        {
            if (storageType != "multi" || part == null) return;

            List<PartResource> filled = new List<PartResource>();
            foreach (PartResource pr in part.Resources)
            {
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 1e-9)
                    filled.Add(pr);
            }

            if (filled.Count == 1 && filled[0].resourceName != activeResource)
            {
                KShared.LogWarning("Recovered multi-resource storage selection from its saved contents: "
                    + filled[0].resourceName + ".", "KhemistryAdvancedStorage/ReconcileMultiResourceState");
                activeResource = filled[0].resourceName;
            }

            if (filled.Count > 1)
            {
                if (!_multiConflictLogged)
                {
                    KShared.LogWarning("Multi-resource storage contains several resources; preserving them and blocking additional non-active input until they are drained.",
                        "KhemistryAdvancedStorage/ReconcileMultiResourceState");
                    _multiConflictLogged = true;
                }
            }
            else _multiConflictLogged = false;
        }

        private bool CanSwitchActiveResource(string targetResource)
        {
            if (targetResource == activeResource) return true;

            foreach (PartResource pr in part.Resources)
                if (_supportedResources.Contains(pr.resourceName) && pr.amount > 1e-9)
                    return false;

            return true;
        }

        private void SanitizePersistentState()
        {
            if (!IsFinite(chargePercent))
            {
                KShared.LogWarning("Saved chargePercent was not finite; resetting it to zero.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                chargePercent = 0f;
            }
            else
            {
                chargePercent = Math.Max(0f, Math.Min(100f, chargePercent));
            }

            if (!Enum.IsDefined(typeof(KShared.ChargablePartState), state))
            {
                KShared.LogWarning("Saved storage state was invalid; resetting it to Off.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                state = KShared.ChargablePartState.Off;
            }

            if (!chargingRequired && state == KShared.ChargablePartState.Charging)
            {
                KShared.LogWarning("Saved storage state was Charging although charging is disabled; resetting it to Off.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                state = KShared.ChargablePartState.Off;
            }
            else if (chargingRequired && state == KShared.ChargablePartState.On && chargePercent < 100f)
            {
                KShared.LogWarning("Saved charged storage was On without a full charge; resetting it to Off.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                state = KShared.ChargablePartState.Off;
            }

            if (!IsFinite(filledUnpoweredElapsed) || filledUnpoweredElapsed < 0.0)
            {
                KShared.LogWarning("Saved filled-unpowered elapsed time was invalid; resetting it.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                filledUnpoweredElapsed = 0.0;
            }
            else if (filledUnpoweredElapsed > MaximumElapsedSeconds)
            {
                KShared.LogWarning("Saved filled-unpowered elapsed time was excessive; clamping it.",
                    "KhemistryAdvancedStorage/SanitizePersistentState");
                filledUnpoweredElapsed = MaximumElapsedSeconds;
            }
        }

        private void ProcessElapsedTime(double dt, bool allowActiveCharging)
        {
            dt = BoundElapsedTime(dt, "elapsed storage update");
            if (dt <= 0.0) return;

            double poweredDt = dt;
            bool wasCharging = allowActiveCharging && chargingRequired
                && state == KShared.ChargablePartState.Charging;
            double timeToFullCharge = wasCharging && chargeRate > 0f
                ? Math.Max(0.0, (100.0 - chargePercent) / chargeRate)
                : 0.0;

            if (allowActiveCharging)
                HandleCharging(dt);
            else
                HandleOfflineChargeDecay(dt);

            if (wasCharging && state == KShared.ChargablePartState.On)
            {
                double unpoweredDt = Math.Min(dt, timeToFullCharge);
                poweredDt = Math.Max(0.0, dt - Math.Min(dt, timeToFullCharge));

                // Charging storage is not yet preserving its contents. Account for the
                // pre-full portion explicitly even though HandleCharging has now changed
                // the current state to On.
                HandleFilledUnpowered(unpoweredDt, treatAsUnpowered: true);

                if (state != KShared.ChargablePartState.On)
                {
                    // A filled-unpowered consequence changed the state at the transition.
                    // The nominal post-charge portion is consequently unpowered as well.
                    HandleFilledUnpowered(poweredDt);
                    return;
                }
            }

            bool wasPoweredBeforePassiveUpdate =
                state == KShared.ChargablePartState.On;
            HandlePassiveConsumption(poweredDt);

            // A passive failure is evaluated over this interval and turns the
            // container off at its end. Filled-unpowered time therefore starts with
            // the following interval instead of being charged retroactively.
            if (!wasPoweredBeforePassiveUpdate
                || state == KShared.ChargablePartState.On)
                HandleFilledUnpowered(dt);
        }

        private void HandleOfflineChargeDecay(double dt)
        {
            // Active charging is intentionally suspended while unloaded. A container that
            // was left in Charging therefore cannot maintain its charge either, and decays
            // just like an Off container until it is loaded again.
            if (!chargingRequired || state == KShared.ChargablePartState.On
                || chargeDecayRate <= 0f)
                return;

            chargePercent = (float)Math.Max(0.0, chargePercent - chargeDecayRate * dt);
        }

        private void PrepareUniversalTimeCatchUp()
        {
            pendingCatchUpSeconds = BoundElapsedTime(pendingCatchUpSeconds,
                "saved pending catch-up interval");
            if (!TryGetUniversalTime(out double now))
            {
                lastUpdateUniversalTime = -1.0;
                return;
            }

            if (IsFinite(lastUpdateUniversalTime) && lastUpdateUniversalTime >= 0.0)
            {
                double elapsed = now - lastUpdateUniversalTime;
                if (elapsed >= 0.0)
                    pendingCatchUpSeconds = BoundElapsedTime(
                        pendingCatchUpSeconds + BoundElapsedTime(elapsed,
                            "saved universal-time interval"),
                        "combined pending catch-up interval");
                else
                    KShared.LogWarning("Saved storage timestamp was in the future; resetting its clock without catch-up.",
                        "KhemistryAdvancedStorage/PrepareUniversalTimeCatchUp");
            }
            else if (lastUpdateUniversalTime != -1.0)
            {
                KShared.LogWarning("Saved storage timestamp was invalid; resetting its clock without catch-up.",
                    "KhemistryAdvancedStorage/PrepareUniversalTimeCatchUp");
            }

            lastUpdateUniversalTime = now;
        }

        private double GetLiveElapsedTime()
        {
            if (TryGetUniversalTime(out double now))
            {
                double elapsed = 0.0;
                if (IsFinite(lastUpdateUniversalTime) && lastUpdateUniversalTime >= 0.0)
                {
                    elapsed = now - lastUpdateUniversalTime;
                    if (elapsed < 0.0)
                    {
                        KShared.LogWarning("Universal time moved backwards; resetting the storage clock.",
                            "KhemistryAdvancedStorage/GetLiveElapsedTime");
                        elapsed = 0.0;
                    }
                }

                lastUpdateUniversalTime = now;
                return BoundElapsedTime(elapsed, "live universal-time interval");
            }

            // Planetarium time should be available in flight, but the physics delta
            // is a safe fallback that preserves the old loaded-vessel behavior. Drop
            // the old checkpoint so elapsed fallback time is not counted again if
            // universal time recovers on a later tick.
            lastUpdateUniversalTime = -1.0;
            return BoundElapsedTime(TimeWarp.fixedDeltaTime, "physics-time fallback");
        }

        private bool TryGetUniversalTime(out double universalTime)
        {
            universalTime = Planetarium.GetUniversalTime();
            if (IsFinite(universalTime) && universalTime >= 0.0)
            {
                _universalTimeWarningLogged = false;
                return true;
            }

            if (!_universalTimeWarningLogged)
            {
                KShared.LogWarning("Universal time was unavailable; using loaded physics time until it recovers.",
                    "KhemistryAdvancedStorage/TryGetUniversalTime");
                _universalTimeWarningLogged = true;
            }
            universalTime = 0.0;
            return false;
        }

        private static double BoundElapsedTime(double elapsed, string source)
        {
            if (!IsFinite(elapsed) || elapsed <= 0.0) return 0.0;
            if (elapsed <= MaximumElapsedSeconds) return elapsed;

            KShared.LogWarning(source + " exceeded the safety limit and was clamped.",
                "KhemistryAdvancedStorage/BoundElapsedTime");
            return MaximumElapsedSeconds;
        }

        private void HandleCharging(double dt)
        {
            if (!chargingRequired) return;
            if (!IsFinite(dt) || dt <= 0.0) return;

            if (state == KShared.ChargablePartState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent = (float)Math.Max(0.0, chargePercent - chargeDecayRate * dt);
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

            double chargingDt = Math.Min(dt, (100.0 - chargePercent) / chargeRate);
            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, chargingDt);
            if (satisfied)
            {
                chargePercent = (float)Math.Min(100.0, chargePercent + chargeRate * chargingDt);
                if (chargePercent >= 100f)
                {
                    chargePercent = 100f;
                    state = KShared.ChargablePartState.On;
                    _passiveUnsatisfiedFired = false;
                    KShared.Log("Container fully charged, now ON.",
                        "KhemistryAdvancedStorage/HandleCharging");
                }
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent = (float)Math.Max(0.0, chargePercent - chargeDecayRate * dt);
                }
            }
        }

        private void HandlePassiveConsumption(double dt)
        {
            if (!passiveConsumption) return;
            if (!IsFinite(dt) || dt <= 0.0) return;
            if (state != KShared.ChargablePartState.On)
            {
                _passiveUnsatisfiedFired = false;
                return;
            }

            // Preservation resources are only required while there is something in
            // the container to preserve. Empty containers neither draw power nor fire
            // the unsatisfied consequence.
            if (!HasAnyStoredResources())
            {
                _passiveUnsatisfiedFired = false;
                return;
            }

            bool satisfied = ConsumeVesselResources(_passiveNames, _passiveAmounts, dt);
            if (satisfied)
            {
                _passiveUnsatisfiedFired = false;
                return;
            }

            if (!_passiveUnsatisfiedFired)
            {
                _passiveUnsatisfiedFired = true;
                ApplyConsequence(_passiveUnsatisfiedResult, "passiveUnsatisfiedResult");
                if (state == KShared.ChargablePartState.On)
                    state = KShared.ChargablePartState.Off;
            }
        }

        private void HandleFilledUnpowered(double dt, bool treatAsUnpowered = false)
        {
            if (!treatAsUnpowered && state == KShared.ChargablePartState.On)
            {
                filledUnpoweredElapsed = 0.0;
                return;
            }

            if (!HasAnyStoredResources())
            {
                filledUnpoweredElapsed = 0.0;
                return;
            }

            if (!IsFinite(dt) || dt <= 0.0) return;

            filledUnpoweredElapsed += dt;
            if (!IsFinite(filledUnpoweredElapsed))
            {
                KShared.LogError("Filled-unpowered timer overflowed; resetting it.",
                    "KhemistryAdvancedStorage/HandleFilledUnpowered");
                filledUnpoweredElapsed = 0.0;
                return;
            }

            if (filledUnpoweredElapsed < FilledUnpoweredTickSeconds) return;

            // Apply all elapsed time at once. A per-0.1-second loop can otherwise
            // execute millions of iterations after a large physics-warp step.
            double elapsed = filledUnpoweredElapsed;
            filledUnpoweredElapsed = 0.0;
            ApplyConsequence(_filledUnpoweredResult, "filledUnpoweredResult", tickDt: elapsed);
        }

        // ── Consequence execution ──────────────────────────────────────────────────

        /// <summary>
        /// Executes a consequence. tickDt is only used by Boiloff; the configured value
        /// is a per-second rate and tickDt is the elapsed interval being processed.
        /// </summary>
        private void ApplyConsequence(ConsequenceConfig cfg, string source, double tickDt = 0.0)
        {
            switch (cfg.type)
            {
                case ConsequenceType.Off:
                    state = KShared.ChargablePartState.Off;
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
                    ApplyBoiloff(cfg.value * tickDt, source);
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
        private void ApplyBoiloff(double amountPerTick, string source)
        {
            if (!IsFinite(amountPerTick) || amountPerTick <= 0.0) return;

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
            if (names == null || amounts == null) return false;
            if (names.Count != amounts.Count) return false;
            if (names.Count == 0) return true;
            if (!IsFinite(dt) || dt <= 0.0) return false;

            // Validate the complete request before pulling anything so malformed
            // runtime state cannot cause a partial transaction.
            for (int i = 0; i < names.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])
                    || !IsFinite(amounts[i]) || amounts[i] <= 0f
                    || PartResourceLibrary.Instance == null
                    || PartResourceLibrary.Instance.GetDefinition(names[i]) == null
                    || !IsFinite(amounts[i] * dt))
                {
                    KShared.LogError("Invalid resource-consumption request.",
                        "KhemistryAdvancedStorage/ConsumeVesselResources");
                    return false;
                }
            }

            List<double> pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];

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

                if (!WasFullyTransferred(needed, got))
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (IsFinite(pulled[i]) && pulled[i] > 0.0)
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

        private void UpdateTransferBlocking()
        {
            bool shouldFreeze =
                (chargingRequired && state != KShared.ChargablePartState.On) ||
                (!chargingRequired && state == KShared.ChargablePartState.Off);

            if (shouldFreeze)
            {
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    if (!_savedFlowStates.ContainsKey(pr.resourceName))
                        _savedFlowStates[pr.resourceName] = pr.flowState;
                    pr.flowState = false;
                }
            }
            else if (_flowBlocked || _savedFlowStates.Count > 0)
            {
                RestoreSavedFlowStates();
            }
            _flowBlocked = shouldFreeze;
        }

        private void RestoreSavedFlowStates()
        {
            if (part != null)
                foreach (PartResource pr in part.Resources)
                    if (_savedFlowStates.TryGetValue(pr.resourceName, out bool flowState))
                        pr.flowState = flowState;

            _savedFlowStates.Clear();
            _flowBlocked = false;
        }

        private void RestoreStaleSavedFlowStates()
        {
            foreach (string resourceName in new List<string>(_savedFlowStates.Keys))
            {
                if (_supportedResources.Contains(resourceName)) continue;
                if (part != null)
                    foreach (PartResource resource in part.Resources)
                        if (resource.resourceName == resourceName)
                        {
                            resource.flowState = _savedFlowStates[resourceName];
                            break;
                        }
                _savedFlowStates.Remove(resourceName);
            }
        }

        private void EnforceCapacity()
        {
            foreach (PartResource pr in part.Resources)
            {
                if (!_supportedResources.Contains(pr.resourceName)) continue;
                if (IsFinite(pr.amount) && pr.amount >= 0.0) continue;

                KShared.LogWarning("Resetting invalid stored amount for resource \"" + pr.resourceName + "\".",
                    "KhemistryAdvancedStorage/EnforceCapacity");
                pr.amount = 0.0;
            }

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

                if (total > maximumResources + 1e-9)
                {
                    // Do not silently delete resources if several stock transfers overfill
                    // separate tanks in the same physics tick. Preserve the excess and expose
                    // no further free capacity until the player drains it.
                    if (!_overCapacityLogged)
                    {
                        KShared.LogWarning("Shared-capacity storage was overfilled by stock resource flow; preserving the excess.",
                            "KhemistryAdvancedStorage/EnforceCapacity");
                        _overCapacityLogged = true;
                    }
                }
                else _overCapacityLogged = false;

                double freeCapacity = Math.Max(0.0, maximumResources - total);
                // The sum of all tank headroom must equal the one shared free-capacity pool.
                // Giving every tank the full headroom lets simultaneous stock transfers exceed
                // the container capacity before the next physics update.
                double unallocated = freeCapacity;
                for (int i = 0; i < list.Count; i++)
                {
                    int tanksRemaining = list.Count - i;
                    double allocation = tanksRemaining == 1
                        ? unallocated : unallocated / tanksRemaining;
                    list[i].maxAmount = Math.Max(list[i].amount,
                        list[i].amount + allocation);
                    unallocated = Math.Max(0.0, unallocated - allocation);
                }
            }
            else
            {
                ReconcileMultiResourceState();
                bool overCapacity = false;
                foreach (PartResource pr in part.Resources)
                {
                    if (!_supportedResources.Contains(pr.resourceName)) continue;
                    pr.amount = Math.Max(pr.amount, 0.0);
                    if (pr.amount > maximumResources + 1e-9)
                        overCapacity = true;
                    pr.maxAmount = storageType == "multi" && pr.resourceName != activeResource
                        ? pr.amount
                        : Math.Max(pr.amount, maximumResources);
                }

                if (overCapacity && !_overCapacityLogged)
                    KShared.LogWarning("Storage was overfilled by stock resource flow; preserving the excess until it is drained.",
                        "KhemistryAdvancedStorage/EnforceCapacity");
                _overCapacityLogged = overCapacity;
            }
        }

        private static bool WasFullyTransferred(double requested, double actual)
        {
            if (!IsFinite(requested) || requested < 0.0 || !IsFinite(actual) || actual < 0.0)
                return false;
            return actual >= requested - Math.Max(1e-9, requested * 1e-9);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryReadOptionalFloat(ConfigNode node, string key,
            ref float destination)
        {
            if (node == null) return false;
            if (!node.HasValue(key)) return true;
            if (!float.TryParse(node.GetValue(key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed))
                return false;
            destination = parsed;
            return true;
        }

        private static bool TryReadOptionalBool(ConfigNode node, string key,
            ref bool destination)
        {
            if (node == null) return false;
            if (!node.HasValue(key)) return true;
            if (!bool.TryParse(node.GetValue(key), out bool parsed)) return false;
            destination = parsed;
            return true;
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
