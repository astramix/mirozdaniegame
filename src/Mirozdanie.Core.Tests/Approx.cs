using Xunit;

namespace Mirozdanie.Core.Tests;

public static class Approx
{
    public static void Equal(double expected, double actual, int digits)
    {
        double tol = 0.5 * Math.Pow(10, -digits);
        Assert.True(Math.Abs(expected - actual) <= tol, $"ожидалось {expected}, получено {actual} (±{tol})");
    }
}
