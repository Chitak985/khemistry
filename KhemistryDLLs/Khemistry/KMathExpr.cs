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
            // Failsafe if no variables are set
            if(vars == null)
                vars = new Dictionary<string, string>();

            // Replace every variable in the expression with the provided variable
            foreach (string var in vars.Keys)
                expr.Replace(var, vars[var]);

            result = 0.0;
            error = null;
            try
            {
                int pos = 0;
                result = ParseExpr(expr, ref pos);
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

        private static double ParseExpr(string s, ref int pos)
        {
            double val = ParseTerm(s, ref pos);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseTerm(s, ref pos);
                    val = op == '+' ? val + rhs : val - rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseTerm(string s, ref int pos)
        {
            double val = ParseFactor(s, ref pos);
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
                {
                    char op = s[pos]; pos++;
                    double rhs = ParseFactor(s, ref pos);
                    val = op == '*' ? val * rhs : val / rhs;
                }
                else break;
            }
            return val;
        }

        private static double ParseFactor(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '-') { pos++; return -ParseFactor(s, ref pos); }
            if (pos < s.Length && s[pos] == '+') { pos++; return ParseFactor(s, ref pos); }
            return ParsePrimary(s, ref pos);
        }

        private static double ParsePrimary(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new Exception("Unexpected end of expression.");

            if (s[pos] == '(')
            {
                pos++;
                double val = ParseExpr(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ')') throw new Exception("Expected ')'.");
                pos++;
                return val;
            }

            if (char.IsDigit(s[pos]) || s[pos] == '.')
            {
                int start = pos;
                while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
                return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
            }

            if (char.IsLetter(s[pos]) || s[pos] == '_')
            {
                int start = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string ident = s.Substring(start, pos - start);

                if (ident == "PI") return Math.PI;

                if (ident == "Pow")
                {
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != '(') throw new Exception("Expected '(' after Pow.");
                    pos++;
                    double a = ParseExpr(s, ref pos);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ',') throw new Exception("Expected ',' in Pow(...).");
                    pos++;
                    double b = ParseExpr(s, ref pos);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != ')') throw new Exception("Expected ')' to close Pow(...).");
                    pos++;
                    return Math.Pow(a, b);
                }

                throw new Exception("Unknown identifier \"" + ident + "\".");
            }

            throw new Exception("Unexpected character '" + s[pos] + "' at position " + pos + ".");
        }
    }
}
