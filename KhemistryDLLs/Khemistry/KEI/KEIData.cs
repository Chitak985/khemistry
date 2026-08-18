using System.Collections.Generic;

namespace Khemistry
{
    /// <summary>
    /// Information about a resource shown in the Resource Library.
    /// </summary>
    public class KhemistryResourceInfo
    {
        public string name;
        public string displayName;
        public string abbreviation;
        public float unitCost;
        public float density;
        public float volume;
        public string flowMode;
        public string transfer;
        public bool isTweakable;
        public bool isVisible;
        public string description;
    }

    /// <summary>
    /// Information about an input/output shown in the Recipe Library.
    /// </summary>
    public class KhemistryRecipeIO
    {
        public string resourceName;
        public double ratio;
    }

    /// <summary>
    /// Information about a recipe shown in the Recipe Library.
    /// </summary>
    public class KhemistryRecipeInfo
    {
        public string converterName;
        public bool generatesHeat;
        public string partTitle;
        public List<KhemistryRecipeIO> inputs = new List<KhemistryRecipeIO>();
        public List<KhemistryRecipeIO> outputs = new List<KhemistryRecipeIO>();
    }
}
