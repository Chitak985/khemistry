using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Khemistry
{
    // Plan without mutating KSP state. Apply is synchronous and remains reversible until
    // the resource/material/tree transaction succeeds; publish inventory events only then.
    internal sealed class CargoInventoryTransaction
    {
        private readonly ModuleInventoryPart inventory;
        private readonly StoredPart protectedPart;
        private readonly Dictionary<int, StoredPart> original = new Dictionary<int, StoredPart>();
        private readonly Dictionary<int, int> originalAmounts = new Dictionary<int, int>();
        private readonly Dictionary<int, StoredPart> planned = new Dictionary<int, StoredPart>();
        private bool applied;
        private static readonly MethodInfo ResetNames = typeof(ModuleInventoryPart).GetMethod(
            "ResetInventoryPartsByName", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateUI = typeof(ModuleInventoryPart).GetMethod(
            "UpdateModuleUI", BindingFlags.Instance | BindingFlags.NonPublic);

        internal CargoInventoryTransaction(ModuleInventoryPart inventory, StoredPart protectedPart)
        {
            if (ResetNames == null || UpdateUI == null)
                throw new NotSupportedException("This KSP inventory API is not supported.");
            this.inventory = inventory;
            this.protectedPart = protectedPart;
            for (int i = 0; i < inventory.storedParts.Count; i++)
            {
                StoredPart item = inventory.storedParts.At(i);
                original.Add(item.slotIndex, item);
                originalAmounts.Add(item.slotIndex, item.quantity);
                planned.Add(item.slotIndex, Copy(item));
            }
        }

        private static StoredPart Copy(StoredPart item) => new StoredPart(item.partName, item.slotIndex)
        {
            snapshot = item.snapshot, quantity = item.quantity,
            stackCapacity = item.stackCapacity, variantName = item.variantName
        };

        internal bool Consume(string name, int amount, out string error)
        {
            error = null;
            foreach (var pair in planned.OrderBy(pair => pair.Key).ToList())
            {
                if (amount == 0) break;
                StoredPart item = pair.Value;
                if (item.partName != name || item.snapshot == null
                    || (original.TryGetValue(pair.Key, out var old) && ReferenceEquals(old, protectedPart))) continue;
                int take = Math.Min(amount, Math.Max(0, item.quantity));
                item.quantity -= take; amount -= take;
                if (item.quantity == 0) planned.Remove(pair.Key);
            }
            if (amount == 0) return true;
            error = "Missing " + amount + " cargo part(s): " + name;
            return false;
        }

        // Do not merge freshly manufactured parts into a used/modified stack. In particular,
        // this must not copy a charged battery or a filled cell into every new item.
        private static bool SameState(ProtoPartSnapshot first, ProtoPartSnapshot second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            var a = new ConfigNode("PART"); var b = new ConfigNode("PART");
            first.Save(a); second.Save(b);
            foreach (string key in new[] { "uid", "persistentId", "mid", "launchID", "flag" })
            { a.RemoveValues(key); b.RemoveValues(key); }
            return a.ToString() == b.ToString();
        }

        internal bool Produce(string name, int amount, out string error)
        {
            error = null;
            if (amount == 0) return true;
            Part prefab = PartLoader.getPartInfoByName(name)?.partPrefab;
            ModuleCargoPart cargo = prefab?.FindModuleImplementing<ModuleCargoPart>();
            if (cargo == null || !KShared.IsFinite(cargo.packedVolume) || cargo.packedVolume < 0)
            { error = "Not a storable cargo part: " + name; return false; }
            var snapshot = new ProtoPartSnapshot(prefab, inventory.vessel?.protoVessel);
            int stackCapacity = Math.Max(1, snapshot.moduleCargoStackableQuantity);
            foreach (var pair in planned.OrderBy(pair => pair.Key))
            {
                if (amount == 0) break;
                StoredPart item = pair.Value;
                if (item.partName != name || item.quantity >= item.stackCapacity
                    || (original.TryGetValue(pair.Key, out var old) && ReferenceEquals(old, protectedPart))
                    || !SameState(item.snapshot, snapshot)) continue;
                int add = Math.Min(amount, item.stackCapacity - item.quantity);
                item.quantity += add; amount -= add;
            }
            // At most InventorySlots iterations, even for an expression returning int.MaxValue.
            for (int slot = 0; slot < inventory.InventorySlots && amount > 0; slot++)
            {
                if (planned.ContainsKey(slot)) continue;
                int count = Math.Min(amount, stackCapacity);
                planned.Add(slot, new StoredPart(name, slot)
                {
                    snapshot = new ProtoPartSnapshot(prefab, inventory.vessel?.protoVessel),
                    quantity = count, stackCapacity = stackCapacity,
                    variantName = snapshot.moduleVariantName
                });
                amount -= count;
            }
            if (amount != 0) { error = "Not enough inventory slots for: " + name; return false; }
            return true;
        }

        internal bool HasCapacity(out string error)
        {
            error = null;
            double volume = 0, mass = 0;
            foreach (StoredPart item in planned.Values)
            {
                if (item.quantity <= 0) continue;
                ProtoPartSnapshot snapshot = item.snapshot;
                ModuleCargoPart cargo = snapshot?.partPrefab?.FindModuleImplementing<ModuleCargoPart>();
                if (snapshot == null || cargo == null || cargo.packedVolume < 0)
                { error = "Cannot determine inventory capacity for: " + item.partName; return false; }
                volume += (double)cargo.packedVolume * item.quantity;
                double unitMass = snapshot.mass;
                foreach (ProtoPartResourceSnapshot resource in snapshot.resources)
                    unitMass += resource.amount * (resource.definition?.density ?? 0);
                mass += unitMass * item.quantity;
            }
            if (!KShared.IsFinite(volume) || !KShared.IsFinite(mass) || volume < 0 || mass < 0)
            { error = "Invalid cargo inventory mass or volume"; return false; }
            if (inventory.HasPackedVolumeLimit && volume > inventory.packedVolumeLimit + 1e-6)
            { error = "Not enough cargo inventory volume"; return false; }
            if (inventory.HasMassLimit && mass > inventory.massLimit + 1e-6)
            { error = "Not enough cargo inventory mass capacity"; return false; }
            return true;
        }

        internal void Apply()
        {
            applied = true;
            foreach (int slot in original.Keys) inventory.storedParts.Remove(slot);
            foreach (var pair in planned)
            {
                StoredPart item = pair.Value;
                // Keep live references to held processors/cells, including their snapshots.
                if (original.TryGetValue(pair.Key, out StoredPart old)
                    && ReferenceEquals(old.snapshot, item.snapshot))
                { old.quantity = item.quantity; item = old; }
                inventory.storedParts.Add(pair.Key, item);
            }
        }

        internal void Rollback()
        {
            if (!applied) return;
            foreach (int slot in planned.Keys) inventory.storedParts.Remove(slot);
            foreach (var pair in original)
            {
                pair.Value.quantity = originalAmounts[pair.Key];
                inventory.storedParts.Add(pair.Key, pair.Value);
            }
            applied = false;
        }

        internal void Publish()
        {
            // KSP keeps a private name-count cache in addition to storedParts. Invalidate
            // it exactly as the stock Store/Clear methods do before notifying listeners.
            ResetNames.Invoke(inventory, null);
            UpdateUI.Invoke(inventory, null);
            foreach (int slot in original.Keys.Union(planned.Keys))
                GameEvents.onModuleInventorySlotChanged.Fire(inventory, slot);
            GameEvents.onModuleInventoryChanged.Fire(inventory);
        }
    }
}
