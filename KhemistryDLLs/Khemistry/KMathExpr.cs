using System;
using System.Collections.Generic;
using System.Globalization;

namespace Khemistry
{
    /// <summary>
    /// A minimal recursive-descent arithmetic expression evaluator supporting +, -, *, /,
    /// parentheses, unary +/-, the constant PI, and the function Pow(a,b).
    /// Used for parsing mathematic expressions in config values.
    /// </summary>
    public static class KMathExpr
    {
        /// <summary>A closed range of finite values used for conservative config validation.</summary>
        public struct ValueRange
        {
            public double Minimum;
            public double Maximum;

            public ValueRange(double minimum, double maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }
        }

        public static bool TryEvaluate(string expr, out double result, out string error, Dictionary<string, string> vars=null)
        {
            result = 0.0;
            error = null;
            if (string.IsNullOrWhiteSpace(expr))
            {
                error = "Expression is empty.";
                return false;
            }

            Dictionary<string, string> expressionVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> variable in vars ?? new Dictionary<string, string>())
                expressionVars[variable.Key] = variable.Value;

            try
            {
                int pos = 0;
                result = ParseExpr(expr, ref pos, expressionVars);
                SkipWhitespace(expr, ref pos);
                if (pos != expr.Length)
                {
                    error = "Unexpected trailing characters at position " + pos + ".";
                    return false;
                }
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    error = "Expression result is not finite.";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static double ParseExpr(string s, ref int pos, Dictionary<string, string> vars)
        {
            double val = ParseTerm(s, ref pos, vars);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseTerm(s, ref pos, vars);
                    val = op == '+' ? val + rhs : val - rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseTerm(string s, ref int pos, Dictionary<string, string> vars)
        {
            double val = ParseFactor(s, ref pos, vars);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseFactor(s, ref pos, vars);
                    val = op == '*' ? val * rhs : val / rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseFactor(string s, ref int pos, Dictionary<string, string> vars)
        {
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '-') { pos++; return -ParseFactor(s, ref pos, vars); }
            if (pos < s.Length && s[pos] == '+') { pos++; return ParseFactor(s, ref pos, vars); }
            return ParsePrimary(s, ref pos, vars);
        }

        private static double ParsePrimary(string s, ref int pos, Dictionary<string, string> vars)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new Exception("Unexpected end of expression.");

            if (s[pos] == '(')
            {
                pos++;
                double val = ParseExpr(s, ref pos, vars);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')') throw new Exception("Expected ')'.");
                pos++;
                return val;
            }

            if (char.IsDigit(s[pos]) || s[pos] == '.')
            {
                int start = pos;
                while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
                if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
                {
                    pos++;
                    if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                    int exponentStart = pos;
                    while (pos < s.Length && char.IsDigit(s[pos])) pos++;
                    if (pos == exponentStart) throw new Exception("Expected digits after exponent marker.");
                }
                return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
            }

            if (char.IsLetter(s[pos]) || s[pos] == '_')
            {
                int start = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string ident = s.Substring(start, pos - start);

                if (string.Equals(ident, "PI", StringComparison.OrdinalIgnoreCase)) return Math.PI;

                if (string.Equals(ident, "Pow", StringComparison.OrdinalIgnoreCase))
                {
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != '(') throw new Exception("Expected '(' after Pow.");
                    pos++;
                    double a = ParseExpr(s, ref pos, vars);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ',') throw new Exception("Expected ',' in Pow(...).");
                    pos++;
                    double b = ParseExpr(s, ref pos, vars);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ')') throw new Exception("Expected ')' to close Pow(...).");
                    pos++;
                    return Math.Pow(a, b);
                }

                if (vars.TryGetValue(ident, out string rawVariableValue))
                {
                    if (double.TryParse(rawVariableValue, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double variableValue)
                        && !double.IsNaN(variableValue) && !double.IsInfinity(variableValue))
                        return variableValue;
                    throw new Exception("Variable \"" + ident + "\" has non-numeric value \""
                        + rawVariableValue + "\".");
                }

                throw new Exception("Unknown identifier \"" + ident + "\".");
            }

            throw new Exception("Unexpected character '" + s[pos] + "' at position " + pos + ".");
        }

        /// <summary>
        /// Conservatively evaluates the complete result range of an expression. This supports
        /// the same grammar as <see cref="TryEvaluate"/>. Pow requires a fixed exponent; a
        /// variable exponent is rejected because its extrema cannot generally be bounded by
        /// endpoint arithmetic.
        /// </summary>
        public static bool TryEvaluateRange(string expr, out ValueRange result,
            out string error, Dictionary<string, ValueRange> vars = null)
        {
            result = new ValueRange(0.0, 0.0);
            error = null;
            if (string.IsNullOrWhiteSpace(expr))
            {
                error = "Expression is empty.";
                return false;
            }

            Dictionary<string, ValueRange> expressionVars =
                new Dictionary<string, ValueRange>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ValueRange> variable in
                     vars ?? new Dictionary<string, ValueRange>())
            {
                if (!IsFinite(variable.Value.Minimum)
                    || !IsFinite(variable.Value.Maximum)
                    || variable.Value.Minimum > variable.Value.Maximum)
                {
                    error = "Variable \"" + variable.Key + "\" has an invalid range.";
                    return false;
                }
                expressionVars[variable.Key] = variable.Value;
            }

            try
            {
                int pos = 0;
                result = ParseRangeExpr(expr, ref pos, expressionVars);
                SkipWhitespace(expr, ref pos);
                if (pos != expr.Length)
                {
                    error = "Unexpected trailing characters at position " + pos + ".";
                    return false;
                }
                if (!IsFinite(result.Minimum) || !IsFinite(result.Maximum)
                    || result.Minimum > result.Maximum)
                {
                    error = "Expression range is not finite.";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static ValueRange ParseRangeExpr(string s, ref int pos,
            Dictionary<string, ValueRange> vars)
        {
            ValueRange value = ParseRangeTerm(s, ref pos, vars);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || (s[pos] != '+' && s[pos] != '-')) return value;
                char operation = s[pos++];
                ValueRange right = ParseRangeTerm(s, ref pos, vars);
                value = operation == '+'
                    ? MakeRange(value.Minimum + right.Minimum,
                        value.Maximum + right.Maximum)
                    : MakeRange(value.Minimum - right.Maximum,
                        value.Maximum - right.Minimum);
            }
        }

        private static ValueRange ParseRangeTerm(string s, ref int pos,
            Dictionary<string, ValueRange> vars)
        {
            ValueRange value = ParseRangeFactor(s, ref pos, vars);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || (s[pos] != '*' && s[pos] != '/')) return value;
                char operation = s[pos++];
                ValueRange right = ParseRangeFactor(s, ref pos, vars);
                value = operation == '*'
                    ? MultiplyRanges(value, right)
                    : DivideRanges(value, right);
            }
        }

        private static ValueRange ParseRangeFactor(string s, ref int pos,
            Dictionary<string, ValueRange> vars)
        {
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '-')
            {
                pos++;
                ValueRange value = ParseRangeFactor(s, ref pos, vars);
                return MakeRange(-value.Maximum, -value.Minimum);
            }
            if (pos < s.Length && s[pos] == '+')
            {
                pos++;
                return ParseRangeFactor(s, ref pos, vars);
            }
            return ParseRangePrimary(s, ref pos, vars);
        }

        private static ValueRange ParseRangePrimary(string s, ref int pos,
            Dictionary<string, ValueRange> vars)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new Exception("Unexpected end of expression.");

            if (s[pos] == '(')
            {
                pos++;
                ValueRange value = ParseRangeExpr(s, ref pos, vars);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')')
                    throw new Exception("Expected ')'.");
                pos++;
                return value;
            }

            if (char.IsDigit(s[pos]) || s[pos] == '.')
            {
                int start = pos;
                while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
                if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
                {
                    pos++;
                    if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                    int exponentStart = pos;
                    while (pos < s.Length && char.IsDigit(s[pos])) pos++;
                    if (pos == exponentStart)
                        throw new Exception("Expected digits after exponent marker.");
                }
                double number = double.Parse(s.Substring(start, pos - start),
                    CultureInfo.InvariantCulture);
                return MakeRange(number, number);
            }

            if (char.IsLetter(s[pos]) || s[pos] == '_')
            {
                int start = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string identifier = s.Substring(start, pos - start);
                if (string.Equals(identifier, "PI", StringComparison.OrdinalIgnoreCase))
                    return MakeRange(Math.PI, Math.PI);

                if (string.Equals(identifier, "Pow", StringComparison.OrdinalIgnoreCase))
                {
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != '(')
                        throw new Exception("Expected '(' after Pow.");
                    pos++;
                    ValueRange baseRange = ParseRangeExpr(s, ref pos, vars);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ',')
                        throw new Exception("Expected ',' in Pow(...).");
                    pos++;
                    ValueRange exponentRange = ParseRangeExpr(s, ref pos, vars);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ')')
                        throw new Exception("Expected ')' to close Pow(...).");
                    pos++;
                    return PowRange(baseRange, exponentRange);
                }

                if (vars.TryGetValue(identifier, out ValueRange variableValue))
                    return variableValue;
                throw new Exception("Unknown identifier \"" + identifier + "\".");
            }

            throw new Exception("Unexpected character '" + s[pos] + "' at position "
                + pos + ".");
        }

        private static ValueRange MultiplyRanges(ValueRange left, ValueRange right)
        {
            double a = left.Minimum * right.Minimum;
            double b = left.Minimum * right.Maximum;
            double c = left.Maximum * right.Minimum;
            double d = left.Maximum * right.Maximum;
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c) || !IsFinite(d))
                throw new Exception("Expression range overflowed.");
            return new ValueRange(Math.Min(Math.Min(a, b), Math.Min(c, d)),
                Math.Max(Math.Max(a, b), Math.Max(c, d)));
        }

        private static ValueRange DivideRanges(ValueRange numerator, ValueRange denominator)
        {
            if (denominator.Minimum <= 0.0 && denominator.Maximum >= 0.0)
                throw new Exception("Expression can divide by zero.");
            ValueRange reciprocal = MakeRange(1.0 / denominator.Maximum,
                1.0 / denominator.Minimum);
            return MultiplyRanges(numerator, reciprocal);
        }

        private static ValueRange PowRange(ValueRange baseRange, ValueRange exponentRange)
        {
            double exponentScale = Math.Max(1.0,
                Math.Max(Math.Abs(exponentRange.Minimum), Math.Abs(exponentRange.Maximum)));
            if (Math.Abs(exponentRange.Maximum - exponentRange.Minimum)
                > exponentScale * 1e-12)
                throw new Exception("Pow with a variable exponent cannot be safely bounded.");

            double exponent = (exponentRange.Minimum + exponentRange.Maximum) * 0.5;
            if (!IsFinite(exponent)) throw new Exception("Pow exponent is not finite.");
            if (exponent == 0.0) return MakeRange(1.0, 1.0);

            double roundedExponent = Math.Round(exponent);
            bool integerExponent = exponent == roundedExponent;
            if (!integerExponent && baseRange.Minimum < 0.0)
                throw new Exception("Pow can receive a negative base with a non-integer exponent.");
            if (exponent < 0.0 && baseRange.Minimum <= 0.0
                && baseRange.Maximum >= 0.0)
                throw new Exception("Pow can divide by zero for a negative exponent.");

            double first = Math.Pow(baseRange.Minimum, exponent);
            double second = Math.Pow(baseRange.Maximum, exponent);
            if (!IsFinite(first) || !IsFinite(second))
                throw new Exception("Pow range is not finite.");

            double minimum = Math.Min(first, second);
            double maximum = Math.Max(first, second);
            if (integerExponent && exponent > 0.0
                && Math.Abs(roundedExponent % 2.0) < 0.5
                && baseRange.Minimum <= 0.0 && baseRange.Maximum >= 0.0)
                minimum = 0.0;
            return MakeRange(minimum, maximum);
        }

        private static ValueRange MakeRange(double first, double second)
        {
            if (!IsFinite(first) || !IsFinite(second))
                throw new Exception("Expression range is not finite.");
            return new ValueRange(Math.Min(first, second), Math.Max(first, second));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
