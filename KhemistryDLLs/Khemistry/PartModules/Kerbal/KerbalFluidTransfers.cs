using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Khemistry
{
    public partial class KhemistryKerbal
    {
        internal double RequestInventoryProcessorResource(StoredPart stored, string name,
            double amount, bool allowSuitCell,
            List<KhemistryResourceNetwork.Transfer> transfers = null)
        {
            if (!IsStoredPartCurrent(stored) || string.IsNullOrWhiteSpace(name)
                || !KShared.IsFinite(amount) || amount == 0.0)
                return 0.0;

            name = name.Trim();
            bool hasPartCell = GetCellModuleSnapshot(stored) != null;
            double moved = 0.0;
            KhemistryFluidCell rateCell = hasPartCell ? ReadFluidCellPrefab(stored.partName) : null;
            double cellRequest = rateCell == null ? 0 : rateCell.LimitTransfer(CellBudget(stored), amount, true);

            if (hasPartCell && amount > 0.0)
            {
                double available = ReadResourceAmountValue(stored, name);
                double take = Math.Min(cellRequest, available);
                if (take > 0.0
                    && WriteResourceAmount(stored, name, available - take))
                    moved += take;
            }
            else if (hasPartCell && amount < 0.0
                && IsResourceAllowedForAddition(stored, name))
            {
                double current = ReadResourceAmountValue(stored, name);
                KhemistryFluidCell cell = ReadFluidCellPrefab(stored.partName);
                double totalSpace = cell == null || !KShared.IsFinite(cell.ResourceMaxAmount)
                    || cell.ResourceMaxAmount <= 0f
                    ? 0.0
                    : Math.Max(0.0, cell.ResourceMaxAmount - ReadResourceAmount(stored));
                double add = Math.Min(-cellRequest, totalSpace);
                if (add > 0.0
                    && WriteResourceAmount(stored, name, current + add))
                    moved += add;
            }

            if (moved > 0)
            {
                double signed = amount > 0 ? moved : -moved;
                rateCell.RecordTransfer(CellBudget(stored), signed, true);
                transfers?.Add(new KhemistryResourceNetwork.Transfer { amount = signed,
                    undo = value => UndoCellTransfer(stored, name, value, true) });
            }
            double remaining = Math.Abs(amount) - moved;
            if (allowSuitCell && remaining > 0.0)
            {
                double suitMoved = RequestSuitCellResource(name,
                    amount > 0.0 ? remaining : -remaining);
                if (KShared.IsFinite(suitMoved))
                {
                    moved += Math.Abs(suitMoved);
                    if (suitMoved != 0)
                        transfers?.Add(new KhemistryResourceNetwork.Transfer { amount = suitMoved,
                            undo = value => {
                                var contents = GetSuitCellDict();
                                contents.TryGetValue(name, out double current);
                                double restored = current + value;
                                if (restored <= 0) contents.Remove(name); else contents[name] = restored;
                                SetSuitCellFromDict(contents);
                            } });
                }
            }

            return amount > 0.0 ? moved : -moved;
        }

        private readonly ConditionalWeakTable<StoredPart, FluidCellRateBudget> cellRateBudgets
            = new ConditionalWeakTable<StoredPart, FluidCellRateBudget>();
        private FluidCellRateBudget CellBudget(StoredPart stored)
            => cellRateBudgets.GetValue(stored, key => new FluidCellRateBudget());

        private void UndoCellTransfer(StoredPart stored, string name, double signed,
            bool internalTransfer)
        {
            var contents = ReadCellResourceDictionary(stored);
            contents.TryGetValue(name, out double current);
            double restored = current + signed;
            if (restored <= 0) contents.Remove(name); else contents[name] = restored;
            GetCellModuleSnapshot(stored).moduleValues.SetValue("StoredResourcesData",
                KhemistryFluidCell.SerializeResources(contents), true);
            CellBudget(stored).Record(KhemistryFluidCell.RateChannel(signed < 0, internalTransfer),
                -Math.Abs(signed), UnityEngine.Time.fixedTime);
            NotifyInventoryChanged();
        }

        private sealed class CellTransfer
        {
            public StoredPart stored;
            public KhemistryResourceNetwork.Endpoint endpoint;
            public bool input;
            public double remaining, moved;
            public float range;
        }
        private readonly List<CellTransfer> cellTransfers = new List<CellTransfer>();

        private void StartCellTransfer(StoredPart stored, KhemistryResourceNetwork.Endpoint endpoint,
            bool input, double amount, float range)
        {
            if (!KShared.IsFinite(amount) || amount <= 0
                || (stored != null ? !IsStoredPartCurrent(stored) : !HasFluidSuitCell)) return;
            var cell = stored == null ? null : ReadFluidCellPrefab(stored.partName);
            if (stored != null && (cell == null || cell.GetRate(input, false) == 0))
            {
                CellTransferMessage("This cell's transfer rate is zero.");
                return;
            }
            // Replacing a job cannot accidentally duplicate a selected transfer.
            cellTransfers.RemoveAll(job => ReferenceEquals(job.stored, stored) && job.input == input);
            var transfer = new CellTransfer { stored = stored, endpoint = endpoint,
                input = input, remaining = amount, range = range };
            cellTransfers.Add(transfer);
            CellTransferMessage("Fluid cell transfer started.");
            TickCellTransfers();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Cancel Cell Transfers")]
        public void CancelCellTransfers()
        {
            cellTransfers.Clear();
            Events["CancelCellTransfers"].active = false;
            CellTransferMessage("Fluid cell transfers cancelled.");
        }

        private static void CellTransferMessage(string message)
            => ScreenMessages.PostScreenMessage(new ScreenMessage(message, 5f, ScreenMessageStyle.UPPER_CENTER));

        private bool WriteTransferCell(StoredPart stored, string name, double amount)
        {
            if (stored != null) return WriteResourceAmount(stored, name, amount);
            var contents = GetSuitCellDict();
            if (amount <= 0) contents.Remove(name); else contents[name] = amount;
            SetSuitCellFromDict(contents);
            return true;
        }

        private void TickCellTransfers()
        {
            foreach (var job in cellTransfers.ToArray())
            {
                var endpoint = job.endpoint;
                if ((job.stored != null ? !IsStoredPartCurrent(job.stored) : !HasFluidSuitCell)
                    || endpoint == null || !endpoint.IsCurrent
                    || !IsPartCurrentAndInRange(endpoint.part, job.range))
                {
                    cellTransfers.Remove(job);
                    CellTransferMessage("Fluid cell transfer stopped: cell or target unavailable/out of range.");
                    continue;
                }
                var cell = job.stored == null ? null : ReadFluidCellPrefab(job.stored.partName);
                string name = endpoint.resourceName;
                var suit = job.stored == null ? GetSuitCellDict() : null;
                double current = job.stored != null ? ReadResourceAmountValue(job.stored, name)
                    : (suit.TryGetValue(name, out double suitAmount) ? suitAmount : 0);
                double free = job.stored != null ? ReadCellTotalFreeSpace(job.stored)
                    : Math.Max(0, _suitCellMaxAmount - GetResourceDictionaryTotal(suit));
                double localAvailable = job.input ? free : current;
                bool allowed = job.stored != null ? IsResourceAllowedForAddition(job.stored, name)
                    : CanAddToSuitCell(name, suit.Keys);
                if ((job.stored != null && cell == null) || localAvailable <= 0
                    || (job.input && !allowed))
                {
                    cellTransfers.Remove(job);
                    CellTransferMessage("Fluid cell transfer stopped: cell empty, full, or incompatible.");
                    continue;
                }
                double requested = Math.Min(job.remaining, localAvailable);
                requested = Math.Min(requested, job.input ? endpoint.amount : endpoint.space);
                if (requested <= 0) continue; // A target may be temporarily rate-limited or disabled.
                double signed = job.input ? -requested : requested;
                if (job.stored != null) signed = cell.LimitTransfer(CellBudget(job.stored), signed, false);
                if (signed == 0) continue;
                // Reserve the local side first; settle only the amount accepted by the endpoint.
                if (!WriteTransferCell(job.stored, name, current - signed)) continue;
                double actual = endpoint.Request(-signed);
                WriteTransferCell(job.stored, name, current + actual);
                if (job.stored != null) cell.RecordTransfer(CellBudget(job.stored), -actual, false);
                double moved = Math.Abs(actual);
                job.remaining = Math.Max(0, job.remaining - moved);
                job.moved += moved;
                if (job.remaining <= 1e-9)
                {
                    cellTransfers.Remove(job);
                    CellTransferMessage(string.Format("Transferred {0:F2} units of {1}.", job.moved, name));
                }
            }
            Events["CancelCellTransfers"].active = cellTransfers.Count > 0;
        }
    }
}
