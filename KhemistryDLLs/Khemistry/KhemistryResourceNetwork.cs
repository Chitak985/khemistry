using System;
using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    /// <summary>Explicit access to internal storage; never registers hidden stock tanks.</summary>
    public static class KhemistryResourceNetwork
    {
        public sealed class Transfer
        {
            internal Action<double> undo;
            internal double amount;
        }

        public static void Rollback(List<Transfer> transfers)
        {
            if (transfers == null) return;
            for (int i = transfers.Count - 1; i >= 0; i--) transfers[i].undo(transfers[i].amount);
            transfers.Clear();
        }

        internal static void RollbackAmount(List<Transfer> transfers, double amount)
        {
            for (int i = transfers.Count - 1; i >= 0 && amount > 0.0; i--)
            {
                Transfer transfer = transfers[i];
                double take = Math.Min(amount, Math.Abs(transfer.amount));
                double signed = Math.Sign(transfer.amount) * take;
                transfer.undo(signed);
                transfer.amount -= signed;
                amount -= take;
                if (transfer.amount == 0.0) transfers.RemoveAt(i);
            }
        }

        internal static IEnumerable<KhemistryAdvancedStorage> ConnectedStorage(
            Part origin, string name, ResourceFlowMode flow)
        {
            if (origin == null) yield break;
            var definition = PartResourceLibrary.Instance?.GetDefinition(name);
            if (definition == null) yield break;
            if (flow == ResourceFlowMode.NULL) flow = definition.resourceFlowMode;
            IEnumerable<Part> parts = origin.vessel != null
                ? (IEnumerable<Part>)origin.vessel.parts : new[] { origin };
            foreach (Part target in parts.OrderByDescending(p => p.GetResourcePriority()))
            {
                if (target == null || (target != origin
                    && (flow == ResourceFlowMode.NO_FLOW
                        || !origin.CanCrossfeed(target, definition.id, flow)))) continue;
                foreach (KhemistryAdvancedStorage storage in
                    target.Modules.OfType<KhemistryAdvancedStorage>())
                    yield return storage;
            }
        }

        internal static double RequestInternal(Part origin, string name, double amount,
            ResourceFlowMode flow, List<Transfer> transfers = null)
        {
            if (!KShared.IsFinite(amount) || amount == 0.0) return 0.0;
            double moved = 0.0;
            foreach (var storage in ConnectedStorage(origin, name, flow))
            {
                double step = storage.RequestStoredResource(name, amount - moved);
                if (step == 0.0) continue;
                moved += step;
                if (transfers != null)
                {
                    var captured = storage;
                    transfers.Add(new Transfer { amount = step,
                        undo = signed => captured.UndoTransfer(name, signed) });
                }
                if (Math.Abs(moved) >= Math.Abs(amount)) break;
            }
            return moved;
        }

        /// <summary>Part.RequestResource sign convention, including internal AdvancedStorage.</summary>
        public static double Request(Part origin, string name, double amount,
            ResourceFlowMode flow = ResourceFlowMode.STAGE_PRIORITY_FLOW,
            List<Transfer> transfers = null)
        {
            if (origin == null || string.IsNullOrEmpty(name)
                || !KShared.IsFinite(amount) || amount == 0.0) return 0.0;
            double moved = RequestInternal(origin, name, amount, flow, transfers);
            double remaining = amount - moved;
            if (remaining == 0.0) return moved;

            // Stock requests retain KSP's staging, crossfeed and balancing rules.
            // Record exact tank deltas only when the caller needs synchronous rollback.
            Dictionary<PartResource, double> before = null;
            if (transfers != null)
            {
                IEnumerable<Part> parts = origin.vessel != null
                    ? (IEnumerable<Part>)origin.vessel.parts : new[] { origin };
                before = parts.SelectMany(p => p.Resources)
                    .Where(r => r.resourceName == name).ToDictionary(r => r, r => r.amount);
            }
            double stock = origin.RequestResource(name, remaining, flow);
            if (before != null)
                foreach (var saved in before)
                {
                    double delta = saved.Value - saved.Key.amount;
                    if (delta == 0.0) continue;
                    PartResource resource = saved.Key;
                    transfers.Add(new Transfer { amount = delta,
                        undo = signed => resource.amount += signed });
                }
            return moved + stock;
        }

        /// <summary>A selectable, live resource endpoint on one specific nearby part.</summary>
        public sealed class Endpoint
        {
            public Part part;
            public string resourceName;
            internal PartResource stock;
            internal KhemistryAdvancedStorage storage;
            internal KhemistryFluidCell cell;
            private bool IsCurrent => part != null && (stock != null
                ? part.Resources.Any(r => ReferenceEquals(r, stock))
                : storage != null ? part.Modules.Cast<PartModule>().Any(m => ReferenceEquals(m, storage))
                : cell != null && part.Modules.Cast<PartModule>().Any(m => ReferenceEquals(m, cell)));
            public double amount => !IsCurrent ? 0.0 : stock != null
                ? (stock.flowState ? Math.Max(0.0, stock.amount) : 0.0)
                : storage != null ? storage.GetAvailableAmount(resourceName)
                : cell.GetStoredAmount(resourceName);
            public double space => !IsCurrent ? 0.0 : stock != null
                ? (stock.flowState ? Math.Max(0.0, stock.maxAmount - stock.amount) : 0.0)
                : storage != null ? storage.GetAvailableSpace(resourceName)
                : cell.CanAddResource(resourceName, cell.GetStoredResources().Keys)
                    ? Math.Max(0.0, cell.ResourceMaxAmount - cell.GetStoredTotal()) : 0.0;
            public double maxAmount => amount + space;
            public double Request(double requested)
            {
                if (!IsCurrent || !KShared.IsFinite(requested) || requested == 0.0) return 0.0;
                if (storage != null) return storage.RequestStoredResource(resourceName, requested);
                if (cell != null) return cell.RequestStoredResource(resourceName, requested);
                double moved = requested > 0.0 ? Math.Min(requested, amount)
                    : -Math.Min(-requested, space);
                stock.amount -= moved;
                return moved;
            }
        }

        public static IEnumerable<Endpoint> GetEndpoints(Part part)
        {
            if (part == null) yield break;
            foreach (PartResource resource in part.Resources)
                yield return new Endpoint { part = part, resourceName = resource.resourceName,
                    stock = resource };
            foreach (var storage in part.Modules.OfType<KhemistryAdvancedStorage>())
                foreach (string name in storage.SupportedResources
                    .Union(storage.GetStoredResources().Keys))
                    yield return new Endpoint { part = part, resourceName = name, storage = storage };
            foreach (var cell in part.Modules.OfType<KhemistryFluidCell>())
                foreach (string name in cell.SupportedResources.Union(cell.GetStoredResources().Keys))
                    yield return new Endpoint { part = part, resourceName = name, cell = cell };
        }
    }
}
