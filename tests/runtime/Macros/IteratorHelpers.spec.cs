using System;
using System.Collections.Generic;

namespace RuntimeSpecs.Macros;

public static class IteratorHelpersSpec
{
    public static void ShouldIterateUsingTsIter()
    {
        var values = new List<int> { 1, 2, 3 };
        var sum = 0;
        foreach (var value in TS.iter(values))
        {
            sum += value;
        }

        if (sum != 6)
        {
            throw new Exception($"Expected TS.iter to preserve list iteration, got {sum}.");
        }
    }

    public static void ShouldFlattenNestedArrays()
    {
        var nested = new List<List<int>>
        {
            new() { 1, 2 },
            new() { 3 },
        };

        var collected = new List<int>();
        foreach (var value in TS.array_flatten(nested))
        {
            collected.Add(value);
        }

        if (collected.Count != 3 || collected[0] != 1 || collected[^1] != 3)
        {
            throw new Exception("array_flatten did not flatten nested arrays.");
        }
    }
}
