using System.Numerics;

namespace Reliquary.Decks.Analysis;

/// <summary>
/// The hypergeometric distribution: draws without replacement from a population with a number of successes.
/// </summary>
/// <remarks>
/// The calculation uses integers with no size limit, so the result is the exact fraction until the last division.
/// </remarks>
public static class Hypergeometric
{
    /// <summary>
    /// Gets the probability to draw exactly a number of successes.
    /// </summary>
    /// <param name="population">The number of cards, for example 99.</param>
    /// <param name="successes">The number of successes in the population, for example 38 lands.</param>
    /// <param name="draws">The number of cards to draw.</param>
    /// <param name="count">The number of successes to draw.</param>
    /// <returns>The probability, from 0 to 1.</returns>
    public static double Exactly(int population, int successes, int draws, int count)
    {
        Validate(population, successes, draws);
        return Divide(Ways(population, successes, draws, count), Binomial(population, draws));
    }

    /// <summary>
    /// Gets the probability to draw at least a number of successes.
    /// </summary>
    /// <param name="population">The number of cards, for example 99.</param>
    /// <param name="successes">The number of successes in the population, for example 38 lands.</param>
    /// <param name="draws">The number of cards to draw.</param>
    /// <param name="minimum">The lowest number of successes to draw.</param>
    /// <returns>The probability, from 0 to 1.</returns>
    public static double AtLeast(int population, int successes, int draws, int minimum)
    {
        Validate(population, successes, draws);
        if (minimum <= 0)
        {
            return 1;
        }

        var total = BigInteger.Zero;
        for (var count = minimum; count <= Math.Min(draws, successes); count++)
        {
            total += Ways(population, successes, draws, count);
        }

        return Divide(total, Binomial(population, draws));
    }

    /// <summary>
    /// Gets the probability to draw at least each number of successes, from 0 to a maximum, in one calculation.
    /// </summary>
    /// <param name="population">The number of cards, for example 99.</param>
    /// <param name="successes">The number of successes in the population, for example 38 lands.</param>
    /// <param name="draws">The number of cards to draw.</param>
    /// <param name="maximum">The highest number of successes in the result.</param>
    /// <returns>The probabilities. The item at index N is the probability to draw at least N successes.</returns>
    public static IReadOnlyList<double> AtLeastEach(int population, int successes, int draws, int maximum)
    {
        Validate(population, successes, draws);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var result = new double[maximum + 1];
        var total = Binomial(population, draws);
        var tail = BigInteger.Zero;
        for (var count = Math.Min(draws, successes); count >= 0; count--)
        {
            tail += Ways(population, successes, draws, count);
            if (count <= maximum)
            {
                result[count] = Divide(tail, total);
            }
        }

        result[0] = 1;
        return result;
    }

    /// <summary>
    /// Gets the number of combinations of <paramref name="k"/> items from <paramref name="n"/> items.
    /// </summary>
    /// <param name="n">The number of items.</param>
    /// <param name="k">The number of items to choose.</param>
    /// <returns>The binomial coefficient, or 0 when <paramref name="k"/> is out of the range from 0 to <paramref name="n"/>.</returns>
    public static BigInteger Binomial(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return BigInteger.Zero;
        }

        k = Math.Min(k, n - k);
        var result = BigInteger.One;
        for (var index = 0; index < k; index++)
        {
            // The product of i + 1 consecutive integers is a multiple of (i + 1)!, so the division has no remainder.
            result = result * (n - index) / (index + 1);
        }

        return result;
    }

    private static BigInteger Ways(int population, int successes, int draws, int count) =>
        Binomial(successes, count) * Binomial(population - successes, draws - count);

    private static double Divide(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero)
        {
            return 0;
        }

        var limit = new BigInteger(double.MaxValue);
        return numerator < limit && denominator < limit
            ? (double)numerator / (double)denominator
            : Math.Exp(BigInteger.Log(numerator) - BigInteger.Log(denominator));
    }

    private static void Validate(int population, int successes, int draws)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(population);
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfNegative(draws);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, population);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(draws, population);
    }
}
