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
                            CultureInfo.InvariantCulture, out double variableValue))
                        return variableValue;
                    throw new Exception("Variable \"" + ident + "\" has non-numeric value \""
                        + rawVariableValue + "\".");
                }

                throw new Exception("Unknown identifier \"" + ident + "\".");
            }

            throw new Exception("Unexpected character '" + s[pos] + "' at position " + pos + ".");
        }
    }
}
