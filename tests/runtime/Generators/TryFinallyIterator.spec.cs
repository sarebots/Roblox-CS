using System;
using System.Collections.Generic;

namespace RuntimeSpecs.Generators;

public static class TryFinallyIteratorSpec
{
    public static void ShouldInvokeFinallyAroundYield()
    {
        var values = new List<int>();
        try
        {
            foreach (var value in Produce())
            {
                values.Add(value);
                if (value == 1)
                {
                    break;
                }
            }
        }
        finally
        {
            values.Add(99);
        }

        if (values.Count != 2 || values[0] != 1 || values[1] != 99)
        {
            throw new Exception($"Expected try/finally to capture 1 and finally marker, got {string.Join(",", values)}");
        }
    }

    private static IEnumerable<int> Produce()
    {
        try
        {
            yield return 1;
            yield return 2;
        }
        finally
        {
            // ensures Generator.close propagates when exiting early
        }
    }
}
