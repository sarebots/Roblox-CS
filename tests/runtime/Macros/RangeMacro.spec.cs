using System;
using System.Collections.Generic;

namespace RuntimeSpecs.Macros;

public static class RangeMacroSpec
{
    public static void ShouldIterateAscending()
    {
        var values = new List<double>();
        foreach (var value in range(0, 3))
        {
            values.Add(value);
        }

        if (values.Count != 4 || values[0] != 0 || values[3] != 3)
        {
            throw new Exception($"Expected ascending range 0-3, got {string.Join(",", values)}");
        }
    }

    public static void ShouldRespectNegativeStep()
    {
        var values = new List<double>();
        foreach (var value in range(3, 0, -1))
        {
            values.Add(value);
        }

        if (values.Count != 4 || values[0] != 3 || values[^1] != 0)
        {
            throw new Exception($"Expected descending range 3-0, got {string.Join(",", values)}");
        }
    }

    public static void ShouldSupportFractionalStep()
    {
        var values = new List<double>();
        foreach (var value in range(0, 1, 0.5))
        {
            values.Add(value);
        }

        if (values.Count != 3 || values[1] != 0.5)
        {
            throw new Exception($"Expected fractional range 0-1 step .5, got {string.Join(",", values)}");
        }
    }
}
