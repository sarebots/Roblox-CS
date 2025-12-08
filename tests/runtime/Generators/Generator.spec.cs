using System.Collections.Generic;

namespace RuntimeSpecs.Generators;

public static class GeneratorSpec
{
    public static void ShouldSumYieldedValues()
    {
        var sum = 0;
        foreach (var value in Sample())
        {
            sum += value;
        }

        if (sum != 3)
        {
            throw new System.Exception($"Expected sum 3, got {sum}");
        }
    }

    public static void ShouldStopAfterYieldBreak()
    {
        var seen = new List<int>();
        foreach (var value in YieldBreakSample())
        {
            seen.Add(value);
        }

        if (seen.Count != 1 || seen[0] != 1)
        {
            throw new System.Exception($"Expected to see only the first value before yield break, got {string.Join(",", seen)}");
        }
    }

    public static void ShouldEnumerateIEnumerator()
    {
        var enumerator = ProduceEnumerator();
        var values = new List<int>();

        while (enumerator.MoveNext())
        {
            values.Add(enumerator.Current);
        }

        if (values.Count != 2 || values[0] != 5 || values[1] != 6)
        {
            throw new System.Exception($"Expected IEnumerator to yield 5,6 but got {string.Join(",", values)}");
        }
    }

    private static IEnumerable<int> Sample()
    {
        yield return 1;
        yield return 2;
    }

    private static IEnumerable<int> YieldBreakSample()
    {
        yield return 1;
        yield break;
    }

    private static IEnumerator<int> ProduceEnumerator()
    {
        yield return 5;
        yield return 6;
    }
}
