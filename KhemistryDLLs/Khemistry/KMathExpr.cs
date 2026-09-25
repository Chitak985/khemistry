using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Khemistry
{
    /// <summary>
    /// A minimal recursive-descent arithmetic expression evaluator supporting +, -, *, /,
    /// parentheses, unary +/-, the constant PI, and the function Pow(a,b).
    /// Used for parsing mathematical expressions in config values. Config consumers expose
    /// expressions through string interpolation: literal text [expression] literal text.
    /// The expressions it supports are in its three constant dictionaries at the top:
    /// constants, functions1Arg, and functions2Arg.
    /// </summary>
    public static class KMathExpr
    {
        /// <summary>The dictionary of supported constants.</summary>
        static readonly Dictionary<string, double> constants = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "PI", Math.PI },
            { "E", Math.E }
        };
        /// <summary>The dictionary of supported one-argument functions.</summary>
        static readonly Dictionary<string, UnaryFunction> functions1Arg = new Dictionary<string, UnaryFunction>(StringComparer.OrdinalIgnoreCase)
        {
            { "Sqrt", Monotone(Math.Sqrt) },
            { "Log", Monotone(Math.Log) },
            { "Log10", Monotone(Math.Log10) }
        };
        /// <summary>The dictionary of supported two-argument functions.</summary>
        static readonly Dictionary<string, BinaryFunction> functions2Arg = new Dictionary<string, BinaryFunction>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pow", new BinaryFunction(Math.Pow, PowRange) },
            { "Min", CornerBounded(Math.Min) },
            { "Max", CornerBounded(Math.Max) },
            { "randf", new BinaryFunction(KShared.RandomDouble,
                (a, b) => MakeRange(Math.Min(a.Minimum, b.Minimum),
                    Math.Max(a.Maximum, b.Maximum))) }
        };

        // Register scalar and interval behavior together: an arbitrary delegate cannot be
        // bounded safely by sampling. These shared policies avoid a range method per function.
        private sealed class UnaryFunction
        {
            internal readonly Func<double, double> Evaluate;
            internal readonly Func<ValueRange, ValueRange> Range;
            internal UnaryFunction(Func<double, double> evaluate, Func<ValueRange, ValueRange> range)
            { Evaluate = evaluate; Range = range; }
        }

        private sealed class BinaryFunction
        {
            internal readonly Func<double, double, double> Evaluate;
            internal readonly Func<ValueRange, ValueRange, ValueRange> Range;
            internal BinaryFunction(Func<double, double, double> evaluate,
                Func<ValueRange, ValueRange, ValueRange> range)
            { Evaluate = evaluate; Range = range; }
        }

        // Only for functions monotone throughout their domain, with no internal domain holes.
        private static UnaryFunction Monotone(Func<double, double> function)
            => new UnaryFunction(function, value =>
                MakeRange(function(value.Minimum), function(value.Maximum)));

        // Only for functions whose extrema on any valid rectangle occur at its corners.
        private static BinaryFunction CornerBounded(Func<double, double, double> function)
            => new BinaryFunction(function, (a, b) => EvaluateCorners(function, a, b));

        private static ValueRange EvaluateCorners(Func<double, double, double> function,
            ValueRange a, ValueRange b)
        {
            ValueRange first = MakeRange(function(a.Minimum, b.Minimum), function(a.Minimum, b.Maximum));
            ValueRange second = MakeRange(function(a.Maximum, b.Minimum), function(a.Maximum, b.Maximum));
            return MakeRange(Math.Min(first.Minimum, second.Minimum), Math.Max(first.Maximum, second.Maximum));
        }
        
        /// <summary>True when a value contains at least one bracketed <see cref="KMathExpr"/>.</summary>
        public static bool ContainsInterpolation(string value)
            => !string.IsNullOrEmpty(value) && value.IndexOf('[') >= 0;

        /// <summary>
        /// Evaluates every <see cref="KMathExpr"/> segment and inserts its invariant numeric result into
        /// the surrounding text. Text outside brackets is preserved verbatim.
        /// </summary>
        public static bool TryInterpolate(string template, out string result,
            out string error, Dictionary<string, string> vars = null,
            Func<double, double, double> randomFunction = null)
        {
            result = template;
            error = null;
            if (template == null) return true;

            StringBuilder builder = new StringBuilder(template.Length);
            int position = 0;
            while (position < template.Length)
            {
                int opening = template.IndexOf('[', position);
                int unexpectedClosing = template.IndexOf(']', position);
                if (unexpectedClosing >= 0 && (opening < 0 || unexpectedClosing < opening))
                {
                    error = "Unexpected ']' at position " + unexpectedClosing + ".";
                    return false;
                }
                if (opening < 0)
                {
                    builder.Append(template, position, template.Length - position);
                    break;
                }

                builder.Append(template, position, opening - position);
                int closing = template.IndexOf(']', opening + 1);
                if (closing < 0)
                {
                    error = "Expression beginning at position " + opening + " has no closing ']'.";
                    return false;
                }

                string expression = template.Substring(opening + 1,
                    closing - opening - 1);
                if (!TryEvaluate(expression, out double value, out string expressionError,
                        vars, randomFunction))
                {
                    error = "Expression [" + expression + "] is invalid: " + expressionError;
                    return false;
                }
                builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
                position = closing + 1;
            }

            result = builder.ToString();
            return true;
        }

        public static bool TryInterpolateNumber(string template, out double result,
            out string error, Dictionary<string, string> vars = null,
            Func<double, double, double> randomFunction = null)
        {
            result = 0.0;
            if (!TryInterpolate(template, out string resolved, out error, vars,
                    randomFunction))
                return false;
            if (double.TryParse(resolved, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result)
                && !double.IsNaN(result) && !double.IsInfinity(result))
                return true;
            error = "Interpolated value \"" + resolved + "\" is not a finite number.";
            return false;
        }

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

        public static string ValueRangeToString(string name, ValueRange range)
            => $"{name} is [{range.Minimum}, {range.Maximum}]";

        public static bool TryEvaluate(string expr, out double result, out string error,
            Dictionary<string, string> vars=null,
            Func<double, double, double> randomFunction = null)
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
                result = ParseExpr(expr, ref pos, expressionVars, randomFunction);
                SkipWhitespace(expr, ref pos);
                if (pos != expr.Length)
                {
                    error = "Unexpected trailing characters at position " + pos + ".";
                    return false;
                }
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    error = $"Expression result {result} is not finite.";
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

        private static double ParseExpr(string s, ref int pos, Dictionary<string, string> vars,
            Func<double, double, double> randomFunction)
        {
            double val = ParseTerm(s, ref pos, vars, randomFunction);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseTerm(s, ref pos, vars, randomFunction);
                    val = op == '+' ? val + rhs : val - rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseTerm(string s, ref int pos, Dictionary<string, string> vars,
            Func<double, double, double> randomFunction)
        {
            double val = ParseFactor(s, ref pos, vars, randomFunction);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseFactor(s, ref pos, vars, randomFunction);
                    val = op == '*' ? val * rhs : val / rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseFactor(string s, ref int pos, Dictionary<string, string> vars,
            Func<double, double, double> randomFunction)
        {
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '-') { pos++; return -ParseFactor(s, ref pos, vars, randomFunction); }
            if (pos < s.Length && s[pos] == '+') { pos++; return ParseFactor(s, ref pos, vars, randomFunction); }
            return ParsePrimary(s, ref pos, vars, randomFunction);
        }

        private static bool Parse1ArgFunction(string ident, string s, ref int pos, Dictionary<string, string> vars, string funcName, out double result,
            Func<double, double, double> randomFunction)
        {
            if (string.Equals(ident, funcName, StringComparison.OrdinalIgnoreCase))
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '(')
                    KShared.LogFatalError($"Expected ( after {funcName} function at position {pos}! String: {s}", "KMathExpr/Parse1ArgFunction");
                pos++;
                double a = ParseExpr(s, ref pos, vars, randomFunction);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')')
                    KShared.LogFatalError($"Expected ) closing the {funcName} function at position {pos}! String: {s}", "KMathExpr/Parse1ArgFunction");
                pos++;
                result = functions1Arg[funcName].Evaluate(a);
                return true;
            }
            result = 0;
            return false;
        }

        private static bool Parse2ArgFunction(string ident, string s, ref int pos, Dictionary<string, string> vars, string funcName, out double result,
            Func<double, double, double> randomFunction)
        {
            if (string.Equals(ident, funcName, StringComparison.OrdinalIgnoreCase))
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '(')
                    KShared.LogFatalError($"Expected ( after {funcName} function at position {pos}! String: {s}", "KMathExpr/Parse2ArgFunction");
                pos++;
                double a = ParseExpr(s, ref pos, vars, randomFunction);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ',')
                    KShared.LogFatalError($"Expected , to separate arguments in {funcName} function at position {pos}! String: {s}", "KMathExpr/Parse2ArgFunction");
                pos++;
                double b = ParseExpr(s, ref pos, vars, randomFunction);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')')
                    KShared.LogFatalError($"Expected ) closing the {funcName} function at position {pos}! String: {s}", "KMathExpr/Parse2ArgFunction");
                pos++;
                result = string.Equals(funcName, "randf", StringComparison.OrdinalIgnoreCase)
                    && randomFunction != null
                    ? randomFunction(a, b) : functions2Arg[funcName].Evaluate(a, b);
                return true;
            }
            result = 0;
            return false;
        }

        private static double ParsePrimary(string s, ref int pos, Dictionary<string, string> vars,
            Func<double, double, double> randomFunction)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length)
                KShared.LogFatalError($"Unexpected end of expression at position {pos}! String: {s}", "KMathExpr/ParsePrimary");

            if (s[pos] == '(')
            {
                pos++;
                double val = ParseExpr(s, ref pos, vars, randomFunction);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')')
                    KShared.LogFatalError($"Expected closing ) paranthesis at position {pos}! String: {s}", "KMathExpr/ParsePrimary");
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
                    if (pos == exponentStart)
                        KShared.LogFatalError($"Expected digits after exponent marker at position {pos}! String: {s}", "KMathExpr/ParsePrimary");
                }
                return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
            }

            if (char.IsLetter(s[pos]) || s[pos] == '_')
            {
                int start = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string ident = s.Substring(start, pos - start);

                // Parse constants
                foreach (string constant in constants.Keys)
                    if (string.Equals(ident, constant, StringComparison.OrdinalIgnoreCase))
                        return constants[constant];

                // Parse 1 argument functions
                foreach (string function in functions1Arg.Keys)
                    if (Parse1ArgFunction(ident, s, ref pos, vars, function, out double result, randomFunction))
                        return result;

                // Parse 2 argument functions
                foreach (string function in functions2Arg.Keys)
                    if (Parse2ArgFunction(ident, s, ref pos, vars, function, out double result, randomFunction))
                        return result;

                // Parse variables
                if (vars.TryGetValue(ident, out string rawVariableValue))
                {
                    if (double.TryParse(rawVariableValue, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double variableValue)
                        && !double.IsNaN(variableValue) && !double.IsInfinity(variableValue))
                        return variableValue;
                    KShared.LogFatalError("Variable " + ident + " has invalid value: " + rawVariableValue, "KMathExpr/ParsePrimary"); return 0;
                }

                KShared.LogFatalError("Unknown identifier \"" + ident + "\"! String: " + s, "KMathExpr/ParsePrimary"); return 0;
            }

            KShared.LogFatalError("Unexpected character '" + s[pos] + "'! String: " + s, "KMathExpr/ParsePrimary"); return 0;
        }

        /// <summary>
        /// Conservatively evaluates the complete result range of an expression. This supports
        /// the same grammar and functions as <see cref="TryEvaluate"/>. Invalid domains and
        /// non-finite bounds are rejected. Negative Pow bases require a fixed integer exponent.
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
                if (!KShared.IsFinite(variable.Value.Minimum)
                    || !KShared.IsFinite(variable.Value.Maximum)
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
                if (!KShared.IsFinite(result.Minimum) || !KShared.IsFinite(result.Maximum)
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
            if (pos >= s.Length)
            {
                KShared.LogFatalError($"Unexpected end of expression at position {pos}! String: {s}", "KMathExpr/ParseRangePrimary");
                throw new OperationCanceledException();
            }

            if (s[pos] == '(')
            {
                pos++;
                ValueRange value = ParseRangeExpr(s, ref pos, vars);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')')
                {
                    KShared.LogFatalError($"Expected a closing ')' at position {pos}! String: {s}", "KMathExpr/ParseRangePrimary");
                    throw new OperationCanceledException();
                }
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
                    {
                        KShared.LogFatalError($"Expected digits after exponent marker at position {pos}! String: {s}", "KMathExpr/ParseRangePrimary");
                        throw new OperationCanceledException();
                    }
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

                // Parse constants
                foreach (string constant in constants.Keys)
                    if (string.Equals(identifier, constant, StringComparison.OrdinalIgnoreCase))
                        return MakeRange(constants[constant], constants[constant]);

                if (functions1Arg.TryGetValue(identifier, out UnaryFunction unary))
                {
                    ExpectRangeToken(s, ref pos, '(', identifier);
                    ValueRange argument = ParseRangeExpr(s, ref pos, vars);
                    ExpectRangeToken(s, ref pos, ')', identifier);
                    return unary.Range(argument);
                }
                if (functions2Arg.TryGetValue(identifier, out BinaryFunction binary))
                {
                    ExpectRangeToken(s, ref pos, '(', identifier);
                    ValueRange first = ParseRangeExpr(s, ref pos, vars);
                    ExpectRangeToken(s, ref pos, ',', identifier);
                    ValueRange second = ParseRangeExpr(s, ref pos, vars);
                    ExpectRangeToken(s, ref pos, ')', identifier);
                    return binary.Range(first, second);
                }

                if (vars.TryGetValue(identifier, out ValueRange variableValue))
                    return variableValue;
                KShared.LogFatalError("Unknown identifier \"" + identifier
                    + "\" at position " + pos + "! String: " + s, "KMathExpr/ParseRangePrimary");
                throw new OperationCanceledException();
            }

            KShared.LogFatalError("Unexpected character '" + s[pos]
                + "' at position " + pos + "! String: " + s, "KMathExpr/ParseRangePrimary");
            throw new OperationCanceledException();
        }

        private static ValueRange MultiplyRanges(ValueRange left, ValueRange right)
        {
            double a = left.Minimum * right.Minimum;
            double b = left.Minimum * right.Maximum;
            double c = left.Maximum * right.Minimum;
            double d = left.Maximum * right.Maximum;
            if (!KShared.IsFinite(a) || !KShared.IsFinite(b) || !KShared.IsFinite(c) || !KShared.IsFinite(d))
                KShared.LogFatalError($"Expression range overflow! " +
                                      ValueRangeToString("left", left) + ", " +
                                      ValueRangeToString("right", right),
                                      "KMathExpr/MultiplyRanges");
            return new ValueRange(Math.Min(Math.Min(a, b), Math.Min(c, d)),
                Math.Max(Math.Max(a, b), Math.Max(c, d)));
        }

        private static ValueRange DivideRanges(ValueRange numerator, ValueRange denominator)
        {
            if (denominator.Minimum <= 0.0 && denominator.Maximum >= 0.0)
                KShared.LogFatalError("Expression can divide by zero! " +
                                      ValueRangeToString("numerator", numerator) + ", " +
                                      ValueRangeToString("denominator", denominator),
                                      "KMathExpr/DivideRanges");
            ValueRange reciprocal = MakeRange(1.0 / denominator.Maximum,
                1.0 / denominator.Minimum);
            return MultiplyRanges(numerator, reciprocal);
        }

        private static void ExpectRangeToken(string expression, ref int position,
            char token, string function)
        {
            SkipWhitespace(expression, ref position);
            if (position >= expression.Length || expression[position] != token)
                throw new FormatException("Expected '" + token + "' in " + function
                    + " at position " + position + ".");
            position++;
        }

        private static ValueRange PowRange(ValueRange baseRange, ValueRange exponentRange)
        {
            bool fixedExponent = exponentRange.Minimum == exponentRange.Maximum;
            double exponent = exponentRange.Minimum;
            if (baseRange.Minimum < 0.0
                && (!fixedExponent || exponent != Math.Truncate(exponent)))
                throw new ArgumentException("Pow with negative bases requires a fixed integer exponent.");
            if (baseRange.Minimum <= 0.0 && baseRange.Maximum >= 0.0
                && exponentRange.Minimum < 0.0)
                throw new ArgumentException("Pow can divide by zero for a negative exponent.");

            // For nonnegative bases, extrema are at corners (including x=0 and y=0).
            // For fixed integer powers of signed bases, x=0 is the only extra extremum.
            ValueRange result = EvaluateCorners(Math.Pow, baseRange, exponentRange);
            if (fixedExponent && exponent > 0.0 && exponent % 2.0 == 0.0
                && baseRange.Minimum < 0.0 && baseRange.Maximum > 0.0)
                result.Minimum = 0.0;
            return result;
        }

        private static ValueRange MakeRange(double first, double second)
        {
            if (!KShared.IsFinite(first) || !KShared.IsFinite(second))
                KShared.LogFatalError("Expression range is not finite! " +
                                      "first is " + first + ", " +
                                      "second is " + second,
                                      "KMathExpr/MakeRange");
            return new ValueRange(Math.Min(first, second), Math.Max(first, second));
        }
    }
}
