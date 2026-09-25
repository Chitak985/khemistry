using System;
using System.Globalization;

namespace Khemistry
{
    public partial class KShared
    {
        /// <summary>
        /// Evaluates a single PARAM_REQUIREMENTS comparison expression against a material
        /// parameter's string value.
        /// <list type="bullet">"Mx" — paramValue &gt; x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"Lx" — paramValue &lt; x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"EMx" — paramValue &gt;= x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">"ELx" — paramValue &lt;= x (numeric, false if paramValue isn't a number)</list>
        /// <list type="bullet">anything else — exact string match against paramValue</list>
        /// </summary>
        public static bool EvaluateParamComparison(string paramValue, string comparison)
        {
            if (comparison == null) return false;
            if (comparison.StartsWith(MaterialParameterCondition.LiteralPrefix, StringComparison.Ordinal))
                return paramValue == comparison.Substring(MaterialParameterCondition.LiteralPrefix.Length);

            bool TryNumeric(string raw, out double parsed) =>
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                && !double.IsNaN(parsed) && !double.IsInfinity(parsed);

            if (comparison.StartsWith("EM", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(2), out double cv)) return false;
                return pv >= cv;
            }
            if (comparison.StartsWith("EL", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(2), out double cv)) return false;
                return pv <= cv;
            }
            if (comparison.StartsWith("M", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(1), out double cv)) return false;
                return pv > cv;
            }
            if (comparison.StartsWith("L", StringComparison.Ordinal))
            {
                if (!TryNumeric(paramValue, out double pv) || !TryNumeric(comparison.Substring(1), out double cv)) return false;
                return pv < cv;
            }

            // Exact value match — works for anything, numeric or not.
            return paramValue == comparison;
        }

    }
}
