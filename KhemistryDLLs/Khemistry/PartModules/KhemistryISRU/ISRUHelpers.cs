namespace Khemistry
{
    public partial class KhemistryISRU
    {
        /// <summary>Formats batch progress as "0% (1.2 / 3.4 sec)", or "Off" if there's no valid recipeTime.</summary>
        protected static string FormatProgress(double progress, double recipeTime)
        {
            if (recipeTime <= 0.0) return "Off";
            double pct = 100.0 * progress / recipeTime;
            return string.Format("{0:F0}% ({1:F1} / {2:F1} sec)", pct, progress, recipeTime);
        }

        private static void ApplyShowRule(BaseEvent ev, bool showPAW, bool showEVA)
        {
            ev.guiActive = ev.guiActiveUnfocused = ev.externalToEVAOnly = showEVA;
            ev.active = showPAW || showEVA;
        }

        /// <summary>
        /// Requests (positive amount) or produces (negative amount) a resource from wherever
        /// this converter actually draws from: the vessel resource network normally, or the
        /// kerbal's fluid suit cell when moduleType == "kerbalEVA". Same amount/return contract
        /// as <see cref="Part.RequestResource(string, double)"/>.
        /// </summary>
        private double RequestResourceRouted(string name, double amount)
        {
            if (moduleType == "kerbalEVA" && _kerbalHost != null)
                return _kerbalHost.RequestSuitCellResource(name, amount);
            return part.RequestResource(name, amount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
        }

        /// <summary>
        /// Fully hides this converter from the PAW: disables every event/action and every
        /// displayed KSPField (including statusDisplay itself, so the error message the caller
        /// sets afterward via the field's raw value is never actually rendered).
        /// </summary>
        protected void DisableAllUI()
        {
            foreach (BaseEvent e in Events)
                e.active = false;
            foreach (BaseField f in Fields)
                f.guiActive = f.guiActiveEditor = false;
        }

        /// <summary>
        /// For moduleType == "kerbalEVA": strips out worker-count/worker-type/control-rule and
        /// distance-multiplier fields that don't make sense for suit-cell-routed EVA ISRU
        /// (logging a warning per field actually present in the config), and forces any MAINT
        /// powerfailResult to VOID since there's no Engineer to perform EVA self-maintenance.
        /// </summary>
        private void StripKerbalEVAIncompatibleFields(ConfigNode moduleNode)
        {
            void WarnIfPresent(ConfigNode n, string key, string context)
            {
                if (n != null && n.HasValue(key))
                    KShared.LogError(
                        "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): \"" + key
                        + "\" in " + context + " is ignored for kerbalEVA converters.",
                        "KhemistryISRU/StripKerbalEVAIncompatibleFields");
            }

            WarnIfPresent(moduleNode, "workersType", "the MODULE node");
            WarnIfPresent(moduleNode, "controlRules", "the MODULE node");

            foreach (ConfigNode recipeNode in moduleNode.GetNodes("RECIPE"))
            {
                WarnIfPresent(recipeNode, "workersEngineers", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersPilots", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersScientists", "a RECIPE node");
                WarnIfPresent(recipeNode, "workersType", "a RECIPE node");
                WarnIfPresent(recipeNode, "controlRules", "a RECIPE node");

                foreach (ConfigNode planetNode in recipeNode.GetNodes("PLANET_CONFIG"))
                    foreach (ConfigNode biomeNode in planetNode.GetNodes("BIOME_CONFIG"))
                        WarnBiomeMultipliers(biomeNode);
                foreach (ConfigNode biomeNode in recipeNode.GetNodes("BIOME_CONFIG"))
                    WarnBiomeMultipliers(biomeNode);

                void WarnBiomeMultipliers(ConfigNode biomeNode)
                {
                    WarnIfPresent(biomeNode, "maxDisplayDistanceMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "maxInteractionDistanceMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersScientistsMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersPilotsMul", "a BIOME_CONFIG node");
                    WarnIfPresent(biomeNode, "workersEngineersMul", "a BIOME_CONFIG node");
                }
            }

            foreach (KhemistryISRURecipe recipe in recipes)
            {
                recipe._workersEngineers = 0;
                recipe._workersPilots = 0;
                recipe._workersScientists = 0;
                recipe._workersEVA = false;
                recipe._workersCREW = false;

                for (int i = 0; i < recipe._passiveInputs.Count; i++)
                {
                    var pinp = recipe._passiveInputs[i];
                    if (pinp.powerfail == KhemistryISRURecipe.PowerfailResult.Maint)
                    {
                        KShared.LogError(
                            "Converter \"" + ConverterName + "\" (moduleType=kerbalEVA): Recipe \"" + recipe._name
                            + "\" has a MAINT powerfailResult, which requires an Engineer and doesn't apply here — treating as VOID.",
                            "KhemistryISRU/StripKerbalEVAIncompatibleFields");
                        pinp.powerfail = KhemistryISRURecipe.PowerfailResult.Void;
                        recipe._passiveInputs[i] = pinp;
                    }
                }
            }
        }

        /// <summary>
        /// Discards the tracked in-progress-batch consumption without refunding it.
        /// </summary>
        protected void ClearPassiveConsumption()
        {
            for (int i = 0; i < _passiveConsumedThisBatch.Count; i++)
                _passiveConsumedThisBatch[i] = 0.0;
        }
    }
}