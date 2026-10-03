using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Khemistry
{
    /// <summary>INPUT_MATERIAL conditions: numeric expressions or interpolated string equality.</summary>
    internal static class MaterialParameterCondition
    {
        // Runtime-only marker prevents an interpolated equality string beginning with M/L/EM/EL
        // from being reinterpreted as an inequality by the shared storage matcher. Never saved.
        internal const string LiteralPrefix = "\0";

        internal static bool TryResolve(string condition, Func<string, double?> settingValue,
            out string resolved, out string error, bool validateOnly = false)
        {
            resolved = null;
            error = null;
            if (condition == null) { error = "Condition is null."; return false; }
            if (condition.IndexOf("(INMAT:", StringComparison.OrdinalIgnoreCase) >= 0
                || condition.IndexOf("(OUTMAT:", StringComparison.OrdinalIgnoreCase) >= 0)
            { error = "Input parameter conditions do not support INMAT or OUTMAT."; return false; }

            string value = condition;
            int prefixLength = value.StartsWith("EM", StringComparison.Ordinal)
                || value.StartsWith("EL", StringComparison.Ordinal) ? 2
                : value.StartsWith("M", StringComparison.Ordinal)
                    || value.StartsWith("L", StringComparison.Ordinal) ? 1 : 0;
            try
            {
                value = Regex.Replace(value, @"\(SETTING:([^)]*)\)", match =>
                {
                    string variable = match.Groups[1].Value.Trim();
                    double? number = settingValue?.Invoke(variable);
                    if (!number.HasValue || !KShared.IsFinite(number.Value))
                        throw new FormatException("Unknown or invalid SETTING var \"" + variable + "\".");
                    return number.Value.ToString("R", CultureInfo.InvariantCulture);
                }, RegexOptions.IgnoreCase);
                if (value.IndexOf("(SETTING:", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new FormatException("Unclosed SETTING reference.");
            }
            catch (Exception exception) { error = exception.Message; return false; }

            if (prefixLength > 0)
            {
                string expression = value.Substring(prefixLength);
                if (validateOnly)
                    return KMathExpr.TryEvaluateRange(expression, out _, out error);
                if (!KMathExpr.TryEvaluate(expression, out double number, out error)) return false;
                resolved = value.Substring(0, prefixLength) + number.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }

            if (validateOnly)
            {
                // Validate without consuming random numbers or mistaking literal text for math.
                int position = 0;
                while (position < value.Length)
                {
                    int open = value.IndexOf('[', position);
                    int close = value.IndexOf(']', position);
                    if (close >= 0 && (open < 0 || close < open))
                    { error = "Unexpected ']'."; return false; }
                    if (open < 0) return true;
                    if (close < 0) { error = "Expression has no closing ']'."; return false; }
                    if (!KMathExpr.TryEvaluateRange(value.Substring(open + 1, close - open - 1),
                            out _, out error)) return false;
                    position = close + 1;
                }
                return true;
            }
            if (!KMathExpr.TryInterpolate(value, out string literal, out error)) return false;
            resolved = LiteralPrefix + literal;
            return true;
        }
    }
}
