namespace Khemistry
{
    public partial class KhemistryISRU
    {
        [KSPAction("Start Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StartConverterAction(KSPActionParam param) => StartConverter();

        [KSPAction("Stop Converter")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Called by KSP with parameter")]
        public void StopConverterAction(KSPActionParam param) => StopConverter();
    }
}
