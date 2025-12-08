using System;
using System.Collections.Generic;

namespace Roblox;

public static class RangeHelper
{
    public static IEnumerable<double> Range(double start, double finish) =>
        throw new NotSupportedException("Roblox.RangeHelper.Range is only available during transpilation.");

    public static IEnumerable<double> Range(double start, double finish, double step) =>
        throw new NotSupportedException("Roblox.RangeHelper.Range is only available during transpilation.");
}
