using System;
using System.Globalization;

namespace Khemistry
{
    public partial class KhemistryISRURecipe
    {
        // Shared by ISRU recipes and AdvancedStorage: keep one configuration contract.
        internal static bool TryParsePassiveInput(ConfigNode pinputNode, string context,
            out PassiveResourceInput result)
        {
            result = default;
            string resName = pinputNode.GetValue("name")?.Trim();
            if (string.IsNullOrEmpty(resName))
            {
                KShared.LogNoValueInNode("PINPUT_RESOURCE", "name", "\"" + context + "\" ", "PINPUT_RESOURCE/Parse");
                return false;
            }

            double amount = KShared.GetDoubleValueFromCFG(pinputNode, "amount", 0.0);
            double period = 1.0;
            bool validPeriod = pinputNode.HasValue("period")
                ? TryReadRequiredDouble(pinputNode, "period", out period)
                : (!pinputNode.HasValue("peirod")
                    || TryReadRequiredDouble(pinputNode, "peirod", out period));
            if (!pinputNode.HasValue("period") && pinputNode.HasValue("peirod"))
                KShared.LogWarning("\"" + context
                    + "\": PINPUT_RESOURCE uses legacy misspelling \"peirod\"; use \"period\".",
                    "PINPUT_RESOURCE/Parse");
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
            {
                KShared.LogError("\"" + context + "\": PINPUT_RESOURCE \""
                    + resName + "\" has an invalid amount and was skipped.",
                    "PINPUT_RESOURCE/Parse");
                return false;
            }
            if (!validPeriod || double.IsNaN(period) || double.IsInfinity(period)
                || period <= 0.0)
            {
                KShared.LogError("\"" + context + "\": PINPUT_RESOURCE \""
                    + resName + "\" has an invalid period and was skipped.",
                    "PINPUT_RESOURCE/Parse");
                return false;
            }

            ResourceFlowMode flowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW;
            string pFlowStr = pinputNode.GetValue("flowmode");
            if (!string.IsNullOrEmpty(pFlowStr))
            {
                if (Enum.TryParse(pFlowStr.Trim(), true, out ResourceFlowMode pParsed)
                    && Enum.IsDefined(typeof(ResourceFlowMode), pParsed))
                    flowMode = pParsed;
                else
                    KShared.LogError(
                        "\"" + context + "\": Unknown flowmode \"" + pFlowStr + "\" for PINPUT_RESOURCE " + resName + ", defaulting to STAGE_PRIORITY_FLOW.",
                        "PINPUT_RESOURCE/Parse");
            }

            bool ignorePowerfail = false;
            string ignorePowerfailRaw = pinputNode.GetValue("ignorePowerfail");
            if (!string.IsNullOrEmpty(ignorePowerfailRaw)
                && !bool.TryParse(ignorePowerfailRaw.Trim(), out ignorePowerfail))
            {
                KShared.LogError("\"" + context + "\": PINPUT_RESOURCE \""
                    + resName + "\" has an invalid ignorePowerfail value \""
                    + ignorePowerfailRaw + "\" and was skipped.",
                    "PINPUT_RESOURCE/Parse");
                return false;
            }

            // Accept both spellings: "powerfail" (correct, used in actual configs) and
            // "powefail" (the original literal spec) — the former takes precedence.
            PowerfailResult powerfail = PowerfailResult.Pause;
            double explosionRadius = 0.0;
            double explosionTemperature = 0.0;
            string pfRaw = pinputNode.GetValue("powerfail") ?? pinputNode.GetValue("powefail");
            if (!string.IsNullOrEmpty(pfRaw))
            {
                string pf = pfRaw.Trim().Trim('"').ToUpperInvariant();
                if (pf == "PAUSE")
                {
                    powerfail = PowerfailResult.Pause;
                }
                else if (pf == "STOP")
                {
                    powerfail = PowerfailResult.Stop;
                }
                else if (pf == "VOID")
                {
                    powerfail = PowerfailResult.Void;
                }
                else if (pf == "MAINT")
                {
                    powerfail = PowerfailResult.Maint;
                }
                else if (pf.StartsWith("EXPLODE,"))
                {
                    string[] parts = pf.Substring(8).Split(',');
                    if (parts.Length == 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tempC)
                        && radius > 0.0 && !double.IsNaN(radius) && !double.IsInfinity(radius)
                        && !double.IsNaN(tempC) && !double.IsInfinity(tempC))
                    {
                        powerfail = PowerfailResult.Explode;
                        explosionRadius = radius;
                        explosionTemperature = tempC;
                    }
                    else
                    {
                        KShared.LogError(
                            "\"" + context + "\": Could not parse EXPLODE radius/temperature \"" + pfRaw + "\" for PINPUT_RESOURCE " + resName + " (expected EXPLODE,radiusMeters,tempCelsius) — defaulting to PAUSE.",
                            "PINPUT_RESOURCE/Parse");
                        powerfail = PowerfailResult.Pause;
                    }
                }
                else
                {
                    KShared.LogError(
                        "\"" + context + "\": Unknown powefail \"" + pfRaw + "\" for PINPUT_RESOURCE " + resName + " — defaulting to PAUSE.",
                        "PINPUT_RESOURCE/Parse");
                    powerfail = PowerfailResult.Pause;
                }
            }

            result = new PassiveResourceInput
            {
                resourceName = resName,
                amount = amount,
                period = period,
                powerfail = powerfail,
                powerfailExplosionRadius = explosionRadius,
                powerfailExplosionTemperature = explosionTemperature,
                flowMode = flowMode,
                ignorePowerfail = ignorePowerfail
            };
            return true;
        }
    }
}
