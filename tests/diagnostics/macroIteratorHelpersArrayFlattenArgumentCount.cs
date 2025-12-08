using Roblox;

public static class IteratorHelperArrayFlattenArgumentCount
{
    public static void Sample()
    {
        foreach (var value in TS.array_flatten(new[] { new[] { 1 }, new[] { 2 } }, new[] { new[] { 3 } }))
        {
            _ = value; // expect: [ROBLOXCS3035] TS.array_flatten expects exactly one enumerable collection of enumerable values.
        }
    }
}
