using System;
using System.Collections.Generic;
using System.Globalization;

namespace Khemistry
{
    internal static class MaterialDrop
    {
        internal static bool TryReadAmount(string text, int maximum, out int amount)
            => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out amount)
                && amount > 0 && amount <= maximum;

        internal static bool TryDrop(IList<KhemistryMaterialInstance> contents,
            KhemistryMaterialInstance selected, int amount)
        {
            if (contents == null || selected == null || amount <= 0 || amount > selected.amount)
                return false;
            for (int i = 0; i < contents.Count; i++)
            {
                if (!ReferenceEquals(contents[i], selected)) continue;
                if (amount == selected.amount) contents.RemoveAt(i);
                else
                {
                    selected.amount -= amount;
                    selected.UpdateParams("MaterialDrop/TryDrop");
                }
                return true;
            }
            return false;
        }
    }
}
