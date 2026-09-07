using System;
using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        internal enum InventoryAction
        {
            EnableCharging,
            DisableCharging,
            TurnOn,
            TurnOff,
            Start,
            Stop,
            SwitchRecipe
        }

        internal sealed class InventoryProcessorInfo
        {
            public string converterName;
            public string activeRecipeName;
            public string status;
            public string progress;
            public bool isRunning;
            public bool needsMaintenance;
            public bool chargingRequired;
            public float chargePercent;
            public KShared.ChargablePartState state;
            public readonly List<string> recipeNames = new List<string>();
        }

        private void ResetInventoryPersistentState()
        {
            isRunning = false;
            needsMaintenance = false;
            state = KShared.ChargablePartState.Off;
            chargePercent = 0f;
            activeRecipeName = null;
            batchProgress = 0.0;
            hasLastBiome = false;
            lastBiomePlanet = "";
            lastBiomeName = "";
            statusDisplay = "Stopped";
            progressDisplay = "Off";
            chargeDisplay = "N/A";
            stateDisplay = "Off";
            _loadedAuthoritativePersistentState = false;
        }

        private bool BeginInventorySession(KhemistryKerbal host, StoredPart stored,
            ProtoPartModuleSnapshot snapshot, ConfigNode config)
        {
            if (host == null || stored == null || snapshot?.moduleValues == null
                || !IsPartEVAConfig(config))
                return false;

            if (_inventoryPrefabPristineState == null)
            {
                _inventoryPrefabPristineState = new ConfigNode("MODULE");
                Save(_inventoryPrefabPristineState);
            }
            _kerbalHost = host;
            _inventoryStoredPart = stored;
            _inventorySessionActive = true;
            _inventoryOriginalSnapshotText = snapshot.moduleValues.ToString();
            host.BeginInventoryProcessorChanges();
            try
            {
                ResetInventoryPersistentState();
                Load(snapshot.moduleValues);

                _fatalConfigError = false;
                if (!_inventoryConfigLoaded)
                {
                    LoadConfig(config);
                    _inventoryConfigLoaded = !_fatalConfigError;
                }
                else
                {
                    moduleType = "partEVA";
                    SelectActiveRecipeFromLoadedState();
                }

                if (_fatalConfigError || recipes.Count == 0 || _activeRecipe == null)
                {
                    RestoreInventoryPrefabState();
                    host.EndInventoryProcessorChanges();
                    ClearInventorySession();
                    return false;
                }

                if (float.IsNaN(chargePercent) || float.IsInfinity(chargePercent))
                    chargePercent = 0f;
                chargePercent = UnityEngine.Mathf.Clamp(chargePercent, 0f, 100f);
                if (!Enum.IsDefined(typeof(KShared.ChargablePartState), state))
                    state = KShared.ChargablePartState.Off;
                if (chargingRequired && state == KShared.ChargablePartState.On
                    && chargePercent < 100f)
                    state = KShared.ChargablePartState.Off;
                if (!chargingRequired)
                    state = KShared.ChargablePartState.On;

                RestoreMaterialOutputBuffer();
                _runtimeData = new KhemistryRuntimeData(host.vessel);
                ResetForBiomeTransition();
                return true;
            }
            catch (Exception ex)
            {
                KShared.LogError("Could not initialize held partEVA converter \""
                    + ConverterName + "\": " + ex,
                    "KhemistryISRU/BeginInventorySession");
                RestoreInventoryPrefabState();
                host.EndInventoryProcessorChanges();
                ClearInventorySession();
                return false;
            }
        }

        private void EndInventorySession(ProtoPartModuleSnapshot snapshot)
        {
            try
            {
                if (snapshot?.moduleValues != null)
                {
                    Save(snapshot.moduleValues);
                    if (!string.Equals(_inventoryOriginalSnapshotText,
                            snapshot.moduleValues.ToString(), StringComparison.Ordinal))
                        _kerbalHost?.NotifyInventoryProcessorChanged();
                }
            }
            finally
            {
                RestoreInventoryPrefabState();
                _kerbalHost?.EndInventoryProcessorChanges();
                ClearInventorySession();
            }
        }

        private void ClearInventorySession()
        {
            _runtimeData = null;
            _inventoryStoredPart = null;
            _inventorySessionActive = false;
            _inventoryOriginalSnapshotText = null;
            _kerbalHost = null;
        }

        private void RestoreInventoryPrefabState()
        {
            if (_inventoryPrefabPristineState != null)
                Load(_inventoryPrefabPristineState);
            _activeRecipe = null;
            _passiveTimers.Clear();
            _passiveConsumedThisBatch.Clear();
            chargingRequired = _moduleChargingRequired;
            chargeRate = _moduleChargeRate;
            chargeDecayRate = _moduleChargeDecayRate;
            _chargeNames.Clear();
            _chargeNames.AddRange(_moduleChargeNames);
            _chargeAmounts.Clear();
            _chargeAmounts.AddRange(_moduleChargeAmounts);
            _controlsShowPAW = true;
            _controlsShowEVA = false;
            statusDisplay = "Stopped";
            progressDisplay = "Off";
            _loadedAuthoritativePersistentState = false;
        }

        internal bool RunInventoryCycle(KhemistryKerbal host, StoredPart stored,
            ProtoPartModuleSnapshot snapshot, ConfigNode config, double dt)
        {
            bool began = BeginInventorySession(host, stored, snapshot, config);
            if (!began)
                return false;

            try
            {
                if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0.0)
                    return false;

                HandleCharging(dt);
                ProcessPendingPassiveRefunds();
                TryTransferMaterialOutputBuffer();

                if (needsMaintenance || !isRunning
                    || state != KShared.ChargablePartState.On
                    || _activeRecipe == null)
                {
                    statusDisplay = needsMaintenance
                        ? "Needs maintenance"
                        : (!isRunning ? "Stopped" : "Not ready");
                    progressDisplay = "Off";
                    return false;
                }

                return RunBatchCycle(dt);
            }
            finally
            {
                EndInventorySession(snapshot);
            }
        }

        internal InventoryProcessorInfo ReadInventoryInfo(KhemistryKerbal host,
            StoredPart stored, ProtoPartModuleSnapshot snapshot, ConfigNode config)
        {
            if (!BeginInventorySession(host, stored, snapshot, config))
                return null;

            try
            {
                var info = new InventoryProcessorInfo
                {
                    converterName = ConverterName,
                    activeRecipeName = activeRecipeName,
                    status = needsMaintenance ? "Needs maintenance"
                        : (isRunning
                            ? (state == KShared.ChargablePartState.On
                                ? "Running" : "Not ready")
                            : "Stopped"),
                    progress = FormatProgress(batchProgress,
                        _activeRecipe?._recipeTime ?? 0.0),
                    isRunning = isRunning,
                    needsMaintenance = needsMaintenance,
                    chargingRequired = chargingRequired,
                    chargePercent = chargePercent,
                    state = state
                };
                info.recipeNames.AddRange(recipes.Select(recipe => recipe._name));
                return info;
            }
            finally
            {
                EndInventorySession(snapshot);
            }
        }

        internal bool ExecuteInventoryAction(KhemistryKerbal host, StoredPart stored,
            ProtoPartModuleSnapshot snapshot, ConfigNode config, InventoryAction action,
            string recipeName = null)
        {
            if (!BeginInventorySession(host, stored, snapshot, config))
                return false;

            try
            {
                switch (action)
                {
                    case InventoryAction.EnableCharging:
                        if (!chargingRequired || state == KShared.ChargablePartState.On)
                            return false;
                        state = KShared.ChargablePartState.Charging;
                        return true;

                    case InventoryAction.DisableCharging:
                        if (!chargingRequired || state != KShared.ChargablePartState.Charging)
                            return false;
                        state = KShared.ChargablePartState.Off;
                        return true;

                    case InventoryAction.TurnOn:
                        if (chargingRequired && chargePercent < 100f) return false;
                        state = KShared.ChargablePartState.On;
                        return true;

                    case InventoryAction.TurnOff:
                        state = KShared.ChargablePartState.Off;
                        return true;

                    case InventoryAction.Start:
                        if (needsMaintenance || state != KShared.ChargablePartState.On)
                            return false;
                        KhemistryISRUBiomeConfig biomeConfig = _activeRecipe.GetBiomeConfig(
                            _runtimeData.planet, _runtimeData.biome);
                        if (GetRequiredDepositConditions(biomeConfig).Count > 0
                            && !IsAtRequiredDeposit(biomeConfig))
                            return false;
                        isRunning = true;
                        return true;

                    case InventoryAction.Stop:
                        isRunning = false;
                        return true;

                    case InventoryAction.SwitchRecipe:
                        if (isRunning || string.IsNullOrEmpty(recipeName)) return false;
                        KhemistryISRURecipe selected = recipes.FirstOrDefault(recipe =>
                            recipe._name == recipeName);
                        if (selected == null || selected == _activeRecipe) return false;
                        RefundPassiveConsumption();
                        ApplyRecipe(selected);
                        if (chargingRequired && chargePercent < 100f
                            && state == KShared.ChargablePartState.On)
                            state = KShared.ChargablePartState.Off;
                        else if (!chargingRequired)
                            state = KShared.ChargablePartState.On;
                        return true;
                }
                return false;
            }
            finally
            {
                EndInventorySession(snapshot);
            }
        }
    }
}
