using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KhemistryConstructionOverhaul
{
    // Kept independent of Unity so grouping, sorting and sale validation can be tested.
    internal static class KSCResourceListData
    {
        internal sealed class MaterialRow
        {
            public string name, shape, size;
            public int amount;
            public Dictionary<string, string> parameters;
        }

        internal sealed class Group
        {
            public string key, label;
            public long amount;
            public List<MaterialRow> rows;
        }

        internal sealed class Sort
        {
            public string column;
            public bool descending;
            public Sort(string initial) { column = initial; }
            public void Select(string selected)
            {
                descending = column == selected && !descending;
                column = selected;
            }
        }

        public static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        public static string Token(string text) => text == null ? "-1:" : text.Length + ":" + text;
        private static int Rank(char c) => char.IsLetter(c) ? 0 : char.IsDigit(c) ? 1 : 2;
        internal sealed class AlphaComparer : IComparer<string>
        {
            public static readonly AlphaComparer Instance = new AlphaComparer();
            public int Compare(string left, string right)
            {
                left = left ?? ""; right = right ?? "";
                for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
                {
                    char a = char.ToLowerInvariant(left[i]), b = char.ToLowerInvariant(right[i]);
                    int comparison = Rank(a).CompareTo(Rank(b));
                    if (comparison == 0) comparison = a.CompareTo(b);
                    if (comparison != 0) return comparison;
                }
                return left.Length.CompareTo(right.Length);
            }
        }

        public static List<T> Sorted<T>(IEnumerable<T> rows, Sort sort, Func<T, string> value,
            Func<T, string> identity)
        {
            var sorted = sort.descending
                ? rows.OrderByDescending(value, AlphaComparer.Instance)
                : rows.OrderBy(value, AlphaComparer.Instance);
            return sorted.ThenBy(identity, StringComparer.Ordinal).ToList();
        }

        public static List<Group> Groups(IEnumerable<MaterialRow> rows, Func<MaterialRow, string> key,
            Func<MaterialRow, string> label)
            => rows.GroupBy(key, StringComparer.Ordinal).Select(group => new Group
            {
                key = group.Key, label = label(group.First()),
                amount = group.Sum(row => (long)row.amount), rows = group.ToList()
            }).ToList();

        public static string ParameterKey(MaterialRow row)
            => string.Concat(row.parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => Token(p.Key) + Token(p.Value)));

        public static string[] ParameterNames(IEnumerable<MaterialRow> rows)
            => rows.SelectMany(row => row.parameters.Keys).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, AlphaComparer.Instance).ThenBy(name => name, StringComparer.Ordinal).ToArray();

        public static double Density(double tons, string unit)
            => tons * (unit == "g" ? 1000000 : unit == "kg" ? 1000 : 1);

        public static bool TrySale(IDictionary<string, double> ledger, string name, string text,
            bool career, double unitCost, out double remaining, out double proceeds, out string error)
        {
            remaining = proceeds = 0;
            error = null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
                || !Finite(amount) || amount <= 0)
                error = "Enter a finite amount greater than zero (use a decimal point).";
            else if (ledger == null || string.IsNullOrEmpty(name) || !ledger.TryGetValue(name, out double available)
                || !Finite(available) || amount > available)
                error = "The KSC no longer has that amount.";
            else
            {
                remaining = available - amount;
                proceeds = career ? amount * unitCost : 0;
                if (remaining == available)
                    error = "That amount is too small for the stored balance.";
                else if (!Finite(proceeds) || proceeds < 0)
                    error = "This resource has an invalid sale value.";
            }
            return error == null;
        }

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
