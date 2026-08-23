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
                part.RequestResource(_activeRecipe._passiveInputs[i].resourceName, -amount, _activeRecipe._passiveInputs[i].flowMode);
                _passiveConsumedThisBatch[i] = 0.0;
            }
        }

        /// <summary>
        /// Whether the vessel's current location satisfies at least one of this converter's
        /// depositCondition entries (surface or underground). Logs an error and returns false —
        /// rather than throwing — if KShared isn't loaded yet. Always true if depositCondition
        /// is empty (no restriction configured).
        /// </summary>
        protected bool IsAtRequiredDeposit()
        {
            if (_depositConditions.Count == 0) return true;

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
            return _depositConditions.Any(d => here.Contains(d));
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

            ///// Deposit conditions /////
            _depositConditions.Clear();
            _depositConditions.AddRange(moduleNode.GetValues("depositCondition"));

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
            _chargeNames.Clear();
            _chargeAmounts.Clear();
            if (chargingRequired)
                _chargeNames = KShared.GetChargingFromCFG(moduleNode, out _chargeAmounts);

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
                        if (float.TryParse(amt, out float mTmp))
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

                            // Check if this wasn't already added by RECIPE_NAMES logic
                            foreach (KhemistryISRURecipe recipe in recipes)
                                if (recipe._name == overriddenCandidate._name)
                                    continue;  // skip this candidate

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
            if (initial == null) initial = recipes[0];
            ApplyRecipe(initial);
        }

        /// <summary>
        /// Makes the given recipe the active one: applies its own charging fields
        /// (falling back to module-level charging if the recipe doesn't define its own),
        /// resets batch progress, and updates control show-rules.
        /// </summary>
        protected void ApplyRecipe(KhemistryISRURecipe recipe)
        {
            _activeRecipe = recipe;
            activeRecipeName = recipe._name;
            batchProgress = 0.0;

            _passiveTimers.Clear();
            _passiveConsumedThisBatch.Clear();
            for (int i = 0; i < recipe._passiveInputs.Count; i++)
            {
                _passiveTimers.Add(0.0);
                _passiveConsumedThisBatch.Add(0.0);
            }

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

            _controlsShowPAW = recipe._controlsShowPAW;
            _controlsShowEVA = recipe._controlsShowEVA;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _fatalConfigError = false;
            _outputWarnCooldown = 0.0;

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

            Fields["statusDisplay"].guiActiveUnfocused = true;
            Fields["chargeDisplay"].guiActiveUnfocused = true;
            Fields["progressDisplay"].guiActiveUnfocused = true;
            Fields["stateDisplay"].guiActiveUnfocused = true;

            Fields["statusDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["chargeDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["progressDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;
            Fields["stateDisplay"].guiUnfocusedRange = _configMaxDisplayDistance;

            Events["StartConverter"].guiName = StartActionName;
            Events["StopConverter"].guiName = StopActionName;
            Actions["StartConverterAction"].guiName = StartActionName;
            Actions["StopConverterAction"].guiName = StopActionName;

            Events["StartConverter"].unfocusedRange = _configMaxInteractionDistance;
            Events["StopConverter"].unfocusedRange = _configMaxInteractionDistance;
            Events["PerformMaintenance"].unfocusedRange = _configMaxInteractionDistance;
            Events["SwitchRecipe"].unfocusedRange = _configMaxInteractionDistance;

            if (!chargingRequired)
                this.state = KShared.ChargablePartState.On;

            _runtimeData = new KhemistryRuntimeData(vessel);  // vessel could be null

            SetupActiveAnimation();

            UpdateEventVisibility();
        }

        protected void UpdateEventVisibility()
        {
            ApplyShowRule(Events["StartConverter"],
                showPAW: !isRunning && !needsMaintenance && _controlsShowPAW,
                showEVA: !isRunning && !needsMaintenance && _controlsShowEVA);

            ApplyShowRule(Events["StopConverter"],
                showPAW: isRunning && _controlsShowPAW,
                showEVA: isRunning && _controlsShowEVA);

            Events["PerformMaintenance"].active = needsMaintenance;
            Events["PerformMaintenance"].guiActiveUnfocused = needsMaintenance;
            Events["PerformMaintenance"].unfocusedRange = _maxInteractionDistance;

            ApplyShowRule(Events["SwitchRecipe"],
                showPAW: !isRunning && recipes.Count > 1 && _controlsShowPAW,
                showEVA: !isRunning && recipes.Count > 1 && _controlsShowEVA);
        }

        /// <summary>
        /// Pulls the given resources from the vessel network. Returns true only if every
        /// resource was fully satisfied. Refunds all pulled resources if any fall short
        /// (all-or-nothing semantics).
        /// </summary>
        private bool ConsumeVesselResources(List<string> names, List<float> amounts, double dt)
        {
            if (names.Count == 0 || amounts.Count == 0) return true;
            if (names.Count != amounts.Count) return false;

            var pulled = new List<double>(names.Count);
            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                float rate = amounts[i];
                if (rate <= 0f) { pulled.Add(0.0); continue; }

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryISRU/ConsumeVesselResources");
                    pulled.Add(0.0);
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                double got = RequestResourceRouted(names[i], needed);
                pulled.Add(got);

                if (got < needed * 0.999)
                    allSatisfied = false;
            }

            if (!allSatisfied)
            {
                for (int i = 0; i < names.Count; i++)
                    if (pulled[i] > 0.0)
                        RequestResourceRouted(names[i], -pulled[i]);
                return false;
            }

            return true;
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

            if (state == KShared.ChargablePartState.Off)
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
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

            bool satisfied = ConsumeVesselResources(_chargeNames, _chargeAmounts, dt);
            if (satisfied)
            {
                chargePercent += chargeRate * (float)dt;
                if (chargePercent > 100f)
                    chargePercent = 100f;
            }
            else
            {
                if (chargeDecayRate > 0f)
                {
                    chargePercent -= chargeDecayRate * (float)dt;
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
            _outputWarnCooldown = Math.Max(0.0, _outputWarnCooldown - dt);

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

            if (_depositConditions.Count > 0 && !IsAtRequiredDeposit())
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

                while (timer >= pinp.period)
                {
                    timer -= pinp.period;
                    double needed = pinp.amount * biomeConfig.inputMultiplier;
                    if (needed <= 0.0) continue;

                    double got = RequestResourceRouted(pinp.resourceName, needed);

                    if (got < needed * 0.999)
                    {
                        // Passive consumption is all-or-nothing per tick — refund any partial draw.
                        if (got > 0.0) RequestResourceRouted(pinp.resourceName, -got);

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
        protected bool TryRunBatch(KhemistryISRUBiomeConfig biomeConfig)
        {
            var names = new List<string>();
            var amounts = new List<float>();
            foreach (var inp in _activeRecipe._inputs)
            {
                names.Add(inp.resourceName);
                amounts.Add((float)(inp.amount * biomeConfig.inputMultiplier));
            }

            if (!ConsumeVesselResources(names, amounts, 1.0)) return false;

            foreach (var outp in _activeRecipe._outputs)
            {
                double toAdd = outp.amount * biomeConfig.outputMultiplier;
                if (toAdd <= 0.0) continue;
                double got = RequestResourceRouted(outp.resourceName, -toAdd);
                if (outp.dumpExcess && Math.Abs(got) < toAdd * 0.999 && _outputWarnCooldown <= 0.0)
                {
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        "Converter \"" + ConverterName + "\": Not enough space for output \"" + outp.resourceName + "\", excess dumped.",
                        5f, ScreenMessageStyle.UPPER_CENTER));
                    _outputWarnCooldown = 10.0;
                }
            }

            foreach (var mat in _activeRecipe._outputMaterials)
            {
                if (mat.amount <= 0.0) continue;
                if (!_materialOutputAmount.ContainsKey(mat)) _materialOutputAmount.Add(mat, 0.0);
                _materialOutputAmount[mat] += mat.amount;
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
                double wholeUnits = Math.Floor(buffered);
                if (wholeUnits < 1.0) continue;

                KhemistryMaterial material = KShared.Instance?.materialList.FirstOrDefault(m => m.name == matOutput.name);
                if (material == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": OUTPUT_MATERIAL \"" + matOutput.name
                        + "\" does not match any loaded KHEMISTRY_MATERIAL definition.",
                        "KhemistryISRU/TryTransferMaterialOutputBuffer");
                    continue;
                }

                string resolvedSize = ResolveRandf(matOutput.size);
                Dictionary<string, string> resolvedParameters = new Dictionary<string, string>();
                foreach (var kv in matOutput.parameters)
                    resolvedParameters[kv.Key] = ResolveRandf(kv.Value);

                if (!KShared.TryEvaluateOutVolumeExpression(matOutput.outVolume, resolvedSize, resolvedParameters,
                        "KhemistryISRU/TryTransferMaterialOutputBuffer", out double perUnitVolume))
                    continue;  // error already logged

                KhemistryMaterialInstance instance = new KhemistryMaterialInstance(
                    material, matOutput.shape, resolvedSize,
                    (float)(perUnitVolume * wholeUnits), resolvedParameters);

                bool placed = false;
                if (moduleType == "kerbalEVA" && _kerbalHost != null)
                {
                    if (_kerbalHost.TryAddMaterialToSuitCell(instance))
                    {
                        _materialOutputAmount[matOutput] = buffered - wholeUnits;
                        transferredAny = true;
                        placed = true;
                    }
                }
                else
                {
                    foreach (Part vesselPart in vessel.parts)
                    {
                        foreach (KhemistryMaterialStorage storageModule in vesselPart.Modules.OfType<KhemistryMaterialStorage>())
                        {
                            if (storageModule.AddMaterial(instance))
                            {
                                // Keep any fractional remainder instead of discarding it.
                                _materialOutputAmount[matOutput] = buffered - wholeUnits;
                                transferredAny = true;
                                placed = true;
                                break;
                            }
                        }
                        if (placed) break;
                    }
                }
            }

            return transferredAny;
        }
    }
}