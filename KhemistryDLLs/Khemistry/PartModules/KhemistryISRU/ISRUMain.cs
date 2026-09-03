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
            AnimationState animationState = _activeAnim[_activeAnimationName];
            if (animationState == null)
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": Animator does not contain clip \""
                    + animName + "\".", "KhemistryISRU/SetupActiveAnimation");
                _activeAnim = null;
                _activeAnimationName = null;
                return;
            }
            animationState.wrapMode = WrapMode.Loop;

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
                    ResetPassiveTimers();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Void:
                    ClearPassiveConsumption();
                    ResetPassiveTimers();
                    batchProgress = 0.0;
                    isRunning = false;
                    statusDisplay = "Stopped (powerfail, resources lost)";
                    break;
                case KhemistryISRURecipe.PowerfailResult.Maint:
                    ClearPassiveConsumption();
                    ResetPassiveTimers();
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
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                {
                    _passiveConsumedThisBatch[i] = 0.0;
                    continue;
                }
                KhemistryISRURecipe.PassiveResourceInput input =
                    _activeRecipe._passiveInputs[i];
                double returned = Math.Abs(RequestResourceRouted(input.resourceName,
                    -amount, input.flowMode));
                if (double.IsNaN(returned) || double.IsInfinity(returned)
                    || returned < 0.0) returned = 0.0;
                double unreturned = amount - Math.Min(amount, returned);
                if (unreturned > 0.0)
                    QueuePassiveRefund(input.resourceName, input.flowMode, unreturned);
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
            here.AddRange(shared.UndergroundDepositsBelowPoint((float)vessel.latitude,
                (float)vessel.longitude, vessel.mainBody.name));
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

                if (float.IsNaN(_moduleChargeRate) || float.IsInfinity(_moduleChargeRate)
                    || _moduleChargeRate <= 0f
                    || float.IsNaN(_moduleChargeDecayRate) || float.IsInfinity(_moduleChargeDecayRate)
                    || _moduleChargeDecayRate < 0f
                    || _moduleChargeNames.Count == 0)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": chargingRequired needs a finite positive chargeRate, a finite non-negative chargeDecayRate, and valid charging resources; the converter was disabled to prevent uncharged operation.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                    _fatalConfigError = true;
                    return;
                }
            }

            ///// Recipes: local RECIPE nodes /////
            recipes.Clear();
            if (moduleNode.HasNode("RECIPE"))
            {
                foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
                {
                    ConfigNode mergedNode = KhemistryISRURecipe.ApplyModuleOverrides(moduleNode, recipeNode);
                    KhemistryISRURecipe recipe = new KhemistryISRURecipe(mergedNode, ConverterName);
                    if (recipe.IsValid)
                        recipe.ValidateReferences(shared?.materialList,
                            "KhemistryISRU/LoadConfigFromPartInfo");
                    if (recipe.IsValid && !recipes.Any(existing => existing._name == recipe._name))
                        recipes.Add(recipe);
                    else if (!recipe.IsValid)
                        KShared.LogError("Converter \"" + ConverterName + "\": invalid local recipe \""
                            + recipe._name + "\" was ignored.", "KhemistryISRU/LoadConfigFromPartInfo");
                    else
                        KShared.LogError("Converter \"" + ConverterName + "\": duplicate local recipe \""
                            + recipe._name + "\" was ignored.", "KhemistryISRU/LoadConfigFromPartInfo");
                }
            }

            ///// Recipes: imported by name (RECIPE_NAMES & RECIPE_MULTIPLIERS) /////
            recipeMultiplier = KShared.GetFloatValueFromCFG(moduleNode, "recipeMultiplier", 1f);
            if (float.IsNaN(recipeMultiplier) || float.IsInfinity(recipeMultiplier) || recipeMultiplier <= 0f)
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": recipeMultiplier must be finite and greater than zero; using 1.",
                    "KhemistryISRU/LoadConfigFromPartInfo");
                recipeMultiplier = 1f;
            }

            recipeType = KShared.GetStrValueFromCFG(moduleNode, "recipeType", moduleType == "kerbalEVA" ? "kerbalEVA" : null);
            recipeSubtype = KShared.GetStrValueFromCFG(moduleNode, "recipeSubtype",
                KShared.GetStrValueFromCFG(moduleNode, "recipeSubype", null));
            if (!moduleNode.HasValue("recipeSubtype") && moduleNode.HasValue("recipeSubype"))
                KShared.LogWarning("Converter \"" + ConverterName
                    + "\" uses legacy misspelling \"recipeSubype\"; use \"recipeSubtype\".",
                    "KhemistryISRU/LoadConfigFromPartInfo");
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
                    bool validMultipliers = true;
                    foreach (string amt in moduleNode.GetNode("RECIPE_MULTIPLIERS").GetValues("amount"))
                        if (float.TryParse(amt, NumberStyles.Float, CultureInfo.InvariantCulture, out float mTmp)
                            && !float.IsNaN(mTmp) && !float.IsInfinity(mTmp) && mTmp > 0f)
                            _recipeMultipliers.Add(mTmp);
                        else
                            validMultipliers = false;

                    if (!validMultipliers || _recipeMultipliers.Count != _recipeNames.Count)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\": RECIPE_MULTIPLIERS must contain one finite positive amount for every RECIPE_NAMES entry — ignoring RECIPE_MULTIPLIERS.",
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
                        string wantedName = _recipeNames[i]?.Trim();
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
                        if (overriddenFound.IsValid)
                            overriddenFound.ValidateReferences(shared.materialList,
                                "KhemistryISRU/LoadConfigFromPartInfo");
                        if (!overriddenFound.IsValid)
                        {
                            KShared.LogError("Converter \"" + ConverterName + "\": overridden recipe \""
                                + wantedName + "\" is invalid and was ignored.", "KhemistryISRU/LoadConfigFromPartInfo");
                            continue;
                        }

                        KhemistryISRURecipe scaled = overriddenFound.ScaledCopy(recipeMultiplier * localMult);
                        if (!recipes.Any(recipe => recipe._name == scaled._name))
                            recipes.Add(scaled);
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
                            if (overriddenCandidate.IsValid)
                                overriddenCandidate.ValidateReferences(shared.materialList,
                                    "KhemistryISRU/LoadConfigFromPartInfo");

                            if (!overriddenCandidate.IsValid)
                            {
                                KShared.LogError("Converter \"" + ConverterName + "\": overridden recipe \""
                                    + candidate._name + "\" is invalid and was ignored.", "KhemistryISRU/LoadConfigFromPartInfo");
                                continue;
                            }

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
                if (float.IsNaN(_configMaxInteractionDistance) || float.IsInfinity(_configMaxInteractionDistance)
                    || _configMaxInteractionDistance < 0f)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": maxInteractionDistance must be finite and non-negative; using 7.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                    _configMaxInteractionDistance = 7f;
                }
                if (float.IsNaN(_configMaxDisplayDistance) || float.IsInfinity(_configMaxDisplayDistance)
                    || _configMaxDisplayDistance < 0f)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": maxDisplayDistance must be finite and non-negative; using 10.",
                        "KhemistryISRU/LoadConfigFromPartInfo");
                    _configMaxDisplayDistance = 10f;
                }
            }
            _maxInteractionDistance = _configMaxInteractionDistance;
            _maxDisplayDistance = _configMaxDisplayDistance;

            ///// Select active recipe /////
            KhemistryISRURecipe initial = null;
            string savedRecipeName = activeRecipeName;
            if (!string.IsNullOrEmpty(savedRecipeName))
                initial = recipes.FirstOrDefault(r => r._name == savedRecipeName);
            bool restoringSavedRecipe = initial != null;
            PrepareLegacyPassiveStates(savedRecipeName, initial);
            if (!string.IsNullOrEmpty(savedRecipeName) && !restoringSavedRecipe)
            {
                // A different first recipe is useful for the selector/UI, but must never start
                // automatically using progress and withdrawals that belonged to a removed one.
                isRunning = false;
                batchProgress = 0.0;
                KShared.LogWarning("Converter \"" + ConverterName + "\": saved recipe \""
                    + savedRecipeName
                    + "\" is no longer available; the converter was stopped and selected the first available recipe.",
                    "KhemistryISRU/LoadConfigFromPartInfo");
            }
            if (initial == null) initial = recipes[0];
            ApplyRecipe(initial, resetProgress: !restoringSavedRecipe);
            ProcessPendingPassiveRefunds();
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
            if (resetProgress || double.IsNaN(batchProgress) || double.IsInfinity(batchProgress)
                || batchProgress < 0.0)
                batchProgress = 0.0;
            else if (batchProgress > recipe._recipeTime)
                batchProgress = recipe._recipeTime;

            _passiveTimers.Clear();
            _passiveConsumedThisBatch.Clear();
            for (int i = 0; i < recipe._passiveInputs.Count; i++)
            {
                double timer = 0.0;
                double consumed = 0.0;
                if (!resetProgress)
                {
                    KhemistryISRURecipe.PassiveResourceInput input = recipe._passiveInputs[i];
                    int savedIndex = _loadedPassiveStates.FindIndex(saved =>
                        SavedPassiveInputMatches(saved, input));
                    if (savedIndex >= 0)
                    {
                        timer = _loadedPassiveStates[savedIndex].timer;
                        consumed = _loadedPassiveStates[savedIndex].consumed;
                        _loadedPassiveStates.RemoveAt(savedIndex);
                    }
                }
                _passiveTimers.Add(timer);
                _passiveConsumedThisBatch.Add(consumed);
            }

            // A saved entry whose recipe identity no longer exists cannot safely be attached
            // to a different input. Return its tracked withdrawal using its own saved identity.
            foreach (PassiveInputSaveState unmatched in _loadedPassiveStates)
            {
                if (unmatched.consumed > 0.0)
                    QueuePassiveRefund(unmatched.resourceName, unmatched.flowMode,
                        unmatched.consumed);
                KShared.LogWarning("Converter \"" + ConverterName
                    + "\": detached unmatched saved passive-input state for \""
                    + unmatched.resourceName
                    + "\" after its recipe definition changed; its consumed resource is queued for refund.",
                    "KhemistryISRU/ApplyRecipe");
            }
            _loadedPassiveStates.Clear();

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

        private void PrepareLegacyPassiveStates(string savedRecipeName,
            KhemistryISRURecipe selectedRecipe)
        {
            if (_pendingLegacyPassiveNodes.Count == 0) return;

            foreach (ConfigNode legacyNode in _pendingLegacyPassiveNodes)
            {
                string legacyRecipeName = legacyNode.GetValue("recipeName")?.Trim();
                bool belongsToSelected = selectedRecipe != null
                    && !string.IsNullOrEmpty(savedRecipeName)
                    && string.Equals(legacyRecipeName, savedRecipeName,
                        StringComparison.Ordinal)
                    && string.Equals(selectedRecipe._name, savedRecipeName,
                        StringComparison.Ordinal);

                bool alreadyQuarantined = bool.TryParse(
                    legacyNode.GetValue("quarantined"), out bool quarantined)
                    && quarantined;
                if (alreadyQuarantined) continue;

                legacyNode.SetValue("quarantined", true, true);
                if (belongsToSelected && _loadedPassiveStates.Count == 0)
                {
                    // The old format stores only list positions. Even a same-name recipe may
                    // have changed or reordered its passive inputs, so applying or refunding
                    // those values could create the wrong resource. Start a fresh batch once,
                    // while retaining the opaque record for possible manual/future recovery.
                    isRunning = false;
                    batchProgress = 0.0;
                }

                KShared.LogWarning("Converter \"" + ConverterName
                    + "\": quarantined legacy positional passive-input state for recipe \""
                    + (legacyRecipeName ?? "")
                    + "\" because it has no resource identities; it was not applied or refunded.",
                    "KhemistryISRU/PrepareLegacyPassiveStates");
            }
        }

        private void QueuePassiveRefund(string resourceName, ResourceFlowMode flowMode,
            double amount)
        {
            if (string.IsNullOrWhiteSpace(resourceName) || double.IsNaN(amount)
                || double.IsInfinity(amount) || amount <= 0.0
                || !Enum.IsDefined(typeof(ResourceFlowMode), flowMode)) return;
            resourceName = resourceName.Trim();
            PassiveRefundSaveState existing = _pendingPassiveRefunds.FirstOrDefault(
                refund => refund.resourceName == resourceName && refund.flowMode == flowMode);
            if (existing != null)
            {
                double combined = existing.amount + amount;
                if (!double.IsNaN(combined) && !double.IsInfinity(combined))
                {
                    existing.amount = combined;
                    return;
                }
            }
            _pendingPassiveRefunds.Add(new PassiveRefundSaveState
            {
                resourceName = resourceName,
                flowMode = flowMode,
                amount = amount
            });
        }

        private void ProcessPendingPassiveRefunds()
        {
            for (int i = _pendingPassiveRefunds.Count - 1; i >= 0; i--)
            {
                PassiveRefundSaveState refund = _pendingPassiveRefunds[i];
                if (refund == null || string.IsNullOrEmpty(refund.resourceName)
                    || double.IsNaN(refund.amount) || double.IsInfinity(refund.amount)
                    || refund.amount <= 0.0)
                {
                    _pendingPassiveRefunds.RemoveAt(i);
                    continue;
                }

                double returned = Math.Abs(RequestResourceRouted(refund.resourceName,
                    -refund.amount, refund.flowMode));
                if (double.IsNaN(returned) || double.IsInfinity(returned)
                    || returned < 0.0) returned = 0.0;
                returned = Math.Min(returned, refund.amount);
                refund.amount -= returned;
                if (refund.amount <= 0.0) _pendingPassiveRefunds.RemoveAt(i);
            }
        }

        private static bool SavedPassiveInputMatches(PassiveInputSaveState saved,
            KhemistryISRURecipe.PassiveResourceInput input)
        {
            return saved != null
                && string.Equals(saved.resourceName, input.resourceName,
                    StringComparison.Ordinal)
                && saved.flowMode == input.flowMode
                && NearlyEqualSavedValue(saved.amount, input.amount)
                && NearlyEqualSavedValue(saved.period, input.period);
        }

        private static bool NearlyEqualSavedValue(double left, double right)
        {
            return !double.IsNaN(left) && !double.IsInfinity(left)
                && !double.IsNaN(right) && !double.IsInfinity(right)
                && left.Equals(right);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (node != null && (node.HasValue("isRunning")
                || node.HasValue("needsMaintenance") || node.HasValue("state")
                || node.HasValue("chargePercent") || node.HasValue("activeRecipeName")
                || node.HasValue("batchProgress") || node.HasNode("PASSIVE_INPUT_STATE")
                || node.HasNode("PENDING_PASSIVE_REFUND")
                || node.HasNode("ORPHANED_LEGACY_PASSIVE_STATE")
                || node.HasNode("MATERIAL_OUTPUT_BUFFER")))
                _loadedAuthoritativePersistentState = true;
            _loadedPassiveStates.Clear();
            _opaquePassiveInputNodes.Clear();
            _pendingPassiveRefunds.Clear();
            _opaquePassiveRefundNodes.Clear();
            _pendingLegacyPassiveNodes.Clear();
            _pendingMaterialOutputNodes.Clear();
            _materialOutputAmount.Clear();
            _materialOutputRandomSeed.Clear();
            _materialOutputRandomSequence.Clear();

            ConfigNode passiveNode = node.GetNode("PASSIVE_INPUT_STATE");
            if (passiveNode != null)
            {
                foreach (ConfigNode inputNode in passiveNode.GetNodes("INPUT"))
                {
                    string resourceName = inputNode.GetValue("name")?.Trim();
                    string flowModeRaw = inputNode.GetValue("flowmode");
                    if (string.IsNullOrEmpty(resourceName)
                        || !TryReadSavedPassiveDouble(inputNode, "amount", true,
                            out double amount)
                        || !TryReadSavedPassiveDouble(inputNode, "period", true,
                            out double period)
                        || !TryReadSavedPassiveDouble(inputNode, "timer", false,
                            out double timer)
                        || !TryReadSavedPassiveDouble(inputNode, "consumed", false,
                            out double consumed)
                        || !Enum.TryParse(flowModeRaw, true, out ResourceFlowMode flowMode)
                        || !Enum.IsDefined(typeof(ResourceFlowMode), flowMode))
                    {
                        KShared.LogError("Converter \"" + ConverterName
                            + "\": preserved a malformed named passive-input save record without applying it.",
                            "KhemistryISRU/OnLoad");
                        ConfigNode opaque = new ConfigNode("INPUT");
                        inputNode.CopyTo(opaque);
                        _opaquePassiveInputNodes.Add(opaque);
                        continue;
                    }

                    _loadedPassiveStates.Add(new PassiveInputSaveState
                    {
                        resourceName = resourceName,
                        amount = amount,
                        period = period,
                        flowMode = flowMode,
                        timer = timer,
                        consumed = consumed
                    });
                }

                // Preserve old positional values exactly until their original recipe identity
                // can be checked. Parsing them here used to collapse invalid/missing positions
                // and could attach a withdrawal to the wrong resource.
                if (passiveNode.GetValues("timer").Length > 0
                    || passiveNode.GetValues("consumed").Length > 0)
                {
                    ConfigNode legacy = new ConfigNode("ORPHANED_LEGACY_PASSIVE_STATE");
                    legacy.AddValue("recipeName", activeRecipeName ?? "");
                    foreach (string raw in passiveNode.GetValues("timer"))
                        legacy.AddValue("timer", raw ?? "");
                    foreach (string raw in passiveNode.GetValues("consumed"))
                        legacy.AddValue("consumed", raw ?? "");
                    _pendingLegacyPassiveNodes.Add(legacy);
                }
            }

            foreach (ConfigNode legacyNode in node.GetNodes("ORPHANED_LEGACY_PASSIVE_STATE"))
            {
                ConfigNode copy = new ConfigNode("ORPHANED_LEGACY_PASSIVE_STATE");
                legacyNode.CopyTo(copy);
                _pendingLegacyPassiveNodes.Add(copy);
            }

            foreach (ConfigNode refundNode in node.GetNodes("PENDING_PASSIVE_REFUND"))
            {
                string resourceName = refundNode.GetValue("name")?.Trim();
                string flowModeRaw = refundNode.GetValue("flowmode");
                if (!string.IsNullOrEmpty(resourceName)
                    && TryReadSavedPassiveDouble(refundNode, "amount", true,
                        out double amount)
                    && Enum.TryParse(flowModeRaw, true, out ResourceFlowMode flowMode)
                    && Enum.IsDefined(typeof(ResourceFlowMode), flowMode))
                {
                    QueuePassiveRefund(resourceName, flowMode, amount);
                }
                else
                {
                    ConfigNode copy = new ConfigNode("PENDING_PASSIVE_REFUND");
                    refundNode.CopyTo(copy);
                    _opaquePassiveRefundNodes.Add(copy);
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": preserved a malformed pending passive refund without applying it.",
                        "KhemistryISRU/OnLoad");
                }
            }

            foreach (ConfigNode bufferedNode in node.GetNodes("MATERIAL_OUTPUT_BUFFER"))
            {
                ConfigNode copy = new ConfigNode("MATERIAL_OUTPUT_BUFFER");
                bufferedNode.CopyTo(copy);
                _pendingMaterialOutputNodes.Add(copy);
            }
            _completedOnLoadCount++;
        }

        internal bool HasAuthoritativePersistentState
            => _loadedAuthoritativePersistentState;

        internal bool LoadEVAISRUBoardingState(ConfigNode node)
        {
            if (node == null) return false;
            int completedBefore = _completedOnLoadCount;
            Load(node);
            return _completedOnLoadCount > completedBefore;
        }

        private void ResetPassiveTimers()
        {
            for (int i = 0; i < _passiveTimers.Count; i++)
                _passiveTimers[i] = 0.0;
        }

        /// <summary>
        /// Reverses only the passive-resource work performed during one attempted simulation
        /// step. This keeps PAUSE atomic when a later passive input cannot be satisfied.
        /// </summary>
        private void RollBackPassiveInputStep(IList<double> timersBefore,
            IList<double> consumedBefore)
        {
            if (_activeRecipe != null)
            {
                int count = Math.Min(_activeRecipe._passiveInputs.Count,
                    Math.Min(_passiveConsumedThisBatch.Count, consumedBefore.Count));
                for (int i = count - 1; i >= 0; i--)
                {
                    double newlyConsumed = _passiveConsumedThisBatch[i] - consumedBefore[i];
                    if (double.IsNaN(newlyConsumed) || double.IsInfinity(newlyConsumed)
                        || newlyConsumed <= 0.0) continue;
                    KhemistryISRURecipe.PassiveResourceInput input =
                        _activeRecipe._passiveInputs[i];
                    RequestResourceRouted(input.resourceName, -newlyConsumed, input.flowMode);
                }
            }

            _passiveTimers.Clear();
            _passiveTimers.AddRange(timersBefore);
            _passiveConsumedThisBatch.Clear();
            _passiveConsumedThisBatch.AddRange(consumedBefore);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (node == null) return;

            while (node.HasNode("PASSIVE_INPUT_STATE"))
                node.RemoveNode("PASSIVE_INPUT_STATE");
            while (node.HasNode("PENDING_PASSIVE_REFUND"))
                node.RemoveNode("PENDING_PASSIVE_REFUND");
            while (node.HasNode("ORPHANED_LEGACY_PASSIVE_STATE"))
                node.RemoveNode("ORPHANED_LEGACY_PASSIVE_STATE");
            while (node.HasNode("MATERIAL_OUTPUT_BUFFER"))
                node.RemoveNode("MATERIAL_OUTPUT_BUFFER");

            ConfigNode passiveNode = new ConfigNode("PASSIVE_INPUT_STATE");
            foreach (ConfigNode opaqueInput in _opaquePassiveInputNodes)
            {
                ConfigNode copy = new ConfigNode("INPUT");
                opaqueInput.CopyTo(copy);
                passiveNode.AddNode(copy);
            }
            if (_activeRecipe != null
                && _passiveTimers.Count == _activeRecipe._passiveInputs.Count
                && _passiveConsumedThisBatch.Count == _activeRecipe._passiveInputs.Count)
            {
                for (int i = 0; i < _activeRecipe._passiveInputs.Count; i++)
                {
                    KhemistryISRURecipe.PassiveResourceInput input =
                        _activeRecipe._passiveInputs[i];
                    AddPassiveInputSaveNode(passiveNode, input.resourceName, input.amount,
                        input.period, input.flowMode, _passiveTimers[i],
                        _passiveConsumedThisBatch[i]);
                }
            }
            if (_loadedPassiveStates.Count > 0)
            {
                foreach (PassiveInputSaveState saved in _loadedPassiveStates)
                {
                    AddPassiveInputSaveNode(passiveNode, saved.resourceName, saved.amount,
                        saved.period, saved.flowMode, saved.timer, saved.consumed);
                }
            }

            if (passiveNode.nodes.Count > 0 || passiveNode.values.Count > 0)
                node.AddNode(passiveNode);

            foreach (ConfigNode legacyNode in _pendingLegacyPassiveNodes)
            {
                ConfigNode copy = new ConfigNode("ORPHANED_LEGACY_PASSIVE_STATE");
                legacyNode.CopyTo(copy);
                node.AddNode(copy);
            }

            foreach (ConfigNode opaqueRefund in _opaquePassiveRefundNodes)
            {
                ConfigNode copy = new ConfigNode("PENDING_PASSIVE_REFUND");
                opaqueRefund.CopyTo(copy);
                node.AddNode(copy);
            }
            foreach (PassiveRefundSaveState refund in _pendingPassiveRefunds)
            {
                if (refund == null || string.IsNullOrEmpty(refund.resourceName)
                    || double.IsNaN(refund.amount) || double.IsInfinity(refund.amount)
                    || refund.amount <= 0.0) continue;
                ConfigNode refundNode = new ConfigNode("PENDING_PASSIVE_REFUND");
                refundNode.AddValue("name", refund.resourceName);
                refundNode.AddValue("flowmode", refund.flowMode.ToString());
                refundNode.AddValue("amount",
                    refund.amount.ToString("R", CultureInfo.InvariantCulture));
                node.AddNode(refundNode);
            }

            // Preserve saved output records that could not be interpreted with the current
            // configuration. A broken or temporarily missing recipe must not silently erase them.
            foreach (ConfigNode pendingNode in _pendingMaterialOutputNodes)
            {
                ConfigNode copy = new ConfigNode("MATERIAL_OUTPUT_BUFFER");
                pendingNode.CopyTo(copy);
                node.AddNode(copy);
            }

            foreach (KeyValuePair<KhemistryISRURecipe.ResourceOutputMaterial, double> buffered in _materialOutputAmount)
            {
                if (double.IsNaN(buffered.Value) || double.IsInfinity(buffered.Value)
                    || buffered.Value <= 0.0) continue;
                KhemistryISRURecipe.ResourceOutputMaterial material = buffered.Key;
                EnsureMaterialOutputRandomState(material);
                ConfigNode outputNode = new ConfigNode("MATERIAL_OUTPUT_BUFFER");
                outputNode.AddValue("name", material.name ?? "");
                outputNode.AddValue("shape", material.shape ?? "");
                outputNode.AddValue("size", material.size ?? "");
                outputNode.AddValue("amount", buffered.Value.ToString("R", CultureInfo.InvariantCulture));
                outputNode.AddValue("outVolume", material.outVolume ?? "0");
                outputNode.AddValue("randomSeed", _materialOutputRandomSeed[material]
                    .ToString(CultureInfo.InvariantCulture));
                outputNode.AddValue("randomSequence", _materialOutputRandomSequence[material]
                    .ToString(CultureInfo.InvariantCulture));
                ConfigNode paramsNode = new ConfigNode("PARAMS");
                foreach (KeyValuePair<string, string> parameter in
                         (material.parameters ?? new Dictionary<string, string>())
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
                    paramsNode.AddValue(parameter.Key, parameter.Value);
                outputNode.AddNode(paramsNode);
                node.AddNode(outputNode);
            }
        }

        private static bool TryReadSavedPassiveDouble(ConfigNode node, string key,
            bool requirePositive, out double value)
        {
            value = 0.0;
            return node != null
                && double.TryParse(node.GetValue(key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value)
                && (requirePositive ? value > 0.0 : value >= 0.0);
        }

        private static void AddPassiveInputSaveNode(ConfigNode parent, string resourceName,
            double amount, double period, ResourceFlowMode flowMode, double timer,
            double consumed)
        {
            if (parent == null || string.IsNullOrEmpty(resourceName)
                || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0
                || double.IsNaN(period) || double.IsInfinity(period) || period <= 0.0
                || double.IsNaN(timer) || double.IsInfinity(timer) || timer < 0.0
                || double.IsNaN(consumed) || double.IsInfinity(consumed) || consumed < 0.0)
                return;

            ConfigNode inputNode = new ConfigNode("INPUT");
            inputNode.AddValue("name", resourceName);
            inputNode.AddValue("amount", amount.ToString("R", CultureInfo.InvariantCulture));
            inputNode.AddValue("period", period.ToString("R", CultureInfo.InvariantCulture));
            inputNode.AddValue("flowmode", flowMode.ToString());
            inputNode.AddValue("timer", timer.ToString("R", CultureInfo.InvariantCulture));
            inputNode.AddValue("consumed", consumed.ToString("R", CultureInfo.InvariantCulture));
            parent.AddNode(inputNode);
        }

        private static bool OutputMaterialsEquivalent(KhemistryISRURecipe.ResourceOutputMaterial left,
            KhemistryISRURecipe.ResourceOutputMaterial right)
        {
            if (left.name != right.name || left.shape != right.shape || left.size != right.size
                || left.outVolume != right.outVolume)
                return false;
            if (ReferenceEquals(left.parameters, right.parameters)) return true;
            if (left.parameters == null || right.parameters == null
                || left.parameters.Count != right.parameters.Count) return false;
            foreach (KeyValuePair<string, string> parameter in left.parameters)
                if (!right.parameters.TryGetValue(parameter.Key, out string value) || value != parameter.Value)
                    return false;
            return true;
        }

        private bool TryGetBufferedMaterialKey(
            KhemistryISRURecipe.ResourceOutputMaterial candidate,
            out KhemistryISRURecipe.ResourceOutputMaterial key)
        {
            foreach (KhemistryISRURecipe.ResourceOutputMaterial bufferedKey in
                     _materialOutputAmount.Keys)
            {
                if (!OutputMaterialsEquivalent(bufferedKey, candidate)) continue;
                key = bufferedKey;
                return true;
            }
            key = default(KhemistryISRURecipe.ResourceOutputMaterial);
            return false;
        }

        private static KhemistryISRURecipe.ResourceOutputMaterial CloneMaterialOutputKey(
            KhemistryISRURecipe.ResourceOutputMaterial source)
        {
            source.parameters = source.parameters == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(source.parameters);
            return source;
        }

        private static int CreateMaterialOutputRandomSeed()
            => UnityEngine.Random.Range(1, int.MaxValue);

        private void EnsureMaterialOutputRandomState(
            KhemistryISRURecipe.ResourceOutputMaterial material)
        {
            if (!_materialOutputRandomSeed.ContainsKey(material))
                _materialOutputRandomSeed[material] = CreateMaterialOutputRandomSeed();
            if (!_materialOutputRandomSequence.ContainsKey(material))
                _materialOutputRandomSequence[material] = 0L;
        }

        private static System.Random CreateMaterialOutputRandom(int seed, long sequence)
        {
            unchecked
            {
                int mixed = seed;
                mixed = (mixed * 397) ^ (int)sequence;
                mixed = (mixed * 397) ^ (int)(sequence >> 32);
                return new System.Random(mixed & int.MaxValue);
            }
        }

        private void RestoreMaterialOutputBuffer()
        {
            _materialOutputAmount.Clear();
            _materialOutputRandomSeed.Clear();
            _materialOutputRandomSequence.Clear();
            List<ConfigNode> unrestored = new List<ConfigNode>();
            foreach (ConfigNode outputNode in _pendingMaterialOutputNodes)
            {
                if (!double.TryParse(outputNode.GetValue("amount"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double amount)
                    || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
                {
                    unrestored.Add(outputNode);
                    continue;
                }

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

                if (!matched)
                {
                    // Keep output from a removed, renamed, or temporarily invalid recipe as
                    // opaque save data. Replaying it without a current matching definition
                    // could manufacture a material with an obsolete shape/volume contract.
                    unrestored.Add(outputNode);
                    continue;
                }

                bool hasSeed = outputNode.HasValue("randomSeed");
                bool hasSequence = outputNode.HasValue("randomSequence");
                int randomSeed = 0;
                long randomSequence = 0L;
                if (hasSeed != hasSequence
                    || (hasSeed && (!int.TryParse(outputNode.GetValue("randomSeed"),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out randomSeed)
                        || !long.TryParse(outputNode.GetValue("randomSequence"),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out randomSequence)
                        || randomSequence < 0L)))
                {
                    unrestored.Add(outputNode);
                    continue;
                }
                if (!hasSeed)
                {
                    // Legacy buffers did not preserve realized randomness. Assign the stream
                    // once during migration; all subsequent failed attempts and reloads are stable.
                    randomSeed = CreateMaterialOutputRandomSeed();
                    randomSequence = 0L;
                }

                // Distinct saved records retain distinct streams. This matters for malformed or
                // hand-edited saves containing duplicate logical templates: combining their
                // streams would change already-promised randomized units.
                if (_materialOutputAmount.Keys.Any(key =>
                        OutputMaterialsEquivalent(key, restored)))
                    restored = CloneMaterialOutputKey(restored);
                _materialOutputAmount[restored] = amount;
                _materialOutputRandomSeed[restored] = randomSeed;
                _materialOutputRandomSequence[restored] = randomSequence;
            }
            _pendingMaterialOutputNodes.Clear();
            _pendingMaterialOutputNodes.AddRange(unrestored);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // This must precede recipe/config initialization: PartModule.Load restores both
            // persistent KSPFields and custom PASSIVE_INPUT_STATE/MATERIAL_OUTPUT_BUFFER nodes
            // which the remainder of OnStart then resolves against the current recipes.
            KhemistryKerbalSuitScenario suitScenario = KhemistryKerbalSuitScenario.Instance;
            if (suitScenario != null && suitScenario.IsReady)
                suitScenario.TryRestoreEVAISRUState(this);

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

            if (float.IsNaN(chargePercent) || float.IsInfinity(chargePercent)) chargePercent = 0f;
            chargePercent = Mathf.Clamp(chargePercent, 0f, 100f);
            if (!Enum.IsDefined(typeof(KShared.ChargablePartState), this.state))
                this.state = KShared.ChargablePartState.Off;
            if (chargingRequired && this.state == KShared.ChargablePartState.On
                && chargePercent < 100f)
                this.state = KShared.ChargablePartState.Off;

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
                    double interaction = _configMaxInteractionDistance
                        * currentBiome.maxInteractionDistanceMultiplier;
                    double display = _configMaxDisplayDistance
                        * currentBiome.maxDisplayDistanceMultiplier;
                    _maxInteractionDistance = interaction >= float.MaxValue
                        ? float.MaxValue : (float)interaction;
                    _maxDisplayDistance = display >= float.MaxValue
                        ? float.MaxValue : (float)display;
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

        private static bool WasFullyTransferred(double requested, double actual)
        {
            if (double.IsNaN(requested) || double.IsInfinity(requested) || requested < 0.0
                || double.IsNaN(actual) || double.IsInfinity(actual) || actual < 0.0)
                return false;

            if (requested == 0.0) return actual == 0.0;
            if (actual == 0.0) return false;

            // Scale the tolerance to the request. An absolute floor made every positive
            // transfer at or below that floor look complete even when KSP moved nothing.
            double tolerance = requested * 1e-9;
            return actual >= requested - tolerance;
        }

        private sealed class MaterialRemovalRecord
        {
            public KhemistryMaterialStorage storage;
            public KhemistryKerbal suitHost;
            public List<KhemistryMaterialInstance> pieces;
        }

        private bool ConsumeVesselResources(IList<string> names, IList<double> amounts,
            List<ResourceFlowMode> flowModes, double dt, out List<ResourceDraw> draws)
        {
            draws = new List<ResourceDraw>();
            if (names == null || amounts == null || flowModes == null
                || double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0.0)
                return false;
            if (names.Count != amounts.Count || names.Count != flowModes.Count) return false;
            if (names.Count == 0) return true;

            bool allSatisfied = true;

            for (int i = 0; i < names.Count; i++)
            {
                double rate = amounts[i];
                if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0.0
                    || string.IsNullOrWhiteSpace(names[i]))
                {
                    allSatisfied = false;
                    continue;
                }
                if (rate == 0.0 || dt == 0.0) continue;

                var def = PartResourceLibrary.Instance.GetDefinition(names[i]);
                if (def == null)
                {
                    KShared.LogError("Unknown resource \"" + names[i] + "\" in consumption list.",
                        "KhemistryISRU/ConsumeVesselResources");
                    allSatisfied = false;
                    continue;
                }

                double needed = rate * dt;
                if (double.IsNaN(needed) || double.IsInfinity(needed) || needed <= 0.0)
                {
                    allSatisfied = false;
                    continue;
                }
                double got = RequestResourceRouted(names[i], needed, flowModes[i]);
                if (!double.IsNaN(got) && !double.IsInfinity(got) && got > 0.0)
                    draws.Add(new ResourceDraw { name = names[i], amount = got, flowMode = flowModes[i] });

                if (!WasFullyTransferred(needed, got))
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

        private bool ConsumeVesselResources(IList<string> names, IList<double> amounts, double dt)
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
            long available = 0;
            foreach (KhemistryMaterialStorage storage in storages)
            {
                available += storage.GetMatchingMaterialAmount(
                    material.name, material.shape, material.size, material.parameters);
                if (available >= amount) break;
            }
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
                    bool restored = record.suitHost != null
                        ? record.suitHost.RestoreRemovedMaterialToSuitCell(piece)
                        : record.storage?.RestoreRemovedMaterial(piece) == true;
                    if (!restored)
                        KShared.LogError("Could not restore a material removed by a rolled-back batch.",
                            "KhemistryISRU/RefundMaterialRemovals");
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
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0.0) return;

            KhemistryISRUBiomeConfig biomeConfig = _activeRecipe != null && _runtimeData != null
                ? _activeRecipe.GetBiomeConfig(_runtimeData.planet, _runtimeData.biome)
                : null;
            double decayMultiplier = biomeConfig?.chargeDecayMultiplier ?? 1.0;
            double rateMultiplier = biomeConfig?.chargeRateMultiplier ?? 1.0;
            double consumptionMultiplier = biomeConfig?.chargeConsumptionMultiplier ?? 1.0;
            if (double.IsNaN(decayMultiplier) || double.IsInfinity(decayMultiplier)
                || decayMultiplier < 0.0
                || double.IsNaN(rateMultiplier) || double.IsInfinity(rateMultiplier)
                || rateMultiplier < 0.0
                || double.IsNaN(consumptionMultiplier)
                || double.IsInfinity(consumptionMultiplier)
                || consumptionMultiplier < 0.0)
            {
                statusDisplay = "ERROR: invalid charging multiplier";
                return;
            }

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

            double effectiveChargeRate = chargeRate * rateMultiplier;
            if (double.IsNaN(effectiveChargeRate) || double.IsInfinity(effectiveChargeRate)
                || effectiveChargeRate <= 0.0)
            {
                // In particular, a biome charge-rate multiplier of zero must not consume
                // resources while producing no charge.
                statusDisplay = "Charging unavailable here";
                return;
            }

            double secondsToFullCharge = (100.0 - chargePercent) / effectiveChargeRate;
            double chargingDt = Math.Min(dt, secondsToFullCharge);
            if (double.IsNaN(chargingDt) || double.IsInfinity(chargingDt)
                || chargingDt <= 0.0)
                return;

            List<double> scaledChargeAmounts = _chargeAmounts
                .Select(amount => (double)amount * consumptionMultiplier)
                .ToList();
            bool satisfied = ConsumeVesselResources(_chargeNames, scaledChargeAmounts,
                chargingDt);
            if (satisfied)
            {
                chargePercent += (float)(effectiveChargeRate * chargingDt);
                if (chargePercent >= 100f - 1e-5f)
                {
                    chargePercent = 100f;
                    state = KShared.ChargablePartState.On;
                    KShared.Log("Converter fully charged, now ON.",
                        "KhemistryISRU/HandleCharging");
                }
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
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0.0) return;
            HandleCharging(dt);
            UpdateUI();
            ProcessPendingPassiveRefunds();
            TryTransferMaterialOutputBuffer();
            UpdateEventVisibility();

            if (needsMaintenance || !isRunning || state != KShared.ChargablePartState.On || _activeRecipe == null)
            {
                statusDisplay = needsMaintenance ? "Needs maintenance" : (!isRunning ? "Stopped" : "Not ready");
                progressDisplay = "Off";
                SetActiveAnimationPlaying(false);
                return;
            }

            SetActiveAnimationPlaying(RunBatchCycle(dt));
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
            double interactionDistance = _configMaxInteractionDistance * biomeConfig.maxInteractionDistanceMultiplier;
            double displayDistance = _configMaxDisplayDistance * biomeConfig.maxDisplayDistanceMultiplier;
            _maxInteractionDistance = interactionDistance >= float.MaxValue
                ? float.MaxValue : (float)interactionDistance;
            _maxDisplayDistance = displayDistance >= float.MaxValue
                ? float.MaxValue : (float)displayDistance;
            ApplyInteractionRanges();

            return false;
        }

        /// <summary>
        /// Looks up the applicable biome config for the active recipe at the vessel's current
        /// location and, if found and operable, advances batch progress; consumes the full
        /// batch of inputs and produces the full batch of outputs once recipeTime is reached.
        /// </summary>
        protected bool RunBatchCycle(double dt)
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
                return false;
            }

            if (CheckBiomeConfig(biomeConfig))
                return false;

            if (biomeConfig.disabled)
            {
                statusDisplay = "Disabled in this biome";
                return false;
            }

            if (biomeConfig.situationOperating.Count > 0 && !biomeConfig.situationOperating.Contains(_runtimeData.sitCon))
            {
                statusDisplay = "Wrong situation (" + _runtimeData.sitCon + ")";
                return false;
            }

            if (!IsAtRequiredDeposit(biomeConfig))
            {
                statusDisplay = "Not at a required deposit";
                return false;
            }

            if (_runtimeData.alt < biomeConfig.minOperatingAltitude || _runtimeData.alt > biomeConfig.maxOperatingAltitude)
            {
                statusDisplay = "Out of operating altitude range";
                return false;
            }

            if (_runtimeData.g < biomeConfig.minOperatingG || _runtimeData.g > biomeConfig.maxOperatingG)
            {
                statusDisplay = "Out of operating G range";
                return false;
            }

            if (_runtimeData.temperature < biomeConfig.minOperatingTemperature || _runtimeData.temperature > biomeConfig.maxOperatingTemperature)
            {
                statusDisplay = "Out of operating temperature range";
                return false;
            }

            if (_runtimeData.pressure < biomeConfig.minOperatingPressure || _runtimeData.pressure > biomeConfig.maxOperatingPressure)
            {
                statusDisplay = "Out of operating pressure range";
                return false;
            }

            if (!CountWorkers(out uint engineers, out uint pilots, out uint scientists))
            {
                statusDisplay = "No workers nearby";
                return false;
            }

            double reqEngineers = _activeRecipe._workersEngineers * biomeConfig.workersEngineersMultiplier;
            double reqPilots = _activeRecipe._workersPilots * biomeConfig.workersPilotsMultiplier;
            double reqScientists = _activeRecipe._workersScientists * biomeConfig.workersScientistsMultiplier;

            if (engineers < reqEngineers || pilots < reqPilots || scientists < reqScientists)
            {
                statusDisplay = "Insufficient workers";
                return false;
            }

            double effectiveRecipeTime = _activeRecipe._recipeTime;
            double speed = biomeConfig.speedMul;
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0.0
                || double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0.0
                || double.IsNaN(effectiveRecipeTime) || double.IsInfinity(effectiveRecipeTime)
                || effectiveRecipeTime <= 0.0)
            {
                statusDisplay = "ERROR: invalid recipe timing, see log";
                KShared.LogError("Converter \"" + ConverterName
                    + "\": batch timing contains a non-finite or non-positive value.",
                    "KhemistryISRU/RunBatchCycle");
                return false;
            }

            if (double.IsNaN(batchProgress) || double.IsInfinity(batchProgress) || batchProgress < 0.0)
                batchProgress = 0.0;
            if (batchProgress > effectiveRecipeTime) batchProgress = effectiveRecipeTime;

            const int maxBatchesPerTick = 1000;
            int completedBatches = 0;
            bool performedWork = false;
            double remainingDt = dt;

            // Consume only the slice of wall-clock time needed to reach a batch boundary.
            // This prevents high time warp from charging passive inputs beyond a blocked batch,
            // and permits multiple complete batches in one physics update.
            while ((remainingDt > 0.0
                    || batchProgress >= effectiveRecipeTime)
                   && completedBatches < maxBatchesPerTick)
            {
                if (batchProgress >= effectiveRecipeTime)
                {
                    batchProgress = effectiveRecipeTime;
                    if (!TryRunBatch(biomeConfig))
                    {
                        statusDisplay = "Insufficient resources / no output space";
                        progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);
                        return performedWork;
                    }

                    batchProgress = 0.0;
                    ClearPassiveConsumption();
                    completedBatches++;
                    performedWork = true;
                    continue;
                }

                double secondsToBoundary = (effectiveRecipeTime - batchProgress) / speed;
                if (double.IsNaN(secondsToBoundary) || double.IsInfinity(secondsToBoundary)
                    || secondsToBoundary <= 0.0)
                {
                    statusDisplay = "ERROR: invalid recipe timing, see log";
                    return performedWork;
                }

                double step = Math.Min(remainingDt, secondsToBoundary);
                bool reachesBoundary = step >= secondsToBoundary;
                if (!ProcessPassiveInputs(biomeConfig, step))
                {
                    progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);
                    return performedWork;
                }

                batchProgress = reachesBoundary
                    ? effectiveRecipeTime
                    : batchProgress + step * speed;
                if (step > 0.0) performedWork = true;
                if (batchProgress > effectiveRecipeTime) batchProgress = effectiveRecipeTime;
                remainingDt -= step;
                if (remainingDt < 0.0) remainingDt = 0.0;
            }

            progressDisplay = FormatProgress(batchProgress, effectiveRecipeTime);
            if (completedBatches >= maxBatchesPerTick && remainingDt > 0.0)
            {
                statusDisplay = "Time-warp batch limit reached";
                KShared.LogWarning("Converter \"" + ConverterName
                    + "\" reached the safety limit of 1000 batches in one physics update; excess elapsed time was not processed.",
                    "KhemistryISRU/RunBatchCycle");
            }
            else if (completedBatches > 0)
                statusDisplay = completedBatches == 1 ? "Batch complete" : completedBatches + " batches complete";
            else
                statusDisplay = string.Format("Running ({0:F0}%)",
                    100.0 * batchProgress / effectiveRecipeTime);
            return performedWork;
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
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0.0) return false;

            List<double> timersBefore = new List<double>(_passiveTimers);
            List<double> consumedBefore = new List<double>(_passiveConsumedThisBatch);

            for (int i = 0; i < _activeRecipe._passiveInputs.Count; i++)
            {
                KhemistryISRURecipe.PassiveResourceInput pinp = _activeRecipe._passiveInputs[i];
                double timer = (i < _passiveTimers.Count) ? _passiveTimers[i] : 0.0;
                if (double.IsNaN(timer) || double.IsInfinity(timer) || timer < 0.0) timer = 0.0;
                timer += dt;
                double effectivePeriod = pinp.period * biomeConfig.passivePeriodMultiplier;
                double perOccurrence = pinp.amount * biomeConfig.inputMultiplier
                    * biomeConfig.passiveMultiplier;
                if (double.IsNaN(timer) || double.IsInfinity(timer)
                    || double.IsNaN(effectivePeriod) || double.IsInfinity(effectivePeriod)
                    || effectivePeriod <= 0.0
                    || double.IsNaN(perOccurrence) || double.IsInfinity(perOccurrence)
                    || perOccurrence < 0.0)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": passive-input timing or amount became invalid.",
                        "KhemistryISRU/ProcessPassiveInputs");
                    RollBackPassiveInputStep(timersBefore, consumedBefore);
                    return false;
                }

                double dueOccurrences = Math.Floor(timer / effectivePeriod);
                if (dueOccurrences < 1.0 || perOccurrence == 0.0)
                {
                    if (perOccurrence == 0.0 && dueOccurrences >= 1.0)
                        timer -= dueOccurrences * effectivePeriod;
                    if (i < _passiveTimers.Count) _passiveTimers[i] = Math.Max(0.0, timer);
                    continue;
                }

                double totalNeeded = dueOccurrences * perOccurrence;
                if (double.IsNaN(totalNeeded) || double.IsInfinity(totalNeeded))
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": passive-input consumption overflowed.",
                        "KhemistryISRU/ProcessPassiveInputs");
                    RollBackPassiveInputStep(timersBefore, consumedBefore);
                    return false;
                }

                double alreadyConsumed = i < _passiveConsumedThisBatch.Count
                    ? _passiveConsumedThisBatch[i] : 0.0;
                if (double.IsNaN(alreadyConsumed) || double.IsInfinity(alreadyConsumed)
                    || alreadyConsumed < 0.0
                    || double.IsInfinity(alreadyConsumed + totalNeeded))
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": passive-input consumption accounting overflowed.",
                        "KhemistryISRU/ProcessPassiveInputs");
                    RollBackPassiveInputStep(timersBefore, consumedBefore);
                    return false;
                }

                // A single routed request avoids an unbounded loop at high time warp. If KSP
                // returns a partial draw, retain only whole configured occurrences and refund
                // the fractional remainder.
                double got = RequestResourceRouted(pinp.resourceName, totalNeeded, pinp.flowMode);
                if (double.IsNaN(got) || double.IsInfinity(got) || got < 0.0) got = 0.0;
                double satisfiedOccurrences = Math.Min(dueOccurrences,
                    Math.Floor((got + perOccurrence * 1e-9) / perOccurrence));
                double kept = satisfiedOccurrences * perOccurrence;
                double partial = got - kept;
                if (partial > perOccurrence * 1e-9)
                    RequestResourceRouted(pinp.resourceName, -partial, pinp.flowMode);

                if (kept > 0.0 && i < _passiveConsumedThisBatch.Count)
                    _passiveConsumedThisBatch[i] += kept;
                timer -= satisfiedOccurrences * effectivePeriod;

                if (satisfiedOccurrences < dueOccurrences)
                {
                    if (pinp.ignorePowerfail)
                    {
                        // Missing ignored occurrences are skipped, matching the configured
                        // "nothing happens" behavior without issuing thousands of requests.
                        timer -= (dueOccurrences - satisfiedOccurrences) * effectivePeriod;
                    }
                    else
                    {
                        if (pinp.powerfail == KhemistryISRURecipe.PowerfailResult.Pause)
                        {
                            RollBackPassiveInputStep(timersBefore, consumedBefore);
                            TriggerPowerfail(part, pinp.powerfail,
                                pinp.powerfailExplosionRadius,
                                pinp.powerfailExplosionTemperature);
                            statusDisplay = "Paused: out of " + pinp.resourceName;
                            return false;
                        }

                        // Consume the failed occurrence's timer just as the old per-period loop
                        // did; any later due occurrences remain queued for a future update.
                        timer -= effectivePeriod;
                        if (i < _passiveTimers.Count) _passiveTimers[i] = Math.Max(0.0, timer);

                        TriggerPowerfail(part, pinp.powerfail, pinp.powerfailExplosionRadius,
                            pinp.powerfailExplosionTemperature);
                        return false;
                    }
                }

                if (i < _passiveTimers.Count) _passiveTimers[i] = Math.Max(0.0, timer);
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
                if (!WasFullyTransferred(output.amount, added))
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
                if (!WasFullyTransferred(output.amount, added))
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
                if (WasFullyTransferred(output.amount, added))
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
            if (amount <= 0 || double.IsNaN(multiplier) || double.IsInfinity(multiplier)
                || multiplier <= 0.0) return 0;
            double scaled = amount * multiplier;
            if (double.IsNaN(scaled) || scaled <= 0.0) return 0;
            if (scaled >= int.MaxValue) return int.MaxValue;
            int result = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return Math.Max(1, result);
        }

        private bool CanBufferMaterialOutputs(KhemistryISRUBiomeConfig biomeConfig)
        {
            for (int i = 0; i < _activeRecipe._outputMaterials.Count; i++)
            {
                KhemistryISRURecipe.ResourceOutputMaterial material =
                    _activeRecipe._outputMaterials[i];
                double amount = material.amount * biomeConfig.outputMultiplier;
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0.0)
                    return false;

                double projected = 0.0;
                foreach (KeyValuePair<KhemistryISRURecipe.ResourceOutputMaterial, double>
                         buffered in _materialOutputAmount)
                {
                    if (!OutputMaterialsEquivalent(buffered.Key, material)) continue;
                    projected += buffered.Value;
                    if (double.IsNaN(projected) || double.IsInfinity(projected)) return false;
                }
                for (int previous = 0; previous <= i; previous++)
                {
                    KhemistryISRURecipe.ResourceOutputMaterial other =
                        _activeRecipe._outputMaterials[previous];
                    if (!OutputMaterialsEquivalent(other, material)) continue;
                    double otherAmount = other.amount * biomeConfig.outputMultiplier;
                    if (double.IsNaN(otherAmount) || double.IsInfinity(otherAmount)
                        || otherAmount < 0.0) return false;
                    projected += otherAmount;
                    if (double.IsNaN(projected) || double.IsInfinity(projected)) return false;
                }
            }
            return true;
        }

        protected bool TryRunBatch(KhemistryISRUBiomeConfig biomeConfig)
        {
            List<PreparedResourceOutput> outputs = PrepareResourceOutputs(biomeConfig);
            if (!CanBufferMaterialOutputs(biomeConfig))
            {
                KShared.LogError("Converter \"" + ConverterName
                    + "\": material output buffer would overflow; batch was not run.",
                    "KhemistryISRU/TryRunBatch");
                return false;
            }

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
            List<double> amounts = new List<double>();
            List<ResourceFlowMode> flowModes = new List<ResourceFlowMode>();

            foreach (var inp in _activeRecipe._inputs)
            {
                names.Add(inp.resourceName);
                amounts.Add(inp.amount * biomeConfig.inputMultiplier);
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
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) continue;
                KhemistryISRURecipe.ResourceOutputMaterial key;
                if (!TryGetBufferedMaterialKey(mat, out key))
                {
                    key = mat;
                    _materialOutputAmount.Add(key, 0.0);
                }
                EnsureMaterialOutputRandomState(key);
                _materialOutputAmount[key] += amount;
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
        protected static string ResolveRandf(string value, System.Random random)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var match = _randfPattern.Match(value.Trim());
            if (!match.Success) return value;

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
                || double.IsNaN(a) || double.IsInfinity(a)
                || double.IsNaN(b) || double.IsInfinity(b))
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has non-numeric bounds — leaving value as-is.",
                    "KhemistryISRU/ResolveRandf");
                return value;
            }

            if (!int.TryParse(match.Groups[3].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int n))
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has an invalid decimal-place count — leaving value as-is.",
                    "KhemistryISRU/ResolveRandf");
                return value;
            }
            if (n < 0 || n > 15)
            {
                int clamped = Math.Max(0, Math.Min(15, n));
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" has a decimal-place count outside 0–15 — treating as "
                    + clamped + ".", "KhemistryISRU/ResolveRandf");
                n = clamped;
            }

            double lo = Math.Min(a, b);
            double hi = Math.Max(a, b);
            double t = random == null ? UnityEngine.Random.value : random.NextDouble();
            double roll = lo * (1.0 - t) + hi * t;
            if (double.IsNaN(roll) || double.IsInfinity(roll))
            {
                KShared.LogError(
                    "randf(...) expression \"" + value + "\" overflowed — leaving value as-is.",
                    "KhemistryISRU/ResolveRandf");
                return value;
            }
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
            int transferredThisUpdate = 0;
            const int maxTransfersPerUpdate = 1000;
            foreach (var matOutput in _materialOutputAmount.Keys.ToList())
            {
                double buffered = _materialOutputAmount[matOutput];
                if (double.IsNaN(buffered) || double.IsInfinity(buffered) || buffered < 0.0)
                {
                    KShared.LogError("Converter \"" + ConverterName
                        + "\": discarded an invalid material-output buffer value.",
                        "KhemistryISRU/TryTransferMaterialOutputBuffer");
                    _materialOutputAmount.Remove(matOutput);
                    _materialOutputRandomSeed.Remove(matOutput);
                    _materialOutputRandomSequence.Remove(matOutput);
                    continue;
                }
                if (buffered < 1.0) continue;

                KhemistryMaterial material = KShared.Instance?.materialList.FirstOrDefault(m => m.name == matOutput.name);
                if (material == null)
                {
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\": OUTPUT_MATERIAL \"" + matOutput.name
                        + "\" does not match any loaded KHEMISTRY_MATERIAL definition.",
                        "KhemistryISRU/TryTransferMaterialOutputBuffer");
                    continue;
                }

                while (buffered >= 1.0 && transferredThisUpdate < maxTransfersPerUpdate)
                {
                    EnsureMaterialOutputRandomState(matOutput);
                    int randomSeed = _materialOutputRandomSeed[matOutput];
                    long randomSequence = _materialOutputRandomSequence[matOutput];
                    System.Random random = CreateMaterialOutputRandom(randomSeed,
                        randomSequence);
                    string resolvedSize = ResolveRandf(matOutput.size, random);
                    Dictionary<string, string> resolvedParameters = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> kv in
                             (matOutput.parameters ?? new Dictionary<string, string>())
                             .OrderBy(value => value.Key, StringComparer.Ordinal))
                        resolvedParameters[kv.Key] = ResolveRandf(kv.Value, random);

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
                    if (randomSequence == long.MaxValue)
                    {
                        _materialOutputRandomSeed[matOutput] =
                            CreateMaterialOutputRandomSeed();
                        _materialOutputRandomSequence[matOutput] = 0L;
                    }
                    else
                        _materialOutputRandomSequence[matOutput] = randomSequence + 1L;
                    if (buffered == 0.0)
                    {
                        _materialOutputAmount.Remove(matOutput);
                        _materialOutputRandomSeed.Remove(matOutput);
                        _materialOutputRandomSequence.Remove(matOutput);
                    }
                    else
                        _materialOutputAmount[matOutput] = buffered;
                    transferredAny = true;
                    transferredThisUpdate++;
                }

                if (transferredThisUpdate >= maxTransfersPerUpdate) break;
            }

            return transferredAny;
        }
    }
}
