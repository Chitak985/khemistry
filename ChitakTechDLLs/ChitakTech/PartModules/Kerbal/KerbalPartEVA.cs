using System;
using System.Collections.Generic;
using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechKerbal
    {
        private sealed class HeldPartEVAProcessor
        {
            public StoredPart stored;
            public ChitakTechISRU prefab;
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

                List<ChitakTechISRU> prefabModules = prefabPart.Modules
                    .OfType<ChitakTechISRU>().ToList();
                List<ConfigNode> moduleConfigs = partConfig.GetNodes("MODULE")
                    .Where(node => node.GetValue("name") == "ChitakTechISRU")
                    .ToList();
                List<ProtoPartModuleSnapshot> savedModules = stored.snapshot.modules
                    .Where(module => module.moduleName == "ChitakTechISRU")
                    .ToList();

                int moduleCount = Math.Min(prefabModules.Count, moduleConfigs.Count);
                for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                {
                    ConfigNode config = moduleConfigs[moduleIndex];
                    if (!ChitakTechISRU.IsPartEVAConfig(config)) continue;

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
            ChitakTechFluidCell cell = prefabPart?.FindModuleImplementing<ChitakTechFluidCell>();
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
                module.moduleName == "ChitakTechEVACombinedProcessor");
            if (legacy?.moduleValues == null)
            {
                target.moduleValues.SetValue("legacyCombinedProcessorMigrated",
                    "True", true);
                return;
            }

            var remaining = DeserializeResourceDictionary(
                legacy.moduleValues.GetValue("storedResourcesData"));
            ProtoPartModuleSnapshot cellSnapshot = GetCellModuleSnapshot(stored);
            ChitakTechFluidCell cell = ReadFluidCellPrefab(stored.partName);
            Dictionary<string, double> cellContents = ReadCellResourceDictionary(stored);
            foreach (KeyValuePair<string, double> value in remaining.ToList())
            {
                if (cell == null || cellSnapshot?.moduleValues == null
                    || !cell.CanAddResource(value.Key, cellContents.Keys))
                    continue;
                double totalSpace = Math.Max(0.0, cell.ResourceMaxAmount
                    - ChitakTechFluidCell.GetResourceTotal(cellContents));
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
                    ChitakTechFluidCell.SerializeResources(cellContents), true);
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
                    CTShared.ChargablePartState.On.ToString(), true);
            }

            if (remaining.Count == 0)
                target.moduleValues.SetValue("legacyCombinedProcessorMigrated",
                    "True", true);
            NotifyInventoryChanged();
        }

        private void ShowPartEVAProcessorMenu(HeldPartEVAProcessor processor)
        {
            if (!IsStoredPartCurrent(processor.stored)) return;
            ChitakTechISRU.InventoryProcessorInfo info = processor.prefab
                .ReadInventoryInfo(this, processor.stored, processor.snapshot,
                    processor.config);
            if (info == null) return;

            var actions = new List<string>();
            if (info.chargingRequired)
            {
                if (info.state == CTShared.ChargablePartState.Charging)
                    actions.Add("Disable Charging");
                else if (info.chargePercent < 100f)
                    actions.Add("Enable Charging");
                if (info.state != CTShared.ChargablePartState.On
                    && info.chargePercent >= info.chargeThreshold)
                    actions.Add("Prepare Converter");
                if (info.state != CTShared.ChargablePartState.Off)
                    actions.Add("Turn Off Converter");
            }
            if (info.isRunning)
                actions.Add("Stop Converter");
            else if (!info.needsMaintenance
                && info.canOperate)
                actions.Add("Start Converter");
            if (!info.isRunning && info.recipeNames.Count > 1)
                actions.Add("Switch Recipe");
            if (!info.isRunning && info.recipeSettings.Count > 0)
                actions.Add("See/Change Parameters");
            if (info.hasRepairableMaintenance) actions.Add("Fix maintenance");
            if (info.hasMaintenance) actions.Add("Auto maintenance fixing: "
                + (info.autoMaintenanceFixing ? "On" : "Off"));

            string title = (info.converterName ?? "Converter") + " — "
                + (info.activeRecipeName ?? "No recipe") + " — "
                + (info.status ?? "Stopped") + " — " + info.progress
                + (info.hasMaintenance ? " — " + info.maintenance : "");
            CTShared.Instance?.ShowSelector(title, actions, action =>
                ExecutePartEVAProcessorAction(processor, info, action));
        }

        private void ExecutePartEVAProcessorAction(HeldPartEVAProcessor processor,
            ChitakTechISRU.InventoryProcessorInfo info, string action)
        {
            if (!IsStoredPartCurrent(processor.stored)) return;
            if (action == "Switch Recipe")
            {
                List<string> choices = info.recipeNames
                    .Where(name => name != info.activeRecipeName).ToList();
                CTShared.Instance?.ShowSelector("Select recipe", choices, recipe =>
                {
                    if (IsStoredPartCurrent(processor.stored))
                    {
                        bool switched = RunPartEVAProcessorAction(processor,
                            ChitakTechISRU.InventoryAction.SwitchRecipe, recipe);
                        if (switched) ShowPartEVARecipeSettings(processor);
                    }
                });
                return;
            }
            if (action == "See/Change Parameters")
            {
                ShowPartEVARecipeSettings(processor, info);
                return;
            }

            ChitakTechISRU.InventoryAction selected;
            switch (action)
            {
                case "Fix maintenance":
                    selected = ChitakTechISRU.InventoryAction.FixMaintenance; break;
                case "Auto maintenance fixing: On":
                case "Auto maintenance fixing: Off":
                    selected = ChitakTechISRU.InventoryAction.ToggleAutoMaintenanceFixing; break;
                case "Enable Charging":
                    selected = ChitakTechISRU.InventoryAction.EnableCharging; break;
                case "Disable Charging":
                    selected = ChitakTechISRU.InventoryAction.DisableCharging; break;
                case "Prepare Converter":
                    selected = ChitakTechISRU.InventoryAction.TurnOn; break;
                case "Turn Off Converter":
                    selected = ChitakTechISRU.InventoryAction.TurnOff; break;
                case "Start Converter":
                    selected = ChitakTechISRU.InventoryAction.Start; break;
                case "Stop Converter":
                    selected = ChitakTechISRU.InventoryAction.Stop; break;
                default:
                    return;
            }
            RunPartEVAProcessorAction(processor, selected);
        }

        private bool RunPartEVAProcessorAction(HeldPartEVAProcessor processor,
            ChitakTechISRU.InventoryAction action, string recipeName = null)
        {
            bool succeeded = processor.prefab.ExecuteInventoryAction(this,
                processor.stored, processor.snapshot, processor.config, action,
                recipeName);
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                succeeded ? "Held converter updated."
                    : "That converter action is not available right now.",
                4f, ScreenMessageStyle.UPPER_CENTER));
            return succeeded;
        }

        private void ShowPartEVARecipeSettings(HeldPartEVAProcessor processor,
            ChitakTechISRU.InventoryProcessorInfo info = null)
        {
            if (!IsStoredPartCurrent(processor.stored)) return;
            if (info == null)
                info = processor.prefab.ReadInventoryInfo(this, processor.stored,
                    processor.snapshot, processor.config);
            if (info == null || info.recipeSettings.Count == 0) return;
            string expectedRecipe = info.activeRecipeName;
            CTShared.Instance?.ShowRecipeSettings(
                (expectedRecipe ?? "Recipe") + " Parameters",
                info.recipeSettings, info.recipeSettingValues, values =>
                {
                    if (!IsStoredPartCurrent(processor.stored)) return;
                    ChitakTechISRU.InventoryProcessorInfo current = processor.prefab
                        .ReadInventoryInfo(this, processor.stored,
                            processor.snapshot, processor.config);
                    bool saved = current != null && !current.isRunning
                        && string.Equals(current.activeRecipeName, expectedRecipe,
                            StringComparison.Ordinal)
                        && processor.prefab.SetInventoryRecipeSettings(this,
                            processor.stored, processor.snapshot, processor.config,
                            values);
                    ScreenMessages.PostScreenMessage(new ScreenMessage(
                        saved ? "Held converter parameters saved."
                            : "Those converter parameters could not be saved.",
                        4f, ScreenMessageStyle.UPPER_CENTER));
                });
        }
    }
}
