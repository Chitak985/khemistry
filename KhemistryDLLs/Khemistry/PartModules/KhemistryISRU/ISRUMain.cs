using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Khemistry
{
    /// <summary>
    /// An ISRU module that uses batches and is the main Khemistry ISRU module.
    /// </summary>
    public partial class KhemistryISRU : PartModule
    {
        private void SetupActiveAnimation()
        {
            ModuleAnimationGroup animGroup = part.FindModuleImplementing<ModuleAnimationGroup>();
            string animName = (animGroup != null && !string.IsNullOrEmpty(animGroup.activeAnimationName))
                ? animGroup.activeAnimationName
                : activeAnimationNameOverride;

            if (string.IsNullOrEmpty(animName))
            {
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            Animation[] animators = part.FindModelAnimators(animName);
            if (animators.Length == 0)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No animator found for clip \"" + animName + "\".",
                    "KhemistryISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }

            _activeAnim = animators[0];
            _activeAnimationName = animName;
            _activeAnim[_activeAnimationName].wrapMode = WrapMode.Loop;

            KShared.Log(
                "Converter \"" + ConverterName + "\": Hooked active animation \"" + animName + "\""
                + (animGroup != null ? " (from ModuleAnimationGroup)." : " (from activeAnimationNameOverride)."),
                "KhemistryISRU/SetupActiveAnimation");
        }

        /// <summary>
        /// Start or stop the active animation.
        /// Will play it until it is turned off.
        /// </summary>
        /// <param name="playing">Whether to play the active animation or not.</param>
        private void SetActiveAnimationPlaying(bool playing)
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            if (playing == _animationPlaying) return;

            if (playing) _activeAnim.Play(_activeAnimationName);
            else _activeAnim.Stop(_activeAnimationName);

            _animationPlaying = playing;
        }

        /// <summary>
        /// Play the active animation once, without looping.
        /// </summary>
        private void PlayActiveAnimationOnce()
        {
            if (_activeAnim == null || string.IsNullOrEmpty(_activeAnimationName)) return;
            _activeAnim.Play(_activeAnimationName);
        }

        ///// Powerfail /////
        /// <summary>
        /// Applies a powerfail result against the current batch. PAUSE leaves the batch and
        /// converter untouched (caller is expected to just skip this tick). STOP refunds
        /// everything consumed so far this batch (via _passiveConsumedThisBatch) and resets
        /// progress, then stops the converter. VOID does the same but discards the consumed
        /// resources instead of refunding them. MAINT is VOID plus a maintenance requirement.
        /// EXPLODE destroys the part and applies falling-off heat to nearby parts.
        /// </summary>
        protected void TriggerPowerfail(Part contextPart, KhemistryISRURecipe.PowerfailResult powerfailResult,
            double explosionRadius = 0.0, double explosionTemperatureCelsius = 0.0)
        {
            KShared.Log(
                "Converter \"" + ConverterName + "\" powerfailed. Result: " + powerfailResult,
                "KhemistryISRU/TriggerPowerfail");

            switch (powerfailResult)
            {
                case KhemistryISRURecipe.PowerfailResult.Pause:
                    statusDisplay = "Paused";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Stop:
                    RefundPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Void:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail, resources lost)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Maint:
                    ClearPassiveConsumption();
                    batchProgress = 0.0;
                    isRunning = false;
                    needsMaintenance = true;
                    statusDisplay = "Needs maintenance";
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + ConverterName + "\": Requires maintenance by an Engineer.",
                        8f, ScreenMessageStyle.UPPER_CENTER));
                    break;
                case KhemistryISRURecipe.PowerfailResult.Explode:
                    KShared.TriggerExplosionWithHeat(contextPart, (float)explosionRadius, (float)explosionTemperatureCelsius);
                    break;
            }
        }

        /// <summary>
        /// Gives back everything withdrawn by passive inputs during the in-progress batch.
        /// </summary>
        protected void RefundPassiveConsumption()
        {
            if (_activeRecipe == null) return;
            for (int i = 0; i < _activeRecipe._passiveInputs.Count && i < _passiveConsumedThisBatch.Count; i++)
            {
                double amount = _passiveConsumedThisBatch[i];
                if (amount <= 0.0) continue;
                RequestResourceRouted(_activeRecipe._passiveInputs[i].resourceName, -amount,
                    _activeRecipe._passiveInputs[i].flowMode);
                _passiveConsumedThisBatch[i] = 0.0;
            }
        }

        /// <summary>
        /// Whether the vessel's current location satisfies at least one of this converter's
        /// depositCondition entries (surface or underground). Logs an error and returns false —
        /// rather than throwing — if KShared isn't loaded yet. Always true if depositCondition
        /// is empty (no restriction configured).
        /// </summary>
        protected List<string> GetRequiredDepositConditions(KhemistryISRUBiomeConfig biomeConfig)
        {
            List<string> conditions = new List<string>();
            if (_activeRecipe != null) conditions.AddRange(_activeRecipe._depositConditions);
            if (biomeConfig != null) conditions.AddRange(biomeConfig.depositConditions);
            return conditions.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        }

        protected bool IsAtRequiredDeposit(KhemistryISRUBiomeConfig biomeConfig)
        {
            List<string> conditions = GetRequiredDepositConditions(biomeConfig);
            if (conditions.Count == 0) return true;

            KShared shared = KShared.Instance;
            // KSharedMainMenu (and thus its deposit lists) may not be loaded yet.
            if (shared == null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Cannot check depositCondition — KShared instance is not loaded yet.",
                    "KhemistryISRU/IsAtRequiredDeposit");
                return false;
            }

            if (vessel == null || vessel.mainBody == null) return false;

            List<string> here = shared.SurfaceDepositsAtPoint((float)vessel.latitude, (float)vessel.longitude, vessel.mainBody.name, 0);
            here.AddRange(shared.UndergroundDepositsAtPoint((float)vessel.latitude, (float)vessel.longitude, vessel.mainBody.name, 0));
            return conditions.Any(d => here.Contains(d));
        }

        ///// Config loading /////
        /// <summary>
        /// Load the config values from part information.
        /// </summary>
        protected void LoadConfigFromPartInfo()
        {
            ConfigNode moduleNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryISRU");
            if (moduleNode == null)
            {
                // Already logged by FindModuleConfigNode — fail loudly instead of NRE-ing through
                // the rest of this method (which would otherwise be silently swallowed by Unity's
                // per-callback exception handling and never show up as a Khemistry-prefixed log).
                _fatalConfigError = true;
                statusDisplay = "ERROR: config node not found, see log";
                return;
            }
            KShared shared = KShared.Instance;

            ///// Module type /////
            moduleType = KShared.GetStrValueFromCFG(moduleNode, "moduleType", "normal");

            if (moduleType == "partEVA")
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": moduleType=partEVA is not implemented yet — falling back to normal.",
                    "KhemistryISRU/LoadConfigFromPartInfo");
                moduleType = "normal";
            }

            if (moduleType == "kerbalEVA")
            {
                // kerbalEVA-specific defaults: a bare converter with no explicit ConverterName
                // or recipeType is named "Kerbal" and imports the "kerbalEVA" recipeType.
                ConverterName = KShared.GetStrValueFromCFG(moduleNode, "ConverterName", "Kerbal");
                StartActionName = KShared.GetStrValueFromCFG(moduleNode, "StartActionName", "Start working");
                StopActionName = KShared.GetStrValueFromCFG(moduleNode, "StopActionName", "Stop working");
            }

            ///// Charging /////
            _moduleChargingRequired = chargingRequired;
            _moduleChargeRate = chargeRate;
            _moduleChargeDecayRate = chargeDecayRate;
            _moduleChargeNames.Clear();
            _moduleChargeAmounts.Clear();
            if (_moduleChargingRequired)
            {
                _moduleChargeNames.AddRange(KShared.GetChargingFromCFG(moduleNode, out List<float> moduleChargeAmounts));
                _moduleChargeAmounts.AddRange(moduleChargeAmounts);
            }

            ///// Recipes: local RECIPE nodes /////
            recipes.Clear();
            if (moduleNode.HasNode("RECIPE"))
            {
                foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
                {
                    ConfigNode mergedNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, recipeNode);
                    recipes.Add(new KhemistryISRURecipe(mergedNode, ConverterName));
                }
            }

            ///// Recipes: imported by name (RECIPE_NAMES & RECIPE_MULTIPLIERS) /////
            recipeMultiplier = KShared.GetFloatValueFromCFG(moduleNode, "recipeMultiplier", 1f);

            recipeType = KShared.GetStrValueFromCFG(moduleNode, "recipeType", moduleType == "kerbalEVA" ? "kerbalEVA" : null);
            recipeSubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubtype", null);
            recipeSubsubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubsubtype", null);

            _recipeNames.Clear();
            _recipeMultipliers.Clear();
            if (moduleNode.HasNode("RECIPE_NAMES"))
            {
                if (!moduleNode.GetNode("RECIPE_NAMES").HasValue("name"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": Node RECIPE_NAMES is present but has no \"name\" values, skipping.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                else
                    _recipeNames.AddRange(moduleNode.GetNode("RECIPE_NAMES").GetValues("name"));

                if (moduleNode.HasNode("RECIPE_MULTIPLIERS"))
                {
                    foreach (string amt in moduleNode.GetNode("RECIPE_MULTIPLIERS").GetValues("amount"))
                        if (float.TryParse(amt, NumberStyles.Float, CultureInfo.InvariantCulture, out float mTmp))
                            _recipeMultipliers.Add(mTmp);

                    if (_recipeMultipliers.Count != _recipeNames.Count)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": RECIPE_NAMES and RECIPE_MULTIPLIERS have unequal counts ("
                            + _recipeNames.Count + ", " + _recipeMultipliers.Count + ") — ignoring RECIPE_MULTIPLIERS.",
                            "KhemistryISRU/LoadConfigFromPartInfo");
                        _recipeMultipliers.Clear();
                    }
                }
            }
            else if (moduleNode.HasNode("RECIPE_MULTIPLIERS"))
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Node RECIPE_MULTIPLIERS is present but no RECIPE_NAMES node is present.",
                    "KhemistryISRU/LoadConfigFromPartInfo");

            if (shared != null)
            {
                if (_recipeNames.Count > 0)
                {
                    for (int i = 0; i < _recipeNames.Count; i++)
                    {
                        string wantedName = _recipeNames[i];
                        KhemistryISRURecipe found = shared.batchRecipeList.FirstOrDefault(r => r._name == wantedName);
                        if (found == null)
                        {
                            KShared.LogError(
                                "Converter \"" + ConverterName + "\": RECIPE_NAMES entry \"" + wantedName
                                + "\" does not match any loaded KHEMISTRYISRU_RECIPE.",
                                "KhemistryISRU/LoadConfigFromPartInfo");
                            continue;
                        }
                        // Global recipeMultiplier and the per-name RECIPE_MULTIPLIERS entry stack:
                        // global is applied first, then the local (per-name) multiplier.
                        float localMult = (i < _recipeMultipliers.Count) ? _recipeMultipliers[i] : 1f;
                        ConfigNode mergedFoundNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, found.mainNode);
                        KhemistryISRURecipe overriddenFound = new KhemistryISRURecipe(mergedFoundNode, ConverterName);
                        recipes.Add(overriddenFound.ScaledCopy(recipeMultiplier * localMult));
                    }
                }
                if (recipeType != null || recipeSubtype != null || recipeSubsubtype != null)
                {
                    foreach (KhemistryISRURecipe candidate in shared.batchRecipeList)
                    {
                        if (candidate.MatchesTypes(recipeType, recipeSubtype, recipeSubsubtype))
                        {
                            ConfigNode mergedCandidateNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, candidate.mainNode);
                            KhemistryISRURecipe overriddenCandidate = new KhemistryISRURecipe(mergedCandidateNode, ConverterName);

                            if (recipes.Any(recipe => recipe._name == overriddenCandidate._name))
                                continue;

                            recipes.Add(overriddenCandidate.ScaledCopy(recipeMultiplier));
                        }
                    }
                }
            }

            if (recipes.Count == 0)
            {
                _fatalConfigError = true;
                KShared.LogError("Converter \"" + ConverterName + "\": No recipes were loaded!",
                        "KhemistryISRU/LoadSharedConfig");
                return;
            }

            if (moduleType == "kerbalEVA")
            {
                StripKerbalEVAIncompatibleFields(moduleNode);
                // Interaction/display distance are meaningless here — the GUI lives on the
                // kerbal itself, so it's always "in range" of its own converter.
                if (moduleNode.HasValue("maxInteractionDistance"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"maxInteractionDistance\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                if (moduleNode.HasValue("maxDisplayDistance"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"maxDisplayDistance\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                if (moduleNode.HasValue("workersCrewSamePart"))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"workersCrewSamePart\" is ignored.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                _configMaxInteractionDistance = float.MaxValue;
                _configMaxDisplayDistance = float.MaxValue;
            }
            else
            {
                if (bool.TryParse(KShared.GetStrValueFromCFG(moduleNode, "workersCrewSamePart", "false"), out bool wcspTmp))
                    workersCrewSamePart = wcspTmp;
                _configMaxInteractionDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxInteractionDistance", _configMaxInteractionDistance);
                _configMaxDisplayDistance = KShared.GetFloatValueFromCFG(moduleNode, "maxDisplayDistance", _configMaxDisplayDistance);
            }
            _maxInteractionDistance = _configMaxInteractionDistance;
            _maxDisplayDistance = _configMaxDisplayDistance;

            ///// Select active recipe /////
            KhemistryISRURecipe initial = null;
            if (!string.IsNullOrEmpty(activeRecipeName))
                initial = recipes.FirstOrDefault(r => r._name == activeRecipeName);
            bool restoringSavedRecipe = initial != null;
            if (initial == null) initial = recipes[0];
            ApplyRecipe(initial, resetProgress: !restoringSavedRecipe);
        }

        /// <summary>
        /// Makes the given recipe the active one: applies its own charging fields
        /// (falling back to module-level charging if the recipe doesn't define its own),
        /// resets batch progress, and updates control show-rules.
        /// </summary>
        protected void ApplyRecipe(KhemistryISRURecipe recipe, bool resetProgress = true)
        {
            _activeRecipe = recipe;
            activeRecipeName = recipe._name;
            if (resetProgress) batchProgress = 0.0;

            _passiveTimers.Clear();
            _passiveConsumedThisBatch.Clear();
            for (int i = 0; i < recipe._passiveInputs.Count; i++)
            {
                _passiveTimers.Add(!resetProgress && i < _loadedPassiveTimers.Count ? _loadedPassiveTimers[i] : 0.0);
                _passiveConsumedThisBatch.Add(!resetProgress && i < _loadedPassiveConsumed.Count ? _loadedPassiveConsumed[i] : 0.0);
            }
            _loadedPassiveTimers.Clear();
            _loadedPassiveConsumed.Clear();

            if (recipe._chargingRequired)
            {
                chargingRequired = true;
                chargeRate = recipe._chargeRate;
                chargeDecayRate = recipe._chargeDecay;
                _chargeNames.Clear();
                _chargeNames.AddRange(recipe._chargeNames);
                _chargeAmounts.Clear();
                _chargeAmounts.AddRange(recipe._chargeAmounts);
            }
            else
            {
                chargingRequired = _moduleChargingRequired;
                chargeRate = _moduleChargeRate;
                chargeDecayRate = _moduleChargeDecayRate;
                _chargeNames.Clear();
                _chargeNames.AddRange(_moduleChargeNames);
                _chargeAmounts.Clear();
                _chargeAmounts.AddRange(_moduleChargeAmounts);
            }

            _controlsShowPAW = recipe._controlsShowPAW;
            _controlsShowEVA = recipe._controlsShowEVA;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _loadedPassiveTimers.Clear();
            _loadedPassiveConsumed.Clear();
            _pendingMaterialOutputNodes.Clear();

            ConfigNode passiveNode = node.GetNode("PASSIVE_INPUT_STATE");
            if (passiveNode != null)
            {
                foreach (string raw in passiveNode.GetValues("timer"))
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        _loadedPassiveTimers.Add(Math.Max(0.0, value));
                foreach (string raw in passiveNode.GetValues("consumed"))
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        _loadedPassiveConsumed.Add(Math.Max(0.0, value));
            }

            foreach (ConfigNode bufferedNode in node.GetNodes("MATERIAL_OUTPUT_BUFFER"))
            {
                ConfigNode copy = new ConfigNode("MATERIAL_OUTPUT_BUFFER");
                bufferedNode.CopyTo(copy);
                _pendingMaterialOutputNodes.Add(copy);
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            if (_passiveTimers.Count > 0 || _passiveConsumedThisBatch.Count > 0)
            {
                ConfigNode passiveNode = new ConfigNode("PASSIVE_INPUT_STATE");
                foreach (double timer in _passiveTimers)
                    passiveNode.AddValue("timer", timer.ToString("R", CultureInfo.InvariantCulture));
                foreach (double consumed in _passiveConsumedThisBatch)
                    passiveNode.AddValue("consumed", consumed.ToString("R", CultureInfo.InvariantCulture));
                node.AddNode(passiveNode);
            }

            foreach (KeyValuePair<KhemistryISRURecipe.ResourceOutputMaterial, double> buffered in _materialOutputAmount)
            {
                if (buffered.Value <= 0.0) continue;
                KhemistryISRURecipe.ResourceOutputMaterial material = buffered.Key;
                ConfigNode outputNode = new ConfigNode("MATERIAL_OUTPUT_BUFFER");
                outputNode.AddValue("name", material.name ?? "");
                outputNode.AddValue("shape", material.shape ?? "");
                outputNode.AddValue("size", material.size ?? "");
                outputNode.AddValue("amount", buffered.Value.ToString("R", CultureInfo.InvariantCulture));
                outputNode.AddValue("outVolume", material.outVolume ?? "0");
                ConfigNode paramsNode = new ConfigNode("PARAMS");
                foreach (KeyValuePair<string, string> parameter in material.parameters)
                    paramsNode.AddValue(parameter.Key, parameter.Value);
                outputNode.AddNode(paramsNode);
                node.AddNode(outputNode);
            }
        }

        private static bool OutputMaterialsEquivalent(KhemistryISRURecipe.ResourceOutputMaterial left,
            KhemistryISRURecipe.ResourceOutputMaterial right)
        {
            if (left.name != right.name || left.shape != right.shape || left.size != right.size
                || left.outVolume != right.outVolume || left.parameters.Count != right.parameters.Count)
                return false;
            foreach (KeyValuePair<string, string> parameter in left.parameters)
                if (!right.parameters.TryGetValue(parameter.Key, out string value) || value != parameter.Value)
                    return false;
            return true;
        }

        private void RestoreMaterialOutputBuffer()
        {
            _materialOutputAmount.Clear();
            foreach (ConfigNode outputNode in _pendingMaterialOutputNodes)
            {
                if (!double.TryParse(outputNode.GetValue("amount"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)
                    || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                    continue;

                Dictionary<string, string> parameters = outputNode.HasNode("PARAMS")
                    ? KShared.NodeToDictionary(outputNode.GetNode("PARAMS"))
                    : new Dictionary<string, string>();
                KhemistryISRURecipe.ResourceOutputMaterial restored = new KhemistryISRURecipe.ResourceOutputMaterial
                {
                    name = outputNode.GetValue("name"),
                    shape = outputNode.GetValue("shape"),
                    size = outputNode.GetValue("size"),
                    usesParams = parameters.Count > 0,
                    parameters = parameters,
                    amount = 0.0,
                    outVolume = outputNode.GetValue("outVolume")
                };

                bool matched = false;
                foreach (KhemistryISRURecipe.ResourceOutputMaterial configured in
                         recipes.SelectMany(recipe => recipe._outputMaterials))
                {
                    if (!OutputMaterialsEquivalent(restored, configured)) continue;
                    restored = configured;
                    matched = true;
                    break;
                }

                if (!matched && string.IsNullOrEmpty(restored.name)) continue;
                if (_materialOutputAmount.ContainsKey(restored)) _materialOutputAmount[restored] += amount;
                else _materialOutputAmount.Add(restored, amount);
            }
            _pendingMaterialOutputNodes.Clear();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            // Peek moduleType early (before LoadConfigFromPartInfo runs its full parse) so the
            // kerbal-host / duplicate checks below can bail out before doing anything else.
            ConfigNode precheckNode = KShared.FindModuleConfigNode(part, ConverterName, "KhemistryISRU");
            moduleType = KShared.GetStrValueFromCFG(precheckNode, "moduleType", "normal");

            if (moduleType == "kerbalEVA")
            {
                _kerbalHost = part.FindModuleImplementing<KhemistryKerbal>();
                if (_kerbalHost == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" has moduleType=kerbalEVA but part \"" + part.name
                        + "\" has no KhemistryKerbal module — this module only works on a kerbal part.",
                        "KhemistryISRU/OnStart");
                    _fatalConfigError = true;
                    DisableAllUI();
                    statusDisplay = "ERROR: not on a kerbal part, see log";
                    return;
                }

                // Only one kerbalEVA-type KhemistryISRU may run per kerbal. Rather than
                // re-deriving each sibling's moduleType from its (possibly already-renamed)
                // ConverterName — which breaks once the first instance renames itself to
                // "Kerbal" — claim a slot on the (already deduplicated, singular) KhemistryKerbal
                // host. This is robust regardless of how many duplicate module instances KSP's
                // EVA-construct/DLC part assembly ends up creating, and in what order they start.
                if (_kerbalHost.kerbalEVAISRU != null && _kerbalHost.kerbalEVAISRU != this)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": another kerbalEVA KhemistryISRU is already present on this kerbal — only one is allowed, disabling this one.",
                        "KhemistryISRU/OnStart");
                    _fatalConfigError = true;
                    DisableAllUI();
                    statusDisplay = "ERROR: duplicate kerbalEVA module, see log";
                    return;
                }
                _kerbalHost.kerbalEVAISRU = this;
            }

            LoadConfigFromPartInfo();

            if (_fatalConfigError)
            {
                DisableAllUI();
                statusDisplay = "ERROR: see log";
                return;
            }

            RestoreMaterialOutputBuffer();

            Fields["statusDisplay"].guiActiveUnfocused = true;
            Fields["chargeDisplay"].guiActiveUnfocused = true;
            Fields["progressDisplay"].guiActiveUnfocused = true;
            Fields["stateDisplay"].guiActiveUnfocused = true;

            Events["StartConverter"].guiName = StartActionName;
            Events["StopConverter"].guiName = StopActionName;
            Actions["StartConverterAction"].guiName = StartActionName;
            Actions["StopConverterAction"].guiName = StopActionName;

            ApplyInteractionRanges();

            if (!chargingRequired)
                this.state = KShared.ChargablePartState.On;

            _runtimeData = new KhemistryRuntimeData(vessel);  // vessel could be null

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        protected void UpdateEventVisibility()
        {
            _maxInteractionDistance = _configMaxInteractionDistance;
            _maxDisplayDistance = _configMaxDisplayDistance;
            if (_activeRecipe != null && _runtimeData != null)
            {
                KhemistryISRUBiomeConfig currentBiome = _activeRecipe.GetBiomeConfig(
                    _runtimeData.planet, _runtimeData.biome);
                if (currentBiome != null)
                {
                    _maxInteractionDistance = _configMaxInteractionDistance
                        * (float)currentBiome.maxInteractionDistanceMultiplier;
                    _maxDisplayDistance = _configMaxDisplayDistance
                        * (float)currentBiome.maxDisplayDistanceMultiplier;
                }
            }
            ApplyInteractionRanges();
            ApplyShowRule(Events["StartConverter"],
                showPAW: !isRunning && !needsMaintenance && _controlsShowPAW,
                showEVA: !isRunning && !needsMaintenance && _controlsShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: isRunning && _controlsShowPAW,
                showEVA: isRunning && _controlsShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;

            ApplyShowRule(Events["SwitchRecipe"],
                showPAW: !isRunning && recipes.Count > 1 && _controlsShowPAW,
                showEVA: !isRunning && recipes.Count > 1 && _controlsShowEVA);
        }

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private struct ResourceDraw
        {
            public string name;
            public double amount;
            public ResourceFlowMode flowMode;
        }

        private sealed class MaterialRemovalRecord
        {
            public KhemistryMaterialStorage storage;
            public KhemistryKerbal suitHost;
            public List<KhemistryMaterialInstance> pieces;
        }

        private bool ConsumeVesselResources(List<string> names, List<float> amounts,
            List<ResourceFlowMode> flowModes, double dt, out List<ResourceDraw> draws)
        {
            draws = new List<ResourceDraw>();
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count || names.Count != flowModes.Count) return false;

            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) continue;

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryISRU/ConsumeVesselResources");
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = RequestResourceRouted(names[i], needed, flowModes[i]);
                if (got > 0.0)
                    draws.Add(new ResourceDraw { name = names[i], amount = got, flowMode = flowModes[i] });

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                RefundResourceDraws(draws);
                draws.Clear();
                return false;
            }

            return true;
        }

        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            List<ResourceFlowMode> modes = Enumerable.Repeat(ResourceFlowMode.STAGE_PRIORITY_FLOW, names.Count).ToList();
            return ConsumeVesselResources(names, amounts, modes, dt, out _);
        }

        private void RefundResourceDraws(IEnumerable<ResourceDraw> draws)
        {
            foreach (ResourceDraw draw in draws.Reverse())
                RequestResourceRouted(draw.name, -draw.amount, draw.flowMode);
        }

        /// <summary>
        /// Pulls the given input material from the vessel network. Returns true only if the
        /// material was fully satisfied.
        /// </summary>
        private bool ConsumeVesselMaterials(KhemistryISRURecipe.ResourceInputMaterial material, int amount,
            List<MaterialRemovalRecord> transaction)
        {
            if (amount <= 0) return true;

            if (moduleType == "kerbalEVA" && _kerbalHost != null)
            {
                if (!_kerbalHost.TryRemoveMaterialFromSuitCell(material.name, material.shape, material.size,
                        material.parameters, amount, out List<KhemistryMaterialInstance> removed))
                    return false;
                transaction.Add(new MaterialRemovalRecord { suitHost = _kerbalHost, pieces = removed });
                return true;
            }

            List<KhemistryMaterialStorage> storages = vessel.parts
                .SelectMany(vesselPart => vesselPart.Modules.OfType<KhemistryMaterialStorage>())
                .ToList();
            int available = storages.Sum(storage => storage.GetMatchingMaterialAmount(
                material.name, material.shape, material.size, material.parameters));
            if (available < amount) return false;

            int remaining = amount;
            foreach (KhemistryMaterialStorage storage in storages)
            {
                int availableHere = storage.GetMatchingMaterialAmount(
                    material.name, material.shape, material.size, material.parameters);
                int take = Math.Min(remaining, availableHere);
                if (take <= 0) continue;

                if (!storage.TryRemoveMaterial(material.name, material.shape, material.size,
                        material.parameters, take, out List<KhemistryMaterialInstance> removed))
                    return false;
                transaction.Add(new MaterialRemovalRecord { storage = storage, pieces = removed });
                remaining -= take;
                if (remaining == 0) return true;
            }
            return false;
        }

        private void RefundMaterialRemovals(IEnumerable<MaterialRemovalRecord> transaction)
        {
            foreach (MaterialRemovalRecord record in transaction.Reverse())
            {
                foreach (KhemistryMaterialInstance piece in record.pieces)
                {
                    if (record.suitHost != null) record.suitHost.TryAddMaterialToSuitCell(piece);
                    else record.storage?.AddMaterial(piece);
                }
            }
        }

        public void UpdateUI()
        {
            chargeDisplay = chargingRequired
                ? string.Format("{0:F1}%", chargePercent)
                : "N/A";

            if (state == KShared.ChargablePartState.On)
                stateDisplay = "Ready";
            else
                stateDisplay = state.ToString();

            Events["EnableCharging"].active = chargingRequired && state != KShared.ChargablePartState.Charging && state != KShared.ChargablePartState.On;
            Events["DisableCharging"].active = chargingRequired && state == KShared.ChargablePartState.Charging;
            Events["TurnOnConverter"].active = state != KShared.ChargablePartState.On;
            Events["TurnOffConverter"].active = state == KShared.ChargablePartState.On;
        }

        public void HandleCharging(double dt)
        {
            if (!chargingRequired) return;

            KhemistryISRUBiomeConfig biomeConfig = _activeRecipe != null && _runtimeData != null
                ? _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome)
                : null;
            double decayMultiplier = biomeConfig?.chargeDecayMultiplier ?? 1.0;
            double rateMultiplier = biomeConfig?.chargeRateMultiplier ?? 1.0;
            double consumptionMultiplier = biomeConfig?.chargeConsumptionMultiplier ?? 1.0;

            if (state == KShared.ChargablePartState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)(dt * decayMultiplier);
                    if (chargePercent < 0f)
                        chargePercent = 0f;
                }
                return;
            }

            if (state != KShared.ChargablePartState.Charging) return;

            if (chargePercent >= 100f)
            {
                chargePercent = 100f;
                state = KShared.ChargablePartState.On;
                KShared.Log("Converter fully charged, now ON.",
                    "KhemistryISRU/HandleCharging");
                return;
            }

            List<float> scaledChargeAmounts = _chargeAmounts
                .Select(amount => (float)(amount * consumptionMultiplier))
                .ToList();
            bool satisfied = ConsumeVesselResources(_chargeNames, scaledChargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)(dt * rateMultiplier);
                if (chargePercent > 100f)
                    chargePercent = 100f;
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)(dt * decayMultiplier);
                    if (chargePercent < 0f)
                        chargePercent = 0f;
                }
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null || part == null) return;
            if (_fatalConfigError) return;

            _runtimeData.Update(vessel);

            double dt = TimeWarp.fixedDeltaTime;
            HandleCharging(dt);
            UpdateUI();
            TryTransferMaterialOutputBuffer();
            UpdateEventVisibility();

            if (needsMaintenance || !isRunning || state != KShared.ChargablePartState.On || _activeRecipe == null)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : (!isRunning ? "Stopped" : "Not ready");
                progressDisplay = "Off";
                SetActiveAnimationPlaying(false);
                return;
            }

            RunBatchCycle(dt);
            SetActiveAnimationPlaying(true);
        }

        /// <summary>
        /// Check the current BIOME_CONFIG to update values and explode if needed.
        /// </summary>
        /// <param name="biomeConfig">The <see cref="KhemistryISRUBiomeConfig"/> fetched outside the function.</param>
        /// <returns>If <see langword="true"/>, an explosion or error happened.</returns>
        protected bool CheckBiomeConfig(KhemistryISRUBiomeConfig biomeConfig)
        {
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryISRU/CheckBiomeConfig");
                return true;
            }

            // One hundred and one ways to explode
            if (biomeConfig.situationDestructive.Contains(_runtimeData.sitCon) ||
                _runtimeData.alt < biomeConfig.minAltitude || _runtimeData.alt > biomeConfig.maxAltitude ||
                _runtimeData.g < biomeConfig.minG || _runtimeData.g > biomeConfig.maxG ||
                _runtimeData.temperature < biomeConfig.minTemperature || _runtimeData.temperature > biomeConfig.maxTemperature ||
                _runtimeData.pressure < biomeConfig.minPressure || _runtimeData.pressure > biomeConfig.maxPressure)
            {
                TriggerPowerfail(part, KhemistryISRURecipe.PowerfailResult.Explode);
                return true;
            }

            // Apply multipliers
            _maxInteractionDistance = _configMaxInteractionDistance * (float)biomeConfig.maxInteractionDistanceMultiplier;
            _maxDisplayDistance = _configMaxDisplayDistance * (float)biomeConfig.maxDisplayDistanceMultiplier;
            ApplyInteractionRanges();

            return false;
        }

        /// <summary>
        /// Looks up the applicable biome config for the active recipe at the vessel's current
        /// location and, if found and operable, advances batch progress; consumes the full
        /// batch of inputs and produces the full batch of outputs once recipeTime is reached.
        /// </summary>
        protected void RunBatchCycle(double dt)
        {
            // Reflects pre-tick progress for every early-return branch below (converter is
            // "on" but may be paused this tick); recomputed again once progress actually advances.
            progressDisplay = FormatProgress(batchProgress, _activeRecipe._recipeTime);

            KhemistryISRUBiomeConfig biomeConfig = _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome);
            if (biomeConfig == null)
            {
                statusDisplay = "ERROR, please report this to the dev with the KSP.log.";
                KShared.LogError($"Biome config is null for recipe \"{_activeRecipe._name}\" on planet \"{_runtimeData.planet}\" in biome \"{_runtimeData.biome}\"!",
                    "KhemistryISRU/RunBatchCycle");
                return;
            }

            if (CheckBiomeConfig(biomeConfig))
                return;

            if (biomeConfig.disabled)
            {
                statusDisplay = "Disabled in this biome";
                return;
            }

            if (biomeConfig.situationOperating.Count > 0 && !biomeConfig.situationOperating.Contains(_runtimeData.sitCon))
            {
                statusDisplay = "Wrong situation (" + _runtimeData.sitCon + ")";
                return;
            }

            if (!IsAtRequiredDeposit(biomeConfig))
            {
                statusDisplay = "Not at a required deposit";
                return;
            }

            if (_runtimeData.alt < biomeConfig.minOperatingAltitude || _runtimeData.alt > biomeConfig.maxOperatingAltitude)
            {
                statusDisplay = "Out of operating altitude range";
                return;
            }

            if (_runtimeData.g < biomeConfig.minOperatingG || _runtimeData.g > biomeConfig.maxOperatingG)
            {
                statusDisplay = "Out of operating G range";
                return;
            }

            if (_runtimeData.temperature < biomeConfig.minOperatingTemperature || _runtimeData.temperature > biomeConfig.maxOperatingTemperature)
            {
                statusDisplay = "Out of operating temperature range";
                return;
            }

            if (_runtimeData.pressure < biomeConfig.minOperatingPressure || _runtimeData.pressure > biomeConfig.maxOperatingPressure)
            {
                statusDisplay = "Out of operating pressure range";
                return;
            }

            if (!CountWorkers(out uint engineers, out uint pilots, out uint scientists))
            {
                statusDisplay = "No workers nearby";
                return;
            }

            double reqEngineers = _activeRecipe._workersEngineers * biomeConfig.workersEngineersMultiplier;
            double reqPilots = _activeRecipe._workersPilots * biomeConfig.workersPilotsMultiplier;
            double reqScientists = _activeRecipe._workersScientists * biomeConfig.workersScientistsMultiplier;

            if (engineers < reqEngineers || pilots < reqPilots || scientists < reqScientists)
            {
                statusDisplay = "Insufficient workers";
                return;
            }

            if (!ProcessPassiveInputs(biomeConfig, dt))
            {
                progressDisplay = FormatProgress(batchProgress, _activeRecipe._recipeTime);
                return;
            }

            batchProgress += dt * biomeConfig.speedMul;

            double effectiveRecipeTime = _activeRecipe._recipeTime;
            progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);

            if (batchProgress < effectiveRecipeTime)
            {
                statusDisplay = string.Format("Running ({0:F0}%)", 100.0 * batchProgress / Math.Max(effectiveRecipeTime, 1e-6));
                return;
            }

            if (!TryRunBatch(biomeConfig))
            {
                statusDisplay = "Insufficient resources / no output space";
                return;
            }

            batchProgress -= effectiveRecipeTime;
            if (batchProgress < 0.0) batchProgress = 0.0;
            ClearPassiveConsumption();
            progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);
            statusDisplay = "Batch complete";
            PlayActiveAnimationOnce();
        }

        /// <summary>
        /// Processes every PINPUT_RESOURCE on the active recipe: consumes <c>amount</c> every
        /// <c>period</c> seconds, tracking the cumulative amount taken from each since the batch
        /// last completed/reset. On insufficient resource: if ignorePowerfail, silently skips
        /// consumption for that tick and the batch continues normally; otherwise applies the
        /// configured powefail result — PAUSE (default) just stalls this tick, STOP refunds
        /// everything consumed so far this batch and halts the converter, VOID/MAINT discard
        /// it instead (MAINT also requiring maintenance), and EXPLODE destroys the part with
        /// falling-off heat. Returns false if the current tick's batch progress should not be
        /// advanced (anything other than a successful tick).
        /// </summary>
        protected bool ProcessPassiveInputs(KhemistryISRUBiomeConfig biomeConfig, double dt)
        {
            if (_activeRecipe._passiveInputs.Count == 0) return true;

            for (int i = 0; i < _activeRecipe._passiveInputs.Count; i++)
            {
                KhemistryISRURecipe.PassiveResourceInput pinp = _activeRecipe._passiveInputs[i];
                double timer = (i < _passiveTimers.Count) ? _passiveTimers[i] : 0.0;
                timer += dt;
                double effectivePeriod = pinp.period * biomeConfig.passivePeriodMultiplier;
                if (effectivePeriod <= 0.0) effectivePeriod = pinp.period;

                while (timer >= effectivePeriod)
                {
                    timer -= effectivePeriod;
                    double needed = pinp.amount * biomeConfig.inputMultiplier * biomeConfig.passiveMultiplier;
                    if (needed <= 0.0) continue;

                    double got = RequestResourceRouted(pinp.resourceName, needed, pinp.flowMode);

                    if (got < needed * 0.999)
                    {
                        // Passive consumption is all-or-nothing per tick — refund any partial draw.
                        if (got > 0.0) RequestResourceRouted(pinp.resourceName, -got, pinp.flowMode);

                        if (pinp.ignorePowerfail)
                            continue;  // "nothing happens" — resource just isn't consumed this tick

                        if (i < _passiveTimers.Count) _passiveTimers[i] = timer;

                        if (pinp.powerfail == KhemistryISRURecipe.PowerfailResult.Pause)
                            statusDisplay = "Paused: out of " + pinp.resourceName;

                        TriggerPowerfail(part, pinp.powerfail, pinp.powerfailExplosionRadius, pinp.powerfailExplosionTemperature);
                        return false;
                    }

                    if (i < _passiveConsumedThisBatch.Count) _passiveConsumedThisBatch[i] += needed;
                }

                if (i < _passiveTimers.Count) _passiveTimers[i] = timer;
            }

            return true;
        }

        /// <summary>
        /// Attempts to consume a full batch of the active recipe's INPUT_RESOURCE amounts
        /// (all-or-nothing) and, if successful, produces the OUTPUT_RESOURCE amounts and
        /// buffers OUTPUT_MATERIAL production for KhemistryMaterialStorage pickup.
        /// </summary>
        private struct PreparedResourceOutput
        {
            public string name;
            public double amount;
            public bool dumpExcess;
        }

        private List<PreparedResourceOutput> PrepareResourceOutputs(KhemistryISRUBiomeConfig biomeConfig)
        {
            List<PreparedResourceOutput> outputs = new List<PreparedResourceOutput>();
            foreach (KhemistryISRURecipe.ResourceOutput output in _activeRecipe._outputs)
            {
                double amount = output.amount * biomeConfig.outputMultiplier;
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) continue;
                outputs.Add(new PreparedResourceOutput
                {
                    name = output.resourceName,
                    amount = amount,
                    dumpExcess = output.dumpExcess
                });
            }
            return outputs;
        }

        private void RollBackProducedResources(IEnumerable<ResourceDraw> produced)
        {
            foreach (ResourceDraw output in produced.Reverse())
                RequestResourceRouted(output.name, output.amount, output.flowMode);
        }

        private bool HasRequiredOutputSpace(List<PreparedResourceOutput> outputs)
        {
            List<ResourceDraw> probeOutputs = new List<ResourceDraw>();
            bool enoughSpace = true;
            foreach (PreparedResourceOutput output in outputs.Where(value => !value.dumpExcess))
            {
                double added = Math.Abs(RequestResourceRouted(output.name, -output.amount));
                if (added > 0.0)
                    probeOutputs.Add(new ResourceDraw
                    {
                        name = output.name,
                        amount = added,
                        flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW
                    });
                if (added < output.amount * 0.999)
                {
                    enoughSpace = false;
                    break;
                }
            }
            RollBackProducedResources(probeOutputs);
            return enoughSpace;
        }

        private bool CommitResourceOutputs(List<PreparedResourceOutput> outputs)
        {
            List<ResourceDraw> committed = new List<ResourceDraw>();

            // Outputs that may not be dumped go first. The preflight above should make each
            // request succeed in full; any unexpected shortfall rolls the entire output set back.
            foreach (PreparedResourceOutput output in outputs.Where(value => !value.dumpExcess))
            {
                double added = Math.Abs(RequestResourceRouted(output.name, -output.amount));
                if (added > 0.0)
                    committed.Add(new ResourceDraw
                    {
                        name = output.name,
                        amount = added,
                        flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW
                    });
                if (added < output.amount * 0.999)
                {
                    RollBackProducedResources(committed);
                    return false;
                }
            }

            // Dumpable output is all-or-nothing per entry. If a full amount cannot be stored,
            // remove the partial probe and silently dump the complete output amount.
            foreach (PreparedResourceOutput output in outputs.Where(value => value.dumpExcess))
            {
                double added = Math.Abs(RequestResourceRouted(output.name, -output.amount));
                if (added >= output.amount * 0.999)
                {
                    committed.Add(new ResourceDraw
                    {
                        name = output.name,
                        amount = added,
                        flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW
                    });
                }
                else if (added > 0.0)
                {
                    RequestResourceRouted(output.name, added);
                }
            }
            return true;
        }

        private static int ScaleDiscreteMaterialAmount(int amount, double multiplier)
        {
            if (amount <= 0 || multiplier <= 0.0) return 0;
            double scaled = amount * multiplier;
            if (scaled >= int.MaxValue) return int.MaxValue;
            int result = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return Math.Max(1, result);
        }

        protected bool TryRunBatch(KhemistryISRUBiomeConfig biomeConfig)
        {
            List<PreparedResourceOutput> outputs = PrepareResourceOutputs(biomeConfig);

            List<MaterialRemovalRecord> materialTransaction = new List<MaterialRemovalRecord>();
            foreach (KhemistryISRURecipe.ResourceInputMaterial material in _activeRecipe._inputMaterials)
            {
                int amount = ScaleDiscreteMaterialAmount(material.amount, biomeConfig.inputMultiplier);
                if (!ConsumeVesselMaterials(material, amount, materialTransaction))
                {
                    RefundMaterialRemovals(materialTransaction);
                    return false;
                }
            }

            List<string> names = new List<string>();
            List<float> amounts = new List<float>();
            List<ResourceFlowMode> flowModes = new List<ResourceFlowMode>();

            foreach (var inp in _activeRecipe._inputs)
            {
                names.Add(inp.resourceName);
                amounts.Add((float)(inp.amount * biomeConfig.inputMultiplier));
                flowModes.Add(inp.flowMode);
            }
            if (!ConsumeVesselResources(names, amounts, flowModes, 1.0, out List<ResourceDraw> resourceDraws))
            {
                RefundMaterialRemovals(materialTransaction);
                return false;
            }

            if (!HasRequiredOutputSpace(outputs))
            {
                RefundResourceDraws(resourceDraws);
                RefundMaterialRemovals(materialTransaction);
                return false;
            }

            if (!CommitResourceOutputs(outputs))
            {
                RefundResourceDraws(resourceDraws);
                RefundMaterialRemovals(materialTransaction);
                return false;
            }

            foreach (var mat in _activeRecipe._outputMaterials)
            {
                double amount = mat.amount * biomeConfig.outputMultiplier;
                if (amount <= 0.0) continue;
                if (!_materialOutputAmount.ContainsKey(mat)) _materialOutputAmount.Add(mat, 0.0);
                _materialOutputAmount[mat] += amount;
            }

            return true;
        }

        /// <summary>
        /// Counts nearby engineer/pilot/scientist workers for the active recipe's workersType:
        /// CREW counts seated crew on this vessel (optionally restricted to this part if
        /// workersCrewSamePart is set), EVA counts a nearby EVA kerbal within maxInteractionDistance.
        /// </summary>
        protected bool CountWorkers(out uint engineers, out uint pilots, out uint scientists)
        {
            engineers = 0; pilots = 0; scientists = 0;
            bool anyWorkerTypeAllowed = _activeRecipe._workersEVA || _activeRecipe._workersCREW;
            if (!anyWorkerTypeAllowed) return true;  // No workers required at all

            if (_activeRecipe._workersCREW)
            {
                IEnumerable<ProtoCrewMember> crew = workersCrewSamePart
                    ? part.protoModuleCrew
                    : vessel.GetVesselCrew();

                foreach (ProtoCrewMember c in crew)
                {
                    if (c.trait == "Engineer") engineers++;
                    else if (c.trait == "Pilot") pilots++;
                    else if (c.trait == "Scientist") scientists++;
                }
            }

            if (_activeRecipe._workersEVA)
            {
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null || !v.isEVA || v.loaded == false) continue;
                    double dist = Vector3d.Distance(v.transform.position, part.transform.position);
                    if (dist > _maxInteractionDistance) continue;

                    foreach (ProtoCrewMember c in v.GetVesselCrew())
                    {
                        if (c.trait == "Engineer") engineers++;
                        else if (c.trait == "Pilot") pilots++;
                        else if (c.trait == "Scientist") scientists++;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// If the given value is a randf(a,b,n) expression, replaces it with a random float
        /// between a and b (inclusive on both ends), rounded to n decimal places. Negative n is
        /// an error and is treated as 0. Non-randf values are returned unchanged.
        /// </summary>
        protected static string ResolveRandf(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var match = _randfPattern.Match(value.Trim());
            if (!match.Success) return value;

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has non-numeric bounds — leaving value as-is.",
                    "KhemistryISRU/ResolveRandf");
                return value;
            }

            int n = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (n < 0)
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has a negative decimal-place count — treating as 0.",
                    "KhemistryISRU/ResolveRandf");
                n = 0;
            }

            double lo = Math.Min(a, b);
            double hi = Math.Max(a, b);
            double roll = lo + UnityEngine.Random.value * (hi - lo);
            double rounded = Math.Round(roll, n, MidpointRounding.AwayFromZero);

            return rounded.ToString("F" + n, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Attempts to drain any buffered material output into a KhemistryMaterialStorage
        /// module on the vessel. Only whole units are ever moved.
        /// </summary>
        protected bool TryTransferMaterialOutputBuffer()
        {
            if (vessel == null || part == null) return false;
            if (_materialOutputAmount.Count == 0) return false;

            bool transferredAny = false;
            foreach (var matOutput in _materialOutputAmount.Keys.ToList())
            {
                double buffered = _materialOutputAmount[matOutput];
                if (buffered < 1.0 - 1e-9) continue;

                KhemistryMaterial material = KShared.Instance?.materialList.FirstOrDefault(m => m.name == matOutput.name);
                if (material == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": OUTPUT_MATERIAL \"" + matOutput.name
                        + "\" does not match any loaded KHEMISTRY_MATERIAL definition.",
                        "KhemistryISRU/TryTransferMaterialOutputBuffer");
                    continue;
                }

                while (buffered >= 1.0 - 1e-9)
                {
                    string resolvedSize = ResolveRandf(matOutput.size);
                    Dictionary<string, string> resolvedParameters = new Dictionary<string, string>();
                    foreach (var kv in matOutput.parameters)
                        resolvedParameters[kv.Key] = ResolveRandf(kv.Value);

                    if (!KShared.TryEvaluateOutVolumeExpression(matOutput.outVolume, resolvedSize, resolvedParameters,
                            "KhemistryISRU/TryTransferMaterialOutputBuffer", out double perUnitVolume))
                        break;
                    if (double.IsNaN(perUnitVolume) || double.IsInfinity(perUnitVolume) || perUnitVolume <= 0.0
                        || perUnitVolume > float.MaxValue)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": OUTPUT_MATERIAL \"" + matOutput.name
                            + "\" produced an invalid per-unit volume.",
                            "KhemistryISRU/TryTransferMaterialOutputBuffer");
                        break;
                    }

                    KhemistryMaterialInstance instance = new KhemistryMaterialInstance(
                        material, matOutput.shape, resolvedSize, (float)perUnitVolume, resolvedParameters)
                    {
                        amount = 1
                    };

                    bool placed = false;
                    if (moduleType == "kerbalEVA" && _kerbalHost != null)
                    {
                        placed = _kerbalHost.TryAddMaterialToSuitCell(instance);
                    }
                    else
                    {
                        foreach (Part vesselPart in vessel.parts)
                        {
                            foreach (KhemistryMaterialStorage storageModule in vesselPart.Modules.OfType<KhemistryMaterialStorage>())
                            {
                                if (storageModule.AddMaterial(instance))
                                {
                                    placed = true;
                                    break;
                                }
                            }
                            if (placed) break;
                        }
                    }

                    if (!placed) break;
                    buffered -= 1.0;
                    if (buffered < 0.0 && buffered > -1e-9) buffered = 0.0;
                    _materialOutputAmount[matOutput] = buffered;
                    transferredAny = true;
                }
            }

            return transferredAny;
        }
    }
}
