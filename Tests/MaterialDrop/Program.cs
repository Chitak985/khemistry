using System;
using System.Collections.Generic;
using Khemistry;
namespace Khemistry
{
    public class KhemistryMaterialInstance
    {
        public int amount, updates;
        public void UpdateParams(string context) { updates++; }
    }
}
class Program
{
    static int count;
    static void Check(bool value, string why) { count++; if (!value) throw new Exception(why); }
    static int Main()
    {
        try
        {
            var selected = new KhemistryMaterialInstance { amount = 10 };
            var other = new KhemistryMaterialInstance { amount = 10 };
            var contents = new List<KhemistryMaterialInstance> { selected, other };
            foreach (var input in new[] { "", "0", "-1", "1.5", "1e2", "2147483648", "11" })
                Check(!MaterialDrop.TryReadAmount(input, 10, out _), "Invalid input: " + input);
            Check(MaterialDrop.TryReadAmount("10", 10, out int value) && value == 10, "Exact maximum");
            Check(MaterialDrop.TryReadAmount("2147483647", int.MaxValue, out value) && value == int.MaxValue, "Integer precision");
            Check(!MaterialDrop.TryDrop(contents, selected, 11) && selected.amount == 10, "Overdraw rejected");
            Check(!MaterialDrop.TryDrop(contents, selected, 0) && selected.amount == 10, "Zero rejected");
            Check(MaterialDrop.TryDrop(contents, selected, 3), "Partial deletion");
            Check(selected.amount == 7 && selected.updates == 1 && other.amount == 10, "Only selected stack changed; params updated");
            Check(!MaterialDrop.TryDrop(contents, selected, 10) && selected.amount == 7, "Stale amount rejected");
            Check(MaterialDrop.TryDrop(contents, selected, 7) && contents.Count == 1 && ReferenceEquals(contents[0], other), "Whole stack removed");
            Check(!MaterialDrop.TryDrop(contents, selected, 1) && other.amount == 10, "Stale identity cannot delete similar stack");
            Check(!MaterialDrop.TryDrop(null, selected, 1), "Unavailable storage");
            Console.WriteLine(count + " material-drop checks passed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
