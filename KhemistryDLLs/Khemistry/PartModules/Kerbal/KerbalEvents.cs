using System.Collections.Generic;

namespace Khemistry
{
    public partial class KhemistryKerbal
    {
        ///// Occupation System /////
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Leave current occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void LeaveOccupation() => occupation = null;


        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void DisableOccupation()
        {
            occupation = null;
            canBeOccupied = false;
        }


        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable automatic occupation",
                 groupName = "occupation", groupDisplayName = "Occupation", groupStartCollapsed = false,
                 externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void EnableOccupation() => canBeOccupied = true;


        ///// EVA Fluid Cell Actions /////
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from cell to nearby part",
                 groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVASendResources()
        {
            KShared shared = KShared.Instance;
            if (shared == null) { KShared.LogError("KShared is null!", "KhemistryKerbal/EVATakeResources"); return; }
            KShared.Log("Called! (Transfer from ... to nearby part button)", "KhemistryKerbal/EVASendResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForSend(cells[0]);
            }
            else
            {
                List<string> labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    if (cells[i].isSuit)
                    {
                        var dict = GetSuitCellDict();
                        double total = GetResourceDictionaryTotal(dict);
                        if (dict.Count == 0)
                            labels.Add(string.Format("{0}: Empty (0/{1:F2})", cellLabel, _suitCellMaxAmount));
                        else
                        {
                            List<string> cp = new List<string>();
                            foreach (var kvp in dict)
                                cp.Add(string.Format("{0}: {1:F2}", kvp.Key, kvp.Value));
                            labels.Add(string.Format("{0}: {1} ({2:F2}/{3:F2})", cellLabel,
                                string.Join(", ", cp.ToArray()), total, _suitCellMaxAmount));
                        }
                    }
                    else
                        labels.Add(string.Format("{0}: {1}", cellLabel,
                            DescribeStoredCell(cells[i].stored)));
                }
                shared.ShowSelector("Which cell to send from?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForSend(cells[index]);
                });
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Transfer from nearby part to cell",
         groupName = "fluidcelleva", groupDisplayName = "Fluid Cells", groupStartCollapsed = false)]
        public void EVATakeResources()
        {
            var shared = KShared.Instance;
            if (shared == null) { KShared.LogError("KShared is null!", "KhemistryKerbal/EVATakeResources"); return; }
            KShared.Log("Called! (Transfer from ... to cell button)", "KhemistryKerbal/EVATakeResources");

            var cells = GetAllCellRefs();
            if (cells.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No fluid cells available.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (cells.Count == 1)
            {
                ShowPartSelectorForTake(cells[0]);
            }
            else
            {
                var labels = new List<string>();
                for (int i = 0; i < cells.Count; i++)
                {
                    string cellLabel = GetCellLabel(cells[i], i);
                    if (!cells[i].isSuit)
                        labels.Add(string.Format("{0}: {1}", cellLabel,
                            DescribeStoredCell(cells[i].stored)));
                    else
                    {
                        string resName = ReadCellResourceName(cells[i]);
                        float resAmount = ReadCellResourceAmount(cells[i]);
                        float maxAmount = ReadCellMaxAmount(cells[i]);
                        labels.Add(string.IsNullOrEmpty(resName)
                            ? string.Format("{0}: Empty", cellLabel)
                            : string.Format("{0}: {1} {2:F1}/{3:F1} units",
                                cellLabel, resName, resAmount, maxAmount));
                    }
                }
                shared.ShowSelector("Which cell to fill?", labels, label =>
                {
                    int index = labels.IndexOf(label);
                    if (index >= 0) ShowPartSelectorForTake(cells[index]);
                });
            }
        }

        ///// EVA Processor Actions /////
        
        [KSPEvent(guiActive = true, guiActiveEditor = false,
                  guiName = "Use Held Processor", groupName = "processoreva",
                  groupDisplayName = "Processors", groupStartCollapsed = false)]
        public void EVAUseProcessor()
        {
            List<HeldPartEVAProcessor> processors = GetHeldPartEVAProcessors();
            if (processors.Count == 0)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "No partEVA BatchISRU processor is in this kerbal's inventory.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (processors.Count == 1)
            {
                ShowPartEVAProcessorMenu(processors[0]);
                return;
            }

            var labels = new List<string>();
            var byLabel = new Dictionary<string, HeldPartEVAProcessor>();
            foreach (HeldPartEVAProcessor processor in processors)
            {
                KhemistryISRU.InventoryProcessorInfo info = processor.prefab
                    .ReadInventoryInfo(this, processor.stored, processor.snapshot,
                        processor.config);
                string title = PartLoader.getPartInfoByName(processor.stored.partName)
                    ?.title ?? processor.stored.partName;
                string label = MakeUniqueLabel(labels, title + " / "
                    + (info?.converterName ?? "Converter") + " — "
                    + (info?.isRunning == true ? "Running" : "Stopped"));
                labels.Add(label);
                byLabel.Add(label, processor);
            }

            KShared.Instance?.ShowSelector("Select held processor", labels, label =>
            {
                if (byLabel.TryGetValue(label, out HeldPartEVAProcessor selected)
                    && IsStoredPartCurrent(selected.stored))
                    ShowPartEVAProcessorMenu(selected);
            });
        }
    }
}
