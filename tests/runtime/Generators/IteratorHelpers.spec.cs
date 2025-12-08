using System.Collections.Generic;

namespace RuntimeSpecs.Generators;

public static class IteratorHelpersSpec
{
    public static void ShouldConsumeEnumeratorViaRuntime()
    {
        var iterator = Produce();
        var enumerator = iterator.GetEnumerator();

        var seen = new List<int>();
        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current);
        }

        if (seen.Count != 3 || seen[0] != 10 || seen[2] != 30)
        {
            throw new System.Exception($"Expected runtime iterator to yield 10,20,30 but got {string.Join(",", seen)}");
        }
    }

    private static IEnumerable<int> Produce()
    {
        yield return 10;
        yield return 20;
        yield return 30;
    }
}
