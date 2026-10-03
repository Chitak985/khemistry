using System.Collections.Generic;

namespace Khemistry
{
    public partial class KShared
    {
        public List<string> SurfaceDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> result = new List<string>();
            foreach (KhemistryGDeposit deposit in surfaceDeposits)
                if (deposit.IsInsideDeposit(lat, lon, body, depth))
                    result.Add(deposit.Resource);
            return result;
        }
        public List<string> UndergroundDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> result = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
                if (deposit.IsInsideDeposit(lat, lon, body, depth))
                    result.Add(deposit.Resource);
            return result;
        }

        /// <summary>
        /// Returns underground resources whose horizontal footprint is below a point. This is
        /// used by surface-mounted extractors, which have no meaningful drill-depth value to
        /// pass to <see cref="UndergroundDepositsAtPoint"/>.
        /// </summary>
        public List<string> UndergroundDepositsBelowPoint(float lat, float lon, string body)
        {
            List<string> result = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
                if (deposit.IsInsideDeposit(lat, lon, body))
                    result.Add(deposit.Resource);
            return result;
        }
    }
}
