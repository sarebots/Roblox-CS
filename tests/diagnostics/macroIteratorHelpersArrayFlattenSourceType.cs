using Roblox;

public static class IteratorHelperArrayFlattenSourceType
{
    public static void Sample()
    {
        foreach (var value in TS.array_flatten(new[] { 1, 2, 3 }))
        {
            _ = value; // expect: [ROBLOXCS3036] TS.array_flatten requires an array or IEnumerable of enumerable values.
        }
    }
}
