namespace Khemistry
{
    public partial class KShared
    {
        /// <summary>
        /// Get a random integer between two numbers, inclusive.
        /// If the KShared instance is null, returns the mean value of a and b instead.
        /// </summary>
        /// <param name="a">The lowest value.</param>
        /// <param name="b">The highest value.</param>
        /// <returns>The resulting inclusive random value between <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static int RandomIntInclusive(int a, int b)
            => (a == b) ?
                a :
                (int)(a + (long)(rand.NextDouble() * ((long)b - a + 1L)));

        /// <summary>
        /// Get a random double between two numbers.
        /// If the KShared instance is null, returns the mean value of a and b instead.
        /// </summary>
        /// <param name="a">The lowest value.</param>
        /// <param name="b">The highest value.</param>
        /// <returns>The resulting random value between <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static double RandomDouble(double a, double b)
            => (a == b) ?
                a :
                a + rand.NextDouble() * (b - a);
    }
}
