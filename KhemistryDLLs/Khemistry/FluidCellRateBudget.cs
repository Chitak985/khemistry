using System;

namespace Khemistry
{
    // Four independent aggregate budgets; unused time never accumulates into a burst.
    internal sealed class FluidCellRateBudget
    {
        private double tick = double.NaN;
        private readonly double[] used = new double[4];
        private void Refresh(double now)
        {
            if (tick == now) return;
            tick = now;
            Array.Clear(used, 0, used.Length);
        }
        internal double Available(int channel, double rate, double now, double dt)
        {
            Refresh(now);
            if (rate < 0) return double.PositiveInfinity;
            if (double.IsNaN(rate) || double.IsInfinity(rate)
                || double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0) return 0;
            return Math.Max(0, rate * dt - used[channel]);
        }
        internal void Record(int channel, double amount, double now)
        {
            Refresh(now);
            used[channel] = Math.Max(0, used[channel] + amount);
        }
    }
}
