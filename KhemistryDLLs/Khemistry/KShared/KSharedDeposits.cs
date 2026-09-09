using System.Collections.Generic;

namespace Khemistry
{
    public partial class KShared
    {
        public List<string> SurfaceDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryGDeposit deposit in surfaceDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
        }
        public List<string> UndergroundDepositsAtPoint(float lat, float lon, string body, float depth)
        {
            List<string> tmp = new List<string>();
            foreach (KhemistryUDeposit deposit in undergroundDeposits)
            {
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon) && deposit.IsDepthInsideDeposit(depth))
                {
                    tmp.Add(deposit.Resource);
                }
            }
            return tmp;
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
                if (body == deposit.Planet && deposit.IsInsideDeposit(lat, lon))
                    result.Add(deposit.Resource);
            return result;
        }
    }
}
