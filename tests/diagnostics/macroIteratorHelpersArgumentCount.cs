using Roblox;

public static class IteratorHelperArgumentCount
{
    public static void Sample()
    {
        foreach (var value in TS.iter(new[] { 1, 2 }, new[] { 3 }))
        {
            _ = value; // expect: [ROBLOXCS3033] TS.iter expects exactly one enumerable argument.
        }
    }
}
