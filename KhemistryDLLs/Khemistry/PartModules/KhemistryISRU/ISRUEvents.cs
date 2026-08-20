using System.Collections.Generic;
using System.Linq;

namespace Khemistry
{
    public partial class KhemistryISRU
    {
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryisru")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (state == KShared.ChargablePartState.On) return;
            state = KShared.ChargablePartState.Charging;
            KShared.Log("Charging enabled.", "KhemistryISRU/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != KShared.ChargablePartState.Charging) return;
            state = KShared.ChargablePartState.Off;
            KShared.Log("Charging disabled.", "KhemistryISRU/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Prepare converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOnConverter()
        {
            if (chargingRequired && chargePercent < 100f)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter must be fully charged before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = KShared.ChargablePartState.On;
            KShared.Log("Converter turned ON.", "KhemistryISRU/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOffConverter()
        {
            state = KShared.ChargablePartState.Off;
            KShared.Log("Converter turned OFF.", "KhemistryISRU/TurnOffContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter",
        groupName = "khemistryisru")]
        public void StartConverter()
        {
            if (needsMaintenance)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Requires maintenance before starting.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            if (state != KShared.ChargablePartState.On) return;

            if (_depositConditions.Count > 0 && !IsAtRequiredDeposit())
            {
                KShared.LogError(
                    "Converter \"" + ConverterName + "\": No matching deposit (" + string.Join(", ", _depositConditions) + ") found at this location.",
                    "KhemistryISRU/StartConverter");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Can't operate — not at a required deposit.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            isRunning = true;
            KShared.Log("Converter \"" + ConverterName + "\" started.", "KhemistryISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            KShared.Log("Converter \"" + ConverterName + "\" stopped.", "KhemistryISRU/StopConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Switch Recipe",
                  groupName = "khemistryisru")]
        public void SwitchRecipe()
        {
            if (isRunning)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Stop the converter before switching recipes.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (recipes.Count <= 1)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": No other recipes available to switch to.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            var shared = KShared.Instance;
            if (shared == null) return;

            var labels = new List<string>();
            foreach (KhemistryISRURecipe r in recipes)
                labels.Add(r._name + (r == _activeRecipe ? " [Active]" : ""));

            shared.ShowSelector("Switch Recipe", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx < 0) return;
                if (recipes[idx] == _activeRecipe) return;

                ApplyRecipe(recipes[idx]);
                UpdateEventVisibility();
                UpdateUI();

                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Switched to recipe \"" + _activeRecipe._name + "\".", 5f, ScreenMessageStyle.UPPER_CENTER));
                KShared.Log("Converter \"" + ConverterName + "\" switched active recipe to \"" + _activeRecipe._name + "\".",
                    "KhemistryISRU/SwitchRecipe");
            });
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Perform Maintenance",
                  groupName = "khemistryisru",
                  externalToEVAOnly = true, guiActiveUnfocused = false, unfocusedRange = 10f)]
        public void PerformMaintenance()
        {
            ProtoCrewMember kerbal = FlightGlobals.ActiveVessel?.GetVesselCrew()?.FirstOrDefault();
            if (kerbal == null || kerbal.trait != "Engineer")
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Requires maintenance by an Engineer.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            needsMaintenance = false;
            KShared.Log("Converter \"" + ConverterName + "\" maintained by " + kerbal.name + ".",
                "KhemistryISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + ConverterName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }
    }
}
