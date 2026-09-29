using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Khemistry
{
    // Registered by the optional ConstructionOverhaul assembly; no hard dependency on it.
    public static class KerbalKSCBridge
    {
        public static Func<KhemistryMaterialInstance, bool> SendMaterial;
        public static bool Available => SendMaterial != null;
    }

    public partial class KhemistryKerbal
    {
        [KSPField] public double KSCResourceMaxAmount = 100;
        [KSPField] public double KSCMaterialMaxVolume = 1;

        private bool CanSendToKSC()
        {
            if (_disabledDuplicate || !KerbalKSCBridge.Available || KShared.Instance == null
                || !HighLogic.LoadedSceneIsFlight) return false;
            var active = FlightGlobals.ActiveVessel;
            if (active == null || active != vessel || !active.isEVA
                || active.mainBody == null || active.mainBody.name != FlightGlobals.GetHomeBodyName()
                || !active.LandedOrSplashed)
            {
                KSCMessage("The active kerbal must remain landed or splashed on the KSC's home body.");
                return false;
            }
            return true;
        }

        private static void KSCMessage(string message)
            => ScreenMessages.PostScreenMessage(new ScreenMessage(message, 5f, ScreenMessageStyle.UPPER_CENTER));

        private void UpdateKSCSenderControls()
        {
            Events["SendSuitResourcesToKSC"].active = KerbalKSCBridge.Available && HasFluidSuitCell;
            Events["SendSuitMaterialsToKSC"].active = KerbalKSCBridge.Available && HasMaterialSuitCell;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, active = false,
            guiName = "Send stored resources to the KSC", groupName = "resourcesending",
            groupDisplayName = "Resource Sender", groupStartCollapsed = false)]
        public void SendSuitResourcesToKSC()
        {
            if (!CanSendToKSC() || !HasFluidSuitCell) return;
            if (!KShared.IsFinite(KSCResourceMaxAmount) || KSCResourceMaxAmount <= 0)
            { KSCMessage("KSCResourceMaxAmount must be finite and positive."); return; }
            var names = GetSuitCellDict().Where(p => p.Value > 0).Select(p => p.Key).OrderBy(n => n).ToList();
            if (names.Count == 0) { KSCMessage("No suit-cell resources are available to send."); return; }
            KShared.Instance.ShowResourceSelector(names, SendSuitResource);
        }

        private void SendSuitResource(string name)
        {
            if (!CanSendToKSC() || !HasFluidSuitCell || !KShared.IsFinite(KSCResourceMaxAmount)
                || KSCResourceMaxAmount <= 0 || PartResourceLibrary.Instance?.GetDefinition(name) == null) return;
            var contents = GetSuitCellDict();
            if (!contents.TryGetValue(name, out double available) || available <= 0) return;
            var ledger = KShared.Instance.ResourceDict;
            if (ledger == null) return;
            ledger.TryGetValue(name, out double existing);
            if (!KShared.IsFinite(existing) || existing < 0) return;
            double sent = Math.Min(available, Math.Min(KSCResourceMaxAmount, double.MaxValue - existing));
            double total = existing + sent;
            if (sent <= 0 || !KShared.IsFinite(total) || total <= existing)
            { KSCMessage("The KSC resource balance cannot accept this transfer."); return; }
            if (available == sent) contents.Remove(name); else contents[name] = available - sent;
            SetSuitCellFromDict(contents);
            ledger[name] = total;
            KSCMessage(string.Format("Transferred {0:G6} units of {1} to the KSC.", sent, name));
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, active = false,
            guiName = "Send stored materials to the KSC", groupName = "materialsending",
            groupDisplayName = "Material Sender", groupStartCollapsed = false)]
        public void SendSuitMaterialsToKSC()
        {
            if (!CanSendToKSC() || !HasMaterialSuitCell) return;
            if (!KShared.IsFinite(KSCMaterialMaxVolume) || KSCMaterialMaxVolume <= 0)
            { KSCMessage("KSCMaterialMaxVolume must be finite and positive."); return; }
            var options = new Dictionary<string, KhemistryMaterialInstance>();
            foreach (var material in materialSuitCellContents)
            {
                if (material?.material == null || material.amount <= 0) continue;
                string label = material.material.name + " | " + material.shape + " | " + material.size
                    + " | " + material.amount.ToString(CultureInfo.InvariantCulture)
                    + " available | stack " + (options.Count + 1);
                options.Add(label, material);
            }
            if (options.Count == 0) { KSCMessage("No suit-cell materials are available to send."); return; }
            KShared.Instance.ShowSelector("Send suit materials to the KSC", options.Keys.ToList(), label =>
            {
                if (options.TryGetValue(label, out var selected)) SendSuitMaterial(selected);
            });
        }

        private void SendSuitMaterial(KhemistryMaterialInstance selected)
        {
            if (!CanSendToKSC() || !HasMaterialSuitCell || selected?.material == null
                || !materialSuitCellContents.Any(m => ReferenceEquals(m, selected))
                || selected.amount <= 0 || !KShared.IsFinite(KSCMaterialMaxVolume) || KSCMaterialMaxVolume <= 0
                || !KShared.IsFinite(selected.volume) || selected.volume <= 0) return;
            double maxUnits = Math.Floor(KSCMaterialMaxVolume / selected.volume);
            int amount = (int)Math.Min(selected.amount, maxUnits);
            if (amount <= 0) { KSCMessage("One unit exceeds KSCMaterialMaxVolume."); return; }
            var piece = new KhemistryMaterialInstance(selected) { amount = amount };
            piece.UpdateParams("KhemistryKerbal/SendSuitMaterial");
            if (!KerbalKSCBridge.SendMaterial(piece))
            { KSCMessage("The KSC material ledger could not accept this transfer."); return; }
            if (amount == selected.amount) materialSuitCellContents.Remove(selected);
            else
            {
                selected.amount -= amount;
                selected.UpdateParams("KhemistryKerbal/SendSuitMaterial");
            }
            KSCMessage(string.Format("Transferred {0} of {1} ({2:G6} m³) to the KSC.",
                amount, piece.material.name, piece.TotalVolume));
        }
    }
}
