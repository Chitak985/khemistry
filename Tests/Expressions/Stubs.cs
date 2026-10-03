using System;

namespace Khemistry
{
    public partial class KShared
    {
        public static bool ContainsInterpolation(string value) => value?.Contains("[") == true;
        public static int RandomCalls;
        public static double RandomDouble(double a, double b)
        { RandomCalls++; return a + (b - a) * 0.5; }
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static void LogFatalError(string message, string context) => throw new ArgumentException(message);
    }
}
