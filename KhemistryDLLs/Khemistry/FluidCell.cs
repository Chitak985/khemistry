using System.Collections.Generic;

namespace Khemistry
{
    /// <summary>
    /// A part that can hold some resources and be carried by a kerbal to transfer resources between vessels.
    /// </summary>
    public class KhemistryFluidCell : PartModule
    {
        [KSPField(isPersistant = false)]
        public float ResourceMaxAmount = 100.0f;

        [KSPField(isPersistant = false)]
        public float TransferDistance = 10.0f;

        [KSPField(isPersistant = true)]
        public float ResourceAmount = 0.0f;
        [KSPField(isPersistant = true)]
        public string ResourceName = "";

        public HashSet<string> AllowedResources = new HashSet<string>();

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Contents")]
        public string ContentsDisplay = "Empty";

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            AllowedResources.Clear();

            if (node.HasNode("ALLOWED_RESOURCES"))
            {
                foreach (string name in node.GetNode("ALLOWED_RESOURCES").GetValues("name"))
                    AllowedResources.Add(name.Trim());
                KShared.Log(
                    "Loaded " + AllowedResources.Count + " allowed resources.",
                    "KhemistryFluidCell/OnLoad");
            }
            else
                KShared.LogError(
                    "Part \"" + part.name + "\" has KhemistryFluidCell but no ALLOWED_RESOURCES node. This part is now capable of storing anything.",
                    "KhemistryFluidCell/OnLoad");
        }

        public override void OnUpdate()
            => ContentsDisplay = string.IsNullOrEmpty(ResourceName)
                ? "Empty"
                : string.Format("{0}: {1:F2} / {2:F2} kg", ResourceName, ResourceAmount, ResourceMaxAmount);
    }
}
