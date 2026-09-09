using System;
using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryKerbal
    {
        private sealed class HeldPartEVAProcessor
        {
            public StoredPart stored;
            public KhemistryISRU prefab;
            public ProtoPartModuleSnapshot snapshot;
            public ConfigNode config;
        }

        private List<HeldPartEVAProcessor> GetHeldPartEVAProcessors()
        {
            var result = new List<HeldPartEVAProcessor>();
            if (_inventory == null) return result;

            for (int storedIndex = 0; storedIndex < _inventory.storedParts.Count;
                 storedIndex++)
            {
                StoredPart stored = _inventory.storedParts.At(storedIndex);
                AvailablePart available = PartLoader.getPartInfoByName(stored?.partName);
                Part prefabPart = available?.partPrefab;
                ConfigNode partConfig = prefabPart?.partInfo?.partConfig;
                if (stored?.snapshot == null || prefabPart == null || partConfig == null)
                    continue;

                List<KhemistryISRU> prefabModules = prefabPart.Modules
                    .OfType<KhemistryISRU>().ToList();
                List<ConfigNode> moduleConfigs = partConfig.GetNodes("MODULE")
                    .Where(node => node.GetValue("name") == "KhemistryISRU")
                    .ToList();
                List<ProtoPartModuleSnapshot> savedModules = stored.snapshot.modules
                    .Where(module => module.moduleName == "KhemistryISRU")
                    .ToList();

                int moduleCount = Math.Min(prefabModules.Count, moduleConfigs.Count);
                for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                {
                    ConfigNode config = moduleConfigs[moduleIndex];
                    if (!KhemistryISRU.IsPartEVAConfig(config)) continue;

                    while (savedModules.Count <= moduleIndex)
                    {
                        var created = new ProtoPartModuleSnapshot(
                            prefabModules[savedModules.Count]);
                        stored.snapshot.modules.Add(created);
                        savedModules.Add(created);
                        NotifyInventoryChanged();
                    }

                    EnsureHeldFluidCellSnapshots(stored, prefabPart);
                    MigrateLegacyCombinedProcessor(stored,
                        savedModules[moduleIndex]);
                    result.Add(new HeldPartEVAProcessor
                    {
                        stored = stored,
                        prefab = prefabModules[moduleIndex],
                        snapshot = savedModules[moduleIndex],
                        config = config
                    });
                }
            }
            return result;
        }

        private void EnsureHeldFluidCellSnapshots(StoredPart stored, Part prefabPart)
        {
            KhemistryFluidCell cell = prefabPart?.FindModuleImplementing<KhemistryFluidCell>();
            if (cell == null || stored?.snapshot == null) return;

            bool changed = false;
            ProtoPartModuleSnapshot snapshot = GetCellModuleSnapshot(stored);
            if (snapshot == null)
            {
                snapshot = new ProtoPartModuleSnapshot(cell);
                stored.snapshot.modules.Add(snapshot);
                changed = true;
            }
            if (snapshot.moduleValues != null
                && !snapshot.moduleValues.HasValue("StoredResourcesData"))
            {
                snapshot.moduleValues.AddValue("StoredResourcesData", "");
                changed = true;
            }

            if (changed) NotifyInventoryChanged();
        }

        private void MigrateLegacyCombinedProcessor(StoredPart stored,
            ProtoPartModuleSnapshot target)
        {
            if (stored?.snapshot?.modules == null || target?.moduleValues == null
                || string.Equals(target.moduleValues.GetValue(
                    "legacyCombinedProcessorMigrated"), "True",
                    StringComparison.OrdinalIgnoreCase))
                return;

            ProtoPartModuleSnapshot legacy = stored.snapshot.modules.FirstOrDefault(module =>
                module.moduleName == "KhemistryEVACombinedProcessor");
            if (legacy?.moduleValues == null)
            {
                target.moduleValues.SetValue("legacyCombinedProcessorMigrated",
                    "True", true);
                return;
            }

            var remaining = DeserializeResourceDictionary(
                legacy.moduleValues.GetValue("storedResourcesData"));
            ProtoPartModuleSnapshot cellSnapshot = GetCellModuleSnapshot(stored);
            KhemistryFluidCell cell = ReadFluidCellPrefab(stored.partName);
            Dictionary<string, double> cellContents = ReadCellResourceDictionary(stored);
            foreach (KeyValuePair<string, double> value in remaining.ToList())
            {
                if (cell == null || cellSnapshot?.moduleValues == null
                    || !cell.CanAddResource(value.Key, cellContents.Keys))
                    continue;
                double totalSpace = Math.Max(0.0, cell.ResourceMaxAmount
                    - KhemistryFluidCell.GetResourceTotal(cellContents));
                double add = Math.Min(value.Value, totalSpace);
                if (add <= 0.0) continue;
                cellContents.TryGetValue(value.Key, out double current);
                cellContents[value.Key] = current + add;
                double left = value.Value - add;
                if (left <= 1e-9) remaining.Remove(value.Key);
                else remaining[value.Key] = left;
            }
            if (cellSnapshot?.moduleValues != null)
                cellSnapshot.moduleValues.SetValue("StoredResourcesData",
                    KhemistryFluidCell.SerializeResources(cellContents), true);
            legacy.moduleValues.SetValue("storedResourcesData",
                SerializeResourceDictionary(remaining), true);

            string oldRecipe = legacy.moduleValues.GetValue("activeConverterName")?.Trim();
            if (string.IsNullOrEmpty(target.moduleValues.GetValue("activeRecipeName"))
                && !string.IsNullOrEmpty(oldRecipe))
                target.moduleValues.SetValue("activeRecipeName", oldRecipe, true);
            if (bool.TryParse(legacy.moduleValues.GetValue("isRunning"),
                    out bool wasRunning) && wasRunning)
            {
                target.moduleValues.SetValue("isRunning", "True", true);
                target.moduleValues.SetValue("state",
                    KShared.ChargablePartState.On.ToString(), true);
            }

            if (remaining.Count == 0)
                target.moduleValues.SetValue("legacyCombinedProcessorMigrated",
                    "True", true);
            NotifyInventoryChanged();
        }

        private void ShowPartEVAProcessorMenu(HeldPartEVAProcessor processor)
        {
            if (!IsStoredPartCurrent(processor.stored)) return;
            KhemistryISRU.InventoryProcessorInfo info = processor.prefab
                .ReadInventoryInfo(this, processor.stored, processor.snapshot,
                    processor.config);
            if (info == null) return;

            var actions = new List<string>();
            if (info.chargingRequired)
            {
                if (info.state == KShared.ChargablePartState.Charging)
                    actions.Add("Disable Charging");
                else if (info.state != KShared.ChargablePartState.On)
                    actions.Add("Enable Charging");
                if (info.state != KShared.ChargablePartState.On
                    && info.chargePercent >= 100f)
                    actions.Add("Prepare Converter");
                if (info.state == KShared.ChargablePartState.On)
                    actions.Add("Turn Off Converter");
            }
            if (info.isRunning)
                actions.Add("Stop Converter");
            else if (!info.needsMaintenance
                && info.state == KShared.ChargablePartState.On)
                actions.Add("Start Converter");
            if (!info.isRunning && info.recipeNames.Count > 1)
                actions.Add("Switch Recipe");

            string title = (info.converterName ?? "Converter") + " — "
                + (info.activeRecipeName ?? "No recipe") + " — "
                + (info.status ?? "Stopped") + " — " + info.progress;
            KShared.Instance?.ShowSelector(title, actions, action =>
                ExecutePartEVAProcessorAction(processor, info, action));
        }

        private void ExecutePartEVAProcessorAction(HeldPartEVAProcessor processor,
            KhemistryISRU.InventoryProcessorInfo info, string action)
        {
            if (!IsStoredPartCurrent(processor.stored)) return;
            if (action == "Switch Recipe")
            {
                List<string> choices = info.recipeNames
                    .Where(name => name != info.activeRecipeName).ToList();
                KShared.Instance?.ShowSelector("Select recipe", choices, recipe =>
                {
                    if (IsStoredPartCurrent(processor.stored))
                        RunPartEVAProcessorAction(processor,
                            KhemistryISRU.InventoryAction.SwitchRecipe, recipe);
                });
                return;
            }

            KhemistryISRU.InventoryAction selected;
            switch (action)
            {
                case "Enable Charging":
                    selected = KhemistryISRU.InventoryAction.EnableCharging; break;
                case "Disable Charging":
                    selected = KhemistryISRU.InventoryAction.DisableCharging; break;
                case "Prepare Converter":
                    selected = KhemistryISRU.InventoryAction.TurnOn; break;
                case "Turn Off Converter":
                    selected = KhemistryISRU.InventoryAction.TurnOff; break;
                case "Start Converter":
                    selected = KhemistryISRU.InventoryAction.Start; break;
                case "Stop Converter":
                    selected = KhemistryISRU.InventoryAction.Stop; break;
                default:
                    return;
            }
            RunPartEVAProcessorAction(processor, selected);
        }

        private void RunPartEVAProcessorAction(HeldPartEVAProcessor processor,
            KhemistryISRU.InventoryAction action, string recipeName = null)
        {
            bool succeeded = processor.prefab.ExecuteInventoryAction(this,
                processor.stored, processor.snapshot, processor.config, action,
                recipeName);
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                succeeded ? "Held converter updated."
                    : "That converter action is not available right now.",
                4f, ScreenMessageStyle.UPPER_CENTER));
        }
    }
}
