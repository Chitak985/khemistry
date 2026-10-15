using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechStorage
    {
        private sealed class StoragePassiveInput
        {
            public ChitakTechISRURecipe.PassiveResourceInput input;
            public bool whenFilled;
            public string key;
            public double elapsed, consumed;
        }

        private sealed class PassiveRefund
        {
            public string name;
            public ResourceFlowMode flow;
            public double amount;
        }

        private readonly List<StoragePassiveInput> _storagePassiveInputs = new List<StoragePassiveInput>();
        private readonly List<PassiveRefund> _storagePassiveRefunds = new List<PassiveRefund>();
        private ConfigNode _savedStoragePassiveState;
        private bool _passivePaused, _passiveNeedsMaintenance, _processingPassive;

        private bool LoadPassiveInputs(ConfigNode module)
        {
            _storagePassiveInputs.Clear();
            var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ConfigNode node in module.GetNodes("PINPUT_RESOURCE"))
            {
                if (!ChitakTechISRURecipe.TryParsePassiveInput(node, "AdvancedStorage",
                        out ChitakTechISRURecipe.PassiveResourceInput input))
                    return false;
                string condition = (node.GetValue("condition") ?? "always").Trim();
                bool filled = string.Equals(condition, "moreThan0", StringComparison.OrdinalIgnoreCase);
                if ((!filled && !string.Equals(condition, "always", StringComparison.OrdinalIgnoreCase))
                    || PartResourceLibrary.Instance?.GetDefinition(input.resourceName) == null)
                {
                    CTShared.LogError("PINPUT_RESOURCE requires a known resource and condition always or moreThan0.",
                        "ChitakTechStorage/LoadPassiveInputs");
                    return false;
                }
                // Match saved timers by the complete definition, not their position in the config.
                string key = input.resourceName.Length + ":" + input.resourceName + "|"
                    + Number(input.amount) + "|" + Number(input.period) + "|" + input.flowMode
                    + "|" + input.powerfail + "|" + Number(input.powerfailExplosionRadius)
                    + "|" + Number(input.powerfailExplosionTemperature) + "|" + input.ignorePowerfail
                    + "|" + filled;
                duplicates.TryGetValue(key, out int occurrence);
                duplicates[key] = occurrence + 1;
                key += "|" + occurrence;
                var entry = new StoragePassiveInput { input = input, whenFilled = filled, key = key };
                ConfigNode saved = _savedStoragePassiveState?.GetNodes("INPUT")
                    .FirstOrDefault(n => n.GetValue("key") == key);
                if (saved != null)
                {
                    entry.elapsed = ReadNonNegative(saved, "elapsed");
                    entry.consumed = ReadNonNegative(saved, "consumed");
                }
                _storagePassiveInputs.Add(entry);
            }
            _savedStoragePassiveState = null;
            return true;
        }

        private void LoadStoragePassiveState(ConfigNode node)
        {
            _storagePassiveInputs.Clear();
            _storagePassiveRefunds.Clear();
            _savedStoragePassiveState = node.GetNodes("STORAGE_PASSIVE_STATE").FirstOrDefault();
            _passivePaused = _savedStoragePassiveState?.GetValue("paused") == "True";
            _passiveNeedsMaintenance = _savedStoragePassiveState?.GetValue("maintenance") == "True";
            if (_savedStoragePassiveState == null) return;
            foreach (ConfigNode saved in _savedStoragePassiveState.GetNodes("REFUND"))
            {
                string name = saved.GetValue("name");
                double amount = ReadNonNegative(saved, "amount");
                if (!string.IsNullOrWhiteSpace(name) && amount > 0.0
                    && Enum.TryParse(saved.GetValue("flow"), out ResourceFlowMode flow)
                    && Enum.IsDefined(typeof(ResourceFlowMode), flow))
                    _storagePassiveRefunds.Add(new PassiveRefund { name = name, amount = amount, flow = flow });
            }
        }

        private void SaveStoragePassiveState(ConfigNode node)
        {
            node.RemoveNodes("STORAGE_PASSIVE_STATE");
            ConfigNode saved = node.AddNode("STORAGE_PASSIVE_STATE");
            // OnSave can run before OnStart/config loading.
            if (_savedStoragePassiveState != null)
            {
                _savedStoragePassiveState.CopyTo(saved);
                return;
            }
            saved.AddValue("paused", _passivePaused);
            saved.AddValue("maintenance", _passiveNeedsMaintenance);
            foreach (StoragePassiveInput entry in _storagePassiveInputs)
            {
                ConfigNode input = saved.AddNode("INPUT");
                input.AddValue("key", entry.key);
                input.AddValue("elapsed", Number(entry.elapsed));
                input.AddValue("consumed", Number(entry.consumed));
            }
            foreach (PassiveRefund refund in _storagePassiveRefunds)
            {
                ConfigNode pending = saved.AddNode("REFUND");
                pending.AddValue("name", refund.name);
                pending.AddValue("amount", Number(refund.amount));
                pending.AddValue("flow", refund.flow);
            }
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static double ReadNonNegative(ConfigNode node, string key)
            => double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value) && CTShared.IsFinite(value) && value >= 0.0 ? value : 0.0;

        private void ProcessStoragePassiveInputs(double dt, double poweredDt)
        {
            RetryStoragePassiveRefunds();
            if (_passiveNeedsMaintenance) return;
            bool filled = HasAnyStoredResources();
            var active = _storagePassiveInputs.Select(p => p.whenFilled ? filled
                : state == CTShared.ChargablePartState.On).ToArray();
            // A paused interval is retried, not multiplied by every waiting physics tick.
            for (int i = 0; i < _storagePassiveInputs.Count; i++)
                if (active[i] && !_passivePaused)
                {
                    StoragePassiveInput entry = _storagePassiveInputs[i];
                    double elapsed = entry.elapsed + (entry.whenFilled ? dt : poweredDt);
                    if (!CTShared.IsFinite(elapsed)) { _passivePaused = true; return; }
                    entry.elapsed = elapsed;
                }
            double[] timers = _storagePassiveInputs.Select(p => p.elapsed).ToArray();
            double[] consumed = _storagePassiveInputs.Select(p => p.consumed).ToArray();
            var step = new List<ChitakTechResourceNetwork.Transfer>();
            _processingPassive = true;
            try
            {
                for (int i = 0; i < _storagePassiveInputs.Count; i++)
                {
                    if (!active[i]) continue;
                    StoragePassiveInput entry = _storagePassiveInputs[i];
                    ChitakTechISRURecipe.PassiveResourceInput input = entry.input;
                    input.amount = CTShared.Multiply(input.amount, EffectivePassiveMultiplier);
                    double due = Math.Floor(entry.elapsed / input.period);
                    if (due < 1.0) continue;
                    if (input.amount == 0.0)
                    {
                        entry.elapsed = System.Math.Max(0, entry.elapsed - due * input.period);
                        continue;
                    }
                    double needed = due * input.amount;
                    if (!CTShared.IsFinite(needed) || !CTShared.IsFinite(entry.consumed + needed))
                    {
                        RollbackStoragePassiveStep(step, timers, consumed);
                        _passivePaused = true;
                        return;
                    }
                    var transfers = new List<ChitakTechResourceNetwork.Transfer>();
                    double got = ChitakTechResourceNetwork.Request(part, input.resourceName, needed,
                        input.flowMode, transfers);
                    double satisfied = Math.Min(due, Math.Floor((got + input.amount * 1e-9) / input.amount));
                    double kept = Math.Min(got, satisfied * input.amount);
                    if (got > kept) ChitakTechResourceNetwork.RollbackAmount(transfers, got - kept);
                    step.AddRange(transfers);
                    entry.consumed += kept;
                    entry.elapsed = Math.Max(0.0, entry.elapsed - satisfied * input.period);
                    if (satisfied >= due) continue;
                    if (input.ignorePowerfail)
                    {
                        entry.elapsed = Math.Max(0.0, entry.elapsed - (due - satisfied) * input.period);
                        continue;
                    }
                    if (input.powerfail == ChitakTechISRURecipe.PowerfailResult.Pause)
                    {
                        RollbackStoragePassiveStep(step, timers, consumed);
                        _passivePaused = true;
                        return;
                    }
                    FailStoragePassiveInput(input);
                    return;
                }
                _passivePaused = false;
            }
            finally { _processingPassive = false; }
        }

        private void RollbackStoragePassiveStep(List<ChitakTechResourceNetwork.Transfer> step,
            double[] timers, double[] consumed)
        {
            ChitakTechResourceNetwork.Rollback(step);
            for (int i = 0; i < _storagePassiveInputs.Count; i++)
            {
                _storagePassiveInputs[i].elapsed = timers[i];
                _storagePassiveInputs[i].consumed = consumed[i];
            }
        }

        private void FailStoragePassiveInput(ChitakTechISRURecipe.PassiveResourceInput failed)
        {
            if (failed.powerfail == ChitakTechISRURecipe.PowerfailResult.Stop)
                foreach (StoragePassiveInput entry in _storagePassiveInputs)
                    if (entry.consumed > 0.0)
                        _storagePassiveRefunds.Add(new PassiveRefund
                        {
                            name = entry.input.resourceName, flow = entry.input.flowMode,
                            amount = entry.consumed
                        });
            foreach (StoragePassiveInput entry in _storagePassiveInputs)
                entry.elapsed = entry.consumed = 0.0;
            _passivePaused = false;
            _passiveNeedsMaintenance = failed.powerfail == ChitakTechISRURecipe.PowerfailResult.Maint;
            // Try refunds before turning off so this storage can receive its own refund.
            RetryStoragePassiveRefunds();
            state = CTShared.ChargablePartState.Off;
            if (failed.powerfail == ChitakTechISRURecipe.PowerfailResult.Explode)
                CTShared.TriggerExplosionWithHeat(part, (float)failed.powerfailExplosionRadius,
                    (float)failed.powerfailExplosionTemperature);
        }

        private void RetryStoragePassiveRefunds()
        {
            foreach (PassiveRefund refund in _storagePassiveRefunds)
            {
                double returned = -ChitakTechResourceNetwork.Request(part, refund.name, -refund.amount, refund.flow);
                if (CTShared.IsFinite(returned) && returned > 0.0)
                    refund.amount = Math.Max(0.0, refund.amount - returned);
            }
            _storagePassiveRefunds.RemoveAll(p => p.amount <= 0.0);
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
            groupName = "chitaktechadvstorage", externalToEVAOnly = true,
            guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformStorageMaintenance()
        {
            if (!_passiveNeedsMaintenance) return;
            ProtoCrewMember engineer = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
            if (engineer == null || engineer.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Container requires maintenance by an Engineer.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            _passiveNeedsMaintenance = false;
            UpdateUI();
        }
    }
}
