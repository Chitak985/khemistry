using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Khemistry;

internal static class Program
{
    private static int assertions;
    private static double radius = 2;
    private static double? Setting(string name) => name == "radius" ? radius : (double?)null;

    private static int Main()
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Conditions();
            Ranges();
            assertions += EvaRecipeTests.Run();
            Console.WriteLine("Passed " + assertions + " expression assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Assert(bool success, string message)
    { assertions++; if (!success) throw new InvalidOperationException(message); }

    private static void Match(string condition, string actual, bool expected)
    {
        Assert(MaterialParameterCondition.TryResolve(condition, Setting, out string resolved,
            out string error), condition + ": " + error);
        Assert(KShared.EvaluateParamComparison(actual, resolved) == expected,
            condition + " compared against " + actual);
        Assert(MaterialParameterCondition.TryResolve(condition, Setting, out _, out error,
            validateOnly: true), "Validation: " + condition + ": " + error);
    }

    private static void Conditions()
    {
        Match("EM2*2+1", "5", true);
        Match("EM2*2+1", "4.99", false);
        Match("EL Pow(2,3)/2", "4", true);
        Match("EL Pow(2,3)/2", "4.01", false);
        Match("M (2+3)*2", "10", false);
        Match("M (2+3)*2", "10.1", true);
        Match("L -2*2", "-4", false);
        Match("L -2*2", "-5", true);
        Match("EM1e-3", "0.001", true);
        Match("EM Sqrt(9)+Log(E)+Log10(100)+Min(3,4)+Max(1,2)", "11", true);
        Match("M1", "text", false);
        Match("EM(SETTING:radius)*2", "4", true);
        radius = 3;
        Match("EM(SETTING:radius)*2", "4", false);
        Match("EM(SETTING:radius)*2", "6", true);
        Match("EL(SETTING:radius)/2", "1.5", true);
        Match("[(SETTING:radius)*2]", "6", true);
        Match("[(SETTING:radius)*2]", "6.0", false); // Equality is string, not numeric.
        Match("log-[2+2]", "log-4", true);
        Match("piece[1+2]-[3*4]", "piece3-12", true);
        Match("wood", "Wood", false);
        Match(" wood [2+2] ", " wood 4 ", true);
        Match("[2+2]", "4", true);
        Match("2+2", "2+2", true);
        Match("2+2", "4", false);
        Match("[(SETTING:radius)]", "3", true);
        Match("(SETTING:radius)", "3", true);
        Match("", "", true);
        radius = -2;
        Match("EM2*(SETTING:radius)", "-4", true);
        Match("EM1-(SETTING:radius)", "3", true);
        radius = 3;
        foreach (string invalid in new[] { "EM", "M[2*2]", "Mfoo+1", "Mamount", "Mradius",
            "M(INMAT:source:radius)", "[(OUTMAT:result:radius)]", "(INMAT:source:shape)",
            "M(SETTING:missing)", "[(SETTING:missing)]", "M(SETTING:Radius)", "(SETTING:radius",
            "[amount]", "[radius]", "[1/0]", "M1/0", "MLog(0)", "MSqrt(-1)",
            "piece[1+2", "piece1]", "[[1]]", "[]" })
        {
            Assert(!MaterialParameterCondition.TryResolve(invalid, Setting, out _, out string error)
                && !string.IsNullOrEmpty(error), "Runtime should reject " + invalid);
            Assert(!MaterialParameterCondition.TryResolve(invalid, Setting, out _, out error, true)
                && !string.IsNullOrEmpty(error), "Validation should reject " + invalid);
        }
        int calls = KShared.RandomCalls;
        Assert(MaterialParameterCondition.TryResolve("EMrandf(1,2)", Setting, out _, out _, true),
            "Validate random numeric comparison");
        Assert(MaterialParameterCondition.TryResolve("piece[randf(1,2)]", Setting, out _, out _, true),
            "Validate random equality");
        Assert(calls == KShared.RandomCalls, "Condition validation must not draw random numbers.");
        Match("EMrandf(1,3)", "2", true);
        Match("piece[randf(1,3)]", "piece2", true);
        Assert(KShared.EvaluateParamComparison("M4", MaterialParameterCondition.LiteralPrefix + "M4"),
            "Resolved equality is never reinterpreted as an operator");
        Assert(KShared.EvaluateParamComparison("4", "EM3"), "Legacy storage comparison preserved");
        Assert(!KShared.EvaluateParamComparison("4", "EM2+1"), "Non-ISRU conditions unchanged");
    }

    private static KMathExpr.ValueRange Range(string expression, double amin, double amax,
        double bmin, double bmax)
    {
        var vars = new Dictionary<string, KMathExpr.ValueRange>
        {
            ["a"] = new KMathExpr.ValueRange(amin, amax),
            ["b"] = new KMathExpr.ValueRange(bmin, bmax)
        };
        Assert(KMathExpr.TryEvaluateRange(expression, out var range, out string error, vars),
            expression + ": " + error);
        return range;
    }

    private static void Bounds(string expression, double amin, double amax,
        double bmin, double bmax, double min, double max)
    {
        var range = Range(expression, amin, amax, bmin, bmax);
        Assert(Math.Abs(range.Minimum - min) < 1e-10 && Math.Abs(range.Maximum - max) < 1e-10,
            expression + " unexpected bounds: " + range.Minimum + ", " + range.Maximum);
    }

    private static void Reject(string expression, double amin = 0, double amax = 1,
        double bmin = 0, double bmax = 1)
    {
        var vars = new Dictionary<string, KMathExpr.ValueRange>
        {
            ["a"] = new KMathExpr.ValueRange(amin, amax),
            ["b"] = new KMathExpr.ValueRange(bmin, bmax)
        };
        Assert(!KMathExpr.TryEvaluateRange(expression, out _, out string error, vars)
            && !string.IsNullOrEmpty(error), "Expected rejection: " + expression);
    }

    private static void Ranges()
    {
        Bounds("Sqrt(a)", 0, 9, 0, 0, 0, 3);
        Bounds("Log(a)", 1, Math.E, 0, 0, 0, 1);
        Bounds("Log10(a)", 0.01, 100, 0, 0, -2, 2);
        Bounds("Min(a,b)", -3, 5, 2, 9, -3, 5);
        Bounds("Max(a,b)", -3, 5, 2, 9, 2, 9);
        Bounds("Pow(a,2)", -3, 2, 0, 0, 0, 9);
        Bounds("Pow(a,3)", -3, 2, 0, 0, -27, 8);
        Bounds("Pow(a,-2)", -3, -1, 0, 0, 1.0/9, 1);
        Bounds("Pow(a,b)", 0.5, 2, -2, 3, 0.125, 8);
        Bounds("Pow(a,b)", 0, 2, 0, 3, 0, 8);
        Bounds("Pow(a,b)", 0, 0, 0, 3, 0, 1);
        Bounds("Pow(a,b)", -3, 2, 0, 0, 1, 1);
        Bounds("randf(a,b)", -3, 5, 2, 9, -3, 9);
        Bounds("randf(a,b)", 2, 9, -3, 5, -3, 9);
        Bounds("SQRT(Max(Pow(a,2),Min(b,4)))", -3, 2, 1, 5, 1, 3);
        Bounds("Log10(Pow(10,a))", -2, 2, 0, 0, -2, 2);
        Reject("Sqrt(a)", -1, 1);
        Reject("Log(a)", 0, 1);
        Reject("Log10(a)", -2, -1);
        Reject("Pow(a,b)", -2, 2, 1, 3);
        Reject("Pow(a,b)", -2, -1, 2, 2.0000000000001);
        Reject("Pow(a,b)", -2, 2, 0.5, 0.5);
        Reject("Pow(a,b)", -2, 2, -2, -2);
        Reject("Pow(a,b)", 0, 2, -1, 2);
        Reject("Pow(1e308,2)");
        Reject("Pow(a,b)", 1, 2, double.NaN, 2);
        Reject("Sqrt(a)", 2, 1);
        Reject("Log(1/a)", -1, 1);
        foreach (string invalid in new[] { "Sqrt()", "Sqrt(1,2)", "Min(1)", "Max(1,2,3)",
            "Log 2", "Min(1,2", "Sqrt(1))", "Unknown(1)", "Pow(2;3)", "randf(1,2) trailing" })
            Reject(invalid);

        // Every registered function must parse in both evaluators and bound sampled results.
        foreach (string dictionaryName in new[] { "functions1Arg", "functions2Arg" })
        {
            var dictionary = (System.Collections.IDictionary)typeof(KMathExpr)
                .GetField(dictionaryName, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (string name in dictionary.Keys)
            {
                string expression = name + (dictionaryName == "functions1Arg" ? "(a)" : "(a,b)");
                var range = Range(expression, 0.25, 4, 0.5, 3);
                for (int i = 0; i <= 10; i++)
                for (int j = 0; j <= 10; j++)
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["a"] = (0.25 + i * 0.375).ToString("R", CultureInfo.InvariantCulture),
                        ["b"] = (0.5 + j * 0.25).ToString("R", CultureInfo.InvariantCulture)
                    };
                    Assert(KMathExpr.TryEvaluate(expression, out double value, out string error, vars),
                        expression + ": " + error);
                    Assert(value >= range.Minimum - 1e-12 && value <= range.Maximum + 1e-12,
                        expression + " sample fell outside bounds");
                }
            }
        }
        int calls = KShared.RandomCalls;
        Bounds("randf(Sqrt(1),Pow(2,3))", 0, 0, 0, 0, 1, 8);
        Assert(calls == KShared.RandomCalls, "Range validation must not draw random numbers.");
        Assert(KMathExpr.TryEvaluate("randf(1,3)", out double random, out _,
            randomFunction: (a, b) => b) && random == 3, "Scalar random override preserved");
        Assert(KMathExpr.TryInterpolate("piece[Sqrt(9)]-[Max(1,2)]", out string label, out _)
            && label == "piece3-2", "Interpolation still uses all scalar functions");
    }
}
