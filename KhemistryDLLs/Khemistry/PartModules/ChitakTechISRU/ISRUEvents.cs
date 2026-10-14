using System.Collections.Generic;
using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechISRU
    {
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Enable Charging",
                  groupName = "khemistryisru")]
        public void EnableCharging()
        {
            if (!chargingRequired) return;
            if (chargePercent >= 100f) return;
            state = CTShared.ChargablePartState.Charging;
            CTShared.Log("Charging enabled.", "ChitakTechISRU/EnableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Disable Charging",
                  groupName = "khemistryisru", active = false)]
        public void DisableCharging()
        {
            if (!chargingRequired) return;
            if (state != CTShared.ChargablePartState.Charging) return;
            state = CTShared.ChargablePartState.On;
            CTShared.Log("Charging disabled.", "ChitakTechISRU/DisableCharging");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Prepare converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOnConverter()
        {
            if (chargingRequired && chargePercent < EffectiveChargeThreshold)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter must reach its charge threshold before turning on.", 5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            state = CTShared.ChargablePartState.On;
            CTShared.Log("Converter turned ON.", "ChitakTechISRU/TurnOnContainer");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Turn off converter",
                  groupName = "khemistryisru", active = false)]
        public void TurnOffConverter()
        {
            state = CTShared.ChargablePartState.Off;
            CTShared.Log("Converter turned OFF.", "ChitakTechISRU/TurnOffContainer");
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
            if (!CanOperate) return;

            ChitakTechISRUBiomeConfig biomeConfig = _activeRecipe != null && _runtimeData != null
                ? GetEffectiveBiomeConfig()
                : null;
            List<string> depositConditions = GetRequiredDepositConditions(biomeConfig);
            if (depositConditions.Count > 0 && !IsAtRequiredDeposit(biomeConfig))
            {
                CTShared.LogError(
                    "Converter \"" + ConverterName + "\": No matching deposit (" + string.Join(", ", depositConditions) + ") found at this location.",
                    "ChitakTechISRU/StartConverter");
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": Can't operate — not at a required deposit.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            if (_activeRecipe != null && _activeRecipe.UsesParallaxScatters
                && !TryGetNearbyParallaxTarget(true, out _))
            {
                statusDisplay = KhemistryParallaxIntegration.NoSuitableTreeMessage;
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName + "\": "
                    + KhemistryParallaxIntegration.NoSuitableTreeMessage + ".",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }

            isRunning = true;
            CTShared.Log("Converter \"" + ConverterName + "\" started.", "ChitakTechISRU/StartConverter");
            UpdateEventVisibility();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop Converter",
                  groupName = "khemistryisru")]
        public void StopConverter()
        {
            isRunning = false;
            CTShared.Log("Converter \"" + ConverterName + "\" stopped.", "ChitakTechISRU/StopConverter");
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

            var shared = CTShared.Instance;
            if (shared == null) return;

            var labels = new List<string>();
            foreach (KhemistryISRURecipe r in recipes)
                labels.Add(r._name + (r == _activeRecipe ? " [Active]" : ""));

            shared.ShowSelector("Switch Recipe", labels, label =>
            {
                int idx = labels.IndexOf(label);
                if (idx < 0) return;
                if (recipes[idx]._name == _activeRecipe?._name) return;

                RefundPassiveConsumption();
                ApplyRecipe(recipes[idx]);
                if (chargingRequired && chargePercent < EffectiveChargeThreshold && state == CTShared.ChargablePartState.On)
                    state = CTShared.ChargablePartState.Off;
                else if (!chargingRequired)
                    state = CTShared.ChargablePartState.On;
                UpdateEventVisibility();
                UpdateUI();
                ShowRecipeSettings(_activeRecipe);

                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Switched to recipe \"" + _activeRecipe._name + "\".", 5f, ScreenMessageStyle.UPPER_CENTER));
                CTShared.Log("Converter \"" + ConverterName + "\" switched active recipe to \"" + _activeRecipe._name + "\".",
                    "ChitakTechISRU/SwitchRecipe");
            });
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false,
                  guiName = "See/Change Parameters", groupName = "khemistryisru")]
        public void ChangeRecipeParameters()
        {
            if (isRunning)
            {
                ScreenMessages.PostScreenMessage(new ScreenMessage(
                    "Converter \"" + ConverterName
                    + "\": Stop the converter before changing recipe parameters.",
                    5f, ScreenMessageStyle.UPPER_CENTER));
                return;
            }
            ShowRecipeSettings(_activeRecipe);
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
            CTShared.Log("Converter \"" + ConverterName + "\" maintained by " + kerbal.name + ".",
                "ChitakTechISRU/PerformMaintenance");
            ScreenMessages.PostScreenMessage(new ScreenMessage(
                "Converter \"" + ConverterName + "\": Maintenance complete.", 5f, ScreenMessageStyle.UPPER_CENTER));
            UpdateEventVisibility();
        }
    }
}
