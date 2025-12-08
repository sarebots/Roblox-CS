using System;

namespace Roblox;

public static class NumberExtensions
{
    public static int idiv(this int value, int divisor) =>
        throw new NotSupportedException("Roblox.NumberExtensions.idiv is only available during transpilation.");

    public static long idiv(this long value, long divisor) =>
        throw new NotSupportedException("Roblox.NumberExtensions.idiv is only available during transpilation.");

    public static double idiv(this double value, double divisor) =>
        throw new NotSupportedException("Roblox.NumberExtensions.idiv is only available during transpilation.");

    public static float idiv(this float value, float divisor) =>
        throw new NotSupportedException("Roblox.NumberExtensions.idiv is only available during transpilation.");

}
